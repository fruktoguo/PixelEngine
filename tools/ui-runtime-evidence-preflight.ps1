param(
    [string]$EvidenceManifestPath = "",
    [string]$Artifacts = "artifacts/ui-runtime-evidence-preflight",
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

function Get-CurrentGitCommit {
    param([string]$Root)

    $output = & git -C $Root rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$output)) {
        throw "无法读取当前 git HEAD，不能校验 UI Runtime evidence 是否来自当前提交。"
    }

    return ([string]$output).Trim()
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

function Get-RequiredManifestString {
    param(
        [object]$Node,
        [string]$Name
    )

    $value = [string](Get-JsonPropertyValue -Node $Node -Name $Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "UI Runtime evidence manifest 缺少 $Name。"
    }

    return $value.Trim()
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

    if ($value -in @("1", "yes", "present", "pass", "passed")) {
        return $true
    }

    if ($value -in @("0", "no", "missing", "fail", "failed")) {
        return $false
    }

    throw "$Scope $Name 必须为 true/false。"
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

function Get-RequiredNumber {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    $value = Get-RequiredField -Fields $Fields -Name $Name -Scope $Scope
    $parsed = 0.0
    if (-not [double]::TryParse(
        $value,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$parsed)) {
        throw "$Scope $Name 必须为数字。"
    }

    return $parsed
}

function Assert-NumberAtLeast {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [double]$Minimum,
        [string]$Scope
    )

    $actual = Get-RequiredNumber -Fields $Fields -Name $Name -Scope $Scope
    if ($actual -lt $Minimum) {
        throw "$Scope $Name 必须至少为 $Minimum，实际为 $actual。"
    }
}

function Assert-CommonEvidenceIdentity {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Scope,
        [string]$ReviewSessionId,
        [string]$GitCommit
    )

    $actualScope = Get-RequiredField -Fields $Fields -Name "scope" -Scope $Scope
    if (-not [string]::Equals($actualScope, $Scope, [StringComparison]::Ordinal)) {
        throw "$Scope 报告 scope 必须为 $Scope，实际为 $actualScope。"
    }

    $actualReviewSessionId = Get-RequiredField -Fields $Fields -Name "reviewSessionId" -Scope $Scope
    if (-not [string]::Equals($actualReviewSessionId, $ReviewSessionId, [StringComparison]::Ordinal)) {
        throw "$Scope reviewSessionId 必须与 manifest 一致：expected=$ReviewSessionId actual=$actualReviewSessionId"
    }

    $actualGitCommit = Get-RequiredField -Fields $Fields -Name "gitCommit" -Scope $Scope
    if (-not [string]::Equals($actualGitCommit, $GitCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Scope gitCommit 必须与 manifest 一致：expected=$GitCommit actual=$actualGitCommit"
    }

    $conclusion = Get-RequiredField -Fields $Fields -Name "conclusion" -Scope $Scope
    if ($conclusion -notin @("pass", "passed", "accepted", "ready_for_review")) {
        throw "$Scope conclusion 必须为 pass/passed/accepted/ready_for_review。"
    }

    $risk = Get-RequiredField -Fields $Fields -Name "risk" -Scope $Scope
    if ($risk.Length -lt 12) {
        throw "$Scope risk 必须包含可复核的风险说明，不能留空或只写 ok。"
    }
}

function Get-RequiredUiRuntimeScopes {
    @(
        [pscustomobject]@{
            scope = "transparent_ui_product_window"
            title = "真实窗口透明 UI 产品面：same-window/same-GL、alpha、pass-through、capture、Editor overlay"
            trueFields = @("sameWindowSameGl", "alphaBlendVerified", "passThroughVerified", "captureVerified", "editorOverlayVerified", "videoOrWalkthroughAttached")
            minimumNumberFields = @(
                @{ name = "videoDurationSeconds"; minimum = 30 },
                @{ name = "capturedFrameCount"; minimum = 300 }
            )
        },
        [pscustomobject]@{
            scope = "rmlui_angle_gles_native_profile"
            title = "RmlUi ANGLE/GLES native profile：GLES3 renderer、ANGLE context、shader profile、状态恢复 smoke"
            trueFields = @("glesRendererImplemented", "angleContextVerified", "shaderProfileGles300Es", "sameContextFunctionTable", "stateRestoreSmokePassed")
            minimumNumberFields = @(
                @{ name = "smokeFrameCount"; minimum = 60 }
            )
        },
        [pscustomobject]@{
            scope = "platform_ime_composition"
            title = "真实平台 IME composition：预编辑、候选窗、committed text 分离、三后端一致性"
            trueFields = @("windowsImeSmokePassed", "preeditVisible", "candidateWindowChecked", "committedTextSeparated", "backendConsistencyChecked")
            minimumNumberFields = @(
                @{ name = "compositionSessionCount"; minimum = 1 }
            )
        },
        [pscustomobject]@{
            scope = "ultralight_optional_profile_gate"
            title = "Ultralight optional profile：后端/许可/provenance/发行 gate 或明确 inactive gate 复核"
            trueFields = @("nativeSdkProvenanceRecorded", "licenseReviewed", "redistributionDecisionRecorded", "releaseAuditGatePassed", "fallbackPathVerified")
            minimumNumberFields = @(
                @{ name = "licenseDocumentCount"; minimum = 1 }
            )
        },
        [pscustomobject]@{
            scope = "ui_native_release_artifact"
            title = "UI native release artifact：active RID R2R/AOT fallback、SHA256、NOTICE/license、tag release 同源证据"
            trueFields = @("activeRidArtifactsAttached", "aotFallbackVerified", "sha256SumsAttached", "noticeLicenseAttached", "tagReleaseWorkflowEvidence")
            minimumNumberFields = @(
                @{ name = "releaseArtifactCount"; minimum = 1 },
                @{ name = "sha256EntryCount"; minimum = 1 }
            )
        }
    )
}

