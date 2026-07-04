param(
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [ValidateSet('r2r', 'aot')]
  [string]$Channel,

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [string]$ProductName,
  [string]$Version,
  [string]$InformationalVersion,
  [string]$ApplicationIcon,
  [switch]$IncludeSymbols,
  [string]$PublishDir,
  [switch]$AllowLoadOnly,
  [switch]$SkipNativeBuild,
  [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$publishRoot = Join-Path $repoRoot 'artifacts/publish'

function Invoke-Checked([string]$filePath, [string[]]$arguments) {
  & $filePath @arguments
  if ($LASTEXITCODE -ne 0) {
    throw "命令失败($LASTEXITCODE): $filePath $($arguments -join ' ')"
  }
}

function Get-HostRid {
  $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
      return 'win-arm64'
    }

    return 'win-x64'
  }

  if ($IsMacOS) {
    if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
      return 'osx-arm64'
    }

    return 'osx-x64'
  }

  if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
    return 'linux-arm64'
  }

  return 'linux-x64'
}

function Get-ExecutablePath([string]$directory, [string]$targetRid) {
  if ($ProductName) {
    $productEntry = Join-Path $directory $(if ($targetRid.StartsWith('win-')) { "$ProductName.exe" } else { $ProductName })
    if (Test-Path -LiteralPath $productEntry -PathType Leaf) {
      return $productEntry
    }
  }

  $name = if ($targetRid.StartsWith('win-')) { 'PixelEngine.Demo.exe' } else { 'PixelEngine.Demo' }
  return Join-Path $directory $name
}

function Get-Box2DDynamicPath([string]$directory, [string]$targetRid) {
  $libraryName = switch -Wildcard ($targetRid) {
    'win-*' { 'box2d.dll'; break }
    'linux-*' { 'libbox2d.so'; break }
    'osx-*' { 'libbox2d.dylib'; break }
    default { throw "不支持的 RID: $targetRid" }
  }

  return Join-Path $directory "runtimes/$targetRid/native/$libraryName"
}

function Test-QemuAarch64Available {
  if ((Get-HostRid) -ne 'linux-x64') {
    return $false
  }

  foreach ($name in @('qemu-aarch64', 'qemu-aarch64-static')) {
    if (Get-Command $name -ErrorAction SilentlyContinue) {
      return $true
    }
  }

  return $false
}

function Invoke-Smoke([string]$executable, [string]$targetRid) {
  if ((Get-HostRid) -eq $targetRid) {
    Invoke-Checked $executable -arguments @('--smoke')
    return
  }

  if ((Get-HostRid) -eq 'linux-x64' -and $targetRid -eq 'linux-arm64' -and (Test-QemuAarch64Available)) {
    $qemu = (Get-Command qemu-aarch64 -ErrorAction SilentlyContinue)
    if (-not $qemu) {
      $qemu = Get-Command qemu-aarch64-static -ErrorAction Stop
    }

    Invoke-Checked $qemu.Source -arguments @($executable, '--smoke')
    return
  }

  if (-not $AllowLoadOnly) {
    throw "当前宿主 $(Get-HostRid) 不能直接运行 $targetRid；若此路径是有意的跨架构验证，请传 -AllowLoadOnly。"
  }

  Write-Host "跨架构/跨 OS 产物执行被降级为加载校验: $targetRid"
}

function Assert-PublishLayout([string]$directory, [string]$targetRid, [string]$targetChannel) {
  if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    throw "发布目录不存在: $directory"
  }

  $executable = Get-ExecutablePath $directory $targetRid
  if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "未找到发布产物入口: $executable"
  }

  $contentDir = Join-Path $directory 'content'
  if (-not (Test-Path -LiteralPath (Join-Path $contentDir 'materials.json') -PathType Leaf) -or
      -not (Test-Path -LiteralPath (Join-Path $contentDir 'reactions.json') -PathType Leaf)) {
    throw "发布产物缺少 Demo content/materials.json 或 content/reactions.json: $contentDir"
  }

  $dynamicBox2D = Get-Box2DDynamicPath $directory $targetRid
  if ($targetChannel -eq 'r2r') {
    if (-not (Test-Path -LiteralPath $dynamicBox2D -PathType Leaf)) {
      throw "R2R 发布产物缺少动态 Box2D: $dynamicBox2D"
    }
  } elseif (Test-Path -LiteralPath $dynamicBox2D -PathType Leaf) {
    throw "AOT 发布产物不应携带动态 Box2D: $dynamicBox2D"
  }

  return $executable
}

function Invoke-PublishIfNeeded([string]$targetRid, [string]$targetChannel, [string]$directory) {
  if ($SkipPublish -or $PublishDir) {
    return
  }

  $script = Join-Path $PSScriptRoot "publish-$targetChannel.ps1"
  & $script -Rid $targetRid -Configuration $Configuration -Output $directory -Version $Version -InformationalVersion $InformationalVersion -ProductName $ProductName -ApplicationIcon $ApplicationIcon -IncludeSymbols:$IncludeSymbols.IsPresent -SkipNativeBuild:$SkipNativeBuild.IsPresent
  if ($LASTEXITCODE -ne 0) {
    throw "命令失败($LASTEXITCODE): $script -Rid $targetRid -Configuration $Configuration -Output $directory"
  }
}

if (-not $Rid) {
  $Rid = Get-HostRid
}

$channels = if ($Channel) { @($Channel) } else { @('r2r', 'aot') }
foreach ($targetChannel in $channels) {
  $directory = if ($PublishDir) { $PublishDir } else { Join-Path $publishRoot "$Rid-$targetChannel" }
  Invoke-PublishIfNeeded $Rid $targetChannel $directory
  $resolvedDirectory = (Resolve-Path $directory).Path
  $executable = Assert-PublishLayout $resolvedDirectory $Rid $targetChannel
  Invoke-Smoke $executable $Rid
  Write-Host "Publish verification completed for $Rid/$targetChannel."
  Write-Host "PublishDir: $resolvedDirectory"
}
