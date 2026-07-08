param(
  [string]$Project = "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj",
  [Parameter(Mandatory = $true)]
  [string]$BaselinePath,
  [string]$Artifacts = "artifacts/benchmark-regression",
  [string]$ReportsPath = ""
)

$ErrorActionPreference = 'Stop'

function Convert-MeanToNanoseconds([string]$meanText) {
  $normalized = ($meanText -replace '\*', '') -replace ',', ''
  $match = [regex]::Match($normalized, '^(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>ns|us|µs|μs|ms|s)$')
  if (-not $match.Success) {
    throw "Could not parse mean value: '$meanText'."
  }

  $value = [double]$match.Groups['value'].Value
  switch ($match.Groups['unit'].Value) {
    'ns' { return $value }
    'us' { return $value * 1000.0 }
    'µs' { return $value * 1000.0 }
    'μs' { return $value * 1000.0 }
    'ms' { return $value * 1000000.0 }
    's' { return $value * 1000000000.0 }
    default { throw "Unsupported benchmark time unit: $($match.Groups['unit'].Value)" }
  }
}

function Convert-MarkdownCellToText([string]$cell) {
  return (($cell -replace '<[^>]+>', '') -replace '\*', '' -replace '`', '').Trim()
}

function Split-MarkdownRow([string]$line) {
  $trimmed = $line.Trim()
  if (-not $trimmed.StartsWith('|') -or -not $trimmed.EndsWith('|')) {
    return @()
  }

  return @($trimmed.Substring(1, $trimmed.Length - 2) -split '\|' | ForEach-Object { Convert-MarkdownCellToText $_ })
}

function Test-MarkdownSeparator([object[]]$cells) {
  if ($cells.Count -eq 0) {
    return $false
  }

  foreach ($cell in $cells) {
    if ([string]$cell -notmatch '^:?-{3,}:?$') {
      return $false
    }
  }

  return $true
}

function Get-HeaderIndex([object[]]$headers, [string[]]$names) {
  for ($i = 0; $i -lt $headers.Count; $i++) {
    $normalized = ([string]$headers[$i] -replace '\s+', '')
    foreach ($name in $names) {
      if ($normalized.Equals($name, [StringComparison]::OrdinalIgnoreCase)) {
        return $i
      }
    }
  }

  return -1
}

function Get-BenchmarkReportRows([string]$reports) {
  $rows = [System.Collections.Generic.List[object]]::new()
  $lines = $reports -split "`r?`n"

  for ($i = 0; $i -lt ($lines.Count - 1); $i++) {
    $headers = @(Split-MarkdownRow $lines[$i])
    $separator = @(Split-MarkdownRow $lines[$i + 1])
    if ($headers.Count -eq 0 -or -not (Test-MarkdownSeparator $separator)) {
      continue
    }

    $methodIndex = Get-HeaderIndex $headers @('Method')
    if ($methodIndex -lt 0) {
      continue
    }

    $meanIndex = Get-HeaderIndex $headers @('Mean')
    if ($meanIndex -lt 0) {
      throw 'BenchmarkDotNet report table is missing a Mean column.'
    }

    for ($j = $i + 2; $j -lt $lines.Count; $j++) {
      $cells = @(Split-MarkdownRow $lines[$j])
      if ($cells.Count -eq 0) {
        break
      }

      if (Test-MarkdownSeparator $cells) {
        continue
      }

      if ($cells.Count -le [Math]::Max($methodIndex, $meanIndex)) {
        throw "BenchmarkDotNet report row has fewer cells than the header: '$($lines[$j])'."
      }

      $rows.Add([pscustomobject]@{
        Method = [string]$cells[$methodIndex]
        Mean = [string]$cells[$meanIndex]
        Raw = [string]$lines[$j]
      })
    }
  }

  if ($rows.Count -eq 0) {
    throw 'No BenchmarkDotNet report rows with Method and Mean columns were found.'
  }

  return $rows
}

function Test-PositiveFinite([double]$value) {
  return $value -gt 0 -and -not [double]::IsNaN($value) -and -not [double]::IsInfinity($value)
}

