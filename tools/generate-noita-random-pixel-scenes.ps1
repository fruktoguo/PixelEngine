[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-random-pixel-scenes.json'),
    [string] $RuntimeCatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-random-pixel-scenes-runtime.json'),
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\maps\noita\random-pixel-scenes'),
    [string] $GeneratedCodePath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\scripts\NoitaRandomPixelScenes.Generated.cs')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($resolvedOutputRoot) | Out-Null
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json -Depth 30

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Normalize-Color([string] $Value) {
    $normalized = $Value.Trim().Replace('0x', '').ToLowerInvariant()
    if ($normalized.Length -eq 8) { return $normalized.Substring(2) }
    if ($normalized.Length -eq 6) { return $normalized }
    throw "Invalid color '$Value'."
}

function Add-Alias([hashtable] $Aliases, [string] $Color, [string] $Name, [int] $Priority) {
    if ([string]::IsNullOrWhiteSpace($Color)) { return }
    $key = Normalize-Color $Color
    if (-not $Aliases.ContainsKey($key) -or $Priority -gt $Aliases[$key].Priority) {
        $Aliases[$key] = [pscustomobject]@{ Name = $Name; Priority = $Priority }
    }
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

function Escape-CSharp([string] $Value) {
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

[xml]$materialsXml = [IO.File]::ReadAllText((Join-Path $resolvedDataRoot 'materials.xml'))
$aliases = @{}
foreach ($material in $materialsXml.SelectNodes('//*[@name]')) {
    $name = [string]$material.name
    if ($material.HasAttribute('wang_color')) { Add-Alias $aliases ([string]$material.wang_color) $name 3 }
    foreach ($graphics in $material.SelectNodes('.//Graphics[@color]')) {
        Add-Alias $aliases ([string]$graphics.color) $name 2
    }
    foreach ($graphics in $material.SelectNodes('.//Graphics[@pixel_all_around]')) {
        Add-Alias $aliases ([string]$graphics.pixel_all_around) $name 1
    }
}

$overrideByKey = @{}
foreach ($table in $catalog.colorMaterialTables) {
    foreach ($color in $table.colors) {
        $key = "$($table.sourcePath)|$($table.name)|$(Normalize-Color ([string]$color.color))"
        $overrideByKey[$key] = @($color.materials)
    }
}

$materialNames = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
$overrideKeys = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
$rawScenes = [Collections.Generic.List[object]]::new()
$tables = [Collections.Generic.List[object]]::new()
foreach ($sourceTable in $catalog.catalogs) {
    $firstScene = $rawScenes.Count
    foreach ($entry in $sourceTable.entries) {
        $sourcePath = [string]$entry.material.path
        $sourceFile = Join-Path $resolvedDataRoot $sourcePath.Substring(5)
        if ((Get-Sha256 $sourceFile) -ne [string]$entry.material.sha256) {
            throw "Random Pixel Scene source hash mismatch: $sourcePath"
        }
        $bitmap = [Drawing.Bitmap]::new($sourceFile)
        try {
            $pixels = [string[]]::new($bitmap.Width * $bitmap.Height)
            $markerColors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                for ($x = 0; $x -lt $bitmap.Width; $x++) {
                    $color = $bitmap.GetPixel($x, $y)
                    if ($color.A -eq 0) { continue }
                    $rgb = '{0:x2}{1:x2}{2:x2}' -f $color.R, $color.G, $color.B
                    $overrideKey = ''
                    if (-not [string]::IsNullOrWhiteSpace([string]$entry.colorMaterialTable)) {
                        $candidate = "$($sourceTable.sourcePath)|$($entry.colorMaterialTable)|$rgb"
                        if ($overrideByKey.ContainsKey($candidate)) { $overrideKey = $candidate }
                    }
                    if ($overrideKey) {
                        $pixels[($y * $bitmap.Width) + $x] = "@$overrideKey"
                        [void]$overrideKeys.Add($overrideKey)
                        foreach ($choice in $overrideByKey[$overrideKey]) { [void]$materialNames.Add([string]$choice) }
                    }
                    elseif ($aliases.ContainsKey($rgb)) {
                        $name = [string]$aliases[$rgb].Name
                        $pixels[($y * $bitmap.Width) + $x] = $name
                        [void]$materialNames.Add($name)
                    }
                    else { [void]$markerColors.Add($rgb) }
                }
            }
            $rawScenes.Add([pscustomobject]@{
                TableIndex = $tables.Count
                Probability = [double]$entry.probability
                IsUnique = [bool]$entry.isUnique
                SourcePath = $sourcePath
                SourceSha256 = [string]$entry.material.sha256
                Width = $bitmap.Width
                Height = $bitmap.Height
                Pixels = $pixels
                MarkerColors = @($markerColors | Sort-Object)
                Visual = $entry.visual
                Background = $entry.background
            })
        }
        finally { $bitmap.Dispose() }
    }
    $tables.Add([pscustomobject]@{
        BiomeId = [string]$sourceTable.biomeId
        SourcePath = [string]$sourceTable.sourcePath
        Name = [string]$sourceTable.table
        Functions = @($sourceTable.functions)
        FirstScene = $firstScene
        SceneCount = $rawScenes.Count - $firstScene
    })
}

$materialNameArray = @($materialNames)
$overrideKeyArray = @($overrideKeys)
if ($materialNameArray.Count + $overrideKeyArray.Count -gt 254) {
    throw "Random Pixel Scene byte code capacity exceeded: $($materialNameArray.Count) materials + $($overrideKeyArray.Count) overrides."
}
$materialIndex = @{}
for ($i = 0; $i -lt $materialNameArray.Count; $i++) { $materialIndex[$materialNameArray[$i]] = $i + 1 }
$overrideCode = @{}
for ($i = 0; $i -lt $overrideKeyArray.Count; $i++) { $overrideCode[$overrideKeyArray[$i]] = $materialNameArray.Count + $i + 1 }

$assetByPath = @{}
$assets = [Collections.Generic.List[object]]::new()
function Copy-VisualAsset($Asset, [string] $Kind) {
    if ($null -eq $Asset) { return '' }
    $sourcePath = [string]$Asset.path
    if ($assetByPath.ContainsKey($sourcePath)) { return [string]$assetByPath[$sourcePath] }
    $sourceFile = Join-Path $resolvedDataRoot $sourcePath.Substring(5)
    if ((Get-Sha256 $sourceFile) -ne [string]$Asset.sha256) { throw "Visual hash mismatch: $sourcePath" }
    $contentName = '{0}-{1}.png' -f ([string]$Asset.sha256).Substring(0, 16), $Kind
    $contentFile = Join-Path $resolvedOutputRoot $contentName
    Copy-Item -LiteralPath $sourceFile -Destination $contentFile -Force
    $contentPath = "maps/noita/random-pixel-scenes/$contentName"
    $assetByPath[$sourcePath] = $contentPath
    $assets.Add([ordered]@{
        kind = $Kind
        sourcePath = $sourcePath
        sourceSha256 = [string]$Asset.sha256
        contentPath = $contentPath
        contentSha256 = Get-Sha256 $contentFile
    })
    return $contentPath
}

$compiledScenes = [Collections.Generic.List[object]]::new()
for ($sceneIndex = 0; $sceneIndex -lt $rawScenes.Count; $sceneIndex++) {
    $raw = $rawScenes[$sceneIndex]
    $bytes = [byte[]]::new($raw.Pixels.Length)
    for ($i = 0; $i -lt $raw.Pixels.Length; $i++) {
        $value = $raw.Pixels[$i]
        if ([string]::IsNullOrEmpty($value)) { continue }
        $bytes[$i] = [byte]$(if ($value[0] -eq '@') { $overrideCode[$value.Substring(1)] } else { $materialIndex[$value] })
    }
    $compressed = Compress-Brotli $bytes
    $compiledScenes.Add([pscustomobject]@{
        Id = "random-scene-$sceneIndex"
        TableIndex = $raw.TableIndex
        Probability = $raw.Probability
        IsUnique = $raw.IsUnique
        MaterialPath = $raw.SourcePath
        MaterialSha256 = $raw.SourceSha256
        Width = $raw.Width
        Height = $raw.Height
        MarkerColors = $raw.MarkerColors
        DecodedSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        Data = [Convert]::ToBase64String($compressed)
        VisualContentPath = Copy-VisualAsset $raw.Visual 'visual'
        BackgroundContentPath = Copy-VisualAsset $raw.Background 'background'
    })
}

$provenance = [ordered]@{
    schemaVersion = 1
    referenceBuildId = [string]$catalog.reference.steamBuildId
    generatedBy = 'tools/generate-noita-random-pixel-scenes.ps1'
    assetCount = $assets.Count
    assets = $assets
}
[IO.File]::WriteAllText((Join-Path $resolvedOutputRoot 'provenance.json'), ($provenance | ConvertTo-Json -Depth 10) + "`n", $utf8NoBom)
$runtimeCatalog = [ordered]@{
    schema = 'pixelengine.noita-random-pixel-scenes-runtime/v1'
    referenceBuildId = [string]$catalog.reference.steamBuildId
    tables = $tables.Count
    scenes = $compiledScenes.Count
    materialNames = $materialNameArray
    overrideCount = $overrideKeyArray.Count
    assetCount = $assets.Count
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($RuntimeCatalogPath), ($runtimeCatalog | ConvertTo-Json -Depth 5) + "`n", $utf8NoBom)

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('// <auto-generated />')
$lines.Add('using PixelEngine.Hosting;')
$lines.Add('using PixelEngine.Scripting;')
$lines.Add('')
$lines.Add('namespace PixelEngine.Demo;')
$lines.Add('')
$lines.Add('internal static partial class NoitaRandomPixelSceneCatalog')
$lines.Add('{')
$maximumWidth = ($compiledScenes | Measure-Object -Property Width -Maximum).Maximum
$maximumHeight = ($compiledScenes | Measure-Object -Property Height -Maximum).Maximum
$lines.Add(('    internal const int MaximumWidth = {0};' -f $maximumWidth))
$lines.Add(('    internal const int MaximumHeight = {0};' -f $maximumHeight))
$lines.Add('')
$lines.Add('    private static readonly string[] MaterialNameValues =')
$lines.Add('    [')
foreach ($name in $materialNameArray) { $lines.Add(('        "{0}",' -f (Escape-CSharp $name))) }
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    private static readonly NoitaRandomPixelSceneOverrideDefinition[] OverrideValues =')
$lines.Add('    [')
foreach ($key in $overrideKeyArray) {
    $choiceIndices = @($overrideByKey[$key] | ForEach-Object { [int]$materialIndex[[string]$_] })
    $lines.Add(('        new({0}, [{1}]),' -f $overrideCode[$key], ([string]::Join(', ', $choiceIndices))))
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    private static readonly NoitaRandomPixelSceneTableDefinition[] TableValues =')
$lines.Add('    [')
foreach ($table in $tables) {
    $functions = [string]::Join(', ', @($table.Functions | ForEach-Object { '"' + (Escape-CSharp ([string]$_)) + '"' }))
    $lines.Add(('        new("{0}", "{1}", "{2}", [{3}], {4}, {5}),' -f
        (Escape-CSharp $table.BiomeId), (Escape-CSharp $table.SourcePath), (Escape-CSharp $table.Name),
        $functions, $table.FirstScene, $table.SceneCount))
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    private static readonly NoitaRandomPixelSceneDefinition[] SceneValues =')
$lines.Add('    [')
foreach ($scene in $compiledScenes) {
    $visual = if (-not $scene.VisualContentPath) { 'default' } else { 'new ScriptAssetReference(ScriptAssetKind.Texture, "noita-random-' + $scene.Id + '-visual", "' + $scene.VisualContentPath + '")' }
    $background = if (-not $scene.BackgroundContentPath) { 'default' } else { 'new ScriptAssetReference(ScriptAssetKind.Texture, "noita-random-' + $scene.Id + '-background", "' + $scene.BackgroundContentPath + '")' }
    $lines.Add(('        new("{0}", {1}, {2}d, {3}, {4}, {5}, "{6}", "{7}", {8}, {9}),' -f
        $scene.Id, $scene.TableIndex, $scene.Probability.ToString('R', [Globalization.CultureInfo]::InvariantCulture),
        $scene.IsUnique.ToString().ToLowerInvariant(), $scene.Width, $scene.Height, $scene.DecodedSha256, $scene.Data,
        $visual, $background))
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static ReadOnlySpan<string> MaterialNames => MaterialNameValues;')
$lines.Add('    internal static ReadOnlySpan<NoitaRandomPixelSceneOverrideDefinition> Overrides => OverrideValues;')
$lines.Add('    internal static ReadOnlySpan<NoitaRandomPixelSceneTableDefinition> Tables => TableValues;')
$lines.Add('    internal static ReadOnlySpan<NoitaRandomPixelSceneDefinition> Scenes => SceneValues;')
$lines.Add('}')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($GeneratedCodePath), ($lines -join "`n") + "`n", $utf8NoBom)

[pscustomobject]@{
    Tables = $tables.Count
    Scenes = $compiledScenes.Count
    Materials = $materialNameArray.Count
    Overrides = $overrideKeyArray.Count
    Assets = $assets.Count
}
