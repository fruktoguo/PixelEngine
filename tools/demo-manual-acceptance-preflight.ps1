param(
    [string]$Project = "demo/PixelEngine.Demo/PixelEngine.Demo.csproj",
    [string]$Content = "demo/PixelEngine.Demo/content",
    [string]$Artifacts = "artifacts/demo-manual-acceptance-preflight",
    [string]$EvidenceManifestPath = "",
    [switch]$RunScriptedProbes,
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

function Get-ManualScopes {
    @(
        [pscustomobject]@{
            scope = "controlFeelReport"
            title = "角色跑/跳/蹬墙、站在 settled 沙堆与 RigidOwned 刚体 stamp 像素上不穿不陷"
        },
        [pscustomobject]@{
            scope = "materialBrushAndReactionVideo"
            title = "真实鼠标/滚轮/数字键操作材质笔刷，并观察沙堆休止角、水找平、油浮水、气体上升、反应和温度相变视觉质量"
        },
        [pscustomobject]@{
            scope = "rigidBodyGameplayVideo"
            title = "真实窗口推动/被砸、挖断木桥转刚体、继续挖/烧/酸蚀破碎、metal 近熔岩熔化坍塌"
        },
        [pscustomobject]@{
            scope = "particleLightingVideo"
            title = "血/碎屑/发光火花、爆炸推动邻近刚体、bloom/fog/mining lighting 视觉质量和长时间玩法无粒子泄漏"
        },
        [pscustomobject]@{
            scope = "audioListeningReport"
            title = "真实设备听感与空间感：impact、splash、ambient、反应音、爆炸/破碎、玩家/UI/通关音效"
        },
        [pscustomobject]@{
            scope = "fullRoutePlaythroughVideo"
            title = "从出生点用至少一种解法完整抵达出口，贯穿材质/反应/刚体/粒子/光照/音频"
        },
        [pscustomobject]@{
            scope = "hudMenuEditorVideo"
            title = "HUD 像素布局、菜单点击、Editor dockspace 打开、重开、退出请求与叠层切换"
        },
        [pscustomobject]@{
            scope = "hotReloadWindowReport"
            title = "开发态真实窗口修改 Behaviour 后 Roslyn + ALC 热重载，场景与世界状态保留"
        }
    )
}

function Invoke-ScriptedProbe {
    param(
        [string]$Name,
        [int]$Ticks,
        [string]$Scene,
        [string]$Root,
        [string]$OutputRoot
    )

    $directory = Join-Path $OutputRoot $Name
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $stdoutPath = Join-Path $directory "stdout.txt"
    $stderrPath = Join-Path $directory "stderr.txt"
    $logDirectory = Join-Path $directory "logs"

    $arguments = @(
        "run",
        "--project", $Project,
        "-c", "Release",
        "--no-build",
        "--",
        "--no-hot-reload",
        "--window-ticks", $Ticks.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--scripted-window-demo",
        "--content", $Content,
        "--scene", $Scene,
        "--log-dir", $logDirectory
    )

    $process = Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $Root -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $stdout = Get-Content -LiteralPath $stdoutPath -Raw
    $summary = ($stdout -split "`r?`n" | Where-Object { $_.StartsWith("脚本化窗口输入摘要：", [StringComparison]::Ordinal) } | Select-Object -Last 1)
    if ($process.ExitCode -ne 0) {
        throw "scripted window probe $Name 退出码为 $($process.ExitCode)。"
    }

    [pscustomobject]@{
        name = $Name
        scene = $Scene
        ticks = $Ticks
        stdout = ConvertTo-RepositoryRelativePath -Root $Root -Path $stdoutPath
        stderr = ConvertTo-RepositoryRelativePath -Root $Root -Path $stderrPath
        summary = if ([string]::IsNullOrWhiteSpace($summary)) { "<missing>" } else { $summary }
    }
}

function Read-EvidenceManifest {
    param(
        [string]$Root,
        [string]$ManifestPath
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "人工验收 evidence manifest 不存在：$ManifestPath"
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "Demo manual acceptance manifest schemaVersion 必须为 1。"
    }

    $requiredScopes = @(Get-ManualScopes | ForEach-Object { $_.scope })
    $entries = @($manifest.evidence)
    $scopes = @{}
    foreach ($entry in $entries) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.scope)) {
            throw "evidence entry 缺少 scope。"
        }

        if ($scopes.ContainsKey([string]$entry.scope)) {
            throw "重复 evidence scope：$($entry.scope)"
        }

        $scopes[[string]$entry.scope] = $entry
    }

    $missing = @($requiredScopes | Where-Object { -not $scopes.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        return [pscustomobject]@{
            status = "blocked_missing_manual_scope_evidence"
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

        $evidence.Add([pscustomobject]@{
            scope = $scope
            path = ConvertTo-RepositoryRelativePath -Root $Root -Path $path
            sha256 = Get-FileSha256 -Path $path
        })
    }

    [pscustomobject]@{
        status = "manual_evidence_attached_pending_review"
        missing = @()
        evidence = @($evidence)
    }
}

function Write-ManualAcceptanceReport {
    param(
        [string]$Path,
        [string]$Status,
        [int]$ExitCode,
        [object[]]$Evidence,
        [object[]]$Missing,
        [object[]]$ProbeRuns
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Demo 真实窗口人工验收预检")
    $lines.Add("")
    $lines.Add("status: $Status")
    $lines.Add("exit_code: $ExitCode")
    $lines.Add("")
    $lines.Add("## 说明")
    $lines.Add("")
    $lines.Add('该脚本只收集 plan/13 真实窗口人工验收证据。`scripted_probe_only` 与 `manual_evidence_attached_pending_review` 都不是验收通过状态；plan/13 的 `[!]` 只有在人工确认视觉、听感、手感、完整路线和热重载体验后才能改为 `[x]`。')
    $lines.Add("")
    $lines.Add("## 人工验收 scope")
    $lines.Add("")
    foreach ($scope in Get-ManualScopes) {
        $lines.Add("- $($scope.scope): $($scope.title)")
    }
    $lines.Add("")

    if ($ProbeRuns.Count -gt 0) {
        $lines.Add("## 机器 probe")
        $lines.Add("")
        foreach ($run in $ProbeRuns) {
            $lines.Add("### $($run.name)")
            $lines.Add("")
            $lines.Add("scene: $($run.scene)")
            $lines.Add("ticks: $($run.ticks)")
            $lines.Add(('stdout: `{0}`' -f $run.stdout))
            $lines.Add(('stderr: `{0}`' -f $run.stderr))
            $lines.Add("")
            $lines.Add('```text')
            $lines.Add($run.summary)
            $lines.Add('```')
            $lines.Add("")
        }
    }

    if ($Evidence.Count -gt 0) {
        $lines.Add("## 人工证据")
        $lines.Add("")
        foreach ($item in $Evidence) {
            $lines.Add("- scope=$($item.scope); path=$($item.path); sha256=$($item.sha256)")
        }
        $lines.Add("")
    }

    if ($Missing.Count -gt 0) {
        $lines.Add("## 缺失 scope")
        $lines.Add("")
        foreach ($scope in $Missing) {
            $lines.Add("- $scope")
        }
        $lines.Add("")
    }

    [System.IO.File]::WriteAllLines($Path, $lines, [System.Text.UTF8Encoding]::new($false))
}

$root = Resolve-RepositoryRoot
$artifactRoot = if ([System.IO.Path]::IsPathRooted($Artifacts)) { $Artifacts } else { Join-Path $root $Artifacts }
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$reportPath = Join-Path $artifactRoot "demo-manual-acceptance-preflight.md"
$status = "blocked_missing_manual_evidence"
$exitCode = 2
$evidence = @()
$missing = @()
$probeRuns = [System.Collections.Generic.List[object]]::new()

if ($RunScriptedProbes) {
    $probeRoot = Join-Path $artifactRoot "scripted-probes"
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
    $probeRuns.Add((Invoke-ScriptedProbe -Name "main" -Ticks 80 -Scene "scenes/lava-mine.scene" -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "goal" -Ticks 40 -Scene "scenes/lava-mine-goal-probe.scene" -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "audio" -Ticks 30 -Scene "scenes/lava-mine-audio-probe.scene" -Root $root -OutputRoot $probeRoot))
    $probeRuns.Add((Invoke-ScriptedProbe -Name "particle-light" -Ticks 120 -Scene "scenes/lava-mine-particle-light-probe.scene" -Root $root -OutputRoot $probeRoot))
    $status = "scripted_probe_only"
}

if (-not [string]::IsNullOrWhiteSpace($EvidenceManifestPath)) {
    $manifestPath = if ([System.IO.Path]::IsPathRooted($EvidenceManifestPath)) { $EvidenceManifestPath } else { Join-Path $root $EvidenceManifestPath }
    $manifestResult = Read-EvidenceManifest -Root $root -ManifestPath $manifestPath
    $status = $manifestResult.status
    $evidence = @($manifestResult.evidence)
    $missing = @($manifestResult.missing)
    if ($missing.Count -gt 0) {
        $exitCode = 5
    }
    else {
        $exitCode = 2
    }
}

Write-ManualAcceptanceReport -Path $reportPath -Status $status -ExitCode $exitCode -Evidence $evidence -Missing $missing -ProbeRuns @($probeRuns)
Write-Host "Demo manual acceptance preflight status: $status"
Write-Host "Report: $(ConvertTo-RepositoryRelativePath -Root $root -Path $reportPath)"

if ($exitCode -ne 0 -and -not $AllowBlocked) {
    [Console]::Error.WriteLine("Demo manual acceptance preflight failed: $status")
    exit $exitCode
}

exit 0
