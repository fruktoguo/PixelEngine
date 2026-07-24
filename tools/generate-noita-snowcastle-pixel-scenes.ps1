[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-snowcastle-pixel-scenes.json'),
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\maps\noita\snowcastle-pixel-scenes'),
    [string] $GeneratedCodePath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\scripts\NoitaSnowcastlePixelScenes.Generated.cs')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$utf8NoBom = [Text.UTF8Encoding]::new($false)
$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($resolvedOutputRoot) | Out-Null

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Normalize-Color([string] $Value) {
    $normalized = $Value.Trim().Replace('0x', '').ToLowerInvariant()
    if ($normalized.Length -eq 8) { return $normalized.Substring(2) }
    if ($normalized.Length -eq 6) { return $normalized }
    throw "Invalid material color '$Value'."
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

$snowcastleLua = Join-Path $resolvedDataRoot 'scripts\biomes\snowcastle.lua'
$directorHelpersLua = Join-Path $resolvedDataRoot 'scripts\director_helpers.lua'
$expectedSnowcastleLuaSha256 = 'e430ca0bb48e37b96ead0dba9b2af3039d8d0fbe8c497d871204d761d094b8e7'
$expectedDirectorHelpersSha256 = '9a29fcc75303a584df141d2c8168f16b586dfa757f9628c9d28490fc5aea301a'
if ((Get-Sha256 $snowcastleLua) -ne $expectedSnowcastleLuaSha256) {
    throw 'snowcastle.lua source hash changed; review the C# scene table before regenerating.'
}
if ((Get-Sha256 $directorHelpersLua) -ne $expectedDirectorHelpersSha256) {
    throw 'director_helpers.lua source hash changed; review random selection semantics before regenerating.'
}

$sceneOptions = @(
    [pscustomobject]@{ Group='load_pixel_scene'; Probability=0.5; Id='shaft'; Material='shaft.png'; Visual='shaft_visual.png'; Background='' },
    [pscustomobject]@{ Group='load_pixel_scene'; Probability=0.5; Id='bridge'; Material='bridge.png'; Visual=''; Background='' },
    [pscustomobject]@{ Group='load_pixel_scene'; Probability=0.5; Id='drill'; Material='drill.png'; Visual='drill_visual.png'; Background='drill_background.png' },
    [pscustomobject]@{ Group='load_pixel_scene'; Probability=0.5; Id='greenhouse'; Material='greenhouse.png'; Visual='greenhouse_visual.png'; Background='greenhouse_background.png' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.4; Id='cargobay'; Material='cargobay.png'; Visual=''; Background='' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.8; Id='bar'; Material='bar.png'; Visual='bar_visual.png'; Background='bar_background.png' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.8; Id='bedroom'; Material='bedroom.png'; Visual=''; Background='bedroom_background.png' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.4; Id='acidpool'; Material='acidpool.png'; Visual=''; Background='' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.4; Id='polymorphroom'; Material='polymorphroom.png'; Visual=''; Background='' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.2; Id='teleroom'; Material='teleroom.png'; Visual=''; Background='' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.3; Id='sauna'; Material='sauna.png'; Visual='sauna_visual.png'; Background='sauna_background.png' },
    [pscustomobject]@{ Group='load_pixel_scene2'; Probability=0.3; Id='kitchen'; Material='kitchen.png'; Visual=''; Background='' }
)

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

$materialNames = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
$rawScenes = [Collections.Generic.List[object]]::new()
foreach ($option in $sceneOptions) {
    $sourcePath = "data/biome_impl/snowcastle/$($option.Material)"
    $sourceFile = Join-Path $resolvedDataRoot $sourcePath.Substring(5)
    $bitmap = [Drawing.Bitmap]::new($sourceFile)
    try {
        $pixels = [string[]]::new($bitmap.Width * $bitmap.Height)
        $markerColors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $color = $bitmap.GetPixel($x, $y)
                if ($color.A -eq 0) { continue }
                $rgb = '{0:x2}{1:x2}{2:x2}' -f $color.R, $color.G, $color.B
                if ($aliases.ContainsKey($rgb)) {
                    $name = [string]$aliases[$rgb].Name
                    $pixels[($y * $bitmap.Width) + $x] = $name
                    [void]$materialNames.Add($name)
                }
                else {
                    [void]$markerColors.Add($rgb)
                }
            }
        }

        $rawScenes.Add([pscustomobject]@{
            Option = $option
            SourcePath = $sourcePath
            SourceSha256 = Get-Sha256 $sourceFile
            Width = $bitmap.Width
            Height = $bitmap.Height
            Pixels = $pixels
            MarkerColors = @($markerColors | Sort-Object)
        })
    }
    finally { $bitmap.Dispose() }
}

$materialNameArray = @($materialNames)
if ($materialNameArray.Count -gt 254) { throw 'Snowcastle Pixel Scene material table exceeds byte index capacity.' }
$materialIndex = @{}
for ($i = 0; $i -lt $materialNameArray.Count; $i++) { $materialIndex[$materialNameArray[$i]] = $i + 1 }

