param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [ValidatePattern('^\d+\.\d+\.\d+$')]
  [string]$Version,

  [string]$OutputRoot = 'artifacts/windows-installer/current',

  [string]$EditorPublishRoot,

  [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/build-windows-installer.ps1 requires PowerShell 7+.'
}
if (-not $IsWindows) {
  throw 'The PixelEngine MSI can only be built on Windows.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repoPrefix = $repoRoot.TrimEnd(
  [IO.Path]::DirectorySeparatorChar,
  [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

function Resolve-RepoPath([string]$Path) {
  if ([IO.Path]::IsPathRooted($Path)) {
    return [IO.Path]::GetFullPath($Path)
  }
  return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-UnderRepo([string]$Path, [string]$Label) {
  $full = [IO.Path]::GetFullPath($Path)
  if (-not $full.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$Label must be inside the repository: $full"
  }
}

function Invoke-Checked([string]$Label, [string]$FilePath, [string[]]$Arguments) {
  Write-Host "[$Label] $FilePath $($Arguments -join ' ')"
  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "$Label failed with exit code $LASTEXITCODE."
  }
}

function Copy-Directory([string]$Source, [string]$Destination) {
  if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Directory does not exist: $Source"
  }
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

function Get-StableId([string]$Prefix, [string]$Value) {
  $bytes = [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
  $hash = [Security.Cryptography.SHA256]::HashData($bytes)
  return $Prefix + [Convert]::ToHexString($hash).Substring(0, 24)
}

function Get-StableGuid([string]$Value) {
  $bytes = [Text.Encoding]::UTF8.GetBytes("PixelEngine/Installer/Component/v1/$($Value.ToLowerInvariant())")
  $characters = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).Substring(0, 32).ToCharArray()
  $characters[12] = '5'
  $characters[16] = '8'
  $hex = -join $characters
  return "{$($hex.Substring(0, 8))-$($hex.Substring(8, 4))-$($hex.Substring(12, 4))-$($hex.Substring(16, 4))-$($hex.Substring(20, 12))}"
}

function Write-WixPayloadFragment([string]$PayloadRoot, [string]$OutputPath) {
  $payloadPrefix = $PayloadRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  $files = @(Get-ChildItem -LiteralPath $PayloadRoot -File -Recurse -Force | Sort-Object FullName)
  if ($files.Count -eq 0) {
    throw 'Installer payload is empty.'
  }

  $relativeFiles = [Collections.Generic.List[object]]::new()
  $directorySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($file in $files) {
    $full = [IO.Path]::GetFullPath($file.FullName)
    if (-not $full.StartsWith($payloadPrefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Payload file escaped the payload root: $full"
    }
    $relative = [IO.Path]::GetRelativePath($PayloadRoot, $full).Replace('\', '/')
    $directoryName = [IO.Path]::GetDirectoryName($relative)
    $directory = if ([string]::IsNullOrWhiteSpace($directoryName)) {
      ''
    } else {
      $directoryName.Replace('\', '/')
    }
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
      $ancestor = $directory
      while (-not [string]::IsNullOrWhiteSpace($ancestor)) {
        [void]$directorySet.Add($ancestor)
        $ancestorSeparator = $ancestor.LastIndexOf('/')
        $ancestor = if ($ancestorSeparator -lt 0) { '' } else { $ancestor.Substring(0, $ancestorSeparator) }
      }
    }
    $relativeFiles.Add([pscustomobject]@{
      FullPath = $full
      RelativePath = $relative
      DirectoryPath = $directory
    })
  }

  $ns = [Xml.Linq.XNamespace]::Get('http://wixtoolset.org/schemas/v4/wxs')
  $wix = [Xml.Linq.XElement]::new($ns + 'Wix')
  $directoryFragment = [Xml.Linq.XElement]::new($ns + 'Fragment')
  $directoryRef = [Xml.Linq.XElement]::new($ns + 'DirectoryRef')
  $directoryRef.SetAttributeValue('Id', 'INSTALLFOLDER')
  $directoryFragment.Add($directoryRef)
  $wix.Add($directoryFragment)

  $directoryElements = [Collections.Generic.Dictionary[string, Xml.Linq.XElement]]::new(
    [StringComparer]::OrdinalIgnoreCase)
  $directoryElements.Add('', $directoryRef)
  $directories = @($directorySet) | Sort-Object `
    @{ Expression = { ($_ -split '/').Count } }, `
    @{ Expression = { $_ } }
  foreach ($directory in $directories) {
    $separator = $directory.LastIndexOf('/')
    $parent = if ($separator -lt 0) { '' } else { $directory.Substring(0, $separator) }
    $name = if ($separator -lt 0) { $directory } else { $directory.Substring($separator + 1) }
    $element = [Xml.Linq.XElement]::new($ns + 'Directory')
    $element.SetAttributeValue('Id', (Get-StableId 'Dir' $directory))
    $element.SetAttributeValue('Name', $name)
    $directoryElements[$parent].Add($element)
    $directoryElements.Add($directory, $element)
  }

  $componentFragment = [Xml.Linq.XElement]::new($ns + 'Fragment')
  $componentGroup = [Xml.Linq.XElement]::new($ns + 'ComponentGroup')
  $componentGroup.SetAttributeValue('Id', 'EditorPayload')
  $componentFragment.Add($componentGroup)
  $wix.Add($componentFragment)
  foreach ($entry in $relativeFiles) {
    $stableSuffix = Get-StableId '' $entry.RelativePath
    $component = [Xml.Linq.XElement]::new($ns + 'Component')
    $component.SetAttributeValue('Id', "Cmp$stableSuffix")
    $componentDirectory = if ([string]::IsNullOrWhiteSpace($entry.DirectoryPath)) {
      'INSTALLFOLDER'
    } else {
      Get-StableId 'Dir' $entry.DirectoryPath
    }
    $component.SetAttributeValue('Directory', $componentDirectory)
    $component.SetAttributeValue('Guid', (Get-StableGuid "file/$($entry.RelativePath)"))
    $file = [Xml.Linq.XElement]::new($ns + 'File')
    $file.SetAttributeValue('Id', "Fil$stableSuffix")
    $file.SetAttributeValue('Source', $entry.FullPath)
    $file.SetAttributeValue('KeyPath', 'no')
    $file.SetAttributeValue('Checksum', 'yes')
    $component.Add($file)
    $registryValue = [Xml.Linq.XElement]::new($ns + 'RegistryValue')
    $registryValue.SetAttributeValue('Root', 'HKCU')
    $registryValue.SetAttributeValue('Key', 'Software\PixelEngine\Components')
    $registryValue.SetAttributeValue('Name', $stableSuffix)
    $registryValue.SetAttributeValue('Type', 'integer')
    $registryValue.SetAttributeValue('Value', '1')
    $registryValue.SetAttributeValue('KeyPath', 'yes')
    $component.Add($registryValue)
    $componentGroup.Add($component)
  }

  foreach ($directory in $directories) {
    $stableSuffix = Get-StableId '' "directory/$directory"
    $cleanupComponent = [Xml.Linq.XElement]::new($ns + 'Component')
    $cleanupComponent.SetAttributeValue('Id', "Cln$stableSuffix")
    $cleanupComponent.SetAttributeValue('Directory', (Get-StableId 'Dir' $directory))
    $cleanupComponent.SetAttributeValue('Guid', (Get-StableGuid "directory/$directory"))
    $removeFolder = [Xml.Linq.XElement]::new($ns + 'RemoveFolder')
    $removeFolder.SetAttributeValue('Id', "Rmv$stableSuffix")
    $removeFolder.SetAttributeValue('Directory', (Get-StableId 'Dir' $directory))
    $removeFolder.SetAttributeValue('On', 'uninstall')
    $cleanupComponent.Add($removeFolder)
    $registryValue = [Xml.Linq.XElement]::new($ns + 'RegistryValue')
    $registryValue.SetAttributeValue('Root', 'HKCU')
    $registryValue.SetAttributeValue('Key', 'Software\PixelEngine\Directories')
    $registryValue.SetAttributeValue('Name', $stableSuffix)
    $registryValue.SetAttributeValue('Type', 'integer')
    $registryValue.SetAttributeValue('Value', '1')
    $registryValue.SetAttributeValue('KeyPath', 'yes')
    $cleanupComponent.Add($registryValue)
    $componentGroup.Add($cleanupComponent)
  }

  $document = [Xml.Linq.XDocument]::new([Xml.Linq.XDeclaration]::new('1.0', 'utf-8', $null), $wix)
  $settings = [Xml.XmlWriterSettings]::new()
  $settings.Encoding = [Text.UTF8Encoding]::new($false)
  $settings.Indent = $true
  $writer = [Xml.XmlWriter]::Create($OutputPath, $settings)
  try {
    $document.Save($writer)
  }
  finally {
    $writer.Dispose()
  }
  return $relativeFiles
}

function Replace-Output([string]$SourceRoot, [string]$DestinationRoot) {
  Assert-UnderRepo $SourceRoot 'Installer staging output'
  Assert-UnderRepo $DestinationRoot 'Installer output'
  $newRoot = "$DestinationRoot.__new"
  $backupRoot = "$DestinationRoot.__previous"
  Assert-UnderRepo $newRoot 'Installer new output'
  Assert-UnderRepo $backupRoot 'Installer backup output'
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $DestinationRoot) | Out-Null
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

if ([string]::IsNullOrWhiteSpace($Version)) {
  [xml]$buildProps = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
  $Version = ([string]$buildProps.Project.PropertyGroup.VersionPrefix).Trim()
}
if ($Version -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<build>\d+)$' -or
    [int]$Matches.major -gt 255 -or [int]$Matches.minor -gt 255 -or [int]$Matches.build -gt 65535) {
  throw "Version is not a valid MSI product version: $Version"
}

$outputRootFull = Resolve-RepoPath $OutputRoot
Assert-UnderRepo $outputRootFull 'Installer output'
$stagingRoot = Join-Path $repoRoot "artifacts/windows-installer-staging/$([Guid]::NewGuid().ToString('N'))"
Assert-UnderRepo $stagingRoot 'Installer staging root'
$payloadRoot = Join-Path $stagingRoot 'payload'
$buildOutput = Join-Path $stagingRoot 'build'
$nextOutput = Join-Path $stagingRoot 'next-output'
$payloadFragment = Join-Path $stagingRoot 'Payload.generated.wxs'

$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source
$editorProject = Join-Path $repoRoot 'apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj'
$setupProject = Join-Path $repoRoot 'installer/PixelEngine.Setup/PixelEngine.Setup.wixproj'
$nativeBuildScript = Join-Path $repoRoot 'tools/build-native.ps1'
$verifier = Join-Path $repoRoot 'tools/verify-windows-installer.ps1'

try {
  New-Item -ItemType Directory -Force -Path $payloadRoot, $buildOutput, $nextOutput | Out-Null
  if ([string]::IsNullOrWhiteSpace($EditorPublishRoot)) {
    if (-not $SkipNativeBuild) {
      Invoke-Checked 'native-build' $pwsh @(
        '-NoLogo', '-NoProfile', '-File', $nativeBuildScript,
        '-Rid', 'win-x64', '-Configuration', $Configuration)
    }
    Invoke-Checked 'editor-publish' $dotnet @(
      'publish', $editorProject,
      '-c', $Configuration,
      '-r', 'win-x64',
      '--self-contained', 'true',
      '--disable-build-servers',
      '-p:PublishReadyToRun=true',
      '-p:PublishReadyToRunComposite=true',
      '-p:PublishSingleFile=false',
      '-p:PublishTrimmed=false',
      '-o', $payloadRoot)
  }
  else {
    $publishRootFull = [IO.Path]::GetFullPath($EditorPublishRoot)
    Copy-Directory $publishRootFull $payloadRoot
  }

  Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force |
    Where-Object { $_.Extension -in @('.pdb', '.xml') } |
    Remove-Item -Force
  Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $payloadRoot 'LICENSE.txt') -Force

  foreach ($requiredFile in @('PixelEngine.exe', 'PixelEngine.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot $requiredFile) -PathType Leaf)) {
      throw "Installer payload is not self-contained or is missing $requiredFile."
    }
  }
  $legacyEntrypoint = Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force |
    Where-Object { $_.Name -in @('PixelEngine.Editor.Shell.exe', 'PixelEngine.Editor.Shell.dll') } |
    Select-Object -First 1
  if ($legacyEntrypoint) {
    throw "Installer payload contains a legacy Shell entrypoint: $($legacyEntrypoint.FullName)"
  }
  $versionInfo = (Get-Item -LiteralPath (Join-Path $payloadRoot 'PixelEngine.exe')).VersionInfo
  if ($versionInfo.ProductName -ne 'PixelEngine' -or $versionInfo.FileDescription -ne 'PixelEngine') {
    throw 'PixelEngine.exe does not contain the expected product resources.'
  }

  $payloadFiles = Write-WixPayloadFragment $payloadRoot $payloadFragment
  Invoke-Checked 'msi-build' $dotnet @(
    'build', $setupProject,
    '-c', 'Release',
    '--disable-build-servers',
    '-m:1',
    "-p:ProductVersion=$Version",
    "-p:GeneratedPayloadWxs=$payloadFragment",
    '-o', $buildOutput)

  $msiName = "PixelEngine-Setup-$Version-win-x64.msi"
  $builtMsi = Join-Path $buildOutput $msiName
  if (-not (Test-Path -LiteralPath $builtMsi -PathType Leaf)) {
    throw "WiX build did not create the expected MSI: $builtMsi"
  }
  $msiDestination = Join-Path $nextOutput $msiName
  Copy-Item -LiteralPath $builtMsi -Destination $msiDestination -Force

  $staticReportPath = Join-Path $nextOutput 'verification.json'
  Invoke-Checked 'msi-verify' $pwsh @(
    '-NoLogo', '-NoProfile', '-File', $verifier,
    '-MsiPath', $msiDestination,
    '-ExpectedVersion', $Version,
    '-ReportPath', $staticReportPath)

  $gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
  if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[a-f0-9]{40}$') {
    throw 'Unable to resolve the source Git commit.'
  }
  $trackedStatus = (& git -C $repoRoot status --porcelain --untracked-files=no) -join "`n"
  $manifest = [ordered]@{
    schema = 'pixelengine.windows-installer/v1'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitCommit = $gitCommit
    sourceTrackedWorktreeClean = [string]::IsNullOrWhiteSpace($trackedStatus)
    productName = 'PixelEngine'
    version = $Version
    rid = 'win-x64'
    editorExecutable = 'PixelEngine.exe'
    editorSelfContained = $true
    editorReadyToRun = $true
    installerType = 'msi'
    installerToolchain = 'WixToolset.Sdk/4.0.6'
    installerFile = $msiName
    installerSha256 = (Get-FileHash -LiteralPath $msiDestination -Algorithm SHA256).Hash.ToLowerInvariant()
    payloadFileCount = $payloadFiles.Count
    signed = $false
    verificationReport = 'verification.json'
  }
  $manifestPath = Join-Path $nextOutput 'manifest.json'
  $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
  $checksumPath = Join-Path $nextOutput 'SHA256SUMS'
  @($msiName, 'manifest.json', 'verification.json') |
    ForEach-Object {
      $hash = (Get-FileHash -LiteralPath (Join-Path $nextOutput $_) -Algorithm SHA256).Hash.ToLowerInvariant()
      "$hash  $_"
    } | Set-Content -LiteralPath $checksumPath -Encoding ascii

  Replace-Output $nextOutput $outputRootFull
  Write-Host "windows_installer_build schema=pixelengine.windows-installer-build/v1 ok=True version=$Version payloadFiles=$($payloadFiles.Count) output=$outputRootFull"
}
finally {
  if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
  }
}
