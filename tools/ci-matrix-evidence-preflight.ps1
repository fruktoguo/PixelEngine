param(
    [string]$EvidenceManifestPath = "",
    [string]$Artifacts = "artifacts/ci-matrix-evidence-preflight",
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

function Add-EvidenceFile {
    param(
        [System.Collections.Generic.List[object]]$Evidence,
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Scope,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Missing.Add("scope $Scope 缺少 path")
        return
    }

    $resolved = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $Root $Path }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        $Missing.Add("scope $Scope 指向文件不存在：$Path")
        return
    }

    $Evidence.Add([pscustomobject]@{
        scope = $Scope
        path = ConvertTo-RepositoryRelativePath -Root $Root -Path $resolved
        sha256 = Get-FileSha256 -Path $resolved
    })
}

function Get-JsonPropertyNames {
    param([object]$Node)
    if ($null -eq $Node) {
        return @()
    }

    return @($Node.PSObject.Properties | ForEach-Object { $_.Name })
}

function Write-CiEvidenceReport {
    param(
        [string]$Path,
        [string]$Status,
        [int]$ExitCode,
        [object[]]$Evidence,
        [string[]]$Missing,
        [string]$Detail
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# PixelEngine CI matrix evidence preflight")
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

$root = Resolve-RepositoryRoot
$artifactRoot = if ([System.IO.Path]::IsPathRooted($Artifacts)) { $Artifacts } else { Join-Path $root $Artifacts }
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$reportPath = Join-Path $artifactRoot "ci-matrix-evidence-preflight.md"
$evidence = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
$rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
$testRids = @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
$verifyRids = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")

if ([string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    $detail = "CI matrix evidence preflight failed: 缺少 evidence manifest。本脚本只校验 GitHub Actions 6-RID build/test、benchmark guard 与 publish verify 运行证据，不生成 CI 运行结果。"
    Write-CiEvidenceReport -Path $reportPath -Status "blocked_missing_ci_manifest" -ExitCode 2 -Evidence @($evidence) -Missing @("ci evidence manifest 不存在") -Detail $detail
    Write-Host "CI matrix evidence preflight blocked_missing_ci_manifest. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        Write-Error "CI matrix evidence preflight failed: blocked_missing_ci_manifest"
        exit 2
    }

    exit 0
}

$manifestPath = if ([System.IO.Path]::IsPathRooted($EvidenceManifestPath)) { $EvidenceManifestPath } else { Join-Path $root $EvidenceManifestPath }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    $detail = "CI matrix evidence preflight failed: manifest 路径不存在。"
    Write-CiEvidenceReport -Path $reportPath -Status "blocked_missing_ci_manifest" -ExitCode 2 -Evidence @($evidence) -Missing @("ci evidence manifest 不存在：$EvidenceManifestPath") -Detail $detail
    Write-Host "CI matrix evidence preflight blocked_missing_ci_manifest. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        Write-Error "CI matrix evidence preflight failed: blocked_missing_ci_manifest"
        exit 2
    }

    exit 0
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "CI evidence manifest schemaVersion 必须为 1。"
}

$knownBuildRids = Get-JsonPropertyNames $manifest.buildTest
foreach ($rid in $knownBuildRids) {
    if ($rid -notin $rids) {
        $missing.Add("buildTest 包含未知 RID：$rid")
    }
}

$knownVerifyRids = Get-JsonPropertyNames $manifest.verifyPublish
foreach ($rid in $knownVerifyRids) {
    if ($rid -notin $verifyRids) {
        $missing.Add("verifyPublish 包含未知 RID：$rid")
    }
}

Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "workflow_run" -Path ([string]$manifest.workflowRunReport)
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "benchmark_guard" -Path ([string]$manifest.benchmarkGuard.report)

foreach ($rid in $rids) {
    $node = $manifest.buildTest.$rid
    if ($null -eq $node) {
        $missing.Add("buildTest.$rid 缺失")
        continue
    }

    Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "build_test/$rid/report" -Path ([string]$node.report)
    if ($rid -in $testRids -and -not [bool]$node.testsRan) {
        $missing.Add("buildTest.$rid 必须标记 testsRan=true")
    }

    if ($rid -eq "win-arm64" -and [bool]$node.testsRan) {
        $missing.Add("buildTest.win-arm64 当前 CI 设计应为 build-only，不能伪装成真实 arm64 测试")
    }
}

foreach ($rid in $verifyRids) {
    $node = $manifest.verifyPublish.$rid
    if ($null -eq $node) {
        $missing.Add("verifyPublish.$rid 缺失")
        continue
    }

    Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "verify_publish/$rid/report" -Path ([string]$node.report)
}

if ($missing.Count -gt 0) {
    $detail = "CI matrix evidence preflight failed: manifest 存在，但 6-RID build/test、benchmark guard 或 publish verify 证据不完整。不得据此勾选 plan/14 的 CI 矩阵阻塞项。"
    Write-CiEvidenceReport -Path $reportPath -Status "blocked_missing_ci_scope_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @($missing) -Detail $detail
    Write-Host "CI matrix evidence preflight blocked_missing_ci_scope_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        Write-Error "CI matrix evidence preflight failed: blocked_missing_ci_scope_evidence"
        exit 5
    }

    exit 0
}

$detail = "CI matrix evidence manifest is complete and SHA256 hashes were recorded. Human review still must confirm the GitHub Actions run proves 6-RID build, available-RID dotnet test, benchmark guard, and R2R/AOT publish verify before plan/14 can be unblocked."
Write-CiEvidenceReport -Path $reportPath -Status "ci_matrix_evidence_attached_pending_review" -ExitCode 2 -Evidence @($evidence) -Missing @($missing) -Detail $detail
Write-Host "CI matrix evidence preflight ci_matrix_evidence_attached_pending_review. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if (-not $AllowBlocked) {
    Write-Error "CI matrix evidence preflight failed: ci_matrix_evidence_attached_pending_review"
    exit 2
}

exit 0
