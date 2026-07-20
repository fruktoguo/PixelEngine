param(
  [ValidateSet('win-x64', 'win-arm64')]
  [string]$Rid = 'win-x64',

  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [string]$OutputRoot = '最终输出',

  [ValidateSet('ManagedFallback', 'RmlUi', 'Ultralight')]
  [string]$DemoRuntimeUiBackend = 'RmlUi'
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/update-final-output-fast.ps1 需要 PowerShell 7+。'
}

$utf8NoBom = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootFull = if ([IO.Path]::IsPathRooted($OutputRoot)) {
  [IO.Path]::GetFullPath($OutputRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

function Assert-UnderRepo([string]$Path, [string]$Label) {
  $full = [IO.Path]::GetFullPath($Path)
  $root = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
  if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label 必须位于仓库目录内：$full"
  }
}

function Invoke-Checked([string]$Label, [string]$FilePath, [string[]]$Arguments) {
  Write-Host "[$Label] $FilePath $($Arguments -join ' ')"
  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "$Label 失败，退出码：$LASTEXITCODE"
  }
}

function Copy-Directory([string]$Source, [string]$Destination) {
  if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "目录不存在：$Source"
  }

  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

function Replace-FinalOutput([string]$SourceRoot, [string]$DestinationRoot) {
  Assert-UnderRepo $SourceRoot '待发布目录'
  Assert-UnderRepo $DestinationRoot '最终输出目录'

  $newRoot = "$DestinationRoot.__new"
  $backupRoot = "$DestinationRoot.__previous"
  Assert-UnderRepo $newRoot '最终输出临时目录'
  Assert-UnderRepo $backupRoot '最终输出备份目录'

  Remove-Item -LiteralPath $newRoot -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
  Move-Item -LiteralPath $SourceRoot -Destination $newRoot

  try {
    if (Test-Path -LiteralPath $DestinationRoot) {
      Move-Item -LiteralPath $DestinationRoot -Destination $backupRoot
    }

    Move-Item -LiteralPath $newRoot -Destination $DestinationRoot
    Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
  catch {
    if ((Test-Path -LiteralPath $backupRoot) -and -not (Test-Path -LiteralPath $DestinationRoot)) {
      Move-Item -LiteralPath $backupRoot -Destination $DestinationRoot
    }

    throw
  }
}

Assert-UnderRepo $outputRootFull '最终输出目录'

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stagingRoot = Join-Path $repoRoot "artifacts/fast-final-output-staging/$timestamp"
$editorPublish = Join-Path $stagingRoot 'editor-publish'
$demoPublish = Join-Path $stagingRoot 'demo-publish'
$demoPackageRoot = Join-Path $stagingRoot 'demo-package'
$nextRoot = Join-Path $stagingRoot 'next-final-output'
$finalEditorDir = Join-Path $nextRoot '编辑器'
$finalDemoDir = Join-Path $nextRoot '游戏Demo'
$finalInstallerDir = Join-Path $nextRoot '安装器'
$fastRecordDir = Join-Path $nextRoot '_快速构建'

$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$nativeBuildScript = Join-Path $PSScriptRoot 'build-native.ps1'
$demoPublishScript = Join-Path $PSScriptRoot 'publish-r2r.ps1'
$packageScript = Join-Path $PSScriptRoot 'package.ps1'
$installerBuildScript = Join-Path $PSScriptRoot 'build-windows-installer.ps1'
$editorProject = Join-Path $repoRoot 'apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj'
$editorExe = Join-Path $finalEditorDir 'PixelEngine.exe'
$demoExe = Join-Path $finalDemoDir 'PixelEngine Demo.exe'

foreach ($requiredFile in @($nativeBuildScript, $demoPublishScript, $packageScript, $installerBuildScript, $editorProject)) {
  if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
    throw "快速输出缺少必需文件：$requiredFile"
  }
}

$gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[a-f0-9]{40}$') {
  throw '无法读取当前 Git commit。'
}

$trackedStatus = (& git -C $repoRoot status --porcelain --untracked-files=no) -join "`n"
$sourceTrackedWorktreeClean = [string]::IsNullOrWhiteSpace($trackedStatus)
$succeeded = $false

