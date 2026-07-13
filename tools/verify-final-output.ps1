param(
  [string]$OutputRoot = '最终输出',

  [switch]$AllowCommitMismatch
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/verify-final-output.ps1 需要 PowerShell 7+。请使用 pwsh -NoProfile -File tools/verify-final-output.ps1。'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootFull = if ([IO.Path]::IsPathRooted($OutputRoot)) {
  [IO.Path]::GetFullPath($OutputRoot)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

function Resolve-OutputPath([string]$RelativePath, [string]$Label) {
  if ([string]::IsNullOrWhiteSpace($RelativePath)) {
    throw "$Label 不能为空。"
  }

  if ([IO.Path]::IsPathRooted($RelativePath)) {
    throw "$Label 必须是相对路径：$RelativePath"
  }

  $full = [IO.Path]::GetFullPath((Join-Path $outputRootFull $RelativePath))
  $root = $outputRootFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and
      -not [string]::Equals($full, $outputRootFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label 不能逃逸正式输出目录：$RelativePath"
  }

  return $full
}

function Assert-FileExists([string]$Path, [string]$Label) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "$Label 不存在：$Path"
  }
}

function Assert-ChecksumContains([Collections.Generic.HashSet[string]]$RelativePaths, [string]$RelativePath, [string]$Label) {
  $normalized = $RelativePath.Replace('\', '/')
  if (-not $RelativePaths.Contains($normalized)) {
    throw "SHA256SUMS 缺少 $Label：$normalized"
  }
}

function Get-OutputFileText([string]$RelativePath, [string]$Label) {
  $full = Resolve-OutputPath $RelativePath $Label
  Assert-FileExists $full $Label
  return Get-Content -Raw -LiteralPath $full
}

function Assert-SummaryValue([string]$Text, [string]$Prefix, [string]$Key, [string]$Expected, [string]$Label) {
  $line = ($Text -split "`r?`n" | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) } | Select-Object -Last 1)
  if (-not $line -or -not $line.Contains("$Key=$Expected", [StringComparison]::Ordinal)) {
    throw "$Label 缺少成功摘要：$Prefix$key=$Expected"
  }
}

function Assert-TextContains([string]$Text, [string]$Needle, [string]$Label) {
  if (-not $Text.Contains($Needle, [StringComparison]::Ordinal)) {
    throw "$Label 缺少验证标记：$Needle"
  }
}

function Get-ManagedEditorDependencyFileNames(
  [string]$EditorRoot,
  [string[]]$PrimaryAssemblyNames
) {
  $primaryAssemblySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($primaryAssemblyName in $PrimaryAssemblyNames) {
    [void]$primaryAssemblySet.Add($primaryAssemblyName)
  }

  $managedDependencyFiles = [Collections.Generic.List[string]]::new()
  $editorDlls = @(Get-ChildItem -LiteralPath $EditorRoot -File -Filter '*.dll' -Force | Sort-Object Name)
  foreach ($file in $editorDlls) {
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($primaryAssemblySet.Contains($assemblyName) -or
        $file.Name.Equals('PixelEngine.Editor.dll', [StringComparison]::OrdinalIgnoreCase) -or
        $file.Name.Equals('PixelEngine.Editor.Shell.dll', [StringComparison]::OrdinalIgnoreCase)) {
      continue
    }

    if ($assemblyName.StartsWith('PixelEngine.', [StringComparison]::OrdinalIgnoreCase)) {
      throw "编辑器运行目录包含未登记的 PixelEngine 脚本引用程序集：$($file.Name)"
    }

    try {
      [void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)
    }
    catch [BadImageFormatException] {
      continue
    }

    $managedDependencyFiles.Add($file.Name)
  }

  return $managedDependencyFiles.ToArray()
}

if (-not (Test-Path -LiteralPath $outputRootFull -PathType Container)) {
  throw "正式输出目录不存在：$outputRootFull"
}

$manifestRelative = '_验证记录/manifest.json'
$manifestPath = Resolve-OutputPath $manifestRelative 'manifest'
Assert-FileExists $manifestPath 'manifest'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

