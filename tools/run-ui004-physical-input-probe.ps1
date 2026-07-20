param(
  [string]$EditorPath = 'apps/PixelEngine.Editor.Shell/bin/Release/net10.0/PixelEngine.exe',

  [string]$DemoPath = 'demo/PixelEngine.Demo/bin/Release/net10.0/PixelEngine.Demo.exe',

  [string]$CliPath = 'tools/PixelEngine.Editor.Cli/bin/Release/net10.0/pixelengine-editor.exe',

  [string]$InputHelperPath = 'tools/PixelEngine.Tools.PhysicalInput/bin/Release/net10.0/pixelengine-physical-input.exe',

  [string]$ProjectRoot = 'demo/PixelEngine.Demo',

  [string]$ContentRoot = '',

  [string]$OutputRoot = 'artifacts/ui004-physical-input-probe',

  [ValidateRange(600, 10000)]
  [int]$PlayerWindowTicks = 1200,

  [ValidateRange(30, 300)]
  [int]$ProcessTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
  throw 'UI-004 物理输入 probe 只支持具有交互桌面的 Windows。'
}

$utf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Resolve-RepoPath([string]$Path, [string]$Label) {
  if ([string]::IsNullOrWhiteSpace($Path)) {
    throw "$Label 不能为空。"
  }

  $full = if ([IO.Path]::IsPathRooted($Path)) {
    [IO.Path]::GetFullPath($Path)
  } else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
  }

  if (-not (Test-Path -LiteralPath $full)) {
    throw "$Label 不存在：$full"
  }

  return $full
}

$editorPathFull = Resolve-RepoPath $EditorPath 'Editor executable'
$demoPathFull = Resolve-RepoPath $DemoPath 'Demo executable'
$cliPathFull = Resolve-RepoPath $CliPath 'pixelengine-editor CLI'
$inputHelperPathFull = Resolve-RepoPath $InputHelperPath 'physical input helper'
$projectRootFull = Resolve-RepoPath $ProjectRoot 'Demo project'
$contentRootCandidate = if ([string]::IsNullOrWhiteSpace($ContentRoot)) {
  Join-Path $projectRootFull 'content'
} else {
  $ContentRoot
}
$contentRootFull = Resolve-RepoPath $contentRootCandidate 'Demo content'
$gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $gitCommit -cnotmatch '^[a-f0-9]{40}$') {
  throw '无法读取当前 Git commit。'
}
$outputRootFull = if ([IO.Path]::IsPathRooted($OutputRoot)) {
  [IO.Path]::GetFullPath($OutputRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}
New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null

function Assert-True([bool]$Condition, [string]$Message) {
  if (-not $Condition) {
    throw $Message
  }
}

function Invoke-PhysicalClick([IntPtr]$WindowHandle, [double]$NormalizedX, [double]$NormalizedY) {
  $x = $NormalizedX.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
  $y = $NormalizedY.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
  $output = & $inputHelperPathFull `
    click `
    --hwnd $($WindowHandle.ToInt64().ToString([Globalization.CultureInfo]::InvariantCulture)) `
    --normalized-x $x `
    --normalized-y $y 2>&1
  $exitCode = $LASTEXITCODE
  $text = ($output -join "`n")
  if ($exitCode -ne 0) {
    throw "physical input helper 失败，exit=$exitCode：$text"
  }

  return $text | ConvertFrom-Json
}

function Start-CapturedProcess(
  [string]$FilePath,
  [string[]]$Arguments,
  [string]$WorkingDirectory,
  [string]$StdoutPath,
  [string]$StderrPath
) {
  $psi = [Diagnostics.ProcessStartInfo]::new()
  $psi.FileName = $FilePath
  $psi.WorkingDirectory = $WorkingDirectory
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.StandardOutputEncoding = $utf8NoBom
  $psi.StandardErrorEncoding = $utf8NoBom
  foreach ($argument in $Arguments) {
    $psi.ArgumentList.Add($argument)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $psi
  Assert-True $process.Start() "进程启动失败：$FilePath"
  return [pscustomobject]@{
    Process = $process
    StdoutTask = $process.StandardOutput.ReadToEndAsync()
    StderrTask = $process.StandardError.ReadToEndAsync()
    StdoutPath = $StdoutPath
    StderrPath = $StderrPath
  }
}

function Wait-ForWindow([Diagnostics.Process]$Process, [int]$TimeoutSeconds = 20) {
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
  while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $Process.Refresh()
    if ($Process.HasExited) {
      throw "进程在创建窗口前退出：pid=$($Process.Id), exit=$($Process.ExitCode)"
    }

    if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
      return $Process.MainWindowHandle
    }

    Start-Sleep -Milliseconds 100
  }

  throw "等待窗口超时：pid=$($Process.Id)"
}

