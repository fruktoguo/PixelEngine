param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [Parameter(Mandatory = $true)]
  [ValidateSet('r2r', 'aot')]
  [string]$Channel,

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [Parameter(Mandatory = $true)]
  [string]$Output,

  [string]$Version,
  [string]$InformationalVersion,
  [string]$ProductName = 'PixelEngine Demo',
  [string]$IconPath,
  [string]$ApplicationIcon,
  [switch]$IncludeSymbols,
  [string]$StartScene = 'scenes/playable-world.scene',
  [int]$WindowWidth = 1280,
  [int]$WindowHeight = 720,
  [string]$VSync = 'true',
  [string]$RuntimeUiBackend = 'ManagedFallback',
  [ValidateSet('Development', 'Production')]
  [string]$ReleaseChannel = 'Development',
  [string[]]$IncludeScene = @(),
  [switch]$DevLayout
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$outputRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location) $Output))
if ([IO.Path]::IsPathRooted($Output)) {
  $outputRoot = [IO.Path]::GetFullPath($Output)
}

$publishRoot = Join-Path $outputRoot 'publish'
$publishDir = Join-Path $publishRoot "$Rid-$Channel"
$packageRoot = Join-Path $outputRoot 'package'
$playerDir = Join-Path $outputRoot 'player'
$resultPath = Join-Path $outputRoot 'build-result.json'
$phaseTimings = [ordered]@{}
$warnings = [System.Collections.Generic.List[string]]::new()
$resolvedVersion = ''
$resolvedInformationalVersion = ''
$initialPlatformEnvironment = [Environment]::GetEnvironmentVariable('Platform', 'Process')

function Write-BuildEvent(
  [string]$Kind,
  [string]$Phase,
  [double]$Percent,
  [string]$Level,
  [string]$Message
) {
  $event = [ordered]@{
    schema = 'pixelengine.build/v1'
    kind = $Kind
    phase = $Phase
    percent = [math]::Round($Percent, 2)
    level = $Level
    message = $Message
    ts = [DateTimeOffset]::UtcNow.ToString('O')
  }
  $event | ConvertTo-Json -Compress -Depth 4
}

function Resolve-Version {
  if ($Version) {
    return $Version
  }

  $demoProject = Join-Path $repoRoot 'demo/PixelEngine.Demo/PixelEngine.Demo.csproj'
  $resolved = (& dotnet msbuild $demoProject -nologo -getProperty:VersionPrefix).Trim()
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolved)) {
    throw '无法读取 VersionPrefix。'
  }

  return $resolved
}

function Resolve-InformationalVersion([string]$ResolvedVersion) {
  if ($InformationalVersion) {
    return $InformationalVersion
  }

  $sha = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) {
    return $ResolvedVersion
  }

  return "$ResolvedVersion+$($sha.Trim())"
}

function Invoke-BuildPhase(
  [string]$Phase,
  [double]$StartPercent,
  [double]$EndPercent,
  [string]$Script,
  [hashtable]$Arguments
) {
  Write-BuildEvent 'Progress' $Phase $StartPercent 'Info' "开始 $Phase。"
  $stopwatch = [Diagnostics.Stopwatch]::StartNew()
  try {
    & $Script @Arguments *>&1 | ForEach-Object {
      $text = $_.ToString()
      if (-not [string]::IsNullOrWhiteSpace($text)) {
        Write-BuildEvent 'Log' $Phase $StartPercent 'Info' $text
      }
    }

    if ($LASTEXITCODE -ne 0) {
      throw "命令失败($LASTEXITCODE): $Script $($Arguments.Keys -join ',')"
    }
  }
  catch {
    Write-BuildEvent 'Log' $Phase $StartPercent 'Error' $_.Exception.Message
    throw
  }
  finally {
    $stopwatch.Stop()
    $phaseTimings[$Phase.Substring(0, 1).ToUpperInvariant() + $Phase.Substring(1)] = $stopwatch.Elapsed.TotalMilliseconds
  }

  Write-BuildEvent 'Progress' $Phase $EndPercent 'Info' "完成 $Phase。"
}

