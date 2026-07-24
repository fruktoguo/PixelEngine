[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $WorldCatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-world-content.json'),
    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-vegetation.json'),
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\maps\noita\vegetation'),
    [string] $GeneratedCodePath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\scripts\NoitaVegetation.Generated.cs')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$utf8NoBom = [Text.UTF8Encoding]::new($false)
$dataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$worldCatalog = Get-Content -LiteralPath $WorldCatalogPath -Raw | ConvertFrom-Json -Depth 40
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
Get-ChildItem -LiteralPath $outputRoot -File | Remove-Item -Force

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Escape-CSharp([string] $Value) {
    return $Value.Replace('\', '\\').Replace('"', '\"')
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

function Read-Double($Object, [string] $Name, [double] $DefaultValue = 0) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return $DefaultValue
    }

    return [double]::Parse([string]$property.Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Read-String($Object, [string] $Name, [string] $DefaultValue = '') {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $DefaultValue }
    return [string]$property.Value
}

function Expand-NoitaRange([string] $Path) {
    $match = [regex]::Match($Path, '\$\[(?<start>\d+)-(?<end>\d+)\]')
    if (-not $match.Success) { return @($Path) }

    $startText = $match.Groups['start'].Value
    $endText = $match.Groups['end'].Value
    $values = [Collections.Generic.List[string]]::new()
    for ($value = [int]$startText; $value -le [int]$endText; $value++) {
        $replacement = $value.ToString([Globalization.CultureInfo]::InvariantCulture)
        $values.Add($Path.Substring(0, $match.Index) + $replacement + $Path.Substring($match.Index + $match.Length))
    }

    return @($values)
}

function Resolve-SourceFile([string] $DataPath) {
    if (-not $DataPath.StartsWith('data/', [StringComparison]::Ordinal)) {
        throw "Vegetation path must be rooted at data/: $DataPath"
    }

    $path = Join-Path $dataRoot $DataPath.Substring(5).Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Vegetation source missing: $DataPath"
    }

    return $path
}

function Read-VisualSource([string] $DataPath) {
    $sourceFile = Resolve-SourceFile $DataPath
    if ([IO.Path]::GetExtension($sourceFile).Equals('.png', [StringComparison]::OrdinalIgnoreCase)) {
        $bitmap = [Drawing.Bitmap]::new($sourceFile)
        return [pscustomobject]@{
            SourcePath = $DataPath
            SourceFile = $sourceFile
            SourceSha256 = Get-Sha256 $sourceFile
            Bitmap = $bitmap
            OffsetX = 0
            OffsetY = 0
            FrameIndex = 0
        }
    }

    [xml]$document = [IO.File]::ReadAllText($sourceFile)
    $sprite = $document.SelectSingleNode('/Sprite')
    $animation = $document.SelectSingleNode('/Sprite/RectAnimation[@name="vegetation_growth"]')
    if ($null -eq $sprite -or $null -eq $animation) {
        throw "Vegetation XML lacks Sprite/vegetation_growth: $DataPath"
    }

    $imagePath = [string]$sprite.filename
    $imageFile = Resolve-SourceFile $imagePath
    $sheet = [Drawing.Bitmap]::new($imageFile)
    try {
        $frameCount = [int]$animation.frame_count
        $frameWidth = [int]$animation.frame_width
        $frameHeight = [int]$animation.frame_height
        $cropWidth = $frameWidth
        $cropHeight = $frameHeight
        if ($animation.HasAttribute('shrink_by_one_pixel') -and [string]$animation.shrink_by_one_pixel -eq '1') {
            $cropWidth--
            $cropHeight--
        }
        $framesPerRow = [int]$animation.frames_per_row
        $positionX = [int]$animation.pos_x
        $positionY = [int]$animation.pos_y
        if ($frameCount -le 0 -or $frameWidth -le 0 -or $frameHeight -le 0 -or $framesPerRow -le 0) {
            throw "Vegetation XML has invalid mature frame metadata: $DataPath"
        }

        $frameIndex = $frameCount - 1
        $rectangle = [Drawing.Rectangle]::new(
            $positionX + (($frameIndex % $framesPerRow) * $frameWidth),
            $positionY + ([Math]::Floor($frameIndex / $framesPerRow) * $frameHeight),
            $cropWidth,
            $cropHeight)
        if ($rectangle.Right -gt $sheet.Width -or $rectangle.Bottom -gt $sheet.Height) {
            throw "Vegetation mature frame exceeds sprite sheet: $DataPath"
        }

        return [pscustomobject]@{
            SourcePath = $DataPath
            SourceFile = $sourceFile
            SourceSha256 = Get-Sha256 $sourceFile
            ImagePath = $imagePath
            ImageSha256 = Get-Sha256 $imageFile
            Bitmap = $sheet.Clone($rectangle, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            OffsetX = [int]$sprite.offset_x
            OffsetY = [int]$sprite.offset_y
            FrameIndex = $frameIndex
        }
    }
    finally { $sheet.Dispose() }
}

$assetByKey = @{}
$assets = [Collections.Generic.List[object]]::new()
$layers = [Collections.Generic.List[object]]::new()
$materials = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
$layerOrdinal = 0
foreach ($biome in $worldCatalog.biomes) {
    foreach ($sourceLayer in $biome.vegetationLayers) {
        $layerOrdinal++
        $variantIndices = [Collections.Generic.List[int]]::new()
        $imageProperty = $sourceLayer.PSObject.Properties['tree_image_file']
        $imagePattern = if ($null -eq $imageProperty) { '' } else { [string]$imageProperty.Value }
        foreach ($variantPath in $(if ([string]::IsNullOrWhiteSpace($imagePattern)) { @() } else { Expand-NoitaRange $imagePattern })) {
            $visual = Read-VisualSource $variantPath
            try {
                $key = "$variantPath|$($visual.FrameIndex)"
                if (-not $assetByKey.ContainsKey($key)) {
                    $assetIndex = $assets.Count
                    $contentName = '{0:D3}_{1}.png' -f $assetIndex, ([IO.Path]::GetFileNameWithoutExtension($variantPath) -replace '[^a-zA-Z0-9_-]', '-')
                    $contentFile = Join-Path $outputRoot $contentName
                    $visual.Bitmap.Save($contentFile, [Drawing.Imaging.ImageFormat]::Png)
                    $mask = [byte[]]::new($visual.Bitmap.Width * $visual.Bitmap.Height)
                    for ($y = 0; $y -lt $visual.Bitmap.Height; $y++) {
                        for ($x = 0; $x -lt $visual.Bitmap.Width; $x++) {
                            if ($visual.Bitmap.GetPixel($x, $y).A -ne 0) { $mask[($y * $visual.Bitmap.Width) + $x] = 1 }
                        }
                    }

                    $asset = [ordered]@{
                        sourcePath = $visual.SourcePath
                        sourceSha256 = $visual.SourceSha256
                        imagePath = $(if ($null -ne $visual.PSObject.Properties['ImagePath']) { $visual.ImagePath } else { $visual.SourcePath })
                        imageSha256 = $(if ($null -ne $visual.PSObject.Properties['ImageSha256']) { $visual.ImageSha256 } else { $visual.SourceSha256 })
                        frameIndex = $visual.FrameIndex
                        width = $visual.Bitmap.Width
                        height = $visual.Bitmap.Height
                        offsetX = $visual.OffsetX
                        offsetY = $visual.OffsetY
                        contentPath = "maps/noita/vegetation/$contentName"
                        contentSha256 = Get-Sha256 $contentFile
                        decodedMaskSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($mask)).ToLowerInvariant()
                        mask = [Convert]::ToBase64String((Compress-Brotli $mask))
                    }
                    $assets.Add($asset)
                    $assetByKey[$key] = $assetIndex
                }

                $variantIndices.Add([int]$assetByKey[$key])
            }
            finally { $visual.Bitmap.Dispose() }
        }

        $materialProperty = $sourceLayer.PSObject.Properties['tree_material']
        $material = if ($null -eq $materialProperty) { '' } else { [string]$materialProperty.Value }
        if (-not [string]::IsNullOrWhiteSpace($material)) { [void]$materials.Add($material) }
        $colorProperty = $sourceLayer.PSObject.Properties['visual_color']
        $layers.Add([ordered]@{
            ordinal = $layerOrdinal
            biomeId = [string]$biome.id
            enabled = ((Read-String $sourceLayer '_enabled' '1') -ne '0')
            isVisual = ((Read-String $sourceLayer 'is_visual') -eq '1')
            isCeiling = ($null -ne $sourceLayer.PSObject.Properties['is_ceiling_plant'] -and [string]$sourceLayer.is_ceiling_plant -eq '1')
            randomSeed = Read-Double $sourceLayer 'rand_seed'
            material = $material
            probability = Read-Double $sourceLayer 'tree_probability'
            radiusLow = Read-Double $sourceLayer 'tree_radius_low'
            radiusHigh = Read-Double $sourceLayer 'tree_radius_high'
            treeWidth = Read-Double $sourceLayer 'tree_width' 64
            extraY = Read-Double $sourceLayer 'tree_extra_y'
            visualColor = $(if ($null -eq $colorProperty) { '' } else { [string]$colorProperty.Value })
            visualOffsetX = Read-Double $sourceLayer 'visual_offset_x'
            visualOffsetY = Read-Double $sourceLayer 'visual_offset_y'
            variantIndices = @($variantIndices)
        })
    }
}

$catalog = [ordered]@{
    schemaVersion = 1
    referenceBuildId = [string]$worldCatalog.reference.steamBuildId
    referenceVersionHash = [string]$worldCatalog.reference.versionHash
    worldCatalogSha256 = Get-Sha256 $WorldCatalogPath
    layerCount = $layers.Count
    assetCount = $assets.Count
    materialNames = @($materials)
    assets = @($assets)
    layers = @($layers)
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($CatalogPath), (($catalog | ConvertTo-Json -Depth 20) -replace "`r`n", "`n") + "`n", $utf8NoBom)

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('// <auto-generated />')
$lines.Add('using PixelEngine.Scripting;')
$lines.Add('')
$lines.Add('namespace PixelEngine.Demo;')
$lines.Add('')
$lines.Add('internal static partial class NoitaVegetationCatalog')
$lines.Add('{')
$lines.Add('    private static readonly NoitaVegetationAssetDefinition[] AssetValues =')
$lines.Add('    [')
for ($i = 0; $i -lt $assets.Count; $i++) {
    $asset = $assets[$i]
    $lines.Add("        new NoitaVegetationAssetDefinition($($asset.width), $($asset.height), $($asset.offsetX), $($asset.offsetY), `"$($asset.decodedMaskSha256)`", `"$($asset.mask)`", new ScriptAssetReference(ScriptAssetKind.Texture, `"noita-vegetation-$i`", `"$($asset.contentPath)`")),")
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    private static readonly NoitaVegetationLayerDefinition[] LayerValues =')
$lines.Add('    [')
foreach ($layer in $layers) {
    $variants = [string]::Join(', ', @($layer.variantIndices))
    $color = ([string]$layer.visualColor).Replace('0x', '')
    if ($color.Length -ge 6) { $color = "FF$($color.Substring($color.Length - 6))" } else { $color = 'FFFFFFFF' }
    $lines.Add("        new NoitaVegetationLayerDefinition($($layer.ordinal), `"$(Escape-CSharp $layer.biomeId)`", $($layer.enabled.ToString().ToLowerInvariant()), $($layer.isVisual.ToString().ToLowerInvariant()), $($layer.isCeiling.ToString().ToLowerInvariant()), $($layer.randomSeed.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), `"$(Escape-CSharp $layer.material)`", $($layer.probability.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), $($layer.radiusLow.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), $($layer.radiusHigh.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), $($layer.treeWidth.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), $($layer.extraY.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), 0x$($color)u, $($layer.visualOffsetX.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), $($layer.visualOffsetY.ToString('R', [Globalization.CultureInfo]::InvariantCulture)), [$variants]),")
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static ReadOnlySpan<NoitaVegetationAssetDefinition> Assets => AssetValues;')
$lines.Add('')
$lines.Add('    internal static ReadOnlySpan<NoitaVegetationLayerDefinition> Layers => LayerValues;')
$lines.Add('}')
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($GeneratedCodePath), $lines, $utf8NoBom)

Write-Host "Noita vegetation generated: layers=$($layers.Count) assets=$($assets.Count) materials=$($materials.Count)"
