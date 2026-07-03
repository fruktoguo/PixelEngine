param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [Parameter(Mandatory = $true)]
  [ValidateSet('r2r', 'aot')]
  [string]$Channel,

  [string]$Version,
  [string]$PublishDir,
  [string]$OutputRoot,
  [string]$ContentRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$demoProject = Join-Path $repoRoot 'demo/PixelEngine.Demo/PixelEngine.Demo.csproj'

if (-not $Version) {
  $Version = (& dotnet msbuild $demoProject -nologo -getProperty:VersionPrefix).Trim()
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Version)) {
    throw '无法读取 VersionPrefix。'
  }
}

if (-not $OutputRoot) {
  $OutputRoot = Join-Path $repoRoot 'artifacts/package'
}

if (-not $PublishDir) {
  $PublishDir = Join-Path $repoRoot "artifacts/publish/$Rid-$Channel"
}

if (-not $ContentRoot) {
  $ContentRoot = Join-Path $repoRoot 'demo/PixelEngine.Demo/content'
}

if (-not (Test-Path $PublishDir)) {
  throw "发布目录不存在: $PublishDir"
}

if (-not (Test-Path $ContentRoot)) {
  throw "内容目录不存在: $ContentRoot"
}

$packageName = "PixelEngine-Demo-$Version-$Rid-$Channel"
$stagingRoot = Join-Path $OutputRoot 'staging'
$stagingDir = Join-Path $stagingRoot $packageName
$appDir = Join-Path $stagingDir 'app'
New-Item -ItemType Directory -Force $OutputRoot | Out-Null
Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $appDir | Out-Null

Get-ChildItem -LiteralPath $PublishDir -Force | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName -Destination $appDir -Recurse -Force
}
$stagedContent = Join-Path $appDir 'content'
Remove-Item -LiteralPath $stagedContent -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $ContentRoot -Destination $stagedContent -Recurse -Force

$readme = @"
PixelEngine Demo
================

Start the game from this folder:
  Windows: PixelEngine Demo.cmd
  Linux/macOS: ./PixelEngine Demo.sh

The actual runtime files are under app/. This keeps the package root readable.
Advanced users can also run app/PixelEngine.Demo directly.
"@
Set-Content -LiteralPath (Join-Path $stagingDir 'README.txt') -Value $readme -Encoding ASCII

if ($Rid.StartsWith('win-')) {
  $launcher = @"
@echo off
setlocal
pushd "%~dp0app" >nul
".\PixelEngine.Demo.exe" %*
set "exitCode=%ERRORLEVEL%"
popd >nul
exit /b %exitCode%
"@
  Set-Content -LiteralPath (Join-Path $stagingDir 'PixelEngine Demo.cmd') -Value $launcher -Encoding ASCII
} else {
  $launcher = @"
#!/usr/bin/env sh
set -eu
script_dir=`$(CDPATH= cd -- "`$(dirname -- "`$0")" && pwd)
cd "`$script_dir/app"
exec ./PixelEngine.Demo "`$@"
"@
  Set-Content -LiteralPath (Join-Path $stagingDir 'PixelEngine Demo.sh') -Value $launcher -Encoding ASCII
}

$packageChecksumLines = Get-ChildItem -LiteralPath $appDir -Recurse -File |
  Sort-Object FullName |
  ForEach-Object {
    $relative = 'app/' + [IO.Path]::GetRelativePath($appDir, $_.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
  }
Set-Content -LiteralPath (Join-Path $stagingDir 'SHA256SUMS') -Value $packageChecksumLines -Encoding ASCII

if ($Rid.StartsWith('win-')) {
  $archiveName = "$packageName.zip"
  $archivePath = Join-Path $OutputRoot $archiveName
  Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
  dotnet run --project (Join-Path $repoRoot 'tools/PixelEngine.Tools.DeterministicPackage/PixelEngine.Tools.DeterministicPackage.csproj') -c Release -- `
    --source $stagingDir `
    --output $archivePath `
    --root-name $packageName `
    --format zip
  if ($LASTEXITCODE -ne 0) {
    throw "deterministic zip 打包失败: $archivePath"
  }
} else {
  $archiveName = "$packageName.tar.gz"
  $archivePath = Join-Path $OutputRoot $archiveName
  Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
  dotnet run --project (Join-Path $repoRoot 'tools/PixelEngine.Tools.DeterministicPackage/PixelEngine.Tools.DeterministicPackage.csproj') -c Release -- `
    --source $stagingDir `
    --output $archivePath `
    --root-name $packageName `
    --format tar.gz
  if ($LASTEXITCODE -ne 0) {
    throw "deterministic tar.gz 打包失败: $archivePath"
  }
}

$checksumPath = Join-Path $OutputRoot 'SHA256SUMS'
$archives = Get-ChildItem $OutputRoot -File |
  Where-Object { $_.Name -match '^PixelEngine-Demo-.+-(r2r|aot)\.(zip|tar\.gz)$' } |
  Sort-Object Name

$lines = foreach ($archive in $archives) {
  $hash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  "$hash  $($archive.Name)"
}

Set-Content -LiteralPath $checksumPath -Value $lines -Encoding ASCII

Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
if ((Test-Path -LiteralPath $stagingRoot -PathType Container) -and
  -not (Get-ChildItem -LiteralPath $stagingRoot -Force -ErrorAction SilentlyContinue | Select-Object -First 1)) {
  Remove-Item -LiteralPath $stagingRoot -Force -ErrorAction SilentlyContinue
}

Write-Host "Package completed for $Rid/$Channel."
Write-Host "Archive: $archivePath"
Write-Host "Checksums: $checksumPath"
