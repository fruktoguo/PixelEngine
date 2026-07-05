param(
    [int]$WindowTicks = 1500,
    [string]$Configuration = "Release",
    [string]$ArtifactsDir = "artifacts/demo-playthrough",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root $ArtifactsDir
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$capture = Join-Path $outDir "lava-mine-playthrough.bmp"
$log = Join-Path $outDir "lava-mine-playthrough.log"

$args = @(
    "run",
    "--project", "demo/PixelEngine.Demo",
    "-c", $Configuration,
    "--no-build",
    "--",
    "--no-hot-reload",
    "--no-vsync",
    "--scene", "scenes/lava-mine.scene",
    "--window-ticks", $WindowTicks,
    "--scripted-window-route",
    "--capture-frame", $capture
)

Push-Location $root
try {
    if (-not $SkipBuild) {
        & dotnet build PixelEngine.sln -c $Configuration --no-restore -m:1
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }
    }

    $output = & dotnet @args 2>&1
    $output | Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) {
        throw "Demo playthrough process failed with exit code $LASTEXITCODE"
    }

    $summary = ($output | Select-String -Pattern "脚本化窗口输入摘要：" | Select-Object -Last 1).Line
    if ([string]::IsNullOrWhiteSpace($summary)) {
        throw "Missing scripted window summary in $log"
    }

    foreach ($required in @("goal_reached=True", "weapon_last_kind=Laser", "max_physics_created=", "player_air_control=True")) {
        if (-not $summary.Contains($required)) {
            throw "Playthrough summary did not contain '$required': $summary"
        }
    }

    Write-Host "Demo playthrough passed. Capture: $capture"
}
finally {
    Pop-Location
}
