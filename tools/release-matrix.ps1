param(
    [string]$RidConfigPath = "",
    [switch]$ExcludeWinArm64,
    [switch]$Print
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($RidConfigPath)) {
    $RidConfigPath = Join-Path $repoRoot "tools/release-rids.json"
}

if (-not (Test-Path -LiteralPath $RidConfigPath -PathType Leaf)) {
    throw "找不到 RID 激活配置: $RidConfigPath"
}

$config = Get-Content -Raw -LiteralPath $RidConfigPath | ConvertFrom-Json
$channels = @($config.channels | ForEach-Object { [string]$_ })
if ($channels.Count -eq 0) {
    throw "release-rids.json 必须声明至少一个 channel。"
}

$allRids = @($config.rids)
if ($allRids.Count -eq 0) {
    throw "release-rids.json 必须声明至少一个 RID。"
}

$seenRids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($rid in $allRids) {
    foreach ($field in @("rid", "runner", "shell", "smoke")) {
        if ([string]::IsNullOrWhiteSpace([string]$rid.$field)) {
            throw "release-rids.json 的 RID 条目缺少 $field 字段。"
        }
    }

    if (-not $seenRids.Add([string]$rid.rid)) {
        throw "release-rids.json 包含重复 RID: $($rid.rid)"
    }
}

$activeRids = @(
    $allRids | Where-Object {
        [bool]$_.active -and
        (-not $ExcludeWinArm64 -or -not [string]::Equals([string]$_.rid, "win-arm64", [StringComparison]::Ordinal))
    }
)

if ($activeRids.Count -eq 0) {
    throw "RID 激活集为空，无法生成 release matrix。"
}

$nativeGroups = @(
    $activeRids |
        Group-Object -Property runner, shell |
        ForEach-Object {
            $first = $_.Group[0]
            $ridNames = @($_.Group | ForEach-Object { [string]$_.rid })
            [ordered]@{
                group = (($ridNames[0] -split "-", 2)[0])
                runner = [string]$first.runner
                shell = [string]$first.shell
                rids = ($ridNames -join " ")
            }
        }
)

$buildEntries = [System.Collections.Generic.List[object]]::new()
foreach ($rid in $activeRids) {
    foreach ($channel in $channels) {
        $buildEntries.Add([ordered]@{
            rid = [string]$rid.rid
            channel = [string]$channel
            runner = [string]$rid.runner
            shell = [string]$rid.shell
            smoke = [string]$rid.smoke
            codesign = [bool]$rid.codesign
            allow_load_only = [string]::Equals([string]$rid.smoke, "load-only", [StringComparison]::OrdinalIgnoreCase)
        })
    }
}

$activeRidNames = @($activeRids | ForEach-Object { [string]$_.rid })
$expected = [ordered]@{
    activeRids = $activeRidNames
    channels = $channels
    packageCount = $activeRidNames.Count * $channels.Count
    assetCount = ($activeRidNames.Count * $channels.Count) + 1
}

$result = [ordered]@{
    "native-matrix" = [ordered]@{ include = $nativeGroups }
    "build-matrix" = [ordered]@{ include = @($buildEntries) }
    expected = $expected
}

$nativeJson = $result["native-matrix"] | ConvertTo-Json -Depth 8 -Compress
$buildJson = $result["build-matrix"] | ConvertTo-Json -Depth 8 -Compress
$expectedJson = $result.expected | ConvertTo-Json -Depth 8 -Compress

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "native-matrix=$nativeJson"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "native_matrix=$nativeJson"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "build-matrix=$buildJson"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "build_matrix=$buildJson"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "expected=$expectedJson"
}

if ($Print -or [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    $result | ConvertTo-Json -Depth 8
}
