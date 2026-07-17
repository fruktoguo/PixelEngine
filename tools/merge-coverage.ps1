[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CoverageResultsDirectory,

    [Parameter(Mandatory)]
    [string]$FullTestResultsDirectory,

    [Parameter(Mandatory)]
    [string]$BehaviorTestResultsDirectory,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [string]$PolicyPath = 'tools/coverage-policy.json',

    [string]$SourceProjectsRoot = 'src',

    [string]$RunId = '',

    [string]$CommitSha = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryRoot {
    $root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $root 'PixelEngine.sln') -PathType Leaf)) {
        throw "无法定位 PixelEngine.sln：$root"
    }

    return $root
}

function Resolve-AbsolutePath {
    param([string]$Root, [string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Assert-ContainedPath {
    param([string]$Parent, [string]$Child, [string]$Description)

    $parentWithSeparator = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $fullChild = [IO.Path]::GetFullPath($Child)
    if (-not $fullChild.StartsWith($parentWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description 必须位于 $Parent 内：$fullChild"
    }
}

function Read-JsonHashtable {
    param([string]$Path, [string]$Description)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description 不存在：$Path"
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        throw "$Description 不是有效 JSON：$Path $($_.Exception.Message)"
    }
}

function Read-XmlDocument {
    param([string]$Path)

    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    finally {
        $reader.Dispose()
    }
}

function Get-OutcomeKind {
    param([string]$Outcome)

    switch -CaseSensitive ($Outcome) {
        'Passed' { return 'passed' }
        'NotExecuted' { return 'notExecuted' }
        'Skipped' { return 'notExecuted' }
        default { return 'failed' }
    }
}

function Read-TestLayerSummary {
    param(
        [string]$ResultsDirectory,
        [string]$DisciplineSuffix,
        [int]$ExpectedTrxCount,
        [switch]$RequireBehaviorOnly
    )

    if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
        throw "TRX 目录不存在：$ResultsDirectory"
    }

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter '*.trx' -File)
    if ($trxFiles.Count -ne $ExpectedTrxCount) {
        throw "TRX 数量不匹配：directory=$ResultsDirectory expected=$ExpectedTrxCount actual=$($trxFiles.Count)"
    }

    $summary = [ordered]@{
        trxCount = $trxFiles.Count
        behavior = [ordered]@{ total = 0; passed = 0; notExecuted = 0; failed = 0 }
        sourceDiscipline = [ordered]@{ total = 0; passed = 0; notExecuted = 0; failed = 0 }
    }
    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($trx in $trxFiles) {
        $document = Read-XmlDocument -Path $trx.FullName
        $testRun = $document.SelectSingleNode("/*[local-name()='TestRun']")
        if ($null -eq $testRun -or [string]::IsNullOrWhiteSpace([string]$testRun.id) -or -not $runIds.Add([string]$testRun.id)) {
            throw "TRX TestRun.id 缺失或重复：$($trx.FullName)"
        }

        $definitions = @{}
        foreach ($unitTest in @($document.SelectNodes("//*[local-name()='UnitTest']"))) {
            $testId = [string]$unitTest.id
            $method = $unitTest.SelectSingleNode("./*[local-name()='TestMethod']")
            if ([string]::IsNullOrWhiteSpace($testId) -or $null -eq $method -or [string]::IsNullOrWhiteSpace([string]$method.className)) {
                throw "TRX 测试定义不完整：$($trx.FullName)"
            }

            $definitions[$testId] = [string]$method.className
        }

        $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
        $counters = $document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters -or [int]$counters.total -ne $results.Count) {
            throw "TRX Counters.total 与逐条结果不一致：$($trx.FullName)"
        }

        foreach ($result in $results) {
            $testId = [string]$result.testId
            if (-not $definitions.ContainsKey($testId)) {
                throw "TRX 结果找不到测试定义：$($trx.FullName) testId=$testId"
            }

            $className = [string]$definitions[$testId]
            $isDiscipline = $className.EndsWith($DisciplineSuffix, [StringComparison]::Ordinal)
            if ($RequireBehaviorOnly -and $isDiscipline) {
                throw "行为 coverage TRX 混入源码纪律测试：$className"
            }

            $layerName = if ($isDiscipline) { 'sourceDiscipline' } else { 'behavior' }
            $kind = Get-OutcomeKind -Outcome ([string]$result.outcome)
            $summary[$layerName].total++
            $summary[$layerName][$kind]++
        }
    }

    return $summary
}

