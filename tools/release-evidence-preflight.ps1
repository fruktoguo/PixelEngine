param(
    [string]$EvidenceManifestPath = "",
    [string]$Artifacts = "artifacts/release-evidence-preflight",
    [string]$ActiveRids = "",
    [int]$ExpectedPackageCount = 0,
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

function Get-ExpectedRunIdentity {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$WorkflowRunReport
    )

    $resolved = Resolve-EvidencePath -Root $Root -Path $WorkflowRunReport
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return $null
    }

    $values = Read-MarkdownEvidenceTable -Path $resolved
    foreach ($key in @("run_id", "sha", "workflow", "run_attempt")) {
        if (-not $values.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string]$values[$key])) {
            $Missing.Add("workflow_run 报告缺少 $key 字段")
        }
    }

    if (-not $values.ContainsKey("run_id") -or
        -not $values.ContainsKey("sha") -or
        -not $values.ContainsKey("workflow") -or
        -not $values.ContainsKey("run_attempt")) {
        return $null
    }

    return [pscustomobject]@{
        RunId = [string]$values["run_id"]
        Sha = [string]$values["sha"]
        Workflow = [string]$values["workflow"]
        RunAttempt = [string]$values["run_attempt"]
    }
}

function Add-WorkflowRunMetadataCheck {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [hashtable]$Values
    )

    foreach ($key in @("workflow", "event", "run_attempt")) {
        if (-not $Values.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string]$Values[$key])) {
            $Missing.Add("workflow_run 报告缺少 $key 字段")
        }
    }

    if ($Values.ContainsKey("workflow") -and
        -not [string]::Equals([string]$Values["workflow"], "Release", [StringComparison]::Ordinal)) {
        $Missing.Add("workflow_run 报告 workflow 必须为 Release，实际为 $($Values["workflow"])")
    }

    if ($Values.ContainsKey("event")) {
        $event = [string]$Values["event"]
        if ($event -ne "push") {
            $Missing.Add("workflow_run 报告 event 必须为 push/tag push，实际为 $event")
        }
    }

    if ($Values.ContainsKey("run_attempt")) {
        $runAttempt = 0
        if (-not [int]::TryParse([string]$Values["run_attempt"], [ref]$runAttempt) -or $runAttempt -lt 1) {
            $Missing.Add("workflow_run 报告 run_attempt 必须为 >= 1 的整数，实际为 $($Values["run_attempt"])")
        }
    }
}

function Add-RunIdentityCheck {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Scope,
        [string]$Path,
        [object]$ExpectedIdentity
    )

    if ($null -eq $ExpectedIdentity) {
        return
    }

    $resolved = Resolve-EvidencePath -Root $Root -Path $Path
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return
    }

    $values = Read-MarkdownEvidenceTable -Path $resolved
    foreach ($key in @("run_id", "sha", "workflow", "run_attempt")) {
        if (-not $values.ContainsKey($key)) {
            $Missing.Add("$Scope 报告缺少 $key 字段，不能证明与 workflow_run 同源")
            continue
        }

        $actual = [string]$values[$key]
        $expected = switch ($key) {
            "run_id" { [string]$ExpectedIdentity.RunId }
            "sha" { [string]$ExpectedIdentity.Sha }
            "workflow" { [string]$ExpectedIdentity.Workflow }
            "run_attempt" { [string]$ExpectedIdentity.RunAttempt }
        }
        if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            $Missing.Add("$Scope 报告 $key 必须与 workflow_run 一致：expected=$expected actual=$actual")
        }
    }
}

