[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsDirectory,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [string]$Rid,

    [Parameter(Mandatory)]
    [string]$Runner,

    [Parameter(Mandatory)]
    [string]$RunId,

    [Parameter(Mandatory)]
    [string]$CommitSha,

    [Parameter(Mandatory)]
    [ValidateSet('true', 'false')]
    [string]$BuildOnly,

    [Parameter(Mandatory)]
    [ValidateSet('success', 'failure', 'cancelled', 'skipped')]
    [string]$TestStepOutcome,

    [Parameter(Mandatory)]
    [ValidateSet('success', 'failure', 'cancelled', 'skipped')]
    [string]$JobStatus,

    [ValidateRange(1, [int]::MaxValue)]
    [int]$MinimumTotal = 1,

    [string]$ExpectedTestProjectsRoot = ''
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
    param(
        [string]$Root,
        [string]$Path
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function ConvertTo-MarkdownCell {
    param([AllowEmptyString()][string]$Value)

    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Get-ExpectedTestAssemblies {
    param([string]$ProjectsRoot)

    if (-not (Test-Path -LiteralPath $ProjectsRoot -PathType Container)) {
        throw "测试项目根目录不存在：$ProjectsRoot"
    }

    $assemblies = [System.Collections.Generic.List[string]]::new()
    foreach ($directory in Get-ChildItem -LiteralPath $ProjectsRoot -Directory | Sort-Object Name) {
        $projects = @(Get-ChildItem -LiteralPath $directory.FullName -File -Filter '*.csproj')
        foreach ($project in $projects) {
            [xml]$document = Get-Content -LiteralPath $project.FullName -Raw
            $assemblyNode = $document.SelectSingleNode("//*[local-name()='AssemblyName']")
            $assemblyName = if ($null -eq $assemblyNode -or [string]::IsNullOrWhiteSpace($assemblyNode.InnerText)) {
                $project.BaseName
            } else {
                $assemblyNode.InnerText.Trim()
            }

            if (-not $assemblyName.EndsWith('.Tests', [StringComparison]::Ordinal)) {
                throw "测试项目程序集必须以 .Tests 结尾：$($project.FullName) => $assemblyName"
            }

            $assemblies.Add($assemblyName)
        }
    }

    $duplicates = @($assemblies | Group-Object | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) {
        throw "测试项目程序集名称重复：$($duplicates.Name -join ', ')"
    }
    if ($assemblies.Count -eq 0) {
        throw "测试项目根目录没有发现 *.csproj：$ProjectsRoot"
    }

    return @($assemblies | Sort-Object)
}

function Get-RequiredCounterValue {
    param(
        [System.Xml.XmlElement]$Counters,
        [string]$Name,
        [string]$TrxPath
    )

    $attribute = $Counters.Attributes[$Name]
    if ($null -eq $attribute -or [string]::IsNullOrWhiteSpace($attribute.Value)) {
        throw "TRX Counters 缺少 $Name：$TrxPath"
    }

    $value = 0
    if (-not [int]::TryParse(
            $attribute.Value,
            [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$value) -or
        $value -lt 0) {
        throw "TRX Counters.$Name 不是非负整数：$TrxPath value=$($attribute.Value)"
    }

    return $value
}

function Get-OutcomeCounterName {
    param(
        [string]$Outcome,
        [string]$TrxPath
    )

    switch -CaseSensitive ($Outcome) {
        'Passed' { 'passed' }
        'Failed' { 'failed' }
        'Error' { 'error' }
        'Timeout' { 'timeout' }
        'Aborted' { 'aborted' }
        'Inconclusive' { 'inconclusive' }
        'PassedButRunAborted' { 'passedButRunAborted' }
        'NotRunnable' { 'notRunnable' }
        'NotExecuted' { 'notExecuted' }
        'Skipped' { 'notExecuted' }
        'Disconnected' { 'disconnected' }
        'Warning' { 'warning' }
        'Completed' { 'completed' }
        'InProgress' { 'inProgress' }
        'Pending' { 'pending' }
        default { throw "TRX 包含未知测试 outcome：$TrxPath outcome=$Outcome" }
    }
}

function Read-TrxReport {
    param([string]$Path)

    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $testRun = $document.SelectSingleNode("/*[local-name()='TestRun']")
    if ($null -eq $testRun) {
        throw "TRX 缺少 TestRun 根节点：$Path"
    }

    $runGuid = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$testRun.id, [ref]$runGuid) -or $runGuid -eq [Guid]::Empty) {
        throw "TRX TestRun.id 不是有效非空 GUID：$Path"
    }

    $countersNode = $document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $countersNode -or $countersNode -isnot [System.Xml.XmlElement]) {
        throw "TRX 缺少 ResultSummary/Counters：$Path"
    }

    [System.Xml.XmlElement]$counters = $countersNode
    $counterNames = @(
        'total',
        'executed',
        'passed',
        'failed',
        'error',
        'timeout',
        'aborted',
        'inconclusive',
        'passedButRunAborted',
        'notRunnable',
        'notExecuted',
        'disconnected',
        'warning',
        'completed',
        'inProgress',
        'pending'
    )
    $values = @{}
    foreach ($counterName in $counterNames) {
        $values[$counterName] = Get-RequiredCounterValue -Counters $counters -Name $counterName -TrxPath $Path
    }

    $resultNodes = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($resultNodes.Count -ne [int]$values.total) {
        throw "TRX total 与 UnitTestResult 数量不一致：$Path counters.total=$($values.total) results=$($resultNodes.Count)"
    }

    $outcomeCounts = @{}
    foreach ($counterName in $counterNames | Where-Object { $_ -notin @('total', 'executed') }) {
        $outcomeCounts[$counterName] = 0
    }

    $executionIds = [System.Collections.Generic.HashSet[Guid]]::new()
    $executedFromResults = 0
    foreach ($result in $resultNodes) {
        $executionId = [Guid]::Empty
        if (-not [Guid]::TryParse([string]$result.executionId, [ref]$executionId) -or
            $executionId -eq [Guid]::Empty -or
            -not $executionIds.Add($executionId)) {
            throw "TRX UnitTestResult.executionId 缺失、无效或重复：$Path"
        }

        $outcome = [string]$result.outcome
        $counterName = Get-OutcomeCounterName -Outcome $outcome -TrxPath $Path
        $outcomeCounts[$counterName] = [int]$outcomeCounts[$counterName] + 1
        if ($counterName -notin @('notExecuted', 'notRunnable', 'inProgress', 'pending')) {
            $executedFromResults++
        }
    }

    foreach ($counterName in $outcomeCounts.Keys) {
        # xUnit VSTest adapter 3.x 会把 skipped 写成 UnitTestResult outcome=NotExecuted，
        # 同时保持 Counters.notExecuted=0；逐条结果才是 skipped 的权威来源。
        # 只允许这一种已知差异，任何其它非零错配仍 fail-closed。
        if ($counterName -eq 'notExecuted' -and
            [int]$values[$counterName] -eq 0 -and
            [int]$outcomeCounts[$counterName] -gt 0) {
            continue
        }

        if ([int]$values[$counterName] -ne [int]$outcomeCounts[$counterName]) {
            throw "TRX Counters.$counterName 与逐条 outcome 不一致：$Path counter=$($values[$counterName]) results=$($outcomeCounts[$counterName])"
        }
    }
    if ([int]$values.executed -ne $executedFromResults) {
        throw "TRX Counters.executed 与逐条 outcome 不一致：$Path counter=$($values.executed) results=$executedFromResults"
    }

    $testMethods = @($document.SelectNodes("//*[local-name()='UnitTest']/*[local-name()='TestMethod']"))
    $assemblies = @(
        $testMethods |
            ForEach-Object { [IO.Path]::GetFileNameWithoutExtension([string]$_.codeBase) } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    if ($assemblies.Count -ne 1) {
        throw "TRX 必须且只能对应一个测试程序集：$Path actual=$($assemblies -join ',')"
    }

    return [pscustomobject]@{
        Path = $Path
        RunId = $runGuid
        Assembly = $assemblies[0]
        Total = [int]$values.total
        Executed = [int]$values.executed
        Passed = [int]$values.passed
        Failed = [int]$values.failed
        Error = [int]$values.error
        Timeout = [int]$values.timeout
        Aborted = [int]$values.aborted
        Inconclusive = [int]$values.inconclusive
        PassedButRunAborted = [int]$values.passedButRunAborted
        NotRunnable = [int]$values.notRunnable
        NotExecuted = [int]$outcomeCounts.notExecuted
        Disconnected = [int]$values.disconnected
        Warning = [int]$values.warning
        Completed = [int]$values.completed
        InProgress = [int]$values.inProgress
        Pending = [int]$values.pending
    }
}

function Add-Failure {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string]$Message
    )

    $Failures.Add($Message)
    Write-Warning $Message
}

