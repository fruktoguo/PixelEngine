param(
  [ValidateSet('win-x64', 'win-arm64')]
  [string]$Rid = 'win-x64',

  [ValidateSet('r2r', 'aot')]
  [string]$DemoChannel = 'r2r',

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [string]$OutputRoot = '最终输出',

  [int]$EditorProbeTimeoutSeconds = 900,

  [int]$DemoProbeTimeoutSeconds = 180,

  [int]$DemoWindowTicks = 80,

  [ValidateSet('ManagedFallback', 'RmlUi', 'Ultralight')]
  [string]$DemoRuntimeUiBackend = 'RmlUi'
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/update-final-output.ps1 需要 PowerShell 7+。请使用 pwsh -NoProfile -File tools/update-final-output.ps1。'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootFull = if ([IO.Path]::IsPathRooted($OutputRoot)) {
  [IO.Path]::GetFullPath($OutputRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

function Assert-UnderRepo([string]$Path, [string]$Label) {
  $full = [IO.Path]::GetFullPath($Path)
  $root = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label 必须位于仓库目录内：$full"
  }
}

Assert-UnderRepo $outputRootFull '正式输出目录'

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stagingRoot = Join-Path $repoRoot "artifacts/final-output-staging/$timestamp"
$editorPublish = Join-Path $stagingRoot 'editor-publish'
$editorBuildOutput = Join-Path $stagingRoot 'editor-probe-build'
$demoBuildOutput = Join-Path $stagingRoot 'demo-build'
$validationRoot = Join-Path $stagingRoot 'validation'
$nextRoot = Join-Path $stagingRoot 'next-final-output'
$logRoot = Join-Path $validationRoot 'logs'

New-Item -ItemType Directory -Force -Path $editorPublish, $editorBuildOutput, $demoBuildOutput, $validationRoot, $logRoot | Out-Null

function Get-LogTail([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return ''
  }

  return (Get-Content -LiteralPath $Path -Tail 80 -Raw)
}

function Invoke-ProcessChecked(
  [string]$Name,
  [string]$FilePath,
  [string[]]$Arguments,
  [string]$WorkingDirectory,
  [string]$StdoutPath,
  [string]$StderrPath,
  [int]$TimeoutSeconds = 0
) {
  Write-Host "[$Name] $FilePath $($Arguments -join ' ')"
  $psi = [Diagnostics.ProcessStartInfo]::new()
  $psi.FileName = $FilePath
  $psi.WorkingDirectory = $WorkingDirectory
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  foreach ($argument in $Arguments) {
    $psi.ArgumentList.Add($argument)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $psi
  if (-not $process.Start()) {
    throw "无法启动进程：$FilePath"
  }

  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $completed = if ($TimeoutSeconds -gt 0) {
    $process.WaitForExit($TimeoutSeconds * 1000)
  } else {
    $process.WaitForExit()
    $true
  }

  if (-not $completed) {
    try {
      $process.Kill($true)
    } catch {
      $process.Kill()
    }

    throw "$Name 超时：${TimeoutSeconds}s"
  }

  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  Set-Content -LiteralPath $StdoutPath -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $StderrPath -Value $stderr -Encoding UTF8

  if ($process.ExitCode -ne 0) {
    $tail = (Get-LogTail $StdoutPath) + "`n" + (Get-LogTail $StderrPath)
    throw "$Name 失败，退出码 $($process.ExitCode)。日志尾部：`n$tail"
  }

  [pscustomobject]@{
    ExitCode = $process.ExitCode
    Stdout = $stdout
    Stderr = $stderr
    StdoutPath = $StdoutPath
    StderrPath = $StderrPath
  }
}

function Test-SummaryValue([string]$Text, [string]$Prefix, [string]$Key, [string]$Expected) {
  $line = ($Text -split "`r?`n" | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) } | Select-Object -Last 1)
  if (-not $line) {
    return $false
  }

  return $line.Contains("$Key=$Expected", [StringComparison]::Ordinal)
}

function Copy-Directory([string]$Source, [string]$Destination) {
  if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "目录不存在：$Source"
  }

  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

