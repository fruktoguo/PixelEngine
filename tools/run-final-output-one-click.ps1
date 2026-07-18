param(
  [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw '一键最终输出需要 PowerShell 7+。'
}

$utf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$batchPath = Join-Path $repoRoot '一键更新最终输出.bat'
$updateScript = Join-Path $PSScriptRoot 'update-final-output.ps1'
$verifyScript = Join-Path $PSScriptRoot 'verify-final-output.ps1'
$outputRoot = Join-Path $repoRoot '最终输出'
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source

foreach ($requiredFile in @($batchPath, $updateScript, $verifyScript)) {
  if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
    throw "一键最终输出缺少必需文件：$requiredFile"
  }
}

$batchBytes = [IO.File]::ReadAllBytes($batchPath)
if ($batchBytes.Where({ $_ -gt 0x7F }, 'First').Count -ne 0) {
  throw '一键 BAT 内容必须保持纯 ASCII，避免 cmd.exe 在本地代码页下误解码。'
}

$batchText = [Text.Encoding]::ASCII.GetString($batchBytes)
if ($batchText -match '(?<!\r)\n') {
  throw '一键 BAT 必须使用 CRLF 换行，避免 cmd.exe 吞掉每行开头。'
}

if ($CheckOnly) {
  Write-Host 'one_click_final_output_preflight ok=True batchAscii=True batchCrlf=True'
  exit 0
}

Write-Host '============================================================'
Write-Host 'PixelEngine 一键更新最终输出'
Write-Host '完整构建和真实探针耗时较长，请保持此窗口开启。'
Write-Host '============================================================'
Write-Host
Write-Host '[1/2] 构建、探针验证并原子更新最终输出...'
& $pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $updateScript
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
  Write-Host
  Write-Host "[FAILED] 正式输出更新失败，退出码：$exitCode" -ForegroundColor Red
  Write-Host '旧的最终输出仍会保留；请根据上方错误修复后重试。'
  exit $exitCode
}

Write-Host
Write-Host '[2/2] 对已发布的最终输出再次运行独立审计...'
& $pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $verifyScript
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
  Write-Host
  Write-Host "[FAILED] 发布后独立审计失败，退出码：$exitCode" -ForegroundColor Red
  Write-Host '在审计问题修复前，请勿使用这份输出。'
  exit $exitCode
}

Write-Host
Write-Host "[SUCCESS] 最终输出已完成：$outputRoot" -ForegroundColor Green
Start-Process -FilePath explorer.exe -ArgumentList @($outputRoot)
exit 0