function Get-ReportSum {
    param(
        [System.Collections.Generic.List[object]]$Reports,
        [string]$Property
    )

    if ($Reports.Count -eq 0) {
        return 0
    }

    return [int](($Reports | Measure-Object -Property $Property -Sum).Sum)
}

$root = Resolve-RepositoryRoot
$resultsRoot = Resolve-AbsolutePath -Root $root -Path $ResultsDirectory
$outputFull = Resolve-AbsolutePath -Root $root -Path $OutputPath
$projectsRoot = if ([string]::IsNullOrWhiteSpace($ExpectedTestProjectsRoot)) {
    Join-Path $root 'tests'
} else {
    Resolve-AbsolutePath -Root $root -Path $ExpectedTestProjectsRoot
}

if ($Rid -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)+$') {
    throw "RID 格式无效：$Rid"
}
if ([string]::IsNullOrWhiteSpace($Runner) -or $Runner -match '[|\r\n]') {
    throw 'Runner 不能为空或包含 Markdown/换行控制字符。'
}
if ($RunId -notmatch '^\d+$') {
    throw "GitHub run id 必须是十进制数字：$RunId"
}
if ($CommitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Git commit SHA 必须是 40 位十六进制：$CommitSha"
}

$isBuildOnly = [string]::Equals($BuildOnly, 'true', [StringComparison]::Ordinal)
$expectedAssemblies = @(Get-ExpectedTestAssemblies -ProjectsRoot $projectsRoot)
$trxFiles = @(
    if (Test-Path -LiteralPath $resultsRoot -PathType Container) {
        Get-ChildItem -LiteralPath $resultsRoot -Recurse -File -Filter '*.trx' | Sort-Object FullName
    }
)

