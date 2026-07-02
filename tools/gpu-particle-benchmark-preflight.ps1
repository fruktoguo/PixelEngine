param(
    [string]$Project = "demo/PixelEngine.Demo/PixelEngine.Demo.csproj",
    [string]$Content = "demo/PixelEngine.Demo/content",
    [string]$Artifacts = "artifacts/gpu-particle-benchmark-preflight",
    [int]$ParticleCount = 100000,
    [int]$WindowTicks = 8,
    [int]$WarmupFrames = 2,
    [switch]$RunProbe,
    [string]$EvidenceManifestPath = "",
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

function Write-Report {
    param(
        [string]$ReportPath,
        [string]$Status,
        [object[]]$Evidence,
        [object[]]$ProbeSummaries,
        [string[]]$Notes,
        [int]$ExitCode
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# GPU 粒子目标硬件基准预检")
    $lines.Add("")
    $lines.Add("status: $Status")
    $lines.Add("exit_code: $ExitCode")
    $lines.Add("particle_count: $ParticleCount")
    $lines.Add("window_ticks: $WindowTicks")
    $lines.Add("warmup_frames: $WarmupFrames")
    $lines.Add("")
    $lines.Add("## 说明")
    $lines.Add("")
    $lines.Add('该脚本只负责收集/校验 plan/09 高密度 GPU 粒子基准证据。`local_probe_only` 与 `target_gpu_evidence_attached_pending_review` 都不是验收通过状态；plan 勾选仍需要人工确认目标 GPU 硬件上的长基准结论。')
    $lines.Add("")

    if ($ProbeSummaries.Count -gt 0) {
        $lines.Add("## 本机 probe")
        $lines.Add("")
        foreach ($summary in $ProbeSummaries) {
            $lines.Add("### $($summary.mode)")
            $lines.Add("")
            $lines.Add(('stdout: `{0}`' -f $summary.stdout))
            $lines.Add(('stderr: `{0}`' -f $summary.stderr))
            $lines.Add("exit_code: $($summary.exitCode)")
            $lines.Add("")
            $lines.Add('```text')
            $lines.Add($summary.summaryLine)
            $lines.Add('```')
            $lines.Add("")
        }
    }

    if ($Evidence.Count -gt 0) {
        $lines.Add("## 目标硬件证据")
        $lines.Add("")
        foreach ($item in $Evidence) {
            $lines.Add("- scope=$($item.scope); path=$($item.path); sha256=$($item.sha256)")
        }
        $lines.Add("")
    }

    if ($Notes.Count -gt 0) {
        $lines.Add("## 备注")
        $lines.Add("")
        foreach ($note in $Notes) {
            $lines.Add("- $note")
        }
        $lines.Add("")
    }

    [System.IO.File]::WriteAllLines($ReportPath, $lines, [System.Text.UTF8Encoding]::new($false))
}

function Invoke-ParticleProbe {
    param(
        [string]$Mode,
        [string]$Root,
        [string]$ProbeArtifacts
    )

    $modeDirectory = Join-Path $ProbeArtifacts $Mode
    New-Item -ItemType Directory -Force -Path $modeDirectory | Out-Null
    $stdoutPath = Join-Path $modeDirectory "stdout.txt"
    $stderrPath = Join-Path $modeDirectory "stderr.txt"
    $logDirectory = Join-Path $modeDirectory "logs"

    $arguments = @(
        "run",
        "--project", $Project,
        "-c", "Release",
        "--no-build",
        "--",
        "--no-hot-reload",
        "--window-ticks", $WindowTicks.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--particle-frame-probe",
        "--particle-render-mode", $Mode,
        "--particle-count", $ParticleCount.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--particle-probe-warmup", $WarmupFrames.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--content", $Content,
        "--log-dir", $logDirectory
    )

    $process = Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $Root -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $stdout = Get-Content -LiteralPath $stdoutPath -Raw
    $summaryLine = ($stdout -split "`r?`n" | Where-Object { $_.StartsWith("particle_frame_probe", [StringComparison]::Ordinal) } | Select-Object -Last 1)

    if ([string]::IsNullOrWhiteSpace($summaryLine)) {
        throw "未在 $Mode probe 输出中找到 particle_frame_probe 摘要。"
    }

    [pscustomobject]@{
        mode = $Mode
        stdout = ConvertTo-RepositoryRelativePath -Root $Root -Path $stdoutPath
        stderr = ConvertTo-RepositoryRelativePath -Root $Root -Path $stderrPath
        exitCode = $process.ExitCode
        summaryLine = $summaryLine
    }
}

function Read-EvidenceManifest {
    param(
        [string]$Root,
        [string]$ManifestPath
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "证据清单不存在：$ManifestPath"
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "GPU 粒子基准 evidence manifest schemaVersion 必须为 1。"
    }

    $requiredScopes = @(
        "targetHardwareReport",
        "cpuProbeReport",
        "gpuProbeReport",
        "comparisonReport"
    )

    $entries = @($manifest.evidence)
    $scopes = @{}
    foreach ($entry in $entries) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.scope)) {
            throw "evidence entry 缺少 scope。"
        }

        $scope = [string]$entry.scope
        if ($scope -notin $requiredScopes) {
            throw "未知 evidence scope：$scope"
        }

        if ($scopes.ContainsKey($scope)) {
            throw "重复 evidence scope：$scope"
        }

        $scopes[$scope] = $entry
    }

    $missing = @($requiredScopes | Where-Object { -not $scopes.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        return [pscustomobject]@{
            status = "blocked_missing_target_gpu_scope_evidence"
            missing = $missing
            evidence = @()
        }
    }

    $evidence = [System.Collections.Generic.List[object]]::new()
    foreach ($scope in $requiredScopes) {
        $entry = $scopes[$scope]
        if ([string]::IsNullOrWhiteSpace([string]$entry.path)) {
            throw "evidence scope $scope 缺少 path。"
        }

        $path = [string]$entry.path
        if (-not [System.IO.Path]::IsPathRooted($path)) {
            $path = Join-Path $Root $path
        }

        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "evidence scope $scope 指向文件不存在：$path"
        }

        $hashProperty = $entry.PSObject.Properties | Where-Object { $_.Name -eq "sha256" } | Select-Object -First 1
        $declaredHash = if ($null -eq $hashProperty) { "" } else { [string]$hashProperty.Value }
        if ([string]::IsNullOrWhiteSpace($declaredHash)) {
            throw "evidence scope $scope 缺少 sha256。"
        }

        $actualHash = Get-FileSha256 -Path $path
        $expectedHash = $declaredHash.Trim().ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "evidence scope $scope sha256 不匹配：expected=$expectedHash actual=$actualHash"
        }

        $evidence.Add([pscustomobject]@{
            scope = $scope
            path = ConvertTo-RepositoryRelativePath -Root $Root -Path $path
            sha256 = $actualHash
        })
    }

    [pscustomobject]@{
        status = "target_gpu_evidence_attached_pending_review"
        missing = @()
        evidence = @($evidence)
    }
}