function Get-ReleaseTagVersion {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$WorkflowRunReport,
        [string]$UploadReport
    )

    $workflowPath = Resolve-EvidencePath -Root $Root -Path $WorkflowRunReport
    $uploadPath = Resolve-EvidencePath -Root $Root -Path $UploadReport
    if ([string]::IsNullOrWhiteSpace($workflowPath) -or
        [string]::IsNullOrWhiteSpace($uploadPath) -or
        -not (Test-Path -LiteralPath $workflowPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $uploadPath -PathType Leaf)) {
        return ""
    }

    $workflow = Read-MarkdownEvidenceTable -Path $workflowPath
    $upload = Read-MarkdownEvidenceTable -Path $uploadPath

    $version = ""
    $expectedTag = ""
    if (-not $workflow.ContainsKey("ref")) {
        $Missing.Add("workflow_run 报告缺少 ref 字段")
    }
    else {
        $ref = [string]$workflow["ref"]
        if ($ref -notmatch '^refs/tags/v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$') {
            $Missing.Add("workflow_run ref 必须是 refs/tags/v<semver>：actual=$ref")
        }
        else {
            $version = $Matches["version"]
            $expectedTag = "v$version"
        }
    }

    foreach ($key in @("tag", "release_tag")) {
        if (-not $upload.ContainsKey($key)) {
            $Missing.Add("github_release_upload 报告缺少 $key 字段")
        }
    }

    if ($upload.ContainsKey("release_tag")) {
        $releaseTag = [string]$upload["release_tag"]
        if (-not [string]::Equals($releaseTag, "true", [StringComparison]::OrdinalIgnoreCase)) {
            $Missing.Add("github_release_upload release_tag 必须为 true，实际为 $releaseTag")
        }
    }

    if ($upload.ContainsKey("tag")) {
        $actualTag = [string]$upload["tag"]
        if (-not [string]::IsNullOrWhiteSpace($expectedTag) -and
            -not [string]::Equals($actualTag, $expectedTag, [StringComparison]::Ordinal)) {
            $Missing.Add("github_release_upload tag 必须与 workflow_run ref 一致：expected=$expectedTag actual=$actualTag")
        }
    }

    return $version
}