New-Item -ItemType Directory -Force -Path `
  $editorPublish, `
  $demoPublish, `
  $demoPackageRoot, `
  $finalEditorDir, `
  $fastRecordDir | Out-Null

try {
  Invoke-Checked 'native-build' $pwsh @(
    '-NoLogo', '-NoProfile', '-File', $nativeBuildScript,
    '-Rid', $Rid,
    '-Configuration', $Configuration
  )

  Invoke-Checked 'editor-publish' $dotnet @(
    'publish', $editorProject,
    '-c', $Configuration,
    '-r', $Rid,
    '--self-contained', 'false',
    '-o', $editorPublish
  )
  Copy-Directory $editorPublish $finalEditorDir

  $installerManifest = $null
  if ($Rid -eq 'win-x64') {
    Invoke-Checked 'windows-installer' $pwsh @(
      '-NoLogo', '-NoProfile', '-File', $installerBuildScript,
      '-Configuration', $Configuration,
      '-SkipNativeBuild',
      '-OutputRoot', $finalInstallerDir
    )
    $installerManifest = Get-Content -Raw -LiteralPath (Join-Path $finalInstallerDir 'manifest.json') | ConvertFrom-Json
  }

  Invoke-Checked 'demo-publish' $pwsh @(
    '-NoLogo', '-NoProfile', '-File', $demoPublishScript,
    '-Rid', $Rid,
    '-Configuration', $Configuration,
    '-Output', $demoPublish,
    '-ProductName', 'PixelEngine Demo',
    '-SkipNativeBuild'
  )

  Invoke-Checked 'demo-package' $pwsh @(
    '-NoLogo', '-NoProfile', '-File', $packageScript,
    '-Rid', $Rid,
    '-Channel', 'r2r',
    '-PublishDir', $demoPublish,
    '-OutputRoot', $demoPackageRoot,
    '-PlayerOutputDir', $finalDemoDir,
    '-ProductName', 'PixelEngine Demo',
    '-StartScene', 'scenes/infinite-sandbox.scene',
    '-WindowWidth', '1080',
    '-WindowHeight', '720',
    '-WindowMode', 'Windowed',
    '-VSync', 'true',
    '-RuntimeUiBackend', $DemoRuntimeUiBackend,
    '-ReleaseChannel', 'Production'
  )

  if (-not (Test-Path -LiteralPath $editorExe -PathType Leaf)) {
    throw "快速输出缺少编辑器入口：$editorExe"
  }

  if (-not (Test-Path -LiteralPath $demoExe -PathType Leaf)) {
    throw "快速输出缺少游戏入口：$demoExe"
  }

  $manifest = [ordered]@{
    schema = 'pixelengine.fast-final-output/v1'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitCommit = $gitCommit
    sourceTrackedWorktreeClean = $sourceTrackedWorktreeClean
    rid = $Rid
    configuration = $Configuration
    demoChannel = 'r2r'
    demoRuntimeUiBackendRequested = $DemoRuntimeUiBackend
    verified = $false
    testsRun = $false
    probesRun = $false
    verifierRun = $false
    editorExecutable = '编辑器/PixelEngine.exe'
    demoExecutable = '游戏Demo/PixelEngine Demo.exe'
    installer = if ($null -eq $installerManifest) {
      $null
    } else {
      [ordered]@{
        package = "安装器/$($installerManifest.installerFile)"
        manifest = '安装器/manifest.json'
        verification = '安装器/verification.json'
        checksum = '安装器/SHA256SUMS'
        signed = $false
      }
    }
  }
  $manifest | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $fastRecordDir 'manifest.json') -Encoding UTF8

  $installerReadmeLine = if ($null -eq $installerManifest) {
    '- Windows 安装器：当前 RID 不提供 MSI'
  } else {
    "- Windows 安装器：安装器\$($installerManifest.installerFile)"
  }
  @"
PixelEngine 开发者快速输出

这份目录只完成 native build、Editor publish、Demo publish/package。
未运行测试、窗口/输入探针或正式 verifier，不能作为正式验收证据。

- 编辑器：编辑器\PixelEngine.exe
- 可玩游戏：游戏Demo\PixelEngine Demo.exe
$installerReadmeLine
- 快速构建身份：_快速构建\manifest.json

需要完整验证版时，请双击“一键更新最终输出.bat”。
"@ | Set-Content -LiteralPath (Join-Path $nextRoot 'README.txt') -Encoding UTF8

  Replace-FinalOutput $nextRoot $outputRootFull
  $succeeded = $true

  Write-Host "快速输出已更新：$outputRootFull"
  Write-Host "编辑器入口：$(Join-Path $outputRootFull '编辑器/PixelEngine.exe')"
  Write-Host "游戏入口：$(Join-Path $outputRootFull '游戏Demo/PixelEngine Demo.exe')"
  if ($null -ne $installerManifest) {
    Write-Host "Windows 安装器：$(Join-Path $outputRootFull "安装器/$($installerManifest.installerFile)")"
  }
  Write-Host '测试 / probe / verifier：全部跳过'
}
finally {
  if ($succeeded -and (Test-Path -LiteralPath $stagingRoot)) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
  }
}