function Add-ScopedEvidence {
    param(
        [System.Collections.Generic.List[object]]$Evidence,
        [System.Collections.Generic.List[string]]$Missing,
        [string]$Root,
        [hashtable]$EntriesByScope,
        [object]$ScopeDefinition
    )

    $scope = [string]$ScopeDefinition.scope
    if (-not $EntriesByScope.ContainsKey($scope)) {
        $Missing.Add("缺少 evidence scope：$scope")
        return
    }

    $entry = $EntriesByScope[$scope]
    $path = [string](Get-JsonPropertyValue -Node $entry -Name "path")
    if ([string]::IsNullOrWhiteSpace($path)) {
        $Missing.Add("scope $scope 缺少 path")
        return
    }

    $declaredHash = [string](Get-JsonPropertyValue -Node $entry -Name "sha256")
    if ([string]::IsNullOrWhiteSpace($declaredHash)) {
        $Missing.Add("scope $scope 缺少 sha256")
        return
    }

    $resolved = if ([System.IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $Root $path }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        $Missing.Add("scope $scope 指向文件不存在：$path")
        return
    }

    $actualHash = Get-FileSha256 -Path $resolved
    $expectedHash = $declaredHash.Trim().ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        $Missing.Add("scope $scope sha256 不匹配：expected=$expectedHash actual=$actualHash")
        return
    }

    try {
        $fields = Read-MachineReadableFields -Path $resolved
        Assert-CommonEvidenceIdentity -Fields $fields -Scope $scope -ReviewSessionId $script:manifestReviewSessionId -GitCommit $script:manifestGitCommit
        foreach ($field in @($ScopeDefinition.trueFields)) {
            Assert-TrueField -Fields $fields -Name ([string]$field) -Scope $scope
        }

        foreach ($field in @($ScopeDefinition.minimumNumberFields)) {
            Assert-NumberAtLeast `
                -Fields $fields `
                -Name ([string]$field.name) `
                -Minimum ([double]$field.minimum) `
                -Scope $scope
        }
    }
    catch {
        $Missing.Add("scope $scope 语义无效：$($_.Exception.Message)")
        return
    }

    $Evidence.Add([pscustomobject]@{
        scope = $scope
        path = ConvertTo-RepositoryRelativePath -Root $Root -Path $resolved
        sha256 = $actualHash
    })
}

