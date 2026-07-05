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
  [string]$PlayerOutputDir,
  [string]$ContentRoot,
  [string]$ProductName,
  [string]$StartScene,
  [string[]]$IncludeScene = @(),
  [switch]$IncludeSymbols
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

if (-not $PlayerOutputDir) {
  $PlayerOutputDir = Join-Path $repoRoot 'artifacts/PixelEngine Demo'
}

if (-not $PublishDir) {
  $PublishDir = Join-Path $repoRoot "artifacts/publish/$Rid-$Channel"
}

if (-not $ContentRoot) {
  $ContentRoot = Join-Path $repoRoot 'demo/PixelEngine.Demo/content'
}

if (-not $ProductName) {
  $ProductName = 'PixelEngine Demo'
}

$assemblyBaseName = 'PixelEngine.Demo'
$windowsLauncherName = "$ProductName.exe"
$unixLauncherName = "$ProductName.sh"

if (-not (Test-Path $PublishDir)) {
  throw "发布目录不存在: $PublishDir"
}

if ($PSBoundParameters.ContainsKey('ProductName') -and -not [string]::IsNullOrWhiteSpace($ProductName)) {
  $candidate = Join-Path $PublishDir $(if ($Rid.StartsWith('win-')) { "$ProductName.exe" } else { $ProductName })
  if (Test-Path -LiteralPath $candidate -PathType Leaf) {
    $assemblyBaseName = $ProductName
  }
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

function Remove-PlayerPackageNoise([string]$Directory, [bool]$KeepSymbols) {
  Get-ChildItem -LiteralPath $Directory -Recurse -File -Force -ErrorAction SilentlyContinue |
    Where-Object {
      (-not $KeepSymbols -and $_.Extension -in @('.pdb', '.xml')) -or
      $_.Name.EndsWith('.resources.dll', [StringComparison]::OrdinalIgnoreCase) -or
      $_.Name.Equals('createdump.exe', [StringComparison]::OrdinalIgnoreCase) -or
      $_.Name.Equals('createdump', [StringComparison]::OrdinalIgnoreCase)
    } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

  Get-ChildItem -LiteralPath $Directory -Recurse -Directory -Force -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Where-Object {
      -not (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
    } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}

function Set-AppHostRelativeAssemblyPath([string]$AppHostPath, [string]$AssemblyName, [string]$RelativeAssemblyPath) {
  $bytes = [IO.File]::ReadAllBytes($AppHostPath)
  $old = [Text.Encoding]::UTF8.GetBytes($AssemblyName)
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
    throw "无法在 apphost 中定位 ${AssemblyName}: $AppHostPath"
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

function Normalize-ScenePath([string]$Scene) {
  $normalized = $Scene.Replace('\', '/').TrimStart('/')
  if (-not $normalized.StartsWith('scenes/', [StringComparison]::OrdinalIgnoreCase)) {
    $normalized = "scenes/$normalized"
  }

  return $normalized
}

function Copy-FilteredContent([string]$SourceRoot, [string]$DestinationRoot, [string[]]$Scenes, [string]$StartupScene) {
  Remove-Item -LiteralPath $DestinationRoot -Recurse -Force -ErrorAction SilentlyContinue
  Copy-Item -LiteralPath $SourceRoot -Destination $DestinationRoot -Recurse -Force
  $scenesToCopy = [System.Collections.Generic.List[string]]::new()
  foreach ($scene in $Scenes) {
    if (-not [string]::IsNullOrWhiteSpace($scene)) {
      $scenesToCopy.Add((Normalize-ScenePath $scene))
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($StartupScene)) {
    $startup = Normalize-ScenePath $StartupScene
    $startupJson = @"
{
  "startScene": "$startup"
}
"@
    Set-Content -LiteralPath (Join-Path $DestinationRoot 'startup.json') -Value $startupJson -Encoding UTF8
    if (-not $scenesToCopy.Contains($startup)) {
      $scenesToCopy.Add($startup)
    }
  }

  if ($scenesToCopy.Count -eq 0) {
    return
  }

  $sceneRoot = Join-Path $DestinationRoot 'scenes'
  Remove-Item -LiteralPath $sceneRoot -Recurse -Force -ErrorAction SilentlyContinue
  foreach ($scene in $scenesToCopy) {
    $relative = $scene
    if (-not $relative.EndsWith('.scene', [StringComparison]::OrdinalIgnoreCase)) {
      $relative = "$relative.scene"
    }

    $source = Join-Path $SourceRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
      throw "入包场景不存在: $source"
    }

    $destination = Join-Path $DestinationRoot $relative
    New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
  }
}

New-Item -ItemType Directory -Force $OutputRoot | Out-Null
$samePairPattern = '^PixelEngine-Demo-.+-' + [regex]::Escape($Rid) + '-' + [regex]::Escape($Channel) + '(\.zip|\.tar\.gz)?$'
Get-ChildItem -LiteralPath $OutputRoot -Force -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -match $samePairPattern } |
  ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $appDir | Out-Null

Get-ChildItem -LiteralPath $PublishDir -Force | ForEach-Object {
  if ($_.Name -eq 'content' -or $_.Name -eq '_PUBLISH_INTERMEDIATE_README.txt') {
    return
  }

  Copy-Item -LiteralPath $_.FullName -Destination $appDir -Recurse -Force
}
Remove-PlayerPackageNoise $appDir $IncludeSymbols.IsPresent
Remove-Item -LiteralPath $stagedContent -Recurse -Force -ErrorAction SilentlyContinue
Copy-FilteredContent $ContentRoot $stagedContent $IncludeScene $StartScene

if ($Rid.StartsWith('win-')) {
  $rootEntry = Join-Path $stagingDir $windowsLauncherName
  Copy-Item -LiteralPath (Join-Path $PublishDir "$assemblyBaseName.exe") -Destination $rootEntry -Force
  if ($Channel -eq 'r2r') {
    Set-AppHostRelativeAssemblyPath $rootEntry "$assemblyBaseName.dll" "app\$assemblyBaseName.dll"
  }

  Remove-Item -LiteralPath (Join-Path $appDir "$assemblyBaseName.exe") -Force -ErrorAction SilentlyContinue
}

$symbolLine = if ($IncludeSymbols) {
  'This development layout keeps debug symbols for local debugging.'
} else {
  'Debug symbols, XML documentation, diagnostic dump helpers, and localized satellite resource DLLs are stripped from player packages.'
}

$readme = @"
${ProductName}
================

Start the game from this folder:
  Windows: ${windowsLauncherName}
  Linux/macOS: ./${unixLauncherName}

Runtime dependencies are under app/. Game content is under content/.
$symbolLine
"@
Set-Content -LiteralPath (Join-Path $stagingDir 'README.txt') -Value $readme -Encoding ASCII

$notice = @"
Third-party notices
===================

PixelEngine ships dynamic/runtime dependencies in app/ and game content in content/.

- Box2D: MIT license. Used for pixel rigid body physics.
- RmlUi: MIT license. PixelEngine.UI.Native links the RmlUi core into the dynamic UI backend when the native UI library is present.
- FreeType: FreeType Project License. Used by the RmlUi native UI backend for font rasterization.
- Ultralight: optional commercial-license backend. It is not included in this package unless an activated UI profile explicitly ships its native binaries.

Full upstream license texts are kept with the vendored sources under native/.
"@
Set-Content -LiteralPath (Join-Path $stagingDir 'NOTICE.txt') -Value $notice -Encoding ASCII

if (-not $Rid.StartsWith('win-')) {
  $launcher = @"
#!/usr/bin/env sh
set -eu
script_dir=`$(CDPATH= cd -- "`$(dirname -- "`$0")" && pwd)
cd "`$script_dir/app"
exec ./${assemblyBaseName} --content "`$script_dir/content" "`$@"
"@
  Set-Content -LiteralPath (Join-Path $stagingDir $unixLauncherName) -Value $launcher -Encoding ASCII
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
Remove-Item -LiteralPath $PlayerOutputDir -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $packageDir -Destination $PlayerOutputDir -Recurse -Force
if ((Test-Path -LiteralPath $stagingRoot -PathType Container) -and
  -not (Get-ChildItem -LiteralPath $stagingRoot -Force -ErrorAction SilentlyContinue | Select-Object -First 1)) {
  Remove-Item -LiteralPath $stagingRoot -Force -ErrorAction SilentlyContinue
}

Write-Host "Package completed for $Rid/$Channel."
Write-Host "Archive: $archivePath"
Write-Host "Expanded: $packageDir"
Write-Host "PlayerOutput: $PlayerOutputDir"
Write-Host "Checksums: $checksumPath"
