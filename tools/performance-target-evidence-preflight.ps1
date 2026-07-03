param(
    [string]$EvidenceManifestPath = "",
    [string]$Artifacts = "artifacts/performance-target-evidence-preflight",
    [switch]$AllowBlocked
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot {
    $directory = [System.IO.DirectoryInfo]::new((Get-Location).Path)
    while ($null -ne $directory) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName "PixelEngine.sln")) {
            return $directory.FullName
        }

        $directory = $directory.Parent
    }

    throw "无法从当前目录定位 PixelEngine.sln。"
}

function ConvertTo-RepositoryRelativePath {
    param(
        [string]$Root,
        [string]$Path
    )

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ($resolved.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
        return $resolved.Substring($Root.Length).TrimStart('\', '/')
    }

    return $resolved
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-MachineReadableFields {
    param([string]$Path)

    $fields = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($trimmed -match '^\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|?$') {
            $key = $Matches[1].Trim()
            $value = $Matches[2].Trim()
            if ($key -notin @("Key", "---", "metric", "scope") -and -not $key.StartsWith("---", [StringComparison]::Ordinal)) {
                $fields[$key] = $value
            }

            continue
        }

        if ($trimmed -match '^([A-Za-z][A-Za-z0-9_.-]*)\s*[:=]\s*(.+?)\s*$') {
            $fields[$Matches[1].Trim()] = $Matches[2].Trim()
        }
    }

    return $fields
}

function Get-RequiredField {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    if (-not $Fields.Contains($Name) -or [string]::IsNullOrWhiteSpace([string]$Fields[$Name])) {
        throw "$Scope 缺少机器可读字段 $Name"
    }

    return [string]$Fields[$Name]
}

function Get-RequiredBool {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    $value = Get-RequiredField -Fields $Fields -Name $Name -Scope $Scope
    $parsed = $false
    if ([bool]::TryParse($value, [ref]$parsed)) {
        return [bool]::Parse($value)
    }

    if ($value -in @("1", "yes", "present")) {
        return $true
    }

    if ($value -in @("0", "no", "missing")) {
        return $false
    }

    throw "$Scope $Name 必须为 true/false。"
}

function Get-RequiredDouble {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    $value = Get-RequiredField -Fields $Fields -Name $Name -Scope $Scope
    return [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-RequiredInt {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    $value = Get-RequiredField -Fields $Fields -Name $Name -Scope $Scope
    return [int]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-JsonPropertyNames {
    param([object]$Node)
    if ($null -eq $Node) {
        return @()
    }

    return @($Node.PSObject.Properties | ForEach-Object { $_.Name })
}

function Get-JsonPropertyValue {
    param(
        [object]$Node,
        [string]$Name
    )

    if ($null -eq $Node) {
        return $null
    }

    $property = $Node.PSObject.Properties | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Write-PerformanceEvidenceReport {
    param(
        [string]$Path,
        [string]$Status,
        [int]$ExitCode,
        [object[]]$Evidence,
        [string[]]$Missing,
        [string]$Detail
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# PixelEngine target performance evidence preflight")
    $lines.Add("")
    $lines.Add("status: $Status")
    $lines.Add("exit_code: $ExitCode")
    $lines.Add("")
    $lines.Add("## Detail")
    $lines.Add("")
    $lines.Add($Detail)
    $lines.Add("")

    if ($Evidence.Count -gt 0) {
        $lines.Add("## Evidence")
        $lines.Add("")
        foreach ($item in $Evidence) {
            $lines.Add("- scope=$($item.scope); path=$($item.path); sha256=$($item.sha256)")
        }
        $lines.Add("")
    }

    if ($Missing.Count -gt 0) {
        $lines.Add("## Missing")
        $lines.Add("")
        foreach ($item in $Missing) {
            $lines.Add("- $item")
        }
        $lines.Add("")
    }

    [System.IO.File]::WriteAllLines($Path, $lines, [System.Text.UTF8Encoding]::new($false))
}

function Add-ScopedEvidence {
    param(
        [System.Collections.Generic.List[object]]$Evidence,
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [hashtable]$EntriesByScope,
        [string]$Scope
    )

    if (-not $EntriesByScope.ContainsKey($Scope)) {
        $Missing.Add("缺少 evidence scope：$Scope")
        return
    }

    $entry = $EntriesByScope[$Scope]
    $path = [string](Get-JsonPropertyValue -Node $entry -Name "path")
    if ([string]::IsNullOrWhiteSpace($path)) {
        $Missing.Add("scope $Scope 缺少 path")
        return
    }

    $declaredHash = [string](Get-JsonPropertyValue -Node $entry -Name "sha256")
    if ([string]::IsNullOrWhiteSpace($declaredHash)) {
        $Missing.Add("scope $Scope 缺少 sha256")
        return
    }

    $resolved = if ([System.IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $Root $path }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        $Missing.Add("scope $Scope 指向文件不存在：$path")
        return
    }

    $actualHash = Get-FileSha256 -Path $resolved
    $expectedHash = $declaredHash.Trim().ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        $Missing.Add("scope $Scope sha256 不匹配：expected=$expectedHash actual=$actualHash")
        return
    }

    $Evidence.Add([pscustomobject]@{
        scope = $Scope
        path = ConvertTo-RepositoryRelativePath -Root $Root -Path $resolved
        sha256 = $actualHash
    })

    return $resolved
}

function Assert-TrueField {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    if (-not (Get-RequiredBool -Fields $Fields -Name $Name -Scope $Scope)) {
        throw "$Scope $Name 必须为 true。"
    }
}

function Assert-Avx512Evidence {
    param([string]$Path)

    $scope = "avx512_downclock_net_loss"
    $fields = Read-MachineReadableFields -Path $Path
    [void](Get-RequiredField -Fields $fields -Name "targetCpuName" -Scope $scope)
    [void](Get-RequiredField -Fields $fields -Name "dotnetVersion" -Scope $scope)
    Assert-TrueField -Fields $fields -Name "benchmarkDotNet" -Scope $scope
    Assert-TrueField -Fields $fields -Name "vector512HardwareAccelerated" -Scope $scope
    Assert-TrueField -Fields $fields -Name "avx512Enabled" -Scope $scope
    Assert-TrueField -Fields $fields -Name "noNetDownclockLoss" -Scope $scope
}

function Assert-HardwareCounterEvidence {
    param([string]$Path)

    $scope = "hardware_counters_cache_branch"
    $fields = Read-MachineReadableFields -Path $Path
    Assert-TrueField -Fields $fields -Name "benchmarkDotNet" -Scope $scope
    Assert-TrueField -Fields $fields -Name "elevatedEtwKernelSession" -Scope $scope
    Assert-TrueField -Fields $fields -Name "cacheMissesPresent" -Scope $scope
    Assert-TrueField -Fields $fields -Name "branchMispredictionsPresent" -Scope $scope

    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -notmatch 'Cache Misses' -or $content -notmatch 'Branch Mispredictions') {
        throw "$scope 必须包含 Cache Misses 与 Branch Mispredictions 列名。"
    }
}

function Assert-FrameBudgetEvidence {
    param([string]$Path)

    $scope = "frame_budget_target_hardware"
    $fields = Read-MachineReadableFields -Path $Path
    [void](Get-RequiredField -Fields $fields -Name "targetHardware" -Scope $scope)
    $sampleSeconds = Get-RequiredDouble -Fields $fields -Name "sampleSeconds" -Scope $scope
    $caP99Ms = Get-RequiredDouble -Fields $fields -Name "caP99Ms" -Scope $scope
    $renderP99Ms = Get-RequiredDouble -Fields $fields -Name "renderP99Ms" -Scope $scope
    $physicsP99Ms = Get-RequiredDouble -Fields $fields -Name "physicsP99Ms" -Scope $scope
    $logicAudioP99Ms = Get-RequiredDouble -Fields $fields -Name "logicAudioP99Ms" -Scope $scope

    if ($sampleSeconds -lt 60.0) {
        throw "$scope sampleSeconds 必须至少为 60 秒。"
    }

    if ($caP99Ms -gt 8.0) {
        throw "$scope caP99Ms 必须 <= 8ms。"
    }

    if ($renderP99Ms -gt 4.0) {
        throw "$scope renderP99Ms 必须 <= 4ms。"
    }

    if ($physicsP99Ms -gt 4.0) {
        throw "$scope physicsP99Ms 必须 <= 4ms。"
    }

    if ($logicAudioP99Ms -gt 1.0) {
        throw "$scope logicAudioP99Ms 必须 <= 1ms。"
    }
}

function Assert-CellsFrameEvidence {
    param(
        [string]$Path,
        [string]$Rid
    )

    $scope = "cells_frame/$Rid"
    $fields = Read-MachineReadableFields -Path $Path
    $reportedRid = Get-RequiredField -Fields $fields -Name "rid" -Scope $scope
    if (-not [string]::Equals($reportedRid, $Rid, [StringComparison]::Ordinal)) {
        throw "$scope rid 必须为 $Rid，实际为 $reportedRid。"
    }

    Assert-TrueField -Fields $fields -Name "benchmarkDotNet" -Scope $scope
    Assert-TrueField -Fields $fields -Name "representativeHardware" -Scope $scope
    $activeCellsPerFrame = Get-RequiredInt -Fields $fields -Name "activeCellsPerFrame" -Scope $scope
    $caFrameMs = Get-RequiredDouble -Fields $fields -Name "caFrameMs" -Scope $scope
    $measuredIterations = Get-RequiredInt -Fields $fields -Name "measuredIterations" -Scope $scope

    if ($activeCellsPerFrame -lt 2000000) {
        throw "$scope activeCellsPerFrame 必须至少为 2000000。"
    }

    if ($caFrameMs -gt 8.0) {
        throw "$scope caFrameMs 必须 <= 8ms。"
    }

    if ($measuredIterations -lt 3) {
        throw "$scope measuredIterations 必须至少为 3。"
    }
}

$root = Resolve-RepositoryRoot
$artifactRoot = if ([System.IO.Path]::IsPathRooted($Artifacts)) { $Artifacts } else { Join-Path $root $Artifacts }
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$reportPath = Join-Path $artifactRoot "performance-target-evidence-preflight.md"
$evidence = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
$rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")

if ([string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    $detail = "Target performance evidence preflight failed: 缺少 evidence manifest。本脚本只校验 AVX-512 降频净损、6 RID cells/frame、帧预算、硬件计数器报告的 scope/hash，不运行本机短样本，也不会把本机短样本当作验收通过。"
    Write-PerformanceEvidenceReport -Path $reportPath -Status "blocked_missing_target_performance_manifest" -ExitCode 2 -Evidence @($evidence) -Missing @("performance target evidence manifest 不存在") -Detail $detail
    Write-Host "Performance target evidence preflight blocked_missing_target_performance_manifest. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Performance target evidence preflight failed: blocked_missing_target_performance_manifest")
        exit 2
    }

    exit 0
}

$manifestPath = if ([System.IO.Path]::IsPathRooted($EvidenceManifestPath)) { $EvidenceManifestPath } else { Join-Path $root $EvidenceManifestPath }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    $detail = "Target performance evidence preflight failed: manifest 路径不存在。"
    Write-PerformanceEvidenceReport -Path $reportPath -Status "blocked_missing_target_performance_manifest" -ExitCode 2 -Evidence @($evidence) -Missing @("performance target evidence manifest 不存在：$EvidenceManifestPath") -Detail $detail
    Write-Host "Performance target evidence preflight blocked_missing_target_performance_manifest. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Performance target evidence preflight failed: blocked_missing_target_performance_manifest")
        exit 2
    }

    exit 0
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ((Get-JsonPropertyValue -Node $manifest -Name "schemaVersion") -ne 1) {
        throw "Performance target evidence manifest schemaVersion 必须为 1。"
    }
}
catch {
    $detail = "Target performance evidence preflight failed: evidence manifest JSON 或 schema 无效。不得据此勾选 plan/16 的目标硬件阻塞项。"
    Write-PerformanceEvidenceReport -Path $reportPath -Status "blocked_invalid_target_performance_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @("performance target evidence manifest 无效：$($_.Exception.Message)") -Detail $detail
    Write-Host "Performance target evidence preflight blocked_invalid_target_performance_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Performance target evidence preflight failed: blocked_invalid_target_performance_evidence")
        exit 5
    }

    exit 0
}