function Test-DeterministicHashRows {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Path,
        [array]$RequiredRids,
        [array]$RequiredChannels
    )

    $resolved = Resolve-EvidencePath -Root $Root -Path $Path
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return
    }

    $seen = @{}
    $rowCount = 0
    foreach ($line in Get-Content -LiteralPath $resolved) {
        if ($line -notmatch '^\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|$') {
            continue
        }

        $rid = $Matches[1].Trim()
        $channel = $Matches[2].Trim()
        $result = $Matches[3].Trim()
        $detail = $Matches[4].Trim()
        if ([string]::IsNullOrWhiteSpace($rid) -or
            $rid -eq "RID" -or
            $rid -match '^-+$') {
            continue
        }

        $rowCount++
        if ($rid -notin $RequiredRids) {
            $Missing.Add("deterministic_hash 报告包含未知 RID 行：$rid")
            continue
        }

        if ($channel -notin $RequiredChannels) {
            $Missing.Add("deterministic_hash 报告包含未知 channel 行：$rid/$channel")
            continue
        }

        $key = "$rid/$channel"
        if ($seen.ContainsKey($key)) {
            $Missing.Add("deterministic_hash 报告重复 $key 行")
            continue
        }

        $seen[$key] = $true
        if (-not [string]::Equals($result, "match", [StringComparison]::OrdinalIgnoreCase)) {
            $Missing.Add("deterministic_hash 报告 $key result 必须为 match，实际为 $result：$detail")
        }
    }

    if ($rowCount -eq 0) {
        $Missing.Add("deterministic_hash 报告缺少 RID/channel/result 明细表")
    }

    foreach ($rid in $RequiredRids) {
        foreach ($channel in $RequiredChannels) {
            $key = "$rid/$channel"
            if (-not $seen.ContainsKey($key)) {
                $Missing.Add("deterministic_hash 报告缺少 $key match 行")
            }
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

    $reportValues = Read-MarkdownEvidenceTable -Path $resolved
    $reportKind = if ($reportValues.ContainsKey("simdProbeKind")) { [string]$reportValues["simdProbeKind"] } else { "" }
    if ([string]::IsNullOrWhiteSpace($reportKind)) {
        $Missing.Add("artifacts.$Rid.aot.simdProbe 报告缺少 simdProbeKind 字段，必须为 $expectedKind")
    }
    elseif (-not [string]::Equals($reportKind, $expectedKind, [StringComparison]::Ordinal)) {
        $Missing.Add("artifacts.$Rid.aot.simdProbe 报告 simdProbeKind 必须为 $expectedKind，实际为 $reportKind")
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

function Test-ReleaseChecksumRows {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Path,
        [hashtable]$ExpectedPackages
    )

    $resolved = Resolve-EvidencePath -Root $Root -Path $Path
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return
    }

    $seen = @{}
    $rowCount = 0
    foreach ($line in Get-Content -LiteralPath $resolved) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($line -notmatch '^\s*([0-9a-fA-F]{64})\s+(.+?)\s*$') {
            $Missing.Add("SHA256SUMS 包含无效行：$line")
            continue
        }

        $hash = $Matches[1].ToUpperInvariant()
        $name = $Matches[2].Trim()
        $rowCount++

        if ($name.Contains("/", [StringComparison]::Ordinal) -or $name.Contains("\", [StringComparison]::Ordinal)) {
            $Missing.Add("SHA256SUMS 只允许 package 文件名，不能包含路径：$name")
            continue
        }

        if ($name -notmatch '^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$') {
            $Missing.Add("SHA256SUMS 包含非发行包条目：$name")
            continue
        }

        if ($seen.ContainsKey($name)) {
            $Missing.Add("SHA256SUMS 包含重复条目：$name")
            continue
        }

        $seen[$name] = $true
        if (-not $ExpectedPackages.ContainsKey($name)) {
            $Missing.Add("SHA256SUMS 包含 manifest 未声明的 package：$name")
            continue
        }

        $expectedHash = [string]$ExpectedPackages[$name]
        if (-not [string]::Equals($hash, $expectedHash.ToUpperInvariant(), [StringComparison]::OrdinalIgnoreCase)) {
            $Missing.Add("SHA256SUMS $name hash 必须匹配 packageSha256：expected=$expectedHash actual=$hash")
        }
    }

    if ($rowCount -eq 0) {
        $Missing.Add("SHA256SUMS 缺少 package hash 行")
    }

    foreach ($name in $ExpectedPackages.Keys) {
        if ($name -notmatch '^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$') {
            $Missing.Add("manifest package 文件名不符合发行包命名：$name")
            continue
        }

        if (-not $seen.ContainsKey($name)) {
            $Missing.Add("SHA256SUMS 缺少 package 条目：$name")
        }
    }
}

function Test-GitHubReleaseUploadedAssets {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [string]$Path,
        [hashtable]$ExpectedPackages,
        [string]$ChecksumSha256,
        [string]$ExpectedTag
    )

    $resolved = Resolve-EvidencePath -Root $Root -Path $Path
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        return
    }

    $values = Read-MarkdownEvidenceTable -Path $resolved
    $expectedAssets = @{}
    foreach ($name in $ExpectedPackages.Keys) {
        $expectedAssets[$name] = ([string]$ExpectedPackages[$name]).ToUpperInvariant()
    }

    if ([string]::IsNullOrWhiteSpace($ChecksumSha256)) {
        $Missing.Add("github_release_upload 无法校验 SHA256SUMS 上传证据：manifest 缺少 checksumSha256")
    }
    else {
        $expectedAssets["SHA256SUMS"] = $ChecksumSha256.ToUpperInvariant()
    }

    if (-not $values.ContainsKey("uploaded_asset_count")) {
        $Missing.Add("github_release_upload 报告缺少 uploaded_asset_count 字段")
    }
    else {
        $rawCount = [string]$values["uploaded_asset_count"]
        $actualCount = 0
        if (-not [int]::TryParse($rawCount, [ref]$actualCount)) {
            $Missing.Add("github_release_upload uploaded_asset_count 必须是整数，实际为 $rawCount")
        }
        elseif ($actualCount -ne $expectedAssets.Count) {
            $Missing.Add("github_release_upload uploaded_asset_count 必须为 $($expectedAssets.Count)，实际为 $actualCount")
        }
    }

    foreach ($name in $expectedAssets.Keys) {
        $key = "asset/$name"
        if (-not $values.ContainsKey($key)) {
            if ($name -eq "SHA256SUMS") {
                $Missing.Add("github_release_upload 缺少 SHA256SUMS 上传证据")
            }
            else {
                $Missing.Add("github_release_upload 缺少上传 asset：$name")
            }

            continue
        }

        $actualHash = ([string]$values[$key]).Trim().ToUpperInvariant()
        $expectedHash = ([string]$expectedAssets[$name]).Trim().ToUpperInvariant()
        if (-not [string]::Equals($actualHash, $expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            $Missing.Add("github_release_upload asset hash 不匹配：$name expected=$expectedHash actual=$actualHash")
        }

        $downloadUrlKey = "browser_download_url/$name"
        if (-not $values.ContainsKey($downloadUrlKey)) {
            $Missing.Add("github_release_upload 缺少 browser_download_url：$name")
        }
        else {
            $downloadUrl = ([string]$values[$downloadUrlKey]).Trim()
            $escapedName = [regex]::Escape($name)
            $tagPattern = if ([string]::IsNullOrWhiteSpace($ExpectedTag)) { "[^/]+" } else { [regex]::Escape($ExpectedTag) }
            if ($downloadUrl -notmatch "^https://github\.com/.+/releases/download/$tagPattern/$escapedName$") {
                $Missing.Add("github_release_upload browser_download_url 必须指向 GitHub Release 下载资产：$name actual=$downloadUrl")
            }
        }
    }

    foreach ($key in $values.Keys) {
        $keyText = [string]$key
        if (-not $keyText.StartsWith("asset/", [StringComparison]::Ordinal)) {
            continue
        }

        $name = $keyText.Substring("asset/".Length)
        if (-not $expectedAssets.ContainsKey($name)) {
            $Missing.Add("github_release_upload 包含 manifest 未声明的上传 asset：$name")
        }
    }

    foreach ($key in $values.Keys) {
        $keyText = [string]$key
        if (-not $keyText.StartsWith("browser_download_url/", [StringComparison]::Ordinal)) {
            continue
        }

        $name = $keyText.Substring("browser_download_url/".Length)
        if (-not $expectedAssets.ContainsKey($name)) {
            $Missing.Add("github_release_upload 包含 manifest 未声明的 browser_download_url：$name")
        }
    }
}

