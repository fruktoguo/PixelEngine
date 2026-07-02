param(
    [string]$Project = "demo/PixelEngine.Demo/PixelEngine.Demo.csproj",
    [string]$Content = "demo/PixelEngine.Demo/content",
    [string]$Scene = "scenes/empty-window-probe.scene",
    [string]$Artifacts = "artifacts/native-leak-preflight",
    [string]$DetectorName = "",
    [string]$DetectorReportPath = "",
    [int]$WindowTicks = 600,
    [int]$EditorTicks = 120,
    [switch]$RunProcessSmoke,
    [switch]$IncludeEditor,
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

function Write-NativeLeakReport {
    param(
        [string]$Path,
        [string]$Status,
        [string]$Detector,
        [string]$DetectorReport,
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
        $arguments = @(
            "run",
            "--project", $ProjectPath,
            "-c", "Release",
            "--no-restore",
            "--",
            "--editor",
            "--no-hot-reload",
            "--window-ticks", $Ticks.ToString([Globalization.CultureInfo]::InvariantCulture),
            "--content", $ContentRoot,
            "--scene", $ScenePath,
            "--log-dir", $runtimeLog
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

$artifactRoot = Join-Path $root $Artifacts
$reportPath = Join-Path $artifactRoot "native-leak-preflight.md"
$runs = @()

if ($RunProcessSmoke) {
    $runs += Invoke-WindowProbe -Root $root -Mode "window" -ProjectPath $Project -ContentRoot $Content -ScenePath $Scene -OutputDirectory (Join-Path $artifactRoot "window") -Ticks $WindowTicks -Editor $false
    if ($IncludeEditor) {
        $runs += Invoke-WindowProbe -Root $root -Mode "editor" -ProjectPath $Project -ContentRoot $Content -ScenePath $Scene -OutputDirectory (Join-Path $artifactRoot "editor") -Ticks $EditorTicks -Editor $true
    }

    foreach ($run in $runs) {
        if ($run.ExitCode -ne 0) {
            $detail = "Process smoke failed before native leak detector evidence could be evaluated. See stdout/stderr under $Artifacts."
            Write-NativeLeakReport -Path $reportPath -Status "process_smoke_failed" -Detector $DetectorName -DetectorReport $DetectorReportPath -Runs $runs -Detail $detail
            exit 3
        }
    }
}

$hasDetectorReport = -not [string]::IsNullOrWhiteSpace($DetectorReportPath) -and (Test-Path $DetectorReportPath)
if (-not $hasDetectorReport) {
    $status = $RunProcessSmoke ? "process_smoke_only" : "blocked_missing_detector"
    $detail = "Native leak preflight failed: 需要专用 detector 报告覆盖 GL、OpenAL、Box2D 与 ALC 释放路径。进程退出码和 PeakWorkingSetMB 只能作为 smoke 证据，不能替代 GPU driver/OpenAL/Box2D 工具级泄漏审计。"
    Write-NativeLeakReport -Path $reportPath -Status $status -Detector $DetectorName -DetectorReport $DetectorReportPath -Runs $runs -Detail $detail
    Write-Host "Native leak preflight $status. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 2
}

$hash = Get-FileHash -Algorithm SHA256 -Path $DetectorReportPath
$relativeDetectorReport = ConvertTo-RelativePath -Root $root -Path $DetectorReportPath
$detail = "External detector report attached. detector=$DetectorName report=$relativeDetectorReport sha256=$($hash.Hash). Human review must confirm the report covers GL, OpenAL, Box2D and ALC with no leaks before plan/18 can be unblocked."
Write-NativeLeakReport -Path $reportPath -Status "detector_report_attached" -Detector $DetectorName -DetectorReport $relativeDetectorReport -Runs $runs -Detail $detail
Write-Host "Native leak preflight detector_report_attached. Report: $reportPath"
