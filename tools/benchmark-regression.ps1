param(
  [string]$Project = "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj",
  [Parameter(Mandatory = $true)]
  [string]$BaselinePath,
  [string]$Artifacts = "artifacts/benchmark-regression"
)

$ErrorActionPreference = 'Stop'

function Convert-MeanToNanoseconds([string]$meanText) {
  $normalized = $meanText -replace ',', ''
  $match = [regex]::Match($normalized, '(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>ns|us|µs|ms|s)')
  if (-not $match.Success) {
    throw "Could not parse mean value: '$meanText'."
  }

  $value = [double]$match.Groups['value'].Value
  switch ($match.Groups['unit'].Value) {
    'ns' { return $value }
    'us' { return $value * 1000.0 }
    'µs' { return $value * 1000.0 }
    'ms' { return $value * 1000000.0 }
    's' { return $value * 1000000000.0 }
    default { throw "Unsupported benchmark time unit: $($match.Groups['unit'].Value)" }
  }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot $Project
$baselineFullPath = Join-Path $repoRoot $BaselinePath
$artifactsPath = Join-Path $repoRoot $Artifacts

if (-not (Test-Path $baselineFullPath)) {
  throw "Baseline file not found: $baselineFullPath"
}

$baseline = Get-Content -LiteralPath $baselineFullPath -Raw | ConvertFrom-Json
if (-not $baseline.benchmarks -or $baseline.benchmarks.Count -eq 0) {
  throw "Baseline file must contain at least one benchmark entry."
}

Remove-Item -LiteralPath $artifactsPath -Recurse -Force -ErrorAction SilentlyContinue

foreach ($entry in $baseline.benchmarks) {
  $filter = [string]$entry.filter
  if ([string]::IsNullOrWhiteSpace($filter)) {
    throw "Baseline entry is missing filter."
  }

  & dotnet run --project $projectPath -c Release --no-build -- `
    --filter $filter `
    --artifacts $artifactsPath `
    --job Short `
    --warmupCount 1 `
    --iterationCount 1 `
    --exporters markdown
  if ($LASTEXITCODE -ne 0) {
    throw "BenchmarkDotNet regression run failed for filter '$filter' with exit code $LASTEXITCODE."
  }
}

$markdownFiles = Get-ChildItem -LiteralPath (Join-Path $artifactsPath 'results') -File -Filter '*report-github.md' -ErrorAction SilentlyContinue
if (-not $markdownFiles) {
  throw "No BenchmarkDotNet markdown report was found under $artifactsPath/results."
}

$reports = ($markdownFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$failed = $false

foreach ($entry in $baseline.benchmarks) {
  $name = [string]$entry.name
  $method = [string]$entry.method
  $baselineMeanNs = [double]$entry.baselineMeanNs
  $maxRatio = [double]$entry.maxRatio
  if ([string]::IsNullOrWhiteSpace($name) -or $baselineMeanNs -le 0 -or $maxRatio -le 0) {
    throw "Invalid baseline entry: name, baselineMeanNs, and maxRatio are required."
  }

  if ([string]::IsNullOrWhiteSpace($method)) {
    $method = ($name -split '\.')[-1]
  }

  $escapedName = [regex]::Escape($name)
  $escapedMethod = [regex]::Escape($method)
  $fullNamePattern = "\|\s*$escapedName\s*\|(?<cells>.*)"
  $methodPattern = "\|\s*$escapedMethod\s*\|(?<cells>.*)"
  $match = [regex]::Match($reports, $fullNamePattern)
  if (-not $match.Success) {
    $match = [regex]::Match($reports, $methodPattern)
  }

  if (-not $match.Success) {
    throw "Benchmark '$name' (method '$method') was not found in BenchmarkDotNet report."
  }

  $cells = $match.Groups['cells'].Value -split '\|'
  $meanText = ($cells | Where-Object { $_ -match '\d' } | Select-Object -First 1).Trim()
  $meanNs = Convert-MeanToNanoseconds $meanText
  $ratio = $meanNs / $baselineMeanNs
  Write-Host "$name mean=$([Math]::Round($meanNs, 3)) ns baseline=$baselineMeanNs ns ratio=$([Math]::Round($ratio, 3)) max=$maxRatio"
  if ($ratio -gt $maxRatio) {
    Write-Error "Benchmark '$name' regressed beyond threshold."
    $failed = $true
  }
}

if ($failed) {
  throw "Benchmark regression gate failed."
}

Write-Host "Benchmark regression gate passed."
