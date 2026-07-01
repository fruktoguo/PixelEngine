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

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$demoProject = Join-Path $repoRoot 'demo/PixelEngine.Demo/PixelEngine.Demo.csproj'

if (-not $Rid) {
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }
  } elseif ($IsMacOS) {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
  } else {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'linux-arm64' } else { 'linux-x64' }
  }
}

if (-not $Output) {
  $Output = Join-Path $repoRoot "artifacts/publish/$Rid-r2r"
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

$publishProperties = @('-p:Channel=R2R')
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

Write-Host "R2R publish completed for $Rid."
Write-Host "Output: $Output"