function Test-RowContains([object]$row, [string[]]$needles) {
  foreach ($needle in $needles) {
    if ($row.Raw -notlike "*$needle*") {
      return $false
    }
  }

  return $true
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

function Resolve-RepositoryPath([string]$path) {
  if ([string]::IsNullOrWhiteSpace($path)) {
    throw 'Path must not be empty.'
  }

  if ([System.IO.Path]::IsPathRooted($path)) {
    return $path
  }

  return Join-Path $repoRoot $path
}

$projectPath = Resolve-RepositoryPath $Project
$baselineFullPath = Resolve-RepositoryPath $BaselinePath
$artifactsPath = Resolve-RepositoryPath $Artifacts

if (-not (Test-Path $baselineFullPath)) {
  throw "Baseline file not found: $baselineFullPath"
}

$baseline = Get-Content -LiteralPath $baselineFullPath -Raw | ConvertFrom-Json
if (-not $baseline.benchmarks -or $baseline.benchmarks.Count -eq 0) {
  throw 'Baseline file must contain at least one benchmark entry.'
}

if ([string]::IsNullOrWhiteSpace($ReportsPath)) {
  Remove-Item -LiteralPath $artifactsPath -Recurse -Force -ErrorAction SilentlyContinue

  $entryIndex = 0
  foreach ($entry in $baseline.benchmarks) {
    $filter = [string]$entry.filter
    if ([string]::IsNullOrWhiteSpace($filter)) {
      throw 'Baseline entry is missing filter.'
    }

    $entryArtifacts = Join-Path $Artifacts "run-$entryIndex"
    & (Join-Path $repoRoot "tools/run-benchmark.ps1") `
      -Project $Project `
      -Artifacts $entryArtifacts `
      -BenchmarkDotNetArgs @(
        "--filter", $filter,
        "--job", "Short",
        "--warmupCount", "1",
        "--iterationCount", "1",
        "--exporters", "markdown")
    if ($LASTEXITCODE -ne 0) {
      throw "BenchmarkDotNet regression run failed for filter '$filter' with exit code $LASTEXITCODE."
    }

    $entryIndex++
  }

  $reportSearchPath = Join-Path $artifactsPath 'results'
  if (-not (Test-Path -LiteralPath $reportSearchPath)) {
    $reportSearchPath = $artifactsPath
  }
}
else {
  $reportSearchPath = Resolve-RepositoryPath $ReportsPath
}

if (Test-Path -LiteralPath $reportSearchPath -PathType Leaf) {
  $markdownFiles = @(Get-Item -LiteralPath $reportSearchPath)
}
else {
  $markdownFiles = @(Get-ChildItem -LiteralPath $reportSearchPath -File -Filter '*report-github.md' -Recurse -ErrorAction SilentlyContinue)
}

if (-not $markdownFiles) {
  throw "No BenchmarkDotNet markdown report was found under $reportSearchPath."
}

$reports = ($markdownFiles | Sort-Object FullName | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$reportRows = @(Get-BenchmarkReportRows $reports)
$failed = $false

foreach ($entry in $baseline.benchmarks) {
  $name = [string]$entry.name
  $method = [string]$entry.method
  $filter = [string]$entry.filter
  $baselineMeanNs = [double]$entry.baselineMeanNs
  $maxRatio = [double]$entry.maxRatio
  if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($filter) -or -not (Test-PositiveFinite $baselineMeanNs) -or -not (Test-PositiveFinite $maxRatio)) {
    throw 'Invalid baseline entry: name, filter, baselineMeanNs, and maxRatio are required and must be positive finite values.'
  }

  if ([string]::IsNullOrWhiteSpace($method)) {
    $method = ($name -split '\.')[-1]
  }

  $methodCandidates = @($name, $method, (($name -split '\.')[-1])) | Select-Object -Unique
  $matches = @($reportRows | Where-Object { $methodCandidates -contains $_.Method -or $_.Raw -like "*$name*" })
  if ($matches.Count -eq 0) {
    throw "Benchmark '$name' (method '$method') was not found in BenchmarkDotNet report."
  }

  $rowContains = @()
  if ($entry.PSObject.Properties.Name -contains 'rowContains') {
    $rowContains = @($entry.rowContains | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  }

  $matches = @($matches | Where-Object { Test-RowContains -row $_ -needles $rowContains })
  if ($matches.Count -eq 0) {
    throw "Benchmark '$name' (method '$method') did not contain a row matching: $($rowContains -join ', ')."
  }

  if ($matches.Count -gt 1) {
    if ($rowContains.Count -eq 0) {
      throw "Benchmark '$name' (method '$method') matched multiple report rows; add rowContains to disambiguate parameterized benchmark output."
    }

    throw "Benchmark '$name' (method '$method') rowContains matched multiple report rows: $($rowContains -join ', ')."
  }

  $meanNs = Convert-MeanToNanoseconds $matches[0].Mean
  $ratio = $meanNs / $baselineMeanNs
  Write-Host "$name mean=$([Math]::Round($meanNs, 3)) ns baseline=$baselineMeanNs ns ratio=$([Math]::Round($ratio, 3)) max=$maxRatio"
  if ($ratio -gt $maxRatio) {
    Write-Host "ERROR: Benchmark '$name' regressed beyond threshold."
    $failed = $true
  }
}

if ($failed) {
  throw 'Benchmark regression gate failed.'
}

Write-Host 'Benchmark regression gate passed.'