function Get-ProjectAssemblyName {
    param([IO.FileInfo]$Project)

    $document = Read-XmlDocument -Path $Project.FullName
    $node = $document.SelectSingleNode("//*[local-name()='AssemblyName']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        return $Project.BaseName
    }

    return $node.InnerText.Trim()
}

function Get-Percentage {
    param([long]$Covered, [long]$Valid)

    if ($Valid -eq 0) {
        return 0.0
    }

    return 100.0 * $Covered / $Valid
}

function Write-Utf8Atomic {
    param([string]$Path, [string]$Content)

    $directory = Split-Path -Parent $Path
    [void][IO.Directory]::CreateDirectory($directory)
    $temporary = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, $Content, [Text.UTF8Encoding]::new($false, $true))
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

$repoRoot = Resolve-RepositoryRoot
$coverageRoot = Resolve-AbsolutePath -Root $repoRoot -Path $CoverageResultsDirectory
$fullResultsRoot = Resolve-AbsolutePath -Root $repoRoot -Path $FullTestResultsDirectory
$behaviorResultsRoot = Resolve-AbsolutePath -Root $repoRoot -Path $BehaviorTestResultsDirectory
$outputRoot = Resolve-AbsolutePath -Root $repoRoot -Path $OutputDirectory
$policyFullPath = Resolve-AbsolutePath -Root $repoRoot -Path $PolicyPath
$sourceRoot = Resolve-AbsolutePath -Root $repoRoot -Path $SourceProjectsRoot

$policy = Read-JsonHashtable -Path $policyFullPath -Description 'coverage policy'
if ($policy.schema -ne 'pixelengine.coverage-policy/v1') {
    throw "不支持的 coverage policy schema：$($policy.schema)"
}
if ([string]$policy.baselineCommit -notmatch '^[0-9a-f]{40}$') {
    throw "coverage policy baselineCommit 必须是完整 40 位小写 Git SHA：$($policy.baselineCommit)"
}

$classification = $policy.testClassification
$expectedTestProjects = [int]$classification.expectedTestProjectCount
$disciplineSuffix = [string]$classification.sourceDisciplineClassSuffix
if ($expectedTestProjects -le 0 -or
    [int]$classification.expectedUniqueCoverageReportCount -le 0 -or
    [string]::IsNullOrWhiteSpace($disciplineSuffix) -or
    [string]$classification.behaviorFilter -cne "FullyQualifiedName!~$disciplineSuffix") {
    throw 'coverage policy 的测试分类边界无效。'
}

$policyAssemblies = @($policy.assemblies)
if ($policyAssemblies.Count -eq 0) {
    throw 'coverage policy 未声明程序集。'
}

$assemblyPolicies = @{}
foreach ($entry in $policyAssemblies) {
    $name = [string]$entry.name
    if ([string]::IsNullOrWhiteSpace($name) -or $assemblyPolicies.ContainsKey($name)) {
        throw "coverage policy 程序集名称为空或重复：$name"
    }

    $sourceDirectory = [string]$entry.sourceDirectory
    if ([string]::IsNullOrWhiteSpace($sourceDirectory) -or
        [IO.Path]::IsPathRooted($sourceDirectory) -or
        [IO.Path]::GetFileName($sourceDirectory) -cne $sourceDirectory) {
        throw "coverage policy sourceDirectory 必须是单一相对目录名：$name => $sourceDirectory"
    }

    $baseline = $entry.baseline
    $minimum = $entry.minimum
    $baselineLinesCovered = [int]$baseline.linesCovered
    $baselineLinesValid = [int]$baseline.linesValid
    $baselineBranchesCovered = [int]$baseline.branchesCovered
    $baselineBranchesValid = [int]$baseline.branchesValid
    $minimumLinePercent = [double]$minimum.linePercent
    $minimumBranchPercent = [double]$minimum.branchPercent
    if ($baselineLinesCovered -le 0 -or
        $baselineLinesCovered -gt $baselineLinesValid -or
        $baselineBranchesCovered -le 0 -or
        $baselineBranchesCovered -gt $baselineBranchesValid -or
        [int]$minimum.linesValid -ne $baselineLinesValid -or
        [int]$minimum.branchesValid -ne $baselineBranchesValid -or
        $minimumLinePercent -le 0 -or
        $minimumLinePercent -gt (Get-Percentage -Covered $baselineLinesCovered -Valid $baselineLinesValid) -or
        $minimumBranchPercent -le 0 -or
        $minimumBranchPercent -gt (Get-Percentage -Covered $baselineBranchesCovered -Valid $baselineBranchesValid)) {
        throw "coverage policy baseline/minimum 无效：$name"
    }

    $assemblyPolicies[$name] = $entry
}

