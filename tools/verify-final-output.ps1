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

function Assert-LowerSha256([string]$Value, [string]$Label) {
  if ($Value -cnotmatch '^[a-f0-9]{64}$') {
    throw "$Label 必须是 64 位小写 SHA256：$Value"
  }
}

function Get-TextSha256([string]$Value) {
  $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
  try {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
  }
  finally {
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)
  }
}

function Resolve-ContainedPath([string]$Root, [string]$RelativePath, [string]$Label) {
  if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
    throw "$Label 必须是非空相对路径：$RelativePath"
  }

  $rootFull = [IO.Path]::GetFullPath($Root)
  $rootPrefix = $rootFull.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  $full = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
  if (-not $full.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label 逃逸其权威根目录：$RelativePath"
  }

  return $full
}

function Test-OrdinalContains([object[]]$Values, [string]$Expected) {
  foreach ($value in $Values) {
    if ([string]::Equals([string]$value, $Expected, [StringComparison]::Ordinal)) {
      return $true
    }
  }

  return $false
}

function Assert-NuGetPackageEntries(
  [string]$Path,
  [string]$Label,
  [string[]]$RequiredEntries
) {
  $archive = [IO.Compression.ZipFile]::OpenRead($Path)
  try {
    $entries = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $archive.Entries) {
      $name = $entry.FullName.Replace('\', '/')
      if ([string]::IsNullOrWhiteSpace($name) -or $name.EndsWith('/', [StringComparison]::Ordinal)) {
        continue
      }

      if ($name.StartsWith('/', [StringComparison]::Ordinal) -or
          ($name -split '/') -contains '..') {
        throw "$Label 包含非法 archive entry：$name"
      }

      if (-not $entries.Add($name)) {
        throw "$Label 包含重复 archive entry：$name"
      }

      if ($name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label 不得包含 PDB：$name"
      }
    }

    foreach ($requiredEntry in $RequiredEntries) {
      if (-not $entries.Contains($requiredEntry)) {
        throw "$Label 缺少 package entry：$requiredEntry"
      }
    }
  }
  finally {
    $archive.Dispose()
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
        $assemblyName.Equals('PixelEngine.Editor', [StringComparison]::OrdinalIgnoreCase) -or
        $assemblyName.StartsWith('PixelEngine.Editor.', [StringComparison]::OrdinalIgnoreCase)) {
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
$expectedDemoWindowMode = [string]$manifest.demoWindowMode
if ($expectedDemoWindowMode -ne 'Windowed') {
  throw "manifest demoWindowMode 必须是正式输出默认 Windowed：$expectedDemoWindowMode"
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

$automation = $manifest.automation
if ($null -eq $automation) {
  throw 'manifest 缺少 automation 节点。'
}

$automationCliRelative = [string]$automation.cliExecutable
$automationProtocolPackageRelative = [string]$automation.protocolPackage
$automationClientPackageRelative = [string]$automation.clientPackage
$automationProtocolSchemaRelative = [string]$automation.protocolSchema
$automationCapabilitySchemaRelative = [string]$automation.capabilitySchema
$automationCapabilityMatrixRelative = [string]$automation.capabilityMatrix
$automationDocumentationRelative = [string]$automation.documentation
$automationSkillRootRelative = [string]$automation.skillRoot
if ($automationCliRelative -ne '自动化/CLI/pixelengine-editor.exe' -or
    $automationProtocolSchemaRelative -ne '自动化/Schema/editor-automation-protocol.v1.schema.json' -or
    $automationCapabilitySchemaRelative -ne '自动化/Schema/editor-automation-capabilities.schema.json' -or
    $automationCapabilityMatrixRelative -ne '自动化/Schema/editor-automation-capabilities.v1.json' -or
    $automationDocumentationRelative -ne '自动化/文档/editor-automation-api.md' -or
    $automationSkillRootRelative -ne '自动化/Skill/pixelengine-editor') {
  throw 'manifest automation 的固定发行路径不匹配。'
}

if ($automationProtocolPackageRelative -cnotmatch
      '^自动化/SDK/PixelEngine\.Editor\.Automation\.Protocol\.[0-9A-Za-z][0-9A-Za-z.-]*\.nupkg$' -or
    $automationClientPackageRelative -cnotmatch
      '^自动化/SDK/PixelEngine\.Editor\.Automation\.Client\.[0-9A-Za-z][0-9A-Za-z.-]*\.nupkg$') {
  throw 'manifest automation SDK package 路径或文件名非法。'
}

$automationCli = Resolve-OutputPath $automationCliRelative 'automation cliExecutable'
$automationProtocolPackage = Resolve-OutputPath $automationProtocolPackageRelative 'automation protocolPackage'
$automationClientPackage = Resolve-OutputPath $automationClientPackageRelative 'automation clientPackage'
$automationProtocolSchema = Resolve-OutputPath $automationProtocolSchemaRelative 'automation protocolSchema'
$automationCapabilitySchema = Resolve-OutputPath $automationCapabilitySchemaRelative 'automation capabilitySchema'
$automationCapabilityMatrix = Resolve-OutputPath $automationCapabilityMatrixRelative 'automation capabilityMatrix'
$automationDocumentation = Resolve-OutputPath $automationDocumentationRelative 'automation documentation'
$automationSkillRoot = Resolve-OutputPath $automationSkillRootRelative 'automation skillRoot'
foreach ($automationFile in @(
    $automationCli,
    $automationProtocolPackage,
    $automationClientPackage,
    $automationProtocolSchema,
    $automationCapabilitySchema,
    $automationCapabilityMatrix,
    $automationDocumentation)) {
  Assert-FileExists $automationFile 'automation 发行文件'
}
if (-not (Test-Path -LiteralPath $automationSkillRoot -PathType Container)) {
  throw "automation Skill 根目录不存在：$automationSkillRoot"
}

$expectedCliMetadataPolicy = if ($editorSymbolsIncluded) {
  'included-for-diagnostics'
} else {
  'runtime-pdb-and-xml-pruned'
}
if ($automation.cliDeveloperMetadataPolicy -ne $expectedCliMetadataPolicy) {
  throw "manifest automation CLI metadata policy 不匹配：$($automation.cliDeveloperMetadataPolicy)"
}

$automationCliRoot = Split-Path -Parent $automationCli
foreach ($requiredCliFile in @(
    'pixelengine-editor.dll',
    'PixelEngine.Editor.Automation.Client.dll',
    'PixelEngine.Editor.Automation.Protocol.dll')) {
  Assert-FileExists (Join-Path $automationCliRoot $requiredCliFile) "automation CLI runtime $requiredCliFile"
}
if (-not $editorSymbolsIncluded) {
  $cliMetadata = Get-ChildItem -LiteralPath $automationCliRoot -File -Recurse -Force |
    Where-Object {
      $_.Extension.Equals('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
      $_.Extension.Equals('.xml', [StringComparison]::OrdinalIgnoreCase)
    } |
    Select-Object -First 1
  if ($cliMetadata) {
    throw "automation CLI 运行目录不应包含 .pdb/.xml 开发元数据：$($cliMetadata.FullName)"
  }
}

$automationSdkRoot = Split-Path -Parent $automationProtocolPackage
$actualAutomationPackages = @(Get-ChildItem -LiteralPath $automationSdkRoot -File -Force)
if ($actualAutomationPackages.Count -ne 2 -or
    -not (Test-Path -LiteralPath $automationClientPackage -PathType Leaf)) {
  throw "automation SDK 目录必须精确包含 Protocol/Client 两个 nupkg：actual=$($actualAutomationPackages.Count)"
}
Assert-NuGetPackageEntries `
  $automationProtocolPackage `
  'automation Protocol nupkg' `
  @(
    'lib/net10.0/PixelEngine.Editor.Automation.Protocol.dll',
    'schema/editor-automation-protocol.v1.schema.json'
  )
Assert-NuGetPackageEntries `
  $automationClientPackage `
  'automation Client nupkg' `
  @('lib/net10.0/PixelEngine.Editor.Automation.Client.dll')

$expectedSkillFiles = @(
  'SKILL.md',
  'agents/openai.yaml',
  'references/workflows.md',
  'scripts/invoke.ps1'
)
$declaredSkillFiles = @($automation.skillFiles)
$declaredSkillFileSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($skillFileValue in $declaredSkillFiles) {
  $skillFile = ([string]$skillFileValue).Replace('\', '/')
  if (-not $declaredSkillFileSet.Add($skillFile)) {
    throw "manifest automation skillFiles 包含重复项：$skillFile"
  }
}
if ($declaredSkillFileSet.Count -ne $expectedSkillFiles.Count) {
  throw "manifest automation skillFiles 数量不匹配：expected=$($expectedSkillFiles.Count), actual=$($declaredSkillFileSet.Count)"
}
foreach ($expectedSkillFile in $expectedSkillFiles) {
  if (-not $declaredSkillFileSet.Contains($expectedSkillFile)) {
    throw "manifest automation skillFiles 缺少：$expectedSkillFile"
  }
}
$actualSkillFiles = @(Get-ChildItem -LiteralPath $automationSkillRoot -File -Recurse -Force)
if ($actualSkillFiles.Count -ne $expectedSkillFiles.Count) {
  throw "automation Skill 文件数量不匹配：expected=$($expectedSkillFiles.Count), actual=$($actualSkillFiles.Count)"
}
foreach ($actualSkillFile in $actualSkillFiles) {
  $relative = [IO.Path]::GetRelativePath($automationSkillRoot, $actualSkillFile.FullName).Replace('\', '/')
  if (-not $declaredSkillFileSet.Contains($relative)) {
    throw "automation Skill 包含未规定文件：$relative"
  }
}
$skillText = Get-Content -Raw -LiteralPath (Join-Path $automationSkillRoot 'SKILL.md')
$skillInvokerText = Get-Content -Raw -LiteralPath (Join-Path $automationSkillRoot 'scripts/invoke.ps1')
Assert-TextContains $skillText 'name: pixelengine-editor' 'automation Skill'
Assert-TextContains $skillText 'scripts/invoke.ps1' 'automation Skill'
Assert-TextContains $skillInvokerText '& $cliPath @args' 'automation Skill CLI wrapper'
if ($skillInvokerText.Contains('NamedPipeClientStream', [StringComparison]::Ordinal) -or
    $skillInvokerText.Contains('Invoke-WebRequest', [StringComparison]::OrdinalIgnoreCase) -or
    $skillInvokerText.Contains('Start-Process', [StringComparison]::OrdinalIgnoreCase)) {
  throw 'automation Skill wrapper 必须直接调用 CLI，不得自行实现 transport 或旁路进程。'
}

$capabilitySchemaDocument = Get-Content -Raw -LiteralPath $automationCapabilitySchema | ConvertFrom-Json -Depth 100
if ($capabilitySchemaDocument.'$ref' -ne
    'editor-automation-protocol.v1.schema.json#/$defs/capabilityMatrixSnapshot') {
  throw 'automation capability Schema 未引用发布的 protocol capabilityMatrixSnapshot。'
}
$protocolSchemaDocument = Get-Content -Raw -LiteralPath $automationProtocolSchema | ConvertFrom-Json -Depth 100
$protocolDefinitions = $protocolSchemaDocument.PSObject.Properties['$defs'].Value
if ($null -eq $protocolDefinitions) {
  throw 'automation protocol Schema 缺少 $defs。'
}

$matrixText = Get-Content -Raw -LiteralPath $automationCapabilityMatrix
$matrixDocument = [Text.Json.JsonDocument]::Parse($matrixText)
try {
  $capabilityArrayText = $matrixDocument.RootElement.GetProperty('capabilities').GetRawText()
  $uiCommandArrayText = $matrixDocument.RootElement.GetProperty('uiCommands').GetRawText()
}
finally {
  $matrixDocument.Dispose()
}
$matrixSnapshot = $matrixText | ConvertFrom-Json -Depth 100
$computedCapabilityDigest = Get-TextSha256 $capabilityArrayText
$computedUiCommandDigest = Get-TextSha256 $uiCommandArrayText
$computedMatrixDigest = Get-TextSha256 (
  "v1`n$computedCapabilityDigest`n$computedUiCommandDigest`n")
foreach ($digestEntry in @(
    @('capabilityDigest', [string]$matrixSnapshot.capabilityDigest, $computedCapabilityDigest),
    @('uiCommandDigest', [string]$matrixSnapshot.uiCommandDigest, $computedUiCommandDigest),
    @('matrixDigest', [string]$matrixSnapshot.matrixDigest, $computedMatrixDigest))) {
  Assert-LowerSha256 $digestEntry[1] "automation matrix $($digestEntry[0])"
  if ($digestEntry[1] -cne $digestEntry[2]) {
    throw "automation matrix $($digestEntry[0]) canonical SHA256 不匹配。"
  }
}

$matrixCapabilities = @($matrixSnapshot.capabilities)
$matrixUiCommands = @($matrixSnapshot.uiCommands)
if ($matrixSnapshot.schemaVersion -ne 1 -or
    $matrixCapabilities.Count -lt 150 -or
    $matrixUiCommands.Count -lt 300 -or
    $automation.capabilityCount -ne $matrixCapabilities.Count -or
    $automation.uiCommandCount -ne $matrixUiCommands.Count -or
    $automation.capabilityDigest -cne $computedCapabilityDigest -or
    $automation.uiCommandDigest -cne $computedUiCommandDigest -or
    $automation.matrixDigest -cne $computedMatrixDigest) {
  throw 'manifest automation matrix schema、count 或 digest 与发布矩阵不一致。'
}

$capabilityById = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$previousCapabilityId = $null
foreach ($capability in $matrixCapabilities) {
  $capabilityId = [string]$capability.id
  if ([string]::IsNullOrWhiteSpace($capabilityId) -or
      ($null -ne $previousCapabilityId -and [string]::CompareOrdinal($previousCapabilityId, $capabilityId) -ge 0) -or
      -not $capabilityById.TryAdd($capabilityId, $capability)) {
    throw "automation matrix capability ID 未严格排序或重复：$capabilityId"
  }
  foreach ($schemaReference in @([string]$capability.requestSchema, [string]$capability.responseSchema)) {
    if (-not $schemaReference.StartsWith('#/$defs/', [StringComparison]::Ordinal)) {
      throw "automation capability $capabilityId 的 Schema ref 非法：$schemaReference"
    }
    $definitionName = $schemaReference.Substring('#/$defs/'.Length)
    if ($null -eq $protocolDefinitions.PSObject.Properties[$definitionName]) {
      throw "automation capability $capabilityId 引用了不存在的 Schema：$schemaReference"
    }
  }
  $previousCapabilityId = $capabilityId
}

$uiCommandById = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$previousUiCommandId = $null
foreach ($uiCommand in $matrixUiCommands) {
  $uiCommandId = [string]$uiCommand.id
  $capabilityIds = @($uiCommand.capabilityIds)
  if ([string]::IsNullOrWhiteSpace($uiCommandId) -or
      ($null -ne $previousUiCommandId -and [string]::CompareOrdinal($previousUiCommandId, $uiCommandId) -ge 0) -or
      -not $uiCommandById.TryAdd($uiCommandId, $uiCommand) -or
      $capabilityIds.Count -eq 0) {
    throw "automation matrix UI command ID 未严格排序、重复或无 capability：$uiCommandId"
  }
  $previousLinkedCapabilityId = $null
  foreach ($linkedCapabilityIdValue in $capabilityIds) {
    $linkedCapabilityId = [string]$linkedCapabilityIdValue
    if (($null -ne $previousLinkedCapabilityId -and
         [string]::CompareOrdinal($previousLinkedCapabilityId, $linkedCapabilityId) -ge 0) -or
        -not $capabilityById.ContainsKey($linkedCapabilityId) -or
        -not (Test-OrdinalContains @($capabilityById[$linkedCapabilityId].uiCommandIds) $uiCommandId)) {
      throw "automation UI command $uiCommandId 的 capability 引用不存在、未排序或不对称：$linkedCapabilityId"
    }
    $previousLinkedCapabilityId = $linkedCapabilityId
  }
  $previousUiCommandId = $uiCommandId
}
foreach ($capability in $matrixCapabilities) {
  foreach ($linkedUiCommandIdValue in @($capability.uiCommandIds)) {
    $linkedUiCommandId = [string]$linkedUiCommandIdValue
    if (-not $uiCommandById.ContainsKey($linkedUiCommandId) -or
        -not (Test-OrdinalContains @($uiCommandById[$linkedUiCommandId].capabilityIds) ([string]$capability.id))) {
      throw "automation capability $($capability.id) 的 UI command 引用不存在或不对称：$linkedUiCommandId"
    }
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

$editorGameViewValidation = $validation.editorGameViewPresentationProbe
if ($null -eq $editorGameViewValidation -or
    $editorGameViewValidation.completed -ne $true -or
    $editorGameViewValidation.allPassed -ne $true -or
    $editorGameViewValidation.scenarioCount -ne 6 -or
    $editorGameViewValidation.uiStackLifecycle -ne '1->0->1') {
  throw '编辑器 Game View presentation probe 记录不是六场景 1->0->1 通过状态。'
}

$automationE2EValidation = $validation.editorAutomationE2E
if ($null -eq $automationE2EValidation -or
    $automationE2EValidation.completed -ne $true -or
    $automationE2EValidation.allPassed -ne $true -or
    $automationE2EValidation.cliOnly -ne $true -or
    [int]$automationE2EValidation.externalCliProcessCount -lt 35 -or
    [int]$automationE2EValidation.requiredScopeCount -ne 10 -or
    [int]$automationE2EValidation.skippedCount -ne 0) {
  throw 'Editor automation E2E 记录不是无跳过的外部 CLI 全链路通过状态。'
}

if ($validation.demoWindowProbe.completed -ne $true) {
  throw 'Demo 窗口 probe 记录不是完成状态。'
}
if ($validation.demoWindowProbe.unicodePath -ne $true) {
  throw 'Demo 窗口 probe 必须从含非 ASCII 字符的发布路径运行。'
}
if ($validation.demoWindowProbe.requestedMode -ne $expectedDemoWindowMode -or
    $validation.demoWindowProbe.applied -ne $true) {
  throw 'Demo 窗口 probe 的请求模式或实际应用状态不匹配。'
}

$validationPaths = @(
  [string]$validation.editorDefaultWorkbenchProbe.stdout,
  [string]$validation.editorDefaultWorkbenchProbe.stderr,
  [string]$validation.editorDefaultWorkbenchProbe.capture,
  [string]$editorGameViewValidation.stdout,
  [string]$editorGameViewValidation.stderr,
  [string]$editorGameViewValidation.report,
  [string]$automationE2EValidation.stdout,
  [string]$automationE2EValidation.stderr,
  [string]$automationE2EValidation.report,
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

$editorGameViewWrapperStdout = Get-OutputFileText ([string]$editorGameViewValidation.stdout) '编辑器 Game View presentation probe stdout'
Assert-TextContains $editorGameViewWrapperStdout 'pixelengine.editor-gameview-presentation-probe/v1' '编辑器 Game View presentation probe stdout'
$editorGameViewReportPath = Resolve-OutputPath ([string]$editorGameViewValidation.report) '编辑器 Game View presentation probe report'
$editorGameViewReport = Get-Content -Raw -LiteralPath $editorGameViewReportPath | ConvertFrom-Json
if ($editorGameViewReport.schema -ne 'pixelengine.editor-gameview-presentation-probe/v1' -or
    $editorGameViewReport.allPassed -ne $true -or
    $editorGameViewReport.gitCommit -ne $manifest.gitCommit) {
  throw '编辑器 Game View presentation probe 报告 schema、allPassed 或 gitCommit 不匹配。'
}

$requiredGameViewScenarios = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($scenarioName in @(
  'aspect-16-9',
  'aspect-4-3',
  'aspect-9-16',
  'resolution-1920-1080',
  'maximize-on-play',
  'narrow-toolbar'
)) {
  [void]$requiredGameViewScenarios.Add($scenarioName)
}

$reportedGameViewScenarios = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$editorGameViewReportRoot = [IO.Path]::GetFullPath((Split-Path -Parent $editorGameViewReportPath))
$editorGameViewReportRootPrefix = $editorGameViewReportRoot.TrimEnd(
  [IO.Path]::DirectorySeparatorChar,
  [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$editorGameViewScenarios = @($editorGameViewReport.scenarios)
if ($editorGameViewScenarios.Count -ne $requiredGameViewScenarios.Count) {
  throw "编辑器 Game View presentation probe 场景数量不匹配：expected=$($requiredGameViewScenarios.Count), actual=$($editorGameViewScenarios.Count)"
}

foreach ($scenario in $editorGameViewScenarios) {
  $scenarioName = [string]$scenario.name
  if (-not $requiredGameViewScenarios.Contains($scenarioName) -or
      -not $reportedGameViewScenarios.Add($scenarioName)) {
    throw "编辑器 Game View presentation probe 场景未知或重复：$scenarioName"
  }

  $summary = $scenario.summary
  $expectedToolbarDensity = if ($scenarioName -eq 'narrow-toolbar') { 'Narrow' } else { 'Full' }
  $requiredSummaryValues = [ordered]@{
    completed = 'True'
    first_ui_stack_depth = '1'
    first_play_exited = 'True'
    exit_ui_stack_depth = '0'
    second_play_entered = 'True'
    second_ui_stack_depth = '1'
    second_controller_found = 'True'
    second_controller_enabled = 'True'
    second_controller_faulted = 'False'
    second_play_ui_restored = 'True'
    presentation_synchronized = 'True'
    toolbar_density = $expectedToolbarDensity
    toolbar_fits = 'True'
    toolbar_overflow_visible = 'True'
  }
  foreach ($entry in $requiredSummaryValues.GetEnumerator()) {
    $actual = [string]$summary.($entry.Key)
    if ($actual -ne [string]$entry.Value) {
      throw "编辑器 Game View presentation probe 场景 $scenarioName 字段不匹配：$($entry.Key) expected=$($entry.Value) actual=$actual"
    }
  }

  $framebufferRelative = [string]$scenario.framebuffer.path
  if ([string]::IsNullOrWhiteSpace($framebufferRelative) -or [IO.Path]::IsPathRooted($framebufferRelative)) {
    throw "编辑器 Game View presentation probe framebuffer path 非法：$scenarioName/$framebufferRelative"
  }
  $framebufferPath = [IO.Path]::GetFullPath((Join-Path $editorGameViewReportRoot $framebufferRelative))
  if (-not $framebufferPath.StartsWith($editorGameViewReportRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "编辑器 Game View presentation probe framebuffer 逃逸报告目录：$scenarioName/$framebufferRelative"
  }
  Assert-FileExists $framebufferPath "编辑器 Game View presentation framebuffer $scenarioName"
  $expectedFramebufferHash = ([string]$scenario.framebuffer.sha256).ToLowerInvariant()
  if ($expectedFramebufferHash -notmatch '^[a-f0-9]{64}$') {
    throw "编辑器 Game View presentation probe framebuffer SHA256 非法：$scenarioName"
  }
  $actualFramebufferHash = (Get-FileHash -LiteralPath $framebufferPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualFramebufferHash -ne $expectedFramebufferHash) {
    throw "编辑器 Game View presentation framebuffer SHA256 不匹配：$scenarioName"
  }
}

$automationE2EWrapperStdout = Get-OutputFileText `
  ([string]$automationE2EValidation.stdout) `
  'Editor automation E2E stdout'
Assert-TextContains `
  $automationE2EWrapperStdout `
  'automation_e2e schema=pixelengine.editor-automation-e2e/v1' `
  'Editor automation E2E stdout'
$automationE2EWrapperStderr = Get-OutputFileText `
  ([string]$automationE2EValidation.stderr) `
  'Editor automation E2E stderr'
if (-not [string]::IsNullOrWhiteSpace($automationE2EWrapperStderr)) {
  throw 'Editor automation E2E wrapper stderr 必须为空。'
}

$automationE2EReportPath = Resolve-OutputPath `
  ([string]$automationE2EValidation.report) `
  'Editor automation E2E report'
$automationE2EReportRoot = [IO.Path]::GetFullPath((Split-Path -Parent $automationE2EReportPath))
$automationE2EReport = Get-Content -Raw -LiteralPath $automationE2EReportPath | ConvertFrom-Json -Depth 100
$automationE2EOperations = @($automationE2EReport.operations)
$automationE2ERequiredScopes = @($automationE2EReport.requiredScopes)
$automationE2EProcessCount = [int]$automationE2EReport.externalCliProcessCount
if ($automationE2EReport.schema -ne 'pixelengine.editor-automation-e2e/v1' -or
    $automationE2EReport.gitCommit -ne $manifest.gitCommit -or
    $automationE2EReport.allPassed -ne $true -or
    $automationE2EReport.cliOnly -ne $true -or
    $automationE2EReport.externalEditorProcess -ne $true -or
    @($automationE2EReport.skipped).Count -ne 0 -or
    $automationE2EProcessCount -lt 35 -or
    $automationE2EProcessCount -ne [int]$automationE2EValidation.externalCliProcessCount -or
    $automationE2EProcessCount -ne [int]$automationE2EReport.cli.processCount -or
    $automationE2EOperations.Count -ne $automationE2EProcessCount) {
  throw 'Editor automation E2E 报告身份、外部进程数量或无跳过状态不匹配。'
}

$expectedAutomationScopeIds = @(
  'discover-and-capability-matrix',
  'transaction-and-undo-redo',
  'scene-authoring-and-save',
  'first-play-runtime-console-profiler-pause-step-stop',
  'second-play-runtime-stop',
  'post-play-modify-and-save',
  'artifact-sha256',
  'build',
  'player-launch-verify-terminate',
  'editor-public-exit-and-discovery-cleanup'
)
$reportedAutomationScopeIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($scope in $automationE2ERequiredScopes) {
  $scopeId = [string]$scope.id
  if ($scope.status -ne 'passed' -or -not $reportedAutomationScopeIds.Add($scopeId)) {
    throw "Editor automation E2E scope 未通过或重复：$scopeId"
  }
}
if ($reportedAutomationScopeIds.Count -ne $expectedAutomationScopeIds.Count) {
  throw "Editor automation E2E scope 数量不匹配：expected=$($expectedAutomationScopeIds.Count), actual=$($reportedAutomationScopeIds.Count)"
}
foreach ($scopeId in $expectedAutomationScopeIds) {
  if (-not $reportedAutomationScopeIds.Contains($scopeId)) {
    throw "Editor automation E2E 缺少必需 scope：$scopeId"
  }
}

$packagedEditorSha256 = (Get-FileHash -LiteralPath $editorExe -Algorithm SHA256).Hash.ToLowerInvariant()
$packagedCliSha256 = (Get-FileHash -LiteralPath $automationCli -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-LowerSha256 ([string]$automationE2EReport.editor.executableSha256) 'Editor automation E2E editor hash'
Assert-LowerSha256 ([string]$automationE2EReport.cli.executableSha256) 'Editor automation E2E CLI hash'
if ($automationE2EReport.editor.executableSha256 -cne $packagedEditorSha256 -or
    $automationE2EReport.cli.executableSha256 -cne $packagedCliSha256 -or
    [int]$automationE2EReport.editor.processId -le 0 -or
    [int]$automationE2EReport.editor.exitCode -ne 0 -or
    $automationE2EReport.editor.descriptorRemoved -ne $true -or
    [string]::IsNullOrWhiteSpace([string]$automationE2EReport.cli.clientInstanceId)) {
  throw 'Editor automation E2E 未绑定发布的 Editor/CLI，或进程退出与 discovery 清理证据无效。'
}

$automationEditorStdout = Resolve-ContainedPath `
  $automationE2EReportRoot `
  ([string]$automationE2EReport.editor.stdout) `
  'Editor automation E2E editor stdout'
$automationEditorStderr = Resolve-ContainedPath `
  $automationE2EReportRoot `
  ([string]$automationE2EReport.editor.stderr) `
  'Editor automation E2E editor stderr'
Assert-FileExists $automationEditorStdout 'Editor automation E2E editor stdout'
Assert-FileExists $automationEditorStderr 'Editor automation E2E editor stderr'
if ($automationE2EReport.editor.allowedStderr -ne
    'libpng-iCCP-known-incorrect-sRGB-profile-only') {
  throw 'Editor automation E2E editor.allowedStderr 策略不匹配。'
}
$automationEditorStderrLines = @(
  (Get-Content -LiteralPath $automationEditorStderr) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
foreach ($stderrLine in $automationEditorStderrLines) {
  if ($stderrLine -cne 'libpng warning: iCCP: known incorrect sRGB profile') {
    throw "Editor automation E2E Editor stderr 含未允许诊断：$stderrLine"
  }
}
if ($automationEditorStderrLines.Count -ne [int]$automationE2EReport.editor.allowedStderrCount -or
    $automationEditorStderrLines.Count -ne [int]$automationE2EReport.workflow.allowedLibPngWarningCount) {
  throw 'Editor automation E2E Editor stderr 计数与报告不匹配。'
}

$workflow = $automationE2EReport.workflow
if ([int]$workflow.capabilityCount -ne $matrixCapabilities.Count -or
    [int]$workflow.uiCommandCount -ne $matrixUiCommands.Count -or
    $workflow.capabilityDigest -cne $computedCapabilityDigest -or
    $workflow.uiCommandDigest -cne $computedUiCommandDigest -or
    $workflow.matrixDigest -cne $computedMatrixDigest -or
    $workflow.transactionFailureRollback -ne $true -or
    $workflow.transactionUndoRedo -ne $true -or
    $workflow.modifiedAndSavedAfterSecondPlay -ne $true -or
    [int]$workflow.firstRuntimeEntityCount -le 0 -or
    [int]$workflow.secondRuntimeEntityCount -le 0 -or
    [int]$workflow.consoleErrorCount -ne 0 -or
    $workflow.buildState -ne 'Succeeded' -or
    $workflow.playerRunningVerified -ne $true -or
    $workflow.playerTerminated -ne $true -or
    [int]$workflow.playerProcessIdObserved -le 0 -or
    $workflow.exitStatus -ne 'executed') {
  throw 'Editor automation E2E 工作流矩阵、事务、运行态、构建、Player 或退出状态不匹配。'
}
if ([string]::IsNullOrWhiteSpace([string]$workflow.firstPlaySessionId) -or
    [string]::IsNullOrWhiteSpace([string]$workflow.secondPlaySessionId) -or
    [string]::Equals(
      [string]$workflow.firstPlaySessionId,
      [string]$workflow.secondPlaySessionId,
      [StringComparison]::Ordinal) -or
    [string]::IsNullOrWhiteSpace([string]$workflow.buildId) -or
    [string]::IsNullOrWhiteSpace([string]$workflow.playerProcessId)) {
  throw 'Editor automation E2E Play session、build 或 player stable ID 无效。'
}
foreach ($workflowHash in @(
    @('sceneCaptureSha256', [string]$workflow.sceneCaptureSha256),
    @('gameCaptureSha256', [string]$workflow.gameCaptureSha256),
    @('buildPackageSha256', [string]$workflow.buildPackageSha256),
    @('launcherSha256', [string]$workflow.launcherSha256),
    @('buildLogSha256', [string]$workflow.buildLogSha256))) {
  Assert-LowerSha256 $workflowHash[1] "Editor automation E2E $($workflowHash[0])"
}

$requiredAutomationOperationNames = @(
  'capability-matrix',
  'transaction-execute-rollback',
  'hierarchy-after-rollback',
  'transaction-execute',
  'history-undo',
  'history-redo',
  'scene-capture',
  'play-enter-first',
  'play-pause',
  'play-step',
  'play-stop-first',
  'game-capture',
  'play-enter-second',
  'play-stop-second',
  'marker-transform-set',
  'scene-save-after-play',
  'build-settings-set',
  'build-preflight',
  'build-start-wait',
  'build-logs',
  'player-launch',
  'player-get-running',
  'player-terminate',
  'workspace-exit',
  'discover-after-exit'
)
$reportedAutomationOperationNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$automationE2ELogRelativePaths = [Collections.Generic.List[string]]::new()
$automationOperationLogPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
for ($operationIndex = 0; $operationIndex -lt $automationE2EOperations.Count; $operationIndex++) {
  $operation = $automationE2EOperations[$operationIndex]
  $operationName = [string]$operation.name
  $expectedSequence = $operationIndex + 1
  $allowedExitCodes = @($operation.allowedExitCodes)
  $expectedOutcome = if ([int]$operation.exitCode -eq 0) { 'passed' } else { 'accepted-nonzero' }
  [void]$reportedAutomationOperationNames.Add($operationName)
  if ([int]$operation.sequence -ne $expectedSequence -or
      [string]::IsNullOrWhiteSpace($operationName) -or
      [int]$operation.processId -le 0 -or
      [long]$operation.durationMilliseconds -lt 0 -or
      $allowedExitCodes.Count -eq 0 -or
      $allowedExitCodes -notcontains [int]$operation.exitCode -or
      $operation.outcome -ne $expectedOutcome) {
    throw "Editor automation E2E operation 记录无效：sequence=$expectedSequence, name=$operationName"
  }

  foreach ($streamName in @('stdout', 'stderr')) {
    $relativeLog = [string]$operation.$streamName
    if (-not $automationOperationLogPaths.Add($relativeLog)) {
      throw "Editor automation E2E operation 日志路径重复：$relativeLog"
    }
    $logPath = Resolve-ContainedPath `
      $automationE2EReportRoot `
      $relativeLog `
      "Editor automation E2E operation $operationName $streamName"
    Assert-FileExists $logPath "Editor automation E2E operation $operationName $streamName"
    $expectedLogHash = [string]$operation.("${streamName}Sha256")
    Assert-LowerSha256 $expectedLogHash "Editor automation E2E operation $operationName $streamName hash"
    $actualLogHash = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualLogHash -cne $expectedLogHash) {
      throw "Editor automation E2E operation $operationName $streamName SHA256 不匹配。"
    }
    $automationE2ELogRelativePaths.Add(
      [IO.Path]::GetRelativePath($outputRootFull, $logPath).Replace('\', '/'))
  }

  $operationStdoutPath = Resolve-ContainedPath `
    $automationE2EReportRoot `
    ([string]$operation.stdout) `
    "Editor automation E2E operation $operationName stdout"
  $operationStdoutText = Get-Content -Raw -LiteralPath $operationStdoutPath
  if ([string]::IsNullOrWhiteSpace($operationStdoutText)) {
    throw "Editor automation E2E operation $operationName stdout 不能为空。"
  }
  try {
    [void]($operationStdoutText | ConvertFrom-Json -Depth 100)
  }
  catch {
    throw "Editor automation E2E operation $operationName stdout 不是合法 JSON。"
  }
}
foreach ($requiredOperationName in $requiredAutomationOperationNames) {
  if (-not $reportedAutomationOperationNames.Contains($requiredOperationName)) {
    throw "Editor automation E2E 缺少必需 operation：$requiredOperationName"
  }
}

$demoProbeStdout = Get-OutputFileText ([string]$validation.demoWindowProbe.stdout) 'Demo 窗口 probe stdout'
Assert-TextContains $demoProbeStdout 'window_frame_probe' 'Demo 窗口 probe stdout'
Assert-TextContains $demoProbeStdout 'PixelEngine.Demo' 'Demo 窗口 probe stdout'
Assert-SummaryValue $demoProbeStdout 'player_window_probe ' 'requested' $expectedDemoWindowMode 'Demo Player window probe stdout'
Assert-SummaryValue $demoProbeStdout 'player_window_probe ' 'available' 'True' 'Demo Player window probe stdout'
Assert-SummaryValue $demoProbeStdout 'player_window_probe ' 'applied' 'True' 'Demo Player window probe stdout'
Assert-SummaryValue $demoProbeStdout 'player_window_probe ' 'reason' 'none' 'Demo Player window probe stdout'
Assert-SummaryValue $demoProbeStdout 'player_window_probe ' 'visible' 'True' 'Demo Player window probe stdout'
Assert-SummaryValue $demoProbeStdout 'player_window_probe ' 'presentation' '1080x720' 'Demo Player window probe stdout'
$windowedClientMatches = ($demoProbeStdout -split "`r?`n" | Where-Object {
  $_.StartsWith('player_window_probe ', [StringComparison]::Ordinal)
} | Select-Object -Last 1)
if (-not $windowedClientMatches -or
    (-not $windowedClientMatches.Contains('client_matches_presentation=True', [StringComparison]::Ordinal) -and
      -not $windowedClientMatches.Contains('presentation_fits_work=False', [StringComparison]::Ordinal))) {
  throw 'Demo Player Windowed 客户区既未匹配 Presentation，也不是因 work area 不足而合法夹取。'
}
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'attached' 'True' 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'canvases' '3' 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'requested' $requestedDemoRuntimeUiBackend 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'active' $expectedDemoRuntimeUiBackendActive 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'fallback' $expectedDemoRuntimeUiBackendFallback.ToString() 'Demo 窗口 Game UI probe stdout'
Assert-SummaryValue $demoProbeStdout 'game_ui_probe ' 'content_path_non_ascii' 'True' 'Demo 窗口 Game UI probe stdout'

$demoBuildResultPath = Resolve-OutputPath ([string]$validation.demoBuildResult) 'demoBuildResult'
$demoBuildResult = Get-Content -Raw -LiteralPath $demoBuildResultPath | ConvertFrom-Json
if ($demoBuildResult.ok -ne $true) {
  throw "demo-build-result.json 不是 ok=true：$($demoBuildResult.error)"
}

if ($demoBuildResult.runtimeUiBackend -ne $requestedDemoRuntimeUiBackend) {
  throw "Demo UI backend 记录不一致。manifest=$requestedDemoRuntimeUiBackend, build-result=$($demoBuildResult.runtimeUiBackend)"
}
if ($demoBuildResult.windowMode -ne $expectedDemoWindowMode) {
  throw "Demo WindowMode 记录不一致。manifest=$expectedDemoWindowMode, build-result=$($demoBuildResult.windowMode)"
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
Assert-ChecksumContains $relativePaths ([string]$editorGameViewValidation.report) '编辑器 Game View presentation report'
Assert-ChecksumContains $relativePaths $automationCliRelative 'automation CLI'
Assert-ChecksumContains $relativePaths $automationProtocolPackageRelative 'automation Protocol nupkg'
Assert-ChecksumContains $relativePaths $automationClientPackageRelative 'automation Client nupkg'
Assert-ChecksumContains $relativePaths $automationProtocolSchemaRelative 'automation protocol Schema'
Assert-ChecksumContains $relativePaths $automationCapabilitySchemaRelative 'automation capability Schema'
Assert-ChecksumContains $relativePaths $automationCapabilityMatrixRelative 'automation capability matrix'
Assert-ChecksumContains $relativePaths $automationDocumentationRelative 'automation documentation'
Assert-ChecksumContains $relativePaths ([string]$automationE2EValidation.stdout) 'automation E2E stdout'
Assert-ChecksumContains $relativePaths ([string]$automationE2EValidation.stderr) 'automation E2E stderr'
Assert-ChecksumContains $relativePaths ([string]$automationE2EValidation.report) 'automation E2E report'
Assert-ChecksumContains `
  $relativePaths `
  ([IO.Path]::GetRelativePath($outputRootFull, $automationEditorStdout).Replace('\', '/')) `
  'automation E2E Editor stdout'
Assert-ChecksumContains `
  $relativePaths `
  ([IO.Path]::GetRelativePath($outputRootFull, $automationEditorStderr).Replace('\', '/')) `
  'automation E2E Editor stderr'
foreach ($skillFile in $expectedSkillFiles) {
  Assert-ChecksumContains `
    $relativePaths `
    "$automationSkillRootRelative/$skillFile" `
    "automation Skill $skillFile"
}
foreach ($automationLogRelative in $automationE2ELogRelativePaths) {
  Assert-ChecksumContains $relativePaths $automationLogRelative 'automation E2E operation log'
}
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

Write-Host "final_output_verify schema=pixelengine.final-output-verify/v1, ok=True, gitCommit=$($manifest.gitCommit), checksum_count=$checksumCount, editor=$($manifest.editorExecutable), demo=$($manifest.demoExecutable), capabilities=$($matrixCapabilities.Count), uiCommands=$($matrixUiCommands.Count), automationCliProcesses=$automationE2EProcessCount"
