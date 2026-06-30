param(
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$demoProject = Join-Path $repoRoot 'demo/PixelEngine.Demo/PixelEngine.Demo.csproj'
$publishRoot = Join-Path $repoRoot 'artifacts/publish'

if (-not $Rid) {
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }
  } elseif ($IsMacOS) {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
  } else {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'linux-arm64' } else { 'linux-x64' }
  }
}

function Invoke-NativeCommand([string]$filePath, [string[]]$arguments) {
  & $filePath @arguments
  if ($LASTEXITCODE -ne 0) {
    throw "命令失败($LASTEXITCODE): $filePath $($arguments -join ' ')"
  }
}

function Get-ExecutablePath([string]$publishDir, [string]$rid) {
  if ($rid.StartsWith('win-')) {
    return Join-Path $publishDir 'PixelEngine.Demo.exe'
  }

  return Join-Path $publishDir 'PixelEngine.Demo'
}

function Invoke-Smoke([string]$publishDir, [string]$rid) {
  $exe = Get-ExecutablePath $publishDir $rid
  if (-not (Test-Path $exe)) {
    throw "未找到发布产物入口: $exe"
  }

  Invoke-NativeCommand $exe @()
}

if (-not $SkipNativeBuild) {
  & (Join-Path $PSScriptRoot 'build-native.ps1') -Rid $Rid -Configuration $Configuration
  if ($LASTEXITCODE -ne 0) {
    throw "native build failed for $Rid"
  }
}

$r2rDir = Join-Path $publishRoot "$Rid-r2r"
$aotDir = Join-Path $publishRoot "$Rid-aot"

Remove-Item -LiteralPath $r2rDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $aotDir -Recurse -Force -ErrorAction SilentlyContinue

Invoke-NativeCommand 'dotnet' @(
  'publish', $demoProject,
  '-c', $Configuration,
  '-r', $Rid,
  '--self-contained', 'true',
  '-p:PublishReadyToRun=true',
  '-p:PublishReadyToRunComposite=true',
  '-p:PublishSingleFile=false',
  '-o', $r2rDir
)

Invoke-Smoke $r2rDir $Rid

Invoke-NativeCommand 'dotnet' @(
  'publish', $demoProject,
  '-c', $Configuration,
  '-r', $Rid,
  '--self-contained', 'true',
  '-p:PublishAot=true',
  '-p:PublishSingleFile=false',
  '-o', $aotDir
)

Invoke-Smoke $aotDir $Rid

Write-Host "Publish verification completed for $Rid."
Write-Host "R2R: $r2rDir"
Write-Host "AOT: $aotDir"