$rawEntries = Get-JsonPropertyValue -Node $manifest -Name "evidence"
$entries = if ($null -eq $rawEntries) { @() } else { @($rawEntries) }
if ($entries.Count -eq 0) {
    $missing.Add("evidence[] 为空")
}

$entriesByScope = @{}
$resolvedEvidencePaths = @{}
$requiredEvidenceScopes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$requiredEvidenceScopes.Add("avx512_downclock_net_loss") | Out-Null
$requiredEvidenceScopes.Add("hardware_counters_cache_branch") | Out-Null
$requiredEvidenceScopes.Add("frame_budget_target_hardware") | Out-Null
foreach ($rid in $rids) {
    $requiredEvidenceScopes.Add("cells_frame/$rid") | Out-Null
}

foreach ($entry in $entries) {
    $scope = [string](Get-JsonPropertyValue -Node $entry -Name "scope")
    if ([string]::IsNullOrWhiteSpace($scope)) {
        $missing.Add("evidence entry 缺少 scope")
        continue
    }

    if (-not $requiredEvidenceScopes.Contains($scope)) {
        $missing.Add("未知 evidence scope：$scope")
        continue
    }

    if ($entriesByScope.ContainsKey($scope)) {
        $missing.Add("重复 evidence scope：$scope")
        continue
    }

    $entriesByScope[$scope] = $entry
}

