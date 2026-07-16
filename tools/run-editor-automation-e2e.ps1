[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$EditorExecutable,

  [Parameter(Mandatory = $true)]
  [string]$CliExecutable,

  [Parameter(Mandatory = $true)]
  [string]$ProjectSource,

  [Parameter(Mandatory = $true)]
  [string]$OutputRoot,

  [Parameter(Mandatory = $true)]
  [string]$WorkRoot,

  [Parameter(Mandatory = $true)]
  [string]$BuildOutputRoot,

  [Parameter(Mandatory = $true)]
  [ValidatePattern('^[0-9a-f]{40}$')]
  [string]$ExpectedGitCommit,

  [string]$RepositoryRoot = '',

  [ValidateRange(10, 300)]
  [int]$EditorStartupTimeoutSeconds = 60,

  [ValidateRange(30, 1800)]
  [int]$BuildTimeoutSeconds = 900,

  [ValidateRange(10, 300)]
  [int]$PlayerTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/run-editor-automation-e2e.ps1 需要 PowerShell 7+。'
}

if (-not $IsWindows) {
  throw 'Editor automation v1 E2E 当前只支持 Windows Named Pipe。'
}

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
  [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
} else {
  [IO.Path]::GetFullPath($RepositoryRoot)
}
$editorPath = [IO.Path]::GetFullPath($EditorExecutable)
$cliPath = [IO.Path]::GetFullPath($CliExecutable)
$projectSourcePath = [IO.Path]::GetFullPath($ProjectSource)
$outputRootPath = [IO.Path]::GetFullPath($OutputRoot)
$workRootPath = [IO.Path]::GetFullPath($WorkRoot)
$buildOutputRoot = [IO.Path]::GetFullPath($BuildOutputRoot)
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
  [IO.Path]::DirectorySeparatorChar
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempPrefix = $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
  [IO.Path]::DirectorySeparatorChar

function Assert-True([bool]$Condition, [string]$Message) {
  if (-not $Condition) {
    throw $Message
  }
}

function Assert-ExistingFile([string]$Path, [string]$Label) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "$Label 不存在：$Path"
  }
}

function Assert-EmptyOrMissingDirectory([string]$Path, [string]$Label) {
  if ((Test-Path -LiteralPath $Path) -and
      @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop).Count -ne 0) {
    throw "$Label 必须不存在或为空：$Path"
  }
}

function Assert-NoReparsePoint([string]$Path, [string]$Label) {
  $current = [IO.Path]::GetFullPath($Path)
  while (-not [string]::IsNullOrWhiteSpace($current)) {
    if (Test-Path -LiteralPath $current) {
      $attributes = [IO.File]::GetAttributes($current)
      if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label 包含 reparse point：$current"
      }
    }

    $parent = [IO.Path]::GetDirectoryName($current)
    if ([string]::Equals($parent, $current, [StringComparison]::OrdinalIgnoreCase)) {
      break
    }

    $current = $parent
  }
}

Assert-ExistingFile $editorPath 'Editor executable'
Assert-ExistingFile $cliPath 'CLI executable'
Assert-ExistingFile (Join-Path $repoRoot 'PixelEngine.sln') 'Repository solution'
Assert-ExistingFile (Join-Path $repoRoot 'tools/build-player.ps1') 'Build player tool'
Assert-ExistingFile (Join-Path $projectSourcePath 'project.pixelproj') 'Project manifest'
Assert-True ($outputRootPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) `
  'E2E OutputRoot 必须位于仓库内。'
Assert-True `
  ($workRootPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $workRootPath.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) `
  'E2E WorkRoot 必须位于仓库或当前用户临时目录内。'
Assert-True ($buildOutputRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) `
  'E2E BuildOutputRoot 必须位于仓库内。'
Assert-True (-not [string]::Equals($outputRootPath, $workRootPath, [StringComparison]::OrdinalIgnoreCase)) `
  'E2E OutputRoot 与 WorkRoot 必须不同。'
Assert-True (-not [string]::Equals($outputRootPath, $buildOutputRoot, [StringComparison]::OrdinalIgnoreCase)) `
  'E2E OutputRoot 与 BuildOutputRoot 必须不同。'
