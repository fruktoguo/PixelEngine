param(
  [string]$PublishRoot,
  [string]$PackageRoot,
  [switch]$RequireAll
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $PublishRoot) {
  $PublishRoot = Join-Path $repoRoot 'artifacts/publish'
}

if (-not $PackageRoot) {
  $PackageRoot = Join-Path $repoRoot 'artifacts/package'
}

$rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
$channels = @('r2r', 'aot')

function Get-EntryName([string]$rid) {
  if ($rid.StartsWith('win-')) {
    return 'PixelEngine.Demo.exe'
  }

  return 'PixelEngine.Demo'
}

function Get-Box2DName([string]$rid) {
  if ($rid.StartsWith('win-')) {
    return 'box2d.dll'
  }

  if ($rid.StartsWith('linux-')) {
    return 'libbox2d.so'
  }

  return 'libbox2d.dylib'
}

function Assert-FileExists([string]$path, [string]$message) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "${message}: $path"
  }
}

function Assert-NoStaticOpenAlOrAngle([string]$directory) {
  $staticLibraries = Get-ChildItem -LiteralPath $directory -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
      $name = $_.Name.ToLowerInvariant()
      ($name.EndsWith('.a') -or $name.EndsWith('.lib')) -and
      ($name.Contains('openal') -or $name.Contains('angle'))
    }

  if ($staticLibraries) {
    $paths = ($staticLibraries | Select-Object -ExpandProperty FullName) -join [Environment]::NewLine
    throw "发行产物包含 OpenAL/ANGLE 静态库，违反 native fan-out 收敛要求：$paths"
  }
}

function Assert-LinuxDynamicLink([string]$entryPath, [string]$rid) {
  if (-not $IsLinux -or -not $rid.StartsWith('linux-')) {
    return
  }

  $ldd = Get-Command ldd -ErrorAction SilentlyContinue
  if (-not $ldd) {
    throw "Linux 产物动态链接审计需要 ldd: $entryPath"
  }

  $output = & $ldd.Source $entryPath 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "ldd 审计失败($LASTEXITCODE): $entryPath`n$($output -join [Environment]::NewLine)"
  }

  $joined = $output -join [Environment]::NewLine
  if ($joined -match 'statically linked' -or $joined -notmatch 'libc\.so') {
    throw "Linux 产物未通过 glibc 动态链接审计: $entryPath`n$joined"
  }
}

function Test-PublishDirectory([string]$rid, [string]$channel) {
  $directory = Join-Path $PublishRoot "$rid-$channel"
  if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    if ($RequireAll) {
      throw "缺少发布目录: $directory"
    }

    return
  }

  $entry = Join-Path $directory (Get-EntryName $rid)
  Assert-FileExists $entry '缺少发布入口'
  Assert-FileExists (Join-Path $directory 'content/materials.json') '缺少 content/materials.json'
  Assert-FileExists (Join-Path $directory 'content/reactions.json') '缺少 content/reactions.json'
  Assert-FileExists (Join-Path $directory 'content/scenes/lava-mine.scene') '缺少默认场景'

  $box2D = Join-Path $directory "runtimes/$rid/native/$(Get-Box2DName $rid)"
  if ($channel -eq 'r2r') {
    Assert-FileExists $box2D 'R2R 产物缺少动态 Box2D'
  } elseif (Test-Path -LiteralPath $box2D -PathType Leaf) {
    throw "AOT 产物不应携带动态 Box2D: $box2D"
  }

  Assert-NoStaticOpenAlOrAngle $directory
  Assert-LinuxDynamicLink $entry $rid
  Write-Host "Publish artifact audit passed: $rid/$channel"
}

function Get-PackageFiles {
  if (-not (Test-Path -LiteralPath $PackageRoot -PathType Container)) {
    if ($RequireAll) {
      throw "缺少 package 目录: $PackageRoot"
    }

    return @()
  }

  return @(Get-ChildItem -LiteralPath $PackageRoot -File |
    Where-Object { $_.Name -match '^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$' } |
    Sort-Object Name)
}

function Assert-PackagesAndChecksums {
  $packages = Get-PackageFiles
  if ($RequireAll -and $packages.Count -ne 12) {
    throw "package 数量不完整：期望 12，实际 $($packages.Count)。"
  }

  if ($packages.Count -eq 0) {
    return
  }

  foreach ($rid in $rids) {
    foreach ($channel in $channels) {
      $exists = $packages | Where-Object { $_.Name -match "-$rid-$channel\.(zip|tar\.gz)$" }
      if ($RequireAll -and -not $exists) {
        throw "缺少 package: $rid/$channel"
      }
    }
  }

  $checksumPath = Join-Path $PackageRoot 'SHA256SUMS'
  Assert-FileExists $checksumPath '缺少 SHA256SUMS'
  $checksumLines = Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
  $checksums = @{}
  foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') {
      throw "SHA256SUMS 行格式无效: $line"
    }

    $checksums[$Matches[2]] = $Matches[1].ToLowerInvariant()
  }

  foreach ($package in $packages) {
    if (-not $checksums.ContainsKey($package.Name)) {
      throw "SHA256SUMS 未覆盖 package: $($package.Name)"
    }

    $actual = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $checksums[$package.Name]) {
      throw "SHA256 mismatch: $($package.Name)"
    }
  }

  if ($RequireAll -and $checksums.Count -ne $packages.Count) {
    throw "SHA256SUMS 条目数与 package 数不一致。"
  }

  Write-Host "Package audit passed. Packages: $($packages.Count)."
}

foreach ($rid in $rids) {
  foreach ($channel in $channels) {
    Test-PublishDirectory $rid $channel
  }
}

Assert-PackagesAndChecksums
Write-Host 'Release artifact audit completed.'
