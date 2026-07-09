[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$taskRoot = Join-Path $repoRoot 'plan/tasks'
$coveragePath = Join-Path $taskRoot 'source-coverage.json'
$errors = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $coveragePath -PathType Leaf)) {
    throw "Task coverage manifest not found: $coveragePath"
}

$coverage = Get-Content -LiteralPath $coveragePath -Raw | ConvertFrom-Json
if ($coverage.schema -ne 'pixelengine.task-source-coverage/v2') {
    $errors.Add("Unsupported coverage schema: $($coverage.schema)")
}

$canonicalFiles = Get-ChildItem -LiteralPath $taskRoot -File -Filter '*.md' |
    Where-Object { $_.Name -match '^(10|20|30|40|50|60)-' } |
    Sort-Object Name

$anyCheckboxPattern = '^\s*-\s+\[[^\]]*\](?:\s+|$)'
$legacyCheckboxPattern = '^\s*-\s+\[(?<state>[x ~!])\]\s+'
$taskPattern = '^\s*-\s+\[(?<state>[x ~!])\]\s+`(?<id>[A-Z]+-\d{3})`(?:\s|$)'
$taskLocations = @{}
$stateCounts = [ordered]@{ done = 0; open = 0; active = 0; blocked = 0 }

foreach ($file in $canonicalFiles) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNumber++
        if ($line -notmatch $anyCheckboxPattern) {
            continue
        }

        if ($line -notmatch $taskPattern) {
            $errors.Add("Non-canonical checkbox: plan/tasks/$($file.Name):$lineNumber")
            continue
        }

        $taskId = $Matches.id
        $state = $Matches.state
        $location = "plan/tasks/$($file.Name):$lineNumber"
        if ($taskLocations.ContainsKey($taskId)) {
            $errors.Add("Duplicate task ID $taskId at $location (first: $($taskLocations[$taskId]))")
        }
        else {
            $taskLocations[$taskId] = $location
        }

        switch ($state) {
            'x' { $stateCounts.done++ }
            ' ' { $stateCounts.open++ }
            '~' { $stateCounts.active++ }
            '!' { $stateCounts.blocked++ }
        }
    }
}

if ($stateCounts.active -gt 1) {
    $errors.Add("More than one canonical task is active: $($stateCounts.active)")
}

foreach ($file in $canonicalFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($content, '\b(?:BASE|SCOPE|OPT|ARCH|PERF|EDITOR|UI|DEMO|DOC|CI|TEST|EVID|REL|PLAN)-\d{3}\b')) {
        if (-not $taskLocations.ContainsKey($match.Value)) {
            $errors.Add("Unknown task reference $($match.Value) in plan/tasks/$($file.Name)")
        }
    }
}

$documentationFiles = @(
    Get-Item -LiteralPath (Join-Path $repoRoot 'plan/README.md')
    Get-Item -LiteralPath (Join-Path $repoRoot 'plan/CODEX-HANDOFF.md')
    Get-ChildItem -LiteralPath $taskRoot -File -Filter '*.md'
)
foreach ($file in $documentationFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($content, '\[[^\]]+\]\((?<target>[^)]+)\)')) {
        $target = $match.Groups['target'].Value.Trim().Trim('<', '>')
        if ($target -match '^(?:https?://|mailto:|#)') {
            continue
        }

        $pathPart = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($pathPart)) {
            continue
        }

        $decodedPath = [Uri]::UnescapeDataString($pathPart)
        $resolvedPath = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $decodedPath))
        if (-not (Test-Path -LiteralPath $resolvedPath)) {
            $relativeFile = [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
            $errors.Add("Broken local Markdown link in ${relativeFile}: $target")
        }
    }
}