Assert-True (-not [string]::Equals($workRootPath, $buildOutputRoot, [StringComparison]::OrdinalIgnoreCase)) `
  'E2E WorkRoot 与 BuildOutputRoot 必须不同。'
Assert-EmptyOrMissingDirectory $outputRootPath 'E2E OutputRoot'
Assert-EmptyOrMissingDirectory $workRootPath 'E2E WorkRoot'
Assert-EmptyOrMissingDirectory $buildOutputRoot 'E2E BuildOutputRoot'
Assert-NoReparsePoint $editorPath 'Editor executable'
Assert-NoReparsePoint $cliPath 'CLI executable'
Assert-NoReparsePoint $projectSourcePath 'Project source'
Assert-NoReparsePoint $outputRootPath 'E2E OutputRoot'
Assert-NoReparsePoint $workRootPath 'E2E WorkRoot'
Assert-NoReparsePoint $buildOutputRoot 'E2E BuildOutputRoot'

[IO.Directory]::CreateDirectory($outputRootPath) | Out-Null
[IO.Directory]::CreateDirectory($workRootPath) | Out-Null
$requestRoot = Join-Path $workRootPath 'requests'
$projectRoot = Join-Path $workRootPath 'project'
$discoveryRoot = Join-Path $workRootPath 'discovery'
$artifactRoot = Join-Path $workRootPath 'artifacts'
$editorLogRoot = Join-Path $workRootPath 'editor-logs'
[IO.Directory]::CreateDirectory($requestRoot) | Out-Null
[IO.Directory]::CreateDirectory($projectRoot) | Out-Null
[IO.Directory]::CreateDirectory($editorLogRoot) | Out-Null

$utf8 = [Text.UTF8Encoding]::new($false)
$script:sequence = 0
$script:cliProcessCount = 0
$script:operations = [Collections.Generic.List[object]]::new()
$script:instanceId = $null
$script:playerProcessId = $null
$script:editorProcess = $null
$script:editorStdoutTask = $null
$script:editorStderrTask = $null
$clientInstanceId = 'pixelengine-final-e2e-' + $ExpectedGitCommit.Substring(0, 12)
$readScopes = 'editor.read'
$authorScopes = 'editor.read,editor.control,project.write'
$settingsScopes = 'editor.read,settings.write'
$buildScopes = 'editor.read,process.build'
$playerScopes = 'editor.read,process.build,process.launch'

function Get-Sha256([string]$Path) {
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-OutputRelativePath([string]$Path) {
  return [IO.Path]::GetRelativePath($outputRootPath, $Path).Replace('\', '/')
}

function Get-SafeName([string]$Value) {
  $safe = [Text.RegularExpressions.Regex]::Replace($Value, '[^A-Za-z0-9._-]+', '-')
  return $safe.Trim('-')
}

function Write-Utf8File([string]$Path, [string]$Content) {
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Content, $utf8)
}

function Write-JsonRequest([string]$Name, [object]$Value) {
  $path = Join-Path $requestRoot ((Get-SafeName $Name) + '.json')
  $json = $Value | ConvertTo-Json -Depth 32 -Compress
  Write-Utf8File $path $json
  return $path
}

function Copy-ProjectTree {
  Get-ChildItem -LiteralPath $projectSourcePath -Recurse -File -Force | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($projectSourcePath, $_.FullName)
    $segments = $relative -split '[\/]'
    if ($segments -contains 'bin' -or
        $segments -contains 'obj' -or
        $segments -contains 'artifacts' -or
        $segments -contains '.git') {
      return
    }

    $target = Join-Path $projectRoot $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
  }
}

function Invoke-CapturedProcess(
  [string]$Name,
  [string]$FilePath,
  [string[]]$Arguments,
  [int]$TimeoutSeconds,
  [int[]]$AllowedExitCodes,
  [bool]$IsCli
) {
  $script:sequence++
  if ($IsCli) {
    $script:cliProcessCount++
  }

  $prefix = '{0:D3}-{1}' -f $script:sequence, (Get-SafeName $Name)
  $stdoutPath = Join-Path $outputRootPath ($prefix + '.stdout.log')
  $stderrPath = Join-Path $outputRootPath ($prefix + '.stderr.log')
  $start = [DateTimeOffset]::UtcNow
  $psi = [Diagnostics.ProcessStartInfo]::new()
  $psi.FileName = $FilePath
  $psi.WorkingDirectory = $repoRoot
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  foreach ($argument in $Arguments) {
    $psi.ArgumentList.Add($argument)
  }

  $process = [Diagnostics.Process]::new()
  try {
    $process.StartInfo = $psi
    if (-not $process.Start()) {
      throw "$Name 无法启动进程：$FilePath"
    }

    $processId = $process.Id
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit([int]($TimeoutSeconds * 1000))) {
      $process.Kill($true)
      $process.WaitForExit()
      throw "$Name 超过 $TimeoutSeconds 秒，已终止进程树。"
    }

    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
  } finally {
    $process.Dispose()
  }

  Write-Utf8File $stdoutPath $stdout
  Write-Utf8File $stderrPath $stderr
  $outcome = if ($exitCode -eq 0) { 'passed' } else { 'accepted-nonzero' }
  $record = [ordered]@{
    sequence = $script:sequence
    name = $Name
    processId = $processId
    exitCode = $exitCode
    outcome = $outcome
    allowedExitCodes = $AllowedExitCodes
    durationMilliseconds = [long]([DateTimeOffset]::UtcNow - $start).TotalMilliseconds
    stdout = Get-OutputRelativePath $stdoutPath
    stderr = Get-OutputRelativePath $stderrPath
    stdoutSha256 = Get-Sha256 $stdoutPath
    stderrSha256 = Get-Sha256 $stderrPath
  }
  $script:operations.Add($record)
  if ($AllowedExitCodes -notcontains $exitCode) {
    $tail = (($stderr -split '\r?\n') | Select-Object -Last 20) -join [Environment]::NewLine
    throw "$Name 退出码 $exitCode 不在允许集合 [$($AllowedExitCodes -join ',')]。stderr:`n$tail"
  }

  $json = $null
  if (-not [string]::IsNullOrWhiteSpace($stdout)) {
    try {
      $json = $stdout | ConvertFrom-Json -Depth 100
    } catch {
      throw "$Name stdout 不是单一合法 JSON document：$($_.Exception.Message)"
    }
  }

  return [pscustomobject]@{
    ExitCode = $exitCode
    Json = $json
    ProcessId = $processId
    StdoutPath = $stdoutPath
    StderrPath = $stderrPath
  }
}

