[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ResultsDirectory = 'artifacts/native-smoke'
)

$ErrorActionPreference = 'Stop'

$requiredEnvironment = @(
    'PIXELENGINE_RENDERING_GL_SMOKE',
    'PIXELENGINE_RENDERING_ANGLE_SMOKE'
)

foreach ($name in $requiredEnvironment) {
    if ([string]::Equals([Environment]::GetEnvironmentVariable($name), '1', [StringComparison]::Ordinal) -eq $false) {
        throw "native-smoke 要求显式启用环境变量 $name=1；当前 job 不允许静默跳过。"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultsRoot = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $repoRoot $ResultsDirectory
}
$runName = 'run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ', [Globalization.CultureInfo]::InvariantCulture) + '-' + $PID
$runDirectory = Join-Path $resultsRoot $runName
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

$projects = @(
    [ordered]@{
        name = 'rendering'
        path = 'tests/PixelEngine.Rendering.Tests/PixelEngine.Rendering.Tests.csproj'
    },
    [ordered]@{
        name = 'ui'
        path = 'tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj'
    },
    [ordered]@{
        name = 'hosting'
        path = 'tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj'
    },
    [ordered]@{
        name = 'demo'
        path = 'tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj'
    }
)

$projectReports = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

Push-Location $repoRoot
try {
    foreach ($project in $projects) {
        $logPath = Join-Path $runDirectory "$($project.name).log"
        $trxName = "$($project.name).trx"
        $arguments = @(
            'test',
            $project.path,
            '--configuration',
            $Configuration,
            '--no-build',
            '--no-restore',
            '--filter',
            'Category=NativeSmoke',
            '--logger',
            "trx;LogFileName=$trxName",
            '--results-directory',
            $runDirectory
        )

        $exitCode = 1
        $output = @()
        try {
            $output = @(& dotnet @arguments 2>&1)
            $exitCode = $LASTEXITCODE
        } catch {
            $output = @($_.Exception.Message)
            $failures.Add("$($project.name): dotnet test 启动失败：$($_.Exception.Message)")
        }

        $output | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $logPath -Encoding UTF8

        $trxPath = Join-Path $runDirectory $trxName
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            $failures.Add("$($project.name): 未生成 TRX，无法证明实际执行了 smoke 用例。exit=$exitCode")
            $projectReports.Add([pscustomobject]@{
                name = $project.name
                path = $project.path
                log = $logPath
                trx = $null
                total = 0
                passed = 0
                failed = 0
                skipped = 0
                notExecuted = 0
                exitCode = $exitCode
            })
            continue
        }

        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $unitTestResults = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
        $outcomes = @($unitTestResults | ForEach-Object { [string]$_.outcome })
        $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")

        $total = @($outcomes).Count
        $passed = @($outcomes | Where-Object { $_ -eq 'Passed' }).Count
        $failed = @($outcomes | Where-Object { $_ -in @('Failed', 'Error') }).Count
        $skipped = @($outcomes | Where-Object { $_ -eq 'Skipped' }).Count
        $notExecuted = @($outcomes | Where-Object { $_ -eq 'NotExecuted' }).Count

        if ($null -ne $counters) {
            $total = [int]$counters.total
            $passed = [int]$counters.passed
            $failed = [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
            $skipped = [int]$counters.skipped
            $notExecuted = [int]$counters.notExecuted
        }

        if ($exitCode -ne 0) {
            $failures.Add("$($project.name): dotnet test exit=$exitCode")
        }
        if ($failed -gt 0) {
            $failures.Add("$($project.name): TRX 报告 failed=$failed")
        }

        $projectReports.Add([pscustomobject]@{
            name = $project.name
            path = $project.path
            log = $logPath
            trx = $trxPath
            total = $total
            passed = $passed
            failed = $failed
            skipped = $skipped
            notExecuted = $notExecuted
            exitCode = $exitCode
        })
        Write-Host "[$($project.name)] total=$total passed=$passed failed=$failed skipped=$skipped notExecuted=$notExecuted exit=$exitCode"
    }
} finally {
    Pop-Location
}

$totalTests = [int](($projectReports | Measure-Object -Property total -Sum).Sum)
$passedTests = [int](($projectReports | Measure-Object -Property passed -Sum).Sum)
$failedTests = [int](($projectReports | Measure-Object -Property failed -Sum).Sum)
$skippedTests = [int](($projectReports | Measure-Object -Property skipped -Sum).Sum)
$notExecutedTests = [int](($projectReports | Measure-Object -Property notExecuted -Sum).Sum)

$summary = [ordered]@{
    schemaVersion = 1
    configuration = $Configuration
    requiredEnvironment = [ordered]@{
        PIXELENGINE_RENDERING_GL_SMOKE = [Environment]::GetEnvironmentVariable('PIXELENGINE_RENDERING_GL_SMOKE')
        PIXELENGINE_RENDERING_ANGLE_SMOKE = [Environment]::GetEnvironmentVariable('PIXELENGINE_RENDERING_ANGLE_SMOKE')
    }
    runDirectory = $runDirectory
    totalTests = $totalTests
    passedTests = $passedTests
    failedTests = $failedTests
    skippedTests = $skippedTests
    notExecutedTests = $notExecutedTests
    projects = @($projectReports)
    failures = @($failures)
}
$summaryPath = Join-Path $runDirectory 'summary.json'
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add('# Native smoke report')
$markdown.Add('')
$markdown.Add("- Configuration: ``$Configuration``")
$markdown.Add('- Required environment: `PIXELENGINE_RENDERING_GL_SMOKE=1`, `PIXELENGINE_RENDERING_ANGLE_SMOKE=1`')
$markdown.Add("- Total: $totalTests; passed: $passedTests; failed: $failedTests; skipped: $skippedTests; not executed: $notExecutedTests")
$markdown.Add('')
$markdown.Add('| Project | Total | Passed | Failed | Skipped | NotExecuted | Exit |')
$markdown.Add('|---|---:|---:|---:|---:|---:|---:|')
foreach ($report in $projectReports) {
    $markdown.Add("| $($report.name) | $($report.total) | $($report.passed) | $($report.failed) | $($report.skipped) | $($report.notExecuted) | $($report.exitCode) |")
}
$markdown.Add('')
$markdown.Add("Summary JSON: ``$summaryPath``")
$markdownPath = Join-Path $runDirectory 'summary.md'
$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Host "Native smoke summary: total=$totalTests passed=$passedTests failed=$failedTests skipped=$skippedTests notExecuted=$notExecutedTests"

if ($totalTests -le 0) {
    throw 'native-smoke 未报告任何测试用例；禁止将空执行视为通过。'
}
if ($failures.Count -gt 0) {
    throw ("native-smoke 失败：`n" + ($failures -join "`n"))
}
