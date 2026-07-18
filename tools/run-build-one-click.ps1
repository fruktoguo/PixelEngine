param(
  [ValidateSet('win-x64', 'win-arm64')]
  [string]$Rid = 'win-x64',

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [ValidateSet('ManagedFallback', 'RmlUi', 'Ultralight')]
  [string]$DemoRuntimeUiBackend = 'RmlUi',

  [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw '一键快速输出需要 PowerShell 7+。'
}

$utf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$batchPath = Join-Path $repoRoot '一键构建.bat'
$fastOutputScript = Join-Path $PSScriptRoot 'update-final-output-fast.ps1'
$outputRoot = Join-Path $repoRoot '最终输出'
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source

foreach ($requiredFile in @($batchPath, $fastOutputScript)) {
  if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
    throw "一键快速输出缺少必需文件：$requiredFile"
  }
}

$batchBytes = [IO.File]::ReadAllBytes($batchPath)
if ($batchBytes.Where({ $_ -gt 0x7F }, 'First').Count -ne 0) {
  throw '一键构建 BAT 内容必须保持纯 ASCII，避免 cmd.exe 在本地代码页下误解码。'
}

$batchText = [Text.Encoding]::ASCII.GetString($batchBytes)
if ($batchText -match '(?<!\r)\n') {
  throw '一键构建 BAT 必须使用 CRLF 换行，避免 cmd.exe 吞掉每行开头。'
}

if ($CheckOnly) {
  Write-Host 'one_click_fast_output_preflight ok=True batchAscii=True batchCrlf=True tests=False probes=False verifier=False'
  exit 0
}

Write-Host '============================================================'
Write-Host "PixelEngine 快速输出（$Rid / $Configuration）"
Write-Host '生成可运行编辑器和可玩 Demo；跳过测试、产品探针与 verifier。'
Write-Host '============================================================'
Write-Host

& $pwsh `
  -NoLogo `
  -NoProfile `
  -ExecutionPolicy Bypass `
  -File $fastOutputScript `
  -Rid $Rid `
  -Configuration $Configuration `
  -DemoRuntimeUiBackend $DemoRuntimeUiBackend
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
  Write-Host
  Write-Host "[FAILED] 快速输出失败，退出码：$exitCode" -ForegroundColor Red
  Write-Host '旧的最终输出仍会保留；请根据上方错误修复后重试。'
  exit $exitCode
}

Write-Host
Write-Host "[SUCCESS] 编辑器和可玩 Demo 已输出到：$outputRoot" -ForegroundColor Green
Start-Process -FilePath explorer.exe -ArgumentList @($outputRoot)
exit 0
