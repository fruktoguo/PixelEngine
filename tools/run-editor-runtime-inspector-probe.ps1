param(
  [string]$EditorExecutable = 'apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe',

  [string]$ProjectRoot = 'demo/PixelEngine.Demo',

  [string]$OutputRoot = '',

  [ValidateRange(48, 10000)]
  [int]$WindowTicks = 48,

  [ValidateRange(1, 5)]
  [int]$MaxAttempts = 3,

  [ValidateRange(10, 1800)]
  [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/run-editor-runtime-inspector-probe.ps1 需要 PowerShell 7+。'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$editorExecutableFull = if ([IO.Path]::IsPathRooted($EditorExecutable)) {
  [IO.Path]::GetFullPath($EditorExecutable)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $EditorExecutable))
}
$projectRootFull = if ([IO.Path]::IsPathRooted($ProjectRoot)) {
  [IO.Path]::GetFullPath($ProjectRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectRoot))
}
$outputRootFull = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  [IO.Path]::GetFullPath((Join-Path $artifactsRoot "editor-runtime-inspector-probe/$(Get-Date -Format 'yyyyMMdd-HHmmss')"))
} elseif ([IO.Path]::IsPathRooted($OutputRoot)) {
  [IO.Path]::GetFullPath($OutputRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRootFull.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "OutputRoot 必须位于仓库 artifacts/ 下：$outputRootFull"
}
if (-not (Test-Path -LiteralPath $editorExecutableFull -PathType Leaf)) {
  throw "Editor executable 不存在：$editorExecutableFull"
}
if (-not (Test-Path -LiteralPath (Join-Path $projectRootFull 'project.pixelproj') -PathType Leaf)) {
  throw "PixelEngine project 不存在：$projectRootFull"
}
if (Test-Path -LiteralPath $outputRootFull) {
  throw "OutputRoot 已存在；为避免覆盖证据，请换用新目录：$outputRootFull"
}

function Invoke-ProbeProcess(
  [string]$FilePath,
  [string]$WorkingDirectory,
  [string[]]$Arguments,
  [string]$StdoutPath,
  [string]$StderrPath
) {
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $FilePath
  $startInfo.WorkingDirectory = $WorkingDirectory
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw "无法启动 Editor：$FilePath"
  }

  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try {
      $process.Kill($true)
    } catch {
      $process.Kill()
    }
    throw "Editor runtime Inspector probe 超时：${TimeoutSeconds}s"
  }

  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  Set-Content -LiteralPath $StdoutPath -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $StderrPath -Value $stderr -Encoding UTF8
  if ($process.ExitCode -ne 0) {
    throw "Editor runtime Inspector probe 退出码 $($process.ExitCode)：$stderr"
  }

  return [pscustomobject]@{
    Stdout = $stdout
    Stderr = $stderr
  }
}

function Get-SummaryValues([string]$Text, [string]$Prefix) {
  $line = @($Text -split "`r?`n" | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) }) | Select-Object -Last 1
  if (-not $line) {
    throw "缺少 runtime Inspector 摘要：$Prefix"
  }

  $values = [ordered]@{}
  foreach ($part in $line.Substring($Prefix.Length).Split(',')) {
    $token = $part.Trim()
    $separator = $token.IndexOf('=')
    if ($separator -le 0) {
      continue
    }
    $values[$token.Substring(0, $separator)] = $token.Substring($separator + 1)
  }

  return [pscustomobject]@{
    Line = $line
    Values = $values
  }
}

function Assert-SummaryValue(
  [Collections.IDictionary]$Values,
  [string]$Key,
  [string]$Expected
) {
  if (-not $Values.Contains($Key) -or -not [string]::Equals([string]$Values[$Key], $Expected, [StringComparison]::Ordinal)) {
    $actual = if ($Values.Contains($Key)) { [string]$Values[$Key] } else { '<missing>' }
    throw "runtime Inspector 摘要字段不匹配：$Key expected=$Expected actual=$actual"
  }
}

function Assert-PositiveSummaryInteger(
  [Collections.IDictionary]$Values,
  [string]$Key
) {
  if (-not $Values.Contains($Key)) {
    throw "runtime Inspector 摘要缺少字段：$Key"
  }

  $value = 0L
  if (-not [long]::TryParse(
      [string]$Values[$Key],
      [Globalization.NumberStyles]::Integer,
      [Globalization.CultureInfo]::InvariantCulture,
      [ref]$value) -or $value -le 0) {
    throw "runtime Inspector 摘要字段必须为正整数：$Key=$($Values[$Key])"
  }
}