$actualProjects = @(
    Get-ChildItem -LiteralPath $sourceRoot -Directory |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' -File } |
        ForEach-Object { Get-ProjectAssemblyName -Project $_ } |
        Sort-Object -Unique
)
$expectedProjects = @($assemblyPolicies.Keys | Sort-Object)
if (($actualProjects -join "`n") -cne ($expectedProjects -join "`n")) {
    throw "coverage policy 与 src 程序集集合不一致。expected=$($expectedProjects -join ',') actual=$($actualProjects -join ',')"
}
foreach ($entry in $policyAssemblies) {
    $projectDirectory = [IO.Path]::GetFullPath((Join-Path $sourceRoot ([string]$entry.sourceDirectory)))
    Assert-ContainedPath -Parent $sourceRoot -Child $projectDirectory -Description "coverage policy sourceDirectory $($entry.name)"
    if (-not (Test-Path -LiteralPath $projectDirectory -PathType Container)) {
        throw "coverage policy sourceDirectory 不存在：$($entry.name) => $projectDirectory"
    }

    $projectAssemblies = @(
        Get-ChildItem -LiteralPath $projectDirectory -Filter '*.csproj' -File |
            ForEach-Object { Get-ProjectAssemblyName -Project $_ }
    )
    if ($projectAssemblies.Count -ne 1 -or $projectAssemblies[0] -cne [string]$entry.name) {
        throw "coverage policy sourceDirectory 与程序集不匹配：$($entry.name) => $($projectAssemblies -join ',')"
    }
}

$fullTests = Read-TestLayerSummary -ResultsDirectory $fullResultsRoot -DisciplineSuffix $disciplineSuffix -ExpectedTrxCount $expectedTestProjects
$behaviorTests = Read-TestLayerSummary -ResultsDirectory $behaviorResultsRoot -DisciplineSuffix $disciplineSuffix -ExpectedTrxCount $expectedTestProjects -RequireBehaviorOnly

$violations = [Collections.Generic.List[string]]::new()
if ($fullTests.behavior.failed -ne 0 -or $fullTests.sourceDiscipline.failed -ne 0 -or $behaviorTests.behavior.failed -ne 0) {
    $violations.Add('测试结果包含失败、错误或非允许终态。')
}
if ($fullTests.behavior.passed -lt [int]$classification.minimumBehaviorPassed) {
    $violations.Add("行为测试通过数低于门槛：actual=$($fullTests.behavior.passed) minimum=$($classification.minimumBehaviorPassed)")
}
if ($fullTests.behavior.notExecuted -gt [int]$classification.maximumBehaviorNotExecuted) {
    $violations.Add("行为测试未执行数超过门槛：actual=$($fullTests.behavior.notExecuted) maximum=$($classification.maximumBehaviorNotExecuted)")
}
if ($fullTests.sourceDiscipline.passed -lt [int]$classification.minimumSourceDisciplinePassed) {
    $violations.Add("源码纪律测试通过数低于门槛：actual=$($fullTests.sourceDiscipline.passed) minimum=$($classification.minimumSourceDisciplinePassed)")
}
if ($fullTests.sourceDiscipline.notExecuted -gt [int]$classification.maximumSourceDisciplineNotExecuted) {
    $violations.Add("源码纪律测试未执行数超过门槛：actual=$($fullTests.sourceDiscipline.notExecuted) maximum=$($classification.maximumSourceDisciplineNotExecuted)")
}
foreach ($key in @('total', 'passed', 'notExecuted', 'failed')) {
    if ($fullTests.behavior[$key] -ne $behaviorTests.behavior[$key]) {
        $violations.Add("行为 coverage 重跑与完整测试计数不一致：field=$key full=$($fullTests.behavior[$key]) coverage=$($behaviorTests.behavior[$key])")
    }
}

$physicalReports = @(Get-ChildItem -LiteralPath $coverageRoot -Recurse -Filter 'coverage.json' -File)
$reportByHash = @{}
foreach ($report in $physicalReports) {
    $hash = (Get-FileHash -LiteralPath $report.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $reportByHash.ContainsKey($hash)) {
        $reportByHash[$hash] = $report
    }
}
$coverageReports = @($reportByHash.Values)
if ($coverageReports.Count -ne [int]$classification.expectedUniqueCoverageReportCount) {
    throw "唯一 coverage.json 数量不匹配：expected=$($classification.expectedUniqueCoverageReportCount) actual=$($coverageReports.Count) physical=$($physicalReports.Count)"
}

