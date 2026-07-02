param(
    [string]$Project = "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj",
    [string]$Artifacts = "artifacts/hardware-counters",
    [string]$Filter = "*ReactionLookupBenchmark.FindDirect*",
    [switch]$RunBenchmark,
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

function Test-IsWindowsAdministrator {
    if (-not $IsWindows) {
        return $false
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-PreflightReport {
    param(
        [string]$Path,
        [string]$Status,
        [bool]$IsWindowsHost,
        [bool]$IsAdministrator,
        [bool]$BenchmarkRequested,
        [string]$CommandLine,
        [string]$Detail
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $lines = @(
        "# PixelEngine hardware counter preflight",
        "",
        "| Key | Value |",
        "|---|---|",
        "| status | $Status |",
        "| is_windows | $IsWindowsHost |",
        "| is_administrator | $IsAdministrator |",
        "| benchmark_requested | $BenchmarkRequested |",
        "| required_counters | Cache Misses; Branch Mispredictions |",
        "",
        "## Command",
        "",
        "````pwsh",
        $CommandLine,
        "````",
        "",
        "## Detail",
        "",
        $Detail
    )

    Set-Content -Path $Path -Value $lines -Encoding UTF8
}

$root = Resolve-RepositoryRoot
Set-Location $root

$isWindowsHost = $IsWindows
$isAdministrator = Test-IsWindowsAdministrator
$artifactRoot = Join-Path $root $Artifacts
$reportPath = Join-Path $artifactRoot "hardware-counter-preflight.md"
$projectPath = Join-Path $root $Project

if (-not (Test-Path $projectPath)) {
    throw "Benchmark project 不存在：$Project"
}

$benchmarkArgs = @(
    "run",
    "--project", $Project,
    "-c", "Release",
    "--no-build",
    "--",
    "--filter", $Filter,
    "--artifacts", $Artifacts,
    "--job", "short",
    "--warmupCount", "1",
    "--iterationCount", "1",
    "--exporters", "markdown"
)
$commandLine = '$env:PIXELENGINE_BENCH_HARDWARE_COUNTERS="1"; dotnet ' + ($benchmarkArgs -join " ")

if (-not $isWindowsHost) {
    $detail = "Hardware counter preflight failed: 当前脚本验证的是 BenchmarkDotNet Windows ETW 硬件计数器路径。非 Windows runner 不作为 Cache Misses / Branch Mispredictions 验收环境。"
    Write-PreflightReport -Path $reportPath -Status "blocked_non_windows" -IsWindowsHost $isWindowsHost -IsAdministrator $isAdministrator -BenchmarkRequested $RunBenchmark.IsPresent -CommandLine $commandLine -Detail $detail
    Write-Host "Hardware counter preflight blocked_non_windows. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 4
}

if ($isWindowsHost -and -not $isAdministrator) {
    $detail = "Hardware counter preflight failed: BenchmarkDotNet 在 Windows 上采集 HardwareCounter.CacheMisses 与 HardwareCounter.BranchMispredictions 需要 elevated ETW Kernel Session。当前 PowerShell 不是管理员会话，因此不会运行 benchmark；请在管理员 PowerShell 或专用 runner 中执行上方命令。"
    Write-PreflightReport -Path $reportPath -Status "blocked_non_admin" -IsWindowsHost $isWindowsHost -IsAdministrator $isAdministrator -BenchmarkRequested $RunBenchmark.IsPresent -CommandLine $commandLine -Detail $detail
    Write-Host "Hardware counter preflight blocked_non_admin. Report: $reportPath"

    if ($AllowBlocked) {
        exit 0
    }

    exit 2
}

if (-not $RunBenchmark) {
    $detail = "当前会话通过权限预检。重新运行并追加 -RunBenchmark 可执行 BenchmarkDotNet；在强制硬件计数器的 runner 中追加 -RequireCounters。"
    Write-PreflightReport -Path $reportPath -Status "ready" -IsWindowsHost $isWindowsHost -IsAdministrator $isAdministrator -BenchmarkRequested $false -CommandLine $commandLine -Detail $detail
    Write-Host "Hardware counter preflight ready. Report: $reportPath"
    exit 0
}

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$previousValue = $env:PIXELENGINE_BENCH_HARDWARE_COUNTERS
$env:PIXELENGINE_BENCH_HARDWARE_COUNTERS = "1"
try {
    & dotnet @benchmarkArgs
    if ($LASTEXITCODE -ne 0) {
        throw "BenchmarkDotNet 硬件计数器运行失败，exit code: $LASTEXITCODE"
    }
}
finally {
    $env:PIXELENGINE_BENCH_HARDWARE_COUNTERS = $previousValue
}

$reports = Get-ChildItem -Path $artifactRoot -Recurse -Filter "*report-github.md" | Sort-Object LastWriteTimeUtc -Descending
$matchingReport = $null
foreach ($report in $reports) {
    $content = Get-Content -Raw -Path $report.FullName
    if ($content.Contains("Cache Misses") -and $content.Contains("Branch Mispredictions")) {
        $matchingReport = $report
        break
    }
}

if ($null -eq $matchingReport) {
    $detail = "Hardware counter preflight failed: Benchmark 已运行，但 markdown 报告中没有同时出现 Cache Misses 与 Branch Mispredictions 列。请确认 runner 支持硬件计数器、BenchmarkDotNet 没有降级跳过 diagnoser。"
    Write-PreflightReport -Path $reportPath -Status "missing_counter_columns" -IsWindowsHost $isWindowsHost -IsAdministrator $isAdministrator -BenchmarkRequested $true -CommandLine $commandLine -Detail $detail

    if ($AllowBlocked) {
        exit 0
    }

    exit 3
}

$detail = "BenchmarkDotNet 报告已包含 Cache Misses 与 Branch Mispredictions 列：$($matchingReport.FullName)"
Write-PreflightReport -Path $reportPath -Status "counters_present" -IsWindowsHost $isWindowsHost -IsAdministrator $isAdministrator -BenchmarkRequested $true -CommandLine $commandLine -Detail $detail
Write-Host "Hardware counter preflight counters_present. Report: $reportPath"