$legacyTotals = [ordered]@{ done = 0; open = 0; active = 0; blocked = 0 }
$relatedTaskIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$planPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($plan in $coverage.plans) {
    if (-not $planPaths.Add([string]$plan.path)) {
        $errors.Add("Duplicate legacy plan entry: $($plan.path)")
    }

    $planPath = Join-Path $repoRoot ($plan.path -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
        $errors.Add("Legacy plan not found: $($plan.path)")
        continue
    }

    $actual = [ordered]@{ done = 0; open = 0; active = 0; blocked = 0 }
    $snapshotRows = [System.Collections.Generic.List[string]]::new()
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $planPath) {
        $lineNumber++
        if ($line -match $anyCheckboxPattern -and $line -notmatch $legacyCheckboxPattern) {
            $errors.Add("Malformed legacy checkbox: $($plan.path):$lineNumber")
            continue
        }
        if ($line -notmatch $legacyCheckboxPattern) {
            continue
        }

        $snapshotRows.Add($line)
        switch ($Matches.state) {
            'x' { $actual.done++ }
            ' ' { $actual.open++ }
            '~' { $actual.active++ }
            '!' { $actual.blocked++ }
        }
    }

    foreach ($stateName in @('done', 'open', 'active', 'blocked')) {
        $expectedValue = [int]$plan.expected.$stateName
        $actualValue = [int]$actual[$stateName]
        $legacyTotals[$stateName] += $actualValue
        if ($actualValue -ne $expectedValue) {
            $errors.Add("Legacy count drift in $($plan.path): $stateName expected $expectedValue, actual $actualValue")
        }
    }

    $snapshotText = $snapshotRows -join "`n"
    $snapshotBytes = [Text.Encoding]::UTF8.GetBytes($snapshotText)
    $snapshotHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($snapshotBytes)).ToLowerInvariant()
    if ($snapshotHash -ne [string]$plan.checkboxSha256) {
        $errors.Add("Legacy checkbox content drift in $($plan.path): expected $($plan.checkboxSha256), actual $snapshotHash")
    }

    $snapshotCommitLines = @(& git -C $repoRoot show "$($coverage.snapshotCommit):$($plan.path)" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        $errors.Add("Cannot read $($plan.path) from snapshot commit $($coverage.snapshotCommit)")
    }
    else {
        $snapshotCommitRows = @($snapshotCommitLines | Where-Object { $_ -match $legacyCheckboxPattern })
        $snapshotCommitText = $snapshotCommitRows -join "`n"
        $snapshotCommitBytes = [Text.Encoding]::UTF8.GetBytes($snapshotCommitText)
        $snapshotCommitHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($snapshotCommitBytes)).ToLowerInvariant()
        if ($snapshotCommitHash -ne [string]$plan.checkboxSha256) {
            $errors.Add("Manifest digest does not match snapshot commit for $($plan.path): expected $($plan.checkboxSha256), commit has $snapshotCommitHash")
        }
    }

    if ($actual.done -gt 0 -and @($plan.completedCapabilityIds).Count -eq 0) {
        $errors.Add("Legacy completed bundle has no capability mapping: $($plan.path)")
    }
    if (($actual.open + $actual.active + $actual.blocked) -gt 0 -and @($plan.followUpTaskIds).Count -eq 0) {
        $errors.Add("Legacy incomplete bundle has no follow-up mapping: $($plan.path)")
    }

    foreach ($taskId in @($plan.completedCapabilityIds) + @($plan.followUpTaskIds)) {
        [void]$relatedTaskIds.Add([string]$taskId)
        if (-not $taskLocations.ContainsKey([string]$taskId)) {
            $errors.Add("Unknown task ID $taskId related to $($plan.path)")
        }
    }
}

$legacyTotal = ($legacyTotals.Values | Measure-Object -Sum).Sum
if ($legacyTotal -ne [int]$coverage.legacyCheckboxTotal) {
    $errors.Add("Legacy checkbox total drift: expected $($coverage.legacyCheckboxTotal), actual $legacyTotal")
}

$catalogOnlyTaskIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($taskId in $coverage.catalogOnlyTaskIds) {
    if (-not $catalogOnlyTaskIds.Add([string]$taskId)) {
        $errors.Add("Duplicate catalog-only task ID: $taskId")
    }
    if (-not $taskLocations.ContainsKey([string]$taskId)) {
        $errors.Add("Unknown catalog-only task ID: $taskId")
    }
    if ($relatedTaskIds.Contains([string]$taskId)) {
        $errors.Add("Catalog-only task $taskId is also related to a legacy plan")
    }
}

foreach ($taskId in $taskLocations.Keys) {
    if (-not $relatedTaskIds.Contains($taskId) -and -not $catalogOnlyTaskIds.Contains($taskId)) {
        $errors.Add("Canonical task $taskId has no legacy-plan relation or catalogOnlyTaskIds entry")
    }
}

$executionTaskIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$stageIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($stage in $coverage.requiredExecutionStages) {
    if (-not $stageIds.Add([string]$stage.id)) {
        $errors.Add("Duplicate execution stage ID: $($stage.id)")
    }
    foreach ($taskId in $stage.taskIds) {
        if (-not $executionTaskIds.Add([string]$taskId)) {
            $errors.Add("Task $taskId appears more than once in required execution stages")
        }
        if (-not $taskLocations.ContainsKey([string]$taskId)) {
            $errors.Add("Unknown execution task ID: $taskId")
        }
        if ([string]$taskId -match '^(BASE|SCOPE|OPT)-') {
            $errors.Add("Baseline, scope, or optional task must not appear in required execution stages: $taskId")
        }
    }
}

foreach ($taskId in $taskLocations.Keys) {
    $isRequiredExecutionTask = $taskId -notmatch '^(BASE|SCOPE|OPT)-'
    if ($isRequiredExecutionTask -and -not $executionTaskIds.Contains($taskId)) {
        $errors.Add("Required task $taskId is missing from required execution stages")
    }
}

if ($errors.Count -gt 0) {
    Write-Error ("Task catalog validation failed:`n - " + ($errors -join "`n - "))
    exit 1
}

$canonicalTotal = ($stateCounts.Values | Measure-Object -Sum).Sum
Write-Host "Task catalog valid."
Write-Host "Canonical: $canonicalTotal total; $($stateCounts.done) done, $($stateCounts.open) open, $($stateCounts.active) active, $($stateCounts.blocked) blocked."
Write-Host "Legacy snapshot: $legacyTotal total; $($legacyTotals.done) done, $($legacyTotals.open) open, $($legacyTotals.active) active, $($legacyTotals.blocked) blocked."
Write-Host "Coverage: $($coverage.plans.Count) legacy plans, $($relatedTaskIds.Count) related task IDs, $($catalogOnlyTaskIds.Count) catalog-only task IDs."
Write-Host "Execution: $($coverage.requiredExecutionStages.Count) stages, $($executionTaskIds.Count) required task IDs."