$coverageStats = @{}
foreach ($name in $assemblyPolicies.Keys) {
    $coverageStats[$name] = [ordered]@{ lines = @{}; branches = @{}; files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
}
$excludedGeneratedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($report in $coverageReports) {
    $coverage = Read-JsonHashtable -Path $report.FullName -Description 'Coverlet JSON report'
    foreach ($moduleEntry in $coverage.GetEnumerator()) {
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension([string]$moduleEntry.Key)
        if (-not $assemblyPolicies.ContainsKey($assemblyName)) {
            continue
        }

        $sourceDirectory = [string]$assemblyPolicies[$assemblyName].sourceDirectory
        $assemblySourceRoot = [IO.Path]::GetFullPath((Join-Path $sourceRoot $sourceDirectory))
        foreach ($fileEntry in $moduleEntry.Value.GetEnumerator()) {
            $sourcePath = [IO.Path]::GetFullPath([string]$fileEntry.Key)
            if ($sourcePath -match '[\\/](bin|obj)[\\/]') {
                [void]$excludedGeneratedFiles.Add($sourcePath)
                continue
            }

            Assert-ContainedPath -Parent $assemblySourceRoot -Child $sourcePath -Description "coverage 源文件 $assemblyName"
            $relativePath = [IO.Path]::GetRelativePath($assemblySourceRoot, $sourcePath).Replace('\', '/').ToLowerInvariant()
            [void]$coverageStats[$assemblyName].files.Add($relativePath)

            foreach ($classEntry in $fileEntry.Value.GetEnumerator()) {
                foreach ($methodEntry in $classEntry.Value.GetEnumerator()) {
                    $methodData = $methodEntry.Value
                    foreach ($lineEntry in $methodData.Lines.GetEnumerator()) {
                        $lineKey = "$relativePath|$($lineEntry.Key)"
                        if (-not $coverageStats[$assemblyName].lines.ContainsKey($lineKey)) {
                            $coverageStats[$assemblyName].lines[$lineKey] = 0L
                        }
                        $coverageStats[$assemblyName].lines[$lineKey] += [long]$lineEntry.Value
                    }

                    foreach ($branch in @($methodData.Branches)) {
                        $branchKey = "$relativePath|$($classEntry.Key)|$($methodEntry.Key)|$($branch.Offset)|$($branch.EndOffset)|$($branch.Path)|$($branch.Ordinal)"
                        if (-not $coverageStats[$assemblyName].branches.ContainsKey($branchKey)) {
                            $coverageStats[$assemblyName].branches[$branchKey] = 0L
                        }
                        $coverageStats[$assemblyName].branches[$branchKey] += [long]$branch.Hits
                    }
                }
            }
        }
    }
}

$assemblyResults = [Collections.Generic.List[object]]::new()
foreach ($name in $assemblyPolicies.Keys | Sort-Object) {
    $entry = $assemblyPolicies[$name]
    $stats = $coverageStats[$name]
    $linesValid = $stats.lines.Count
    $linesCovered = @($stats.lines.Values | Where-Object { $_ -gt 0 }).Count
    $branchesValid = $stats.branches.Count
    $branchesCovered = @($stats.branches.Values | Where-Object { $_ -gt 0 }).Count
    $linePercent = Get-Percentage -Covered $linesCovered -Valid $linesValid
    $branchPercent = Get-Percentage -Covered $branchesCovered -Valid $branchesValid
    $minimum = $entry.minimum

    if ($stats.files.Count -eq 0) {
        $violations.Add("程序集没有手写源码 coverage：$name")
    }
    if ($linesValid -lt [int]$minimum.linesValid) {
        $violations.Add("程序集可观测行数低于门槛：$name actual=$linesValid minimum=$($minimum.linesValid)")
    }
    if ($branchesValid -lt [int]$minimum.branchesValid) {
        $violations.Add("程序集可观测分支数低于门槛：$name actual=$branchesValid minimum=$($minimum.branchesValid)")
    }
    if ($linePercent -lt [double]$minimum.linePercent) {
        $violations.Add("程序集行覆盖率低于门槛：$name actual=$($linePercent.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) minimum=$($minimum.linePercent)")
    }
    if ($branchPercent -lt [double]$minimum.branchPercent) {
        $violations.Add("程序集分支覆盖率低于门槛：$name actual=$($branchPercent.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) minimum=$($minimum.branchPercent)")
    }

    $assemblyResults.Add([ordered]@{
        name = $name
        sourceFileCount = $stats.files.Count
        linesCovered = $linesCovered
        linesValid = $linesValid
        linePercent = [Math]::Round($linePercent, 2)
        minimumLinePercent = [double]$minimum.linePercent
        branchesCovered = $branchesCovered
        branchesValid = $branchesValid
        branchPercent = [Math]::Round($branchPercent, 2)
        minimumBranchPercent = [double]$minimum.branchPercent
        passed = $linePercent -ge [double]$minimum.linePercent -and
            $branchPercent -ge [double]$minimum.branchPercent -and
            $linesValid -ge [int]$minimum.linesValid -and
            $branchesValid -ge [int]$minimum.branchesValid
    })
}

if ([string]::IsNullOrWhiteSpace($CommitSha)) {
    $CommitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw '无法读取当前 Git commit。'
    }
}
if ($CommitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "CommitSha 必须是完整 40 位 Git SHA：$CommitSha"
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = 'local-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture)
}

$policyHash = (Get-FileHash -LiteralPath $policyFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
$passed = $violations.Count -eq 0
$report = [ordered]@{
    schema = 'pixelengine.coverage-report/v1'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    runId = $RunId
    gitCommit = $CommitSha.ToLowerInvariant()
    policy = [ordered]@{
        path = [IO.Path]::GetRelativePath($repoRoot, $policyFullPath).Replace('\', '/')
        sha256 = $policyHash
        baselineCommit = [string]$policy.baselineCommit
    }
    testLayers = [ordered]@{
        behavior = $fullTests.behavior
        sourceDiscipline = $fullTests.sourceDiscipline
        behaviorCoverageRun = $behaviorTests.behavior
    }
    rawCoverage = [ordered]@{
        physicalReportCount = $physicalReports.Count
        uniqueReportCount = $coverageReports.Count
        excludedGeneratedFileCount = $excludedGeneratedFiles.Count
    }
    assemblies = $assemblyResults
    passed = $passed
    violations = @($violations)
}

$jsonPath = Join-Path $outputRoot 'coverage-summary.json'
$markdownPath = Join-Path $outputRoot 'coverage-summary.md'
$json = $report | ConvertTo-Json -Depth 12
Write-Utf8Atomic -Path $jsonPath -Content ($json + "`n")

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# PixelEngine behavior coverage')
$lines.Add('')
$lines.Add('| Key | Value |')
$lines.Add('|---|---|')
$lines.Add("| run_id | $RunId |")
$lines.Add("| git_commit | $($CommitSha.ToLowerInvariant()) |")
$lines.Add("| policy_sha256 | $policyHash |")
$lines.Add("| behavior | $($fullTests.behavior.passed) passed / $($fullTests.behavior.notExecuted) not executed / $($fullTests.behavior.failed) failed |")
$lines.Add("| source_discipline | $($fullTests.sourceDiscipline.passed) passed / $($fullTests.sourceDiscipline.notExecuted) not executed / $($fullTests.sourceDiscipline.failed) failed |")
$lines.Add("| unique_coverage_reports | $($coverageReports.Count) |")
$lines.Add("| generated_files_excluded | $($excludedGeneratedFiles.Count) |")
$conclusion = if ($passed) { 'success' } else { 'failure' }
$lines.Add("| conclusion | $conclusion |")
$lines.Add('')
$lines.Add('| Assembly | Lines | Line % / min | Branches | Branch % / min | Result |')
$lines.Add('|---|---:|---:|---:|---:|---|')
foreach ($assembly in $assemblyResults) {
    $assemblyConclusion = if ($assembly.passed) { 'pass' } else { 'fail' }
    $lines.Add("| $($assembly.name) | $($assembly.linesCovered)/$($assembly.linesValid) | $($assembly.linePercent.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) / $($assembly.minimumLinePercent.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) | $($assembly.branchesCovered)/$($assembly.branchesValid) | $($assembly.branchPercent.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) / $($assembly.minimumBranchPercent.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) | $assemblyConclusion |")
}
if ($violations.Count -gt 0) {
    $lines.Add('')
    $lines.Add('## Violations')
    $lines.Add('')
    foreach ($violation in $violations) {
        $lines.Add("- $violation")
    }
}
Write-Utf8Atomic -Path $markdownPath -Content (($lines -join "`n") + "`n")

Write-Host "coverage_report json=$jsonPath markdown=$markdownPath passed=$passed assemblies=$($assemblyResults.Count) behavior=$($fullTests.behavior.passed) discipline=$($fullTests.sourceDiscipline.passed)"
if (-not $passed) {
    throw "Coverage gate failed with $($violations.Count) violation(s)."
}