$cellsFrame = Get-JsonPropertyValue -Node $manifest -Name "cellsFrame"
$knownRidNodes = Get-JsonPropertyNames $cellsFrame
foreach ($rid in $knownRidNodes) {
    if ($rid -notin $rids) {
        $missing.Add("cellsFrame 包含未知 RID：$rid")
    }
}

$resolved = Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope "avx512_downclock_net_loss"
if (-not [string]::IsNullOrWhiteSpace($resolved)) { $resolvedEvidencePaths["avx512_downclock_net_loss"] = $resolved }
$resolved = Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope "hardware_counters_cache_branch"
if (-not [string]::IsNullOrWhiteSpace($resolved)) { $resolvedEvidencePaths["hardware_counters_cache_branch"] = $resolved }
$resolved = Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope "frame_budget_target_hardware"
if (-not [string]::IsNullOrWhiteSpace($resolved)) { $resolvedEvidencePaths["frame_budget_target_hardware"] = $resolved }

foreach ($rid in $rids) {
    $ridNode = Get-JsonPropertyValue -Node $cellsFrame -Name $rid
    if ($null -eq $ridNode) {
        $missing.Add("cellsFrame.$rid 缺失")
    }
    elseif (-not [bool](Get-JsonPropertyValue -Node $ridNode -Name "benchmarkDotNet")) {
        $missing.Add("cellsFrame.$rid 必须标记 benchmarkDotNet=true")
    }

    $scope = "cells_frame/$rid"
    $resolved = Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope $scope
    if (-not [string]::IsNullOrWhiteSpace($resolved)) { $resolvedEvidencePaths[$scope] = $resolved }
}

