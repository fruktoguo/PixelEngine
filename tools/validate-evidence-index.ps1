[CmdletBinding()]
param(
    [string]$IndexPath = "docs/evidence-index.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$indexFullPath = if ([System.IO.Path]::IsPathRooted($IndexPath)) {
    [System.IO.Path]::GetFullPath($IndexPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $IndexPath))
}

if (-not (Test-Path -LiteralPath $indexFullPath -PathType Leaf)) {
    throw "Evidence index does not exist: $IndexPath"
}

try {
    $index = Get-Content -LiteralPath $indexFullPath -Raw | ConvertFrom-Json
} catch {
    throw "Evidence index is not valid JSON: $($_.Exception.Message)"
}

if ($index.schema -ne "pixelengine.evidence-index/v1") {
    throw "Unsupported evidence index schema: $($index.schema)"
}

if ($index.generatedFromCommit -notmatch "^[0-9a-f]{7,64}$") {
    throw "generatedFromCommit must be a hexadecimal Git commit."
}

$entries = @($index.entries)
if ($entries.Count -eq 0) {
    throw "Evidence index must contain at least one entry."
}

$canonicalTaskIds = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "plan/tasks") -Filter "*.md" -File |
        Select-String -Pattern '(?<id>[A-Z]+-[0-9]{3})' -AllMatches |
        ForEach-Object { $_.Matches | ForEach-Object { $_.Groups["id"].Value } } |
        Sort-Object -Unique
)

$seenEntryIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$seenTaskIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$requiredProperties = @(
    "entryId",
    "taskIds",
    "evidenceState",
    "commit",
    "runSessionId",
    "runIdentityStatus",
    "hardware",
    "command",
    "reportPath",
    "sha256"
)

foreach ($entry in $entries) {
    foreach ($property in $requiredProperties) {
        if (-not ($entry.PSObject.Properties.Name -contains $property)) {
            throw "Entry is missing required property '$property': $($entry.entryId)"
        }
    }

    if ([string]::IsNullOrWhiteSpace([string]$entry.entryId) -or -not $seenEntryIds.Add([string]$entry.entryId)) {
        throw "Entry IDs must be non-empty and unique: $($entry.entryId)"
    }

    if ($entry.commit -notmatch "^[0-9a-f]{7,64}$") {
        throw "Entry commit must be a hexadecimal Git commit: $($entry.entryId)"
    }

    $taskIds = @($entry.taskIds)
    if ($taskIds.Count -eq 0) {
        throw "Entry must reference at least one task ID: $($entry.entryId)"
    }
    foreach ($taskId in $taskIds) {
        if ($canonicalTaskIds -notcontains [string]$taskId) {
            throw "Entry references an unknown canonical task ID '$taskId': $($entry.entryId)"
        }
        [void]$seenTaskIds.Add([string]$taskId)
    }

    if ([string]::IsNullOrWhiteSpace([string]$entry.hardware) -or [string]::IsNullOrWhiteSpace([string]$entry.command)) {
        throw "Hardware and command are required: $($entry.entryId)"
    }

    if ($entry.runSessionId -eq $null -and $entry.runIdentityStatus -ne "not_recorded_in_source_report") {
        throw "Entries without a run/session ID must explicitly declare not_recorded_in_source_report: $($entry.entryId)"
    }
    if ($entry.runSessionId -ne $null -and [string]::IsNullOrWhiteSpace([string]$entry.runSessionId)) {
        throw "runSessionId must be non-empty when present: $($entry.entryId)"
    }
    if ($entry.evidenceState -eq "complete" -and $entry.runSessionId -eq $null) {
        throw "Complete evidence cannot omit run/session identity: $($entry.entryId)"
    }

    $reportPath = [string]$entry.reportPath
    if ([System.IO.Path]::IsPathRooted($reportPath) -or $reportPath -match "(^|[\\/])\.\.([\\/]|$)") {
        throw "Evidence report path must be repository-relative and contained: $($entry.entryId)"
    }

    $normalizedReportPath = $reportPath.Replace("/", "\")
    $reportFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $normalizedReportPath))
    $repoRootWithSeparator = $repoRoot.TrimEnd("\") + "\"
    if (-not $reportFullPath.StartsWith($repoRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Evidence report escapes the repository root: $($entry.entryId)"
    }
    if ($normalizedReportPath -match "^(artifacts|BenchmarkDotNet\.Artifacts|scratch|publish|最终输出)([\\/]|$)") {
        throw "Evidence report must not be rooted in a volatile output directory: $($entry.entryId)"
    }
    if (-not (Test-Path -LiteralPath $reportFullPath -PathType Leaf)) {
        throw "Evidence report does not exist: $reportPath"
    }

    if ($entry.sha256 -notmatch "^[0-9a-f]{64}$") {
        throw "Evidence report SHA256 must be a 64-character hexadecimal digest: $($entry.entryId)"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $reportFullPath).Hash.ToLowerInvariant()
    if ($actualHash -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "Evidence report SHA256 mismatch for $reportPath. Expected $($entry.sha256), actual $actualHash"
    }
}

Write-Output ("Evidence index valid: {0} entries, {1} referenced task IDs." -f $entries.Count, $seenTaskIds.Count)