function Test-PackageVersionsMatchReleaseTag {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$ReleaseVersion,
        [hashtable]$ExpectedPackages
    )

    if ([string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        return
    }

    foreach ($name in $ExpectedPackages.Keys) {
        if ($name -notmatch '^PixelEngine-Demo-(?<version>.+)-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$') {
            continue
        }

        $packageVersion = $Matches["version"]
        if (-not [string]::Equals($packageVersion, $ReleaseVersion, [StringComparison]::Ordinal)) {
            $Missing.Add("package 文件名版本必须与 release tag 一致：$name expected=$ReleaseVersion actual=$packageVersion")
        }
    }
}

function Test-PackageNameMatchesArtifactNode {
    param(
        [System.Collections.Generic.List[string]]$Missing,
        [string]$PackageName,
        [string]$Rid,
        [string]$Channel,
        [string]$ReleaseVersion
    )

    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        $Missing.Add("artifacts.$Rid.$Channel.package 缺少 package 文件名")
        return
    }

    $expectedExtension = if ($Rid.StartsWith("win-", [StringComparison]::Ordinal)) { "zip" } else { "tar.gz" }
    $pattern = "^PixelEngine-Demo-(?<version>.+)-$([regex]::Escape($Rid))-$([regex]::Escape($Channel))\.$([regex]::Escape($expectedExtension))$"
    if ($PackageName -notmatch $pattern) {
        $Missing.Add("artifacts.$Rid.$Channel.package 文件名必须匹配 PixelEngine-Demo-<version>-$Rid-$Channel.$expectedExtension，实际为 $PackageName")
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        $packageVersion = $Matches["version"]
        if (-not [string]::Equals($packageVersion, $ReleaseVersion, [StringComparison]::Ordinal)) {
            $Missing.Add("artifacts.$Rid.$Channel.package 文件名版本必须与 release tag 一致：expected=$ReleaseVersion actual=$packageVersion")
        }
    }
}

function Get-JsonPropertyNames {
    param([object]$Node)

    if ($null -eq $Node) {
        return @()
    }

    return @($Node.PSObject.Properties | Select-Object -ExpandProperty Name)
}