if ($manifest.schema -ne 'pixelengine.final-output/v1') {
  throw "manifest schema 不匹配：$($manifest.schema)"
}

if ($manifest.sourceWorktreePolicy -ne 'tracked-clean-required') {
  throw "manifest sourceWorktreePolicy 不匹配：$($manifest.sourceWorktreePolicy)"
}

if ($manifest.sourceTrackedWorktreeClean -ne $true) {
  throw 'manifest sourceTrackedWorktreeClean 必须为 true。'
}

$requestedDemoRuntimeUiBackend = [string]$manifest.demoRuntimeUiBackendRequested
$expectedDemoRuntimeUiBackendActive = switch ($requestedDemoRuntimeUiBackend) {
  'RmlUi' { 'RmlUi' }
  'ManagedFallback' { 'ManagedFallback' }
  'Ultralight' { 'ManagedFallback' }
  default { throw "manifest demoRuntimeUiBackendRequested 非法：$requestedDemoRuntimeUiBackend" }
}
$expectedDemoRuntimeUiBackendFallback = $requestedDemoRuntimeUiBackend -eq 'Ultralight'
if ($manifest.demoRuntimeUiBackendActive -ne $expectedDemoRuntimeUiBackendActive) {
  throw "manifest Demo UI active backend 与请求策略不一致。requested=$requestedDemoRuntimeUiBackend, expected=$expectedDemoRuntimeUiBackendActive, actual=$($manifest.demoRuntimeUiBackendActive)"
}
$demoRuntimeUiBackendFallbackProperty = $manifest.PSObject.Properties['demoRuntimeUiBackendFallback']
if ($null -eq $demoRuntimeUiBackendFallbackProperty -or $demoRuntimeUiBackendFallbackProperty.Value -isnot [bool]) {
  throw 'manifest demoRuntimeUiBackendFallback 必须存在且为 bool。'
}
if ([bool]$demoRuntimeUiBackendFallbackProperty.Value -ne $expectedDemoRuntimeUiBackendFallback) {
  throw "manifest Demo UI fallback 与请求策略不一致。requested=$requestedDemoRuntimeUiBackend, expected=$expectedDemoRuntimeUiBackendFallback, actual=$($demoRuntimeUiBackendFallbackProperty.Value)"
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) {
  throw '无法读取当前 git HEAD。'
}

if (-not $AllowCommitMismatch.IsPresent -and $manifest.gitCommit -ne $head) {
  throw "正式输出 gitCommit 与当前 HEAD 不一致。manifest=$($manifest.gitCommit), head=$head。若只做历史产物审计，请显式传 -AllowCommitMismatch。"
}

if ($manifest.checksumFile -ne 'SHA256SUMS') {
  throw "manifest checksumFile 不匹配：$($manifest.checksumFile)"
}

$requiredScriptReferenceAssemblies = @(
  'PixelEngine.Audio',
  'PixelEngine.Content',
  'PixelEngine.Core',
  'PixelEngine.Gui',
  'PixelEngine.Hosting',
  'PixelEngine.Interop',
  'PixelEngine.Physics',
  'PixelEngine.Rendering',
  'PixelEngine.Scripting',
  'PixelEngine.Serialization',
  'PixelEngine.Simulation',
  'PixelEngine.UI',
  'PixelEngine.World'
)
$scriptReferenceAssembliesRelative = [string]$manifest.editorScriptReferenceAssembliesPath
if ($scriptReferenceAssembliesRelative -ne '编辑器/ScriptReferenceAssemblies') {
  throw "manifest editorScriptReferenceAssembliesPath 不匹配：$scriptReferenceAssembliesRelative"
}

if ($manifest.editorScriptReferenceAssembliesPolicy -ne 'managed-dll-and-xml-intellisense-no-pdb') {
  throw "manifest editorScriptReferenceAssembliesPolicy 不匹配：$($manifest.editorScriptReferenceAssembliesPolicy)"
}

