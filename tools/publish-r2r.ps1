param(
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [string]$Output,
  [string]$Version,
  [string]$InformationalVersion,
  [string]$ProductName,
  [string]$AssemblyName,
  [string]$ApplicationIcon,
  [switch]$IncludeSymbols,
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

$publishProperties = @('-p:Channel=R2R', '-p:PixelEnginePlayerBuild=true')
if ($Version) {
  $publishProperties += "-p:Version=$Version"
}

if ($InformationalVersion) {
  $publishProperties += "-p:InformationalVersion=$InformationalVersion"
}

if ($ProductName) {
  $publishProperties += "-p:Product=$ProductName"
}

if ($AssemblyName) {
  if ($AssemblyName.Contains(' ')) {
    throw "AssemblyName 不能包含空格，当前值: $AssemblyName"
  }

  $publishProperties += "-p:AssemblyName=$AssemblyName"
}

if ($ApplicationIcon) {
  $publishProperties += "-p:ApplicationIcon=$ApplicationIcon"
}

if ($IncludeSymbols) {
  $publishProperties += '-p:DebugSymbols=true'
  $publishProperties += '-p:DebugType=portable'
} else {
  $publishProperties += '-p:DebugSymbols=false'
  $publishProperties += '-p:DebugType=None'
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

Invoke-Checked 'dotnet' -arguments (@(
  'publish', $demoProject,
  '-c', $Configuration,
  '-r', $Rid
) + $publishProperties + @(
  '-o', $Output
))

$intermediateReadme = @"
This directory is a raw dotnet publish output for CI/package assembly.
It is not the player-facing package.

Use tools/package.ps1 -Rid $Rid -Channel r2r to create:
  artifacts/package/PixelEngine-Demo-<version>-$Rid-r2r/

That package root contains the launcher, content/, and app/ dependency folder.
"@
Set-Content -LiteralPath (Join-Path $Output '_PUBLISH_INTERMEDIATE_README.txt') -Value $intermediateReadme -Encoding ASCII

Write-Host "R2R publish completed for $Rid."
Write-Host "Output: $Output"