function Resolve-ActiveRids {
    param(
        [string]$Root,
        [string]$Value
    )

    $validRids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        $requested = @($Value -split "[,\s]+" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
        if ($requested.Count -eq 0) {
            throw "-ActiveRids 不能为空。"
        }

        foreach ($rid in $requested) {
            if ($rid -notin $validRids) {
                throw "未知 active RID: $rid"
            }
        }

        return $requested
    }

    $configPath = Join-Path $Root "tools/release-rids.json"
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        return $validRids
    }

    $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
    $active = @($config.rids | Where-Object { [bool]$_.active } | ForEach-Object { [string]$_.rid })
    if ($active.Count -eq 0) {
        throw "release-rids.json active RID 集为空。"
    }

    foreach ($rid in $active) {
        if ($rid -notin $validRids) {
            throw "release-rids.json 包含未知 active RID: $rid"
        }
    }

    return $active
}

function Write-ReleaseEvidenceReport {
    param(
        [string]$Path,
        [string]$Status,
        [array]$Evidence,
        [array]$Missing,
        [array]$RequiredRids,
        [array]$RequiredChannels,
        [string]$Detail
    )

    $ridText = if ($RequiredRids.Count -gt 0) { $RequiredRids -join "; " } else { "n/a" }
    $channelText = if ($RequiredChannels.Count -gt 0) { $RequiredChannels -join "; " } else { "n/a" }

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
        "| required_rids | $ridText |",
        "| required_channels | $channelText |",
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
$channels = @("r2r", "aot")
$rids = Resolve-ActiveRids -Root $root -Value $ActiveRids
$expectedPackageCount = if ($ExpectedPackageCount -gt 0) { $ExpectedPackageCount } else { $rids.Count * $channels.Count }

if ([string]::IsNullOrWhiteSpace($EvidenceManifestPath) -or -not (Test-Path -LiteralPath $EvidenceManifestPath -PathType Leaf)) {
    $detail = "Release evidence preflight failed: 缺少 release evidence manifest。本脚本不生成发布证据，只校验 active RID × channel、AOT SIMD、R2R light-up、macOS signing/notarization 与 GitHub Release 上传证据是否齐全。"
    Write-ReleaseEvidenceReport -Path $reportPath -Status "blocked_missing_release_manifest" -Evidence $evidence -Missing @("release evidence manifest 不存在") -RequiredRids $rids -RequiredChannels $channels -Detail $detail
    Write-Host "Release evidence preflight blocked_missing_release_manifest. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 2
}

$manifest = $null
try {
    $manifest = Get-Content -Raw -LiteralPath $EvidenceManifestPath | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1) {
        throw "release evidence manifest schemaVersion 必须为 1"
    }
}
catch {
    $reason = "release evidence manifest 无效：$($_.Exception.Message)"
    $detail = "Release evidence preflight failed: manifest JSON 无法解析或 schemaVersion 不受支持。不得据此勾选 plan/15 阻塞项。"
    Write-ReleaseEvidenceReport -Path $reportPath -Status "blocked_invalid_release_evidence" -Evidence $evidence -Missing @($reason) -RequiredRids $rids -RequiredChannels $channels -Detail $detail
    Write-Host "Release evidence preflight blocked_invalid_release_evidence. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 5
}