function Get-ArchivePath {
  $archives = @(Get-ChildItem -LiteralPath $packageRoot -File -ErrorAction SilentlyContinue |
      Where-Object {
        $_.Name.EndsWith("-$Rid-$Channel.zip", [StringComparison]::OrdinalIgnoreCase) -or
        $_.Name.EndsWith("-$Rid-$Channel.tar.gz", [StringComparison]::OrdinalIgnoreCase)
      } |
      Sort-Object LastWriteTimeUtc -Descending)
  return $archives | Select-Object -First 1
}

function Restore-DotNetPlatformEnvironment {
  if ([string]::IsNullOrEmpty($initialPlatformEnvironment)) {
    Remove-Item Env:Platform -Force -ErrorAction SilentlyContinue
    return
  }

  [Environment]::SetEnvironmentVariable('Platform', $initialPlatformEnvironment, 'Process')
}

function Write-BuildResult([bool]$Ok, [int]$ExitCode, [string]$ErrorMessage) {
  $archive = Get-ArchivePath
  $packageDir = $null
  $launcherExe = $null
  $sha256 = $null
  $sizeBytes = 0L
  if ($archive) {
    $stem = $archive.Name -replace '\.zip$', '' -replace '\.tar\.gz$', ''
    $packageDir = Join-Path $packageRoot $stem
    $sha256 = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sizeBytes = $archive.Length
  }

  if (Test-Path -LiteralPath $playerDir -PathType Container) {
    $launcherName = if ($Rid.StartsWith('win-')) { "$ProductName.exe" } else { "$ProductName.sh" }
    $launcherPath = Join-Path $playerDir $launcherName
    if (Test-Path -LiteralPath $launcherPath -PathType Leaf) {
      $launcherExe = $launcherPath
    }
  }

  $result = [ordered]@{
    ok = $Ok
    rid = $Rid
    channel = $Channel
    releaseChannel = $ReleaseChannel
    configuration = $Configuration
    version = $resolvedVersion
    informationalVersion = $resolvedInformationalVersion
    packageArchive = if ($archive) { $archive.FullName } else { $null }
    packageDir = $packageDir
    playerDir = if (Test-Path -LiteralPath $playerDir -PathType Container) { $playerDir } else { $null }
    launcherExe = $launcherExe
    sha256 = $sha256
    sizeBytes = $sizeBytes
    phaseTimingsMs = $phaseTimings
    warnings = @($warnings)
    error = if ($ErrorMessage) { $ErrorMessage } else { $null }
    exitCode = $ExitCode
  }
  $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
  Write-BuildEvent 'Result' 'done' $(if ($Ok) { 100 } else { 0 }) $(if ($Ok) { 'Info' } else { 'Error' }) $(if ($Ok) { '构建完成。' } else { $ErrorMessage })
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishRoot, $packageRoot | Out-Null
$icon = if ($IconPath) { $IconPath } else { $ApplicationIcon }
$publishScript = Join-Path $PSScriptRoot "publish-$Channel.ps1"
$includeSymbolsForPackage = $IncludeSymbols.IsPresent -or $DevLayout.IsPresent

function Get-HostRid {
  $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
      return 'win-arm64'
    }

    return 'win-x64'
  }

  if ($IsMacOS) {
    if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
      return 'osx-arm64'
    }

    return 'osx-x64'
  }

  if ($IsLinux) {
    if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
      return 'linux-arm64'
    }

    return 'linux-x64'
  }

  return $null
}

function Get-RidOperatingSystem([string]$TargetRid) {
  if ($TargetRid.StartsWith('win-')) {
    return 'win'
  }

  if ($TargetRid.StartsWith('linux-')) {
    return 'linux'
  }

  if ($TargetRid.StartsWith('osx-')) {
    return 'osx'
  }

  throw "不支持的 RID: $TargetRid"
}

