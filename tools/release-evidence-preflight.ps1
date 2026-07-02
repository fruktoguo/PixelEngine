param(
    [string]$EvidenceManifestPath = "",
    [string]$Artifacts = "artifacts/release-evidence-preflight",
    [switch]$AllowBlocked
)

$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot {
    $directory = Resolve-Path (Join-Path $PSScriptRoot "..")
    while ($null -ne $directory) {
        if (Test-Path (Join-Path $directory "PixelEngine.sln")) {
            return $directory.Path
        }

        $parent = Split-Path -Parent $directory
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $directory.Path) {
            break
        }

        $directory = Resolve-Path $parent
    }

    throw "无法定位 PixelEngine.sln。"
}

function ConvertTo-RelativePath {
    param([string]$Root, [string]$Path)

    $fullRoot = [IO.Path]::GetFullPath($Root)
    $fullPath = [IO.Path]::GetFullPath($Path)
    return [IO.Path]::GetRelativePath($fullRoot, $fullPath).Replace("\", "/")
}

function Add-EvidenceFile {
    param(
        [System.Collections.Generic.List[object]]$Evidence,
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Scope,
        [string]$Path,
        [string]$DeclaredSha256
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Missing.Add("$Scope 缺少路径")
        return
    }

    $existingScope = $Evidence | Where-Object { $_.Scope -eq $Scope } | Select-Object -First 1
    if ($null -ne $existingScope) {
        $Missing.Add("重复 evidence scope：$Scope")
        return
    }

    $resolved = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $Root $Path }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        $Missing.Add("$Scope 文件不存在：$Path")
        return
    }

    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $resolved
    if ([string]::IsNullOrWhiteSpace($DeclaredSha256)) {
        $Missing.Add("$Scope 缺少 sha256")
        return
    }

    $expectedHash = $DeclaredSha256.Trim().ToUpperInvariant()
    $actualHash = $hash.Hash.ToUpperInvariant()
    if ($expectedHash -ne $actualHash) {
        $Missing.Add("$Scope sha256 不匹配：expected=$expectedHash actual=$actualHash")
        return
    }

    $Evidence.Add([pscustomobject]@{
        Scope = $Scope
        Path = ConvertTo-RelativePath -Root $Root -Path $resolved
        Sha256 = $actualHash
    })
}

function Resolve-EvidencePath {
    param(
        [string]$Root,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    if ([IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $Root $Path
}

function Read-MarkdownEvidenceTable {
    param([string]$Path)

    $values = @{}
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $values
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -notmatch '^\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|$') {
            continue
        }

        $key = $Matches[1].Trim()
        $value = $Matches[2].Trim()
        if ([string]::IsNullOrWhiteSpace($key) -or $key -eq "Key" -or $key -match '^-+$') {
            continue
        }

        $values[$key] = $value
    }

    return $values
}

function Add-MarkdownEvidenceCheck {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Scope,
        [string]$Path,
        [hashtable]$ExpectedValues
    )

    $resolved = Resolve-EvidencePath -Root $Root -Path $Path
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return
    }

    $values = Read-MarkdownEvidenceTable -Path $resolved
    foreach ($key in $ExpectedValues.Keys) {
        $expected = [string]$ExpectedValues[$key]
        if (-not $values.ContainsKey($key)) {
            $Missing.Add("$Scope 报告缺少 $key 字段")
            continue
        }

        $actual = [string]$values[$key]
        if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            $Missing.Add("$Scope 报告 $key 必须为 $expected，实际为 $actual")
        }
    }
}