$expectedChecksumPackages = @{}
$checksumPaths = @{}
$checksumSha256ByPath = @{}

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
$artifactAuditReport = [string]$manifest.artifactAuditReport
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "workflow_run" -Path $workflowRunReport -DeclaredSha256 ([string]$manifest.workflowRunSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "workflow_run" -Path $workflowRunReport -ExpectedValues @{ conclusion = "success" }
$workflowRunValues = Read-MarkdownEvidenceTable -Path (Resolve-EvidencePath -Root $root -Path $workflowRunReport)
Add-WorkflowRunMetadataCheck -Missing $missing -Values $workflowRunValues
$expectedRunIdentity = Get-ExpectedRunIdentity -Missing $missing -Root $root -WorkflowRunReport $workflowRunReport
$releaseVersion = Get-ReleaseTagVersion -Missing $missing -Root $root -WorkflowRunReport $workflowRunReport -UploadReport $githubReleaseUploadReport
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "artifact_audit" -Path $artifactAuditReport -DeclaredSha256 ([string]$manifest.artifactAuditSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "artifact_audit" -Path $artifactAuditReport -ExpectedValues @{
    conclusion = "success"
    require_all = "true"
    package_count = $expectedPackageCount.ToString([Globalization.CultureInfo]::InvariantCulture)
    expanded_package_count = $expectedPackageCount.ToString([Globalization.CultureInfo]::InvariantCulture)
    rids = ($rids -join ",")
    channels = ($channels -join ",")
    aot_dynamic_box2d_rejected = "true"
    package_layout_checked = "true"
    checksum_checked = "true"
}
Add-RunIdentityCheck -Missing $missing -Root $root -Scope "artifact_audit" -Path $artifactAuditReport -ExpectedIdentity $expectedRunIdentity
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "github_release_upload" -Path $githubReleaseUploadReport -DeclaredSha256 ([string]$manifest.githubRelease.uploadSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "github_release_upload" -Path $githubReleaseUploadReport -ExpectedValues @{ conclusion = "success" }
Add-RunIdentityCheck -Missing $missing -Root $root -Scope "github_release_upload" -Path $githubReleaseUploadReport -ExpectedIdentity $expectedRunIdentity
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "deterministic_hash" -Path ([string]$manifest.deterministicHashReport) -DeclaredSha256 ([string]$manifest.deterministicHashSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "deterministic_hash" -Path ([string]$manifest.deterministicHashReport) -ExpectedValues @{ conclusion = "success" }
Add-RunIdentityCheck -Missing $missing -Root $root -Scope "deterministic_hash" -Path ([string]$manifest.deterministicHashReport) -ExpectedIdentity $expectedRunIdentity
Test-DeterministicHashRows -Missing $missing -Root $root -Path ([string]$manifest.deterministicHashReport) -RequiredRids $rids -RequiredChannels $channels
Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "r2r_lightup" -Path ([string]$manifest.r2rLightupReport) -DeclaredSha256 ([string]$manifest.r2rLightupSha256)
Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "r2r_lightup" -Path ([string]$manifest.r2rLightupReport) -ExpectedValues @{ conclusion = "success" }
Add-RunIdentityCheck -Missing $missing -Root $root -Scope "r2r_lightup" -Path ([string]$manifest.r2rLightupReport) -ExpectedIdentity $expectedRunIdentity

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
        $packagePath = [string]$node.package
        $packageName = Split-Path -Leaf $packagePath
        Test-PackageNameMatchesArtifactNode -Missing $missing -PackageName $packageName -Rid $rid -Channel $channel -ReleaseVersion $releaseVersion
        if (-not [string]::IsNullOrWhiteSpace($packageName)) {
            if ($expectedChecksumPackages.ContainsKey($packageName)) {
                $missing.Add("manifest 包含重复 package 文件名：$packageName")
            }
            else {
                $expectedChecksumPackages[$packageName] = [string]$node.packageSha256
            }
        }

        $checksumPath = [string]$node.checksum
        if (-not [string]::IsNullOrWhiteSpace($checksumPath)) {
            $checksumPaths[$checksumPath] = $true
            $checksumSha256ByPath[$checksumPath] = [string]$node.checksumSha256
        }

        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/publish" -Path $publishReportPath -DeclaredSha256 ([string]$node.publishSha256)
        Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/publish" -Path $publishReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
        Add-RunIdentityCheck -Missing $missing -Root $root -Scope "$rid/$channel/publish" -Path $publishReportPath -ExpectedIdentity $expectedRunIdentity
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/verify" -Path $verifyReportPath -DeclaredSha256 ([string]$node.verifySha256)
        Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/verify" -Path $verifyReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
        Add-RunIdentityCheck -Missing $missing -Root $root -Scope "$rid/$channel/verify" -Path $verifyReportPath -ExpectedIdentity $expectedRunIdentity
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/package_report" -Path $packageReportPath -DeclaredSha256 ([string]$node.packageReportSha256)
        Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/package_report" -Path $packageReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
        Add-RunIdentityCheck -Missing $missing -Root $root -Scope "$rid/$channel/package_report" -Path $packageReportPath -ExpectedIdentity $expectedRunIdentity
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/package" -Path $packagePath -DeclaredSha256 ([string]$node.packageSha256)
        Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/checksum" -Path $checksumPath -DeclaredSha256 ([string]$node.checksumSha256)

        if ($channel -eq "aot") {
            $simdProbePath = [string]$node.simdProbe
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/simd_probe" -Path $simdProbePath -DeclaredSha256 ([string]$node.simdProbeSha256)
            Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/simd_probe" -Path $simdProbePath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
            Add-RunIdentityCheck -Missing $missing -Root $root -Scope "$rid/$channel/simd_probe" -Path $simdProbePath -ExpectedIdentity $expectedRunIdentity
            Test-AotSimdEvidence -Missing $missing -Root $root -Rid $rid -Node $node
        }

        if ($rid.StartsWith("osx-", [StringComparison]::Ordinal)) {
            $codesignReportPath = [string]$node.codesignReport
            $notarizationReportPath = [string]$node.notarizationReport
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/codesign" -Path $codesignReportPath -DeclaredSha256 ([string]$node.codesignSha256)
            Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/codesign" -Path $codesignReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
            Add-RunIdentityCheck -Missing $missing -Root $root -Scope "$rid/$channel/codesign" -Path $codesignReportPath -ExpectedIdentity $expectedRunIdentity
            Add-EvidenceFile -Evidence $evidence -Missing $missing -Root $root -Scope "$rid/$channel/notarization" -Path $notarizationReportPath -DeclaredSha256 ([string]$node.notarizationSha256)
            Add-MarkdownEvidenceCheck -Missing $missing -Root $root -Scope "$rid/$channel/notarization" -Path $notarizationReportPath -ExpectedValues @{ rid = $rid; channel = $channel; conclusion = "success" }
            Add-RunIdentityCheck -Missing $missing -Root $root -Scope "$rid/$channel/notarization" -Path $notarizationReportPath -ExpectedIdentity $expectedRunIdentity
        }
    }
}

if ($checksumPaths.Count -gt 1) {
    $missing.Add("manifest 中所有 checksum 必须指向同一个 SHA256SUMS 文件")
}
elseif ($checksumPaths.Count -eq 1) {
    $checksumPath = [string](@($checksumPaths.Keys)[0])
    Test-ReleaseChecksumRows -Missing $missing -Root $root -Path $checksumPath -ExpectedPackages $expectedChecksumPackages
    $expectedReleaseTag = if ([string]::IsNullOrWhiteSpace($releaseVersion)) { "" } else { "v$releaseVersion" }
    Test-GitHubReleaseUploadedAssets -Missing $missing -Root $root -Path $githubReleaseUploadReport -ExpectedPackages $expectedChecksumPackages -ChecksumSha256 ([string]$checksumSha256ByPath[$checksumPath]) -ExpectedTag $expectedReleaseTag
}

Test-PackageVersionsMatchReleaseTag -Missing $missing -ReleaseVersion $releaseVersion -ExpectedPackages $expectedChecksumPackages

if ($missing.Count -gt 0) {
    $detail = "Release evidence preflight failed: release manifest 存在，但外部证据不完整。缺失项必须由 release workflow、目标 runner、macOS signing/notary 或 GitHub Release 上传结果补齐；不得据此勾选 plan/15 阻塞项。"
    Write-ReleaseEvidenceReport -Path $reportPath -Status "blocked_missing_release_scope_evidence" -Evidence $evidence -Missing $missing -RequiredRids $rids -RequiredChannels $channels -Detail $detail
    Write-Host "Release evidence preflight blocked_missing_release_scope_evidence. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 5
}

$detail = "Release evidence manifest is complete, declared SHA256 hashes matched, and markdown evidence fields reported success for required jobs. Human review still must confirm the reports prove active RID R2R/AOT outputs, AOT SIMD probes, R2R runtime light-up, macOS codesign/notarization where applicable, deterministic hashes and GitHub Release upload before plan/15 can be unblocked."
Write-ReleaseEvidenceReport -Path $reportPath -Status "release_evidence_attached_pending_review" -Evidence $evidence -Missing $missing -RequiredRids $rids -RequiredChannels $channels -Detail $detail
Write-Host "Release evidence preflight release_evidence_attached_pending_review. Report: $reportPath"

if (-not $AllowBlocked) {
    [Console]::Error.WriteLine("Release evidence preflight failed: release_evidence_attached_pending_review")
    exit 2
}

exit 0