$declaredScriptReferenceAssemblies = @($manifest.editorScriptReferenceAssemblies)
$declaredScriptReferenceSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($assemblyName in $declaredScriptReferenceAssemblies) {
  if (-not $declaredScriptReferenceSet.Add([string]$assemblyName)) {
    throw "manifest editorScriptReferenceAssemblies 存在重复项：$assemblyName"
  }
}

if ($declaredScriptReferenceSet.Count -ne $requiredScriptReferenceAssemblies.Count) {
  throw "manifest editorScriptReferenceAssemblies 数量不匹配：expected=$($requiredScriptReferenceAssemblies.Count), actual=$($declaredScriptReferenceSet.Count)"
}

foreach ($assemblyName in $requiredScriptReferenceAssemblies) {
  if (-not $declaredScriptReferenceSet.Contains($assemblyName)) {
    throw "manifest editorScriptReferenceAssemblies 缺少：$assemblyName"
  }
}

if ($manifest.editorScriptReferenceManagedDependencyPolicy -ne 'managed-editor-publish-dlls-excluding-editor-and-native') {
  throw "manifest editorScriptReferenceManagedDependencyPolicy 不匹配：$($manifest.editorScriptReferenceManagedDependencyPolicy)"
}

$declaredScriptReferenceManagedDependencies = @($manifest.editorScriptReferenceManagedDependencies)
if ($declaredScriptReferenceManagedDependencies.Count -eq 0) {
  throw 'manifest editorScriptReferenceManagedDependencies 不能为空。'
}

$declaredScriptReferenceManagedDependencySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($dependencyFileNameValue in $declaredScriptReferenceManagedDependencies) {
  $dependencyFileName = [string]$dependencyFileNameValue
  if ([IO.Path]::GetFileName($dependencyFileName) -ne $dependencyFileName -or
      -not $dependencyFileName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
    throw "manifest editorScriptReferenceManagedDependencies 文件名非法：$dependencyFileName"
  }

  if ($dependencyFileName.StartsWith('PixelEngine.', [StringComparison]::OrdinalIgnoreCase) -or
      $dependencyFileName.Equals('PixelEngine.Editor.dll', [StringComparison]::OrdinalIgnoreCase) -or
      $dependencyFileName.Equals('PixelEngine.Editor.Shell.dll', [StringComparison]::OrdinalIgnoreCase)) {
    throw "manifest editorScriptReferenceManagedDependencies 不得包含 PixelEngine primary/Editor 装配：$dependencyFileName"
  }

  if (-not $declaredScriptReferenceManagedDependencySet.Add($dependencyFileName)) {
    throw "manifest editorScriptReferenceManagedDependencies 存在重复项：$dependencyFileName"
  }
}

$editorSymbolsProperty = $manifest.PSObject.Properties['editorSymbolsIncluded']
if ($null -eq $editorSymbolsProperty -or $editorSymbolsProperty.Value -isnot [bool]) {
  throw 'manifest editorSymbolsIncluded 必须存在且为 bool。'
}
$editorSymbolsIncluded = [bool]$editorSymbolsProperty.Value

if (-not $editorSymbolsIncluded -and
    $manifest.editorDeveloperMetadataPolicy -ne 'runtime-pdb-and-xml-pruned') {
  throw "manifest editorDeveloperMetadataPolicy 不匹配：$($manifest.editorDeveloperMetadataPolicy)"
}

if ($editorSymbolsIncluded -and
    $manifest.editorDeveloperMetadataPolicy -ne 'included-for-diagnostics') {
  throw "manifest editorDeveloperMetadataPolicy 不匹配：$($manifest.editorDeveloperMetadataPolicy)"
}

$checksumPath = Resolve-OutputPath ([string]$manifest.checksumFile) 'checksumFile'
Assert-FileExists $checksumPath 'SHA256SUMS'

