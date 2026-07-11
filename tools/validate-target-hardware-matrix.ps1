[CmdletBinding()]
param(
    [string]$MatrixPath = "tools/target-hardware-matrix.json",
    [string]$RidConfigPath = "tools/release-rids.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-RepoFile([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $path))
}

function Read-Json([string]$path) {
    $fullPath = Resolve-RepoFile $path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "JSON file does not exist: $path"
    }

    try {
        return Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
    } catch {
        throw "Invalid JSON in $path`: $($_.Exception.Message)"
    }
}

function Assert-Equal([string]$name, [object]$actual, [object]$expected) {
    $actualText = (@($actual) -join ",")
    $expectedText = (@($expected) -join ",")
    if ($actualText -cne $expectedText) {
        throw "$name mismatch. Expected [$expectedText], actual [$actualText]"
    }
}

function Assert-NonEmpty([string]$name, [object]$value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        throw "$name must be non-empty."
    }
}

$matrix = Read-Json $MatrixPath
$ridConfig = Read-Json $RidConfigPath

if ($matrix.schema -ne "pixelengine.target-hardware-matrix/v2") {
    throw "Unsupported target hardware matrix schema: $($matrix.schema)"
}
if ($matrix.sourceOfTruth -ne $RidConfigPath.Replace("\", "/")) {
    throw "Matrix sourceOfTruth must point to $RidConfigPath."
}
if ($matrix.sourceCommit -notmatch "^[0-9a-f]{7,64}$") {
    throw "Matrix sourceCommit must be a hexadecimal Git commit."
}

$configEntries = @($ridConfig.rids)
$matrixEntries = @($matrix.entries)
$configRids = @($configEntries | ForEach-Object { [string]$_.rid } | Sort-Object)
$matrixRids = @($matrixEntries | ForEach-Object { [string]$_.rid } | Sort-Object)
Assert-Equal "RID set" $matrixRids $configRids

$activeConfigRids = @($configEntries | Where-Object { $_.active } | ForEach-Object { [string]$_.rid } | Sort-Object)
$activeMatrixRids = @($matrix.product.activeRidsAtSnapshot | ForEach-Object { [string]$_ } | Sort-Object)
Assert-Equal "active RID set" $activeMatrixRids $activeConfigRids
Assert-Equal "long-term RID set" (@($matrix.product.longTermCompatibilityRids | ForEach-Object { [string]$_ } | Sort-Object)) $configRids

$requiredProperties = @("rid", "lifecycle", "productRequirement", "targetArchitecture", "buildTestRunner", "hardware", "permissions", "benchmark", "smoke")
$hardwareProperties = @("cpu", "gpu", "driver", "os", "dotnet")
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($entry in $matrixEntries) {
    foreach ($property in $requiredProperties) {
        if (-not ($entry.PSObject.Properties.Name -contains $property)) {
            throw "Matrix entry $($entry.rid) is missing $property."
        }
    }

    if ($entry.PSObject.Properties.Name -contains "runner") {
        throw "Matrix v2 entry $($entry.rid) must not use the legacy runner field; use buildTestRunner and a distinct nativeGpuSmokeRunner where required."
    }

    if (-not $seen.Add([string]$entry.rid)) {
        throw "Duplicate matrix RID: $($entry.rid)"
    }

    $config = $configEntries | Where-Object { $_.rid -eq $entry.rid }
    if ($null -eq $config) {
        throw "Matrix entry has no release-rids.json counterpart: $($entry.rid)"
    }
    Assert-Equal "$($entry.rid) active" @([bool]$config.active) @($entry.lifecycle -like "active_*")
    $releaseRunnerLabel = if ($entry.buildTestRunner.PSObject.Properties.Name -contains "releaseRunnerLabel") { [string]$entry.buildTestRunner.releaseRunnerLabel } else { [string]$entry.buildTestRunner.label }
    Assert-Equal "$($entry.rid) release runner" $releaseRunnerLabel ([string]$config.runner)
    Assert-Equal "$($entry.rid) shell" $entry.buildTestRunner.shell ([string]$config.shell)
    Assert-Equal "$($entry.rid) smoke" $entry.smoke.releaseSmokeProfile ([string]$config.smoke)
    Assert-Equal "$($entry.rid) codesign" ([bool]($entry.permissions.signing -match "Developer ID")) ([bool]$config.codesign)

    foreach ($hardwareProperty in $hardwareProperties) {
        $hardware = $entry.hardware.$hardwareProperty
        if ($null -eq $hardware) {
            throw "$($entry.rid) hardware.$hardwareProperty is missing."
        }
        Assert-NonEmpty "$($entry.rid) hardware.$hardwareProperty.value" $hardware.value
        Assert-NonEmpty "$($entry.rid) hardware.$hardwareProperty.status" $hardware.status
        Assert-NonEmpty "$($entry.rid) hardware.$hardwareProperty.source" $hardware.source
    }

    Assert-NonEmpty "$($entry.rid) buildTestRunner.provider" $entry.buildTestRunner.provider
    Assert-NonEmpty "$($entry.rid) buildTestRunner.label" $entry.buildTestRunner.label
    Assert-NonEmpty "$($entry.rid) buildTestRunner.shell" $entry.buildTestRunner.shell
    Assert-NonEmpty "$($entry.rid) permissions.administratorStatus" $entry.permissions.administratorStatus
    Assert-NonEmpty "$($entry.rid) permissions.signing" $entry.permissions.signing
    Assert-NonEmpty "$($entry.rid) benchmark.command" $entry.benchmark.command
    Assert-NonEmpty "$($entry.rid) benchmark.status" $entry.benchmark.status
    Assert-NonEmpty "$($entry.rid) smoke.status" $entry.smoke.status
    Assert-NonEmpty "$($entry.rid) smoke.releaseSmokeProfile" $entry.smoke.releaseSmokeProfile

    if (@($entry.benchmark.requiredFields).Count -lt 1) {
        throw "$($entry.rid) benchmark.requiredFields must not be empty."
    }
    if (@($entry.smoke.commands).Count -lt 1) {
        throw "$($entry.rid) smoke.commands must not be empty."
    }
    foreach ($command in @($entry.smoke.commands)) {
        Assert-NonEmpty "$($entry.rid) smoke command" $command
    }

    if ($entry.rid -eq "win-x64") {
        Assert-Equal "win-x64 buildTestRunner provider" ([string]$entry.buildTestRunner.provider) "github-hosted"
        Assert-Equal "win-x64 buildTestRunner label" ([string]$entry.buildTestRunner.label) "windows-latest"
        Assert-Equal "win-x64 buildTestRunner graphicsNativeSmokeEligible" ([bool]$entry.buildTestRunner.graphicsNativeSmokeEligible) $false
        Assert-NonEmpty "win-x64 buildTestRunner ineligibleReason" $entry.buildTestRunner.ineligibleReason

        if (-not ($entry.PSObject.Properties.Name -contains "nativeGpuSmokeRunner")) {
            throw "win-x64 matrix entry must define nativeGpuSmokeRunner separately from buildTestRunner."
        }

        $nativeRunner = $entry.nativeGpuSmokeRunner
        Assert-Equal "win-x64 nativeGpuSmokeRunner provider" ([string]$nativeRunner.provider) "external_required"
        Assert-Equal "win-x64 nativeGpuSmokeRunner registrationStatus" ([string]$nativeRunner.registrationStatus) "missing"
        Assert-Equal "win-x64 nativeGpuSmokeRunner trigger" ([string]$nativeRunner.trigger) "workflow_dispatch"
        Assert-Equal "win-x64 nativeGpuSmokeRunner workflow" ([string]$nativeRunner.workflow) ".github/workflows/native-gpu-smoke.yml"
        Assert-Equal "win-x64 nativeGpuSmokeRunner candidateShaRequired" ([bool]$nativeRunner.candidateShaRequired) $true
        Assert-Equal "win-x64 nativeGpuSmokeRunner interactiveDesktopRequired" ([bool]$nativeRunner.interactiveDesktopRequired) $true
        Assert-Equal "win-x64 nativeGpuSmokeRunner fixtureAllowedInProduction" ([bool]$nativeRunner.fixtureAllowedInProduction) $false
        Assert-Equal "win-x64 nativeGpuSmokeRunner architecture" ([string]$nativeRunner.hostArchitecture) "x64"

        $requiredNativeLabels = @("self-hosted", "Windows", "X64", "pixelengine-wgl-angle", "pixelengine-native-smoke")
        $actualNativeLabels = @($nativeRunner.labels | ForEach-Object { [string]$_ })
        Assert-Equal "win-x64 nativeGpuSmokeRunner labels" (@($actualNativeLabels | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object)) (@($requiredNativeLabels | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object))

        $requiredCapabilities = @("desktop_gl_3_3", "angle_gles_3_0", "native_smoke")
        $actualCapabilities = @($nativeRunner.capabilities | ForEach-Object { [string]$_ })
        Assert-Equal "win-x64 nativeGpuSmokeRunner capabilities" (@($actualCapabilities | Sort-Object)) (@($requiredCapabilities | Sort-Object))

        foreach ($identityField in @("runner.name", "sessionId", "userInteractive", "GPU driver", "GL_VENDOR", "GL_RENDERER", "GL_VERSION", "ANGLE renderer", "gitCommit", "runId", "runAttempt")) {
            if (-not (@($nativeRunner.identityCapture) -contains $identityField)) {
                throw "win-x64 nativeGpuSmokeRunner identityCapture is missing $identityField."
            }
        }

        $nativeWorkflowPath = Resolve-RepoFile ([string]$nativeRunner.workflow)
        if (-not (Test-Path -LiteralPath $nativeWorkflowPath -PathType Leaf)) {
            throw "win-x64 native GPU workflow does not exist: $nativeWorkflowPath"
        }
    } elseif ($entry.PSObject.Properties.Name -contains "nativeGpuSmokeRunner") {
        throw "Only the Windows-first required win-x64 entry may define nativeGpuSmokeRunner in the current matrix."
    }
}

Assert-Equal "required RID set" (@($matrix.product.requiredRids | ForEach-Object { [string]$_ } | Sort-Object)) @("win-x64")
Assert-Equal "conditional RID set" (@($matrix.product.conditionalRids | ForEach-Object { [string]$_ } | Sort-Object)) @("win-arm64")

$observedLocal = @($matrixEntries | Where-Object { $_.hardware.cpu.status -eq "observed_local" } | ForEach-Object { $_.rid })
Write-Output ("Target hardware matrix valid: {0} RIDs; active={1}; conditional={2}; observed_local={3}; native_gpu_smoke=external_required/missing." -f $matrixEntries.Count, ($activeMatrixRids -join ","), (@($matrix.product.conditionalRids) -join ","), ($observedLocal -join ","))
