param(
  [string]$EditorExecutable = 'apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.Editor.Shell.exe',

  [string]$ProjectRoot = 'demo/PixelEngine.Demo',

  [string]$OutputRoot = '',

  [ValidateRange(75, 10000)]
  [int]$WindowTicks = 100,

  [ValidateRange(10, 1800)]
  [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/run-editor-gameview-presentation-probe.ps1 需要 PowerShell 7+。'
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
  [IO.Path]::GetFullPath((Join-Path $artifactsRoot "editor-gameview-presentation-probe/$(Get-Date -Format 'yyyyMMdd-HHmmss')"))
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
    throw "Editor Game View probe 超时：${TimeoutSeconds}s"
  }

  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  Set-Content -LiteralPath $StdoutPath -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $StderrPath -Value $stderr -Encoding UTF8
  if ($process.ExitCode -ne 0) {
    throw "Editor Game View probe 退出码 $($process.ExitCode)：$stderr"
  }

  return [pscustomobject]@{
    Stdout = $stdout
    Stderr = $stderr
  }
}

function Get-SummaryValues([string]$Text, [string]$Prefix, [string]$Label) {
  $line = @($Text -split "`r?`n" | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) }) | Select-Object -Last 1
  if (-not $line) {
    throw "$Label 缺少摘要：$Prefix"
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
  [string]$Expected,
  [string]$Label
) {
  if (-not $Values.Contains($Key) -or -not [string]::Equals([string]$Values[$Key], $Expected, [StringComparison]::Ordinal)) {
    $actual = if ($Values.Contains($Key)) { [string]$Values[$Key] } else { '<missing>' }
    throw "$Label 字段不匹配：$Key expected=$Expected actual=$actual"
  }
}

function Convert-PresentationSize([string]$Value, [string]$Label) {
  if ($Value -notmatch '^(?<width>\d+)x(?<height>\d+)$') {
    throw "$Label presentation 格式错误：$Value"
  }
  return [pscustomobject]@{ Width = [int]$Matches.width; Height = [int]$Matches.height }
}

function Convert-WorldContent([string]$Value, [string]$Label) {
  if ($Value -notmatch '^(?<x>-?\d+):(?<y>-?\d+):(?<width>\d+)x(?<height>\d+):(?<sourceWidth>\d+)x(?<sourceHeight>\d+):(?<targetWidth>\d+)x(?<targetHeight>\d+)$') {
    throw "$Label world_content 格式错误：$Value"
  }
  return [pscustomobject]@{
    X = [int]$Matches.x
    Y = [int]$Matches.y
    Width = [int]$Matches.width
    Height = [int]$Matches.height
    SourceWidth = [int]$Matches.sourceWidth
    SourceHeight = [int]$Matches.sourceHeight
    TargetWidth = [int]$Matches.targetWidth
    TargetHeight = [int]$Matches.targetHeight
  }
}

function Assert-PositiveProbeRect([string]$Value, [string]$Label) {
  if ($Value -notmatch '^-?\d+(?:\.\d+)?:-?\d+(?:\.\d+)?:-?\d+(?:\.\d+)?x-?\d+(?:\.\d+)?$') {
    throw "$Label rect 格式错误：$Value"
  }
  $size = $Value.Substring($Value.LastIndexOf(':') + 1).Split('x')
  if ([double]$size[0] -le 0 -or [double]$size[1] -le 0) {
    throw "$Label rect 非正尺寸：$Value"
  }
}