function Test-AotSimdEvidence {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Rid,
        [object]$Node
    )

    $expectedKind = if ($Rid.EndsWith("-x64", [StringComparison]::Ordinal)) { "x64_ymm_zmm" } else { "arm64_neon" }
    $actualKind = [string]$Node.simdProbeKind
    if ([string]::IsNullOrWhiteSpace($actualKind)) {
        $Missing.Add("artifacts.$Rid.aot.simdProbeKind 缺失，必须为 $expectedKind")
    }
    elseif ($actualKind -ne $expectedKind) {
        $Missing.Add("artifacts.$Rid.aot.simdProbeKind 必须为 $expectedKind，不能用 skip 或其它报告冒充 SIMD 证据")
    }

    $simdProbePath = [string]$Node.simdProbe
    $resolved = Resolve-EvidencePath -Root $Root -Path $simdProbePath
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return
    }

    $content = Get-Content -Raw -LiteralPath $resolved
    if ($content.Contains("skip", [StringComparison]::OrdinalIgnoreCase) -or
        $content.Contains("skipped", [StringComparison]::OrdinalIgnoreCase)) {
        $Missing.Add("artifacts.$Rid.aot.simdProbe 不能是 skip 报告：$simdProbePath")
    }

    if ($expectedKind -eq "x64_ymm_zmm" -and
        -not ($content.Contains("ymm", [StringComparison]::OrdinalIgnoreCase) -or
              $content.Contains("zmm", [StringComparison]::OrdinalIgnoreCase))) {
        $Missing.Add("artifacts.$Rid.aot.simdProbe 必须包含 ymm 或 zmm 证据：$simdProbePath")
    }

    if ($expectedKind -eq "arm64_neon" -and
        -not $content.Contains("NEON", [StringComparison]::OrdinalIgnoreCase)) {
        $Missing.Add("artifacts.$Rid.aot.simdProbe 必须包含 NEON 证据：$simdProbePath")
    }
}

function Get-JsonPropertyNames {
    param([object]$Node)

    if ($null -eq $Node) {
        return @()
    }

    return @($Node.PSObject.Properties | Select-Object -ExpandProperty Name)
}

