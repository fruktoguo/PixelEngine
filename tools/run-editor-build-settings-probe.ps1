param(
  [string]$EditorExecutable = 'apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe',

  [string]$ProjectRoot = 'demo/PixelEngine.Demo',

  [string]$OutputRoot = '',

  [ValidateRange(1, 5)]
  [int]$MaxAttempts = 3,

  [ValidateRange(10, 1800)]
  [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/run-editor-build-settings-probe.ps1 需要 PowerShell 7+。'
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
  [IO.Path]::GetFullPath((Join-Path $artifactsRoot "editor-build-settings-probe/$(Get-Date -Format 'yyyyMMdd-HHmmss')"))
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

$projectRelative = [IO.Path]::GetRelativePath($repoRoot, $projectRootFull).Replace('\', '/')
if ($projectRelative.StartsWith('../', [StringComparison]::Ordinal) -or [IO.Path]::IsPathRooted($projectRelative)) {
  throw "ProjectRoot 必须位于仓库内，才能从 git tracked 文件创建隔离副本：$projectRootFull"
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
    throw "Editor Build Settings probe 超时：${TimeoutSeconds}s"
  }

  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  Set-Content -LiteralPath $StdoutPath -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $StderrPath -Value $stderr -Encoding UTF8
  if ($process.ExitCode -ne 0) {
    throw "Editor Build Settings probe 退出码 $($process.ExitCode)：$stderr"
  }

  return [pscustomobject]@{
    Stdout = $stdout
    Stderr = $stderr
  }
}

function Get-SummaryValues([string]$Text, [string]$Prefix) {
  $line = @($Text -split "`r?`n" | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) }) | Select-Object -Last 1
  if (-not $line) {
    throw "缺少 Build Settings 摘要：$Prefix"
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
    throw "Build Settings 摘要字段不匹配：$Key expected=$Expected actual=$actual"
  }
}

