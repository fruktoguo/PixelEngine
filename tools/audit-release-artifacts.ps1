param(
  [string]$PublishRoot,
  [string]$PackageRoot,
  [string]$ProductName,
  [string]$AssemblyName,
  [string]$RequiredScene = 'scenes/lava-mine.scene',
  [string]$ActiveRids,
  [switch]$DevLayout,
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

$launcherBaseName = if ($ProductName) { $ProductName } else { 'PixelEngine Demo' }
$assemblyBaseName = if ($AssemblyName) { $AssemblyName } else { 'PixelEngine.Demo' }
$requiredScenePath = $RequiredScene.Replace('\', '/').TrimStart('/')
if (-not $requiredScenePath.StartsWith('scenes/', [StringComparison]::OrdinalIgnoreCase)) {
  $requiredScenePath = "scenes/$requiredScenePath"
}
if (-not $requiredScenePath.EndsWith('.scene', [StringComparison]::OrdinalIgnoreCase)) {
  $requiredScenePath = "$requiredScenePath.scene"
}

function Get-EntryName([string]$rid) {
  $baseName = $assemblyBaseName
  if ($rid.StartsWith('win-')) {
    return "$baseName.exe"
  }

  return $baseName
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

function Resolve-ActiveRids([string]$value) {
  $validRids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
  if (-not [string]::IsNullOrWhiteSpace($value)) {
    $requested = @($value -split '[,\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
    if ($requested.Count -eq 0) {
      throw '-ActiveRids 不能为空。'
    }

    foreach ($rid in $requested) {
      if ($rid -notin $validRids) {
        throw "未知 active RID: $rid"
      }
    }

    return $requested
  }

  $configPath = Join-Path $repoRoot 'tools/release-rids.json'
  if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    return $validRids
  }

  $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
  $active = @($config.rids | Where-Object { [bool]$_.active } | ForEach-Object { [string]$_.rid })
  if ($active.Count -eq 0) {
    throw 'release-rids.json active RID 集为空。'
  }

  foreach ($rid in $active) {
    if ($rid -notin $validRids) {
      throw "release-rids.json 包含未知 active RID: $rid"
    }
  }

  return $active
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

function Assert-NoDynamicBox2D([string]$directory) {
  $dynamicLibraries = Get-ChildItem -LiteralPath $directory -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
      $_.Name -in @('box2d.dll', 'libbox2d.so', 'libbox2d.dylib')
    }

  if ($dynamicLibraries) {
    $paths = ($dynamicLibraries | Select-Object -ExpandProperty FullName) -join [Environment]::NewLine
    throw "AOT 产物不应携带动态 Box2D：$paths"
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

function Get-PackageArchiveEntries([System.IO.FileInfo]$package) {
  if ($package.Name.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
      return @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally {
      $archive.Dispose()
    }
  }

  if ($package.Name.EndsWith('.tar.gz', [StringComparison]::OrdinalIgnoreCase)) {
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    if (-not $tar) {
      throw "检查 tar.gz package 布局需要 tar 命令: $($package.FullName)"
    }

    $output = & $tar.Source -tzf $package.FullName 2>&1
    if ($LASTEXITCODE -ne 0) {
      throw "读取 package 清单失败($LASTEXITCODE): $($package.FullName)`n$($output -join [Environment]::NewLine)"
    }

    return @($output | ForEach-Object { $_.ToString().Replace('\', '/') })
  }

  throw "未知 package 格式: $($package.Name)"
}

function Get-PackageArchiveTextEntry([System.IO.FileInfo]$package, [string]$entryName) {
  if ($package.Name.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
      $entry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $entryName } | Select-Object -First 1
      if (-not $entry) {
        return $null
      }

      $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8, $true)
      try {
        return $reader.ReadToEnd()
      }
      finally {
        $reader.Dispose()
      }
    }
    finally {
      $archive.Dispose()
    }
  }

  if ($package.Name.EndsWith('.tar.gz', [StringComparison]::OrdinalIgnoreCase)) {
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    if (-not $tar) {
      throw "读取 tar.gz package 条目需要 tar 命令: $($package.FullName)"
    }

    $output = & $tar.Source -xOf $package.FullName $entryName 2>$null
    if ($LASTEXITCODE -ne 0) {
      return $null
    }

    return ($output -join [Environment]::NewLine)
  }

  throw "未知 package 格式: $($package.Name)"
}

function Test-DisallowedRuntimeRootFile([string]$relativePath) {
  $name = [IO.Path]::GetFileName($relativePath)
  return $name -match '\.(dll|pdb|xml)$' -or
    $name -match '\.deps\.json$' -or
    $name -match '\.runtimeconfig\.json$'
}

function Test-DisallowedPlayerPackageFile([string]$relativePath) {
  $name = [IO.Path]::GetFileName($relativePath)
  if ($DevLayout -and ($name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) -or $name.EndsWith('.xml', [StringComparison]::OrdinalIgnoreCase))) {
    return $false
  }

  return $name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
    $name.EndsWith('.xml', [StringComparison]::OrdinalIgnoreCase) -or
    $name.EndsWith('.resources.dll', [StringComparison]::OrdinalIgnoreCase) -or
    $name.Equals('createdump.exe', [StringComparison]::OrdinalIgnoreCase) -or
    $name.Equals('createdump', [StringComparison]::OrdinalIgnoreCase)
}

function Test-DisallowedPlayerOnlyFile([string]$relativePath) {
  $name = [IO.Path]::GetFileName($relativePath)
  return $name.Equals('PixelEngine.Editor.dll', [StringComparison]::OrdinalIgnoreCase) -or
    $name.StartsWith('ImGuizmo', [StringComparison]::OrdinalIgnoreCase) -or
    $name.StartsWith('ImPlot', [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoDuplicateContentUnderApp([string]$relativePath, [string]$packageName, [string]$prefix) {
  if ($relativePath -eq 'app/content' -or $relativePath.StartsWith('app/content/', [StringComparison]::Ordinal)) {
    throw "$prefix 不应在 app/ 下重复打包 content；content 只能位于包根 content/: $packageName -> $relativePath"
  }
}

function Assert-NoDuplicateWindowsLauncherUnderApp([string]$relativePath, [string]$rid, [string]$packageName, [string]$prefix) {
  if ($rid.StartsWith('win-') -and $relativePath -eq "app/$assemblyBaseName.exe") {
    throw "$prefix 不应在 app/ 下重复保留原始启动 exe；根目录只允许 $launcherBaseName.exe: $packageName -> $relativePath"
  }
}

function Assert-FriendlyPackageLayout([System.IO.FileInfo]$package) {
  if ($package.Name -notmatch '^PixelEngine-Demo-.+-(?<rid>win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(?<channel>r2r|aot)\.(zip|tar\.gz)$') {
    throw "package 文件名不符合发行命名: $($package.Name)"
  }

  $rid = $Matches['rid']
  $channel = $Matches['channel']
  $rootName = $package.Name -replace '\.zip$', '' -replace '\.tar\.gz$', ''
  $relativeEntries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $relativeFileEntries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($entry in Get-PackageArchiveEntries $package) {
    $raw = $entry.Trim().Replace('\', '/')
    $isDirectory = $raw.EndsWith('/', [StringComparison]::Ordinal)
    $normalized = $raw.TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($normalized)) {
      continue
    }

    $slash = $normalized.IndexOf('/')
    if ($slash -lt 0) {
      if ($normalized -ne $rootName) {
        throw "package 内根目录名称不符合包名: $($package.Name) -> $normalized"
      }

      continue
    }

    $root = $normalized.Substring(0, $slash)
    if ($root -ne $rootName) {
      throw "package 内根目录名称不符合包名: $($package.Name) -> $root"
    }

    $relative = $normalized.Substring($slash + 1)
    if ([string]::IsNullOrWhiteSpace($relative)) {
      continue
    }

    [void]$relativeEntries.Add($relative)
    if (-not $isDirectory) {
      [void]$relativeFileEntries.Add($relative)
    }
  }

  $launcher = if ($rid.StartsWith('win-')) { "$launcherBaseName.exe" } else { "$launcherBaseName.sh" }
  $requiredEntries = [System.Collections.Generic.List[string]]::new()
  foreach ($required in @('README.txt', 'NOTICE.txt', 'SHA256SUMS', $launcher, 'content/materials.json', 'content/reactions.json', 'content/weapons.json', 'content/textures/17_gravel.png', 'content/textures/18_boundary_stone.png', "content/$requiredScenePath")) {
    $requiredEntries.Add($required)
  }

  if ($channel -eq 'r2r') {
    $requiredEntries.Add($(if ($rid.StartsWith('win-')) { "app/$assemblyBaseName.dll" } else { "app/$assemblyBaseName" }))
  } elseif (-not $rid.StartsWith('win-')) {
    $requiredEntries.Add("app/$assemblyBaseName")
  }

  foreach ($required in $requiredEntries) {
    if (-not $relativeEntries.Contains($required)) {
      throw "package 缺少玩家友好布局入口、app 依赖或 content 内容: $($package.Name) -> $required"
    }
  }

  foreach ($relative in $relativeFileEntries) {
    if (Test-DisallowedPlayerPackageFile $relative) {
      throw "package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件: $($package.Name) -> $relative"
    }

    if ($relative.StartsWith('app/', [StringComparison]::Ordinal) -and (Test-DisallowedPlayerOnlyFile $relative)) {
      throw "package 不应包含编辑器专属闭包: $($package.Name) -> $relative"
    }
  }

  foreach ($relative in $relativeEntries) {
    Assert-NoDuplicateContentUnderApp $relative $package.Name 'package'
    Assert-NoDuplicateWindowsLauncherUnderApp $relative $rid $package.Name 'package'

    if (Test-DisallowedRuntimeRootFile $relative) {
      if (-not $relative.StartsWith('app/', [StringComparison]::Ordinal)) {
        throw "package 根目录不应包含运行时依赖，请放入 app/: $($package.Name) -> $relative"
      }
    }

    if (-not $relative.StartsWith('app/', [StringComparison]::Ordinal) -and
        -not $relative.StartsWith('content/', [StringComparison]::Ordinal) -and
        $relative -ne 'app' -and
        $relative -ne 'content' -and
        $relative -ne 'README.txt' -and
        $relative -ne 'SHA256SUMS' -and
        $relative -ne $launcher -and
        $relative -notmatch '^(LICENSE|NOTICE)(\..+)?$') {
      throw "package 根目录只允许启动入口/README/SHA256SUMS/许可文件与 app/content 目录: $($package.Name) -> $relative"
    }
  }

  $checksumEntryName = "$rootName/SHA256SUMS"
  $checksumText = Get-PackageArchiveTextEntry $package $checksumEntryName
  if ([string]::IsNullOrWhiteSpace($checksumText)) {
    throw "package 内 SHA256SUMS 为空或不可读: $($package.Name)"
  }

  $declared = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($line in $checksumText -split '\r?\n') {
    if ([string]::IsNullOrWhiteSpace($line)) {
      continue
    }

    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') {
      throw "package 内 SHA256SUMS 行格式无效: $($package.Name) -> $line"
    }

    $checksumName = $Matches[2] -replace '^\./', ''
    if ($checksumName -eq 'SHA256SUMS') {
      throw "package 内 SHA256SUMS 不应覆盖自身: $($package.Name)"
    }

    if (-not $relativeFileEntries.Contains($checksumName)) {
      throw "package 内 SHA256SUMS 指向不存在的文件: $($package.Name) -> $checksumName"
    }

    if (-not $declared.Add($checksumName)) {
      throw "package 内 SHA256SUMS 重复条目: $($package.Name) -> $checksumName"
    }
  }

  foreach ($relative in $relativeFileEntries) {
    if ($relative -ne 'SHA256SUMS' -and -not $declared.Contains($relative)) {
      throw "package 内 SHA256SUMS 未覆盖文件: $($package.Name) -> $relative"
    }
  }
}

function Assert-FriendlyExpandedPackageLayout([System.IO.DirectoryInfo]$packageDirectory) {
  if ($packageDirectory.Name -notmatch '^PixelEngine-Demo-.+-(?<rid>win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(?<channel>r2r|aot)$') {
    throw "展开 package 目录名不符合发行命名: $($packageDirectory.Name)"
  }

  $rid = $Matches['rid']
  $channel = $Matches['channel']
  $relativeEntries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $relativeFileEntries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

  foreach ($entry in Get-ChildItem -LiteralPath $packageDirectory.FullName -Recurse -Force) {
    $relative = [IO.Path]::GetRelativePath($packageDirectory.FullName, $entry.FullName).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative)) {
      continue
    }

    [void]$relativeEntries.Add($relative)
    if (-not $entry.PSIsContainer) {
      [void]$relativeFileEntries.Add($relative)
    }
  }

  $launcher = if ($rid.StartsWith('win-')) { "$launcherBaseName.exe" } else { "$launcherBaseName.sh" }
  $requiredEntries = [System.Collections.Generic.List[string]]::new()
  foreach ($required in @('README.txt', 'NOTICE.txt', 'SHA256SUMS', $launcher, 'content/materials.json', 'content/reactions.json', 'content/weapons.json', 'content/textures/17_gravel.png', 'content/textures/18_boundary_stone.png', "content/$requiredScenePath")) {
    $requiredEntries.Add($required)
  }

  if ($channel -eq 'r2r') {
    $requiredEntries.Add($(if ($rid.StartsWith('win-')) { "app/$assemblyBaseName.dll" } else { "app/$assemblyBaseName" }))
  } elseif (-not $rid.StartsWith('win-')) {
    $requiredEntries.Add("app/$assemblyBaseName")
  }

  foreach ($required in $requiredEntries) {
    if (-not $relativeEntries.Contains($required)) {
      throw "展开 package 缺少玩家友好布局入口、app 依赖或 content 内容: $($packageDirectory.Name) -> $required"
    }
  }

  foreach ($relative in $relativeFileEntries) {
    if (Test-DisallowedPlayerPackageFile $relative) {
      throw "展开 package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件: $($packageDirectory.Name) -> $relative"
    }

    if ($relative.StartsWith('app/', [StringComparison]::Ordinal) -and (Test-DisallowedPlayerOnlyFile $relative)) {
      throw "展开 package 不应包含编辑器专属闭包: $($packageDirectory.Name) -> $relative"
    }
  }

  foreach ($relative in $relativeEntries) {
    Assert-NoDuplicateContentUnderApp $relative $packageDirectory.Name '展开 package'
    Assert-NoDuplicateWindowsLauncherUnderApp $relative $rid $packageDirectory.Name '展开 package'

    if (Test-DisallowedRuntimeRootFile $relative) {
      if (-not $relative.StartsWith('app/', [StringComparison]::Ordinal)) {
        throw "展开 package 根目录不应包含运行时依赖，请放入 app/: $($packageDirectory.Name) -> $relative"
      }
    }

    if (-not $relative.StartsWith('app/', [StringComparison]::Ordinal) -and
        -not $relative.StartsWith('content/', [StringComparison]::Ordinal) -and
        $relative -ne 'app' -and
        $relative -ne 'content' -and
        $relative -ne 'README.txt' -and
        $relative -ne 'SHA256SUMS' -and
        $relative -ne $launcher -and
        $relative -notmatch '^(LICENSE|NOTICE)(\..+)?$') {
      throw "展开 package 根目录只允许启动入口/README/SHA256SUMS/许可文件与 app/content 目录: $($packageDirectory.Name) -> $relative"
    }
  }

  $checksumPath = Join-Path $packageDirectory.FullName 'SHA256SUMS'
  Assert-FileExists $checksumPath '展开 package 缺少 SHA256SUMS'
  $declared = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ([string]::IsNullOrWhiteSpace($line)) {
      continue
    }

    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') {
      throw "展开 package 内 SHA256SUMS 行格式无效: $($packageDirectory.Name) -> $line"
    }

    $checksumName = $Matches[2] -replace '^\./', ''
    if ($checksumName -eq 'SHA256SUMS') {
      throw "展开 package 内 SHA256SUMS 不应覆盖自身: $($packageDirectory.Name)"
    }

    if (-not $relativeFileEntries.Contains($checksumName)) {
      throw "展开 package 内 SHA256SUMS 指向不存在的文件: $($packageDirectory.Name) -> $checksumName"
    }

    if (-not $declared.Add($checksumName)) {
      throw "展开 package 内 SHA256SUMS 重复条目: $($packageDirectory.Name) -> $checksumName"
    }
  }

  foreach ($relative in $relativeFileEntries) {
    if ($relative -ne 'SHA256SUMS' -and -not $declared.Contains($relative)) {
      throw "展开 package 内 SHA256SUMS 未覆盖文件: $($packageDirectory.Name) -> $relative"
    }
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
  Assert-FileExists (Join-Path $directory 'content/weapons.json') '缺少 content/weapons.json'
  Assert-FileExists (Join-Path $directory 'content/textures/17_gravel.png') '缺少 content/textures/17_gravel.png'
  Assert-FileExists (Join-Path $directory 'content/textures/18_boundary_stone.png') '缺少 content/textures/18_boundary_stone.png'
  Assert-FileExists (Join-Path $directory "content/$requiredScenePath") "缺少必需场景 $requiredScenePath"

  $box2D = Join-Path $directory "runtimes/$rid/native/$(Get-Box2DName $rid)"
  if ($channel -eq 'r2r') {
    Assert-FileExists $box2D 'R2R 产物缺少动态 Box2D'
  } else {
    Assert-NoDynamicBox2D $directory
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
    Where-Object { $_.Name -match '^PixelEngine-Demo-.+\.(zip|tar\.gz)$' } |
    Sort-Object Name)
}

function Get-PackageDirectories {
  if (-not (Test-Path -LiteralPath $PackageRoot -PathType Container)) {
    return @()
  }

  return @(Get-ChildItem -LiteralPath $PackageRoot -Directory |
    Where-Object { $_.Name -match '^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)$' } |
    Sort-Object Name)
}

function Test-ReleasePackageName([string]$name) {
  return $name -match '^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$'
}

function Assert-PackagesAndChecksums {
  $packages = Get-PackageFiles
  $packageDirectories = Get-PackageDirectories
  foreach ($package in $packages) {
    if (-not (Test-ReleasePackageName $package.Name)) {
      throw "package 文件名不符合发行命名: $($package.Name)"
    }

    Assert-FriendlyPackageLayout $package
  }

  $packageNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($package in $packages) {
    $stem = $package.Name -replace '\.zip$', '' -replace '\.tar\.gz$', ''
    [void]$packageNames.Add($stem)
  }

  $directoryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($directory in $packageDirectories) {
    [void]$directoryNames.Add($directory.Name)
    if (-not $packageNames.Contains($directory.Name)) {
      throw "展开 package 目录缺少对应归档: $($directory.Name)"
    }

    Assert-FriendlyExpandedPackageLayout $directory
  }

  foreach ($packageName in $packageNames) {
    if (-not $directoryNames.Contains($packageName)) {
      throw "package 缺少同名展开目录: $packageName"
    }
  }

  $expectedPackageCount = $rids.Count * $channels.Count
  if ($RequireAll -and $packages.Count -ne $expectedPackageCount) {
    throw "package 数量不完整：期望 $expectedPackageCount，实际 $($packages.Count)。"
  }

  if ($packages.Count -eq 0) {
    return
  }

  foreach ($rid in $rids) {
    foreach ($channel in $channels) {
      $pairSuffixZip = '-' + $rid + '-' + $channel + '.zip'
      $pairSuffixTar = '-' + $rid + '-' + $channel + '.tar.gz'
      $matchingPackages = @($packages | Where-Object {
          $_.Name.EndsWith($pairSuffixZip, [StringComparison]::Ordinal) -or
          $_.Name.EndsWith($pairSuffixTar, [StringComparison]::Ordinal)
        })
      $countForPair = $matchingPackages.Count
      if ($RequireAll -and $countForPair -eq 0) {
        throw "missing package: $rid/$channel"
      }

      if ($countForPair -gt 1) {
        throw "同一 RID/channel 存在多个 package: $rid/$channel"
      }
    }
  }

  $checksumPath = Join-Path $PackageRoot 'SHA256SUMS'
  Assert-FileExists $checksumPath '缺少 SHA256SUMS'
  $checksumLines = Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
  $checksums = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
  $packageArchiveNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($package in $packages) {
    [void]$packageArchiveNames.Add($package.Name)
  }

  foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') {
      throw "SHA256SUMS 行格式无效: $line"
    }

    $checksumName = $Matches[2] -replace '^\./', ''
    if ($checksumName.Contains('/') -or $checksumName.Contains('\')) {
      throw "SHA256SUMS 只允许 package root 下的文件名: $checksumName"
    }

    if (-not $packageArchiveNames.Contains($checksumName)) {
      throw "SHA256SUMS 包含 package root 下不存在或非发行包的条目: $checksumName"
    }

    if ($checksums.ContainsKey($checksumName)) {
      throw "SHA256SUMS 重复条目: $checksumName"
    }

    $checksums.Add($checksumName, $Matches[1].ToLowerInvariant())
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

  if ($checksums.Count -ne $packages.Count) {
    throw "SHA256SUMS 条目数与 package 数不一致。"
  }

  Write-Host "Package audit passed. Packages: $($packages.Count). Expanded: $($packageDirectories.Count)."
}

$channels = @('r2r', 'aot')
$rids = Resolve-ActiveRids $ActiveRids

foreach ($rid in $rids) {
  foreach ($channel in $channels) {
    Test-PublishDirectory $rid $channel
  }
}

Assert-PackagesAndChecksums
Write-Host 'Release artifact audit completed.'