$editorExe = Resolve-OutputPath ([string]$manifest.editorExecutable) 'editorExecutable'
$demoExe = Resolve-OutputPath ([string]$manifest.demoExecutable) 'demoExecutable'
Assert-FileExists $editorExe '编辑器入口'
Assert-FileExists $demoExe 'Demo 入口'
$editorRoot = Split-Path -Parent $editorExe
$runtimeManagedDependencies = @(Get-ManagedEditorDependencyFileNames $editorRoot $requiredScriptReferenceAssemblies)
$runtimeManagedDependencySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($dependencyFileName in $runtimeManagedDependencies) {
  [void]$runtimeManagedDependencySet.Add([string]$dependencyFileName)
}
if ($runtimeManagedDependencySet.Count -ne $declaredScriptReferenceManagedDependencySet.Count) {
  throw "manifest managed dependency 数量与编辑器运行目录不匹配：runtime=$($runtimeManagedDependencySet.Count), manifest=$($declaredScriptReferenceManagedDependencySet.Count)"
}
foreach ($dependencyFileName in $runtimeManagedDependencySet) {
  if (-not $declaredScriptReferenceManagedDependencySet.Contains($dependencyFileName)) {
    throw "manifest editorScriptReferenceManagedDependencies 缺少编辑器运行目录 managed dependency：$dependencyFileName"
  }
}
foreach ($dependencyFileName in $declaredScriptReferenceManagedDependencySet) {
  if (-not $runtimeManagedDependencySet.Contains($dependencyFileName)) {
    throw "manifest editorScriptReferenceManagedDependencies 登记了非编辑器运行目录 managed dependency：$dependencyFileName"
  }
}
$scriptReferenceAssembliesRoot = Resolve-OutputPath $scriptReferenceAssembliesRelative 'editorScriptReferenceAssembliesPath'
if (-not (Test-Path -LiteralPath $scriptReferenceAssembliesRoot -PathType Container)) {
  throw "脚本引用程序集目录不存在：$scriptReferenceAssembliesRoot"
}

foreach ($assemblyName in $requiredScriptReferenceAssemblies) {
  $primaryDllPath = Join-Path $scriptReferenceAssembliesRoot "$assemblyName.dll"
  Assert-FileExists $primaryDllPath "脚本引用 DLL $assemblyName"
  Assert-FileExists (Join-Path $scriptReferenceAssembliesRoot "$assemblyName.xml") "脚本引用 XML $assemblyName"
  try {
    $primaryIdentity = [Reflection.AssemblyName]::GetAssemblyName($primaryDllPath)
  }
  catch [BadImageFormatException] {
    throw "脚本引用 primary DLL 不是托管程序集：$assemblyName.dll"
  }

  if ($primaryIdentity.Name -ne $assemblyName) {
    throw "脚本引用 primary DLL identity 不匹配：file=$assemblyName.dll, assembly=$($primaryIdentity.Name)"
  }
}


foreach ($dependencyFileName in $declaredScriptReferenceManagedDependencies) {
  $dependencyPath = Join-Path $scriptReferenceAssembliesRoot ([string]$dependencyFileName)
  Assert-FileExists $dependencyPath "脚本引用 managed dependency $dependencyFileName"
  try {
    $dependencyIdentity = [Reflection.AssemblyName]::GetAssemblyName($dependencyPath)
  }
  catch [BadImageFormatException] {
    throw "脚本引用 managed dependency 不是托管程序集：$dependencyFileName"
  }

  if ("$($dependencyIdentity.Name).dll" -ne $dependencyFileName) {
    throw "脚本引用 managed dependency identity 不匹配：file=$dependencyFileName, assembly=$($dependencyIdentity.Name)"
  }
}

$validation = $manifest.validation
if ($null -eq $validation) {
  throw 'manifest 缺少 validation 节点。'
}

if ($validation.editorDefaultWorkbenchProbe.completed -ne $true -or
    $validation.editorDefaultWorkbenchProbe.succeeded -ne $true -or
    $validation.editorDefaultWorkbenchProbe.buildOk -ne $true) {
  throw '编辑器默认工作台 probe 记录不是通过状态。'
}

if ($validation.demoWindowProbe.completed -ne $true) {
  throw 'Demo 窗口 probe 记录不是完成状态。'
}

