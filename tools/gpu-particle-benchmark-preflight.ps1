param(
    [string]$Project = "demo/PixelEngine.Demo/PixelEngine.Demo.csproj",
    [string]$Content = "demo/PixelEngine.Demo/content",
    [string]$Artifacts = "artifacts/gpu-particle-benchmark-preflight",
    [int]$ParticleCount = 100000,
    [int]$WindowTicks = 8,
    [int]$WarmupFrames = 2,
    [switch]$RunProbe,
    [string]$EvidenceManifestPath = "",
    [string]$DotNetPath = "dotnet",
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
        throw "$Scope 缺少机器可读字段 $Name。"
    }

    return [string]$Fields[$Name]
}

function Get-RequiredFieldDouble {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    $value = Get-RequiredField -Fields $Fields -Name $Name -Scope $Scope
    return [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-RequiredFieldInt {
    param(
        [System.Collections.IDictionary]$Fields,
        [string]$Name,
        [string]$Scope
    )

    $value = Get-RequiredField -Fields $Fields -Name $Name -Scope $Scope
    return [int]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}

function Assert-NearlyEqual {
    param(
        [double]$Actual,
        [double]$Expected,
        [string]$Scope,
        [string]$Name,
        [string]$Expectation = "probe summary"
    )

    $tolerance = [Math]::Max(0.001, [Math]::Abs($Expected) * 0.001)
    if ([Math]::Abs($Actual - $Expected) -gt $tolerance) {
        throw "$Scope $Name 必须与 $Expectation 一致：actual=$Actual expected=$Expected tolerance=$tolerance。"
    }
}

function Assert-TargetHardwareReport {
    param([string]$Path)

    $fields = Read-MachineReadableFields -Path $Path
    foreach ($required in @("targetGpuName", "targetGpuDriver", "gpuBackend", "operatingSystem", "cpuName", "dotnetVersion", "gitCommit")) {
        [void](Get-RequiredField -Fields $fields -Name $required -Scope "targetHardwareReport")
    }

    return $fields
}

function Assert-ComparisonReport {
    param(
        [string]$Path,
        [System.Collections.IDictionary]$CpuMetrics,
        [System.Collections.IDictionary]$GpuMetrics
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -notmatch '(?im)^\s*gpuFasterThanCpu\s*[:=]\s*true\s*$') {
        throw "comparisonReport 必须包含机器可读字段 gpuFasterThanCpu: true，证明目标 GPU 高密度粒子总帧时间优于 CPU stamp。"
    }

    $fields = Read-MachineReadableFields -Path $Path
    $cpuWall = Get-RequiredFieldDouble -Fields $fields -Name "cpuWallAvgMs" -Scope "comparisonReport"
    $gpuWall = Get-RequiredFieldDouble -Fields $fields -Name "gpuWallAvgMs" -Scope "comparisonReport"
    $speedupRatio = Get-RequiredFieldDouble -Fields $fields -Name "speedupRatio" -Scope "comparisonReport"
    $measuredFrames = Get-RequiredFieldInt -Fields $fields -Name "measuredFrames" -Scope "comparisonReport"
    $sampleSeconds = Get-RequiredFieldDouble -Fields $fields -Name "sampleSeconds" -Scope "comparisonReport"

    if ($cpuWall -le $gpuWall) {
        throw "comparisonReport cpuWallAvgMs 必须大于 gpuWallAvgMs，实际 cpu=$cpuWall gpu=$gpuWall。"
    }

    $cpuProbeWall = Get-MetricDouble -Metrics $CpuMetrics -Name "wall_avg_ms"
    $gpuProbeWall = Get-MetricDouble -Metrics $GpuMetrics -Name "wall_avg_ms"
    Assert-NearlyEqual -Actual $cpuWall -Expected $cpuProbeWall -Scope "comparisonReport" -Name "cpuWallAvgMs"
    Assert-NearlyEqual -Actual $gpuWall -Expected $gpuProbeWall -Scope "comparisonReport" -Name "gpuWallAvgMs"

    $recomputedSpeedup = $cpuWall / $gpuWall
    Assert-NearlyEqual -Actual $speedupRatio -Expected $recomputedSpeedup -Scope "comparisonReport" -Name "speedupRatio" -Expectation "cpuWallAvgMs/gpuWallAvgMs 重算值"

    if ($speedupRatio -le 1.0) {
        throw "comparisonReport speedupRatio 必须大于 1。"
    }

    if ($measuredFrames -lt 300) {
        throw "comparisonReport measuredFrames 必须至少为 300。"
    }

    if ($sampleSeconds -lt 10.0) {
        throw "comparisonReport sampleSeconds 必须至少为 10 秒。"
    }

    $cpuProbeFrames = Get-MetricInt -Metrics $CpuMetrics -Name "measured_frames"
    $gpuProbeFrames = Get-MetricInt -Metrics $GpuMetrics -Name "measured_frames"
    if ($measuredFrames -gt $cpuProbeFrames -or $measuredFrames -gt $gpuProbeFrames) {
        throw "comparisonReport measuredFrames 不能大于 cpu/gpu probe 报告的 measured_frames。"
    }
}

function Get-ParticleProbeSummaryFromReport {
    param(
        [string]$Path,
        [string]$Scope
    )

    $summary = (Get-Content -LiteralPath $Path | Where-Object { $_.TrimStart().StartsWith("particle_frame_probe", [StringComparison]::Ordinal) } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($summary)) {
        throw "$Scope 必须包含 particle_frame_probe summary。"
    }

    return $summary
}

function Assert-TargetProbeReport {
    param(
        [string]$Path,
        [string]$ExpectedMode
    )

    $scopeName = "${ExpectedMode}ProbeReport"
    $summary = Get-ParticleProbeSummaryFromReport -Path $Path -Scope $scopeName
    $metrics = Convert-ParticleProbeSummaryToMetrics -SummaryLine $summary
    foreach ($required in @("mode", "gpu_available", "requested_count", "active_count", "measured_frames", "wall_avg_ms", "particle_stamp_avg_ms", "gpu_particle_avg_ms")) {
        if (-not $metrics.Contains($required)) {
            throw "$scopeName 摘要缺少字段 $required。"
        }
    }

    $actualMode = [string]$metrics["mode"]
    if (-not [string]::Equals($actualMode, $ExpectedMode, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$scopeName 实际 mode=$actualMode。"
    }

    $requested = Get-MetricInt -Metrics $metrics -Name "requested_count"
    $active = Get-MetricInt -Metrics $metrics -Name "active_count"
    $measured = Get-MetricInt -Metrics $metrics -Name "measured_frames"
    if ($requested -lt 100000 -or $active -lt 100000) {
        throw "$scopeName requested_count/active_count 必须至少为 100000。"
    }

    if ($requested -ne $active) {
        throw "$scopeName requested_count 必须等于 active_count。"
    }

    if ($measured -lt 300) {
        throw "$scopeName measured_frames 必须至少为 300。"
    }

    $cpuStampAvg = Get-MetricDouble -Metrics $metrics -Name "particle_stamp_avg_ms"
    $gpuParticleAvg = Get-MetricDouble -Metrics $metrics -Name "gpu_particle_avg_ms"
    if ($ExpectedMode -eq "cpu") {
        if ($cpuStampAvg -le 0 -or $gpuParticleAvg -ne 0) {
            throw "cpuProbeReport 必须走 CPU stamp：particle_stamp_avg_ms>0 且 gpu_particle_avg_ms=0。"
        }
    }
    else {
        $gpuAvailable = Get-MetricBool -Metrics $metrics -Name "gpu_available"
        if (-not $gpuAvailable -or $gpuParticleAvg -le 0 -or $cpuStampAvg -ne 0) {
            throw "gpuProbeReport 必须走 GPU point-sprite：gpu_available=True、gpu_particle_avg_ms>0 且 particle_stamp_avg_ms=0。"
        }
    }

    return $metrics
}

function Assert-TargetProbePair {
    param(
        [System.Collections.IDictionary]$CpuMetrics,
        [System.Collections.IDictionary]$GpuMetrics,
        [System.Collections.IDictionary]$HardwareFields
    )

    $cpuRequested = Get-MetricInt -Metrics $CpuMetrics -Name "requested_count"
    $gpuRequested = Get-MetricInt -Metrics $GpuMetrics -Name "requested_count"
    if ($cpuRequested -ne $gpuRequested) {
        throw "cpuProbeReport 与 gpuProbeReport requested_count 必须一致。"
    }

    if ($HardwareFields.Contains("particleCount")) {
        $hardwareParticleCount = [int]::Parse([string]$HardwareFields["particleCount"], [Globalization.CultureInfo]::InvariantCulture)
        if ($hardwareParticleCount -ne $cpuRequested) {
            throw "targetHardwareReport particleCount 必须与 probe requested_count 一致。"
        }
    }
}

function Convert-ParticleProbeSummaryToMetrics {
    param([string]$SummaryLine)

    $metrics = [ordered]@{}
    foreach ($part in ($SummaryLine -split ',\s*')) {
        $token = $part.Trim()
        if ($token.StartsWith("particle_frame_probe ", [StringComparison]::Ordinal)) {
            $token = $token.Substring("particle_frame_probe ".Length)
        }

        if ($token -match '^\s*([^=]+)=(.*?)\s*$') {
            $metrics[$Matches[1].Trim()] = $Matches[2].Trim()
        }
    }

    return $metrics
}

function Get-MetricDouble {
    param(
        [System.Collections.IDictionary]$Metrics,
        [string]$Name
    )

    if (-not $Metrics.Contains($Name)) {
        return [double]::NaN
    }

    return [double]::Parse([string]$Metrics[$Name], [Globalization.CultureInfo]::InvariantCulture)
}

function Get-MetricInt {
    param(
        [System.Collections.IDictionary]$Metrics,
        [string]$Name
    )

    if (-not $Metrics.Contains($Name)) {
        return 0
    }

    return [int]::Parse([string]$Metrics[$Name], [Globalization.CultureInfo]::InvariantCulture)
}

function Get-MetricBool {
    param(
        [System.Collections.IDictionary]$Metrics,
        [string]$Name
    )

    if (-not $Metrics.Contains($Name)) {
        return $false
    }

    return [bool]::Parse([string]$Metrics[$Name])
}

function Format-MetricDouble {
    param([double]$Value)

    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) {
        return "n/a"
    }

    return $Value.ToString("0.###", [Globalization.CultureInfo]::InvariantCulture)
}

function Assert-LocalProbeMetrics {
    param(
        [object]$Probe,
        [string]$ExpectedMode
    )

    $metrics = $Probe.metrics
    foreach ($required in @("mode", "gpu_available", "requested_count", "active_count", "measured_frames", "wall_avg_ms", "particle_stamp_avg_ms", "gpu_particle_avg_ms")) {
        if (-not $metrics.Contains($required)) {
            throw "$ExpectedMode probe 摘要缺少字段 $required。"
        }
    }

    $actualMode = [string]$metrics["mode"]
    if (-not [string]::Equals($actualMode, $ExpectedMode, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$ExpectedMode probe 实际 mode=$actualMode，可能发生 GPU/CPU 回退或输出混淆。"
    }

    $requested = Get-MetricInt -Metrics $metrics -Name "requested_count"
    if ($requested -ne $ParticleCount) {
        throw "$ExpectedMode probe requested_count=$requested，与请求粒子数 $ParticleCount 不一致。"
    }

    $active = Get-MetricInt -Metrics $metrics -Name "active_count"
    if ($active -ne $ParticleCount) {
        throw "$ExpectedMode probe active_count=$active，与请求粒子数 $ParticleCount 不一致。"
    }

    $expectedMeasuredFrames = $WindowTicks - $WarmupFrames
    $measured = Get-MetricInt -Metrics $metrics -Name "measured_frames"
    if ($expectedMeasuredFrames -le 0 -or $measured -ne $expectedMeasuredFrames) {
        throw "$ExpectedMode probe measured_frames=$measured，期望 $expectedMeasuredFrames；window_ticks 必须大于 warmup_frames 且样本帧完整。"
    }

    $cpuStampAvg = Get-MetricDouble -Metrics $metrics -Name "particle_stamp_avg_ms"
    $gpuParticleAvg = Get-MetricDouble -Metrics $metrics -Name "gpu_particle_avg_ms"
    if ($ExpectedMode -eq "cpu") {
        if ($cpuStampAvg -le 0) {
            throw "cpu probe particle_stamp_avg_ms 必须大于 0。"
        }

        if ($gpuParticleAvg -ne 0) {
            throw "cpu probe gpu_particle_avg_ms 必须为 0。"
        }
    }
    else {
        $gpuAvailable = Get-MetricBool -Metrics $metrics -Name "gpu_available"
        if (-not $gpuAvailable) {
            throw "gpu probe gpu_available 必须为 True，不能把 GPU 不可用回退当成本机 GPU probe。"
        }

        if ($gpuParticleAvg -le 0) {
            throw "gpu probe gpu_particle_avg_ms 必须大于 0。"
        }

        if ($cpuStampAvg -ne 0) {
            throw "gpu probe particle_stamp_avg_ms 必须为 0。"
        }
    }
}

function Write-LocalComparison {
    param(
        [string]$Root,
        [string]$ArtifactRoot,
        [object]$CpuProbe,
        [object]$GpuProbe
    )

    $cpu = $CpuProbe.metrics
    $gpu = $GpuProbe.metrics
    $cpuStampAvg = Get-MetricDouble -Metrics $cpu -Name "particle_stamp_avg_ms"
    $gpuParticleAvg = Get-MetricDouble -Metrics $gpu -Name "gpu_particle_avg_ms"
    $cpuWallAvg = Get-MetricDouble -Metrics $cpu -Name "wall_avg_ms"
    $gpuWallAvg = Get-MetricDouble -Metrics $gpu -Name "wall_avg_ms"
    $particleDrawSpeedup = if ($gpuParticleAvg -gt 0) { $cpuStampAvg / $gpuParticleAvg } else { [double]::NaN }
    $wallSpeedup = if ($gpuWallAvg -gt 0) { $cpuWallAvg / $gpuWallAvg } else { [double]::NaN }
    $localGpuParticleDrawFaster = $gpuParticleAvg -gt 0 -and $cpuStampAvg -gt $gpuParticleAvg
    $localGpuWallTimeFaster = $gpuWallAvg -gt 0 -and $cpuWallAvg -gt $gpuWallAvg

    $jsonPath = Join-Path $ArtifactRoot "local-comparison.json"
    $markdownPath = Join-Path $ArtifactRoot "local-comparison.md"
    $comparison = [ordered]@{
        localOnly = $true
        targetGpuEvidence = $false
        local_gpu_particle_draw_faster = $localGpuParticleDrawFaster
        local_gpu_wall_time_faster = $localGpuWallTimeFaster
        local_particle_draw_speedup = if ([double]::IsNaN($particleDrawSpeedup)) { $null } else { $particleDrawSpeedup }
        local_wall_speedup = if ([double]::IsNaN($wallSpeedup)) { $null } else { $wallSpeedup }
        cpu_measured_frames = Get-MetricInt -Metrics $cpu -Name "measured_frames"
        gpu_measured_frames = Get-MetricInt -Metrics $gpu -Name "measured_frames"
        cpu_active_count = Get-MetricInt -Metrics $cpu -Name "active_count"
        gpu_active_count = Get-MetricInt -Metrics $gpu -Name "active_count"
        gpu_available = Get-MetricBool -Metrics $gpu -Name "gpu_available"
        cpu_wall_avg_ms = $cpuWallAvg
        gpu_wall_avg_ms = $gpuWallAvg
        cpu_particle_stamp_avg_ms = $cpuStampAvg
        gpu_particle_avg_ms = $gpuParticleAvg
    }

    $comparison | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# GPU 粒子本机短 probe 对比")
    $lines.Add("")
    $lines.Add("local_only: true")
    $lines.Add("target_gpu_evidence: false")
    $lines.Add("local_gpu_particle_draw_faster: $($localGpuParticleDrawFaster.ToString().ToLowerInvariant())")
    $lines.Add("local_gpu_wall_time_faster: $($localGpuWallTimeFaster.ToString().ToLowerInvariant())")
    $lines.Add("local_particle_draw_speedup: $(Format-MetricDouble $particleDrawSpeedup)")
    $lines.Add("local_wall_speedup: $(Format-MetricDouble $wallSpeedup)")
    $lines.Add("cpu_measured_frames: $($comparison.cpu_measured_frames)")
    $lines.Add("gpu_measured_frames: $($comparison.gpu_measured_frames)")
    $lines.Add("cpu_active_count: $($comparison.cpu_active_count)")
    $lines.Add("gpu_active_count: $($comparison.gpu_active_count)")
    $lines.Add("gpu_available: $($comparison.gpu_available.ToString().ToLowerInvariant())")
    $lines.Add("")
    $lines.Add("| metric | cpu | gpu |")
    $lines.Add("|---|---:|---:|")
    $lines.Add("| wall_avg_ms | $(Format-MetricDouble $cpuWallAvg) | $(Format-MetricDouble $gpuWallAvg) |")
    $lines.Add("| particle_stamp_avg_ms | $(Format-MetricDouble $cpuStampAvg) | 0 |")
    $lines.Add("| gpu_particle_avg_ms | 0 | $(Format-MetricDouble $gpuParticleAvg) |")
    $lines.Add("")
    $lines.Add("该报告只来自本机短窗口 probe，不包含目标 GPU 硬件、驱动、采样时长与人工复核信息；不得作为 comparisonReport evidence，也不会被 `gpuFasterThanCpu` 目标验收规则接受。")
    [System.IO.File]::WriteAllLines($markdownPath, $lines, [System.Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        markdown = ConvertTo-RepositoryRelativePath -Root $Root -Path $markdownPath
        json = ConvertTo-RepositoryRelativePath -Root $Root -Path $jsonPath
        localGpuParticleDrawFaster = $localGpuParticleDrawFaster
        localGpuWallTimeFaster = $localGpuWallTimeFaster
        localParticleDrawSpeedup = Format-MetricDouble $particleDrawSpeedup
        localWallSpeedup = Format-MetricDouble $wallSpeedup
    }
}

function Write-Report {
    param(
        [string]$ReportPath,
        [string]$Status,
        [object[]]$Evidence,
        [object[]]$ProbeSummaries,
        [object]$LocalComparison,
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

    if ($null -ne $LocalComparison) {
        $lines.Add("## 本机短样本对比")
        $lines.Add("")
        $lines.Add(('local_comparison_markdown: `{0}`' -f $LocalComparison.markdown))
        $lines.Add(('local_comparison_json: `{0}`' -f $LocalComparison.json))
        $lines.Add("local_gpu_particle_draw_faster: $($LocalComparison.localGpuParticleDrawFaster.ToString().ToLowerInvariant())")
        $lines.Add("local_gpu_wall_time_faster: $($LocalComparison.localGpuWallTimeFaster.ToString().ToLowerInvariant())")
        $lines.Add("local_particle_draw_speedup: $($LocalComparison.localParticleDrawSpeedup)")
        $lines.Add("local_wall_speedup: $($LocalComparison.localWallSpeedup)")
        $lines.Add("")
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

    $process = Start-Process -FilePath $DotNetPath -ArgumentList $arguments -WorkingDirectory $Root -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $stdout = Get-Content -LiteralPath $stdoutPath -Raw
    $stderr = Get-Content -LiteralPath $stderrPath -Raw
    if ($process.ExitCode -ne 0) {
        throw "$Mode probe 退出码为 $($process.ExitCode)。stdout=$stdoutPath stderr=$stderrPath stderr_tail=$($stderr.Trim())"
    }

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
        metrics = Convert-ParticleProbeSummaryToMetrics -SummaryLine $summaryLine
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
    $resolvedPaths = @{}
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

        $resolvedPaths[$scope] = $path

        $evidence.Add([pscustomobject]@{
            scope = $scope
            path = ConvertTo-RepositoryRelativePath -Root $Root -Path $path
            sha256 = $actualHash
        })
    }

    $hardwareFields = Assert-TargetHardwareReport -Path $resolvedPaths["targetHardwareReport"]
    $cpuMetrics = Assert-TargetProbeReport -Path $resolvedPaths["cpuProbeReport"] -ExpectedMode "cpu"
    $gpuMetrics = Assert-TargetProbeReport -Path $resolvedPaths["gpuProbeReport"] -ExpectedMode "gpu"
    Assert-TargetProbePair -CpuMetrics $cpuMetrics -GpuMetrics $gpuMetrics -HardwareFields $hardwareFields
    Assert-ComparisonReport -Path $resolvedPaths["comparisonReport"] -CpuMetrics $cpuMetrics -GpuMetrics $gpuMetrics

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
$localComparison = $null
$evidence = @()
$status = "blocked_missing_target_gpu_evidence"
$exitCode = 2

if ($RunProbe) {
    try {
        $probeRoot = Join-Path $artifactRoot "local-probe"
        New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
        $cpuProbe = Invoke-ParticleProbe -Mode "cpu" -Root $root -ProbeArtifacts $probeRoot
        $gpuProbe = Invoke-ParticleProbe -Mode "gpu" -Root $root -ProbeArtifacts $probeRoot
        Assert-LocalProbeMetrics -Probe $cpuProbe -ExpectedMode "cpu"
        Assert-LocalProbeMetrics -Probe $gpuProbe -ExpectedMode "gpu"
        $probeSummaries.Add($cpuProbe)
        $probeSummaries.Add($gpuProbe)
        $localComparison = Write-LocalComparison -Root $root -ArtifactRoot $artifactRoot -CpuProbe $cpuProbe -GpuProbe $gpuProbe
        $status = "local_probe_only"
        $notes.Add("本机 probe 只能证明 --particle-frame-probe 与 cpu/gpu 路径可执行，不能替代目标 GPU 硬件长基准。")
        $notes.Add("本机短样本对比已写入 $($localComparison.markdown) 与 $($localComparison.json)，且显式 local_only/target_gpu_evidence=false。")
    }
    catch {
        $status = "blocked_invalid_local_probe"
        $exitCode = 5
        $notes.Add("本机 probe 失败：$($_.Exception.Message)")
    }
}

if (-not [string]::IsNullOrWhiteSpace($EvidenceManifestPath) -and $status -ne "blocked_invalid_local_probe") {
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

Write-Report -ReportPath $reportPath -Status $status -Evidence $evidence -ProbeSummaries @($probeSummaries) -LocalComparison $localComparison -Notes @($notes) -ExitCode $exitCode
Write-Host "GPU particle benchmark preflight status: $status"
Write-Host "Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if ($exitCode -ne 0 -and -not $AllowBlocked) {
    [Console]::Error.WriteLine("GPU particle benchmark preflight failed: $status")
    exit $exitCode
}

exit 0