function Wait-ForPhysicalUiReady(
  [string]$ReadyFile,
  [Diagnostics.Process]$Process,
  [int]$TimeoutSeconds = 20
) {
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
  while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $Process.Refresh()
    if ($Process.HasExited) {
      throw "进程在发布 physical UI ready 前退出：pid=$($Process.Id), exit=$($Process.ExitCode)"
    }

    if (Test-Path -LiteralPath $ReadyFile -PathType Leaf) {
      try {
        $content = [IO.File]::ReadAllText($ReadyFile, $utf8NoBom)
        if ($content -match '^pixelengine\.physical-ui-ready/v1;hwnd=(?<hwnd>[1-9][0-9]*)$') {
          return [IntPtr][long]::Parse(
            $Matches['hwnd'],
            [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture)
        }
      } catch [IO.IOException] {
        # 生产进程可能刚创建文件但尚未关闭句柄；继续等完整握手内容。
      }
    }

    Start-Sleep -Milliseconds 50
  }

  throw "等待 physical UI ready 超时：$ReadyFile"
}

function Complete-CapturedProcess([object]$Run, [int]$TimeoutSeconds) {
  $process = [Diagnostics.Process]$Run.Process
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try { $process.Kill($true) } catch { $process.Kill() }
    throw "进程超时：pid=$($process.Id), timeout=${TimeoutSeconds}s"
  }

  $process.WaitForExit()
  $stdout = $Run.StdoutTask.GetAwaiter().GetResult()
  $stderr = $Run.StderrTask.GetAwaiter().GetResult()
  [IO.File]::WriteAllText($Run.StdoutPath, $stdout, $utf8NoBom)
  [IO.File]::WriteAllText($Run.StderrPath, $stderr, $utf8NoBom)
  if ($process.ExitCode -ne 0) {
    throw "进程失败：pid=$($process.Id), exit=$($process.ExitCode), stderr=$stderr"
  }

  return $stdout
}

function Get-SummaryMap([string]$Text, [string]$Prefix) {
  $line = $Text -split "`r?`n" | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) } | Select-Object -Last 1
  if (-not $line) {
    throw "缺少摘要：$Prefix"
  }

  $map = [ordered]@{}
  foreach ($segment in $line.Substring($Prefix.Length).Split(',')) {
    $part = $segment.Trim()
    $separator = $part.IndexOf('=')
    if ($separator -gt 0) {
      $map[$part.Substring(0, $separator).Trim()] = $part.Substring($separator + 1).Trim()
    }
  }
  return $map
}