$failures = [System.Collections.Generic.List[string]]::new()
$reports = [System.Collections.Generic.List[object]]::new()

if (-not [string]::Equals($JobStatus, 'success', [StringComparison]::Ordinal)) {
    Add-Failure -Failures $failures -Message "job status 不是 success：$JobStatus"
}

if ($isBuildOnly) {
    if (-not [string]::Equals($TestStepOutcome, 'skipped', [StringComparison]::Ordinal)) {
        Add-Failure -Failures $failures -Message "build-only 的 test step 必须是 skipped：$TestStepOutcome"
    }
    if ($trxFiles.Count -ne 0) {
        Add-Failure -Failures $failures -Message "build-only 不得携带 TRX：发现 $($trxFiles.Count) 个"
    }
} else {
    if (-not [string]::Equals($TestStepOutcome, 'success', [StringComparison]::Ordinal)) {
        Add-Failure -Failures $failures -Message "非 build-only 的 test step 必须是 success：$TestStepOutcome"
    }
    if ($trxFiles.Count -eq 0) {
        Add-Failure -Failures $failures -Message "非 build-only 未生成任何 TRX：$resultsRoot"
    }
    if ($trxFiles.Count -ne $expectedAssemblies.Count) {
        Add-Failure -Failures $failures -Message "TRX 数量与测试项目数量不一致：expected=$($expectedAssemblies.Count) actual=$($trxFiles.Count)"
    }

    foreach ($trxFile in $trxFiles) {
        try {
            $reports.Add((Read-TrxReport -Path $trxFile.FullName))
        } catch {
            Add-Failure -Failures $failures -Message $_.Exception.Message
        }
    }

    $duplicateRunIds = @($reports | Group-Object RunId | Where-Object Count -gt 1)
    if ($duplicateRunIds.Count -gt 0) {
        Add-Failure -Failures $failures -Message "TRX TestRun.id 重复：$($duplicateRunIds.Name -join ', ')"
    }

    $duplicateAssemblies = @($reports | Group-Object Assembly | Where-Object Count -gt 1)
    if ($duplicateAssemblies.Count -gt 0) {
        Add-Failure -Failures $failures -Message "同一测试程序集出现多个 TRX：$($duplicateAssemblies.Name -join ', ')"
    }

    $actualAssemblies = @($reports | ForEach-Object Assembly | Sort-Object -Unique)
    $missingAssemblies = @($expectedAssemblies | Where-Object { $_ -notin $actualAssemblies })
    $unknownAssemblies = @($actualAssemblies | Where-Object { $_ -notin $expectedAssemblies })
    if ($missingAssemblies.Count -gt 0) {
        Add-Failure -Failures $failures -Message "缺少测试程序集 TRX：$($missingAssemblies -join ', ')"
    }
    if ($unknownAssemblies.Count -gt 0) {
        Add-Failure -Failures $failures -Message "TRX 包含未知测试程序集：$($unknownAssemblies -join ', ')"
    }
}