function Invoke-Cli(
  [string]$Name,
  [string[]]$Command,
  [string]$Scopes = 'editor.read',
  [int]$TimeoutSeconds = 120,
  [int[]]$AllowedExitCodes = @(0),
  [switch]$WithoutInstance
) {
  $arguments = [Collections.Generic.List[string]]::new()
  $arguments.Add('--discovery-root')
  $arguments.Add($discoveryRoot)
  if (-not $WithoutInstance.IsPresent) {
    Assert-True (-not [string]::IsNullOrWhiteSpace($script:instanceId)) 'CLI instance ID 尚未建立。'
    $arguments.Add('--instance')
    $arguments.Add($script:instanceId)
  }

  $arguments.Add('--client-instance-id')
  $arguments.Add($clientInstanceId)
  $arguments.Add('--scopes')
  $arguments.Add($Scopes)
  $arguments.Add('--connect-timeout')
  $arguments.Add('10')
  $arguments.Add('--timeout')
  $arguments.Add([string]$TimeoutSeconds)
  $arguments.Add('--output')
  $arguments.Add('json')
  foreach ($argument in $Command) {
    $arguments.Add($argument)
  }

  return Invoke-CapturedProcess `
    -Name $Name `
    -FilePath $cliPath `
    -Arguments $arguments.ToArray() `
    -TimeoutSeconds ($TimeoutSeconds + 15) `
    -AllowedExitCodes $AllowedExitCodes `
    -IsCli $true
}

