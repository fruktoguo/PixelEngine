param(
  [string]$OutputRoot = '最终输出',

  [switch]$AllowCommitMismatch
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/verify-final-output.ps1 需要 PowerShell 7+。请使用 pwsh -NoProfile -File tools/verify-final-output.ps1。'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootFull = if ([IO.Path]::IsPathRooted($OutputRoot)) {
  [IO.Path]::GetFullPath($OutputRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

function Resolve-OutputPath([string]$RelativePath, [string]$Label) {
  if ([string]::IsNullOrWhiteSpace($RelativePath)) {
    throw "$Label 不能为空。"
  }

  if ([IO.Path]::IsPathRooted($RelativePath)) {
    throw "$Label 必须是相对路径：$RelativePath"
  }

  $full = [IO.Path]::GetFullPath((Join-Path $outputRootFull $RelativePath))
  $root = $outputRootFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and
      -not [string]::Equals($full, $outputRootFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label 不能逃逸正式输出目录：$RelativePath"
  }

  return $full
}

function Assert-FileExists([string]$Path, [string]$Label) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "$Label 不存在：$Path"
  }
}

function Assert-ChecksumContains([Collections.Generic.HashSet[string]]$RelativePaths, [string]$RelativePath, [string]$Label) {
  $normalized = $RelativePath.Replace('\', '/')
  if (-not $RelativePaths.Contains($normalized)) {
    throw "SHA256SUMS 缺少 $Label：$normalized"
  }
}

if (-not (Test-Path -LiteralPath $outputRootFull -PathType Container)) {
  throw "正式输出目录不存在：$outputRootFull"
}

$manifestRelative = '_验证记录/manifest.json'
$manifestPath = Resolve-OutputPath $manifestRelative 'manifest'
Assert-FileExists $manifestPath 'manifest'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

if ($manifest.schema -ne 'pixelengine.final-output/v1') {
  throw "manifest schema 不匹配：$($manifest.schema)"
}

if ($manifest.sourceWorktreePolicy -ne 'tracked-clean-required') {
  throw "manifest sourceWorktreePolicy 不匹配：$($manifest.sourceWorktreePolicy)"
}

if ($manifest.sourceTrackedWorktreeClean -ne $true) {
  throw 'manifest sourceTrackedWorktreeClean 必须为 true。'
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) {
  throw '无法读取当前 git HEAD。'
}

if (-not $AllowCommitMismatch.IsPresent -and $manifest.gitCommit -ne $head) {
  throw "正式输出 gitCommit 与当前 HEAD 不一致。manifest=$($manifest.gitCommit), head=$head。若只做历史产物审计，请显式传 -AllowCommitMismatch。"
}

if ($manifest.checksumFile -ne 'SHA256SUMS') {
  throw "manifest checksumFile 不匹配：$($manifest.checksumFile)"
}

$checksumPath = Resolve-OutputPath ([string]$manifest.checksumFile) 'checksumFile'
Assert-FileExists $checksumPath 'SHA256SUMS'

$editorExe = Resolve-OutputPath ([string]$manifest.editorExecutable) 'editorExecutable'
$demoExe = Resolve-OutputPath ([string]$manifest.demoExecutable) 'demoExecutable'
Assert-FileExists $editorExe '编辑器入口'
Assert-FileExists $demoExe 'Demo 入口'

$validation = $manifest.validation
if ($null -eq $validation) {
  throw 'manifest 缺少 validation 节点。'
}

if ($validation.editorDefaultWorkbenchProbe.completed -ne $true -or
    $validation.editorDefaultWorkbenchProbe.succeeded -ne $true -or
    $validation.editorDefaultWorkbenchProbe.buildOk -ne $true) {
  throw '编辑器默认工作台 probe 记录不是通过状态。'
}

if ($validation.demoWindowProbe.completed -ne $true) {
  throw 'Demo 窗口 probe 记录不是完成状态。'
}

$validationPaths = @(
  [string]$validation.editorDefaultWorkbenchProbe.stdout,
  [string]$validation.editorDefaultWorkbenchProbe.stderr,
  [string]$validation.editorDefaultWorkbenchProbe.capture,
  [string]$validation.demoWindowProbe.stdout,
  [string]$validation.demoWindowProbe.stderr,
  [string]$validation.demoWindowProbe.capture,
  [string]$validation.demoBuildResult
)

foreach ($relative in $validationPaths) {
  $full = Resolve-OutputPath $relative 'validation path'
  Assert-FileExists $full "验证记录 $relative"
}

$demoBuildResultPath = Resolve-OutputPath ([string]$validation.demoBuildResult) 'demoBuildResult'
$demoBuildResult = Get-Content -Raw -LiteralPath $demoBuildResultPath | ConvertFrom-Json
if ($demoBuildResult.ok -ne $true) {
  throw "demo-build-result.json 不是 ok=true：$($demoBuildResult.error)"
}

if ($demoBuildResult.runtimeUiBackend -ne $manifest.demoRuntimeUiBackendRequested) {
  throw "Demo UI backend 记录不一致。manifest=$($manifest.demoRuntimeUiBackendRequested), build-result=$($demoBuildResult.runtimeUiBackend)"
}

$checksumLines = Get-Content -LiteralPath $checksumPath
$relativePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$checksumCount = 0
foreach ($line in $checksumLines) {
  if ([string]::IsNullOrWhiteSpace($line)) {
    continue
  }

  if ($line -notmatch '^(?<hash>[a-fA-F0-9]{64})\s{2}(?<path>.+)$') {
    throw "SHA256SUMS 行格式错误：$line"
  }

  $expectedHash = $Matches.hash.ToLowerInvariant()
  $relativePath = $Matches.path.Replace('\', '/')
  if (-not $relativePaths.Add($relativePath)) {
    throw "SHA256SUMS 存在重复路径：$relativePath"
  }

  $filePath = Resolve-OutputPath $relativePath 'checksum entry'
  Assert-FileExists $filePath "checksum 文件 $relativePath"
  $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $expectedHash) {
    throw "SHA256 不匹配：$relativePath expected=$expectedHash actual=$actualHash"
  }

  $checksumCount++
}

if ($checksumCount -eq 0) {
  throw 'SHA256SUMS 不能为空。'
}

Assert-ChecksumContains $relativePaths $manifestRelative 'manifest'
Assert-ChecksumContains $relativePaths 'README.txt' 'README'
Assert-ChecksumContains $relativePaths ([string]$manifest.editorExecutable) '编辑器入口'
Assert-ChecksumContains $relativePaths ([string]$manifest.demoExecutable) 'Demo 入口'
Assert-ChecksumContains $relativePaths ([string]$validation.demoBuildResult) 'demo-build-result'

if ($manifest.editorSymbolsIncluded -eq $false) {
  $editorRoot = Split-Path -Parent $editorExe
  $metadata = Get-ChildItem -LiteralPath $editorRoot -File -Recurse -Force |
    Where-Object {
      $_.Extension.Equals('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
      $_.Extension.Equals('.xml', [StringComparison]::OrdinalIgnoreCase)
    }
  if ($metadata) {
    throw "编辑器正式输出不应包含 .pdb/.xml 开发元数据：$($metadata[0].FullName)"
  }
}

Write-Host "final_output_verify schema=pixelengine.final-output-verify/v1, ok=True, gitCommit=$($manifest.gitCommit), checksum_count=$checksumCount, editor=$($manifest.editorExecutable), demo=$($manifest.demoExecutable)"