$total = Get-ReportSum -Reports $reports -Property Total
$executed = Get-ReportSum -Reports $reports -Property Executed
$passed = Get-ReportSum -Reports $reports -Property Passed
$failed = Get-ReportSum -Reports $reports -Property Failed
$errorCount = Get-ReportSum -Reports $reports -Property Error
$timeout = Get-ReportSum -Reports $reports -Property Timeout
$aborted = Get-ReportSum -Reports $reports -Property Aborted
$inconclusive = Get-ReportSum -Reports $reports -Property Inconclusive
$passedButRunAborted = Get-ReportSum -Reports $reports -Property PassedButRunAborted
$notRunnable = Get-ReportSum -Reports $reports -Property NotRunnable
$notExecuted = Get-ReportSum -Reports $reports -Property NotExecuted
$disconnected = Get-ReportSum -Reports $reports -Property Disconnected
$warningCount = Get-ReportSum -Reports $reports -Property Warning
$completed = Get-ReportSum -Reports $reports -Property Completed
$inProgress = Get-ReportSum -Reports $reports -Property InProgress
$pending = Get-ReportSum -Reports $reports -Property Pending

if (-not $isBuildOnly) {
    if ($total -le 0) {
        Add-Failure -Failures $failures -Message '非 build-only 的 TRX 聚合测试总数必须大于 0。'
    }
    if ($total -lt $MinimumTotal) {
        Add-Failure -Failures $failures -Message "测试总数低于最低门槛：minimum=$MinimumTotal actual=$total"
    }

    $unsuccessful = $failed + $errorCount + $timeout + $aborted + $inconclusive + $passedButRunAborted + $notRunnable + $disconnected + $warningCount + $completed + $inProgress + $pending
    if ($unsuccessful -ne 0) {
        Add-Failure -Failures $failures -Message "TRX 存在未成功测试状态：count=$unsuccessful"
    }
}