function Get-BmpEvidence([string]$Path, [bool]$ExpectDockChrome) {
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
  if ($unique.Count -lt 16) {
    throw "Editor framebuffer 可见颜色不足，疑似空白或纯色：unique=$($unique.Count)"
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

  $chrome = Get-RegionMetrics 0 0 $width ([Math]::Min(125, $height)) 3
  $rightSurface = Get-RegionMetrics ([int][Math]::Floor($width * 0.52)) 60 $width ([Math]::Max(61, $height - 25)) 3
  $dockSurface = Get-RegionMetrics `
    ([int][Math]::Floor($width * 0.52)) `
    60 `
    ([int][Math]::Floor($width * 0.75)) `
    ([int][Math]::Floor($height * 0.55)) `
    2
  if ($chrome.UniqueColors -lt 96 -or $chrome.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer 顶部 chrome 不完整：unique=$($chrome.UniqueColors), nearBlack=$($chrome.NearBlackRatio)"
  }
  if ($rightSurface.UniqueColors -lt 64 -or $rightSurface.NearBlackRatio -ge 0.25) {
    throw "Editor framebuffer 右侧 surface 不完整：unique=$($rightSurface.UniqueColors), nearBlack=$($rightSurface.NearBlackRatio)"
  }
  if ($ExpectDockChrome -and ($dockSurface.UniqueColors -lt 96 -or $dockSurface.NearBlackRatio -ge 0.25)) {
    throw "Editor framebuffer Hierarchy dock 不完整：unique=$($dockSurface.UniqueColors), nearBlack=$($dockSurface.NearBlackRatio)"
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
    dockSampledUniqueColors = $dockSurface.UniqueColors
    dockNearBlackRatio = $dockSurface.NearBlackRatio
    sizeBytes = $bytes.Length
    sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}

function Write-IsolatedWorkspace([string]$StateRoot, [Collections.IDictionary]$Scenario) {
  New-Item -ItemType Directory -Path $StateRoot | Out-Null
  $windowWidth = if ($Scenario.Contains('WindowWidth')) { [int]$Scenario.WindowWidth } else { 1024 }
  $windowHeight = if ($Scenario.Contains('WindowHeight')) { [int]$Scenario.WindowHeight } else { 720 }
  $workspace = [ordered]@{
    formatVersion = 2
    lastCleanShutdown = $true
    lastSuccessfulProjectPath = $projectRootFull
    window = [ordered]@{ width = $windowWidth; height = $windowHeight }
    projects = @(
      [ordered]@{
        projectPath = $projectRootFull
        lastScenePath = 'scenes/lava-mine.scene'
        lastOpenedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        gameView = [ordered]@{
          presetId = [string]$Scenario.PresetId
          scalePercent = 0
          panX = 0
          panY = 0
          maximizeOnPlay = [bool]$Scenario.MaximizeOnPlay
          customPresets = @()
        }
      }
    )
  }
  $workspace | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $StateRoot 'editor-workspace.json') -Encoding UTF8
}

New-Item -ItemType Directory -Path $outputRootFull | Out-Null
$scenarios = @(
  [ordered]@{ Name = 'aspect-16-9'; PresetId = 'aspect-16-9'; ExpectedSource = 'EditorAspectRatio'; RatioA = 16; RatioB = 9; MaximizeOnPlay = $false },
  [ordered]@{ Name = 'aspect-4-3'; PresetId = 'aspect-4-3'; ExpectedSource = 'EditorAspectRatio'; RatioA = 4; RatioB = 3; MaximizeOnPlay = $false },
  [ordered]@{ Name = 'aspect-9-16'; PresetId = 'aspect-9-16'; ExpectedSource = 'EditorAspectRatio'; RatioA = 9; RatioB = 16; MaximizeOnPlay = $false },
  [ordered]@{ Name = 'resolution-1920-1080'; PresetId = 'resolution-1920-1080'; ExpectedSource = 'EditorFixedResolution'; ExpectedWidth = 1920; ExpectedHeight = 1080; MaximizeOnPlay = $false },
  [ordered]@{ Name = 'maximize-on-play'; PresetId = 'player-default'; ExpectedSource = 'PlayerDefault'; MaximizeOnPlay = $true },
  [ordered]@{ Name = 'narrow-toolbar'; PresetId = 'aspect-16-9'; ExpectedSource = 'EditorAspectRatio'; RatioA = 16; RatioB = 9; MaximizeOnPlay = $false; WindowWidth = 360; WindowHeight = 720; ExpectedToolbarDensity = 'Narrow'; ExpectDockChrome = $false }
)
$scenarioResults = [Collections.Generic.List[object]]::new()

foreach ($scenario in $scenarios) {
  $name = [string]$scenario.Name
  Write-Host "[editor-gameview] $name"
  $scenarioRoot = Join-Path $outputRootFull $name
  $stateRoot = Join-Path $scenarioRoot 'user-state'
  $logRoot = Join-Path $scenarioRoot 'runtime-logs'
  New-Item -ItemType Directory -Path $scenarioRoot | Out-Null
  New-Item -ItemType Directory -Path $logRoot | Out-Null
  Write-IsolatedWorkspace $stateRoot $scenario

  $capturePath = Join-Path $scenarioRoot 'framebuffer.bmp'
  $stdoutPath = Join-Path $scenarioRoot 'stdout.log'
  $stderrPath = Join-Path $scenarioRoot 'stderr.log'
  $processResult = Invoke-ProbeProcess `
    -FilePath $editorExecutableFull `
    -WorkingDirectory $repoRoot `
    -Arguments @(
      '--user-data-dir', $stateRoot,
      '--no-reopen-last-project',
      '--project', $projectRootFull,
      '--scene', 'scenes/lava-mine.scene',
      '--window-ticks', $WindowTicks.ToString([Globalization.CultureInfo]::InvariantCulture),
      '--scripted-gameview-probe',
      '--capture-frame', $capturePath,
      '--log-directory', $logRoot
    ) `
    -StdoutPath $stdoutPath `
    -StderrPath $stderrPath

  $summary = Get-SummaryValues $processResult.Stdout 'editor_gameview_probe ' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'schema' 'pixelengine.editor-gameview-probe/v2' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'completed' 'True' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'first_ui_stack_depth' '1' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'first_play_exited' 'True' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'exit_ui_stack_depth' '0' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'second_play_entered' 'True' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'second_ui_stack_depth' '1' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'second_play_ui_restored' 'True' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'second_controller_faulted' 'False' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'presentation_synchronized' 'True' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'preset_id' ([string]$scenario.PresetId) "$name Game View probe"
  Assert-SummaryValue $summary.Values 'presentation_source' ([string]$scenario.ExpectedSource) "$name Game View probe"
  Assert-SummaryValue $summary.Values 'maximize_on_play' ([bool]$scenario.MaximizeOnPlay).ToString() "$name Game View probe"
  Assert-SummaryValue $summary.Values 'maximized' ([bool]$scenario.MaximizeOnPlay).ToString() "$name Game View probe"
  Assert-SummaryValue $summary.Values 'toolbar_fits' 'True' "$name Game View probe"
  Assert-SummaryValue $summary.Values 'toolbar_overflow_visible' 'True' "$name Game View probe"
  if ($scenario.Contains('ExpectedToolbarDensity')) {
    Assert-SummaryValue $summary.Values 'toolbar_density' ([string]$scenario.ExpectedToolbarDensity) "$name Game View probe"
  }
  $toolbarAvailable = [double]::Parse([string]$summary.Values['toolbar_available'], [Globalization.CultureInfo]::InvariantCulture)
  $toolbarOccupied = [double]::Parse([string]$summary.Values['toolbar_occupied'], [Globalization.CultureInfo]::InvariantCulture)
  if ($toolbarAvailable -le 0 -or $toolbarOccupied -le 0 -or $toolbarOccupied -gt $toolbarAvailable + 0.01) {
    throw "$name toolbar 越界：occupied=$toolbarOccupied available=$toolbarAvailable"
  }
  Assert-PositiveProbeRect ([string]$summary.Values['display_area']) "$name display_area"
  Assert-PositiveProbeRect ([string]$summary.Values['image_rect']) "$name image_rect"
  Assert-PositiveProbeRect ([string]$summary.Values['visible_viewport']) "$name visible_viewport"

  $presentation = Convert-PresentationSize ([string]$summary.Values['presentation']) "$name Game View probe"
  if ($scenario.Contains('ExpectedWidth')) {
    if ($presentation.Width -ne [int]$scenario.ExpectedWidth -or $presentation.Height -ne [int]$scenario.ExpectedHeight) {
      throw "$name 固定 resolution 不匹配：$($presentation.Width)x$($presentation.Height)"
    }
  } elseif ($scenario.Contains('RatioA')) {
    $ratioError = [Math]::Abs(($presentation.Width * [int]$scenario.RatioB) - ($presentation.Height * [int]$scenario.RatioA))
    if ($ratioError -gt [Math]::Max([int]$scenario.RatioA, [int]$scenario.RatioB) * 2) {
      throw "$name presentation ratio 不匹配：$($presentation.Width)x$($presentation.Height)"
    }
  }

  $world = Convert-WorldContent ([string]$summary.Values['world_content']) "$name Game View probe"
  if ($world.SourceWidth -ne 640 -or $world.SourceHeight -ne 360 -or
      $world.TargetWidth -ne $presentation.Width -or $world.TargetHeight -ne $presentation.Height) {
    throw "$name world content 没有保持固定 640x360 world→presentation：$($summary.Values['world_content'])"
  }
  if ($world.Width -le 0 -or $world.Height -le 0 -or
      [Math]::Abs(($world.X * 2) + $world.Width - $world.TargetWidth) -gt 1 -or
      [Math]::Abs(($world.Y * 2) + $world.Height - $world.TargetHeight) -gt 1) {
    throw "$name world content 未在 presentation 中居中 letterbox：$($summary.Values['world_content'])"
  }

  $expectDockChrome = if ($scenario.Contains('ExpectDockChrome')) {
    [bool]$scenario.ExpectDockChrome
  } else {
    -not [bool]$scenario.MaximizeOnPlay
  }
  $windowWidth = if ($scenario.Contains('WindowWidth')) { [int]$scenario.WindowWidth } else { 1024 }
  $windowHeight = if ($scenario.Contains('WindowHeight')) { [int]$scenario.WindowHeight } else { 720 }
  $scenarioResults.Add([ordered]@{
    name = $name
    presetId = [string]$scenario.PresetId
    maximizeOnPlay = [bool]$scenario.MaximizeOnPlay
    window = [ordered]@{ width = $windowWidth; height = $windowHeight }
    presentation = [ordered]@{ width = $presentation.Width; height = $presentation.Height; source = [string]$summary.Values['presentation_source'] }
    worldContent = $world
    summary = $summary.Values
    framebuffer = Get-BmpEvidence $capturePath $expectDockChrome
    stdout = [IO.Path]::GetRelativePath($outputRootFull, $stdoutPath).Replace('\', '/')
    stderr = [IO.Path]::GetRelativePath($outputRootFull, $stderrPath).Replace('\', '/')
  })
}

$gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitCommit)) {
  throw '无法读取 git HEAD。'
}
$report = [ordered]@{
  schema = 'pixelengine.editor-gameview-presentation-probe/v1'
  capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  gitCommit = $gitCommit
  editorExecutable = $editorExecutableFull
  projectRoot = $projectRootFull
  windowTicks = $WindowTicks
  allPassed = $true
  scenarios = $scenarioResults
}
$reportPath = Join-Path $outputRootFull 'report.json'
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host "Editor Game View presentation probe 通过：$reportPath"
$report | ConvertTo-Json -Depth 16