function Assert-MapValue([Collections.IDictionary]$Map, [string]$Key, [string]$Expected, [string]$Label) {
  Assert-True $Map.Contains($Key) "$Label 缺少 $Key。"
  Assert-True ([string]::Equals([string]$Map[$Key], $Expected, [StringComparison]::OrdinalIgnoreCase)) `
    "$Label 的 $Key 不匹配：expected=$Expected, actual=$($Map[$Key])"
}

function Invoke-PlayerScenario(
  [string]$Name,
  [string]$ContentRoot,
  [double]$NormalizedX,
  [double]$NormalizedY,
  [string]$ExpectedBackend,
  [int]$ExpectedMain,
  [bool]$ExpectHud,
  [bool]$ExpectModal
) {
  $scenarioRoot = Join-Path $outputRootFull $Name
  New-Item -ItemType Directory -Path $scenarioRoot -Force | Out-Null
  $capturePath = Join-Path $scenarioRoot 'after-click.bmp'
  $readyFile = Join-Path $scenarioRoot ("ready-" + [Guid]::NewGuid().ToString('N') + '.flag')
  $run = Start-CapturedProcess `
    $demoPathFull `
    @(
      '--content', $ContentRoot,
      '--window-ticks', [string]$PlayerWindowTicks,
      '--no-hot-reload',
      '--physical-ui-input-probe',
      '--physical-ui-input-ready-file', $readyFile,
      '--capture-frame', $capturePath
    ) `
    (Split-Path -Parent $demoPathFull) `
    (Join-Path $scenarioRoot 'stdout.log') `
    (Join-Path $scenarioRoot 'stderr.log')

  try {
    $handle = Wait-ForPhysicalUiReady $readyFile $run.Process
    $click = Invoke-PhysicalClick $handle $NormalizedX $NormalizedY
    Write-JsonFile (Join-Path $scenarioRoot 'click.json') $click
    Assert-True $click.Foreground "$Name 未取得前台窗口。"
    Assert-True ($click.TargetInputs -eq 2) "$Name 未提交完整目标点击。"
    Assert-True ($click.SentInputs -in @(2, 4)) "$Name 的物理输入数量异常。"
    $stdout = Complete-CapturedProcess $run $ProcessTimeoutSeconds
  }
  catch {
    if (-not $run.Process.HasExited) {
      try { $run.Process.Kill($true) } catch { $run.Process.Kill() }
    }
    throw
  }

  $backend = Get-SummaryMap $stdout 'game_ui_probe '
  $input = Get-SummaryMap $stdout 'physical_ui_input_probe '
  Assert-MapValue $backend 'active' $ExpectedBackend $Name
  Assert-MapValue $input 'raw_press_edges' '1' $Name
  Assert-MapValue $input 'raw_release_edges' '1' $Name
  Assert-MapValue $input 'pointer_pending' '0' $Name
  Assert-MapValue $input 'pointer_coalesced' '0' $Name
  Assert-MapValue $input 'button_calls' '2' $Name
  Assert-MapValue $input 'button_forwarded' '2' $Name
  Assert-MapValue $input 'drained_events' '1' $Name
  Assert-MapValue $input 'controller_faulted' 'False' $Name
  Assert-MapValue $input 'main_screen' ([string]$ExpectedMain) $Name
  if ($ExpectHud) {
    Assert-True ([int]$input['hud_screen'] -gt 0) "$Name 未显示 HUD。"
  } else {
    Assert-MapValue $input 'hud_screen' '0' $Name
  }
  if ($ExpectModal) {
    Assert-True ([int]$input['modal_screen'] -gt 0) "$Name 未打开 modal。"
  } else {
    Assert-MapValue $input 'modal_screen' '0' $Name
  }
  Assert-True (Test-Path -LiteralPath $capturePath -PathType Leaf) "$Name 缺少 framebuffer 截图。"
  $capture = Get-Item -LiteralPath $capturePath
  Assert-True ($capture.Length -gt 1024) "$Name framebuffer 截图为空。"

  return [ordered]@{
    name = $Name
    backend = $ExpectedBackend
    clientWidth = $click.ClientWidth
    clientHeight = $click.ClientHeight
    clientX = $click.ClientX
    clientY = $click.ClientY
    clickEvidence = "$Name/click.json"
    capture = "$Name/after-click.bmp"
    captureSha256 = (Get-FileHash -LiteralPath $capture.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    input = $input
  }
}

function Write-JsonFile([string]$Path, [object]$Value) {
  [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 30), $utf8NoBom)
}

function Invoke-EditorCli([string]$DiscoveryRoot, [string[]]$Arguments) {
  $output = & $cliPathFull --discovery-root $DiscoveryRoot --scopes 'editor.read,editor.control' --output json @Arguments 2>&1
  $exitCode = $LASTEXITCODE
  $text = ($output -join "`n")
  if ($exitCode -ne 0) {
    throw "pixelengine-editor 失败，exit=$exitCode：$text"
  }
  return $text | ConvertFrom-Json
}

function Get-EditorControllerFields([string]$DiscoveryRoot, [string]$PayloadFile) {
  $runtime = Invoke-EditorCli $DiscoveryRoot @('call', 'runtime.entities.list', '--payload-file', $PayloadFile)
  foreach ($entity in $runtime.payload.items) {
    foreach ($component in $entity.components) {
      if ([string]::Equals(
          [string]$component.typeName,
          'PixelEngine.Demo.GameUiDemoController',
          [StringComparison]::Ordinal)) {
        $fields = @{}
        foreach ($field in $component.fields) {
          $fields[[string]$field.name] = [string]$field.value
        }
        return $fields
      }
    }
  }

  return $null
}

function Wait-ForEditorUiReady(
  [string]$DiscoveryRoot,
  [string]$PayloadFile,
  [int]$TimeoutSeconds = 20
) {
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
  $lastFailure = ''
  while ([DateTimeOffset]::UtcNow -lt $deadline) {
    try {
      $fields = Get-EditorControllerFields $DiscoveryRoot $PayloadFile
      if ($null -ne $fields -and
          $fields.ContainsKey('MainScreen') -and
          $fields['MainScreen'].Contains('Value = 1', [StringComparison]::Ordinal)) {
        return $fields
      }
    } catch {
      $lastFailure = $_.Exception.Message
    }

    Start-Sleep -Milliseconds 100
  }

  throw "Editor Game UI ready 超时：$lastFailure"
}

