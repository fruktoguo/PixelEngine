param(
    [string]$EvidenceManifestPath = "",
    [string]$Artifacts = "artifacts/editor-ux-evidence-preflight",
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
        throw "无法读取当前 git HEAD，不能校验 Editor UX evidence 是否来自当前提交。"
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
        throw "Editor UX evidence manifest 缺少 $Name。"
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

function Get-RequiredEditorUxScopes {
    @(
        [pscustomobject]@{
            scope = "editor_full_route_window"
            title = "真实窗口完整路线：启动 Shell、新建/打开工程、默认布局、Play/Exit、保存、Build And Run"
            trueFields = @(
                "shellStarted",
                "editorShellExeLaunched",
                "singleTopLevelWindowVerified",
                "singleProcessInProcessHost",
                "noConsoleWindowObserved",
                "projectOpenedOrCreated",
                "defaultLayoutVisible",
                "playExitVerified",
                "sceneSaved",
                "buildAndRunVerified"
            )
            minimumNumberFields = @(
                @{ name = "videoDurationSeconds"; minimum = 60 },
                @{ name = "capturedFrameCount"; minimum = 600 },
                @{ name = "routeStepCount"; minimum = 8 }
            )
        },
        [pscustomobject]@{
            scope = "project_window_reference_stability"
            title = "Project Window 引用稳定性：移动/重命名/删除确认后 stable id、Scene、Prefab、Inspector 字段与 Build 入包不丢引用"
            trueFields = @(
                "stableIdsChecked",
                "stableIdsBeforeAfterRecorded",
                "sceneReferencesChecked",
                "prefabReferencesChecked",
                "inspectorAssetFieldsChecked",
                "projectPlayerBuildSettingsChecked",
                "startupSettingsChecked",
                "buildRequestChecked",
                "buildPackageReferenceAuditPassed",
                "deleteConfirmationChecked",
                "brokenReferenceCountZero"
            )
            minimumNumberFields = @(
                @{ name = "assetOperationCount"; minimum = 3 },
                @{ name = "referenceDocumentCount"; minimum = 2 },
                @{ name = "stableAssetKindCount"; minimum = 4 },
                @{ name = "buildPackageAuditCount"; minimum = 1 }
            )
        },
        [pscustomobject]@{
            scope = "script_external_editor"
            title = "脚本外部编辑器：真实 OS opener 或 configured editor 打开，失败提示可见"
            trueFields = @("scriptDoubleClickAttempted", "osOrConfiguredEditorObserved", "failureDiagnosticObserved", "noSilentFailure")
            minimumNumberFields = @(
                @{ name = "scriptOpenAttemptCount"; minimum = 1 }
            )
        },
        [pscustomobject]@{
            scope = "settings_build_ux"
            title = "Project/Player/Build Settings UX：填写、保存、重启恢复、错误输入校验、build-player 参数投影"
            trueFields = @("projectSettingsSaved", "playerSettingsSaved", "buildSettingsSaved", "restartReloadVerified", "invalidInputRejected", "buildPlayerProjectionVerified")
            minimumNumberFields = @(
                @{ name = "settingsRoundTripCount"; minimum = 1 },
                @{ name = "buildRunAttemptCount"; minimum = 1 }
            )
        },
        [pscustomobject]@{
            scope = "editor_product_usability"
            title = "编辑器产品可用性：布局、快捷键、拖拽、gizmo、Undo/Redo、Console、Build 面板反馈可理解可恢复"
            trueFields = @("layoutUsable", "shortcutsChecked", "dragDropChecked", "gizmoChecked", "undoRedoChecked", "consoleDiagnosticsChecked", "buildFeedbackChecked")
            minimumNumberFields = @(
                @{ name = "interactionChecklistItemCount"; minimum = 7 },
                @{ name = "reviewerCount"; minimum = 1 }
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

function Write-EditorUxEvidenceReport {
    param(
        [string]$Path,
        [string]$Status,
        [int]$ExitCode,
        [object[]]$Evidence,
        [string[]]$Missing,
        [string]$Detail
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# PixelEngine Editor UX evidence preflight")
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
    foreach ($scope in Get-RequiredEditorUxScopes) {
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
$reportPath = Join-Path $artifactRoot "editor-ux-evidence-preflight.md"
$evidence = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()

if ([string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    $detail = "Editor UX evidence preflight failed: 缺少 evidence manifest。本脚本只校验真实窗口完整路线、Project Window 引用稳定性、脚本外部编辑器、Settings UX 与产品可用性证据的 scope/hash/语义，不运行 scripted probe，也不会把 pending_review 当作验收完成。"
    Write-EditorUxEvidenceReport -Path $reportPath -Status "blocked_missing_editor_ux_evidence" -ExitCode 2 -Evidence @($evidence) -Missing @("editor ux evidence manifest 不存在") -Detail $detail
    Write-Host "Editor UX evidence preflight blocked_missing_editor_ux_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Editor UX evidence preflight failed: blocked_missing_editor_ux_evidence")
        exit 2
    }

    exit 0
}

$manifestPath = if ([System.IO.Path]::IsPathRooted($EvidenceManifestPath)) { $EvidenceManifestPath } else { Join-Path $root $EvidenceManifestPath }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    $detail = "Editor UX evidence preflight failed: manifest 路径不存在。"
    Write-EditorUxEvidenceReport -Path $reportPath -Status "blocked_missing_editor_ux_evidence" -ExitCode 2 -Evidence @($evidence) -Missing @("editor ux evidence manifest 不存在：$EvidenceManifestPath") -Detail $detail
    Write-Host "Editor UX evidence preflight blocked_missing_editor_ux_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Editor UX evidence preflight failed: blocked_missing_editor_ux_evidence")
        exit 2
    }

    exit 0
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ((Get-JsonPropertyValue -Node $manifest -Name "schemaVersion") -ne 1) {
        throw "Editor UX evidence manifest schemaVersion 必须为 1。"
    }

    $script:manifestReviewSessionId = Get-RequiredManifestString -Node $manifest -Name "reviewSessionId"
    $script:manifestGitCommit = Get-RequiredManifestString -Node $manifest -Name "gitCommit"
    $currentGitCommit = Get-CurrentGitCommit -Root $root
    if (-not [string]::Equals($script:manifestGitCommit, $currentGitCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Editor UX evidence manifest gitCommit 必须等于当前 HEAD $currentGitCommit，实际为 $script:manifestGitCommit。"
    }
}
catch {
    $detail = "Editor UX evidence preflight failed: evidence manifest JSON 或 schema 无效。不得据此将 EDITOR-001 至 EDITOR-003 标为完成。"
    Write-EditorUxEvidenceReport -Path $reportPath -Status "blocked_invalid_editor_ux_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @("editor ux evidence manifest 无效：$($_.Exception.Message)") -Detail $detail
    Write-Host "Editor UX evidence preflight blocked_invalid_editor_ux_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Editor UX evidence preflight failed: blocked_invalid_editor_ux_evidence")
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

$scopeDefinitions = @(Get-RequiredEditorUxScopes)
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
    $detail = "Editor UX evidence preflight failed: manifest 存在，但真实窗口完整路线、Project Window 引用稳定性、脚本外部编辑器、Settings UX 或产品可用性证据不完整。不得据此将 EDITOR-001 至 EDITOR-003 标为完成。"
    Write-EditorUxEvidenceReport -Path $reportPath -Status "blocked_missing_editor_ux_scope_evidence" -ExitCode 5 -Evidence @($evidence) -Missing @($missing) -Detail $detail
    Write-Host "Editor UX evidence preflight blocked_missing_editor_ux_scope_evidence. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Editor UX evidence preflight failed: blocked_missing_editor_ux_scope_evidence")
        exit 5
    }

    exit 0
}

$detail = "Editor UX evidence manifest is complete and SHA256/semantic checks passed. Human review still must confirm the attached materials prove the full Editor window route, Project Window reference stability, script external editor behavior, Settings UX, and product usability before EDITOR-001 through EDITOR-003 can be completed."
Write-EditorUxEvidenceReport -Path $reportPath -Status "editor_ux_evidence_attached_pending_review" -ExitCode 2 -Evidence @($evidence) -Missing @($missing) -Detail $detail
Write-Host "Editor UX evidence preflight editor_ux_evidence_attached_pending_review. Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if (-not $AllowBlocked) {
    [Console]::Error.WriteLine("Editor UX evidence preflight failed: editor_ux_evidence_attached_pending_review")
    exit 2
}

exit 0
