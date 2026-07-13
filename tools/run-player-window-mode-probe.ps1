param(
  [string]$PlayerRoot = '最终输出/游戏Demo',

  [string]$OutputRoot = '',

  [ValidateRange(1, 10000)]
  [int]$WindowTicks = 80,

  [ValidateRange(10, 1800)]
  [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/run-player-window-mode-probe.ps1 需要 PowerShell 7+。'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$playerRootFull = if ([IO.Path]::IsPathRooted($PlayerRoot)) {
  [IO.Path]::GetFullPath($PlayerRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $PlayerRoot))
}
$outputRootFull = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  [IO.Path]::GetFullPath((Join-Path $artifactsRoot "player-window-mode-probe/$(Get-Date -Format 'yyyyMMdd-HHmmss')"))
} elseif ([IO.Path]::IsPathRooted($OutputRoot)) {
  [IO.Path]::GetFullPath($OutputRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRootFull.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "OutputRoot 必须位于仓库 artifacts/ 下：$outputRootFull"
}

if (-not (Test-Path -LiteralPath $playerRootFull -PathType Container)) {
  throw "PlayerRoot 不存在：$playerRootFull"
}

if (Test-Path -LiteralPath $outputRootFull) {
  throw "OutputRoot 已存在；为避免覆盖证据，请换用新目录：$outputRootFull"
}

$launcherName = 'PixelEngine Demo.exe'
$sourceLauncher = Join-Path $playerRootFull $launcherName
$sourceStartup = Join-Path $playerRootFull 'content/startup.json'
if (-not (Test-Path -LiteralPath $sourceLauncher -PathType Leaf)) {
  throw "Player 入口不存在：$sourceLauncher"
}
if (-not (Test-Path -LiteralPath $sourceStartup -PathType Leaf)) {
  throw "Player startup.json 不存在：$sourceStartup"
}

$sourceSettings = Get-Content -Raw -LiteralPath $sourceStartup | ConvertFrom-Json
$presentationWidth = [int]$sourceSettings.windowWidth
$presentationHeight = [int]$sourceSettings.windowHeight
$runtimeUiBackend = [string]$sourceSettings.runtimeUiBackend
if ($presentationWidth -le 0 -or $presentationHeight -le 0) {
  throw "startup.json 窗口尺寸非法：${presentationWidth}x${presentationHeight}"
}
if ([string]::IsNullOrWhiteSpace($runtimeUiBackend)) {
  throw 'startup.json 缺少 runtimeUiBackend。'
}

function Copy-Directory([string]$Source, [string]$Destination) {
  New-Item -ItemType Directory -Path $Destination | Out-Null
  Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
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
    throw "无法启动 Player：$FilePath"
  }

  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try {
      $process.Kill($true)
    } catch {
      $process.Kill()
    }
    throw "Player 窗口模式 probe 超时：${TimeoutSeconds}s"
  }

  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  Set-Content -LiteralPath $StdoutPath -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $StderrPath -Value $stderr -Encoding UTF8
  if ($process.ExitCode -ne 0) {
    throw "Player 窗口模式 probe 退出码 $($process.ExitCode)：$stderr"
  }

  return [pscustomobject]@{
    ExitCode = $process.ExitCode
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

function Convert-ProbeRect([string]$Value, [string]$Label) {
  if ($Value -notmatch '^(?<left>-?\d+):(?<top>-?\d+):(?<width>\d+)x(?<height>\d+)$') {
    throw "$Label 矩形格式错误：$Value"
  }

  return [pscustomobject]@{
    Left = [int]$Matches.left
    Top = [int]$Matches.top
    Width = [int]$Matches.width
    Height = [int]$Matches.height
  }
}

function Get-BmpEvidence([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Player framebuffer 不存在：$Path"
  }

  $file = Get-Item -LiteralPath $Path
  if ($file.Length -lt 54) {
    throw "Player framebuffer 不是有效 BMP：$Path"
  }

  $stream = [IO.File]::OpenRead($Path)
  try {
    $reader = [IO.BinaryReader]::new($stream)
    if ($reader.ReadByte() -ne 0x42 -or $reader.ReadByte() -ne 0x4D) {
      throw "Player framebuffer 缺少 BM header：$Path"
    }
    $stream.Position = 18
    $width = $reader.ReadInt32()
    $height = [Math]::Abs($reader.ReadInt32())
    $stream.Position = 28
    $bitsPerPixel = $reader.ReadUInt16()
  } finally {
    $stream.Dispose()
  }

  if ($width -le 0 -or $height -le 0 -or $bitsPerPixel -lt 24) {
    throw "Player framebuffer geometry 非法：${width}x${height}x${bitsPerPixel}"
  }

  return [ordered]@{
    path = [IO.Path]::GetRelativePath($outputRootFull, $Path).Replace('\', '/')
    width = $width
    height = $height
    bitsPerPixel = $bitsPerPixel
    sizeBytes = $file.Length
    sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}

New-Item -ItemType Directory -Path $outputRootFull | Out-Null
$modes = @('Windowed', 'MaximizedWindow', 'BorderlessFullscreen')
$modeResults = [Collections.Generic.List[object]]::new()
$baselineMonitor = $null

foreach ($mode in $modes) {
  Write-Host "[player-window-mode] $mode"
  $modeRoot = Join-Path $outputRootFull $mode
  $playerStage = Join-Path $modeRoot '游戏Demo'
  New-Item -ItemType Directory -Path $modeRoot | Out-Null
  Copy-Directory $playerRootFull $playerStage

  $startupPath = Join-Path $playerStage 'content/startup.json'
  $startup = Get-Content -Raw -LiteralPath $startupPath | ConvertFrom-Json
  $startup.windowMode = $mode
  $startup | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $startupPath -Encoding UTF8

  $capturePath = Join-Path $modeRoot 'framebuffer.bmp'
  $stdoutPath = Join-Path $modeRoot 'stdout.log'
  $stderrPath = Join-Path $modeRoot 'stderr.log'
  $logDirectory = Join-Path $modeRoot 'runtime-logs'
  New-Item -ItemType Directory -Path $logDirectory | Out-Null
  $launcher = Join-Path $playerStage $launcherName
  $processResult = Invoke-ProbeProcess `
    -FilePath $launcher `
    -WorkingDirectory $playerStage `
    -Arguments @(
      '--no-hot-reload',
      '--window-ticks', $WindowTicks.ToString([Globalization.CultureInfo]::InvariantCulture),
      '--capture-frame', $capturePath,
      '--log-dir', $logDirectory
    ) `
    -StdoutPath $stdoutPath `
    -StderrPath $stderrPath

  $windowSummary = Get-SummaryValues $processResult.Stdout 'player_window_probe ' "$mode Player window probe"
  $uiSummary = Get-SummaryValues $processResult.Stdout 'game_ui_probe ' "$mode Game UI probe"
  Assert-SummaryValue $windowSummary.Values 'requested' $mode "$mode Player window probe"
  Assert-SummaryValue $windowSummary.Values 'available' 'True' "$mode Player window probe"
  Assert-SummaryValue $windowSummary.Values 'applied' 'True' "$mode Player window probe"
  Assert-SummaryValue $windowSummary.Values 'reason' 'none' "$mode Player window probe"
  Assert-SummaryValue $windowSummary.Values 'visible' 'True' "$mode Player window probe"
  Assert-SummaryValue $windowSummary.Values 'presentation' "${presentationWidth}x${presentationHeight}" "$mode Player window probe"
  Assert-SummaryValue $uiSummary.Values 'attached' 'True' "$mode Game UI probe"
  Assert-SummaryValue $uiSummary.Values 'canvases' '3' "$mode Game UI probe"
  Assert-SummaryValue $uiSummary.Values 'requested' $runtimeUiBackend "$mode Game UI probe"
  Assert-SummaryValue $uiSummary.Values 'active' $runtimeUiBackend "$mode Game UI probe"
  Assert-SummaryValue $uiSummary.Values 'fallback' 'False' "$mode Game UI probe"
  Assert-SummaryValue $uiSummary.Values 'content_path_non_ascii' 'True' "$mode Game UI probe"

  switch ($mode) {
    'Windowed' {
      Assert-SummaryValue $windowSummary.Values 'zoomed' 'False' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'popup' 'False' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'caption' 'True' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'thick_frame' 'True' "$mode Player window probe"
      if ($windowSummary.Values['client_matches_presentation'] -ne 'True' -and
          $windowSummary.Values['presentation_fits_work'] -ne 'False') {
        throw 'Windowed 客户区既未匹配 Presentation，也不是因 work area 不足而合法夹取。'
      }
      $baselineMonitor = [string]$windowSummary.Values['monitor']
    }
    'MaximizedWindow' {
      Assert-SummaryValue $windowSummary.Values 'zoomed' 'True' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'popup' 'False' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'caption' 'True' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'thick_frame' 'True' "$mode Player window probe"
    }
    'BorderlessFullscreen' {
      Assert-SummaryValue $windowSummary.Values 'popup' 'True' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'caption' 'False' "$mode Player window probe"
      Assert-SummaryValue $windowSummary.Values 'thick_frame' 'False' "$mode Player window probe"
      $windowRect = Convert-ProbeRect ([string]$windowSummary.Values['window']) 'Borderless window'
      $clientRect = Convert-ProbeRect ([string]$windowSummary.Values['client']) 'Borderless client'
      $monitorRect = Convert-ProbeRect ([string]$windowSummary.Values['monitor']) 'Borderless monitor'
      if ($windowRect.Left -ne $monitorRect.Left -or $windowRect.Top -ne $monitorRect.Top -or
          $windowRect.Width -ne $monitorRect.Width -or $windowRect.Height -ne $monitorRect.Height -or
          $clientRect.Width -ne $monitorRect.Width -or $clientRect.Height -ne $monitorRect.Height) {
        throw 'BorderlessFullscreen 未以无边框客户区完整覆盖 monitor。'
      }
      if ($baselineMonitor -and $baselineMonitor -ne [string]$windowSummary.Values['monitor']) {
        throw "BorderlessFullscreen 改变了 monitor mode：before=$baselineMonitor after=$($windowSummary.Values['monitor'])"
      }
    }
  }

  $modeResults.Add([ordered]@{
    mode = $mode
    startupPath = [IO.Path]::GetRelativePath($outputRootFull, $startupPath).Replace('\', '/')
    windowProbe = $windowSummary.Values
    gameUiProbe = $uiSummary.Values
    framebuffer = Get-BmpEvidence $capturePath
    stdout = [IO.Path]::GetRelativePath($outputRootFull, $stdoutPath).Replace('\', '/')
    stderr = [IO.Path]::GetRelativePath($outputRootFull, $stderrPath).Replace('\', '/')
  })
}

$gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitCommit)) {
  throw '无法读取 git HEAD。'
}

$report = [ordered]@{
  schema = 'pixelengine.player-window-mode-probe/v1'
  capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  gitCommit = $gitCommit
  sourcePlayerRoot = $playerRootFull
  presentation = "${presentationWidth}x${presentationHeight}"
  runtimeUiBackend = $runtimeUiBackend
  windowTicks = $WindowTicks
  allPassed = $true
  modes = $modeResults
}
$reportPath = Join-Path $outputRootFull 'report.json'
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host "Player 三种窗口模式 probe 通过：$reportPath"
$report | ConvertTo-Json -Depth 16
