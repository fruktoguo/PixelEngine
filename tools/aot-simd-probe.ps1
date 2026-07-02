param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
  [string]$Rid,

  [string]$PublishDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $PublishDir) {
  $PublishDir = Join-Path $repoRoot "artifacts/publish/$Rid-aot"
}

$exeName = if ($Rid.StartsWith('win-')) { 'PixelEngine.Demo.exe' } else { 'PixelEngine.Demo' }
$exePath = Join-Path $PublishDir $exeName
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
  throw "未找到 AOT 可执行文件: $exePath"
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
        $match = Get-ChildItem -Path $tool -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($match) {
          return $match.FullName
        }
      }
    }
  }

  return $null
}

function Resolve-Disassembler {
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $dumpbin = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
    if ($dumpbin) {
      return @($dumpbin.Source, '/DISASM')
    }

    $vsDumpbin = Find-VsTool 'VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe'
    if ($vsDumpbin) {
      return @($vsDumpbin, '/DISASM')
    }
  }

  $llvmObjdump = Get-Command llvm-objdump -ErrorAction SilentlyContinue
  if ($llvmObjdump) {
    return @($llvmObjdump.Source, '-d')
  }

  $objdump = Get-Command objdump -ErrorAction SilentlyContinue
  if ($objdump) {
    return @($objdump.Source, '-d')
  }

  throw '未找到可用反汇编工具：dumpbin.exe、llvm-objdump 或 objdump。'
}

$tool = Resolve-Disassembler
$filePath = $tool[0]
$arguments = @($tool[1], $exePath)
$assembly = & $filePath @arguments 2>&1
if ($LASTEXITCODE -ne 0) {
  throw "反汇编失败($LASTEXITCODE): $filePath $($arguments -join ' ')"
}

$combined = $assembly -join [Environment]::NewLine
if ($Rid.EndsWith('-x64')) {
  if ($combined -notmatch '\b[yz]mm[0-9]+\b') {
    throw "AOT SIMD probe failed: $Rid 原生可执行反汇编中未发现 ymm/zmm 指令寄存器。"
  }

  Write-Host "AOT SIMD probe passed for ${Rid}: found ymm/zmm marker."
  return
}

if ($combined -notmatch '\b(advSIMD|NEON|fmla|mla|umlal|smlal|addv|uaddlp|zip1|zip2|uzp1|uzp2|trn1|trn2|tbl|ld1|st1)\b' -and
    -not $combined.Contains('NEON', [StringComparison]::OrdinalIgnoreCase)) {
  throw "AOT SIMD probe failed: $Rid 原生可执行反汇编中未发现 NEON 证据。"
}

Write-Host "AOT SIMD probe passed for ${Rid}: found NEON marker."