function Write-UiRuntimeEvidenceReport {
    param(
        [string]$Path,
        [string]$Status,
        [int]$ExitCode,
        [object[]]$Evidence,
        [string[]]$Missing,
        [string]$Detail
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# PixelEngine UI Runtime evidence preflight")
    $lines.Add("")
    $lines.Add("status: $Status")
    $lines.Add("exit_code: $ExitCode")
    $lines.Add("")
    $lines.Add("## Detail")
    $lines.Add("")
    $lines.Add($Detail)
    $lines.Add("")
    $lines.Add("## Required scopes")
    $lines.Add("")
    foreach ($scope in Get-RequiredUiRuntimeScopes) {
        $lines.Add("- $($scope.scope): $($scope.title)")
        $lines.Add("  required_true_fields: $($scope.trueFields -join ', ')")
        $numberFields = @($scope.minimumNumberFields | ForEach-Object { "$($_.name)>=$($_.minimum)" })
        if ($numberFields.Count -gt 0) {
            $lines.Add("  required_number_fields: $($numberFields -join ', ')")
        }
    }
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
$reportPath = Join-Path $artifactRoot "ui-runtime-evidence-preflight.md"
$evidence = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()

if ([string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    $detail = "UI Runtime evidence preflight failed: 缺少 evidence manifest。本脚本只校验真实窗口透明 UI、RmlUi ANGLE/GLES、真实平台 IME、Ultralight gate 与 UI native release artifact 的 scope/hash/语义，不运行本机短 probe，也不会把 pending_review 当作验收完成。"
    Write-UiRuntimeEvidenceReport -Path $reportPath -Status "blocked_missing_ui_runtime_evidence" -ExitCode 2 -Evidence @($evidence) -Missing @("ui runtime evidence manifest 不存在") -Detail $detail
    Write-Host "UI Runtime evidence preflight blocked_missing_ui_runtime_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("UI Runtime evidence preflight failed: blocked_missing_ui_runtime_evidence")
        exit 2
    }

    exit 0
}

$manifestPath = if ([System.IO.Path]::IsPathRooted($EvidenceManifestPath)) { $EvidenceManifestPath } else { Join-Path $root $EvidenceManifestPath }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    $detail = "UI Runtime evidence preflight failed: manifest 路径不存在。"
    Write-UiRuntimeEvidenceReport -Path $reportPath -Status "blocked_missing_ui_runtime_evidence" -ExitCode 2 -Evidence @($evidence) -Missing @("ui runtime evidence manifest 不存在：$EvidenceManifestPath") -Detail $detail
    Write-Host "UI Runtime evidence preflight blocked_missing_ui_runtime_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("UI Runtime evidence preflight failed: blocked_missing_ui_runtime_evidence")
        exit 2
    }

    exit 0
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ((Get-JsonPropertyValue -Node $manifest -Name "schemaVersion") -ne 1) {
        throw "UI Runtime evidence manifest schemaVersion 必须为 1。"
    }

    $script:manifestReviewSessionId = Get-RequiredManifestString -Node $manifest -Name "reviewSessionId"
    $script:manifestGitCommit = Get-RequiredManifestString -Node $manifest -Name "gitCommit"
    $currentGitCommit = Get-CurrentGitCommit -Root $root
    if (-not [string]::Equals($script:manifestGitCommit, $currentGitCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "UI Runtime evidence manifest gitCommit 必须等于当前 HEAD $currentGitCommit，实际为 $script:manifestGitCommit。"
    }
}
catch {
    $detail = "UI Runtime evidence preflight failed: evidence manifest JSON 或 schema 无效。不得据此勾选 plan/20 的 UI Runtime M15 阻塞项。"
    Write-UiRuntimeEvidenceReport -Path $reportPath -Status "blocked_invalid_ui_runtime_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @("ui runtime evidence manifest 无效：$($_.Exception.Message)") -Detail $detail
    Write-Host "UI Runtime evidence preflight blocked_invalid_ui_runtime_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("UI Runtime evidence preflight failed: blocked_invalid_ui_runtime_evidence")
        exit 5
    }

    exit 0
}

$entries = @((Get-JsonPropertyValue -Node $manifest -Name "evidence"))
if ($entries.Count -eq 1 -and $null -eq $entries[0]) {
    $entries = @()
}

if ($entries.Count -eq 0) {
    $missing.Add("evidence[] 为空")
}

$scopeDefinitions = @(Get-RequiredUiRuntimeScopes)
$requiredScopes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($definition in $scopeDefinitions) {
    $requiredScopes.Add([string]$definition.scope) | Out-Null
}

$entriesByScope = @{}
foreach ($entry in $entries) {
    $scope = [string](Get-JsonPropertyValue -Node $entry -Name "scope")
    if ([string]::IsNullOrWhiteSpace($scope)) {
        $missing.Add("evidence entry 缺少 scope")
        continue
    }

    if (-not $requiredScopes.Contains($scope)) {
        $missing.Add("未知 evidence scope：$scope")
        continue
    }

    if ($entriesByScope.ContainsKey($scope)) {
        $missing.Add("重复 evidence scope：$scope")
        continue
    }

    $entriesByScope[$scope] = $entry
}

foreach ($definition in $scopeDefinitions) {
    Add-ScopedEvidence -Evidence $evidence -Missing $missing -Root $root -EntriesByScope $entriesByScope -ScopeDefinition $definition
}

if ($missing.Count -gt 0) {
    $detail = "UI Runtime evidence preflight failed: manifest 存在，但真实窗口透明 UI、RmlUi ANGLE/GLES、真实平台 IME、Ultralight gate 或 UI native release artifact 证据不完整。不得据此勾选 plan/20 的 M14/M15 阻塞项。"
    Write-UiRuntimeEvidenceReport -Path $reportPath -Status "blocked_missing_ui_runtime_scope_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @($missing) -Detail $detail
    Write-Host "UI Runtime evidence preflight blocked_missing_ui_runtime_scope_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("UI Runtime evidence preflight failed: blocked_missing_ui_runtime_scope_evidence")
        exit 5
    }

    exit 0
}

$detail = "UI Runtime evidence manifest is complete and SHA256/semantic checks passed. Human review still must confirm the attached materials prove same-window transparent UI product behavior, RmlUi ANGLE/GLES native profile, real IME composition, Ultralight profile gate, and UI native release artifacts before plan/20 can be unblocked."
Write-UiRuntimeEvidenceReport -Path $reportPath -Status "ui_runtime_evidence_attached_pending_review" -ExitCode 2 -Evidence @($evidence) -Missing @($missing) -Detail $detail
Write-Host "UI Runtime evidence preflight ui_runtime_evidence_attached_pending_review. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if (-not $AllowBlocked) {
    [Console]::Error.WriteLine("UI Runtime evidence preflight failed: ui_runtime_evidence_attached_pending_review")
    exit 2
}

exit 0
