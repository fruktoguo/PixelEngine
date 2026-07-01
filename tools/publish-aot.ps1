param(
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [string]$Output,
  [string]$Version,
  [string]$InformationalVersion,
  [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'

if (-not $Rid) {
  throw '缺少必填参数: -Rid <RID>'
}

$hostIsWindows = $IsWindows -or $env:OS -eq 'Windows_NT'
$hostIsMacOS = $IsMacOS
$hostIsLinux = $IsLinux

if ($Rid.StartsWith('win-') -and -not $hostIsWindows) {
  throw "RID $Rid must be published from Windows."
}

if ($Rid.StartsWith('linux-') -and -not $hostIsLinux) {
  throw "RID $Rid must be published from Linux."
}

if ($Rid.StartsWith('osx-') -and -not $hostIsMacOS) {
  throw "RID $Rid must be published from macOS."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$demoProject = Join-Path $repoRoot 'demo/PixelEngine.Demo/PixelEngine.Demo.csproj'

if (-not $Output) {
  $Output = Join-Path $repoRoot "artifacts/publish/$Rid-aot"
}

function Invoke-Checked([string]$filePath, [string[]]$arguments) {
  & $filePath @arguments
  if ($LASTEXITCODE -ne 0) {
    throw "命令失败($LASTEXITCODE): $filePath $($arguments -join ' ')"
  }
}

if (-not $SkipNativeBuild) {
  & (Join-Path $PSScriptRoot 'build-native.ps1') -Rid $Rid -Configuration $Configuration
}

$publishProperties = @('-p:Channel=AOT')
if ($Version) {
  $publishProperties += "-p:Version=$Version"
}

if ($InformationalVersion) {
  $publishProperties += "-p:InformationalVersion=$InformationalVersion"
}

Remove-Item -LiteralPath $Output -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Checked 'dotnet' -arguments @(
  'publish', $demoProject,
  '-c', $Configuration,
  '-r', $Rid,
  $publishProperties,
  '-o', $Output
)

Write-Host "AOT publish completed for $Rid."
Write-Host "Output: $Output"
