[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-world-content.json'),

    [string] $OutputPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\scripts\NoitaPixelSceneMasks.Generated.cs')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'NoitaFastLzDecoder.cs')

$utf8NoBom = [Text.UTF8Encoding]::new($false)
$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$catalog = Get-Content -Raw -LiteralPath $CatalogPath | ConvertFrom-Json -Depth 30
[xml]$materialsXml = [IO.File]::ReadAllText((Join-Path $resolvedDataRoot 'materials.xml'))
$unmappedMaterials = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Normalize-Color([string] $Value) {
    $normalized = $Value.Trim().Replace('0x', '').ToLowerInvariant()
    if ($normalized.Length -eq 6) { return $normalized }
    if ($normalized.Length -eq 8) { return $normalized.Substring(2) }
    throw "Invalid material color '$Value'."
}

function Add-Alias([hashtable] $Aliases, [string] $Color, [string] $Name, [int] $Priority) {
    if ([string]::IsNullOrWhiteSpace($Color)) { return }
    $key = Normalize-Color $Color
    if (-not $Aliases.ContainsKey($key) -or $Priority -gt $Aliases[$key].Priority) {
        $Aliases[$key] = [pscustomobject]@{ Name = $Name; Priority = $Priority }
    }
}

function Resolve-MaterialCode([string] $Name) {
    $value = $Name.ToLowerInvariant()
    if ($value -match 'air|void') { return 0 }
    if ($value -match '^water') { return 3 }
    if ($value -match '^lava') { return 14 }
    if ($value -eq 'blood') { return 15 }
    if ($value -eq 'bone_static') { return 16 }
    if ($value -eq 'cheese_static') { return 17 }
    if ($value -eq 'mud') { return 18 }
    if ($value -eq 'sand_petrify') { return 19 }
    if ($value -eq 'snow_sticky') { return 20 }
    if ($value -match 'steel|metal') { return 5 }
    if ($value -match 'wood') { return 6 }
    if ($value -match 'templebrick') { return 8 }
    if ($value -match 'glowstone|crystal') { return 9 }
    if ($value -match 'cloud|smoke') { return 10 }
    if ($value -match '^ice') { return 11 }
    if ($value -eq 'snow') { return 12 }
    if ($value -match 'snow_static|sand_static') { return 7 }
    if ($value -match 'soil|grass') { return 1 }
    if ($value -match 'rock|sandstone|snowrock') { return 4 }
    if ($value -match 'trailer_text|glass') { return 13 }
    [void]$unmappedMaterials.Add($Name)
    return 0
}

function Compress-Brotli([byte[]] $Bytes) {
    $output = [IO.MemoryStream]::new()
    try {
        $brotli = [IO.Compression.BrotliStream]::new($output, [IO.Compression.CompressionLevel]::Optimal, $true)
        try { $brotli.Write($Bytes, 0, $Bytes.Length) }
        finally { $brotli.Dispose() }
        return $output.ToArray()
    }
    finally { $output.Dispose() }
}

$aliases = @{}
foreach ($material in $materialsXml.SelectNodes('//*[@name and @wang_color]')) {
    $name = [string]$material.name
    Add-Alias $aliases ([string]$material.wang_color) $name 2
    foreach ($graphics in $material.SelectNodes('.//Graphics[@color]')) {
        Add-Alias $aliases ([string]$graphics.color) $name 1
    }
}

$definitions = [Collections.Generic.List[object]]::new()
$bufferedScenes = @($catalog.globalPixelScenes.bufferedScenes) + @($catalog.globalPixelScenes.splicedScenes)
for ($sceneIndex = 0; $sceneIndex -lt $bufferedScenes.Count; $sceneIndex++) {
    $scene = $bufferedScenes[$sceneIndex]
    if ($null -eq $scene.assets.material) { continue }
    $sourcePath = [string]$scene.material_filename
    $sourceFile = Join-Path $resolvedDataRoot $sourcePath.Substring(5)
    $extension = [IO.Path]::GetExtension($sourceFile).ToLowerInvariant()
    $bitmap = $null
    try {
        if ($extension -eq '.plz') {
            $bytes = [IO.File]::ReadAllBytes($sourceFile)
            if ($bytes.Length -lt 24 -or [BitConverter]::ToInt32($bytes, 0) -ne 1 -or
                [BitConverter]::ToInt32($bytes, 12) -ne 4) {
                throw "Unsupported Noita PLZ header: $sourcePath"
            }

            $encodedWidth = [BitConverter]::ToInt32($bytes, 4)
            $encodedHeight = [BitConverter]::ToInt32($bytes, 8)
            $width = [Math]::Abs($encodedWidth)
            $height = [Math]::Abs($encodedHeight)
            $flipX = $encodedWidth -lt 0
            $flipY = $encodedHeight -lt 0
            $payloadLength = [BitConverter]::ToInt32($bytes, 16)
            $decodedLength = [BitConverter]::ToInt32($bytes, 20)
            if ($payloadLength -ne $bytes.Length - 24 -or $decodedLength -ne $width * $height * 4) {
                throw "Invalid Noita PLZ lengths: $sourcePath"
            }

            $payload = [byte[]]::new($payloadLength)
            [Array]::Copy($bytes, 24, $payload, 0, $payloadLength)
            $rgba = [PixelEngine.Tools.Noita.NoitaFastLzDecoder]::Decode($payload, $decodedLength)
            $pixels = [byte[]]::new($width * $height)
            $markerCount = 0
            for ($pixelIndex = 0; $pixelIndex -lt $pixels.Length; $pixelIndex++) {
                $offset = $pixelIndex * 4
                $r = $rgba[$offset]
                $g = $rgba[$offset + 1]
                $b = $rgba[$offset + 2]
                $a = $rgba[$offset + 3]
                if ($a -eq 0) { continue }
                $rgb = '{0:x2}{1:x2}{2:x2}' -f $r, $g, $b
                $sourceX = $pixelIndex % $width
                $sourceY = [Math]::Floor($pixelIndex / $width)
                $targetX = $(if ($flipX) { $width - 1 - $sourceX } else { $sourceX })
                $targetY = $(if ($flipY) { $height - 1 - $sourceY } else { $sourceY })
                $targetIndex = ($targetY * $width) + $targetX
                if ($aliases.ContainsKey($rgb)) {
                    $pixels[$targetIndex] = [byte](Resolve-MaterialCode $aliases[$rgb].Name)
                }
                elseif ($rgb -ne '000000' -and $rgb -ne 'ffffff') {
                    $markerCount++
                }
            }
        }
        elseif ($extension -eq '.png') {
            $bitmap = [Drawing.Bitmap]::new($sourceFile)
            $width = $bitmap.Width
            $height = $bitmap.Height
            $pixels = [byte[]]::new($width * $height)
            $markerCount = 0
            for ($y = 0; $y -lt $height; $y++) {
                for ($x = 0; $x -lt $width; $x++) {
                    $color = $bitmap.GetPixel($x, $y)
                    if ($color.A -eq 0) { continue }
                    $rgb = '{0:x2}{1:x2}{2:x2}' -f $color.R, $color.G, $color.B
                    if (-not $aliases.ContainsKey($rgb)) {
                        $markerCount++
                        continue
                    }
                    $pixels[($y * $width) + $x] = [byte](Resolve-MaterialCode $aliases[$rgb].Name)
                }
            }
        }
        else {
            throw "Unsupported Noita material scene asset: $sourcePath"
        }

        $compressed = Compress-Brotli $pixels
        $definitions.Add([pscustomobject]@{
            Ordinal = $sceneIndex
            Width = $width
            Height = $height
            MarkerCount = $markerCount
            DecodedSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($pixels)).ToLowerInvariant()
            Data = [Convert]::ToBase64String($compressed)
        })
    }
    finally { if ($null -ne $bitmap) { $bitmap.Dispose() } }
}

$lines = [Collections.Generic.List[string]]::new()
if ($unmappedMaterials.Count -gt 0) {
    throw "No Demo material mapping for Noita materials: $([string]::Join(', ', @($unmappedMaterials | Sort-Object)))"
}

$lines.Add('// <auto-generated />')
$lines.Add('namespace PixelEngine.Demo;')
$lines.Add('')
$lines.Add('internal static partial class NoitaPixelSceneCatalog')
$lines.Add('{')
$lines.Add('    private static readonly NoitaPixelSceneMaskDefinition[] MaskValues =')
$lines.Add('    [')
foreach ($definition in $definitions) {
    $lines.Add(('        new({0}, {1}, {2}, {3}, "{4}", "{5}"),' -f
        $definition.Ordinal, $definition.Width, $definition.Height, $definition.MarkerCount,
        $definition.DecodedSha256, $definition.Data))
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static ReadOnlySpan<NoitaPixelSceneMaskDefinition> Masks => MaskValues;')
$lines.Add('}')

[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($lines -join "`n") + "`n", $utf8NoBom)
Write-Host "Generated $($definitions.Count) Noita pixel scene material masks."
