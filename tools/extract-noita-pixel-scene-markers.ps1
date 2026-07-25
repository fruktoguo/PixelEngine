[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-world-content.json'),

    [string] $OutputPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\scripts\NoitaPixelSceneMarkers.Generated.cs')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'NoitaFastLzDecoder.cs')

$utf8NoBom = [Text.UTF8Encoding]::new($false)
$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$catalog = Get-Content -Raw -LiteralPath $CatalogPath | ConvertFrom-Json -Depth 30
[xml]$materialsXml = [IO.File]::ReadAllText((Join-Path $resolvedDataRoot 'materials.xml'))

function Normalize-Rgb([string] $Value) {
    $normalized = $Value.Trim().Replace('0x', '').ToLowerInvariant()
    if ($normalized.Length -eq 8) { return $normalized.Substring(2) }
    if ($normalized.Length -eq 6) { return $normalized }
    throw "Invalid color '$Value'."
}

function Escape-CSharp([string] $Value) {
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

$materialColors = @{}
foreach ($material in $materialsXml.SelectNodes('//*[@name and @wang_color]')) {
    $materialColors[(Normalize-Rgb ([string]$material.wang_color))] = $true
    foreach ($graphics in $material.SelectNodes('.//Graphics[@color]')) {
        $materialColors[(Normalize-Rgb ([string]$graphics.color))] = $true
    }
}

$registrations = @{}
$biomeScriptRoot = Join-Path $resolvedDataRoot 'scripts\biomes'
foreach ($file in Get-ChildItem -LiteralPath $biomeScriptRoot -Filter '*.lua' -Recurse -File) {
    $relativePath = 'data/' + [IO.Path]::GetRelativePath($resolvedDataRoot, $file.FullName).Replace('\', '/')
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if ($line -notmatch '^\s*RegisterSpawnFunction\(\s*0x([0-9A-Fa-f]{8})\s*,\s*"([^"]+)"') {
            continue
        }

        $color = $Matches[1].ToLowerInvariant()
        if (-not $registrations.ContainsKey($color)) {
            $registrations[$color] = [Collections.Generic.List[object]]::new()
        }

        $registrations[$color].Add([pscustomobject]@{
            Function = $Matches[2]
            SourcePath = $relativePath
        })
    }
}

$markers = [Collections.Generic.List[object]]::new()
function Add-Marker($Scene, [int] $SceneIndex, [int] $X, [int] $Y, [string] $Rgb, [string] $Argb, [string] $SourcePath, [bool] $KeepUnresolved) {
    if ($materialColors.ContainsKey($Rgb) -or $Rgb -eq '000000' -or $Rgb -eq 'ffffff') { return }
    $matching = if ($registrations.ContainsKey($Argb)) { @($registrations[$Argb]) } else { @() }
    if (@($matching).Count -eq 0 -and -not $KeepUnresolved) { return }
    $functions = @($matching | ForEach-Object Function | Sort-Object -Unique)
    if ($functions.Count -ne 1 -and -not $KeepUnresolved) { return }
    $sources = @($matching | ForEach-Object SourcePath | Sort-Object -Unique)
    $function = if ($functions.Count -eq 1) { $functions[0] } else { "builtin-or-unresolved:$Argb" }
    $origin = if ($sources.Count -gt 0) { [string]::Join('|', $sources) } else { $SourcePath }
    $markers.Add([pscustomobject]@{
        SceneOrdinal = $SceneIndex
        LocalX = $X
        LocalY = $Y
        WorldX = [int]$Scene.pos_x + $X
        WorldY = [int]$Scene.pos_y + $Y
        Color = $Argb
        Function = $function
        Origin = $origin
    })
}

$scenes = @($catalog.globalPixelScenes.bufferedScenes) + @($catalog.globalPixelScenes.splicedScenes)
for ($sceneIndex = 0; $sceneIndex -lt $scenes.Count; $sceneIndex++) {
    $scene = $scenes[$sceneIndex]
    if ($null -eq $scene.assets.material) { continue }

    $sourcePath = [string]$scene.assets.material.path
    $sourceFile = Join-Path $resolvedDataRoot $sourcePath.Substring(5)
    $extension = [IO.Path]::GetExtension($sourceFile).ToLowerInvariant()
    if ($extension -eq '.plz') {
        $bytes = [IO.File]::ReadAllBytes($sourceFile)
        $encodedWidth = [BitConverter]::ToInt32($bytes, 4)
        $encodedHeight = [BitConverter]::ToInt32($bytes, 8)
        $width = [Math]::Abs($encodedWidth)
        $height = [Math]::Abs($encodedHeight)
        $payloadLength = [BitConverter]::ToInt32($bytes, 16)
        $decodedLength = [BitConverter]::ToInt32($bytes, 20)
        if ($bytes.Length -lt 24 -or [BitConverter]::ToInt32($bytes, 0) -ne 1 -or
            [BitConverter]::ToInt32($bytes, 12) -ne 4 -or
            $payloadLength -ne $bytes.Length - 24 -or $decodedLength -ne $width * $height * 4) {
            throw "Unsupported Noita PLZ material asset: $sourcePath"
        }
        $payload = [byte[]]::new($payloadLength)
        [Array]::Copy($bytes, 24, $payload, 0, $payloadLength)
        $rgba = [PixelEngine.Tools.Noita.NoitaFastLzDecoder]::Decode($payload, $decodedLength)
        for ($sourceIndex = 0; $sourceIndex -lt $width * $height; $sourceIndex++) {
            $offset = $sourceIndex * 4
            $a = $rgba[$offset + 3]
            if ($a -eq 0) { continue }
            $sourceX = $sourceIndex % $width
            $sourceY = [Math]::Floor($sourceIndex / $width)
            $x = $(if ($encodedWidth -lt 0) { $width - 1 - $sourceX } else { $sourceX })
            $y = $(if ($encodedHeight -lt 0) { $height - 1 - $sourceY } else { $sourceY })
            $rgb = '{0:x2}{1:x2}{2:x2}' -f $rgba[$offset], $rgba[$offset + 1], $rgba[$offset + 2]
            $argb = '{0:x2}{1:x2}{2:x2}{3:x2}' -f $a, $rgba[$offset], $rgba[$offset + 1], $rgba[$offset + 2]
            Add-Marker $scene $sceneIndex $x $y $rgb $argb $sourcePath $false
        }
    }
    elseif ($extension -eq '.png') {
        $bitmap = [Drawing.Bitmap]::new($sourceFile)
        try {
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                for ($x = 0; $x -lt $bitmap.Width; $x++) {
                    $pixel = $bitmap.GetPixel($x, $y)
                    if ($pixel.A -eq 0) { continue }
                    $rgb = '{0:x2}{1:x2}{2:x2}' -f $pixel.R, $pixel.G, $pixel.B
                    $argb = '{0:x2}{1:x2}{2:x2}{3:x2}' -f $pixel.A, $pixel.R, $pixel.G, $pixel.B
                    Add-Marker $scene $sceneIndex $x $y $rgb $argb $sourcePath ($sceneIndex -lt 91)
                }
            }
        }
        finally { $bitmap.Dispose() }
    }
    else {
        throw "Unsupported Noita material scene asset: $sourcePath"
    }
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('// <auto-generated />')
$lines.Add('namespace PixelEngine.Demo;')
$lines.Add('')
$lines.Add('internal static partial class NoitaPixelSceneCatalog')
$lines.Add('{')
$lines.Add('    private static readonly NoitaPixelSceneMarkerDefinition[] MarkerValues =')
$lines.Add('    [')
foreach ($marker in $markers) {
    $lines.Add(('        new({0}, {1}, {2}, {3}L, {4}L, "{5}", "{6}", "{7}"),' -f
        $marker.SceneOrdinal,
        $marker.LocalX,
        $marker.LocalY,
        $marker.WorldX,
        $marker.WorldY,
        (Escape-CSharp $marker.Color),
        (Escape-CSharp $marker.Function),
        (Escape-CSharp $marker.Origin)))
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static ReadOnlySpan<NoitaPixelSceneMarkerDefinition> Markers => MarkerValues;')
$lines.Add('}')

[IO.File]::WriteAllLines([IO.Path]::GetFullPath($OutputPath), $lines, $utf8NoBom)
Write-Output "Generated $($markers.Count) global Pixel Scene markers at '$OutputPath'."