function Write-ReleaseEvidenceReport {
    param(
        [string]$Path,
        [string]$Status,
        [array]$Evidence,
        [array]$Missing,
        [string]$Detail
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $lines = @(
        "# PixelEngine release evidence preflight",
        "",
        "| Key | Value |",
        "|---|---|",
        "| status | $Status |",
        "| required_rids | win-x64; win-arm64; linux-x64; linux-arm64; osx-x64; osx-arm64 |",
        "| required_channels | r2r; aot |",
        "",
        "## Evidence",
        "",
        "| Scope | Path | SHA256 |",
        "|---|---|---|"
    )

    if ($Evidence.Count -eq 0) {
        $lines += "| none | n/a | n/a |"
    }
    else {
        foreach ($item in $Evidence) {
            $lines += "| $($item.Scope) | $($item.Path) | $($item.Sha256) |"
        }
    }

    $lines += @(
        "",
        "## Missing",
        ""
    )

    if ($Missing.Count -eq 0) {
        $lines += "none"
    }
    else {
        foreach ($item in $Missing) {
            $lines += "- $item"
        }
    }

    $lines += @(
        "",
        "## Detail",
        "",
        $Detail
    )

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$root = Resolve-RepositoryRoot
Set-Location $root

$artifactRoot = if ([IO.Path]::IsPathRooted($Artifacts)) { $Artifacts } else { Join-Path $root $Artifacts }
$reportPath = Join-Path $artifactRoot "release-evidence-preflight.md"
$evidence = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()

if ([string]::IsNullOrWhiteSpace($EvidenceManifestPath) -or -not (Test-Path -LiteralPath $EvidenceManifestPath -PathType Leaf)) {
    $detail = "Release evidence preflight failed: 缺少 release evidence manifest。本脚本不生成发布证据，只校验 6 RID × 2 channel、AOT SIMD、R2R light-up、macOS signing/notarization 与 GitHub Release 上传证据是否齐全。"
    Write-ReleaseEvidenceReport -Path $reportPath -Status "blocked_missing_release_manifest" -Evidence $evidence -Missing @("release evidence manifest 不存在") -Detail $detail
    Write-Host "Release evidence preflight blocked_missing_release_manifest. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 2
}

$manifest = Get-Content -Raw -LiteralPath $EvidenceManifestPath | ConvertFrom-Json
$rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
$channels = @("r2r", "aot")

if ([int]$manifest.schemaVersion -ne 1) {
    $missing.Add("schemaVersion 必须为 1")
}

foreach ($ridName in Get-JsonPropertyNames $manifest.artifacts) {
    if ($ridName -notin $rids) {
        $missing.Add("artifacts 包含未知 RID：$ridName")
    }
}

foreach ($ridName in Get-JsonPropertyNames $manifest.artifacts) {
    $ridNode = $manifest.artifacts.$ridName
    foreach ($channelName in Get-JsonPropertyNames $ridNode) {
        if ($channelName -notin $channels) {
            $missing.Add("artifacts.$ridName 包含未知 channel：$channelName")
        }
    }
}

$workflowRunReport = [string]$manifest.workflowRunReport
$githubReleaseUploadReport = [string]$manifest.githubRelease.uploadReport
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "workflow_run" -Path $workflowRunReport -DeclaredSha256 ([string]$manifest.workflowRunSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "workflow_run" -Path $workflowRunReport -ExpectedValues @{ conclusion = "success" }
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "github_release_upload" -Path $githubReleaseUploadReport -DeclaredSha256 ([string]$manifest.githubRelease.uploadSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "github_release_upload" -Path $githubReleaseUploadReport -ExpectedValues @{ conclusion = "success" }
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "deterministic_hash" -Path ([string]$manifest.deterministicHashReport) -DeclaredSha256 ([string]$manifest.deterministicHashSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "deterministic_hash" -Path ([string]$manifest.deterministicHashReport) -ExpectedValues @{ conclusion = "success" }
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "r2r_lightup" -Path ([string]$manifest.r2rLightupReport) -DeclaredSha256 ([string]$manifest.r2rLightupSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "r2r_lightup" -Path ([string]$manifest.r2rLightupReport) -ExpectedValues @{ conclusion = "success" }

foreach ($rid in $rids) {
    foreach ($channel in $channels) {
        $node = $manifest.artifacts.$rid.$channel
        if ($null -eq $node) {
            $missing.Add("artifacts.$rid.$channel 缺失")
            continue
        }

        $publishReportPath = [string]$node.publishReport
        $verifyReportPath = [string]$node.verifyReport
        $packageReportPath = [string]$node.packageReport
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/publish" -Path $publishReportPath -DeclaredSha256 ([string]$node.publishSha256)
        Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/publish" -Path $publishReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/verify" -Path $verifyReportPath -DeclaredSha256 ([string]$node.verifySha256)
        Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/verify" -Path $verifyReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/package_report" -Path $packageReportPath -DeclaredSha256 ([string]$node.packageReportSha256)
        Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/package_report" -Path $packageReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/package" -Path ([string]$node.package) -DeclaredSha256 ([string]$node.packageSha256)
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/checksum" -Path ([string]$node.checksum) -DeclaredSha256 ([string]$node.checksumSha256)

        if ($channel -eq "aot") {
            $simdProbePath = [string]$node.simdProbe
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/simd_probe" -Path $simdProbePath -DeclaredSha256 ([string]$node.simdProbeSha256)
            Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/simd_probe" -Path $simdProbePath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
            Test-AotSimdEvidence -Missing $missing -Root $root -Rid $rid -Node $node
        }

        if ($rid.StartsWith("osx-", [StringComparison]::Ordinal)) {
            $codesignReportPath = [string]$node.codesignReport
            $notarizationReportPath = [string]$node.notarizationReport
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/codesign" -Path $codesignReportPath -DeclaredSha256 ([string]$node.codesignSha256)
            Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/codesign" -Path $codesignReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/notarization" -Path $notarizationReportPath -DeclaredSha256 ([string]$node.notarizationSha256)
            Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/notarization" -Path $notarizationReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
        }
    }
}

if ($missing.Count -gt 0) {
    $detail = "Release evidence preflight failed: release manifest 存在，但外部证据不完整。缺失项必须由 release workflow、目标 runner、macOS signing/notary 或 GitHub Release 上传结果补齐；不得据此勾选 plan/15 阻塞项。"
    Write-ReleaseEvidenceReport -Path $reportPath -Status "blocked_missing_release_scope_evidence" -Evidence $evidence -Missing $missing -Detail $detail
    Write-Host "Release evidence preflight blocked_missing_release_scope_evidence. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 5
}

$detail = "Release evidence manifest is complete, declared SHA256 hashes matched, and markdown evidence fields reported success for required jobs. Human review still must confirm the reports prove 6 RID R2R/AOT outputs, AOT SIMD probes, R2R runtime light-up, macOS codesign/notarization, deterministic hashes and GitHub Release upload before plan/15 can be unblocked."
Write-ReleaseEvidenceReport -Path $reportPath -Status "release_evidence_attached_pending_review" -Evidence $evidence -Missing $missing -Detail $detail
Write-Host "Release evidence preflight release_evidence_attached_pending_review. Report: $reportPath"

if (-not $AllowBlocked) {
    [Console]::Error.WriteLine("Release evidence preflight failed: release_evidence_attached_pending_review")
    exit 2
}

exit 0