$assets = [Collections.Generic.List[object]]::new()
$compiledScenes = [Collections.Generic.List[object]]::new()
foreach ($raw in $rawScenes) {
    $bytes = [byte[]]::new($raw.Pixels.Length)
    for ($i = 0; $i -lt $raw.Pixels.Length; $i++) {
        if (-not [string]::IsNullOrEmpty($raw.Pixels[$i])) { $bytes[$i] = [byte]$materialIndex[$raw.Pixels[$i]] }
    }
    $compressed = Compress-Brotli $bytes
    $visualContentPath = ''
    $backgroundContentPath = ''
    foreach ($kind in @('Visual', 'Background')) {
        $fileName = [string]$raw.Option.$kind
        if ([string]::IsNullOrWhiteSpace($fileName)) { continue }
        $sourcePath = "data/biome_impl/snowcastle/$fileName"
        $sourceFile = Join-Path $resolvedDataRoot $sourcePath.Substring(5)
        $contentName = "$($raw.Option.Id)-$($kind.ToLowerInvariant()).png"
        $contentFile = Join-Path $resolvedOutputRoot $contentName
        Copy-Item -LiteralPath $sourceFile -Destination $contentFile -Force
        $contentPath = "maps/noita/snowcastle-pixel-scenes/$contentName"
        if ($kind -eq 'Visual') { $visualContentPath = $contentPath } else { $backgroundContentPath = $contentPath }
        $assets.Add([ordered]@{
            sceneId = $raw.Option.Id
            kind = $kind.ToLowerInvariant()
            sourcePath = $sourcePath
            sourceSha256 = Get-Sha256 $sourceFile
            contentPath = $contentPath
            contentSha256 = Get-Sha256 $contentFile
        })
    }

    $compiledScenes.Add([ordered]@{
        id = $raw.Option.Id
        markerFunction = $raw.Option.Group
        probability = [double]$raw.Option.Probability
        materialPath = $raw.SourcePath
        materialSha256 = $raw.SourceSha256
        width = $raw.Width
        height = $raw.Height
        visualContentPath = $visualContentPath
        backgroundContentPath = $backgroundContentPath
        markerColors = $raw.MarkerColors
        decodedSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        data = [Convert]::ToBase64String($compressed)
    })
}

$catalog = [ordered]@{
    schemaVersion = 1
    referenceBuildId = '17130612'
    referenceVersionHash = '9dbd52ced019a643169a2db02f46c77f8766c6e5'
    source = [ordered]@{
        snowcastleLua = 'data/scripts/biomes/snowcastle.lua'
        snowcastleLuaSha256 = $expectedSnowcastleLuaSha256
        directorHelpersLua = 'data/scripts/director_helpers.lua'
        directorHelpersLuaSha256 = $expectedDirectorHelpersSha256
    }
    markerBindings = @(
        [ordered]@{ color = 'ffff0000'; function = 'load_pixel_scene'; origin = 'wang-builtin' },
        [ordered]@{ color = 'ffffff00'; function = 'load_pixel_scene2'; origin = 'wang-builtin' }
    )
    materialNames = $materialNameArray
    scenes = $compiledScenes
    assets = $assets
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($CatalogPath), ($catalog | ConvertTo-Json -Depth 12) + "`n", $utf8NoBom)

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('// <auto-generated />')
$lines.Add('using PixelEngine.Hosting;')
$lines.Add('using PixelEngine.Scripting;')
$lines.Add('')
$lines.Add('namespace PixelEngine.Demo;')
$lines.Add('')
$lines.Add('internal static partial class NoitaSnowcastlePixelSceneCatalog')
$lines.Add('{')
$lines.Add('    private static readonly string[] MaterialNameValues =')
$lines.Add('    [')
foreach ($name in $materialNameArray) { $lines.Add(('        "{0}",' -f (Escape-CSharp $name))) }
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    private static readonly NoitaSnowcastlePixelSceneDefinition[] SceneValues =')
$lines.Add('    [')
foreach ($scene in $compiledScenes) {
    $visual = if ([string]::IsNullOrEmpty($scene.visualContentPath)) { 'default' } else { 'new ScriptAssetReference(ScriptAssetKind.Texture, "noita-snowcastle-' + $scene.id + '-visual", "' + $scene.visualContentPath + '")' }
    $background = if ([string]::IsNullOrEmpty($scene.backgroundContentPath)) { 'default' } else { 'new ScriptAssetReference(ScriptAssetKind.Texture, "noita-snowcastle-' + $scene.id + '-background", "' + $scene.backgroundContentPath + '")' }
    $lines.Add(('        new("{0}", "{1}", {2}d, {3}, {4}, "{5}", "{6}", {7}, {8}),' -f
        $scene.id, $scene.markerFunction, ([double]$scene.probability).ToString('R', [Globalization.CultureInfo]::InvariantCulture),
        $scene.width, $scene.height, $scene.decodedSha256, $scene.data, $visual, $background))
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static ReadOnlySpan<string> MaterialNames => MaterialNameValues;')
$lines.Add('    internal static ReadOnlySpan<NoitaSnowcastlePixelSceneDefinition> Scenes => SceneValues;')
$lines.Add('}')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($GeneratedCodePath), ($lines -join "`n") + "`n", $utf8NoBom)

[pscustomobject]@{
    Scenes = $compiledScenes.Count
    Materials = $materialNameArray.Count
    Assets = $assets.Count
}