function Assert-MinimumSummaryInteger(
  [Collections.IDictionary]$Values,
  [string]$Key,
  [long]$Minimum
) {
  if (-not $Values.Contains($Key)) {
    throw "Build Settings 摘要缺少字段：$Key"
  }

  $value = 0L
  if (-not [long]::TryParse(
      [string]$Values[$Key],
      [Globalization.NumberStyles]::Integer,
      [Globalization.CultureInfo]::InvariantCulture,
      [ref]$value) -or $value -lt $Minimum) {
    throw "Build Settings 摘要字段必须 >= ${Minimum}：$Key=$($Values[$Key])"
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
    $opaqueCount = 0
    for ($y = $Y0; $y -lt $Y1; $y += $Step) {
      $sourceY = if ($signedHeight -gt 0) { $height - 1 - $y } else { $y }
      for ($x = $X0; $x -lt $X1; $x += $Step) {
        $offset = $pixelOffset + (($sourceY * $width + $x) * 4)
        if ($offset + 4 -gt $bytes.Length) {
          throw "Editor framebuffer region 越界：x=$x y=$y"
        }

        [void]$regionUnique.Add([BitConverter]::ToUInt32($bytes, $offset))
        $sampleCount++
        if ($bytes[$offset] -le 16 -and $bytes[$offset + 1] -le 16 -and $bytes[$offset + 2] -le 16) {
          $nearBlackCount++
        }
        if ($bytes[$offset + 3] -ge 250) {
          $opaqueCount++
        }
      }
    }

    return [pscustomobject]@{
      UniqueColors = $regionUnique.Count
      NearBlackRatio = if ($sampleCount -eq 0) { 1.0 } else { $nearBlackCount / [double]$sampleCount }
      OpaqueRatio = if ($sampleCount -eq 0) { 0.0 } else { $opaqueCount / [double]$sampleCount }
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
  $sceneSurface = Get-RegionMetrics 0 60 ([int][Math]::Floor($width * 0.52)) ([Math]::Max(61, $height - 25)) 3
  $buildSettingsSurface = Get-RegionMetrics `
    ([int][Math]::Floor($width * 0.52)) `
    ([int][Math]::Floor($height * 0.54)) `
    ([int][Math]::Floor($width * 0.75)) `
    ([Math]::Max(1, $height - 20)) `
    2
  $rightSurface = Get-RegionMetrics ([int][Math]::Floor($width * 0.75)) 60 $width ([Math]::Max(61, $height - 25)) 3

  $regions = [ordered]@{
    chrome = $chrome
    scene = $sceneSurface
    buildSettings = $buildSettingsSurface
    right = $rightSurface
  }
  foreach ($entry in $regions.GetEnumerator()) {
    if ($entry.Value.OpaqueRatio -lt 0.99) {
      throw "Editor framebuffer $($entry.Key) alpha 不完整：opaque=$($entry.Value.OpaqueRatio)"
    }
  }
  if ($unique.Count -lt 32) {
    throw "Editor framebuffer 可见颜色不足，疑似空白或纯色：unique=$($unique.Count)"
  }
  if ($chrome.UniqueColors -lt 96 -or $chrome.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer 顶部 chrome 不完整：unique=$($chrome.UniqueColors), nearBlack=$($chrome.NearBlackRatio)"
  }
  if ($sceneSurface.UniqueColors -lt 96 -or $sceneSurface.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer Scene surface 不完整：unique=$($sceneSurface.UniqueColors), nearBlack=$($sceneSurface.NearBlackRatio)"
  }
  if ($buildSettingsSurface.UniqueColors -lt 64 -or $buildSettingsSurface.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer Build Settings surface 不完整：unique=$($buildSettingsSurface.UniqueColors), nearBlack=$($buildSettingsSurface.NearBlackRatio)"
  }
  if ($rightSurface.UniqueColors -lt 64 -or $rightSurface.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer 右侧 surface 不完整：unique=$($rightSurface.UniqueColors), nearBlack=$($rightSurface.NearBlackRatio)"
  }

  return [ordered]@{
    path = [IO.Path]::GetRelativePath($outputRootFull, $Path).Replace('\', '/')
    width = $width
    height = $height
    bitsPerPixel = $bitsPerPixel
    sampledUniqueColors = $unique.Count
    chromeSampledUniqueColors = $chrome.UniqueColors
    chromeNearBlackRatio = $chrome.NearBlackRatio
    chromeOpaqueRatio = $chrome.OpaqueRatio
    sceneSampledUniqueColors = $sceneSurface.UniqueColors
    sceneNearBlackRatio = $sceneSurface.NearBlackRatio
    sceneOpaqueRatio = $sceneSurface.OpaqueRatio
    buildSettingsSampledUniqueColors = $buildSettingsSurface.UniqueColors
    buildSettingsNearBlackRatio = $buildSettingsSurface.NearBlackRatio
    buildSettingsOpaqueRatio = $buildSettingsSurface.OpaqueRatio
    rightSampledUniqueColors = $rightSurface.UniqueColors
    rightNearBlackRatio = $rightSurface.NearBlackRatio
    rightOpaqueRatio = $rightSurface.OpaqueRatio
    sizeBytes = $bytes.Length
    sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}

function Copy-TrackedProject([string]$Destination) {
  New-Item -ItemType Directory -Path $Destination | Out-Null
  $trackedFiles = @(& git -C $repoRoot ls-files -- $projectRelative)
  if ($LASTEXITCODE -ne 0 -or $trackedFiles.Count -eq 0) {
    throw "无法枚举 ProjectRoot 的 git tracked 文件：$projectRelative"
  }

  foreach ($repoRelativePath in $trackedFiles) {
    $sourcePath = Join-Path $repoRoot $repoRelativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
      continue
    }

    $projectFileRelative = [IO.Path]::GetRelativePath($projectRootFull, $sourcePath)
    if ($projectFileRelative.StartsWith('..', [StringComparison]::Ordinal)) {
      continue
    }

    $destinationPath = Join-Path $Destination $projectFileRelative
    $destinationDirectory = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
      New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
  }

  if (-not (Test-Path -LiteralPath (Join-Path $Destination 'project.pixelproj') -PathType Leaf)) {
    throw "隔离工程缺少 project.pixelproj：$Destination"
  }
}

function Write-IsolatedWorkspace([string]$StateRoot, [string]$AttemptProjectRoot) {
  New-Item -ItemType Directory -Path $StateRoot | Out-Null
  $workspace = [ordered]@{
    formatVersion = 2
    lastCleanShutdown = $true
    lastSuccessfulProjectPath = $AttemptProjectRoot
    window = [ordered]@{ width = 1024; height = 720 }
    projects = @(
      [ordered]@{
        projectPath = $AttemptProjectRoot
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
  Write-Host "[editor-build-settings] attempt $attempt/$MaxAttempts"
  $attemptRoot = Join-Path $outputRootFull "attempt-$attempt"
  $attemptProjectRoot = Join-Path $attemptRoot 'project'
  $stateRoot = Join-Path $attemptRoot 'user-state'
  $logRoot = Join-Path $attemptRoot 'runtime-logs'
  New-Item -ItemType Directory -Path $attemptRoot | Out-Null
  New-Item -ItemType Directory -Path $logRoot | Out-Null
  Copy-TrackedProject $attemptProjectRoot
  Write-IsolatedWorkspace $stateRoot $attemptProjectRoot

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
        '--project', $attemptProjectRoot,
        '--scene', 'scenes/lava-mine.scene',
        '--scripted-build-settings-probe',
        '--build-output', (Join-Path $attemptRoot 'unused-build-output'),
        '--capture-frame', $capturePath,
        '--log-directory', $logRoot
      ) `
      -StdoutPath $stdoutPath `
      -StderrPath $stderrPath

    $summary = Get-SummaryValues $processResult.Stdout 'editor_build_settings_probe '
    Assert-SummaryValue $summary.Values 'schema' 'pixelengine.editor-build-settings-probe/v1'
    Assert-SummaryValue $summary.Values 'applied' 'True'
    Assert-SummaryValue $summary.Values 'close_requested' 'True'
    Assert-SummaryValue $summary.Values 'reopened' 'True'
    Assert-SummaryValue $summary.Values 'build_settings_focused' 'True'
    Assert-SummaryValue $summary.Values 'captured' 'True'
    Assert-SummaryValue $summary.Values 'matches' 'True'
    Assert-SummaryValue $summary.Values 'footer_actions_accessible' 'True'
    Assert-SummaryValue $summary.Values 'footer_secondary_accessible' 'True'
    Assert-MinimumSummaryInteger $summary.Values 'frames_after_focus' 20
    $footerDensity = $summary.Values['footer_density']
    switch ($footerDensity) {
      'Inline' {
        Assert-SummaryValue $summary.Values 'footer_primary_fit' 'True'
        Assert-SummaryValue $summary.Values 'footer_build_visible' 'True'
        Assert-SummaryValue $summary.Values 'footer_build_and_run_visible' 'True'
        Assert-SummaryValue $summary.Values 'footer_overflow_visible' 'False'
        Assert-SummaryValue $summary.Values 'footer_overflow_popup_open' 'False'
        Assert-SummaryValue $summary.Values 'footer_overflow_requested' 'False'
        Assert-SummaryValue $summary.Values 'frames_after_overflow_request' '0'
      }
      'Overflow' {
        Assert-SummaryValue $summary.Values 'footer_primary_fit' 'True'
        Assert-SummaryValue $summary.Values 'footer_build_visible' 'True'
        Assert-SummaryValue $summary.Values 'footer_build_and_run_visible' 'True'
        Assert-SummaryValue $summary.Values 'footer_overflow_visible' 'True'
        Assert-SummaryValue $summary.Values 'footer_overflow_popup_open' 'True'
        Assert-SummaryValue $summary.Values 'footer_overflow_requested' 'True'
        Assert-MinimumSummaryInteger $summary.Values 'frames_after_overflow_request' 1
      }
      'AllOverflow' {
        Assert-SummaryValue $summary.Values 'footer_primary_fit' 'False'
        Assert-SummaryValue $summary.Values 'footer_build_visible' 'False'
        Assert-SummaryValue $summary.Values 'footer_build_and_run_visible' 'False'
        Assert-SummaryValue $summary.Values 'footer_overflow_visible' 'True'
        Assert-SummaryValue $summary.Values 'footer_overflow_popup_open' 'True'
        Assert-SummaryValue $summary.Values 'footer_overflow_requested' 'True'
        Assert-MinimumSummaryInteger $summary.Values 'frames_after_overflow_request' 1
      }
      default {
        throw "未知 Build Settings footer density：$footerDensity"
      }
    }
    $framebuffer = Get-BmpEvidence $capturePath

    $acceptedAttempt = [ordered]@{
      attempt = $attempt
      summaryLine = $summary.Line
      summary = $summary.Values
      framebuffer = $framebuffer
      projectRoot = [IO.Path]::GetRelativePath($outputRootFull, $attemptProjectRoot).Replace('\', '/')
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
  schema = 'pixelengine.editor-build-settings-evidence/v1'
  capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  gitCommit = $gitCommit
  editorExecutable = $editorExecutableFull
  sourceProjectRoot = $projectRootFull
  minimumFramesAfterFocus = 20
  maxAttempts = $MaxAttempts
  allPassed = $null -ne $acceptedAttempt
  attempts = $attemptResults
  accepted = $acceptedAttempt
}
$reportPath = Join-Path $outputRootFull 'report.json'
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding UTF8

if ($null -eq $acceptedAttempt) {
  throw "Editor Build Settings probe 在 $MaxAttempts 次隔离运行中均未通过；报告：$reportPath"
}

Write-Host "Editor Build Settings probe 通过：$reportPath"
$report | ConvertTo-Json -Depth 16
