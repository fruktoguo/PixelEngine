[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ResultsDirectory = 'artifacts/native-smoke',

    [string]$TestRunner = 'dotnet',

    [string]$ProjectManifestPath = ''
)

$ErrorActionPreference = 'Stop'

function Get-OptionalEnvironmentValue([string]$name) {
    $value = [Environment]::GetEnvironmentVariable($name)
    return [string]::IsNullOrWhiteSpace($value) ? $null : $value.Trim()
}

function Assert-SafeIdentityText([string]$name, [AllowNull()][string]$value, [int]$maximumLength) {
    if ($null -eq $value) {
        return
    }

    if ($value.Length -gt $maximumLength -or $value -match '[\x00-\x1F\x7F]') {
        throw "$name 包含控制字符或超过 $maximumLength 个字符。"
    }
}

function Get-NativeSmokeRunIdentity {
    $githubActionsText = Get-OptionalEnvironmentValue 'GITHUB_ACTIONS'
    if ($null -ne $githubActionsText -and
        -not $githubActionsText.Equals('true', [StringComparison]::OrdinalIgnoreCase) -and
        -not $githubActionsText.Equals('false', [StringComparison]::OrdinalIgnoreCase)) {
        throw "GITHUB_ACTIONS 必须为空、true 或 false；actual='$githubActionsText'。"
    }

    $isGitHubActions = $null -ne $githubActionsText -and
        $githubActionsText.Equals('true', [StringComparison]::OrdinalIgnoreCase)
    $runId = Get-OptionalEnvironmentValue 'GITHUB_RUN_ID'
    $runAttemptText = Get-OptionalEnvironmentValue 'GITHUB_RUN_ATTEMPT'
    $dispatchSha = Get-OptionalEnvironmentValue 'GITHUB_SHA'
    $candidateSha = Get-OptionalEnvironmentValue 'PIXELENGINE_CANDIDATE_SHA'
    $checkedOutSha = Get-OptionalEnvironmentValue 'PIXELENGINE_CHECKED_OUT_SHA'
    $runnerName = Get-OptionalEnvironmentValue 'RUNNER_NAME'
    $runnerOs = Get-OptionalEnvironmentValue 'RUNNER_OS'
    $runnerArch = Get-OptionalEnvironmentValue 'RUNNER_ARCH'
    $imageOs = Get-OptionalEnvironmentValue 'ImageOS'
    $imageVersion = Get-OptionalEnvironmentValue 'ImageVersion'

    if ($null -ne $runId -and $runId -notmatch '^[1-9][0-9]*$') {
        throw "GITHUB_RUN_ID 必须为正十进制整数；actual='$runId'。"
    }

    $runAttempt = $null
    if ($null -ne $runAttemptText) {
        $parsedAttempt = 0
        if (-not [int]::TryParse(
            $runAttemptText,
            [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$parsedAttempt) -or $parsedAttempt -lt 1) {
            throw "GITHUB_RUN_ATTEMPT 必须为正 Int32；actual='$runAttemptText'。"
        }

        $runAttempt = $parsedAttempt
    }

    if (($null -eq $runId) -ne ($null -eq $runAttempt)) {
        throw 'GITHUB_RUN_ID 与 GITHUB_RUN_ATTEMPT 必须同时存在或同时为空。'
    }

    if ($null -ne $dispatchSha -and $dispatchSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "GITHUB_SHA 必须为 40 位十六进制 dispatch SHA；actual='$dispatchSha'。"
    }
    if ($null -ne $candidateSha -and $candidateSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "PIXELENGINE_CANDIDATE_SHA 必须为 40 位十六进制 candidate SHA；actual='$candidateSha'。"
    }
    if ($null -ne $checkedOutSha -and $checkedOutSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "PIXELENGINE_CHECKED_OUT_SHA 必须为 40 位十六进制 checked-out SHA；actual='$checkedOutSha'。"
    }
    if (($null -eq $candidateSha) -ne ($null -eq $checkedOutSha)) {
        throw 'PIXELENGINE_CANDIDATE_SHA 与 PIXELENGINE_CHECKED_OUT_SHA 必须同时存在或同时为空。'
    }
    if ($null -ne $candidateSha -and
        -not $candidateSha.Equals($checkedOutSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw "candidate SHA 与 checked-out SHA 不一致：candidate='$candidateSha', checkedOut='$checkedOutSha'。"
    }

    Assert-SafeIdentityText 'RUNNER_NAME' $runnerName 128
    if ($null -ne $runnerOs -and $runnerOs -notin @('Windows', 'Linux', 'macOS')) {
        throw "RUNNER_OS 必须为 Windows、Linux 或 macOS；actual='$runnerOs'。"
    }
    if ($null -ne $runnerArch -and $runnerArch -notin @('X86', 'X64', 'ARM', 'ARM64')) {
        throw "RUNNER_ARCH 必须为 X86、X64、ARM 或 ARM64；actual='$runnerArch'。"
    }
    if ($null -ne $imageOs -and $imageOs -notmatch '^[A-Za-z0-9._-]{1,64}$') {
        throw "ImageOS 格式无效；actual='$imageOs'。"
    }
    if ($null -ne $imageVersion -and $imageVersion -notmatch '^[A-Za-z0-9._-]{1,64}$') {
        throw "ImageVersion 格式无效；actual='$imageVersion'。"
    }

    $runnerCoreValues = @($runnerName, $runnerOs, $runnerArch) | Where-Object { $null -ne $_ }
    if ($runnerCoreValues.Count -ne 0 -and $runnerCoreValues.Count -ne 3) {
        throw 'RUNNER_NAME、RUNNER_OS 与 RUNNER_ARCH 必须同时存在或同时为空。'
    }
    if (($null -ne $imageOs -or $null -ne $imageVersion) -and $runnerCoreValues.Count -eq 0) {
        throw 'ImageOS/ImageVersion 只有在完整 runner identity 存在时才允许记录。'
    }

    if ($isGitHubActions) {
        if ($null -eq $runId -or $null -eq $runAttempt -or $null -eq $dispatchSha -or
            $null -eq $candidateSha -or $null -eq $checkedOutSha) {
            throw ('GitHub Actions native-smoke 必须提供 GITHUB_RUN_ID、GITHUB_RUN_ATTEMPT、GITHUB_SHA、' +
                'PIXELENGINE_CANDIDATE_SHA 与 PIXELENGINE_CHECKED_OUT_SHA。')
        }
        if ($runnerCoreValues.Count -ne 3) {
            throw 'GitHub Actions native-smoke 必须提供完整 RUNNER_NAME/RUNNER_OS/RUNNER_ARCH。'
        }
    }

    return [ordered]@{
        source = $isGitHubActions ? 'github-actions' : 'local'
        githubActions = $isGitHubActions
        githubRun = [ordered]@{
            available = $null -ne $runId
            id = $runId
            attempt = $runAttempt
        }
        dispatchSha = $null -eq $dispatchSha ? $null : $dispatchSha.ToLowerInvariant()
        candidateSha = $null -eq $candidateSha ? $null : $candidateSha.ToLowerInvariant()
        checkedOutSha = $null -eq $checkedOutSha ? $null : $checkedOutSha.ToLowerInvariant()
        runner = [ordered]@{
            available = $runnerCoreValues.Count -eq 3
            name = $runnerName
            os = $runnerOs
            arch = $runnerArch
            imageOs = $imageOs
            imageVersion = $imageVersion
        }
    }
}

function Resolve-NativeSmokeProjects([string]$repoRoot, [string]$manifestPath) {
    $definitions = if ([string]::IsNullOrWhiteSpace($manifestPath)) {
        @(
            [ordered]@{ name = 'rendering'; path = 'tests/PixelEngine.Rendering.Tests/PixelEngine.Rendering.Tests.csproj' },
            [ordered]@{ name = 'ui'; path = 'tests/PixelEngine.UI.Tests/PixelEngine.UI.Tests.csproj' },
            [ordered]@{ name = 'hosting'; path = 'tests/PixelEngine.Hosting.Tests/PixelEngine.Hosting.Tests.csproj' },
            [ordered]@{ name = 'demo'; path = 'tests/PixelEngine.Demo.Tests/PixelEngine.Demo.Tests.csproj' }
        )
    } else {
        $fullManifestPath = [IO.Path]::IsPathRooted($manifestPath) ?
            [IO.Path]::GetFullPath($manifestPath) :
            [IO.Path]::GetFullPath((Join-Path $repoRoot $manifestPath))
        if (-not (Test-Path -LiteralPath $fullManifestPath -PathType Leaf)) {
            throw "native-smoke project manifest 不存在：$fullManifestPath"
        }

        try {
            $manifest = Get-Content -LiteralPath $fullManifestPath -Raw | ConvertFrom-Json
        } catch {
            throw "native-smoke project manifest 无法解析：$($_.Exception.Message)"
        }

        @($manifest.projects)
    }

    if ($definitions.Count -eq 0) {
        throw 'native-smoke project 清单不能为空。'
    }

    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $projects = [Collections.Generic.List[object]]::new()
    foreach ($definition in $definitions) {
        $name = [string]$definition.name
        $path = [string]$definition.path
        if ($name -notmatch '^[a-z][a-z0-9-]{0,31}$') {
            throw "native-smoke project name 格式无效：'$name'。"
        }
        if ([string]::IsNullOrWhiteSpace($path)) {
            throw "native-smoke project '$name' 缺少 path。"
        }

        $resolvedPath = [IO.Path]::IsPathRooted($path) ?
            [IO.Path]::GetFullPath($path) :
            [IO.Path]::GetFullPath((Join-Path $repoRoot $path))
        if (-not [string]::Equals([IO.Path]::GetExtension($resolvedPath), '.csproj', [StringComparison]::OrdinalIgnoreCase)) {
            throw "native-smoke project '$name' 必须指向 .csproj：$resolvedPath"
        }
        if (-not $names.Add($name)) {
            throw "native-smoke project name 重复：$name"
        }
        if (-not $paths.Add($resolvedPath)) {
            throw "native-smoke project path 重复：$resolvedPath"
        }

        $projects.Add([pscustomobject]@{
            name = $name
            path = $path
            resolvedPath = $resolvedPath
        })
    }

    return $projects
}

function Read-GraphicsCapabilityMarkers(
    [string]$projectName,
    [xml]$trx,
    [Collections.Generic.List[string]]$failures) {
    $markerPrefix = 'PIXELENGINE_GRAPHICS_CAPABILITY '
    $capabilities = [Collections.Generic.List[object]]::new()
    $deduplicationKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $stdoutNodes = @($trx.SelectNodes("//*[local-name()='StdOut']"))
    foreach ($stdoutNode in $stdoutNodes) {
        foreach ($line in [regex]::Split([string]$stdoutNode.InnerText, '\r?\n')) {
            $markerIndex = $line.IndexOf($markerPrefix, [StringComparison]::Ordinal)
            if ($markerIndex -lt 0) {
                continue
            }

            $payload = $line.Substring($markerIndex + $markerPrefix.Length).Trim()
            try {
                $marker = $payload | ConvertFrom-Json -ErrorAction Stop
            } catch {
                $failures.Add("$projectName`: graphics capability marker JSON 无法解析：$($_.Exception.Message)")
                continue
            }

            $requiredProperties = @('schema', 'backend', 'vendor', 'renderer', 'version', 'major', 'minor', 'isGles', 'isAngle')
            $missingProperties = @($requiredProperties | Where-Object { $null -eq $marker.PSObject.Properties[$_] })
            if ($missingProperties.Count -gt 0) {
                $failures.Add("$projectName`: graphics capability marker 缺少字段：$($missingProperties -join ', ')。")
                continue
            }

            $schema = $marker.schema
            $backend = $marker.backend
            $vendor = $marker.vendor
            $renderer = $marker.renderer
            $version = $marker.version
            if ($schema -isnot [string] -or $schema -ne 'pixelengine.graphics-capability/v1') {
                $failures.Add("$projectName`: graphics capability marker schema 无效。")
                continue
            }
            if ($backend -isnot [string] -or $backend -notin @('desktop-gl', 'angle-gles')) {
                $failures.Add("$projectName`: graphics capability marker backend 无效：'$backend'。")
                continue
            }
            if ($vendor -isnot [string] -or [string]::IsNullOrWhiteSpace($vendor) -or
                $renderer -isnot [string] -or [string]::IsNullOrWhiteSpace($renderer) -or
                $version -isnot [string] -or [string]::IsNullOrWhiteSpace($version)) {
                $failures.Add("$projectName`: graphics capability marker vendor/renderer/version 必须为非空字符串。")
                continue
            }
            try {
                Assert-SafeIdentityText 'graphics vendor' $vendor 512
                Assert-SafeIdentityText 'graphics renderer' $renderer 512
                Assert-SafeIdentityText 'graphics version' $version 512
            } catch {
                $failures.Add("$projectName`: $($_.Exception.Message)")
                continue
            }

            $majorValue = $marker.major
            $minorValue = $marker.minor
            $majorIsInteger = $majorValue -is [byte] -or $majorValue -is [sbyte] -or
                $majorValue -is [short] -or $majorValue -is [ushort] -or
                $majorValue -is [int] -or $majorValue -is [uint] -or
                $majorValue -is [long] -or $majorValue -is [ulong]
            $minorIsInteger = $minorValue -is [byte] -or $minorValue -is [sbyte] -or
                $minorValue -is [short] -or $minorValue -is [ushort] -or
                $minorValue -is [int] -or $minorValue -is [uint] -or
                $minorValue -is [long] -or $minorValue -is [ulong]
            if (-not $majorIsInteger -or -not $minorIsInteger -or
                [long]$majorValue -lt 0 -or [long]$majorValue -gt [int]::MaxValue -or
                [long]$minorValue -lt 0 -or [long]$minorValue -gt [int]::MaxValue) {
                $failures.Add("$projectName`: graphics capability marker major/minor 必须为非负 Int32。")
                continue
            }
            if ($marker.isGles -isnot [bool] -or $marker.isAngle -isnot [bool]) {
                $failures.Add("$projectName`: graphics capability marker isGles/isAngle 必须为 boolean。")
                continue
            }

            $major = [int]$majorValue
            $minor = [int]$minorValue
            $isGles = [bool]$marker.isGles
            $isAngle = [bool]$marker.isAngle
            if ($backend -eq 'desktop-gl') {
                if ($isGles -or $isAngle -or $major -lt 3 -or ($major -eq 3 -and $minor -lt 3)) {
                    $failures.Add(
                        "$projectName`: desktop-gl marker 必须证明 Desktop GL 3.3+ 且非 GLES/ANGLE；actual=$major.$minor, isGles=$isGles, isAngle=$isAngle。")
                    continue
                }
            } elseif (-not $isGles -or -not $isAngle -or $major -lt 3 -or $renderer -notmatch '(?i)\bANGLE\b') {
                $failures.Add(
                    "$projectName`: angle-gles marker 必须证明 ANGLE renderer 上的 GLES 3.0+；actual=$major.$minor, renderer='$renderer', isGles=$isGles, isAngle=$isAngle。")
                continue
            }

            $deduplicationKey = "$backend`u{001F}$vendor`u{001F}$renderer`u{001F}$version`u{001F}$major`u{001F}$minor"
            if ($deduplicationKeys.Add($deduplicationKey)) {
                $capabilities.Add([pscustomobject][ordered]@{
                    schema = 'pixelengine.graphics-capability/v1'
                    backend = $backend
                    vendor = $vendor
                    renderer = $renderer
                    version = $version
                    major = $major
                    minor = $minor
                    isGles = $isGles
                    isAngle = $isAngle
                    project = $projectName
                })
            }
        }
    }

    return @($capabilities)
}

function Read-TrxAudit(
    [string]$projectName,
    [string]$trxPath,
    [Collections.Generic.List[string]]$failures) {
    try {
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    } catch {
        $failures.Add("$projectName`: TRX XML 无法解析：$($_.Exception.Message)")
        return [pscustomobject]@{
            total = 0; passed = 0; failed = 0; skipped = 0; notExecuted = 0; unexpected = 0
            counterConsistent = $false; counters = $null; resultCounts = $null; graphicsCapabilities = @()
        }
    }

    $counterNames = @(
        'total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
        'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning',
        'completed', 'inProgress', 'pending', 'skipped'
    )
    $requiredCounterNames = @($counterNames | Where-Object { $_ -ne 'skipped' })
    $countersNodes = @($trx.SelectNodes("//*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    $countersNode = $countersNodes.Count -eq 1 ? $countersNodes[0] : $null
    $counterValues = [ordered]@{}
    $counterShapeValid = $null -ne $countersNode
    if ($countersNodes.Count -ne 1) {
        $failures.Add("$projectName`: TRX 必须恰好包含一个 ResultSummary/Counters；actual=$($countersNodes.Count)。")
    }

    foreach ($counterName in $counterNames) {
        $attribute = $null -eq $countersNode ? $null : $countersNode.Attributes[$counterName]
        if ($null -eq $attribute) {
            if ($counterName -in $requiredCounterNames) {
                $failures.Add("$projectName`: TRX Counters 缺少 '$counterName'。")
                $counterShapeValid = $false
            }

            $counterValues[$counterName] = 0
            continue
        }

        $value = 0
        if (-not [int]::TryParse(
            $attribute.Value,
            [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$value) -or $value -lt 0) {
            $failures.Add("$projectName`: TRX Counter '$counterName' 不是非负 Int32：'$($attribute.Value)'。")
            $counterShapeValid = $false
            $value = 0
        }

        $counterValues[$counterName] = $value
    }

    $outcomeToCounter = [ordered]@{
        Passed = 'passed'
        Failed = 'failed'
        Error = 'error'
        Timeout = 'timeout'
        Aborted = 'aborted'
        Inconclusive = 'inconclusive'
        PassedButRunAborted = 'passedButRunAborted'
        NotRunnable = 'notRunnable'
        NotExecuted = 'notExecuted'
        Disconnected = 'disconnected'
        Warning = 'warning'
        Completed = 'completed'
        InProgress = 'inProgress'
        Pending = 'pending'
        Skipped = 'skipped'
    }
    $resultCounts = [ordered]@{}
    foreach ($counterName in $counterNames) {
        if ($counterName -notin @('total', 'executed')) {
            $resultCounts[$counterName] = 0
        }
    }
    $resultCounts['unexpected'] = 0

    $resultsNodes = @($trx.SelectNodes("//*[local-name()='Results']"))
    if ($resultsNodes.Count -ne 1) {
        $failures.Add("$projectName`: TRX 必须恰好包含一个 Results；actual=$($resultsNodes.Count)。")
        $counterShapeValid = $false
    }
    $unitTestResults = $resultsNodes.Count -eq 1 ?
        @($resultsNodes[0].SelectNodes("./*[local-name()='UnitTestResult']")) :
        @()
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($result in $unitTestResults) {
        $executionId = [string]$result.executionId
        $testId = [string]$result.testId
        $testName = [string]$result.testName
        $outcome = [string]$result.outcome
        if ([string]::IsNullOrWhiteSpace($executionId) -or
            [string]::IsNullOrWhiteSpace($testId) -or
            [string]::IsNullOrWhiteSpace($testName) -or
            [string]::IsNullOrWhiteSpace($outcome)) {
            $failures.Add("$projectName`: UnitTestResult 缺少 executionId/testId/testName/outcome。")
            $resultCounts['unexpected']++
            continue
        }
        if (-not $executionIds.Add($executionId)) {
            $failures.Add("$projectName`: UnitTestResult executionId 重复：$executionId")
            $resultCounts['unexpected']++
            continue
        }

        if (-not $outcomeToCounter.Contains($outcome)) {
            $failures.Add("$projectName`: UnitTestResult outcome 未知：'$outcome' ($testName)。")
            $resultCounts['unexpected']++
            continue
        }

        $resultCounts[$outcomeToCounter[$outcome]]++
    }

    $resultCounts['total'] = $unitTestResults.Count
    $notRun = $resultCounts['notExecuted'] + $resultCounts['notRunnable'] +
        $resultCounts['inProgress'] + $resultCounts['pending'] + $resultCounts['skipped']
    $resultCounts['executed'] = $unitTestResults.Count - $notRun

    $counterConsistent = $counterShapeValid
    foreach ($counterName in $counterNames) {
        if ([int]$counterValues[$counterName] -ne [int]$resultCounts[$counterName]) {
            $failures.Add(
                "$projectName`: TRX counter mismatch '$counterName': counter=$($counterValues[$counterName]), UnitTestResult=$($resultCounts[$counterName])。")
            $counterConsistent = $false
        }
    }

    if ($resultCounts['unexpected'] -gt 0) {
        $counterConsistent = $false
    }

    $failed = $resultCounts['failed'] + $resultCounts['error'] + $resultCounts['timeout'] +
        $resultCounts['aborted'] + $resultCounts['inconclusive'] + $resultCounts['passedButRunAborted'] +
        $resultCounts['notRunnable'] + $resultCounts['disconnected'] + $resultCounts['warning'] +
        $resultCounts['completed'] + $resultCounts['inProgress'] + $resultCounts['pending'] +
        $resultCounts['unexpected']
    $graphicsCapabilities = @(Read-GraphicsCapabilityMarkers $projectName $trx $failures)
    return [pscustomobject]@{
        total = [int]$resultCounts['total']
        passed = [int]$resultCounts['passed']
        failed = [int]$failed
        skipped = [int]$resultCounts['skipped']
        notExecuted = [int]$resultCounts['notExecuted']
        unexpected = [int]$resultCounts['unexpected']
        counterConsistent = $counterConsistent
        counters = [pscustomobject]$counterValues
        resultCounts = [pscustomobject]$resultCounts
        graphicsCapabilities = $graphicsCapabilities
    }
}

$requiredEnvironment = @(
    'PIXELENGINE_RENDERING_GL_SMOKE',
    'PIXELENGINE_RENDERING_ANGLE_SMOKE'
)

foreach ($name in $requiredEnvironment) {
    if ([string]::Equals([Environment]::GetEnvironmentVariable($name), '1', [StringComparison]::Ordinal) -eq $false) {
        throw "native-smoke 要求显式启用环境变量 $name=1；当前 job 不允许静默跳过。"
    }
}

$testSeamActive = -not [string]::Equals($TestRunner, 'dotnet', [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::IsNullOrWhiteSpace($ProjectManifestPath)
$runIdentity = Get-NativeSmokeRunIdentity
$githubSeamRejected = $runIdentity.githubActions -and $testSeamActive
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projects = @(Resolve-NativeSmokeProjects $repoRoot ($githubSeamRejected ? '' : $ProjectManifestPath))
$resultsRoot = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsDirectory))
}
$runName = 'run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ', [Globalization.CultureInfo]::InvariantCulture) + '-' + $PID
$runDirectory = Join-Path $resultsRoot $runName
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

$projectReports = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
if ($githubSeamRejected) {
    $failures.Add(
        'GitHub Actions native-smoke 禁止 TestRunner/ProjectManifestPath 测试 seam；必须使用内建四项目与真实 dotnet runner。')
}

Push-Location $repoRoot
try {
    foreach ($project in $projects) {
        $projectFailureStart = $failures.Count
        $logPath = Join-Path $runDirectory "$($project.name).log"
        $trxName = "$($project.name).trx"
        $trxPath = Join-Path $runDirectory $trxName
        $projectExists = Test-Path -LiteralPath $project.resolvedPath -PathType Leaf
        if ($githubSeamRejected) {
            $message = "$($project.name): GitHub Actions 已拒绝 native-smoke 测试 seam，runner 未启动。"
            $failures.Add($message)
            $message | Set-Content -LiteralPath $logPath -Encoding UTF8
            $projectReports.Add([pscustomobject]@{
                name = $project.name; path = $project.path; resolvedPath = $project.resolvedPath
                projectExists = $projectExists; log = $logPath; trx = $null; trxExists = $false
                total = 0; passed = 0; failed = 0; skipped = 0; notExecuted = 0; unexpected = 0
                exitCode = -1; counterConsistent = $false; counters = $null; resultCounts = $null
                graphicsCapabilities = @(); success = $false
            })
            continue
        }
        if (-not $projectExists) {
            $message = "$($project.name): project 不存在：$($project.resolvedPath)"
            $failures.Add($message)
            $message | Set-Content -LiteralPath $logPath -Encoding UTF8
            $projectReports.Add([pscustomobject]@{
                name = $project.name; path = $project.path; resolvedPath = $project.resolvedPath
                projectExists = $false; log = $logPath; trx = $null; trxExists = $false
                total = 0; passed = 0; failed = 0; skipped = 0; notExecuted = 0; unexpected = 0
                exitCode = -1; counterConsistent = $false; counters = $null; resultCounts = $null
                graphicsCapabilities = @(); success = $false
            })
            continue
        }

        $arguments = @(
            'test',
            $project.resolvedPath,
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

        $exitCode = -1
        $output = @()
        try {
            $global:LASTEXITCODE = 1
            $output = @(& $TestRunner @arguments 2>&1)
            $exitCode = [int]$global:LASTEXITCODE
        } catch {
            $output = @($_.Exception.Message)
            $failures.Add("$($project.name): test runner 启动失败：$($_.Exception.Message)")
        }

        $output | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $logPath -Encoding UTF8
        if ($exitCode -ne 0) {
            $failures.Add("$($project.name): test runner exit=$exitCode；要求 exit=0。")
        }

        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            $failures.Add("$($project.name): 未生成 TRX，无法证明实际执行了 native smoke 用例。exit=$exitCode")
            $projectReports.Add([pscustomobject]@{
                name = $project.name; path = $project.path; resolvedPath = $project.resolvedPath
                projectExists = $true; log = $logPath; trx = $null; trxExists = $false
                total = 0; passed = 0; failed = 0; skipped = 0; notExecuted = 0; unexpected = 0
                exitCode = $exitCode; counterConsistent = $false; counters = $null; resultCounts = $null
                graphicsCapabilities = @(); success = $false
            })
            continue
        }

        $audit = Read-TrxAudit $project.name $trxPath $failures
        if ($audit.total -le 0) {
            $failures.Add("$($project.name): total=$($audit.total)；每个 native-smoke project 必须实际执行至少 1 个用例。")
        }
        if ($audit.passed -ne $audit.total) {
            $failures.Add("$($project.name): passed=$($audit.passed) 与 total=$($audit.total) 不一致。")
        }
        if ($audit.failed -ne 0) {
            $failures.Add("$($project.name): failed=$($audit.failed)；要求为 0。")
        }
        if ($audit.skipped -ne 0) {
            $failures.Add("$($project.name): skipped=$($audit.skipped)；要求为 0。")
        }
        if ($audit.notExecuted -ne 0) {
            $failures.Add("$($project.name): notExecuted=$($audit.notExecuted)；要求为 0。")
        }
        if (-not $audit.counterConsistent) {
            $failures.Add("$($project.name): TRX Counters 与逐条 UnitTestResult 不一致。")
        }

        $projectSuccess = $failures.Count -eq $projectFailureStart
        $projectReports.Add([pscustomobject]@{
            name = $project.name
            path = $project.path
            resolvedPath = $project.resolvedPath
            projectExists = $true
            log = $logPath
            trx = $trxPath
            trxExists = $true
            total = $audit.total
            passed = $audit.passed
            failed = $audit.failed
            skipped = $audit.skipped
            notExecuted = $audit.notExecuted
            unexpected = $audit.unexpected
            exitCode = $exitCode
            counterConsistent = $audit.counterConsistent
            counters = $audit.counters
            resultCounts = $audit.resultCounts
            graphicsCapabilities = @($audit.graphicsCapabilities)
            success = $projectSuccess
        })
        Write-Host "[$($project.name)] total=$($audit.total) passed=$($audit.passed) failed=$($audit.failed) skipped=$($audit.skipped) notExecuted=$($audit.notExecuted) exit=$exitCode consistent=$($audit.counterConsistent)"
    }
} finally {
    Pop-Location
}

if ($projectReports.Count -ne $projects.Count) {
    $failures.Add("native-smoke project report 数量不一致：expected=$($projects.Count), actual=$($projectReports.Count)。")
}

$totalTests = [int](($projectReports | Measure-Object -Property total -Sum).Sum)
$passedTests = [int](($projectReports | Measure-Object -Property passed -Sum).Sum)
$failedTests = [int](($projectReports | Measure-Object -Property failed -Sum).Sum)
$skippedTests = [int](($projectReports | Measure-Object -Property skipped -Sum).Sum)
$notExecutedTests = [int](($projectReports | Measure-Object -Property notExecuted -Sum).Sum)
$successfulProjects = @($projectReports | Where-Object { $_.success }).Count
$graphicsEvidenceReport = @($projectReports | Where-Object {
    @($_.graphicsCapabilities | Where-Object { $_.backend -eq 'desktop-gl' }).Count -gt 0 -and
    @($_.graphicsCapabilities | Where-Object { $_.backend -eq 'angle-gles' }).Count -gt 0
} | Sort-Object -Property name | Select-Object -First 1)
$graphicsEvidenceReport = $graphicsEvidenceReport.Count -eq 0 ? $null : $graphicsEvidenceReport[0]
$desktopGl = $null
$angleGles = $null
if ($null -eq $graphicsEvidenceReport) {
    $failures.Add('native-smoke 必须从同一份 TRX 提取至少一个 Desktop GL 3.3+ marker 与一个 ANGLE/GLES 3.0+ marker。')
} else {
    $desktopGl = @($graphicsEvidenceReport.graphicsCapabilities |
        Where-Object { $_.backend -eq 'desktop-gl' } |
        Sort-Object -Property vendor, renderer, version |
        Select-Object -First 1)[0]
    $angleGles = @($graphicsEvidenceReport.graphicsCapabilities |
        Where-Object { $_.backend -eq 'angle-gles' } |
        Sort-Object -Property vendor, renderer, version |
        Select-Object -First 1)[0]
}
$graphicsContext = [ordered]@{
    sourceProject = $null -eq $graphicsEvidenceReport ? $null : $graphicsEvidenceReport.name
    sourceTrx = $null -eq $graphicsEvidenceReport ? $null : $graphicsEvidenceReport.trx
    glVendor = $null -eq $desktopGl ? $null : $desktopGl.vendor
    glRenderer = $null -eq $desktopGl ? $null : $desktopGl.renderer
    glVersion = $null -eq $desktopGl ? $null : $desktopGl.version
    angleBackend = $null -eq $angleGles ? $null : $angleGles.renderer
    desktopGl = $desktopGl
    angleGles = $angleGles
}
$success = $failures.Count -eq 0 -and
    $successfulProjects -eq $projects.Count -and
    $totalTests -gt 0 -and
    $passedTests -eq $totalTests -and
    $failedTests -eq 0 -and
    $skippedTests -eq 0 -and
    $notExecutedTests -eq 0

$summary = [ordered]@{
    schema = 'pixelengine.native-smoke/v2'
    schemaVersion = 2
    evidenceKind = $testSeamActive ? 'native-smoke-test-seam' : 'executed-native-smoke'
    generatedAtUtc = [DateTime]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    success = $success
    configuration = $Configuration
    testRunner = $TestRunner
    testSeamActive = $testSeamActive
    projectSetSource = $githubSeamRejected ? 'built-in-rejection-report' :
        ([string]::IsNullOrWhiteSpace($ProjectManifestPath) ? 'built-in-required-projects' : 'explicit-project-manifest')
    runIdentity = $runIdentity
    requiredEnvironment = [ordered]@{
        PIXELENGINE_RENDERING_GL_SMOKE = [Environment]::GetEnvironmentVariable('PIXELENGINE_RENDERING_GL_SMOKE')
        PIXELENGINE_RENDERING_ANGLE_SMOKE = [Environment]::GetEnvironmentVariable('PIXELENGINE_RENDERING_ANGLE_SMOKE')
    }
    graphicsContext = $graphicsContext
    runDirectory = $runDirectory
    projectCount = $projects.Count
    successfulProjectCount = $successfulProjects
    totalTests = $totalTests
    passedTests = $passedTests
    failedTests = $failedTests
    skippedTests = $skippedTests
    notExecutedTests = $notExecutedTests
    projects = @($projectReports)
    failures = @($failures)
}
$summaryPath = Join-Path $runDirectory 'summary.json'
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add('# Native smoke report')
$markdown.Add('')
$markdown.Add('- Schema: `pixelengine.native-smoke/v2`')
$markdown.Add("- Success: ``$success``")
$markdown.Add("- Configuration: ``$Configuration``")
$markdown.Add('- Required environment: `PIXELENGINE_RENDERING_GL_SMOKE=1`, `PIXELENGINE_RENDERING_ANGLE_SMOKE=1`')
$markdown.Add("- Identity source: ``$($runIdentity.source)``")
$markdown.Add("- GitHub run: " + ($runIdentity.githubRun.available ? "``$($runIdentity.githubRun.id)`` attempt ``$($runIdentity.githubRun.attempt)``" : '`unavailable (local run)`'))
$markdown.Add("- Candidate SHA: " + ($null -eq $runIdentity.candidateSha ? '`unavailable (local run)`' : "``$($runIdentity.candidateSha)``"))
$markdown.Add("- Dispatch SHA: " + ($null -eq $runIdentity.dispatchSha ? '`unavailable (local run)`' : "``$($runIdentity.dispatchSha)``"))
$markdown.Add("- Checked-out SHA: " + ($null -eq $runIdentity.checkedOutSha ? '`unavailable (local run)`' : "``$($runIdentity.checkedOutSha)``"))
$markdown.Add("- Runner identity: " + ($runIdentity.runner.available ? "``$($runIdentity.runner.name) / $($runIdentity.runner.os) / $($runIdentity.runner.arch)``" : '`unavailable (local run)`'))
$markdown.Add("- Desktop GL: " + ($null -eq $desktopGl ? '`unavailable`' : "``$($desktopGl.vendor) / $($desktopGl.renderer) / $($desktopGl.version)``"))
$markdown.Add("- ANGLE/GLES: " + ($null -eq $angleGles ? '`unavailable`' : "``$($angleGles.renderer) / $($angleGles.version)``"))
$markdown.Add("- Projects: $successfulProjects/$($projects.Count) successful")
$markdown.Add("- Total: $totalTests; passed: $passedTests; failed: $failedTests; skipped: $skippedTests; not executed: $notExecutedTests")
$markdown.Add('')
$markdown.Add('| Project | Exists | TRX | Total | Passed | Failed | Skipped | NotExecuted | CountersMatch | Exit | Success |')
$markdown.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
foreach ($report in $projectReports) {
    $markdown.Add("| $($report.name) | $($report.projectExists) | $($report.trxExists) | $($report.total) | $($report.passed) | $($report.failed) | $($report.skipped) | $($report.notExecuted) | $($report.counterConsistent) | $($report.exitCode) | $($report.success) |")
}
$markdown.Add('')
$markdown.Add("Summary JSON: ``$summaryPath``")
$markdownPath = Join-Path $runDirectory 'summary.md'
$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Host "Native smoke summary: projects=$successfulProjects/$($projects.Count) total=$totalTests passed=$passedTests failed=$failedTests skipped=$skippedTests notExecuted=$notExecutedTests success=$success"

if (-not $success) {
    throw ("native-smoke 失败：`n" + ($failures -join "`n"))
}
