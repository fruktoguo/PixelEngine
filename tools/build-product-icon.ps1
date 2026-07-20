param(
  [string]$SourceSvg = 'assets/branding/PixelEngine.svg',
  [string]$OutputIcon = 'assets/branding/PixelEngine.ico',
  [string]$BrowserPath
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
  throw 'tools/build-product-icon.ps1 requires PowerShell 7+.'
}

Add-Type -AssemblyName PresentationCore

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceFull = if ([IO.Path]::IsPathRooted($SourceSvg)) {
  [IO.Path]::GetFullPath($SourceSvg)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $SourceSvg))
}
$outputFull = if ([IO.Path]::IsPathRooted($OutputIcon)) {
  [IO.Path]::GetFullPath($OutputIcon)
} else {
  [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputIcon))
}

if (-not (Test-Path -LiteralPath $sourceFull -PathType Leaf)) {
  throw "SVG source does not exist: $sourceFull"
}

if ([string]::IsNullOrWhiteSpace($BrowserPath)) {
  $browserCandidates = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Google\Chrome\Application\chrome.exe'
  )
  $BrowserPath = $browserCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($BrowserPath) -or
    -not (Test-Path -LiteralPath $BrowserPath -PathType Leaf)) {
  throw 'Microsoft Edge or Google Chrome is required to render the SVG source.'
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/product-icon'))
$workRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot ([Guid]::NewGuid().ToString('N'))))
$artifactPrefix = $artifactRoot.TrimEnd(
  [IO.Path]::DirectorySeparatorChar,
  [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $workRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Icon work directory escaped the artifact root: $workRoot"
}

function Get-PngDimension([byte[]]$Bytes, [string]$Path) {
  if ($Bytes.Length -lt 24 -or
      $Bytes[0] -ne 0x89 -or $Bytes[1] -ne 0x50 -or $Bytes[2] -ne 0x4E -or $Bytes[3] -ne 0x47) {
    throw "Rendered icon is not a PNG: $Path"
  }

  $width = ([int]$Bytes[16] -shl 24) -bor ([int]$Bytes[17] -shl 16) -bor
    ([int]$Bytes[18] -shl 8) -bor [int]$Bytes[19]
  $height = ([int]$Bytes[20] -shl 24) -bor ([int]$Bytes[21] -shl 16) -bor
    ([int]$Bytes[22] -shl 8) -bor [int]$Bytes[23]
  return @($width, $height)
}

function Wait-RenderedPng([string]$Path, [int]$ExpectedSize) {
  $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
  do {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
      try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try {
          $bytes = [byte[]]::new($stream.Length)
          $read = $stream.Read($bytes, 0, $bytes.Length)
          if ($read -ne $bytes.Length) {
            throw "Incomplete PNG read: expected=$($bytes.Length), actual=$read"
          }
        }
        finally {
          $stream.Dispose()
        }

        $dimension = Get-PngDimension $bytes $Path
        if ($dimension[0] -eq $ExpectedSize -and $dimension[1] -eq $ExpectedSize) {
          return $bytes
        }
      }
      catch [IO.IOException] {
        # Chromium has created the file but still owns it; retry until the write is complete.
      }
    }
    Start-Sleep -Milliseconds 100
  } while ([DateTimeOffset]::UtcNow -lt $deadline)

  throw "Timed out waiting for complete ${ExpectedSize}x${ExpectedSize} PNG: $Path"
}