$validationPaths = @(
  [string]$validation.editorDefaultWorkbenchProbe.stdout,
  [string]$validation.editorDefaultWorkbenchProbe.stderr,
  [string]$validation.editorDefaultWorkbenchProbe.capture,
  [string]$validation.demoWindowProbe.stdout,
  [string]$validation.demoWindowProbe.stderr,
  [string]$validation.demoWindowProbe.capture,
  [string]$validation.demoBuildResult
)

foreach ($relative in $validationPaths) {
  $full = Resolve-OutputPath $relative 'validation path'
  Assert-FileExists $full "验证记录 $relative"
}

$editorProbeStdout = Get-OutputFileText ([string]$validation.editorDefaultWorkbenchProbe.stdout) '编辑器默认工作台 probe stdout'
Assert-SummaryValue $editorProbeStdout 'editor_default_workbench_probe ' 'completed' 'True' '编辑器默认工作台 probe stdout'
Assert-SummaryValue $editorProbeStdout 'editor_default_workbench_probe ' 'succeeded' 'True' '编辑器默认工作台 probe stdout'
Assert-SummaryValue $editorProbeStdout 'editor_default_workbench_probe ' 'build_completed' 'True' '编辑器默认工作台 probe stdout'
Assert-SummaryValue $editorProbeStdout 'editor_default_workbench_probe ' 'build_ok' 'True' '编辑器默认工作台 probe stdout'

$demoProbeStdout = Get-OutputFileText ([string]$validation.demoWindowProbe.stdout) 'Demo 窗口 probe stdout'
Assert-TextContains $demoProbeStdout 'window_frame_probe' 'Demo 窗口 probe stdout'
Assert-TextContains $demoProbeStdout 'PixelEngine.Demo' 'Demo 窗口 probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'attached' 'True' 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'canvases' '3' 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'requested' $requestedDemoRuntimeUiBackend 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'active' $expectedDemoRuntimeUiBackendActive 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'fallback' $expectedDemoRuntimeUiBackendFallback.ToString() 'Demo 窗口 Game UI probe stdout'

$demoBuildResultPath = Resolve-OutputPath ([string]$validation.demoBuildResult) 'demoBuildResult'
$demoBuildResult = Get-Content -Raw -LiteralPath $demoBuildResultPath | ConvertFrom-Json
if ($demoBuildResult.ok -ne $true) {
  throw "demo-build-result.json 不是 ok=true：$($demoBuildResult.error)"
}

if ($demoBuildResult.runtimeUiBackend -ne $requestedDemoRuntimeUiBackend) {
  throw "Demo UI backend 记录不一致。manifest=$requestedDemoRuntimeUiBackend, build-result=$($demoBuildResult.runtimeUiBackend)"
}