function Read-Hierarchy([string]$Name, [string]$PageRequestPath) {
  $result = Invoke-Cli `
    -Name $Name `
    -Scopes $readScopes `
    -Command @('call', 'hierarchy.list', '--payload-file', $PageRequestPath)
  return @($result.Json.payload.items)
}

function Wait-RuntimeEntities([string]$Name, [string]$PageRequestPath, [string]$ExpectedPlaySessionId) {
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
  $attempt = 0
  while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $attempt++
    $result = Invoke-Cli `
      -Name "$Name-$attempt" `
      -Scopes $readScopes `
      -AllowedExitCodes @(0, 4) `
      -Command @('call', 'runtime.entities.list', '--payload-file', $PageRequestPath)
    if ($result.ExitCode -eq 0 -and @($result.Json.payload.items).Count -gt 0) {
      Assert-True `
        ([string]::Equals($result.Json.payload.playSessionId, $ExpectedPlaySessionId, [StringComparison]::Ordinal)) `
        "$Name 返回了错误的 Play session。"
      return $result.Json.payload
    }

    Start-Sleep -Milliseconds 200
  }

  throw "$Name 在 60 秒内没有返回 runtime entity。"
}

function Assert-VerifiedArtifact([object]$Result, [string]$Label) {
  Assert-True ($Result.Json.verification.verified -eq $true) "$Label 未通过 Client SHA256 验证。"
  Assert-True ($Result.Json.artifact.sha256 -match '^[0-9a-f]{64}$') "$Label SHA256 非法。"
  Assert-ExistingFile ([string]$Result.Json.artifact.path) "$Label artifact"
}

Copy-ProjectTree
$pageRequestPath = Write-JsonRequest 'page-500' ([ordered]@{
  schemaVersion = 1
  sort = @()
  pageSize = 500
})
$playRequestPath = Write-JsonRequest 'play-current-state' ([ordered]@{
  schemaVersion = 1
  source = 'CurrentState'
})
$markerName = 'Automation CLI E2E Marker'
$rollbackMarkerName = 'Automation CLI E2E Rolled Back Marker'
$rollbackTransactionPlanPath = Write-JsonRequest 'rollback-authoring-transaction' ([ordered]@{
  schemaVersion = 1
  name = 'Automation CLI E2E rollback proof'
  leaseMilliseconds = 60000
  idempotencyKey = 'final-e2e-rollback-transaction'
  operations = @(
    [ordered]@{
      method = 'hierarchy.gameObject.create'
      payload = [ordered]@{
        schemaVersion = 1
        name = $rollbackMarkerName
      }
      idempotencyKey = 'final-e2e-create-rollback-marker'
    },
    [ordered]@{
      method = 'hierarchy.gameObject.rename'
      payload = [ordered]@{
        schemaVersion = 1
        stableId = [int]::MaxValue
        name = 'Must fail atomically'
      }
      idempotencyKey = 'final-e2e-force-rollback'
    }
  )
})
$transactionPlanPath = Write-JsonRequest 'authoring-transaction' ([ordered]@{
  schemaVersion = 1
  name = 'Automation CLI E2E authoring'
  leaseMilliseconds = 60000
  idempotencyKey = 'final-e2e-authoring-transaction'
  operations = @(
    [ordered]@{
      method = 'hierarchy.gameObject.create'
      payload = [ordered]@{
        schemaVersion = 1
        name = $markerName
      }
      idempotencyKey = 'final-e2e-create-marker'
    }
  )
})

$editorStdoutPath = Join-Path $outputRootPath 'editor.stdout.log'
$editorStderrPath = Join-Path $outputRootPath 'editor.stderr.log'
$editorPsi = [Diagnostics.ProcessStartInfo]::new()
$editorPsi.FileName = $editorPath
$editorPsi.WorkingDirectory = $repoRoot
$editorPsi.UseShellExecute = $false
$editorPsi.CreateNoWindow = $true
$editorPsi.RedirectStandardOutput = $true
$editorPsi.RedirectStandardError = $true
$editorPsi.Environment['PIXELENGINE_BUILD_PLAYER_PATH'] = Join-Path $repoRoot 'tools/build-player.ps1'
foreach ($argument in @(
    '--project', $projectRoot,
    '--window-ticks', '10000000',
    '--automation-discovery-root', $discoveryRoot,
    '--automation-artifact-root', $artifactRoot,
    '--automation-import-root', $workRootPath,
    '--log-directory', $editorLogRoot,
    '--ephemeral-user-state',
    '--no-reopen-last-project')) {
  $editorPsi.ArgumentList.Add($argument)
}

$workflow = [ordered]@{}
$failure = $null
try {
  $script:editorProcess = [Diagnostics.Process]::new()
  $script:editorProcess.StartInfo = $editorPsi
  Assert-True ($script:editorProcess.Start()) 'Editor process 启动失败。'
  $editorPid = $script:editorProcess.Id
  $editorStartedAtUtc = $script:editorProcess.StartTime.ToUniversalTime().ToString('O')
  $script:editorStdoutTask = $script:editorProcess.StandardOutput.ReadToEndAsync()
  $script:editorStderrTask = $script:editorProcess.StandardError.ReadToEndAsync()

  $discoveryDeadline = [DateTimeOffset]::UtcNow.AddSeconds($EditorStartupTimeoutSeconds)
  $discovery = $null
  while ([DateTimeOffset]::UtcNow -lt $discoveryDeadline) {
    Assert-True (-not $script:editorProcess.HasExited) `
      "Editor 在 discovery 前退出，code=$($script:editorProcess.ExitCode)。"
    $attempt = Invoke-Cli `
      -Name 'discover' `
      -WithoutInstance `
      -AllowedExitCodes @(0, 3) `
      -Command @('discover')
    if ($attempt.ExitCode -eq 0 -and @($attempt.Json.instances).Count -eq 1) {
      $discovery = $attempt.Json
      $script:instanceId = [string]$attempt.Json.instances[0].descriptor.instanceId
      break
    }

    Start-Sleep -Milliseconds 150
  }

  Assert-True (-not [string]::IsNullOrWhiteSpace($script:instanceId)) `
    "Editor 未在 $EditorStartupTimeoutSeconds 秒内发布唯一实例。"
  $workflow.instanceId = $script:instanceId

  $ping = Invoke-Cli -Name 'ping' -Scopes $readScopes -Command @('ping')
  Assert-True ([string]::Equals($ping.Json.payload.instanceId, $script:instanceId, [StringComparison]::Ordinal)) `
    'ping instance ID 不匹配。'
  $describe = Invoke-Cli -Name 'describe' -Scopes $readScopes -Command @('describe')
  Assert-True ([int]$describe.Json.payload.processId -eq $editorPid) 'describe PID 不匹配。'
  $matrix = Invoke-Cli -Name 'capability-matrix' -Scopes $readScopes -Command @('capabilities', '--matrix')
  $capabilityCount = @($matrix.Json.capabilities).Count
  $uiCommandCount = @($matrix.Json.uiCommands).Count
  Assert-True ($capabilityCount -ge 150) "Capability matrix 过小：$capabilityCount。"
  Assert-True ($uiCommandCount -ge 300) "UI command matrix 过小：$uiCommandCount。"
  $workflow.capabilityCount = $capabilityCount
  $workflow.uiCommandCount = $uiCommandCount
  $workflow.capabilityDigest = [string]$matrix.Json.capabilityDigest
  $workflow.uiCommandDigest = [string]$matrix.Json.uiCommandDigest
  $workflow.matrixDigest = [string]$matrix.Json.matrixDigest

  $workspace = Invoke-Cli -Name 'workspace-get' -Scopes $readScopes -Command @('call', 'workspace.get')
  Assert-True ($workspace.Json.payload.projectOpen -eq $true) 'workspace 未打开工程。'
  Assert-True ($workspace.Json.payload.mode -eq 'Edit') 'workspace 初始模式不是 Edit。'
  Assert-True `
    ([IO.Path]::GetFullPath([string]$workspace.Json.payload.projectRoot) -eq $projectRoot) `
    'workspace projectRoot 不是隔离工程副本。'
  $scene = Invoke-Cli -Name 'scene-get' -Scopes $readScopes -Command @('call', 'scene.get')
  Assert-True (-not [string]::IsNullOrWhiteSpace([string]$scene.Json.payload.sceneId)) 'scene.get 缺少 scene ID。'

  $failedTransaction = Invoke-Cli `
    -Name 'transaction-execute-rollback' `
    -Scopes $authorScopes `
    -AllowedExitCodes @(4) `
    -Command @('transaction', 'execute', '--plan-file', $rollbackTransactionPlanPath)
  Assert-True ($failedTransaction.ExitCode -eq 4) '故障注入 transaction 未返回 remote error。'
  $afterRollback = @(
    Read-Hierarchy 'hierarchy-after-rollback' $pageRequestPath |
      Where-Object name -eq $rollbackMarkerName
  )
  Assert-True ($afterRollback.Count -eq 0) '失败 transaction 在 Hierarchy 留下了部分提交。'
  $workflow.transactionFailureRollback = $true

  $transaction = Invoke-Cli `
    -Name 'transaction-execute' `
    -Scopes $authorScopes `
    -Command @('transaction', 'execute', '--plan-file', $transactionPlanPath)
  Assert-True ($transaction.Json.payload.transaction.status -eq 'Committed') 'Transaction 未提交。'
  Assert-True (@($transaction.Json.payload.operations).Count -eq 1) 'Transaction operation 数量不为 1。'
  $afterCommit = @(Read-Hierarchy 'hierarchy-after-commit' $pageRequestPath | Where-Object name -eq $markerName)
  Assert-True ($afterCommit.Count -eq 1) 'Transaction commit 后 marker 不唯一。'

  $undo = Invoke-Cli -Name 'history-undo' -Scopes $authorScopes -Command @('call', 'editor.history.undo')
  Assert-True ($undo.Json.payload.canRedo -eq $true) 'Undo 后没有 Redo 项。'
  $afterUndo = @(Read-Hierarchy 'hierarchy-after-undo' $pageRequestPath | Where-Object name -eq $markerName)
  Assert-True ($afterUndo.Count -eq 0) 'Undo 未移除 transaction marker。'
  $redo = Invoke-Cli -Name 'history-redo' -Scopes $authorScopes -Command @('call', 'editor.history.redo')
  Assert-True ($redo.Json.payload.canUndo -eq $true) 'Redo 后没有 Undo 项。'
  $afterRedo = @(Read-Hierarchy 'hierarchy-after-redo' $pageRequestPath | Where-Object name -eq $markerName)
  Assert-True ($afterRedo.Count -eq 1) 'Redo 未恢复 transaction marker。'
  $markerStableId = [int]$afterRedo[0].stableId
  $workflow.markerStableId = $markerStableId
  $workflow.transactionUndoRedo = $true

  $savedBeforePlay = Invoke-Cli -Name 'scene-save-before-play' -Scopes $authorScopes -Command @('call', 'scene.save')
  Assert-True ($savedBeforePlay.Json.payload.dirty -ne $true) '首次 scene.save 后仍为 dirty。'
  $sceneCapture = Invoke-Cli `
    -Name 'scene-capture' `
    -Scopes $readScopes `
    -Command @('call', 'scene.capture', '--verify-artifact')
  Assert-VerifiedArtifact $sceneCapture 'Scene capture'
  $workflow.sceneCaptureSha256 = [string]$sceneCapture.Json.artifact.sha256

  $firstPlay = Invoke-Cli `
    -Name 'play-enter-first' `
    -Scopes $authorScopes `
    -Command @('call', 'play.enter', '--payload-file', $playRequestPath)
  Assert-True ($firstPlay.Json.payload.succeeded -eq $true) '第一次 Play 未成功。'
  Assert-True ($firstPlay.Json.payload.snapshot.mode -eq 'Play') '第一次 Play mode 不正确。'
  $firstPlaySessionId = [string]$firstPlay.Json.payload.snapshot.playSessionId
  $firstRuntime = Wait-RuntimeEntities 'runtime-first' $pageRequestPath $firstPlaySessionId
  $workflow.firstPlaySessionId = $firstPlaySessionId
  $workflow.firstRuntimeEntityCount = @($firstRuntime.items).Count

  $consoleCounts = Invoke-Cli -Name 'console-counts' -Scopes $readScopes -Command @('call', 'console.counts.get')
  $consoleEntries = Invoke-Cli `
    -Name 'console-entries' `
    -Scopes $readScopes `
    -Command @('call', 'console.entries.list', '--payload-file', $pageRequestPath)
  $profiler = Invoke-Cli -Name 'profiler-get' -Scopes $readScopes -Command @('call', 'profiler.get')
  Assert-True ([long]$profiler.Json.payload.frameIndex -ge 0) 'Profiler frameIndex 非法。'
  $workflow.consoleEntryCount = @($consoleEntries.Json.payload.items).Count
  $workflow.consoleErrorCount = [int]$consoleCounts.Json.payload.errors
  $workflow.profilerFrameIndex = [long]$profiler.Json.payload.frameIndex

  $gameCapture = Invoke-Cli `
    -Name 'game-capture' `
    -Scopes $readScopes `
    -Command @('call', 'game.capture', '--verify-artifact')
  Assert-VerifiedArtifact $gameCapture 'Game capture'
  $workflow.gameCaptureSha256 = [string]$gameCapture.Json.artifact.sha256

  $paused = Invoke-Cli -Name 'play-pause' -Scopes $authorScopes -Command @('call', 'play.pause')
  Assert-True ($paused.Json.payload.snapshot.mode -eq 'Paused') 'Pause 后 mode 不是 Paused。'
  $pauseFrame = [long]$paused.Json.payload.snapshot.frameIndex
  $stepped = Invoke-Cli -Name 'play-step' -Scopes $authorScopes -Command @('call', 'play.step')
  Assert-True ($stepped.Json.payload.snapshot.mode -eq 'Paused') 'Step 后 mode 不是 Paused。'
  Assert-True ([long]$stepped.Json.payload.snapshot.frameIndex -gt $pauseFrame) 'Step 未推进 frameIndex。'
  $firstStop = Invoke-Cli -Name 'play-stop-first' -Scopes $authorScopes -Command @('call', 'play.stop')
  Assert-True ($firstStop.Json.payload.snapshot.mode -eq 'Edit') '第一次 Stop 后 mode 不是 Edit。'

  $secondPlay = Invoke-Cli `
    -Name 'play-enter-second' `
    -Scopes $authorScopes `
    -Command @('call', 'play.enter', '--payload-file', $playRequestPath)
  Assert-True ($secondPlay.Json.payload.succeeded -eq $true) '第二次 Play 未成功。'
  $secondPlaySessionId = [string]$secondPlay.Json.payload.snapshot.playSessionId
  Assert-True `
    (-not [string]::Equals($firstPlaySessionId, $secondPlaySessionId, [StringComparison]::Ordinal)) `
    '两次 Play session ID 不应复用。'
  $secondRuntime = Wait-RuntimeEntities 'runtime-second' $pageRequestPath $secondPlaySessionId
  $workflow.secondPlaySessionId = $secondPlaySessionId
  $workflow.secondRuntimeEntityCount = @($secondRuntime.items).Count
  $secondStop = Invoke-Cli -Name 'play-stop-second' -Scopes $authorScopes -Command @('call', 'play.stop')
  Assert-True ($secondStop.Json.payload.snapshot.mode -eq 'Edit') '第二次 Stop 后 mode 不是 Edit。'

  $inspectorRequestPath = Write-JsonRequest 'marker-inspector-get' ([ordered]@{
    schemaVersion = 1
    stableId = $markerStableId
  })
  $markerBefore = Invoke-Cli `
    -Name 'marker-inspector-get' `
    -Scopes $readScopes `
    -Command @('call', 'inspector.get', '--payload-file', $inspectorRequestPath)
  $transformRequestPath = Write-JsonRequest 'marker-transform-set' ([ordered]@{
    schemaVersion = 1
    stableId = $markerStableId
    transform = [ordered]@{
      x = [double]$markerBefore.Json.payload.localTransform.x + 1.25
      y = [double]$markerBefore.Json.payload.localTransform.y - 0.75
      rotationRadians = [double]$markerBefore.Json.payload.localTransform.rotationRadians + 0.125
      scaleX = [double]$markerBefore.Json.payload.localTransform.scaleX
      scaleY = [double]$markerBefore.Json.payload.localTransform.scaleY
    }
  })
  $markerAfter = Invoke-Cli `
    -Name 'marker-transform-set' `
    -Scopes $authorScopes `
    -Command @('call', 'inspector.transform.set', '--payload-file', $transformRequestPath, '--idempotency-key', 'final-e2e-transform-marker')
  Assert-True `
    ([Math]::Abs([double]$markerAfter.Json.payload.localTransform.x -
      ([double]$markerBefore.Json.payload.localTransform.x + 1.25)) -lt 0.0001) `
    '第二次 Stop 后 Transform 修改未生效。'
  $savedAfterPlay = Invoke-Cli -Name 'scene-save-after-play' -Scopes $authorScopes -Command @('call', 'scene.save')
  Assert-True ($savedAfterPlay.Json.payload.dirty -ne $true) '最终 scene.save 后仍为 dirty。'
  $workflow.modifiedAndSavedAfterSecondPlay = $true

  $buildSettingsGet = Invoke-Cli -Name 'build-settings-get' -Scopes $readScopes -Command @('call', 'settings.build.get')
  $buildSettings = $buildSettingsGet.Json.payload
  Assert-True (@($buildSettings.scenes).Count -gt 0) 'Build Settings 没有 scene。'
  $buildSettings.rid = 'win-x64'
  $buildSettings.channel = 'R2R'
  $buildSettings.configuration = 'Release'
  $buildSettings.outputDirectory = $buildOutputRoot
  $buildSettings.productName = 'PixelEngine Automation E2E'
  $buildSettings.version = '1.0.0'
  $buildSettings.informationalVersion = '1.0.0+automation-e2e.' + $ExpectedGitCommit.Substring(0, 12)
  $buildSettings.includeSymbols = $false
  $buildSettings.packageWholeContent = $true
  $buildSettings.runAfterBuild = $false
  $buildSettingsPath = Write-JsonRequest 'build-settings-set' $buildSettings
  $buildSettingsSet = Invoke-Cli `
    -Name 'build-settings-set' `
    -Scopes $settingsScopes `
    -Command @('call', 'settings.build.set', '--payload-file', $buildSettingsPath, '--idempotency-key', 'final-e2e-build-settings')
  Assert-True `
    ([IO.Path]::GetFullPath([string]$buildSettingsSet.Json.payload.outputDirectory) -eq $buildOutputRoot) `
    'Build Settings outputDirectory 未生效。'

  $preflight = Invoke-Cli -Name 'build-preflight' -Scopes $buildScopes -Command @('build', 'preflight')
  Assert-True ($preflight.Json.payload.ok -eq $true) `
    ('Build preflight 失败：' + [string]$preflight.Json.payload.diagnostic)
  $build = Invoke-Cli `
    -Name 'build-start-wait' `
    -Scopes $buildScopes `
    -TimeoutSeconds $BuildTimeoutSeconds `
    -Command @('build', 'start', '--wait', '--request-timeout', [string]$BuildTimeoutSeconds, '--idempotency-key', 'final-e2e-build')
  Assert-True ($build.Json.payload.state -eq 'Succeeded') 'Build 未进入 Succeeded。'
  Assert-True ($build.Json.payload.result.ok -eq $true) 'Build result.ok 不是 true。'
  $buildId = [string]$build.Json.payload.buildId
  $launcherPath = [IO.Path]::GetFullPath([string]$build.Json.payload.result.launcherPath)
  Assert-ExistingFile $launcherPath 'Built player launcher'
  $buildLogs = Invoke-Cli `
    -Name 'build-logs' `
    -Scopes $buildScopes `
    -Command @('build', 'logs', $buildId)
  Assert-VerifiedArtifact $buildLogs 'Build logs'
  $workflow.buildId = $buildId
  $workflow.buildState = [string]$build.Json.payload.state
  $workflow.buildPackageSha256 = [string]$build.Json.payload.result.sha256
  $workflow.launcherSha256 = Get-Sha256 $launcherPath
  $workflow.buildLogSha256 = [string]$buildLogs.Json.artifact.sha256

  $player = Invoke-Cli `
    -Name 'player-launch' `
    -Scopes $playerScopes `
    -TimeoutSeconds $PlayerTimeoutSeconds `
    -Command @('player', 'launch', $buildId, '--request-timeout', [string]$PlayerTimeoutSeconds, '--idempotency-key', 'final-e2e-player-launch')
  Assert-True ($player.Json.payload.state -eq 'Running') 'Player launch 未返回 Running。'
  $script:playerProcessId = [string]$player.Json.payload.playerProcessId
  Start-Sleep -Seconds 2
  $playerRunning = Invoke-Cli `
    -Name 'player-get-running' `
    -Scopes $playerScopes `
    -Command @('player', 'get', $script:playerProcessId)
  Assert-True ($playerRunning.Json.payload.state -eq 'Running') 'Player 未保持 Running。'
  $playerTerminated = Invoke-Cli `
    -Name 'player-terminate' `
    -Scopes $playerScopes `
    -TimeoutSeconds $PlayerTimeoutSeconds `
    -Command @('player', 'terminate', $script:playerProcessId, '--wait', '--request-timeout', [string]$PlayerTimeoutSeconds, '--idempotency-key', 'final-e2e-player-terminate')
  Assert-True ($playerTerminated.Json.payload.state -eq 'Exited') 'Player terminate 未进入 Exited。'
  Assert-True ($playerTerminated.Json.payload.terminationRequested -eq $true) `
    'Player terminate 未记录 terminationRequested。'
  $workflow.playerProcessId = $script:playerProcessId
  $workflow.playerProcessIdObserved = [int]$player.Json.payload.processId
  $workflow.playerRunningVerified = $true
  $workflow.playerTerminated = $true
  $script:playerProcessId = $null

  $exit = Invoke-Cli -Name 'workspace-exit' -Scopes $authorScopes -Command @('call', 'workspace.exit')
  $workflow.exitStatus = [string]$exit.Json.payload.status
  Assert-True ($script:editorProcess.WaitForExit(30000)) 'workspace.exit 后 Editor 未在 30 秒内退出。'
  $script:editorProcess.WaitForExit()
  Assert-True ($script:editorProcess.ExitCode -eq 0) `
    "Editor 退出码不是 0：$($script:editorProcess.ExitCode)。"
  $postExitDiscovery = Invoke-Cli `
    -Name 'discover-after-exit' `
    -WithoutInstance `
    -AllowedExitCodes @(3) `
    -Command @('discover')
  Assert-True (@($postExitDiscovery.Json.instances).Count -eq 0) 'Editor 退出后 descriptor 仍可发现。'

  $editorStdout = $script:editorStdoutTask.GetAwaiter().GetResult()
  $editorStderr = $script:editorStderrTask.GetAwaiter().GetResult()
  Write-Utf8File $editorStdoutPath $editorStdout
  Write-Utf8File $editorStderrPath $editorStderr
  $editorStderrLines = @(
    ($editorStderr -split '\r?\n') |
      Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
  )
  $allowedLibPngWarning = 'libpng warning: iCCP: known incorrect sRGB profile'
  $unexpectedEditorStderr = @(
    $editorStderrLines |
      Where-Object { -not [string]::Equals($_, $allowedLibPngWarning, [StringComparison]::Ordinal) }
  )
  Assert-True ($unexpectedEditorStderr.Count -eq 0) `
    ('Editor stderr 含未允许诊断：' + ($unexpectedEditorStderr -join ' | '))
  $workflow.allowedLibPngWarningCount = $editorStderrLines.Count

  $report = [ordered]@{
    schema = 'pixelengine.editor-automation-e2e/v1'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitCommit = $ExpectedGitCommit
    allPassed = $true
    cliOnly = $true
    externalEditorProcess = $true
    externalCliProcessCount = $script:cliProcessCount
    skipped = @()
    editor = [ordered]@{
      executableSha256 = Get-Sha256 $editorPath
      processId = $editorPid
      processStartUtc = $editorStartedAtUtc
      exitCode = $script:editorProcess.ExitCode
      stdout = Get-OutputRelativePath $editorStdoutPath
      stderr = Get-OutputRelativePath $editorStderrPath
      allowedStderr = 'libpng-iCCP-known-incorrect-sRGB-profile-only'
      allowedStderrCount = $editorStderrLines.Count
      descriptorRemoved = $true
    }
    cli = [ordered]@{
      executableSha256 = Get-Sha256 $cliPath
      clientInstanceId = $clientInstanceId
      processCount = $script:cliProcessCount
    }
    workflow = $workflow
    requiredScopes = @(
      [ordered]@{ id = 'discover-and-capability-matrix'; status = 'passed' }
      [ordered]@{ id = 'transaction-and-undo-redo'; status = 'passed' }
      [ordered]@{ id = 'scene-authoring-and-save'; status = 'passed' }
      [ordered]@{ id = 'first-play-runtime-console-profiler-pause-step-stop'; status = 'passed' }
      [ordered]@{ id = 'second-play-runtime-stop'; status = 'passed' }
      [ordered]@{ id = 'post-play-modify-and-save'; status = 'passed' }
      [ordered]@{ id = 'artifact-sha256'; status = 'passed' }
      [ordered]@{ id = 'build'; status = 'passed' }
      [ordered]@{ id = 'player-launch-verify-terminate'; status = 'passed' }
      [ordered]@{ id = 'editor-public-exit-and-discovery-cleanup'; status = 'passed' }
    )
    operations = $script:operations.ToArray()
  }
  $reportPath = Join-Path $outputRootPath 'report.json'
  $temporaryReportPath = Join-Path $outputRootPath ('.report.' + [Guid]::NewGuid().ToString('N') + '.tmp')
  Write-Utf8File $temporaryReportPath ($report | ConvertTo-Json -Depth 32)
  Move-Item -LiteralPath $temporaryReportPath -Destination $reportPath
  [Console]::Out.WriteLine(
    "automation_e2e schema=$($report.schema), allPassed=True, gitCommit=$ExpectedGitCommit, capabilities=$capabilityCount, uiCommands=$uiCommandCount, cliProcesses=$($script:cliProcessCount), buildId=$buildId")
} catch {
  $failure = $_
  throw
} finally {
  if ($null -ne $script:editorProcess) {
    if (-not $script:editorProcess.HasExited) {
      try {
        $script:editorProcess.Kill($true)
        [void]$script:editorProcess.WaitForExit(10000)
      } catch {
        if ($null -eq $failure) {
          throw
        }
      }
    }

    if ($null -ne $script:editorStdoutTask -and -not (Test-Path -LiteralPath $editorStdoutPath)) {
      try {
        Write-Utf8File $editorStdoutPath $script:editorStdoutTask.GetAwaiter().GetResult()
      } catch {
      }
    }

    if ($null -ne $script:editorStderrTask -and -not (Test-Path -LiteralPath $editorStderrPath)) {
      try {
        Write-Utf8File $editorStderrPath $script:editorStderrTask.GetAwaiter().GetResult()
      } catch {
      }
    }

    $script:editorProcess.Dispose()
  }
}
