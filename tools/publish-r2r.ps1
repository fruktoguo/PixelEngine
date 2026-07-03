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

function Remove-RepositoryDirectory([string]$Path) {
  $repoFullPath = [IO.Path]::GetFullPath($repoRoot)
  $fullPath = [IO.Path]::GetFullPath($Path)
  if (-not $fullPath.StartsWith($repoFullPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝删除仓库外目录: $fullPath"
  }

  Remove-Item -LiteralPath $fullPath -Recurse -Force -ErrorAction SilentlyContinue
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

$targetFramework = (& dotnet msbuild $demoProject -nologo -getProperty:TargetFramework).Trim()
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
  throw '无法读取 TargetFramework。'
}

$ridArchitecture = ($Rid -split '-')[-1]
Remove-RepositoryDirectory $Output
Remove-RepositoryDirectory (Join-Path $repoRoot "demo/PixelEngine.Demo/bin/$ridArchitecture/$Configuration/$targetFramework/$Rid")
Remove-RepositoryDirectory (Join-Path $repoRoot "demo/PixelEngine.Demo/obj/$ridArchitecture/$Configuration/$targetFramework/$Rid")
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Directory -Filter 'PixelEngine.*' | ForEach-Object {
  Remove-RepositoryDirectory (Join-Path $_.FullName "bin/$ridArchitecture/$Configuration/$targetFramework")
  Remove-RepositoryDirectory (Join-Path $_.FullName "obj/$ridArchitecture/$Configuration/$targetFramework")
}

Invoke-Checked 'dotnet' -arguments @(
  'publish', $demoProject,
  '-c', $Configuration,
  '-r', $Rid,
  $publishProperties,
  '-o', $Output
)

Write-Host "R2R publish completed for $Rid."
Write-Host "Output: $Output"