$testsRan = -not $isBuildOnly
$effectiveConclusion = if ($failures.Count -eq 0) { 'success' } else { 'failure' }
$minimumMet = $isBuildOnly -or $total -ge $MinimumTotal
$outputDirectory = Split-Path -Parent $outputFull
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add("# CI build-test evidence: $Rid")
$markdown.Add('')
$markdown.Add('| Key | Value |')
$markdown.Add('|---|---|')
$markdown.Add("| rid | $(ConvertTo-MarkdownCell $Rid) |")
$markdown.Add("| runner | $(ConvertTo-MarkdownCell $Runner) |")
$markdown.Add("| build_only | $($isBuildOnly.ToString().ToLowerInvariant()) |")
$markdown.Add("| tests_ran | $($testsRan.ToString().ToLowerInvariant()) |")
$markdown.Add('| native_gpu_smoke_scope | separate_workflow |')
$markdown.Add('| native_gpu_smoke_executed | false |')
$markdown.Add("| run_id | $(ConvertTo-MarkdownCell $RunId) |")
$markdown.Add("| sha | $($CommitSha.ToLowerInvariant()) |")
$markdown.Add("| test_step_outcome | $TestStepOutcome |")
$markdown.Add("| job_status | $JobStatus |")
$markdown.Add("| expected_trx_count | $($expectedAssemblies.Count) |")
$markdown.Add("| trx_count | $($trxFiles.Count) |")
$markdown.Add("| minimum_test_total | $MinimumTotal |")
$markdown.Add("| minimum_test_total_met | $($minimumMet.ToString().ToLowerInvariant()) |")
$markdown.Add("| test_total | $total |")
$markdown.Add("| test_executed | $executed |")
$markdown.Add("| test_passed | $passed |")
$markdown.Add("| test_failed | $failed |")
$markdown.Add("| test_error | $errorCount |")
$markdown.Add("| test_timeout | $timeout |")
$markdown.Add("| test_aborted | $aborted |")
$markdown.Add("| test_inconclusive | $inconclusive |")
$markdown.Add("| test_passed_but_run_aborted | $passedButRunAborted |")
$markdown.Add("| test_not_runnable | $notRunnable |")
$markdown.Add("| test_not_executed | $notExecuted |")
$markdown.Add("| test_disconnected | $disconnected |")
$markdown.Add("| test_warning | $warningCount |")
$markdown.Add("| test_completed | $completed |")
$markdown.Add("| test_in_progress | $inProgress |")
$markdown.Add("| test_pending | $pending |")
$markdown.Add("| conclusion | $effectiveConclusion |")

if ($reports.Count -gt 0) {
    $markdown.Add('')
    $markdown.Add('| Assembly | TRX | Total | Executed | Passed | Unsuccessful | NotExecuted |')
    $markdown.Add('|---|---|---:|---:|---:|---:|---:|')
    foreach ($report in $reports | Sort-Object Assembly) {
        $relativePath = [IO.Path]::GetRelativePath($resultsRoot, $report.Path).Replace('\\', '/')
        $reportUnsuccessful = $report.Total - $report.Passed - $report.NotExecuted
        $markdown.Add("| $(ConvertTo-MarkdownCell $report.Assembly) | $(ConvertTo-MarkdownCell $relativePath) | $($report.Total) | $($report.Executed) | $($report.Passed) | $reportUnsuccessful | $($report.NotExecuted) |")
    }
}

if ($failures.Count -gt 0) {
    $markdown.Add('')
    $markdown.Add('## Fail-closed diagnostics')
    $markdown.Add('')
    foreach ($failure in $failures) {
        $markdown.Add("- $(ConvertTo-MarkdownCell $failure)")
    }
}

$markdown | Set-Content -LiteralPath $outputFull -Encoding UTF8
Write-Host "CI test summary: rid=$Rid buildOnly=$isBuildOnly trx=$($trxFiles.Count) total=$total passed=$passed notExecuted=$notExecuted conclusion=$effectiveConclusion report=$outputFull"

if ($failures.Count -gt 0) {
    throw ("CI test result summary failed:`n" + ($failures -join "`n"))
}
