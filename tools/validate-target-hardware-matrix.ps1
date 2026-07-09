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

if ($matrix.schema -ne "pixelengine.target-hardware-matrix/v1") {
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

$requiredProperties = @("rid", "lifecycle", "productRequirement", "targetArchitecture", "runner", "hardware", "permissions", "benchmark", "smoke")
$hardwareProperties = @("cpu", "gpu", "driver", "os", "dotnet")
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($entry in $matrixEntries) {
    foreach ($property in $requiredProperties) {
        if (-not ($entry.PSObject.Properties.Name -contains $property)) {
            throw "Matrix entry $($entry.rid) is missing $property."
        }
    }

    if (-not $seen.Add([string]$entry.rid)) {
        throw "Duplicate matrix RID: $($entry.rid)"
    }

    $config = $configEntries | Where-Object { $_.rid -eq $entry.rid }
    if ($null -eq $config) {
        throw "Matrix entry has no release-rids.json counterpart: $($entry.rid)"
    }
    Assert-Equal "$($entry.rid) active" @([bool]$config.active) @($entry.lifecycle -like "active_*")
    $releaseRunnerLabel = if ($entry.runner.PSObject.Properties.Name -contains "releaseRunnerLabel") { [string]$entry.runner.releaseRunnerLabel } else { [string]$entry.runner.label }
    Assert-Equal "$($entry.rid) release runner" $releaseRunnerLabel ([string]$config.runner)
    Assert-Equal "$($entry.rid) shell" $entry.runner.shell ([string]$config.shell)
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

    Assert-NonEmpty "$($entry.rid) runner.provider" $entry.runner.provider
    Assert-NonEmpty "$($entry.rid) runner.label" $entry.runner.label
    Assert-NonEmpty "$($entry.rid) runner.shell" $entry.runner.shell
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
}

Assert-Equal "required RID set" (@($matrix.product.requiredRids | ForEach-Object { [string]$_ } | Sort-Object)) @("win-x64")
Assert-Equal "conditional RID set" (@($matrix.product.conditionalRids | ForEach-Object { [string]$_ } | Sort-Object)) @("win-arm64")

$observedLocal = @($matrixEntries | Where-Object { $_.hardware.cpu.status -eq "observed_local" } | ForEach-Object { $_.rid })
Write-Output ("Target hardware matrix valid: {0} RIDs; active={1}; conditional={2}; observed_local={3}." -f $matrixEntries.Count, ($activeMatrixRids -join ","), (@($matrix.product.conditionalRids) -join ","), ($observedLocal -join ","))