function Assert-PngCoverage([byte[]]$Bytes, [int]$ExpectedSize) {
  $stream = [IO.MemoryStream]::new($Bytes, $false)
  try {
    $decoder = [Windows.Media.Imaging.PngBitmapDecoder]::new(
      $stream,
      [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
      [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $frame = $decoder.Frames[0]
    $bitmap = [Windows.Media.Imaging.FormatConvertedBitmap]::new(
      $frame,
      [Windows.Media.PixelFormats]::Bgra32,
      $null,
      0)
    $stride = $ExpectedSize * 4
    $pixels = [byte[]]::new($stride * $ExpectedSize)
    $bitmap.CopyPixels($pixels, $stride, 0)
  }
  finally {
    $stream.Dispose()
  }

  $opaque = 0
  $transparent = 0
  for ($index = 3; $index -lt $pixels.Length; $index += 4) {
    if ($pixels[$index] -ge 248) {
      $opaque++
    }
    if ($pixels[$index] -le 7) {
      $transparent++
    }
  }

  $pixelCount = $ExpectedSize * $ExpectedSize
  if ($opaque -lt [Math]::Max(1, [int]($pixelCount * 0.25)) -or
      $transparent -lt [Math]::Max(1, [int]($pixelCount * 0.01))) {
    throw "Rendered PNG alpha coverage is invalid: size=$ExpectedSize opaque=$opaque transparent=$transparent"
  }
}

function Resize-Png([byte[]]$Bytes, [int]$Size) {
  $input = [IO.MemoryStream]::new($Bytes, $false)
  try {
    $decoder = [Windows.Media.Imaging.PngBitmapDecoder]::new(
      $input,
      [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
      [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $frame = $decoder.Frames[0]
  }
  finally {
    $input.Dispose()
  }

  $scaleX = $Size / [double]$frame.PixelWidth
  $scaleY = $Size / [double]$frame.PixelHeight
  $scaled = [Windows.Media.Imaging.TransformedBitmap]::new(
    $frame,
    [Windows.Media.ScaleTransform]::new($scaleX, $scaleY))
  $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
  $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($scaled))
  $output = [IO.MemoryStream]::new()
  try {
    $encoder.Save($output)
    return $output.ToArray()
  }
  finally {
    $output.Dispose()
  }
}

$entries = [Collections.Generic.List[object]]::new()
try {
  New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
  $svgUri = [Uri]::new($sourceFull).AbsoluteUri
  $renderSize = 512
  $htmlPath = Join-Path $workRoot 'icon-source.html'
  $pngPath = Join-Path $workRoot 'icon-source.png'
  @"
<!doctype html>
<html><head><meta charset="utf-8"><style>
html,body{width:100%;height:100%;margin:0;overflow:hidden;background:transparent}
img{display:block;width:100%;height:100%}
</style></head><body><img src="$svgUri" alt="" onload="document.documentElement.dataset.ready='true'"></body></html>
"@ | Set-Content -LiteralPath $htmlPath -Encoding utf8NoBOM

  [byte[]]$sourcePngBytes = $null
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    try {
      Remove-Item -LiteralPath $pngPath -Force -ErrorAction SilentlyContinue
      $arguments = @(
        '--headless=new',
        '--disable-gpu',
        '--disable-background-networking',
        '--no-first-run',
        '--hide-scrollbars',
        '--run-all-compositor-stages-before-draw',
        '--virtual-time-budget=1000',
        '--default-background-color=00000000',
        '--force-device-scale-factor=1',
        "--window-size=$renderSize,$renderSize",
        "--user-data-dir=$(Join-Path $workRoot "browser-$attempt")",
        "--screenshot=$pngPath",
        ([Uri]::new($htmlPath).AbsoluteUri)
      )
      $startInfo = [Diagnostics.ProcessStartInfo]::new($BrowserPath)
      $startInfo.UseShellExecute = $false
      $startInfo.CreateNoWindow = $true
      $startInfo.RedirectStandardOutput = $true
      $startInfo.RedirectStandardError = $true
      foreach ($argument in $arguments) {
        $startInfo.ArgumentList.Add($argument)
      }
      $process = [Diagnostics.Process]::Start($startInfo)
      $stdoutTask = $process.StandardOutput.ReadToEndAsync()
      $stderrTask = $process.StandardError.ReadToEndAsync()
      if (-not $process.WaitForExit(30000)) {
        $process.Kill($true)
        throw 'Browser SVG render timed out.'
      }
      $stdout = $stdoutTask.GetAwaiter().GetResult()
      $stderr = $stderrTask.GetAwaiter().GetResult()
      if ($process.ExitCode -ne 0) {
        throw "Browser SVG render failed: exit=$($process.ExitCode) stdout=$stdout stderr=$stderr"
      }

      $sourcePngBytes = Wait-RenderedPng $pngPath $renderSize
      Assert-PngCoverage $sourcePngBytes $renderSize
      break
    }
    catch {
      if ($attempt -eq 3) {
        throw
      }
    }
  }

  foreach ($size in $sizes) {
    [byte[]]$pngBytes = Resize-Png $sourcePngBytes $size
    $dimension = Get-PngDimension $pngBytes "generated-$size.png"
    if ($dimension[0] -ne $size -or $dimension[1] -ne $size) {
      throw "Generated PNG dimension mismatch: expected=${size}x${size}, actual=$($dimension[0])x$($dimension[1])"
    }
    Assert-PngCoverage $pngBytes $size
    $entries.Add([pscustomobject]@{ Size = $size; Bytes = $pngBytes })
  }

  $outputDirectory = Split-Path -Parent $outputFull
  New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
  $temporaryIcon = "$outputFull.tmp"
  $stream = [IO.File]::Open($temporaryIcon, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
  try {
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$entries.Count)
    $offset = 6 + (16 * $entries.Count)
    foreach ($entry in $entries) {
      $dimensionByte = if ($entry.Size -eq 256) { [byte]0 } else { [byte]$entry.Size }
      $writer.Write($dimensionByte)
      $writer.Write($dimensionByte)
      $writer.Write([byte]0)
      $writer.Write([byte]0)
      $writer.Write([uint16]1)
      $writer.Write([uint16]32)
      $writer.Write([uint32]$entry.Bytes.Length)
      $writer.Write([uint32]$offset)
      $offset += $entry.Bytes.Length
    }
    foreach ($entry in $entries) {
      $writer.Write([byte[]]$entry.Bytes)
    }
  }
  finally {
    if ($null -ne $writer) {
      $writer.Dispose()
    } else {
      $stream.Dispose()
    }
  }

  Move-Item -LiteralPath $temporaryIcon -Destination $outputFull -Force
  Write-Host "product_icon schema=pixelengine.product-icon/v1 ok=True layers=$($entries.Count) output=$outputFull"
}
finally {
  if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
  }
}