function Resolve-RidSmokeMode([string]$TargetRid) {
  $configPath = Join-Path $PSScriptRoot 'release-rids.json'
  if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    return ''
  }

  $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
  foreach ($entry in @($config.rids)) {
    if ([string]$entry.rid -eq $TargetRid) {
      return [string]$entry.smoke
    }
  }

  return ''
}

try {
  $resolvedVersion = Resolve-Version
  $resolvedInformationalVersion = Resolve-InformationalVersion $resolvedVersion

  $hostRid = Get-HostRid
  $smokeMode = Resolve-RidSmokeMode $Rid
  $allowLoadOnly = $hostRid -and $hostRid -ne $Rid -and $smokeMode -eq 'load-only'
  if ($Channel -eq 'aot' -and $hostRid -and (Get-RidOperatingSystem $hostRid) -ne (Get-RidOperatingSystem $Rid)) {
    throw "NativeAOT 仅支持当前宿主 OS：$hostRid，当前选择为 $Rid。"
  }

  if ($allowLoadOnly) {
    $warnings.Add("目标 RID $Rid 按 release-rids.json 使用 load-only 校验；不会伪造目标硬件 smoke。") | Out-Null
  }

  $nativeArgs = @{
    Rid = $Rid
    Configuration = $Configuration
  }
  Invoke-BuildPhase 'native' 0 20 (Join-Path $PSScriptRoot 'build-native.ps1') $nativeArgs
  Restore-DotNetPlatformEnvironment

  $publishArgs = @{
    Rid = $Rid
    Configuration = $Configuration
    Output = $publishDir
    Version = $resolvedVersion
    InformationalVersion = $resolvedInformationalVersion
    ProductName = $ProductName
    SkipNativeBuild = $true
  }
  if ($icon) {
    $publishArgs.ApplicationIcon = $icon
  }
  if ($includeSymbolsForPackage) {
    $publishArgs.IncludeSymbols = $true
  }
  Invoke-BuildPhase 'publish' 20 45 $publishScript $publishArgs

  $verifyArgs = @{
    Rid = $Rid
    Channel = $Channel
    Configuration = $Configuration
    PublishDir = $publishDir
    ProductName = $ProductName
    SkipNativeBuild = $true
    SkipPublish = $true
  }
  if ($allowLoadOnly) {
    $verifyArgs.AllowLoadOnly = $true
  }
  Invoke-BuildPhase 'verify' 45 60 (Join-Path $PSScriptRoot 'verify-publish.ps1') $verifyArgs

  $packageArgs = @{
    Rid = $Rid
    Channel = $Channel
    Version = $resolvedVersion
    PublishDir = $publishDir
    OutputRoot = $packageRoot
    PlayerOutputDir = $playerDir
    ProductName = $ProductName
    StartScene = $StartScene
    WindowWidth = $WindowWidth
    WindowHeight = $WindowHeight
    VSync = $VSync
    RuntimeUiBackend = $RuntimeUiBackend
    ReleaseChannel = $ReleaseChannel
  }
  if ($IncludeScene.Count -gt 0) {
    $packageArgs.IncludeScene = $IncludeScene
  }
  if ($includeSymbolsForPackage) {
    $packageArgs.IncludeSymbols = $true
  }
  Invoke-BuildPhase 'package' 60 82 (Join-Path $PSScriptRoot 'package.ps1') $packageArgs

  $auditArgs = @{
    PublishRoot = $publishRoot
    PackageRoot = $packageRoot
    ProductName = $ProductName
    RequiredScene = $StartScene
  }
  if ($DevLayout -or $IncludeSymbols) {
    $auditArgs.DevLayout = $true
  }
  Invoke-BuildPhase 'audit' 82 100 (Join-Path $PSScriptRoot 'audit-release-artifacts.ps1') $auditArgs

  Write-BuildResult $true 0 ''
  exit 0
}
catch {
  $message = $_.Exception.Message
  Write-BuildResult $false 1 $message
  exit 1
}
