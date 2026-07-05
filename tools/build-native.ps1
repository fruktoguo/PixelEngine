param(
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [switch]$Clean,
  [string]$CMakePath,
  [string]$NinjaPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$nativeRoot = Join-Path $repoRoot 'native'

if (-not $Rid) {
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }
  } elseif ($IsMacOS) {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
  } else {
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'linux-arm64' } else { 'linux-x64' }
  }
}

function Assert-HostSupportsRid([string]$rid) {
  $hostIsWindows = $IsWindows -or $env:OS -eq 'Windows_NT'
  $hostIsMacOS = $IsMacOS
  $hostIsLinux = $IsLinux

  if ($rid.StartsWith('win-') -and -not $hostIsWindows) {
    throw "RID $rid must be built from Windows."
  }

  if ($rid.StartsWith('linux-') -and -not $hostIsLinux) {
    throw "RID $rid must be built from Linux."
  }

  if ($rid.StartsWith('osx-') -and -not $hostIsMacOS) {
    throw "RID $rid must be built from macOS."
  }
}

function Find-VsTool([string]$relativePath) {
  $vswhereCandidates = @(
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
    "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe"
  )

  foreach ($candidate in $vswhereCandidates) {
    if (Test-Path $candidate) {
      $installPath = & $candidate -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
      if ($installPath) {
        $tool = Join-Path $installPath $relativePath
        if (Test-Path $tool) {
          return $tool
        }
      }
    }
  }

  return $null
}

function Import-VcVars([string]$rid) {
  if (-not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
    return
  }

  if (Get-Command cl.exe -ErrorAction SilentlyContinue) {
    return
  }

  $vcvars = if ($rid -eq 'win-arm64') {
    Find-VsTool 'VC\Auxiliary\Build\vcvarsamd64_arm64.bat'
  } else {
    Find-VsTool 'VC\Auxiliary\Build\vcvars64.bat'
  }

  if (-not $vcvars) {
    throw '未找到 Visual Studio C++ 工具链。请安装 MSVC v143 C++ build tools。'
  }

  $environment = cmd /c "`"$vcvars`" >nul && set"
  foreach ($line in $environment) {
    $index = $line.IndexOf('=')
    if ($index -gt 0) {
      [Environment]::SetEnvironmentVariable($line.Substring(0, $index), $line.Substring($index + 1), 'Process')
    }
  }
}

function Resolve-Tool([string]$explicitPath, [string]$commandName, [string]$vsRelativePath) {
  if ($explicitPath) {
    return (Resolve-Path $explicitPath).Path
  }

  $command = Get-Command $commandName -ErrorAction SilentlyContinue
  if ($command) {
    return $command.Source
  }

  $vsTool = Find-VsTool $vsRelativePath
  if ($vsTool) {
    return $vsTool
  }

  throw "未找到 $commandName。请安装 CMake/Ninja 或将其加入 PATH。"
}

function Invoke-Native([string]$filePath, [string[]]$arguments) {
  & $filePath @arguments
  if ($LASTEXITCODE -ne 0) {
    throw "命令失败($LASTEXITCODE): $filePath $($arguments -join ' ')"
  }
}

Assert-HostSupportsRid $Rid
Import-VcVars $Rid

$cmake = Resolve-Tool $CMakePath 'cmake' 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ninja = Resolve-Tool $NinjaPath 'ninja' 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
$env:PATH = "$(Split-Path $cmake);$(Split-Path $ninja);$env:PATH"

$buildDir = Join-Path $nativeRoot "out/build/$Rid"
if ($Clean -and (Test-Path $buildDir)) {
  Remove-Item -LiteralPath $buildDir -Recurse -Force
}

Invoke-Native $cmake @('--preset', $Rid, '-S', $nativeRoot)
Invoke-Native $cmake @('--build', $buildDir, '--target', 'box2d_shared', 'box2d_static', 'pixelengine_ui_native', '--config', $Configuration)

$sharedDir = Join-Path $nativeRoot "out/$Rid/shared"
$runtimeDir = Join-Path $repoRoot "runtimes/$Rid/native"
New-Item -ItemType Directory -Force $runtimeDir | Out-Null
New-Item -ItemType Directory -Force $sharedDir | Out-Null

$sharedSearchRoots = @(
  $sharedDir,
  (Join-Path $buildDir 'box2d-shared/bin')
) | Where-Object { Test-Path $_ }

$sharedDirPath = (Resolve-Path $sharedDir).Path
$sharedLibraries = $sharedSearchRoots |
  ForEach-Object { Get-ChildItem $_ -File -Recurse } |
  Where-Object { $_.Extension -in '.dll', '.so', '.dylib' }

if (-not $sharedLibraries) {
  throw "未在共享输出目录找到动态库产物。"
}

$staticDir = Join-Path $nativeRoot "out/$Rid/static"
$staticLibraries = if (Test-Path $staticDir) {
  Get-ChildItem $staticDir -File -Include *.lib,*.a -Recurse
} else {
  @()
}

if (-not $staticLibraries) {
  throw "未在静态输出目录找到静态库产物。"
}

foreach ($library in $sharedLibraries) {
  if ($library.DirectoryName -ne $sharedDirPath) {
    Copy-Item -LiteralPath $library.FullName -Destination $sharedDir -Force
  }

  Copy-Item -LiteralPath $library.FullName -Destination $runtimeDir -Force
}

Write-Host "Native build completed for $Rid."
Write-Host "Shared runtime: $runtimeDir"
Write-Host "Static output:  $staticDir"