function Get-BmpEvidence([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Editor framebuffer 不存在：$Path"
  }

  $bytes = [IO.File]::ReadAllBytes($Path)
  if ($bytes.Length -lt 54 -or $bytes[0] -ne 0x42 -or $bytes[1] -ne 0x4D) {
    throw "Editor framebuffer 不是有效 BMP：$Path"
  }

  $pixelOffset = [BitConverter]::ToInt32($bytes, 10)
  $width = [BitConverter]::ToInt32($bytes, 18)
  $signedHeight = [BitConverter]::ToInt32($bytes, 22)
  $height = [Math]::Abs($signedHeight)
  $bitsPerPixel = [BitConverter]::ToUInt16($bytes, 28)
  if ($width -le 0 -or $height -le 0 -or $bitsPerPixel -ne 32 -or $pixelOffset -lt 54) {
    throw "Editor framebuffer geometry 非法：${width}x${height}x${bitsPerPixel}"
  }

  function Get-RegionMetrics(
    [int]$X0,
    [int]$Y0,
    [int]$X1,
    [int]$Y1,
    [int]$Step
  ) {
    $regionUnique = [Collections.Generic.HashSet[uint32]]::new()
    $sampleCount = 0
    $nearBlackCount = 0
    for ($y = $Y0; $y -lt $Y1; $y += $Step) {
      $sourceY = if ($signedHeight -gt 0) { $height - 1 - $y } else { $y }
      for ($x = $X0; $x -lt $X1; $x += $Step) {
        $offset = $pixelOffset + (($sourceY * $width + $x) * 4)
        if ($offset + 4 -gt $bytes.Length) {
          throw "Editor framebuffer region 越界：x=$x y=$y"
        }

        $color = [BitConverter]::ToUInt32($bytes, $offset)
        [void]$regionUnique.Add($color)
        $sampleCount++
        if ($bytes[$offset] -le 16 -and $bytes[$offset + 1] -le 16 -and $bytes[$offset + 2] -le 16) {
          $nearBlackCount++
        }
      }
    }

    return [pscustomobject]@{
      UniqueColors = $regionUnique.Count
      NearBlackRatio = if ($sampleCount -eq 0) { 1.0 } else { $nearBlackCount / [double]$sampleCount }
    }
  }

  $pixelCount = $width * $height
  $sampleStride = [Math]::Max(1, [int][Math]::Floor($pixelCount / 4096.0))
  $unique = [Collections.Generic.HashSet[uint32]]::new()
  for ($pixel = 0; $pixel -lt $pixelCount; $pixel += $sampleStride) {
    $offset = $pixelOffset + ($pixel * 4)
    if ($offset + 4 -gt $bytes.Length) {
      break
    }
    [void]$unique.Add([BitConverter]::ToUInt32($bytes, $offset))
  }

  $chrome = Get-RegionMetrics 0 0 $width ([Math]::Min(125, $height)) 3
  $rightSurface = Get-RegionMetrics ([int][Math]::Floor($width * 0.52)) 60 $width ([Math]::Max(61, $height - 25)) 3
  $inspectorSurface = Get-RegionMetrics ([int][Math]::Floor($width * 0.77)) 120 $width ([Math]::Max(121, $height - 25)) 2
  if ($unique.Count -lt 16) {
    throw "Editor framebuffer 可见颜色不足，疑似空白或纯色：unique=$($unique.Count)"
  }
  if ($chrome.UniqueColors -lt 96 -or $chrome.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer 顶部 chrome 不完整：unique=$($chrome.UniqueColors), nearBlack=$($chrome.NearBlackRatio)"
  }
  if ($rightSurface.UniqueColors -lt 64 -or $rightSurface.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer 右侧 surface 不完整：unique=$($rightSurface.UniqueColors), nearBlack=$($rightSurface.NearBlackRatio)"
  }
  if ($inspectorSurface.UniqueColors -lt 48 -or $inspectorSurface.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer Inspector surface 不完整：unique=$($inspectorSurface.UniqueColors), nearBlack=$($inspectorSurface.NearBlackRatio)"
  }

  return [ordered]@{
    path = [IO.Path]::GetRelativePath($outputRootFull, $Path).Replace('\', '/')
    width = $width
    height = $height
    bitsPerPixel = $bitsPerPixel
    sampledUniqueColors = $unique.Count
    chromeSampledUniqueColors = $chrome.UniqueColors
    chromeNearBlackRatio = $chrome.NearBlackRatio
    rightSampledUniqueColors = $rightSurface.UniqueColors
    rightNearBlackRatio = $rightSurface.NearBlackRatio
    inspectorSampledUniqueColors = $inspectorSurface.UniqueColors
    inspectorNearBlackRatio = $inspectorSurface.NearBlackRatio
    sizeBytes = $bytes.Length
    sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}

function Write-IsolatedWorkspace([string]$StateRoot) {
  New-Item -ItemType Directory -Path $StateRoot | Out-Null
  $workspace = [ordered]@{
    formatVersion = 2
    lastCleanShutdown = $true
    lastSuccessfulProjectPath = $projectRootFull
    window = [ordered]@{ width = 1024; height = 720 }
    projects = @(
      [ordered]@{
        projectPath = $projectRootFull
        lastScenePath = 'scenes/lava-mine.scene'
        lastOpenedUtc = [DateTimeOffset]::UtcNow.ToString('O')
      }
    )
  }
  $workspace | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $StateRoot 'editor-workspace.json') -Encoding UTF8
}

New-Item -ItemType Directory -Path $outputRootFull | Out-Null
$attemptResults = [Collections.Generic.List[object]]::new()
$acceptedAttempt = $null
for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
  Write-Host "[editor-runtime-inspector] attempt $attempt/$MaxAttempts"
  $attemptRoot = Join-Path $outputRootFull "attempt-$attempt"
  $stateRoot = Join-Path $attemptRoot 'user-state'
  $logRoot = Join-Path $attemptRoot 'runtime-logs'
  New-Item -ItemType Directory -Path $attemptRoot | Out-Null
  New-Item -ItemType Directory -Path $logRoot | Out-Null
  Write-IsolatedWorkspace $stateRoot

  $capturePath = Join-Path $attemptRoot 'framebuffer.bmp'
  $stdoutPath = Join-Path $attemptRoot 'stdout.log'
  $stderrPath = Join-Path $attemptRoot 'stderr.log'
  try {
    $processResult = Invoke-ProbeProcess `
      -FilePath $editorExecutableFull `
      -WorkingDirectory $repoRoot `
      -Arguments @(
        '--user-data-dir', $stateRoot,
        '--no-reopen-last-project',
        '--project', $projectRootFull,
        '--scene', 'scenes/lava-mine.scene',
        '--window-ticks', $WindowTicks.ToString([Globalization.CultureInfo]::InvariantCulture),
        '--scripted-runtime-inspector-probe',
        '--capture-frame', $capturePath,
        '--log-directory', $logRoot
      ) `
      -StdoutPath $stdoutPath `
      -StderrPath $stderrPath

    $summary = Get-SummaryValues $processResult.Stdout 'editor_runtime_inspector_probe '
    Assert-SummaryValue $summary.Values 'schema' 'pixelengine.editor-runtime-inspector-probe/v1'
    Assert-SummaryValue $summary.Values 'completed' 'True'
    Assert-SummaryValue $summary.Values 'play_entered' 'True'
    Assert-SummaryValue $summary.Values 'remained_in_play' 'True'
    Assert-SummaryValue $summary.Values 'entity_selected' 'True'
    Assert-SummaryValue $summary.Values 'entity_resolved' 'True'
    Assert-SummaryValue $summary.Values 'transform_table_rendered' 'True'
    Assert-PositiveSummaryInteger $summary.Values 'component_headers'
    Assert-PositiveSummaryInteger $summary.Values 'component_property_tables'
    Assert-PositiveSummaryInteger $summary.Values 'component_numeric_drag_fields'
    Assert-PositiveSummaryInteger $summary.Values 'render_revision'
    $framebuffer = Get-BmpEvidence $capturePath

    $acceptedAttempt = [ordered]@{
      attempt = $attempt
      summaryLine = $summary.Line
      summary = $summary.Values
      framebuffer = $framebuffer
      stdout = [IO.Path]::GetRelativePath($outputRootFull, $stdoutPath).Replace('\', '/')
      stderr = [IO.Path]::GetRelativePath($outputRootFull, $stderrPath).Replace('\', '/')
    }
    $attemptResults.Add([ordered]@{ attempt = $attempt; accepted = $true; diagnostic = 'accepted' })
    break
  } catch {
    $attemptResults.Add([ordered]@{ attempt = $attempt; accepted = $false; diagnostic = $_.Exception.Message })
    Write-Warning "attempt $attempt 未通过：$($_.Exception.Message)"
  }
}

$gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitCommit)) {
  throw '无法读取 git HEAD。'
}

$report = [ordered]@{
  schema = 'pixelengine.editor-runtime-inspector-evidence/v1'
  capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  gitCommit = $gitCommit
  editorExecutable = $editorExecutableFull
  projectRoot = $projectRootFull
  windowTicks = $WindowTicks
  maxAttempts = $MaxAttempts
  allPassed = $null -ne $acceptedAttempt
  attempts = $attemptResults
  accepted = $acceptedAttempt
}
$reportPath = Join-Path $outputRootFull 'report.json'
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding UTF8

if ($null -eq $acceptedAttempt) {
  throw "Editor runtime Inspector probe 在 $MaxAttempts 次隔离运行中均未通过；报告：$reportPath"
}

Write-Host "Editor runtime Inspector probe 通过：$reportPath"
$report | ConvertTo-Json -Depth 16
