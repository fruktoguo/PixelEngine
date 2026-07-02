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
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Missing.Add("$Scope 缺少路径")
        return
    }

    $resolved = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $Root $Path }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        $Missing.Add("$Scope 文件不存在：$Path")
        return
    }

    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $resolved
    $Evidence.Add([pscustomobject]@{
        Scope = $Scope
        Path = ConvertTo-RelativePath -Root $Root -Path $resolved
        Sha256 = $hash.Hash
    })
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

$artifactRoot = Join-Path $root $Artifacts
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

Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "workflow_run" -Path ([string]$manifest.workflowRunReport)
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "github_release_upload" -Path ([string]$manifest.githubRelease.uploadReport)
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "deterministic_hash" -Path ([string]$manifest.deterministicHashReport)
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "r2r_lightup" -Path ([string]$manifest.r2rLightupReport)

foreach ($rid in $rids) {
    foreach ($channel in $channels) {
        $node = $manifest.artifacts.$rid.$channel
        if ($null -eq $node) {
            $missing.Add("artifacts.$rid.$channel 缺失")
            continue
        }

        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/publish" -Path ([string]$node.publishReport)
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/verify" -Path ([string]$node.verifyReport)
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/package" -Path ([string]$node.package)
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/checksum" -Path ([string]$node.checksum)

        if ($channel -eq "aot") {
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/simd_probe" -Path ([string]$node.simdProbe)
        }

        if ($rid.StartsWith("osx-", [StringComparison]::Ordinal)) {
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/codesign" -Path ([string]$node.codesignReport)
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/notarization" -Path ([string]$node.notarizationReport)
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

$detail = "Release evidence manifest is complete and SHA256 hashes were recorded. Human review still must confirm the reports prove 6 RID R2R/AOT outputs, AOT SIMD probes, R2R runtime light-up, macOS codesign/notarization, deterministic hashes and GitHub Release upload before plan/15 can be unblocked."
Write-ReleaseEvidenceReport -Path $reportPath -Status "release_evidence_attached_pending_review" -Evidence $evidence -Missing $missing -Detail $detail
Write-Host "Release evidence preflight release_evidence_attached_pending_review. Report: $reportPath"