function Wait-ForEditorCapturedFrames(
  [string]$DiscoveryRoot,
  [int]$TimeoutSeconds = 20
) {
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
  $verifiedCaptures = 0
  $lastFailure = ''
  while ([DateTimeOffset]::UtcNow -lt $deadline) {
    try {
      $capture = Invoke-EditorCli $DiscoveryRoot @('call', 'game.capture', '--verify-artifact')
      if ([bool]$capture.verification.verified -and
          [long]$capture.artifact.byteLength -gt 1024) {
        $verifiedCaptures++
        if ($verifiedCaptures -eq 2) {
          return $capture
        }
      }
    } catch {
      $lastFailure = $_.Exception.Message
    }

    Start-Sleep -Milliseconds 100
  }

  throw "Editor 未完成两次可验证 Game framebuffer 捕获：count=$verifiedCaptures, failure=$lastFailure"
}

function Invoke-EditorWrite(
  [string]$DiscoveryRoot,
  [string]$Method,
  [string]$IdempotencyKeyPrefix,
  [string]$PayloadFile = ''
) {
  for ($attempt = 1; $attempt -le 20; $attempt++) {
    $workspace = Invoke-EditorCli $DiscoveryRoot @('call', 'workspace.get')
    $revision = [long]$workspace.revision.globalRevision
    $arguments = [Collections.Generic.List[string]]::new()
    $arguments.Add('call')
    $arguments.Add($Method)
    $arguments.Add('--expected-global')
    $arguments.Add([string]$revision)
    $arguments.Add('--idempotency-key')
    $arguments.Add("$IdempotencyKeyPrefix-$attempt")
    if (-not [string]::IsNullOrWhiteSpace($PayloadFile)) {
      $arguments.Add('--payload-file')
      $arguments.Add($PayloadFile)
    }

    try {
      return Invoke-EditorCli $DiscoveryRoot $arguments.ToArray()
    }
    catch {
      if (-not $_.Exception.Message.Contains('revision_conflict', [StringComparison]::Ordinal)) {
        throw
      }

      Start-Sleep -Milliseconds 150
    }
  }

  throw "Editor write 在 20 次 revision 重试后仍冲突：$Method"
}

function Stop-EditorGracefully([string]$DiscoveryRoot, [Diagnostics.Process]$Process, [string]$RunId) {
  if ($Process.HasExited) {
    return
  }

  try {
    $workspace = Invoke-EditorCli $DiscoveryRoot @('call', 'workspace.get')
    if ([string]$workspace.payload.mode -in @('Play', 'Paused')) {
      [void](Invoke-EditorWrite $DiscoveryRoot 'play.stop' "ui004-$RunId-stop")
    }

    [void](Invoke-EditorWrite $DiscoveryRoot 'workspace.exit' "ui004-$RunId-exit")
  }
  catch {
    Write-Warning "Editor 公开 API 清理失败：$($_.Exception.Message)"
  }
}

