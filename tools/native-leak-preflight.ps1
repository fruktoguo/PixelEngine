param(
    [string]$Project = "demo/PixelEngine.Demo/PixelEngine.Demo.csproj",
    [string]$EditorShellProject = "apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj",
    [string]$Content = "demo/PixelEngine.Demo/content",
    [string]$Scene = "scenes/empty-window-probe.scene",
    [string]$Artifacts = "artifacts/native-leak-preflight",
    [string]$DetectorName = "",
    [string]$DetectorReportPath = "",
    [string]$EvidenceManifestPath = "",
    [int]$WindowTicks = 600,
    [int]$EditorTicks = 120,
    [switch]$RunProcessSmoke,
    [switch]$IncludeEditor,
    [switch]$AllowBlocked
)

$ErrorActionPreference = "Stop"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

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

function Read-MarkdownEvidenceTable {
    param([string]$Path)

    $values = @{}
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

function Get-NativeLeakScopeRequiredMetric {
    param([string]$Scope)

    switch ($Scope) {
        "gl" { return "glObjectsLiveAfterShutdown" }
        "openal" { return "openAlObjectsLiveAfterShutdown" }
        "box2d" { return "box2DBodiesLiveAfterShutdown" }
        "alc" { return "alcLoadContextsAliveAfterUnload" }
        default { throw "未知 native leak scope：$Scope" }
    }
}

function Test-NativeLeakLiveCountZero {
    param(
        [hashtable]$Values,
        [string]$Metric,
        [string]$Prefix
    )

    if (-not $Values.ContainsKey($Metric)) {
        return "$Prefix 缺少 $Metric 字段"
    }

    $liveCount = 0
    $rawLiveCount = [string]$Values[$Metric]
    if (-not [int]::TryParse($rawLiveCount, [ref]$liveCount) -or $liveCount -ne 0) {
        return "$Prefix $Metric 必须为 0，实际为 $rawLiveCount"
    }

    return ""
}

function Get-RequiredManifestString {
    param(
        [object]$Node,
        [string]$Name,
        [string]$Prefix
    )

    if ($null -eq $Node) {
        return ""
    }

    $property = $Node.PSObject.Properties | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return ""
    }

    return [string]$property.Value
}

function Test-SingleDetectorReport {
    param(
        [string]$Path,
        [string]$DetectorName
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($DetectorName)) {
        $issues.Add("单 detector report 必须显式传入 DetectorName")
    }

    $values = Read-MarkdownEvidenceTable -Path $Path
    foreach ($key in @("detector", "conclusion", "scopes")) {
        if (-not $values.ContainsKey($key)) {
            $issues.Add("单 detector report 缺少 $key 字段")
        }
    }

    if ($values.ContainsKey("detector") -and -not [string]::IsNullOrWhiteSpace($DetectorName) -and
        -not [string]::Equals([string]$values["detector"], $DetectorName, [StringComparison]::Ordinal)) {
        $issues.Add("单 detector report detector 必须为 $DetectorName，实际为 $($values["detector"])")
    }

    if ($values.ContainsKey("conclusion") -and
        -not [string]::Equals([string]$values["conclusion"], "no_leaks", [StringComparison]::OrdinalIgnoreCase)) {
        $issues.Add("单 detector report conclusion 必须为 no_leaks，实际为 $($values["conclusion"])")
    }

    if ($values.ContainsKey("scopes")) {
        $scopeText = [string]$values["scopes"]
        foreach ($scope in @("GL", "OpenAL", "Box2D", "ALC")) {
            if ($scopeText -notmatch [regex]::Escape($scope)) {
                $issues.Add("单 detector report scopes 必须包含 $scope")
            }
        }
    }

    foreach ($metric in @("glObjectsLiveAfterShutdown", "openAlObjectsLiveAfterShutdown", "box2DBodiesLiveAfterShutdown", "alcLoadContextsAliveAfterUnload")) {
        $issue = Test-NativeLeakLiveCountZero -Values $values -Metric $metric -Prefix "单 detector report"
        if (-not [string]::IsNullOrWhiteSpace($issue)) {
            $issues.Add($issue)
        }
    }

    return @($issues)
}

