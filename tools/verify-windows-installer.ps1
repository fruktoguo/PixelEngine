param(
  [Parameter(Mandatory)]
  [string]$MsiPath,

  [Parameter(Mandatory)]
  [ValidatePattern('^\d+\.\d+\.\d+$')]
  [string]$ExpectedVersion,

  [string]$ReportPath
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/verify-windows-installer.ps1 requires PowerShell 7+.'
}
if (-not $IsWindows) {
  throw 'MSI verification requires Windows Installer.'
}

$msiFull = [IO.Path]::GetFullPath($MsiPath)
if (-not (Test-Path -LiteralPath $msiFull -PathType Leaf)) {
  throw "MSI does not exist: $msiFull"
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiFull, 0)

function Invoke-MsiQuery([string]$Sql, [int]$Columns) {
  $view = $database.OpenView($Sql)
  try {
    [void]$view.Execute()
    $rows = [Collections.Generic.List[object]]::new()
    while ($true) {
      $record = $view.Fetch()
      if ($null -eq $record) {
        break
      }
      $values = [Collections.Generic.List[string]]::new()
      for ($column = 1; $column -le $Columns; $column++) {
        $value = $record.StringData($column)
        $values.Add([string]$value)
      }
      $rows.Add([pscustomobject]@{ Values = $values.ToArray() })
    }
    return $rows.ToArray()
  }
  finally {
    [void]$view.Close()
  }
}

function Get-MsiProperty([string]$Name) {
  $rows = @(Invoke-MsiQuery "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'" 1)
  if ($rows.Count -ne 1) {
    throw "MSI property is missing or duplicated: $Name"
  }
  return [string]$rows[0].Values[0]
}

function Get-MsiLongName([string]$Value) {
  if ($Value.Contains('|', [StringComparison]::Ordinal)) {
    return $Value.Split('|')[-1]
  }
  return $Value
}

$productName = Get-MsiProperty 'ProductName'
$productVersion = Get-MsiProperty 'ProductVersion'
$manufacturer = Get-MsiProperty 'Manufacturer'
$upgradeCode = Get-MsiProperty 'UpgradeCode'
$arpIcon = Get-MsiProperty 'ARPPRODUCTICON'
if ($productName -ne 'PixelEngine' -or
    $productVersion -ne $ExpectedVersion -or
    $manufacturer -ne 'PixelEngine' -or
    $upgradeCode -ne '{6FCA8784-80DC-4E02-B8F4-93B5677C1E87}' -or
    $arpIcon -ne 'PixelEngineIcon') {
  throw "MSI product identity mismatch: name=$productName version=$productVersion manufacturer=$manufacturer upgradeCode=$upgradeCode icon=$arpIcon"
}

$directories = @(Invoke-MsiQuery 'SELECT `Directory`, `DefaultDir` FROM `Directory`' 2)
$installDirectory = @($directories | Where-Object { $_.Values[0] -eq 'INSTALLFOLDER' })
if ($installDirectory.Count -ne 1 -or (Get-MsiLongName $installDirectory[0].Values[1]) -ne 'PixelEngine') {
  throw 'MSI INSTALLFOLDER is missing or does not default to PixelEngine.'
}

