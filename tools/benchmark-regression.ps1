param(
  [string]$Project = "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj",
  [Parameter(Mandatory = $true)]
  [string]$BaselinePath,
  [string]$Artifacts = "artifacts/benchmark-regression",
  [string]$ReportsPath = "",
  [string]$BenchmarkRunnerPath = ""
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

function Convert-HeaderToKey([string]$header) {
  return ($header -replace '\s+', '')
}

function Get-BenchmarkReportRows([System.IO.FileInfo]$reportFile) {
  $rows = [System.Collections.Generic.List[object]]::new()
  $lines = (Get-Content -LiteralPath $reportFile.FullName -Raw) -split "`r?`n"
  $suffix = '-report-github.md'
  if (-not $reportFile.Name.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsupported BenchmarkDotNet report filename: $($reportFile.FullName)"
  }

  $benchmarkType = $reportFile.Name.Substring(0, $reportFile.Name.Length - $suffix.Length)

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

      if ($cells.Count -ne $headers.Count) {
        throw "BenchmarkDotNet report row/header cell count mismatch in '$($reportFile.FullName)': '$($lines[$j])'."
      }

      $cellsByHeader = @{}
      for ($columnIndex = 0; $columnIndex -lt $headers.Count; $columnIndex++) {
        $headerKey = Convert-HeaderToKey ([string]$headers[$columnIndex])
        if ([string]::IsNullOrWhiteSpace($headerKey) -or $cellsByHeader.ContainsKey($headerKey)) {
          throw "BenchmarkDotNet report contains an empty or duplicate normalized header '$headerKey' in '$($reportFile.FullName)'."
        }

        $cellsByHeader[$headerKey] = [string]$cells[$columnIndex]
      }

      $rows.Add([pscustomobject]@{
        BenchmarkType = $benchmarkType
        Method = [string]$cells[$methodIndex]
        Mean = [string]$cells[$meanIndex]
        Cells = $cellsByHeader
        Raw = [string]$lines[$j]
        Source = $reportFile.FullName
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

function Get-ExactParameters([object]$entry) {
  $parameters = @{}
  if ($entry.PSObject.Properties.Name -contains 'rowContains') {
    throw "Benchmark '$([string]$entry.name)' uses unsupported rowContains matching; replace it with an exact parameters object keyed by BenchmarkDotNet column name."
  }

  if ($entry.PSObject.Properties.Name -notcontains 'parameters' -or $null -eq $entry.parameters) {
    return $parameters
  }

  foreach ($property in $entry.parameters.PSObject.Properties) {
    $key = Convert-HeaderToKey ([string]$property.Name)
    if ([string]::IsNullOrWhiteSpace($key) -or $key.Equals('Method', [StringComparison]::OrdinalIgnoreCase) -or $key.Equals('Mean', [StringComparison]::OrdinalIgnoreCase)) {
      throw "Benchmark '$([string]$entry.name)' contains an invalid parameter column '$($property.Name)'."
    }

    if ($parameters.ContainsKey($key)) {
      throw "Benchmark '$([string]$entry.name)' contains duplicate normalized parameter column '$key'."
    }

    $parameters[$key] = [string]$property.Value
  }

  return $parameters
}

function Test-ExactParameters([object]$row, [hashtable]$parameters) {
  foreach ($key in $parameters.Keys) {
    if (-not $row.Cells.ContainsKey($key)) {
      return $false
    }

    if (-not ([string]$row.Cells[$key]).Equals([string]$parameters[$key], [StringComparison]::Ordinal)) {
      return $false
    }
  }

  return $true
}

function Format-ExactParameters([hashtable]$parameters) {
  return (($parameters.Keys | Sort-Object | ForEach-Object { "$_='$($parameters[$_])'" }) -join ', ')
}

function Get-BenchmarkReportFiles([string]$searchPath) {
  if (Test-Path -LiteralPath $searchPath -PathType Leaf) {
    $files = @(Get-Item -LiteralPath $searchPath)
  }
  else {
    $files = @(Get-ChildItem -LiteralPath $searchPath -File -Filter '*report-github.md' -Recurse -ErrorAction SilentlyContinue)
  }

  if (-not $files) {
    throw "No BenchmarkDotNet markdown report was found under $searchPath."
  }

  return @($files | Sort-Object FullName)
}

function Get-BenchmarkReportRowsFromPath([string]$searchPath) {
  $rows = [System.Collections.Generic.List[object]]::new()
  foreach ($file in @(Get-BenchmarkReportFiles $searchPath)) {
    foreach ($row in @(Get-BenchmarkReportRows $file)) {
      $rows.Add($row)
    }
  }

  if ($rows.Count -eq 0) {
    throw 'No BenchmarkDotNet report rows with Method and Mean columns were found.'
  }

  return $rows
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
$benchmarkRunnerFullPath = if ([string]::IsNullOrWhiteSpace($BenchmarkRunnerPath)) {
  Join-Path $repoRoot 'tools/run-benchmark.ps1'
}
else {
  Resolve-RepositoryPath $BenchmarkRunnerPath
}

if (-not (Test-Path $baselineFullPath)) {
  throw "Baseline file not found: $baselineFullPath"
}

if (-not (Test-Path -LiteralPath $benchmarkRunnerFullPath -PathType Leaf)) {
  throw "Benchmark runner not found: $benchmarkRunnerFullPath"
}

$baseline = Get-Content -LiteralPath $baselineFullPath -Raw | ConvertFrom-Json
if (-not $baseline.benchmarks -or $baseline.benchmarks.Count -eq 0) {
  throw 'Baseline file must contain at least one benchmark entry.'
}

$contracts = [System.Collections.Generic.List[object]]::new()
$filters = [System.Collections.Generic.List[string]]::new()
$filterSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $baseline.benchmarks) {
  $name = [string]$entry.name
  $benchmarkType = [string]$entry.benchmarkType
  $method = [string]$entry.method
  $filter = [string]$entry.filter
  $baselineMeanNs = [double]$entry.baselineMeanNs
  $maxRatio = [double]$entry.maxRatio
  if ([string]::IsNullOrWhiteSpace($name) -or
      [string]::IsNullOrWhiteSpace($benchmarkType) -or
      [string]::IsNullOrWhiteSpace($method) -or
      [string]::IsNullOrWhiteSpace($filter) -or
      -not (Test-PositiveFinite $baselineMeanNs) -or
      -not (Test-PositiveFinite $maxRatio)) {
    throw 'Invalid baseline entry: name, benchmarkType, method, filter, baselineMeanNs, and maxRatio are required and numeric values must be positive finite values.'
  }

  $parameters = Get-ExactParameters $entry
  $contracts.Add([pscustomobject]@{
    Name = $name
    BenchmarkType = $benchmarkType
    Method = $method
    Filter = $filter
    Parameters = $parameters
    BaselineMeanNs = $baselineMeanNs
    MaxRatio = $maxRatio
  })

  if ($filterSet.Add($filter)) {
    $filters.Add($filter)
  }
}

$usesProvidedReports = -not [string]::IsNullOrWhiteSpace($ReportsPath)
$reportRowsByFilter = @{}
if (-not $usesProvidedReports) {
  Remove-Item -LiteralPath $artifactsPath -Recurse -Force -ErrorAction SilentlyContinue

  $filterIndex = 0
  foreach ($filter in $filters) {
    $entryArtifacts = Join-Path $artifactsPath "run-$filterIndex"
    & $benchmarkRunnerFullPath `
      -Project $projectPath `
      -Artifacts $entryArtifacts `
      -BenchmarkDotNetArgs @(
        "--filter", $filter,
        "--job", "Short",
        "--warmupCount", "1",
        "--iterationCount", "1",
        "--exporters", "markdown")
    if (-not $?) {
      throw "BenchmarkDotNet regression run failed for filter '$filter'."
    }

    $reportRowsByFilter[$filter] = @(Get-BenchmarkReportRowsFromPath $entryArtifacts)
    $filterIndex++
  }
}
else {
  $reportSearchPath = Resolve-RepositoryPath $ReportsPath
  $providedReportRows = @(Get-BenchmarkReportRowsFromPath $reportSearchPath)
}

$failed = $false

foreach ($contract in $contracts) {
  $name = [string]$contract.Name
  $benchmarkType = [string]$contract.BenchmarkType
  $method = [string]$contract.Method
  $filter = [string]$contract.Filter
  $parameters = [hashtable]$contract.Parameters
  $baselineMeanNs = [double]$contract.BaselineMeanNs
  $maxRatio = [double]$contract.MaxRatio
  $reportRows = if ($usesProvidedReports) {
    @($providedReportRows)
  }
  else {
    @($reportRowsByFilter[$filter])
  }

  $identityMatches = @($reportRows | Where-Object {
    [string]::Equals([string]$_.BenchmarkType, $benchmarkType, [StringComparison]::Ordinal) -and
    [string]::Equals([string]$_.Method, $method, [StringComparison]::Ordinal)
  })
  if ($identityMatches.Count -eq 0) {
    throw "Benchmark '$name' did not find exact report identity benchmarkType='$benchmarkType', method='$method'."
  }

  $matches = @($identityMatches | Where-Object { Test-ExactParameters -row $_ -parameters $parameters })
  if ($matches.Count -eq 0) {
    $parameterDescription = Format-ExactParameters $parameters
    $availableColumns = @($identityMatches | ForEach-Object { $_.Cells.Keys } | Sort-Object -Unique) -join ', '
    throw "Benchmark '$name' did not find exact parameters {$parameterDescription}; available columns: $availableColumns."
  }

  if ($matches.Count -gt 1) {
    if ($parameters.Count -eq 0) {
      throw "Benchmark '$name' matched multiple parameterized rows; add an exact parameters object keyed by BenchmarkDotNet column name."
    }

    $parameterDescription = Format-ExactParameters $parameters
    $sources = @($matches | ForEach-Object { $_.Source } | Sort-Object -Unique) -join ', '
    throw "Benchmark '$name' matched multiple rows for exact parameters {$parameterDescription}; duplicate report rows came from: $sources."
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
