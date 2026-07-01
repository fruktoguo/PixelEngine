param(
  [string]$Project = "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj",
  [string]$Filter = "*PaletteBgraConversionBenchmarks.ConvertAvx2Experimental*",
  [string]$Artifacts = "artifacts/disassembly-guard",
  [switch]$SkipRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot $Project
$artifactsPath = Join-Path $repoRoot $Artifacts

if (-not $SkipRun) {
  Remove-Item -LiteralPath $artifactsPath -Recurse -Force -ErrorAction SilentlyContinue
  & dotnet run --project $projectPath -c Release --no-build -- `
    --filter $Filter `
    --artifacts $artifactsPath `
    --job Short `
    --warmupCount 1 `
    --iterationCount 1
  if ($LASTEXITCODE -ne 0) {
    throw "BenchmarkDotNet disassembly run failed with exit code $LASTEXITCODE."
  }
}

$asmFiles = Get-ChildItem -LiteralPath $artifactsPath -Recurse -File -Include '*asm.md','*.asm' -ErrorAction SilentlyContinue
if (-not $asmFiles) {
  throw "No disassembly artifacts were found under $artifactsPath."
}

$combined = ($asmFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
if ($combined -match 'RNGCHKFAIL') {
  throw "Disassembly guard failed: RNGCHKFAIL was found in generated assembly."
}

$diagnosticFiles = Get-ChildItem -LiteralPath $artifactsPath -Recurse -File -Include '*.log','*report*.md','*report*.html','*report*.csv' -ErrorAction SilentlyContinue
$diagnostics = ($diagnosticFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$requireSimd = $diagnostics -match 'HardwareIntrinsics=.*\bAVX2\b'

if ($requireSimd -and $combined -notmatch '\b(ymm|zmm)[0-9]+\b|vpgather|gather') {
  throw "Disassembly guard failed: AVX2 is supported but no ymm/zmm/gather instruction marker was found."
}

Write-Host "Disassembly guard passed. Files checked: $($asmFiles.Count). SIMD required: $requireSimd."