$checksumLines = Get-Content -LiteralPath $checksumPath
$relativePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$expectedHashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$checksumCount = 0
foreach ($line in $checksumLines) {
  if ([string]::IsNullOrWhiteSpace($line)) {
    continue
  }

  if ($line -notmatch '^(?<hash>[a-fA-F0-9]{64})\s{2}(?<path>.+)$') {
    throw "SHA256SUMS 行格式错误：$line"
  }

  $expectedHash = $Matches.hash.ToLowerInvariant()
  $relativePath = $Matches.path.Replace('\', '/')
  if (-not $relativePaths.Add($relativePath)) {
    throw "SHA256SUMS 存在重复路径：$relativePath"
  }

  $expectedHashes[$relativePath] = $expectedHash
  $checksumCount++
}

if ($checksumCount -eq 0) {
  throw 'SHA256SUMS 不能为空。'
}

Assert-ChecksumContains $relativePaths $manifestRelative 'manifest'
Assert-ChecksumContains $relativePaths 'README.txt' 'README'
Assert-ChecksumContains $relativePaths ([string]$manifest.editorExecutable) '编辑器入口'
Assert-ChecksumContains $relativePaths ([string]$manifest.demoExecutable) 'Demo 入口'
Assert-ChecksumContains $relativePaths ([string]$validation.demoBuildResult) 'demo-build-result'
foreach ($assemblyName in $requiredScriptReferenceAssemblies) {
  Assert-ChecksumContains $relativePaths "$scriptReferenceAssembliesRelative/$assemblyName.dll" "脚本引用 DLL $assemblyName"
  Assert-ChecksumContains $relativePaths "$scriptReferenceAssembliesRelative/$assemblyName.xml" "脚本引用 XML $assemblyName"
}
foreach ($dependencyFileName in $declaredScriptReferenceManagedDependencies) {
  Assert-ChecksumContains $relativePaths "$scriptReferenceAssembliesRelative/$dependencyFileName" "脚本引用 managed dependency $dependencyFileName"
}

$actualFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Get-ChildItem -LiteralPath $outputRootFull -File -Recurse -Force |
  ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($outputRootFull, $_.FullName).Replace('\', '/')
    if ($relative -ne 'SHA256SUMS') {
      [void]$actualFiles.Add($relative)
    }
  }

foreach ($actual in $actualFiles) {
  if (-not $relativePaths.Contains($actual)) {
    throw "正式输出包含未登记文件：$actual"
  }
}

foreach ($listed in $relativePaths) {
  if (-not $actualFiles.Contains($listed)) {
    throw "SHA256SUMS 登记了不存在的文件：$listed"
  }
}

foreach ($listed in $relativePaths) {
  $filePath = Resolve-OutputPath $listed 'checksum entry'
  Assert-FileExists $filePath "checksum 文件 $listed"
  $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
  $expectedHash = $expectedHashes[$listed]
  if ($actualHash -ne $expectedHash) {
    throw "SHA256 不匹配：$listed expected=$expectedHash actual=$actualHash"
  }
}

$scriptReferencePdb = Get-ChildItem -LiteralPath $scriptReferenceAssembliesRoot -File -Recurse -Force |
  Where-Object { $_.Extension.Equals('.pdb', [StringComparison]::OrdinalIgnoreCase) } |
  Select-Object -First 1
if ($scriptReferencePdb) {
  throw "脚本引用程序集目录绝不允许包含 PDB：$($scriptReferencePdb.FullName)"
}

$expectedScriptReferenceFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($assemblyName in $requiredScriptReferenceAssemblies) {
  [void]$expectedScriptReferenceFiles.Add("$assemblyName.dll")
  [void]$expectedScriptReferenceFiles.Add("$assemblyName.xml")
}
foreach ($dependencyFileName in $declaredScriptReferenceManagedDependencies) {
  [void]$expectedScriptReferenceFiles.Add([string]$dependencyFileName)
}

$actualScriptReferenceFiles = Get-ChildItem -LiteralPath $scriptReferenceAssembliesRoot -File -Recurse -Force
if ($actualScriptReferenceFiles.Count -ne $expectedScriptReferenceFiles.Count) {
  throw "脚本引用程序集文件数量不匹配：expected=$($expectedScriptReferenceFiles.Count), actual=$($actualScriptReferenceFiles.Count)"
}

foreach ($file in $actualScriptReferenceFiles) {
  $relative = [IO.Path]::GetRelativePath($scriptReferenceAssembliesRoot, $file.FullName).Replace('\', '/')
  if (-not $expectedScriptReferenceFiles.Contains($relative)) {
    throw "脚本引用程序集目录包含未规定文件：$relative"
  }
}

if (-not $editorSymbolsIncluded) {
  $scriptReferenceRootPrefix = $scriptReferenceAssembliesRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  $metadata = Get-ChildItem -LiteralPath $editorRoot -File -Recurse -Force |
    Where-Object {
      (-not $_.FullName.StartsWith($scriptReferenceRootPrefix, [StringComparison]::OrdinalIgnoreCase)) -and
      ($_.Extension.Equals('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
       $_.Extension.Equals('.xml', [StringComparison]::OrdinalIgnoreCase))
    }
  if ($metadata) {
    throw "编辑器运行目录不应在脚本引用 SDK 之外包含 .pdb/.xml 开发元数据：$($metadata[0].FullName)"
  }
}

Write-Host "final_output_verify schema=pixelengine.final-output-verify/v1, ok=True, gitCommit=$($manifest.gitCommit), checksum_count=$checksumCount, editor=$($manifest.editorExecutable), demo=$($manifest.demoExecutable)"
