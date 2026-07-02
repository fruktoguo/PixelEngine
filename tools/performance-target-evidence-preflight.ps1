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
        Write-Error "Performance target evidence preflight failed: blocked_missing_target_performance_manifest"
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
        Write-Error "Performance target evidence preflight failed: blocked_missing_target_performance_manifest"
        exit 2
    }

    exit 0
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ((Get-JsonPropertyValue -Node $manifest -Name "schemaVersion") -ne 1) {
    throw "Performance target evidence manifest schemaVersion 必须为 1。"
}

$rawEntries = Get-JsonPropertyValue -Node $manifest -Name "evidence"
$entries = if ($null -eq $rawEntries) { @() } else { @($rawEntries) }
if ($entries.Count -eq 0) {
    $missing.Add("evidence[] 为空")
}

$entriesByScope = @{}
foreach ($entry in $entries) {
    $scope = [string](Get-JsonPropertyValue -Node $entry -Name "scope")
    if ([string]::IsNullOrWhiteSpace($scope)) {
        $missing.Add("evidence entry 缺少 scope")
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

Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope "avx512_downclock_net_loss"
Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope "hardware_counters_cache_branch"
Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope "frame_budget_target_hardware"

foreach ($rid in $rids) {
    $ridNode = Get-JsonPropertyValue -Node $cellsFrame -Name $rid
    if ($null -eq $ridNode) {
        $missing.Add("cellsFrame.$rid 缺失")
    }
    elseif (-not [bool](Get-JsonPropertyValue -Node $ridNode -Name "benchmarkDotNet")) {
        $missing.Add("cellsFrame.$rid 必须标记 benchmarkDotNet=true")
    }

    Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -Scope "cells_frame/$rid"
}

if ($missing.Count -gt 0) {
    $detail = "Target performance evidence preflight failed: manifest 存在，但 AVX-512 降频净损、6 RID cells/frame、帧预算或硬件计数器证据不完整。不得据此勾选 plan/16 的目标硬件阻塞项。"
    Write-PerformanceEvidenceReport -Path $reportPath -Status "blocked_missing_target_performance_scope_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @($missing) -Detail $detail
    Write-Host "Performance target evidence preflight blocked_missing_target_performance_scope_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        Write-Error "Performance target evidence preflight failed: blocked_missing_target_performance_scope_evidence"
        exit 5
    }

    exit 0
}

$detail = "Target performance evidence manifest is complete and SHA256 hashes matched. Human review still must confirm the evidence proves AVX-512 has no net downclock loss, 6 RID cells/frame targets were measured with BenchmarkDotNet on representative hardware, frame budgets meet plan/16 §5, and hardware counters include Cache Misses / Branch Mispredictions before plan/16 can be unblocked."
Write-PerformanceEvidenceReport -Path $reportPath -Status "target_performance_evidence_attached_pending_review" -ExitCode 2 -Evidence @($evidence) -Missing @($missing) -Detail $detail
Write-Host "Performance target evidence preflight target_performance_evidence_attached_pending_review. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if (-not $AllowBlocked) {
    Write-Error "Performance target evidence preflight failed: target_performance_evidence_attached_pending_review"
    exit 2
}

exit 0
