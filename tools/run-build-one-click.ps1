param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw '一键构建需要 PowerShell 7+。'
}

$utf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$batchPath = Join-Path $repoRoot '一键构建.bat'
$solutionPath = Join-Path $repoRoot 'PixelEngine.sln'
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source

foreach ($requiredFile in @($batchPath, $solutionPath)) {
  if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
    throw "一键构建缺少必需文件：$requiredFile"
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
  Write-Host 'one_click_build_preflight ok=True batchAscii=True batchCrlf=True tests=False finalOutput=False'
  exit 0
}

Write-Host '============================================================'
Write-Host "PixelEngine 一键构建（$Configuration）"
Write-Host '仅编译 solution；不运行测试、产品探针或最终输出发布。'
Write-Host '============================================================'
Write-Host

& $dotnet build $solutionPath -c $Configuration --disable-build-servers
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
  Write-Host
  Write-Host "[FAILED] 构建失败，退出码：$exitCode" -ForegroundColor Red
  exit $exitCode
}

Write-Host
Write-Host "[SUCCESS] PixelEngine.sln $Configuration 构建完成；未运行任何测试。" -ForegroundColor Green
exit 0