if ($missing.Count -eq 0) {
    try {
        Assert-Avx512Evidence -Path $resolvedEvidencePaths["avx512_downclock_net_loss"]
        Assert-HardwareCounterEvidence -Path $resolvedEvidencePaths["hardware_counters_cache_branch"]
        Assert-FrameBudgetEvidence -Path $resolvedEvidencePaths["frame_budget_target_hardware"]
        foreach ($rid in $rids) {
            Assert-CellsFrameEvidence -Path $resolvedEvidencePaths["cells_frame/$rid"] -Rid $rid
        }
    }
    catch {
        $missing.Add("目标性能 evidence 语义无效：$($_.Exception.Message)")
    }
}

if ($missing.Count -gt 0) {
    $detail = "Target performance evidence preflight failed: manifest 存在，但 AVX-512 降频净损、6 RID cells/frame、帧预算或硬件计数器证据不完整。不得据此勾选 plan/16 的目标硬件阻塞项。"
    Write-PerformanceEvidenceReport -Path $reportPath -Status "blocked_missing_target_performance_scope_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @($missing) -Detail $detail
    Write-Host "Performance target evidence preflight blocked_missing_target_performance_scope_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Performance target evidence preflight failed: blocked_missing_target_performance_scope_evidence")
        exit 5
    }

    exit 0
}

$detail = "Target performance evidence manifest is complete and SHA256 hashes matched. Human review still must confirm the evidence proves AVX-512 has no net downclock loss, 6 RID cells/frame targets were measured with BenchmarkDotNet on representative hardware, frame budgets meet plan/16 §5, and hardware counters include Cache Misses / Branch Mispredictions before plan/16 can be unblocked."
Write-PerformanceEvidenceReport -Path $reportPath -Status "target_performance_evidence_attached_pending_review" -ExitCode 2 -Evidence @($evidence) -Missing @($missing) -Detail $detail
Write-Host "Performance target evidence preflight target_performance_evidence_attached_pending_review. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if (-not $AllowBlocked) {
    [Console]::Error.WriteLine("Performance target evidence preflight failed: target_performance_evidence_attached_pending_review")
    exit 2
}

exit 0
