[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/native-gpu-smoke/preflight",
    [string]$FixturePath = "",
    [switch]$AllowFixture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError([string]$message) {
    if (-not [string]::IsNullOrWhiteSpace($message)) {
        $errors.Add($message)
    }
}

function Get-NodeValue([object]$node, [string]$name, [object]$defaultValue = $null) {
    if ($null -eq $node) {
        return $defaultValue
    }

    if ($node -is [System.Collections.IDictionary]) {
        if ($node.Contains($name)) {
            return $node[$name]
        }

        return $defaultValue
    }

    $property = $node.PSObject.Properties[$name]
    if ($null -eq $property) {
        return $defaultValue
    }

    return $property.Value
}

function Get-EnvironmentValue([string]$name) {
    return [Environment]::GetEnvironmentVariable($name)
}

function Split-RunnerLabels([string]$labels) {
    if ([string]::IsNullOrWhiteSpace($labels)) {
        return @()
    }

    return @($labels.Split(',', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Assert-BoundedIdentityText([object]$value, [string]$label, [int]$maxLength) {
    $text = [string]$value
    if ($text.Length -gt $maxLength) {
        Add-ValidationError "$label 长度超过上限 $maxLength；actual=$($text.Length)。"
    }

    foreach ($character in $text.ToCharArray()) {
        if ([char]::IsControl($character)) {
            Add-ValidationError "$label 不允许包含控制字符。"
            break
        }
    }
}

function ConvertTo-MarkdownCell([object]$value) {
    $text = [string]$value
    $text = $text.Replace("&", "&amp;", [StringComparison]::Ordinal)
    $text = $text.Replace("|", "&#124;", [StringComparison]::Ordinal)
    $text = $text.Replace("`r", "&#13;", [StringComparison]::Ordinal)
    $text = $text.Replace("`n", "&#10;", [StringComparison]::Ordinal)
    $text = $text.Replace("<", "&lt;", [StringComparison]::Ordinal)
    $text = $text.Replace(">", "&gt;", [StringComparison]::Ordinal)
    $text = $text.Replace("``", "&#96;", [StringComparison]::Ordinal)
    return $text
}

function New-EmptySnapshot {
    return [ordered]@{
        platform = [ordered]@{
            isWindows = $false
            osArchitecture = "unknown"
            processArchitecture = "unknown"
        }
        session = [ordered]@{
            id = 0
            userInteractive = $false
            name = ""
            userName = ""
        }
        cpu = @()
        gpuAdapters = @()
        os = [ordered]@{
            caption = ""
            version = ""
            buildNumber = ""
            architecture = ""
        }
        dotnet = [ordered]@{
            version = ""
            info = ""
            exitCode = -1
        }
        runnerIdentity = [ordered]@{
            name = ""
            os = ""
            arch = ""
            labels = @()
            workflow = ""
            event = ""
            repository = ""
            ref = ""
            dispatchSha = ""
            candidateSha = ""
            checkedOutSha = ""
            runId = ""
            runAttempt = ""
        }
    }
}

function Get-ProductionSnapshot {
    $snapshot = New-EmptySnapshot
    $snapshot["platform"] = [ordered]@{
        isWindows = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)
        osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }

    try {
        $sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
    } catch {
        $sessionId = 0
        Add-ValidationError "无法读取当前进程 SessionId：$($_.Exception.Message)"
    }
    $snapshot["session"] = [ordered]@{
        id = $sessionId
        userInteractive = [Environment]::UserInteractive
        name = Get-EnvironmentValue "SESSIONNAME"
        userName = [Environment]::UserName
    }

    if ([bool]$snapshot["platform"]["isWindows"]) {
        try {
            $snapshot["cpu"] = @(Get-CimInstance Win32_Processor | ForEach-Object {
                [ordered]@{
                    name = [string]$_.Name
                    manufacturer = [string]$_.Manufacturer
                    cores = [int]$_.NumberOfCores
                    logicalProcessors = [int]$_.NumberOfLogicalProcessors
                }
            })
        } catch {
            Add-ValidationError "无法读取 Win32_Processor：$($_.Exception.Message)"
        }

        try {
            $snapshot["gpuAdapters"] = @(Get-CimInstance Win32_VideoController | ForEach-Object {
                [ordered]@{
                    name = [string]$_.Name
                    driverVersion = [string]$_.DriverVersion
                    adapterCompatibility = [string]$_.AdapterCompatibility
                    videoProcessor = [string]$_.VideoProcessor
                    pnpDeviceId = [string]$_.PNPDeviceID
                    status = [string]$_.Status
                }
            })
        } catch {
            Add-ValidationError "无法读取 Win32_VideoController：$($_.Exception.Message)"
        }

        try {
            $os = Get-CimInstance Win32_OperatingSystem | Select-Object -First 1
            $snapshot["os"] = [ordered]@{
                caption = [string]$os.Caption
                version = [string]$os.Version
                buildNumber = [string]$os.BuildNumber
                architecture = [string]$os.OSArchitecture
            }
        } catch {
            Add-ValidationError "无法读取 Win32_OperatingSystem：$($_.Exception.Message)"
        }
    }

    try {
        $versionOutput = @(& dotnet --version 2>&1)
        $versionExitCode = $LASTEXITCODE
        $infoOutput = @(& dotnet --info 2>&1)
        $infoExitCode = $LASTEXITCODE
        $snapshot["dotnet"] = [ordered]@{
            version = (($versionOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
            info = (($infoOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
            exitCode = if ($versionExitCode -ne 0) { $versionExitCode } else { $infoExitCode }
        }
    } catch {
        Add-ValidationError "无法执行 dotnet identity probe：$($_.Exception.Message)"
    }

    $snapshot["runnerIdentity"] = [ordered]@{
        name = Get-EnvironmentValue "RUNNER_NAME"
        os = Get-EnvironmentValue "RUNNER_OS"
        arch = Get-EnvironmentValue "RUNNER_ARCH"
        labels = @(Split-RunnerLabels (Get-EnvironmentValue "PIXELENGINE_EXPECTED_RUNNER_LABELS"))
        workflow = Get-EnvironmentValue "GITHUB_WORKFLOW"
        event = Get-EnvironmentValue "GITHUB_EVENT_NAME"
        repository = Get-EnvironmentValue "GITHUB_REPOSITORY"
        ref = Get-EnvironmentValue "GITHUB_REF"
        dispatchSha = Get-EnvironmentValue "GITHUB_SHA"
        candidateSha = Get-EnvironmentValue "PIXELENGINE_CANDIDATE_SHA"
        checkedOutSha = Get-EnvironmentValue "PIXELENGINE_CHECKED_OUT_SHA"
        runId = Get-EnvironmentValue "GITHUB_RUN_ID"
        runAttempt = Get-EnvironmentValue "GITHUB_RUN_ATTEMPT"
    }

    return $snapshot
}

$fixtureUsed = -not [string]::IsNullOrWhiteSpace($FixturePath)
$snapshot = New-EmptySnapshot

if ($fixtureUsed) {
    if (-not $AllowFixture) {
        Add-ValidationError "fixture 输入仅供跨平台自动测试；必须显式传入 -AllowFixture，生产 workflow 禁止使用。"
    } else {
        try {
            $fixtureFullPath = if ([IO.Path]::IsPathRooted($FixturePath)) {
                [IO.Path]::GetFullPath($FixturePath)
            } else {
                [IO.Path]::GetFullPath((Join-Path $repoRoot $FixturePath))
            }
            if (-not (Test-Path -LiteralPath $fixtureFullPath -PathType Leaf)) {
                throw "fixture 文件不存在：$fixtureFullPath"
            }

            $snapshot = Get-Content -LiteralPath $fixtureFullPath -Raw | ConvertFrom-Json -AsHashtable
            if ((Get-NodeValue $snapshot "schema" "") -cne "pixelengine.native-gpu-runner-fixture/v1") {
                Add-ValidationError "fixture schema 必须为 pixelengine.native-gpu-runner-fixture/v1。"
            }
        } catch {
            Add-ValidationError "无法读取 native GPU runner fixture：$($_.Exception.Message)"
            $snapshot = New-EmptySnapshot
        }
    }
} elseif ($AllowFixture) {
    Add-ValidationError "-AllowFixture 只能与非空 -FixturePath 一起使用。"
} else {
    $snapshot = Get-ProductionSnapshot
}

$platform = Get-NodeValue $snapshot "platform" @{}
$session = Get-NodeValue $snapshot "session" @{}
$cpus = @(Get-NodeValue $snapshot "cpu" @())
$gpuAdapters = @(Get-NodeValue $snapshot "gpuAdapters" @())
$os = Get-NodeValue $snapshot "os" @{}
$dotnet = Get-NodeValue $snapshot "dotnet" @{}
$runner = Get-NodeValue $snapshot "runnerIdentity" @{}

Assert-BoundedIdentityText (Get-NodeValue $session "name" "") "session.name" 128
Assert-BoundedIdentityText (Get-NodeValue $session "userName" "") "session.userName" 256
foreach ($cpu in $cpus) {
    Assert-BoundedIdentityText (Get-NodeValue $cpu "name" "") "cpu.name" 256
    Assert-BoundedIdentityText (Get-NodeValue $cpu "manufacturer" "") "cpu.manufacturer" 128
}
foreach ($gpu in $gpuAdapters) {
    foreach ($field in @("name", "driverVersion", "adapterCompatibility", "videoProcessor", "pnpDeviceId", "status")) {
        Assert-BoundedIdentityText (Get-NodeValue $gpu $field "") "gpuAdapters.$field" 512
    }
}
foreach ($field in @("caption", "version", "buildNumber", "architecture")) {
    Assert-BoundedIdentityText (Get-NodeValue $os $field "") "os.$field" 256
}
Assert-BoundedIdentityText (Get-NodeValue $dotnet "version" "") "dotnet.version" 128
$runnerTextLimits = [ordered]@{
    name = 256
    os = 64
    arch = 64
    workflow = 128
    event = 64
    repository = 256
    ref = 512
    dispatchSha = 40
    candidateSha = 40
    checkedOutSha = 40
    runId = 32
    runAttempt = 16
}
foreach ($field in $runnerTextLimits.Keys) {
    Assert-BoundedIdentityText (Get-NodeValue $runner $field "") "runnerIdentity.$field" ([int]$runnerTextLimits[$field])
}
foreach ($label in @(Get-NodeValue $runner "labels" @())) {
    Assert-BoundedIdentityText $label "runnerIdentity.labels[]" 128
}

if (-not [bool](Get-NodeValue $platform "isWindows" $false)) {
    Add-ValidationError "native GPU runner 必须运行在 Windows。"
}
if (-not [string]::Equals([string](Get-NodeValue $platform "osArchitecture" ""), "X64", [StringComparison]::OrdinalIgnoreCase)) {
    Add-ValidationError "native GPU runner OS architecture 必须为 X64。"
}
if (-not [string]::Equals([string](Get-NodeValue $platform "processArchitecture" ""), "X64", [StringComparison]::OrdinalIgnoreCase)) {
    Add-ValidationError "native GPU runner process architecture 必须为 X64。"
}

$sessionId = [int](Get-NodeValue $session "id" 0)
if ($sessionId -le 0) {
    Add-ValidationError "native GPU runner 必须位于非 Session 0 的交互桌面。"
}
if (-not [bool](Get-NodeValue $session "userInteractive" $false)) {
    Add-ValidationError "native GPU runner 必须提供可交互桌面；Environment.UserInteractive=false。"
}

if ($cpus.Count -eq 0) {
    Add-ValidationError "未发现 CPU identity。"
} else {
    foreach ($cpu in $cpus) {
        if ([string]::IsNullOrWhiteSpace([string](Get-NodeValue $cpu "name" ""))) {
            Add-ValidationError "CPU identity 缺少 name。"
        }
        if ([int](Get-NodeValue $cpu "cores" 0) -le 0 -or [int](Get-NodeValue $cpu "logicalProcessors" 0) -le 0) {
            Add-ValidationError "CPU identity 必须包含正数 cores/logicalProcessors。"
        }
    }
}

$ineligibleGpuPattern = "(?i)(Microsoft\s+Basic|Basic\s+Display|Remote\s+Display|Virtual|VMware|Hyper-V|VirtualBox|Parallels|Citrix)"
$eligibleGpus = [System.Collections.Generic.List[object]]::new()
foreach ($gpu in $gpuAdapters) {
    $gpuName = [string](Get-NodeValue $gpu "name" "")
    if ([string]::IsNullOrWhiteSpace($gpuName) -or $gpuName -match $ineligibleGpuPattern) {
        continue
    }

    $driverVersion = [string](Get-NodeValue $gpu "driverVersion" "")
    if ([string]::IsNullOrWhiteSpace($driverVersion)) {
        Add-ValidationError "真实 GPU '$gpuName' 缺少 driverVersion。"
        continue
    }

    $eligibleGpus.Add($gpu)
}
if ($eligibleGpus.Count -eq 0) {
    Add-ValidationError "未发现真实且带 driver 的 GPU；Basic/Remote/Virtual adapter 不具备验收资格。"
}

foreach ($field in @("caption", "version", "buildNumber", "architecture")) {
    if ([string]::IsNullOrWhiteSpace([string](Get-NodeValue $os $field ""))) {
        Add-ValidationError "Windows OS identity 缺少 $field。"
    }
}

$dotnetVersion = [string](Get-NodeValue $dotnet "version" "")
if ([int](Get-NodeValue $dotnet "exitCode" -1) -ne 0) {
    Add-ValidationError "dotnet identity probe 必须 exit=0。"
}
if ($dotnetVersion -notmatch '^10\.') {
    Add-ValidationError "native GPU runner 必须使用 .NET SDK 10.x；actual='$dotnetVersion'。"
}
if ([string]::IsNullOrWhiteSpace([string](Get-NodeValue $dotnet "info" ""))) {
    Add-ValidationError "dotnet identity 缺少 dotnet --info 输出。"
}

$runnerName = [string](Get-NodeValue $runner "name" "")
if ([string]::IsNullOrWhiteSpace($runnerName)) {
    Add-ValidationError "runner identity 缺少 RUNNER_NAME。"
}
if (-not [string]::Equals([string](Get-NodeValue $runner "os" ""), "Windows", [StringComparison]::OrdinalIgnoreCase)) {
    Add-ValidationError "runner identity RUNNER_OS 必须为 Windows。"
}
if (-not [string]::Equals([string](Get-NodeValue $runner "arch" ""), "X64", [StringComparison]::OrdinalIgnoreCase)) {
    Add-ValidationError "runner identity RUNNER_ARCH 必须为 X64。"
}

$requiredLabels = @("self-hosted", "Windows", "X64", "pixelengine-wgl-angle", "pixelengine-native-smoke")
$actualLabels = @((Get-NodeValue $runner "labels" @()) | ForEach-Object { [string]$_ })
foreach ($requiredLabel in $requiredLabels) {
    if (-not ($actualLabels | Where-Object { [string]::Equals($_, $requiredLabel, [StringComparison]::OrdinalIgnoreCase) })) {
        Add-ValidationError "runner identity 缺少 required label：$requiredLabel。"
    }
}

if (([string](Get-NodeValue $runner "workflow" "")) -cne "Native GPU Smoke") {
    Add-ValidationError "runner workflow identity 必须为 Native GPU Smoke。"
}
if (([string](Get-NodeValue $runner "event" "")) -cne "workflow_dispatch") {
    Add-ValidationError "native GPU runner 只接受 workflow_dispatch。"
}
if ([string]::IsNullOrWhiteSpace([string](Get-NodeValue $runner "repository" ""))) {
    Add-ValidationError "runner identity 缺少 GITHUB_REPOSITORY。"
}
if (([string](Get-NodeValue $runner "runId" "")) -notmatch '^[1-9][0-9]*$') {
    Add-ValidationError "runner identity GITHUB_RUN_ID 必须为正整数。"
}
if (([string](Get-NodeValue $runner "runAttempt" "")) -notmatch '^[1-9][0-9]*$') {
    Add-ValidationError "runner identity GITHUB_RUN_ATTEMPT 必须为正整数。"
}

$candidateSha = [string](Get-NodeValue $runner "candidateSha" "")
$checkedOutSha = [string](Get-NodeValue $runner "checkedOutSha" "")
$dispatchSha = [string](Get-NodeValue $runner "dispatchSha" "")
if ($candidateSha -cnotmatch '^[0-9a-fA-F]{40}$') {
    Add-ValidationError "candidate SHA 必须为 40 位 hexadecimal。"
}
if ($checkedOutSha -cnotmatch '^[0-9a-fA-F]{40}$') {
    Add-ValidationError "checked-out SHA 必须为 40 位 hexadecimal。"
}
if ($dispatchSha -cnotmatch '^[0-9a-fA-F]{40}$') {
    Add-ValidationError "dispatch SHA 必须为 40 位 hexadecimal。"
}
if (-not [string]::Equals($candidateSha, $checkedOutSha, [StringComparison]::OrdinalIgnoreCase)) {
    Add-ValidationError "candidate SHA 与 checked-out SHA 不一致。"
}

$status = if ($errors.Count -eq 0) { "success" } else { "failed" }
$result = [ordered]@{
    schema = "pixelengine.native-gpu-runner-preflight/v1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O", [Globalization.CultureInfo]::InvariantCulture)
    status = $status
    fixtureUsed = $fixtureUsed
    validationErrors = @($errors)
    runnerIdentity = $runner
    platform = $platform
    session = $session
    cpu = @($cpus)
    gpuAdapters = @($gpuAdapters)
    eligibleGpuAdapters = @($eligibleGpus)
    os = $os
    dotnet = $dotnet
}

$jsonPath = Join-Path $outputRoot "native-gpu-runner-preflight.json"
$markdownPath = Join-Path $outputRoot "native-gpu-runner-preflight.md"
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add("# Native GPU runner preflight")
$markdown.Add("")
$markdown.Add("| Key | Value |")
$markdown.Add("|---|---|")
$markdown.Add("| status | $(ConvertTo-MarkdownCell $status) |")
$markdown.Add("| fixture_used | $(ConvertTo-MarkdownCell $fixtureUsed.ToString().ToLowerInvariant()) |")
$markdown.Add("| runner | $(ConvertTo-MarkdownCell $runnerName) |")
$markdown.Add("| run_id | $(ConvertTo-MarkdownCell (Get-NodeValue $runner 'runId' '')) |")
$markdown.Add("| run_attempt | $(ConvertTo-MarkdownCell (Get-NodeValue $runner 'runAttempt' '')) |")
$markdown.Add("| candidate_sha | $(ConvertTo-MarkdownCell $candidateSha) |")
$markdown.Add("| checked_out_sha | $(ConvertTo-MarkdownCell $checkedOutSha) |")
$markdown.Add("| session_id | $(ConvertTo-MarkdownCell $sessionId) |")
$markdown.Add("| user_interactive | $(ConvertTo-MarkdownCell ([bool](Get-NodeValue $session 'userInteractive' $false))) |")
$markdown.Add("| dotnet | $(ConvertTo-MarkdownCell $dotnetVersion) |")
$markdown.Add("| eligible_gpu_count | $($eligibleGpus.Count) |")
$markdown.Add("")
$markdown.Add("## Eligible GPU adapters")
$markdown.Add("")
if ($eligibleGpus.Count -eq 0) {
    $markdown.Add("- none")
} else {
    foreach ($gpu in $eligibleGpus) {
        $gpuNameForMarkdown = ConvertTo-MarkdownCell (Get-NodeValue $gpu 'name' 'unknown')
        $driverForMarkdown = ConvertTo-MarkdownCell (Get-NodeValue $gpu 'driverVersion' 'unknown')
        $markdown.Add("- $gpuNameForMarkdown; driver=$driverForMarkdown")
    }
}
$markdown.Add("")
$markdown.Add("## Validation errors")
$markdown.Add("")
if ($errors.Count -eq 0) {
    $markdown.Add("- none")
} else {
    foreach ($validationError in $errors) {
        $markdown.Add("- $(ConvertTo-MarkdownCell $validationError)")
    }
}
$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Host "Native GPU runner preflight status=$status runner=$runnerName eligibleGpuCount=$($eligibleGpus.Count) evidence=$jsonPath"
if ($errors.Count -gt 0) {
    throw ("Native GPU runner preflight failed:`n" + ($errors -join "`n"))
}