function Write-NativeLeakReport {
    param(
        [string]$Path,
        [string]$Status,
        [string]$Detector,
        [string]$DetectorReport,
        [array]$Evidence,
        [array]$Runs,
        [string]$Detail
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $lines = @(
        "# PixelEngine native leak preflight",
        "",
        "| Key | Value |",
        "|---|---|",
        "| status | $Status |",
        "| detector | $Detector |",
        "| detector_report | $DetectorReport |",
        "| required_scope | GL; OpenAL; Box2D; ALC |",
        "",
        "## Evidence",
        "",
        "| Scope | Detector | Report | SHA256 |",
        "|---|---|---|---|"
    )

    if ($Evidence.Count -eq 0) {
        $lines += "| none | n/a | n/a | n/a |"
    }
    else {
        foreach ($item in $Evidence) {
            $lines += "| $($item.Scope) | $($item.Detector) | $($item.Report) | $($item.Sha256) |"
        }
    }

    $lines += @(
        "",
        "## Process smoke",
        "",
        "| Mode | Exit | Elapsed ms | PeakWorkingSetMB | Stdout |",
        "|---|---:|---:|---:|---|"
    )

    if ($Runs.Count -eq 0) {
        $lines += "| none | n/a | n/a | n/a | n/a |"
    }
    else {
        foreach ($run in $Runs) {
            $lines += "| $($run.Mode) | $($run.ExitCode) | $($run.ElapsedMs) | $($run.PeakWorkingSetMB) | $($run.StdoutPath) |"
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

function Read-EvidenceManifest {
    param([string]$Root, [string]$ManifestPath)

    if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
        return @()
    }

    $resolvedManifestPath = if ([IO.Path]::IsPathRooted($ManifestPath)) { $ManifestPath } else { Join-Path $Root $ManifestPath }
    if (-not (Test-Path -LiteralPath $resolvedManifestPath -PathType Leaf)) {
        return @(
            [pscustomobject]@{
                MissingManifest = $true
                Message = "evidence manifest 不存在：$ManifestPath"
            }
        )
    }

    try {
        $manifest = Get-Content -Raw -LiteralPath $resolvedManifestPath | ConvertFrom-Json
    }
    catch {
        return @(
            [pscustomobject]@{
                InvalidManifest = $true
                Scope = "manifest"
                Message = "evidence manifest JSON 无法解析：$($_.Exception.Message)"
            }
        )
    }

    $requiredScopes = @("gl", "openal", "box2d", "alc")
    $items = @()

    if ($manifest.schemaVersion -ne 1) {
        $items += [pscustomobject]@{
            InvalidManifest = $true
            Scope = "schemaVersion"
            Message = "evidence manifest schemaVersion 必须为 1"
        }
    }

    $manifestDetectorRunId = Get-RequiredManifestString -Node $manifest -Name "detectorRunId" -Prefix "evidence manifest"
    if ([string]::IsNullOrWhiteSpace($manifestDetectorRunId)) {
        $items += [pscustomobject]@{
            InvalidManifest = $true
            Scope = "manifest"
            Message = "evidence manifest 缺少 detectorRunId。"
        }
    }

    $manifestGitCommit = Get-RequiredManifestString -Node $manifest -Name "gitCommit" -Prefix "evidence manifest"
    if ([string]::IsNullOrWhiteSpace($manifestGitCommit)) {
        $items += [pscustomobject]@{
            InvalidManifest = $true
            Scope = "manifest"
            Message = "evidence manifest 缺少 gitCommit。"
        }
    }

    if ($null -eq $manifest.scopes) {
        $items += [pscustomobject]@{
            MissingScope = $true
            Scope = "scopes"
            Message = "evidence manifest 缺少 scopes 节点"
        }
    }
    else {
        foreach ($scopeName in @($manifest.scopes.PSObject.Properties | Select-Object -ExpandProperty Name)) {
            if ($scopeName -notin $requiredScopes) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scopeName
                    Message = "evidence manifest 包含未知 scope：$scopeName"
                }
            }
        }
    }

    foreach ($scope in $requiredScopes) {
        $scopeNode = $manifest.scopes.$scope
        if ($null -eq $scopeNode) {
            $items += [pscustomobject]@{
                MissingScope = $true
                Scope = $scope
                Message = "evidence manifest 缺少 scope：$scope"
            }
            continue
        }

        $report = [string]$scopeNode.report
        if ([string]::IsNullOrWhiteSpace($report)) {
            $items += [pscustomobject]@{
                MissingScope = $true
                Scope = $scope
                Message = "evidence manifest scope 缺少 report：$scope"
            }
            continue
        }

        $reportPath = if ([IO.Path]::IsPathRooted($report)) { $report } else { Join-Path $Root $report }
        if (-not (Test-Path $reportPath)) {
            $items += [pscustomobject]@{
                MissingScope = $true
                Scope = $scope
                Message = "evidence report 不存在：$report"
            }
            continue
        }

        $declaredHash = [string]$scopeNode.sha256
        if ([string]::IsNullOrWhiteSpace($declaredHash)) {
            $items += [pscustomobject]@{
                MissingScope = $true
                Scope = $scope
                Message = "evidence manifest scope 缺少 sha256：$scope"
            }
            continue
        }

        $hash = Get-FileHash -Algorithm SHA256 -Path $reportPath
        $actualHash = $hash.Hash.ToLowerInvariant()
        $expectedHash = $declaredHash.Trim().ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            $items += [pscustomobject]@{
                InvalidManifest = $true
                Scope = $scope
                Message = "evidence manifest scope sha256 不匹配：$scope expected=$expectedHash actual=$actualHash"
            }
            continue
        }

        $detectorName = [string]$scopeNode.detector
        if ([string]::IsNullOrWhiteSpace($detectorName)) {
            $items += [pscustomobject]@{
                InvalidManifest = $true
                Scope = $scope
                Message = "evidence manifest scope 缺少 detector：$scope"
            }
            continue
        }

        $scopeDetectorRunId = Get-RequiredManifestString -Node $scopeNode -Name "detectorRunId" -Prefix "scope $scope"
        if ([string]::IsNullOrWhiteSpace($scopeDetectorRunId)) {
            $items += [pscustomobject]@{
                InvalidManifest = $true
                Scope = $scope
                Message = "evidence manifest scope $scope 缺少 detectorRunId。"
            }
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($manifestDetectorRunId) -and
            -not [string]::Equals($scopeDetectorRunId, $manifestDetectorRunId, [StringComparison]::Ordinal)) {
            $items += [pscustomobject]@{
                InvalidManifest = $true
                Scope = $scope
                Message = "evidence scope $scope detectorRunId 必须与 manifest detectorRunId 一致：expected=$manifestDetectorRunId actual=$scopeDetectorRunId"
            }
            continue
        }

        $scopeGitCommit = Get-RequiredManifestString -Node $scopeNode -Name "gitCommit" -Prefix "scope $scope"
        if ([string]::IsNullOrWhiteSpace($scopeGitCommit)) {
            $items += [pscustomobject]@{
                InvalidManifest = $true
                Scope = $scope
                Message = "evidence manifest scope $scope 缺少 gitCommit。"
            }
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($manifestGitCommit) -and
            -not [string]::Equals($scopeGitCommit, $manifestGitCommit, [StringComparison]::OrdinalIgnoreCase)) {
            $items += [pscustomobject]@{
                InvalidManifest = $true
                Scope = $scope
                Message = "evidence scope $scope gitCommit 必须与 manifest gitCommit 一致：expected=$manifestGitCommit actual=$scopeGitCommit"
            }
            continue
        }

        $reportValues = Read-MarkdownEvidenceTable -Path $reportPath
        $semanticInvalid = $false
        foreach ($key in @("scope", "detector", "detectorRunId", "gitCommit", "conclusion")) {
            if (-not $reportValues.ContainsKey($key)) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scope
                    Message = "evidence report $scope 缺少 $key 字段"
                }
                $semanticInvalid = $true
            }
        }

        if (-not $semanticInvalid) {
            if (-not [string]::Equals([string]$reportValues["scope"], $scope, [StringComparison]::OrdinalIgnoreCase)) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scope
                    Message = "evidence report $scope scope 必须为 $scope，实际为 $($reportValues["scope"])"
                }
                $semanticInvalid = $true
            }

            if (-not [string]::Equals([string]$reportValues["detector"], $detectorName, [StringComparison]::Ordinal)) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scope
                    Message = "evidence report $scope detector 必须为 $detectorName，实际为 $($reportValues["detector"])"
                }
                $semanticInvalid = $true
            }

            if (-not [string]::Equals([string]$reportValues["detectorRunId"], $manifestDetectorRunId, [StringComparison]::Ordinal)) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scope
                    Message = "evidence report $scope detectorRunId 必须为 $manifestDetectorRunId，实际为 $($reportValues["detectorRunId"])"
                }
                $semanticInvalid = $true
            }

            if (-not [string]::Equals([string]$reportValues["gitCommit"], $manifestGitCommit, [StringComparison]::OrdinalIgnoreCase)) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scope
                    Message = "evidence report $scope gitCommit 必须为 $manifestGitCommit，实际为 $($reportValues["gitCommit"])"
                }
                $semanticInvalid = $true
            }

            if (-not [string]::Equals([string]$reportValues["conclusion"], "no_leaks", [StringComparison]::OrdinalIgnoreCase)) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scope
                    Message = "evidence report $scope conclusion 必须为 no_leaks，实际为 $($reportValues["conclusion"])"
                }
                $semanticInvalid = $true
            }

            $requiredMetric = Get-NativeLeakScopeRequiredMetric -Scope $scope
            $liveCountIssue = Test-NativeLeakLiveCountZero -Values $reportValues -Metric $requiredMetric -Prefix "evidence report $scope"
            if (-not [string]::IsNullOrWhiteSpace($liveCountIssue)) {
                $items += [pscustomobject]@{
                    InvalidManifest = $true
                    Scope = $scope
                    Message = $liveCountIssue
                }
                $semanticInvalid = $true
            }
        }

        if ($semanticInvalid) {
            continue
        }

        $items += [pscustomobject]@{
            Scope = $scope
            Detector = $detectorName
            Report = ConvertTo-RelativePath -Root $Root -Path $reportPath
            Sha256 = $actualHash
        }
    }

    return $items
}