$files = @(Invoke-MsiQuery 'SELECT `FileName` FROM `File`' 1 | ForEach-Object {
  $name = [string]$_.Values[0]
  if ($name.Contains('|', [StringComparison]::Ordinal)) { $name.Split('|')[-1] } else { $name }
})
foreach ($requiredFile in @('PixelEngine.exe', 'PixelEngine.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'LICENSE.txt')) {
  if ($files -notcontains $requiredFile) {
    throw "MSI payload is missing $requiredFile."
  }
}
if ($files -contains 'PixelEngine.Editor.Shell.exe' -or $files -contains 'PixelEngine.Editor.Shell.dll') {
  throw 'MSI payload contains a legacy Shell entrypoint.'
}

$shortcuts = @(Invoke-MsiQuery 'SELECT `Shortcut`, `Directory_`, `Name`, `Target`, `WkDir`, `Icon_` FROM `Shortcut`' 6)
if ($shortcuts.Count -ne 2) {
  throw "MSI must contain exactly two PixelEngine shortcuts; actual=$($shortcuts.Count)"
}
$shortcutDirectories = @($shortcuts | ForEach-Object { $_.Values[1] } | Sort-Object)
if ($shortcutDirectories[0] -ne 'ApplicationProgramsFolder' -or
    $shortcutDirectories[1] -ne 'DesktopFolder') {
  throw "MSI shortcut directories are invalid: $($shortcutDirectories -join ',')"
}
foreach ($shortcut in $shortcuts) {
  if ((Get-MsiLongName $shortcut.Values[2]) -ne 'PixelEngine' -or
      $shortcut.Values[3] -ne '[INSTALLFOLDER]PixelEngine.exe' -or
      $shortcut.Values[4] -ne 'INSTALLFOLDER' -or
      $shortcut.Values[5] -ne 'PixelEngineIcon') {
    throw "MSI shortcut contract mismatch: $($shortcut.Values -join '|')"
  }
}

$registryRows = @(Invoke-MsiQuery 'SELECT `Root`, `Key`, `Name`, `Value` FROM `Registry`' 4)
$installRegistry = @($registryRows | Where-Object {
  $_.Values[0] -eq '1' -and $_.Values[1] -eq 'Software\PixelEngine' -and $_.Values[2] -eq 'Installed'
})
if ($installRegistry.Count -ne 1) {
  throw 'MSI per-user installation registry key is missing.'
}
$installFolderRegistry = @($registryRows | Where-Object {
  $_.Values[0] -eq '1' -and
  $_.Values[1] -eq 'Software\PixelEngine' -and
  $_.Values[2] -eq 'InstallFolder' -and
  $_.Values[3] -eq '[INSTALLFOLDER]'
})
if ($installFolderRegistry.Count -ne 1) {
  throw 'MSI does not persist the selected installation folder.'
}
$installFolderSearch = @(Invoke-MsiQuery "SELECT ``Property``, ``Signature_`` FROM ``AppSearch`` WHERE ``Property``='INSTALLFOLDER'" 2)
if ($installFolderSearch.Count -ne 1) {
  throw 'MSI does not restore INSTALLFOLDER during maintenance and uninstall.'
}
$installFolderLocator = @(Invoke-MsiQuery "SELECT ``Root``, ``Key``, ``Name`` FROM ``RegLocator`` WHERE ``Signature_``='$($installFolderSearch[0].Values[1])'" 3)
if ($installFolderLocator.Count -ne 1 -or
    $installFolderLocator[0].Values[0] -ne '1' -or
    $installFolderLocator[0].Values[1] -ne 'Software\PixelEngine' -or
    $installFolderLocator[0].Values[2] -ne 'InstallFolder') {
  throw 'MSI INSTALLFOLDER registry search contract is invalid.'
}

$mediaRows = @(Invoke-MsiQuery 'SELECT `DiskId`, `LastSequence`, `Cabinet` FROM `Media` ORDER BY `DiskId`' 3)
if ($mediaRows.Count -lt 1) {
  throw 'MSI payload does not contain a media cabinet.'
}
$previousDiskId = 0
$previousLastSequence = 0
foreach ($mediaRow in $mediaRows) {
  $diskId = [int]$mediaRow.Values[0]
  $lastSequence = [int]$mediaRow.Values[1]
  $cabinet = [string]$mediaRow.Values[2]
  if ($diskId -ne ($previousDiskId + 1) -or
      $lastSequence -le $previousLastSequence -or
      -not $cabinet.StartsWith('#', [StringComparison]::Ordinal)) {
    throw "MSI media sequence or embedded cabinet contract is invalid: disk=$diskId lastSequence=$lastSequence cabinet=$cabinet"
  }
  $previousDiskId = $diskId
  $previousLastSequence = $lastSequence
}
if ($previousLastSequence -ne $files.Count) {
  throw "MSI media does not cover every File row: lastSequence=$previousLastSequence files=$($files.Count)"
}

$report = [ordered]@{
  schema = 'pixelengine.windows-installer-verify/v1'
  ok = $true
  productName = $productName
  productVersion = $productVersion
  manufacturer = $manufacturer
  upgradeCode = $upgradeCode
  installDirectory = 'INSTALLFOLDER'
  installDirectoryDefault = 'PixelEngine'
  installDirectoryPersisted = $true
  perUser = $true
  fileCount = $files.Count
  shortcutCount = $shortcuts.Count
  cabinetCount = $mediaRows.Count
  embeddedCabinet = $true
  sha256 = (Get-FileHash -LiteralPath $msiFull -Algorithm SHA256).Hash.ToLowerInvariant()
}
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
  $reportFull = [IO.Path]::GetFullPath($ReportPath)
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $reportFull) | Out-Null
  $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFull -Encoding utf8NoBOM
}
Write-Host "windows_installer_verify schema=pixelengine.windows-installer-verify/v1 ok=True version=$productVersion files=$($files.Count) shortcuts=$($shortcuts.Count) sha256=$($report.sha256)"
