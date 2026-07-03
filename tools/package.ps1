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
$packageDir = Join-Path $OutputRoot $packageName
$appDir = Join-Path $stagingDir 'app'
$stagedContent = Join-Path $stagingDir 'content'

function Remove-PlayerPackageNoise([string]$Directory) {
  Get-ChildItem -LiteralPath $Directory -Recurse -File -Force -ErrorAction SilentlyContinue |
    Where-Object {
      $_.Extension -in @('.pdb', '.xml') -or
      $_.Name.EndsWith('.resources.dll', [StringComparison]::OrdinalIgnoreCase)
    } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

  Get-ChildItem -LiteralPath $Directory -Recurse -Directory -Force -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Where-Object {
      -not (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
    } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}

function Set-AppHostRelativeAssemblyPath([string]$AppHostPath, [string]$RelativeAssemblyPath) {
  $bytes = [IO.File]::ReadAllBytes($AppHostPath)
  $old = [Text.Encoding]::UTF8.GetBytes('PixelEngine.Demo.dll')
  $new = [Text.Encoding]::UTF8.GetBytes($RelativeAssemblyPath)
  $index = -1
  for ($i = 0; $i -le $bytes.Length - $old.Length; $i++) {
    $matched = $true
    for ($j = 0; $j -lt $old.Length; $j++) {
      if ($bytes[$i + $j] -ne $old[$j]) {
        $matched = $false
        break
      }
    }

    if ($matched) {
      $index = $i
      break
    }
  }

  if ($index -lt 0) {
    throw "无法在 apphost 中定位 PixelEngine.Demo.dll: $AppHostPath"
  }

  for ($j = $old.Length; $j -le $new.Length; $j++) {
    if ($index + $j -ge $bytes.Length -or $bytes[$index + $j] -ne 0) {
      throw "apphost 中没有足够的空白空间写入相对程序集路径: $RelativeAssemblyPath"
    }
  }

  for ($j = 0; $j -lt $new.Length; $j++) {
    $bytes[$index + $j] = $new[$j]
  }

  $bytes[$index + $new.Length] = 0
  [IO.File]::WriteAllBytes($AppHostPath, $bytes)
}

New-Item -ItemType Directory -Force $OutputRoot | Out-Null
$samePairPattern = '^PixelEngine-Demo-.+-' + [regex]::Escape($Rid) + '-' + [regex]::Escape($Channel) + '(\.zip|\.tar\.gz)?$'
Get-ChildItem -LiteralPath $OutputRoot -Force -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -match $samePairPattern } |
  ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $appDir | Out-Null

Get-ChildItem -LiteralPath $PublishDir -Force | ForEach-Object {
  if ($_.Name -eq 'content') {
    return
  }

  Copy-Item -LiteralPath $_.FullName -Destination $appDir -Recurse -Force
}
Remove-PlayerPackageNoise $appDir
Remove-Item -LiteralPath $stagedContent -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $ContentRoot -Destination $stagedContent -Recurse -Force

if ($Rid.StartsWith('win-')) {
  $rootEntry = Join-Path $stagingDir 'PixelEngine Demo.exe'
  Copy-Item -LiteralPath (Join-Path $PublishDir 'PixelEngine.Demo.exe') -Destination $rootEntry -Force
  if ($Channel -eq 'r2r') {
    Set-AppHostRelativeAssemblyPath $rootEntry 'app\PixelEngine.Demo.dll'
  }

  Remove-Item -LiteralPath (Join-Path $appDir 'PixelEngine.Demo.exe') -Force -ErrorAction SilentlyContinue
}

$readme = @"
PixelEngine Demo
================

Start the game from this folder:
  Windows: PixelEngine Demo.exe
  Linux/macOS: ./PixelEngine Demo.sh

Runtime dependencies are under app/. Game content is under content/.
Debug symbols, XML documentation, and localized satellite resource DLLs are stripped from player packages.
"@
Set-Content -LiteralPath (Join-Path $stagingDir 'README.txt') -Value $readme -Encoding ASCII

if (-not $Rid.StartsWith('win-')) {
  $launcher = @"
#!/usr/bin/env sh
set -eu
script_dir=`$(CDPATH= cd -- "`$(dirname -- "`$0")" && pwd)
cd "`$script_dir/app"
exec ./PixelEngine.Demo --content "`$script_dir/content" "`$@"
"@
  Set-Content -LiteralPath (Join-Path $stagingDir 'PixelEngine Demo.sh') -Value $launcher -Encoding ASCII
}

$packageChecksumLines = Get-ChildItem -LiteralPath $stagingDir -Recurse -File |
  Where-Object { $_.Name -ne 'SHA256SUMS' } |
  Sort-Object FullName |
  ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($stagingDir, $_.FullName).Replace('\', '/')
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

Remove-Item -LiteralPath $packageDir -Recurse -Force -ErrorAction SilentlyContinue
Move-Item -LiteralPath $stagingDir -Destination $packageDir -Force
if ((Test-Path -LiteralPath $stagingRoot -PathType Container) -and
  -not (Get-ChildItem -LiteralPath $stagingRoot -Force -ErrorAction SilentlyContinue | Select-Object -First 1)) {
  Remove-Item -LiteralPath $stagingRoot -Force -ErrorAction SilentlyContinue
}

Write-Host "Package completed for $Rid/$Channel."
Write-Host "Archive: $archivePath"
Write-Host "Expanded: $packageDir"
Write-Host "Checksums: $checksumPath"