function New-EditorShellSmokeProject {
    param([string]$OutputDirectory)

    $projectRoot = Join-Path $OutputDirectory "editor-shell-project"
    $sceneRoot = Join-Path $projectRoot "content/scenes"
    New-Item -ItemType Directory -Force -Path $sceneRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot "scripts") | Out-Null
    @"
{
  "formatVersion": 1,
  "name": "Editor Shell Smoke",
  "contentRoot": "content",
  "scriptSourceDir": "scripts",
  "startScene": "scenes/main.scene",
  "scenes": [
    {
      "name": "main",
      "path": "scenes/main.scene"
    }
  ]
}
"@ | Set-Content -LiteralPath (Join-Path $projectRoot "project.pixelproj") -Encoding UTF8
    @"
{
  "formatVersion": 2,
  "name": "main",
  "entities": []
}
"@ | Set-Content -LiteralPath (Join-Path $sceneRoot "main.scene") -Encoding UTF8
    return $projectRoot
}

function Invoke-WindowProbe {
    param(
        [string]$Root,
        [string]$Mode,
        [string]$ProjectPath,
        [string]$ContentRoot,
        [string]$ScenePath,
        [string]$OutputDirectory,
        [int]$Ticks,
        [bool]$Editor
    )

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $stdout = Join-Path $OutputDirectory "$Mode.stdout.txt"
    $stderr = Join-Path $OutputDirectory "$Mode.stderr.txt"
    $runtimeLog = Join-Path $OutputDirectory "runtime"

    $arguments = @(
        "run",
        "--project", $ProjectPath,
        "-c", "Release",
        "--no-restore",
        "--",
        "--no-hot-reload",
        "--window-ticks", $Ticks.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--content", $ContentRoot,
        "--scene", $ScenePath,
        "--log-dir", $runtimeLog
    )

    if ($Editor) {
        $editorProjectRoot = New-EditorShellSmokeProject -OutputDirectory $OutputDirectory
        $arguments = @(
            "run",
            "--project", $EditorShellProject,
            "-c", "Release",
            "--no-restore",
            "--",
            "--project", $editorProjectRoot,
            "--scene", "scenes/main.scene",
            "--window-ticks", $Ticks.ToString([Globalization.CultureInfo]::InvariantCulture),
            "--scripted-probe",
            "--log-directory", $runtimeLog
        )
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    foreach ($argument in $arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $startInfo.WorkingDirectory = $Root
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $utf8NoBom
    $startInfo.StandardErrorEncoding = $utf8NoBom
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $peak = 0L
    $null = $process.Start()

    while (-not $process.WaitForExit(200)) {
        try {
            $process.Refresh()
            if ($process.WorkingSet64 -gt $peak) {
                $peak = $process.WorkingSet64
            }
        }
        catch {
            break
        }
    }

    $stopwatch.Stop()
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    Set-Content -Path $stdout -Value $output -Encoding UTF8
    Set-Content -Path $stderr -Value $errorOutput -Encoding UTF8

    if ($process.WorkingSet64 -gt $peak) {
        $peak = $process.WorkingSet64
    }

    [pscustomobject]@{
        Mode = $Mode
        ExitCode = $process.ExitCode
        ElapsedMs = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
        PeakWorkingSetMB = [Math]::Round($peak / 1MB, 2)
        StdoutPath = ConvertTo-RelativePath -Root $Root -Path $stdout
    }
}

$root = Resolve-RepositoryRoot
Set-Location $root

$artifactRoot = if ([IO.Path]::IsPathRooted($Artifacts)) { $Artifacts } else { Join-Path $root $Artifacts }
$reportPath = Join-Path $artifactRoot "native-leak-preflight.md"
$runs = @()
$evidence = Read-EvidenceManifest -Root $root -ManifestPath $EvidenceManifestPath

if ($RunProcessSmoke) {
    $runs += Invoke-WindowProbe -Root $root -Mode "window" -ProjectPath $Project -ContentRoot $Content -ScenePath $Scene -OutputDirectory (Join-Path $artifactRoot "window") -Ticks $WindowTicks -Editor $false
    if ($IncludeEditor) {
        $runs += Invoke-WindowProbe -Root $root -Mode "editor" -ProjectPath $Project -ContentRoot $Content -ScenePath $Scene -OutputDirectory (Join-Path $artifactRoot "editor") -Ticks $EditorTicks -Editor $true
    }

    foreach ($run in $runs) {
        if ($run.ExitCode -ne 0) {
            $detail = "Process smoke failed before native leak detector evidence could be evaluated. See stdout/stderr under $Artifacts."
            Write-NativeLeakReport -Path $reportPath -Status "process_smoke_failed" -Detector $DetectorName -DetectorReport $DetectorReportPath -Evidence $evidence -Runs $runs -Detail $detail
            exit 3
        }
    }
}

$missingEvidence = @($evidence | Where-Object { $_.MissingManifest -or $_.MissingScope })
$invalidEvidence = @($evidence | Where-Object { $_.InvalidManifest })
if ($invalidEvidence.Count -gt 0) {
    $detail = "Native leak preflight failed: evidence manifest JSON、schema、scope 或 SHA256 无效。错误项：" + (($invalidEvidence | ForEach-Object { $_.Message }) -join "; ")
    Write-NativeLeakReport -Path $reportPath -Status "blocked_invalid_native_leak_evidence" -Detector $DetectorName -DetectorReport $DetectorReportPath -Evidence $evidence -Runs $runs -Detail $detail
    Write-Host "Native leak preflight blocked_invalid_native_leak_evidence. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 5
}

if ($missingEvidence.Count -gt 0) {
    $detail = "Native leak preflight failed: evidence manifest 缺少 GL/OpenAL/Box2D/ALC 中的一个或多个工具级报告。缺失项：" + (($missingEvidence | ForEach-Object { $_.Message }) -join "; ")
    Write-NativeLeakReport -Path $reportPath -Status "blocked_missing_scope_evidence" -Detector $DetectorName -DetectorReport $DetectorReportPath -Evidence $evidence -Runs $runs -Detail $detail
    Write-Host "Native leak preflight blocked_missing_scope_evidence. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 5
}

if ($evidence.Count -eq 4) {
    $detail = "External detector evidence manifest attached. Four required scopes are present and SHA256 hashes were recorded. Human review must confirm the reports cover GL, OpenAL, Box2D and ALC with no leaks before EVID-002 can be completed."
    Write-NativeLeakReport -Path $reportPath -Status "detector_evidence_attached_pending_review" -Detector $DetectorName -DetectorReport $DetectorReportPath -Evidence $evidence -Runs $runs -Detail $detail
    Write-Host "Native leak preflight detector_evidence_attached_pending_review. Report: $reportPath"
    if (-not $AllowBlocked) {
        [Console]::Error.WriteLine("Native leak preflight failed: detector_evidence_attached_pending_review")
        exit 2
    }

    exit 0
}

$hasDetectorReport = -not [string]::IsNullOrWhiteSpace($DetectorReportPath) -and (Test-Path $DetectorReportPath)
if (-not $hasDetectorReport) {
    $status = $RunProcessSmoke ? "process_smoke_only" : "blocked_missing_detector"
    $detail = "Native leak preflight failed: 需要专用 detector 报告覆盖 GL、OpenAL、Box2D 与 ALC 释放路径。进程退出码和 PeakWorkingSetMB 只能作为 smoke 证据，不能替代 GPU driver/OpenAL/Box2D 工具级泄漏审计。"
    Write-NativeLeakReport -Path $reportPath -Status $status -Detector $DetectorName -DetectorReport $DetectorReportPath -Evidence $evidence -Runs $runs -Detail $detail
    Write-Host "Native leak preflight $status. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 2
}

$singleDetectorIssues = @(Test-SingleDetectorReport -Path $DetectorReportPath -DetectorName $DetectorName)
if ($singleDetectorIssues.Count -gt 0) {
    $detail = "Native leak preflight failed: 单 detector report 缺少机器可读 no-leaks 覆盖证据。错误项：" + ($singleDetectorIssues -join "; ")
    Write-NativeLeakReport -Path $reportPath -Status "blocked_invalid_native_leak_evidence" -Detector $DetectorName -DetectorReport $DetectorReportPath -Evidence $evidence -Runs $runs -Detail $detail
    Write-Host "Native leak preflight blocked_invalid_native_leak_evidence. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 5
}

$hash = Get-FileHash -Algorithm SHA256 -Path $DetectorReportPath
$relativeDetectorReport = ConvertTo-RelativePath -Root $root -Path $DetectorReportPath
$detail = "External detector report attached. detector=$DetectorName report=$relativeDetectorReport sha256=$($hash.Hash). Machine-readable no-leaks coverage for GL, OpenAL, Box2D and ALC is present; human review must still confirm detector quality before EVID-002 can be completed."
Write-NativeLeakReport -Path $reportPath -Status "detector_report_attached_pending_review" -Detector $DetectorName -DetectorReport $relativeDetectorReport -Evidence $evidence -Runs $runs -Detail $detail
Write-Host "Native leak preflight detector_report_attached_pending_review. Report: $reportPath"
if (-not $AllowBlocked) {
    [Console]::Error.WriteLine("Native leak preflight failed: detector_report_attached_pending_review")
    exit 2
}