$root = Resolve-RepositoryRoot
$artifactRoot = if ([System.IO.Path]::IsPathRooted($Artifacts)) { $Artifacts } else { Join-Path $root $Artifacts }
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$reportPath = Join-Path $artifactRoot "gpu-particle-benchmark-preflight.md"
$notes = [System.Collections.Generic.List[string]]::new()
$probeSummaries = [System.Collections.Generic.List[object]]::new()
$evidence = @()
$status = "blocked_missing_target_gpu_evidence"
$exitCode = 2

if ($RunProbe) {
    $probeRoot = Join-Path $artifactRoot "local-probe"
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
    $probeSummaries.Add((Invoke-ParticleProbe -Mode "cpu" -Root $root -ProbeArtifacts $probeRoot))
    $probeSummaries.Add((Invoke-ParticleProbe -Mode "gpu" -Root $root -ProbeArtifacts $probeRoot))
    $status = "local_probe_only"
    $notes.Add("本机 probe 只能证明 --particle-frame-probe 与 cpu/gpu 路径可执行，不能替代目标 GPU 硬件长基准。")
}

if (-not [string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    $manifestPath = if ([System.IO.Path]::IsPathRooted($EvidenceManifestPath)) { $EvidenceManifestPath } else { Join-Path $root $EvidenceManifestPath }
    try {
        $manifestResult = Read-EvidenceManifest -Root $root -ManifestPath $manifestPath
        $status = $manifestResult.status
        $evidence = @($manifestResult.evidence)
        if ($manifestResult.missing.Count -gt 0) {
            $notes.Add("缺少目标 GPU evidence scope：$($manifestResult.missing -join ', ')。")
            $exitCode = 5
        }
        else {
            $notes.Add("已附加目标 GPU 硬件基准证据清单，但仍需人工确认 comparisonReport 与 plan/09 验收语义。")
            $exitCode = 2
        }
    }
    catch {
        $status = "blocked_invalid_target_gpu_evidence"
        $evidence = @()
        $notes.Add("目标 GPU evidence manifest 无效：$($_.Exception.Message)")
        $exitCode = 5
    }
}

Write-Report -ReportPath $reportPath -Status $status -Evidence $evidence -ProbeSummaries @($probeSummaries) -Notes @($notes) -ExitCode $exitCode
Write-Host "GPU particle benchmark preflight status: $status"
Write-Host "Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if ($exitCode -ne 0 -and -not $AllowBlocked) {
    [Console]::Error.WriteLine("GPU particle benchmark preflight failed: $status")
    exit $exitCode
}

exit 0