function Write-FinalOutputChecksums([string]$Root, [string]$OutputPath) {
  $rootFull = [IO.Path]::GetFullPath($Root)
  $outputFull = [IO.Path]::GetFullPath($OutputPath)
  $lines = [Collections.Generic.List[string]]::new()
  Get-ChildItem -LiteralPath $rootFull -File -Recurse -Force |
    Sort-Object FullName |
    ForEach-Object {
      $fileFull = [IO.Path]::GetFullPath($_.FullName)
      if ([string]::Equals($fileFull, $outputFull, [StringComparison]::OrdinalIgnoreCase)) {
        return
      }

      $relative = [IO.Path]::GetRelativePath($rootFull, $fileFull).Replace('\', '/')
      $hash = (Get-FileHash -LiteralPath $fileFull -Algorithm SHA256).Hash.ToLowerInvariant()
      $lines.Add("$hash  $relative")
    }

  if ($lines.Count -eq 0) {
    throw "正式输出 checksum 清单为空：$rootFull"
  }

  Set-Content -LiteralPath $outputFull -Value $lines -Encoding UTF8
}

function Replace-FinalOutput([string]$SourceRoot, [string]$DestinationRoot) {
  Assert-UnderRepo $SourceRoot '待发布目录'
  Assert-UnderRepo $DestinationRoot '正式输出目录'

  $newRoot = "$DestinationRoot.__new"
  $backupRoot = "$DestinationRoot.__previous"
  Assert-UnderRepo $newRoot '正式输出临时目录'
  Assert-UnderRepo $backupRoot '正式输出备份目录'

  Remove-Item -LiteralPath $newRoot -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
  Move-Item -LiteralPath $SourceRoot -Destination $newRoot

  try {
    if (Test-Path -LiteralPath $DestinationRoot) {
      Move-Item -LiteralPath $DestinationRoot -Destination $backupRoot
    }

    Move-Item -LiteralPath $newRoot -Destination $DestinationRoot
    Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  catch {
    if ((Test-Path -LiteralPath $backupRoot) -and -not (Test-Path -LiteralPath $DestinationRoot)) {
      Move-Item -LiteralPath $backupRoot -Destination $DestinationRoot
    }

    throw
  }
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()

$editorProject = Join-Path $repoRoot 'apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj'
$demoBuildScript = Join-Path $repoRoot 'tools/build-player.ps1'
$editorExe = Join-Path $editorPublish 'PixelEngine.Editor.Shell.exe'

$editorPublishResult = Invoke-ProcessChecked `
  -Name 'editor-publish' `
  -FilePath $dotnet `
  -Arguments @('publish', $editorProject, '-c', $Configuration, '-r', $Rid, '--self-contained', 'false', '-o', $editorPublish) `
  -WorkingDirectory $repoRoot `
  -StdoutPath (Join-Path $logRoot 'editor-publish.stdout.log') `
  -StderrPath (Join-Path $logRoot 'editor-publish.stderr.log')

if (-not (Test-Path -LiteralPath $editorExe -PathType Leaf)) {
  throw "编辑器发布后缺少入口：$editorExe"
}

$editorProbeCapture = Join-Path $validationRoot 'editor-default-workbench.bmp'
$editorProbeResult = Invoke-ProcessChecked `
  -Name 'editor-default-workbench-probe' `
  -FilePath $editorExe `
  -Arguments @('--scripted-default-workbench-probe', '--build-output', $editorBuildOutput, '--capture-frame', $editorProbeCapture) `
  -WorkingDirectory $repoRoot `
  -StdoutPath (Join-Path $logRoot 'editor-default-workbench.stdout.log') `
  -StderrPath (Join-Path $logRoot 'editor-default-workbench.stderr.log') `
  -TimeoutSeconds $EditorProbeTimeoutSeconds

$editorProbeOk =
  (Test-SummaryValue $editorProbeResult.Stdout 'editor_default_workbench_probe ' 'completed' 'True') -and
  (Test-SummaryValue $editorProbeResult.Stdout 'editor_default_workbench_probe ' 'succeeded' 'True') -and
  (Test-SummaryValue $editorProbeResult.Stdout 'editor_default_workbench_probe ' 'build_completed' 'True') -and
  (Test-SummaryValue $editorProbeResult.Stdout 'editor_default_workbench_probe ' 'build_ok' 'True')

if (-not $editorProbeOk) {
  throw "编辑器默认工作台验证未通过。日志：$($editorProbeResult.StdoutPath)"
}

$demoBuildResult = Invoke-ProcessChecked `
  -Name 'demo-build-player' `
  -FilePath $pwsh `
  -Arguments @(
    '-NoProfile',
    '-File', $demoBuildScript,
    '-Rid', $Rid,
    '-Channel', $DemoChannel,
    '-Configuration', $Configuration,
    '-Output', $demoBuildOutput,
    '-ProductName', 'PixelEngine Demo',
    '-StartScene', 'scenes/lava-mine.scene',
    '-WindowWidth', '1080',
    '-WindowHeight', '720',
    '-VSync', 'true',
    '-RuntimeUiBackend', $DemoRuntimeUiBackend,
    '-ReleaseChannel', 'Production'
  ) `
  -WorkingDirectory $repoRoot `
  -StdoutPath (Join-Path $logRoot 'demo-build-player.stdout.log') `
  -StderrPath (Join-Path $logRoot 'demo-build-player.stderr.log')

$demoBuildResultPath = Join-Path $demoBuildOutput 'build-result.json'
if (-not (Test-Path -LiteralPath $demoBuildResultPath -PathType Leaf)) {
  throw "Demo 构建缺少 build-result.json：$demoBuildResultPath"
}

$demoBuildJson = Get-Content -Raw -LiteralPath $demoBuildResultPath | ConvertFrom-Json
if (-not $demoBuildJson.ok) {
  throw "Demo build-player 返回失败：$($demoBuildJson.error)"
}

$demoPlayerDir = [string]$demoBuildJson.playerDir
$demoExe = [string]$demoBuildJson.launcherExe
if (-not (Test-Path -LiteralPath $demoExe -PathType Leaf)) {
  throw "Demo 构建后缺少入口：$demoExe"
}

$demoProbeCapture = Join-Path $validationRoot 'demo-window.bmp'
$demoProbeResult = Invoke-ProcessChecked `
  -Name 'demo-window-probe' `
  -FilePath $demoExe `
  -Arguments @('--no-hot-reload', '--window-ticks', $DemoWindowTicks.ToString([Globalization.CultureInfo]::InvariantCulture), '--capture-frame', $demoProbeCapture) `
  -WorkingDirectory $demoPlayerDir `
  -StdoutPath (Join-Path $logRoot 'demo-window.stdout.log') `
  -StderrPath (Join-Path $logRoot 'demo-window.stderr.log') `
  -TimeoutSeconds $DemoProbeTimeoutSeconds

$demoProbeOk =
  $demoProbeResult.Stdout.Contains('window_frame_probe', [StringComparison]::Ordinal) -and
  $demoProbeResult.Stdout.Contains('PixelEngine.Demo', [StringComparison]::Ordinal) -and
  (Test-Path -LiteralPath $demoProbeCapture -PathType Leaf)

if (-not $demoProbeOk) {
  throw "Demo 窗口验证未通过。日志：$($demoProbeResult.StdoutPath)"
}

$finalEditorDir = Join-Path $nextRoot '编辑器'
$finalDemoDir = Join-Path $nextRoot '游戏Demo'
$finalValidationDir = Join-Path $nextRoot '_验证记录'
New-Item -ItemType Directory -Force -Path $finalEditorDir, $finalDemoDir, $finalValidationDir | Out-Null
Copy-Directory $editorPublish $finalEditorDir
Copy-Directory $demoPlayerDir $finalDemoDir
Copy-Directory $validationRoot $finalValidationDir

$manifest = [ordered]@{
  schema = 'pixelengine.final-output/v1'
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  gitCommit = $gitCommit
  rid = $Rid
  configuration = $Configuration
  demoChannel = $DemoChannel
  demoRuntimeUiBackendRequested = $DemoRuntimeUiBackend
  editorExecutable = '编辑器/PixelEngine.Editor.Shell.exe'
  demoExecutable = '游戏Demo/PixelEngine Demo.exe'
  updatePolicy = 'staged-build-and-verify-before-replace'
  checksumFile = 'SHA256SUMS'
  validation = [ordered]@{
    editorDefaultWorkbenchProbe = [ordered]@{
      completed = $true
      succeeded = $true
      buildOk = $true
      stdout = '_验证记录/logs/editor-default-workbench.stdout.log'
      stderr = '_验证记录/logs/editor-default-workbench.stderr.log'
      capture = '_验证记录/editor-default-workbench.bmp'
    }
    demoWindowProbe = [ordered]@{
      completed = $true
      windowTicks = $DemoWindowTicks
      stdout = '_验证记录/logs/demo-window.stdout.log'
      stderr = '_验证记录/logs/demo-window.stderr.log'
      capture = '_验证记录/demo-window.bmp'
    }
    demoBuildResult = '_验证记录/demo-build-result.json'
  }
}

Copy-Item -LiteralPath $demoBuildResultPath -Destination (Join-Path $finalValidationDir 'demo-build-result.json') -Force
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $finalValidationDir 'manifest.json') -Encoding UTF8

@"
PixelEngine 正式输出

此目录只由 tools/update-final-output.ps1 更新。脚本会先在 artifacts/final-output-staging 下构建与验证，编辑器默认工作台和游戏 Demo 窗口验证全部通过后，才替换本目录。

- 编辑器：编辑器\PixelEngine.Editor.Shell.exe
- 游戏 Demo：游戏Demo\PixelEngine Demo.exe
- 验证记录：_验证记录\manifest.json
- 完整性校验：SHA256SUMS
"@ | Set-Content -LiteralPath (Join-Path $nextRoot 'README.txt') -Encoding UTF8

Write-FinalOutputChecksums $nextRoot (Join-Path $nextRoot 'SHA256SUMS')

Replace-FinalOutput $nextRoot $outputRootFull

Write-Host "正式输出已更新：$outputRootFull"
Write-Host "编辑器入口：$(Join-Path $outputRootFull '编辑器/PixelEngine.Editor.Shell.exe')"
Write-Host "Demo 入口：$(Join-Path $outputRootFull '游戏Demo/PixelEngine Demo.exe')"
Write-Host "验证 manifest：$(Join-Path $outputRootFull '_验证记录/manifest.json')"
Write-Host "完整性校验：$(Join-Path $outputRootFull 'SHA256SUMS')"
