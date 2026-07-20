param(
  [Parameter(Mandatory)]
  [string]$MsiPath,

  [Parameter(Mandatory)]
  [ValidatePattern('^\d+\.\d+\.\d+$')]
  [string]$ExpectedVersion,

  [string]$InstallRoot,

  [string]$ReportPath = 'artifacts/windows-installer-test/report.json'
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/test-windows-installer.ps1 requires PowerShell 7+.'
}
if (-not $IsWindows) {
  throw 'MSI installation testing requires Windows.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/windows-installer-test'))
$testPrefix = $testRoot.TrimEnd(
  [IO.Path]::DirectorySeparatorChar,
  [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$installTestRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'PixelEngine Installer 测试'))
$installTestPrefix = $installTestRoot.TrimEnd(
  [IO.Path]::DirectorySeparatorChar,
  [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$msiFull = [IO.Path]::GetFullPath($MsiPath)
if (-not (Test-Path -LiteralPath $msiFull -PathType Leaf)) {
  throw "MSI does not exist: $msiFull"
}
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
  $InstallRoot = Join-Path $installTestRoot "PixelEngine Test 安装 $([Guid]::NewGuid().ToString('N'))"
}
$installRootFull = [IO.Path]::GetFullPath($InstallRoot)
if (-not $installRootFull.StartsWith($installTestPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "InstallRoot must be inside the isolated installer test root: $installTestRoot"
}
if (Test-Path -LiteralPath $installRootFull) {
  throw "InstallRoot must not already exist: $installRootFull"
}

$runRoot = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
$installLog = Join-Path $runRoot 'install.log'
$uninstallLog = Join-Path $runRoot 'uninstall.log'
$editorStdout = Join-Path $runRoot 'editor.stdout.log'
$editorStderr = Join-Path $runRoot 'editor.stderr.log'
$editorUserData = Join-Path $runRoot 'editor-user-data'
$reportFull = if ([IO.Path]::IsPathRooted($ReportPath)) {
  [IO.Path]::GetFullPath($ReportPath)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $ReportPath))
}
if (-not $reportFull.StartsWith($testPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "ReportPath must be inside the isolated installer test root: $testRoot"
}
New-Item -ItemType Directory -Force -Path $runRoot, (Split-Path -Parent $reportFull) | Out-Null

$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$database = $windowsInstaller.OpenDatabase($msiFull, 0)
function Get-MsiProperty([string]$Name) {
  $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'")
  try {
    [void]$view.Execute()
    $record = $view.Fetch()
    if ($null -eq $record) {
      throw "MSI property is missing: $Name"
    }
    return [string]$record.StringData(1)
  }
  finally {
    [void]$view.Close()
  }
}

function Invoke-Process(
  [string]$FilePath,
  [string[]]$Arguments,
  [string]$StdoutPath,
  [string]$StderrPath,
  [int]$TimeoutMilliseconds = 180000
) {
  $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::Start($startInfo)
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  if (-not $process.WaitForExit($TimeoutMilliseconds)) {
    $process.Kill($true)
    throw "Process timed out: $FilePath"
  }
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $stdout | Set-Content -LiteralPath $StdoutPath -Encoding utf8NoBOM
  $stderr | Set-Content -LiteralPath $StderrPath -Encoding utf8NoBOM
  return $process.ExitCode
}

function Invoke-MsiExec(
  [ValidateSet('install', 'uninstall')]
  [string]$Operation,
  [string]$PackagePath,
  [string]$LogPath,
  [string]$CustomInstallRoot,
  [int]$TimeoutMilliseconds = 600000
) {
  foreach ($path in @($PackagePath, $LogPath, $CustomInstallRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($path) -and $path.Contains('"', [StringComparison]::Ordinal)) {
      throw "MSI test paths cannot contain a double quote: $path"
    }
  }

  $arguments = if ($Operation -eq 'install') {
    "/i `"$PackagePath`" /qn /norestart INSTALLFOLDER=`"$CustomInstallRoot`" /l*v `"$LogPath`""
  } else {
    "/x `"$PackagePath`" /qn /norestart /l*v `"$LogPath`""
  }
  $startInfo = [Diagnostics.ProcessStartInfo]::new('msiexec.exe')
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  # msiexec requires PROPERTY="path with spaces" to remain one raw command-line expression.
  $startInfo.Arguments = $arguments
  $process = [Diagnostics.Process]::Start($startInfo)
  if (-not $process.WaitForExit($TimeoutMilliseconds)) {
    $process.Kill($true)
    throw "Process timed out: msiexec.exe $Operation"
  }
  return $process.ExitCode
}

function Get-ComparablePath([string]$Path) {
  $full = [IO.Path]::GetFullPath($Path)
  $root = [IO.Path]::GetPathRoot($full)
  if ($full.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
    return $full
  }
  return $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

$productCode = Get-MsiProperty 'ProductCode'
$productName = Get-MsiProperty 'ProductName'
$productVersion = Get-MsiProperty 'ProductVersion'
if ($productName -ne 'PixelEngine' -or $productVersion -ne $ExpectedVersion) {
  throw "MSI identity mismatch: name=$productName version=$productVersion"
}

$desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'PixelEngine.lnk'
$programsFolder = [Environment]::GetFolderPath('Programs')
$startMenuDirectory = Join-Path $programsFolder 'PixelEngine'
$startMenuShortcut = Join-Path $startMenuDirectory 'PixelEngine.lnk'
$productStateBefore = [int]$windowsInstaller.ProductState($productCode)
if ($productStateBefore -ne -1) {
  throw "Installer test refuses to overwrite an existing PixelEngine product registration: code=$productCode state=$productStateBefore"
}
foreach ($preexistingPath in @($desktopShortcut, $startMenuShortcut, 'HKCU:\Software\PixelEngine')) {
  if (Test-Path -LiteralPath $preexistingPath) {
    throw "Installer test refuses to overwrite existing PixelEngine state: $preexistingPath"
  }
}

$result = [ordered]@{
  schema = 'pixelengine.windows-installer-install-test/v1'
  ok = $false
  testedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
  msiPath = $msiFull
  msiSha256 = (Get-FileHash -LiteralPath $msiFull -Algorithm SHA256).Hash.ToLowerInvariant()
  productCode = $productCode
  productName = $productName
  productVersion = $productVersion
  installRoot = $installRootFull
  customPathContainsSpace = $installRootFull.Contains(' ', [StringComparison]::Ordinal)
  customPathContainsNonAscii = $installRootFull.ToCharArray().Where({ [int]$_ -gt 127 }).Count -gt 0
  installExitCode = $null
  editorExitCode = $null
  uninstallExitCode = $null
  editorExecutable = $null
  desktopShortcut = $desktopShortcut
  startMenuShortcut = $startMenuShortcut
  productStateBefore = $productStateBefore
  productStateInstalled = $null
  productStateAfter = $null
  installedFilesVerified = $false
  shortcutsVerified = $false
  productRegistrationVerified = $false
  editorLaunchVerified = $false
  installDirectoryRemoved = $false
  shortcutsRemoved = $false
  productRegistrationRemoved = $false
  error = $null
}

$installed = $false
$failure = $null
try {
  $result.installExitCode = Invoke-MsiExec install $msiFull $installLog $installRootFull
  if ($result.installExitCode -notin @(0, 3010)) {
    throw "MSI install failed with exit code $($result.installExitCode)."
  }
  $installed = $true

  $editorExe = Join-Path $installRootFull 'PixelEngine.exe'
  foreach ($requiredFile in @('PixelEngine.exe', 'PixelEngine.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $installRootFull $requiredFile) -PathType Leaf)) {
      throw "Installed payload is missing $requiredFile."
    }
  }
  if ((Get-Item -LiteralPath $editorExe).VersionInfo.ProductName -ne 'PixelEngine') {
    throw 'Installed PixelEngine.exe product metadata is invalid.'
  }
  $result.editorExecutable = $editorExe
  $result.installedFilesVerified = $true

  foreach ($shortcutPath in @($desktopShortcut, $startMenuShortcut)) {
    if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
      throw "Installed shortcut is missing: $shortcutPath"
    }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ((Get-ComparablePath $shortcut.TargetPath) -ne (Get-ComparablePath $editorExe) -or
        (Get-ComparablePath $shortcut.WorkingDirectory) -ne (Get-ComparablePath $installRootFull)) {
      throw "Shortcut target mismatch: shortcut=$shortcutPath target='$($shortcut.TargetPath)' workingDirectory='$($shortcut.WorkingDirectory)'"
    }
  }
  $result.shortcutsVerified = $true

  $result.productStateInstalled = [int]$windowsInstaller.ProductState($productCode)
  $registeredName = [string]$windowsInstaller.ProductInfo($productCode, 'InstalledProductName')
  $registeredVersion = [string]$windowsInstaller.ProductInfo($productCode, 'VersionString')
  $registeredLocation = [string]$windowsInstaller.ProductInfo($productCode, 'InstallLocation')
  if ($result.productStateInstalled -ne 5 -or
      $registeredName -ne 'PixelEngine' -or
      $registeredVersion -ne $ExpectedVersion -or
      (Get-ComparablePath $registeredLocation) -ne (Get-ComparablePath $installRootFull)) {
    throw "Windows Installer product registration is invalid: state=$($result.productStateInstalled) name='$registeredName' version='$registeredVersion' location='$registeredLocation'"
  }
  $result.productRegistrationVerified = $true

  $result.editorExitCode = Invoke-Process $editorExe @(
    '--window-ticks', '12',
    '--no-reopen-last-project',
    '--ephemeral-user-state',
    '--user-data-dir', $editorUserData,
    '--disable-automation') $editorStdout $editorStderr
  if ($result.editorExitCode -ne 0) {
    throw "Installed Editor launch failed with exit code $($result.editorExitCode)."
  }
  $result.editorLaunchVerified = $true
}
catch {
  $failure = $_
  $result.error = $_.Exception.Message
}
finally {
  if ($installed) {
    try {
      $result.uninstallExitCode = Invoke-MsiExec uninstall $msiFull $uninstallLog ''
      if ($result.uninstallExitCode -notin @(0, 1605, 3010)) {
        throw "MSI uninstall failed with exit code $($result.uninstallExitCode)."
      }
    }
    catch {
      if ($null -eq $failure) {
        $failure = $_
        $result.error = $_.Exception.Message
      }
    }
  }

  $result.installDirectoryRemoved = -not (Test-Path -LiteralPath $installRootFull)
  $result.shortcutsRemoved =
    -not (Test-Path -LiteralPath $desktopShortcut) -and
    -not (Test-Path -LiteralPath $startMenuShortcut) -and
    -not (Test-Path -LiteralPath $startMenuDirectory)
  $result.productStateAfter = [int]$windowsInstaller.ProductState($productCode)
  $result.productRegistrationRemoved =
    $result.productStateAfter -eq -1 -and
    -not (Test-Path -LiteralPath 'HKCU:\Software\PixelEngine')
  if (-not $result.installDirectoryRemoved -or
      -not $result.shortcutsRemoved -or
      -not $result.productRegistrationRemoved) {
    if ($null -eq $failure) {
      $failure = [InvalidOperationException]::new('MSI uninstall left PixelEngine files, shortcuts, or product registration state behind.')
      $result.error = $failure.Message
    }
  }
  $result.ok = $null -eq $failure
  $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFull -Encoding utf8NoBOM
}

if ($null -ne $failure) {
  throw $failure
}
Write-Host "windows_installer_install_test schema=pixelengine.windows-installer-install-test/v1 ok=True version=$ExpectedVersion installRoot='$installRootFull' report=$reportFull"