function Invoke-EditorScenario {
  $name = 'editor-rmlui-settings'
  $scenarioRoot = Join-Path $outputRootFull $name
  New-Item -ItemType Directory -Path $scenarioRoot -Force | Out-Null
  $runId = [Guid]::NewGuid().ToString('N')
  $privateRoot = Join-Path ([IO.Path]::GetTempPath()) "PixelEngine/ui004-physical-$runId"
  $discoveryRoot = Join-Path $privateRoot 'discovery'
  $artifactRoot = Join-Path $privateRoot 'artifacts'
  New-Item -ItemType Directory -Path $discoveryRoot -Force | Out-Null
  $run = Start-CapturedProcess `
    $editorPathFull `
    @(
      '--project', $projectRootFull,
      '--window-ticks', '10000000',
      '--automation-discovery-root', $discoveryRoot,
      '--automation-artifact-root', $artifactRoot,
      '--log-directory', (Join-Path $scenarioRoot 'logs'),
      '--ephemeral-user-state',
      '--no-reopen-last-project',
      '--physical-ui-input-probe') `
    $repoRoot `
    (Join-Path $scenarioRoot 'stdout.log') `
    (Join-Path $scenarioRoot 'stderr.log')

  try {
    $handle = Wait-ForWindow $run.Process
    $discoveryDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while ([DateTimeOffset]::UtcNow -lt $discoveryDeadline) {
      if (@(Get-ChildItem -LiteralPath (Join-Path $discoveryRoot 'instances') -Filter '*.json' -ErrorAction SilentlyContinue).Count -eq 1) {
        break
      }
      Start-Sleep -Milliseconds 100
    }
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $discoveryRoot 'instances') -Filter '*.json' -ErrorAction SilentlyContinue).Count -eq 1) `
      'Editor 未发布唯一 automation descriptor。'

    $panelPayload = Join-Path $scenarioRoot 'panel-set.json'
    Write-JsonFile $panelPayload ([ordered]@{ schemaVersion = 1; panelId = 'editor.panel.game'; visible = $true; focus = $true })
    [void](Invoke-EditorWrite $discoveryRoot 'window.panel.set' "ui004-$runId-panel" $panelPayload)

    $presentationPayload = Join-Path $scenarioRoot 'presentation-set.json'
    Write-JsonFile $presentationPayload ([ordered]@{
      schemaVersion = 1
      selectedPresetId = 'player-default'
      scalePercent = 0
      panX = 0
      panY = 0
      maximizeOnPlay = $false
      maximized = $true
      customPresets = @()
    })
    [void](Invoke-EditorWrite $discoveryRoot 'game.presentation.set' "ui004-$runId-max" $presentationPayload)

    $playPayload = Join-Path $scenarioRoot 'play-enter.json'
    Write-JsonFile $playPayload ([ordered]@{ schemaVersion = 1; source = 'TemporarySnapshot' })
    $play = Invoke-EditorWrite $discoveryRoot 'play.enter' "ui004-$runId-play" $playPayload
    Assert-True ([bool]$play.payload.succeeded) 'Editor 未进入 Play。'

    $pagePayload = Join-Path $scenarioRoot 'runtime-list.json'
    Write-JsonFile $pagePayload ([ordered]@{ schemaVersion = 1; sort = @(); pageSize = 200 })
    [void](Wait-ForEditorUiReady $discoveryRoot $pagePayload)
    [void](Wait-ForEditorCapturedFrames $discoveryRoot)
    $handle = Wait-ForWindow $run.Process
    $click = Invoke-PhysicalClick $handle (687.0 / 1280.0) (456.0 / 720.0)
    Write-JsonFile (Join-Path $scenarioRoot 'click.json') $click
    Assert-True $click.Foreground 'Editor 未取得前台窗口。'
    Assert-True ($click.TargetInputs -eq 2) 'Editor 未提交完整目标点击。'
    Assert-True ($click.SentInputs -in @(2, 4)) 'Editor 的物理输入数量异常。'
    Start-Sleep -Seconds 1

    $fields = Get-EditorControllerFields $discoveryRoot $pagePayload
    Assert-True ($null -ne $fields) 'Editor runtime 缺少 GameUiDemoController。'
    Assert-True $fields['ModalScreen'].Contains('Value = 2', [StringComparison]::Ordinal) `
      "Editor 物理点击未打开设置 modal：$($fields['ModalScreen'])"
    Assert-True $fields['LastAction'].Contains('534032007', [StringComparison]::Ordinal) `
      "Editor 物理点击 action 不匹配：$($fields['LastAction'])"

    $capture = Invoke-EditorCli $discoveryRoot @('call', 'game.capture', '--verify-artifact')
    Assert-True ([bool]$capture.verification.verified) 'Editor game.capture 未通过长度/SHA256 双重校验。'
    $captureDestination = Join-Path $scenarioRoot 'after-settings.bmp'
    Copy-Item -LiteralPath ([string]$capture.artifact.path) -Destination $captureDestination -Force
    Assert-True ((Get-FileHash -LiteralPath $captureDestination -Algorithm SHA256).Hash.ToLowerInvariant() -eq [string]$capture.artifact.sha256) `
      'Editor 持久证据截图 SHA256 与 automation artifact 不一致。'

    Stop-EditorGracefully $discoveryRoot $run.Process $runId
    $stdout = Complete-CapturedProcess $run $ProcessTimeoutSeconds
  }
  catch {
    Stop-EditorGracefully $discoveryRoot $run.Process $runId
    if (-not $run.Process.HasExited) {
      try { $run.Process.Kill($true) } catch { $run.Process.Kill() }
    }
    throw
  }

  $input = Get-SummaryMap $stdout 'editor_physical_ui_input_probe '
  Assert-MapValue $input 'raw_press_edges' '1' $name
  Assert-MapValue $input 'raw_release_edges' '1' $name
  Assert-MapValue $input 'forwarded_press_edges' '1' $name
  Assert-MapValue $input 'forwarded_release_edges' '1' $name
  Assert-MapValue $input 'button_backend' 'RmlUi' $name
  Assert-MapValue $input 'button_calls' '2' $name
  Assert-MapValue $input 'drained_events' '1' $name

  return [ordered]@{
    name = $name
    backend = 'RmlUi'
    clientWidth = $click.ClientWidth
    clientHeight = $click.ClientHeight
    clientX = $click.ClientX
    clientY = $click.ClientY
    clickEvidence = "$name/click.json"
    capture = "$name/after-settings.bmp"
    captureSha256 = (Get-FileHash -LiteralPath $captureDestination -Algorithm SHA256).Hash.ToLowerInvariant()
    input = $input
  }
}

$sourceContent = $contentRootFull
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
  [IO.Path]::DirectorySeparatorChar,
  [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$fallbackRoot = Join-Path ([IO.Path]::GetTempPath()) ("PixelEngine/ui004-fallback-" + [Guid]::NewGuid().ToString('N'))
$fallbackRoot = [IO.Path]::GetFullPath($fallbackRoot)
Assert-True $fallbackRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) `
  "fallback content 临时根越界：$fallbackRoot"
$fallbackContent = Join-Path $fallbackRoot 'content'

try {
  New-Item -ItemType Directory -Path $fallbackRoot -Force | Out-Null
  Copy-Item -LiteralPath $sourceContent -Destination $fallbackContent -Recurse -Force
  $fallbackStartupPath = Join-Path $fallbackContent 'startup.json'
  $fallbackStartup = Get-Content -Raw -LiteralPath $fallbackStartupPath | ConvertFrom-Json
  $fallbackStartup.runtimeUiBackend = 'ManagedFallback'
  [IO.File]::WriteAllText($fallbackStartupPath, ($fallbackStartup | ConvertTo-Json -Depth 10), $utf8NoBom)

  $results = [Collections.Generic.List[object]]::new()
  $results.Add((Invoke-PlayerScenario `
    'player-rmlui-start' `
    $sourceContent `
    (740.0 / 1080.0) `
    (353.0 / 720.0) `
    'RmlUi' `
    0 `
    $true `
    $false))
  $results.Add((Invoke-PlayerScenario `
    'player-managed-fallback-settings' `
    $fallbackContent `
    (638.0 / 1080.0) `
    (401.0 / 720.0) `
    'ManagedFallback' `
    1 `
    $false `
    $true))
  $results.Add((Invoke-EditorScenario))

  $report = [ordered]@{
    schema = 'pixelengine.ui004-physical-input/v1'
    passed = $true
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitCommit = $gitCommit
    binaries = [ordered]@{
      editorSha256 = (Get-FileHash -LiteralPath $editorPathFull -Algorithm SHA256).Hash.ToLowerInvariant()
      demoSha256 = (Get-FileHash -LiteralPath $demoPathFull -Algorithm SHA256).Hash.ToLowerInvariant()
      cliSha256 = (Get-FileHash -LiteralPath $cliPathFull -Algorithm SHA256).Hash.ToLowerInvariant()
      inputHelperSha256 = (Get-FileHash -LiteralPath $inputHelperPathFull -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    packagedContent = [ordered]@{
      startupSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceContent 'startup.json') -Algorithm SHA256).Hash.ToLowerInvariant()
      mainMenuSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceContent 'ui/screens/main-menu.xhtml') -Algorithm SHA256).Hash.ToLowerInvariant()
      settingsSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceContent 'ui/screens/settings.xhtml') -Algorithm SHA256).Hash.ToLowerInvariant()
      fontSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceContent 'ui/fonts/NotoSansSC-VF.ttf') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    scenarios = $results.ToArray()
  }
  $reportPath = Join-Path $outputRootFull 'report.json'
  Write-JsonFile $reportPath $report
  Write-Output "ui004_physical_input_probe passed=True, scenarios=$($results.Count), report=$reportPath"
}
finally {
  if (Test-Path -LiteralPath $fallbackRoot) {
    Remove-Item -LiteralPath $fallbackRoot -Recurse -Force
  }
}
