[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $OutputPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-world-content.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ReferenceBuildId = '17130612'
$ReferenceVersionHash = '9dbd52ced019a643169a2db02f46c77f8766c6e5'
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)

function Convert-ToDataPath([string] $Path) {
    $relative = [IO.Path]::GetRelativePath($resolvedDataRoot, $Path).Replace('\', '/')
    return "data/$relative"
}

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Attributes($Node) {
    $result = [ordered]@{}
    if ($null -eq $Node -or $null -eq $Node.Attributes) {
        return $result
    }

    foreach ($attribute in $Node.Attributes) {
        $result[$attribute.Name] = $attribute.Value
    }

    return $result
}

function Read-NoitaXml([string] $Path) {
    $source = [IO.File]::ReadAllText($Path)
    # Noita's loader accepts separator comments containing repeated '--'; standard XML does not.
    # Comments carry no runtime world data, so remove them before structured parsing.
    $sanitized = [regex]::Replace($source, '(?s)<!--.*?-->', '')
    for ($attempt = 0; $attempt -lt 16; $attempt++) {
        try {
            return [xml]$sanitized
        }
        catch {
            $duplicate = [regex]::Match($_.Exception.Message, "'(?<name>[^']+)' is a duplicate attribute")
            if (-not $duplicate.Success) {
                throw
            }

            $name = [regex]::Escape($duplicate.Groups['name'].Value)
            $duplicatePattern = '(?s)(<[^>]*?\b{0}="[^"]*"[^>]*?)\s+{0}="[^"]*"(?=[^>]*>)' -f $name
            $next = [regex]::Replace(
                $sanitized,
                $duplicatePattern,
                '$1',
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)
            if ([string]::Equals($next, $sanitized, [StringComparison]::Ordinal)) {
                throw
            }

            $sanitized = $next
        }
    }

    throw "Noita XML contains more than 16 duplicate attributes: $Path"
}

function Get-ReferencedDataPaths([string] $Source) {
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($Source, 'data/[A-Za-z0-9_./$\-]+\.(?:xml|png|lua|ogg|wav)')) {
        [void]$paths.Add($match.Value)
    }

    return @($paths | Sort-Object)
}

function Get-AssetDescriptor([string] $DataPath) {
    if ([string]::IsNullOrWhiteSpace($DataPath)) {
        return $null
    }
    if (-not $DataPath.StartsWith('data/', [StringComparison]::Ordinal)) {
        throw "World asset path must start with data/: $DataPath"
    }

    $file = Join-Path $resolvedDataRoot $DataPath.Substring(5)
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "World asset not found: $DataPath"
    }

    $descriptor = [ordered]@{
        path = $DataPath
        sha256 = Get-Sha256 $file
        length = (Get-Item -LiteralPath $file).Length
    }
    if ([IO.Path]::GetExtension($file).Equals('.png', [StringComparison]::OrdinalIgnoreCase)) {
        $image = [Drawing.Image]::FromFile($file)
        try {
            $descriptor.width = $image.Width
            $descriptor.height = $image.Height
        }
        finally {
            $image.Dispose()
        }
    }

    return $descriptor
}

$materialsPath = Join-Path $resolvedDataRoot 'materials.xml'
$pixelScenesPath = Join-Path $resolvedDataRoot 'biome\_pixel_scenes.xml'
$biomeRoot = Join-Path $resolvedDataRoot 'biome'
$biomeScriptRoot = Join-Path $resolvedDataRoot 'scripts\biomes'
$vegetationRoot = Join-Path $resolvedDataRoot 'vegetation'
foreach ($required in @($materialsPath, $pixelScenesPath, $biomeRoot, $biomeScriptRoot, $vegetationRoot)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Noita world source missing: $required"
    }
}

$biomes = [Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $biomeRoot -File -Filter '*.xml' | Sort-Object Name) {
    if ($file.Name.StartsWith('_', [StringComparison]::Ordinal)) {
        continue
    }

    $document = Read-NoitaXml $file.FullName
    $topology = $document.SelectSingleNode('/Biome/Topology')
    if ($null -eq $topology) {
        continue
    }

    $materialLayers = [Collections.Generic.List[object]]::new()
    foreach ($node in $document.SelectNodes('/Biome/Materials/MaterialComponent')) {
        $materialLayers.Add((Get-Attributes $node))
    }

    $vegetationLayers = [Collections.Generic.List[object]]::new()
    foreach ($node in $document.SelectNodes('/Biome/Materials/VegetationComponent')) {
        $vegetationLayers.Add((Get-Attributes $node))
    }

    $luaPath = [string]$topology.Attributes['lua_script']?.Value
    $luaDefinition = $null
    if (-not [string]::IsNullOrWhiteSpace($luaPath) -and $luaPath.StartsWith('data/', [StringComparison]::Ordinal)) {
        $luaFile = Join-Path $resolvedDataRoot $luaPath.Substring(5)
        if (Test-Path -LiteralPath $luaFile -PathType Leaf) {
            $luaSource = [IO.File]::ReadAllText($luaFile)
            $spawnFunctions = [Collections.Generic.List[object]]::new()
            foreach ($match in [regex]::Matches(
                $luaSource,
                'RegisterSpawnFunction\s*\(\s*0x(?<color>[0-9a-fA-F]{8})\s*,\s*"(?<function>[^"]+)"')) {
                $spawnFunctions.Add([ordered]@{
                    color = $match.Groups['color'].Value.ToLowerInvariant()
                    function = $match.Groups['function'].Value
                })
            }

            $luaDefinition = [ordered]@{
                path = $luaPath
                sha256 = Get-Sha256 $luaFile
                spawnFunctions = @($spawnFunctions)
                referencedDataPaths = @(Get-ReferencedDataPaths $luaSource)
            }
        }
    }

    $biomes.Add([ordered]@{
        id = [IO.Path]::GetFileNameWithoutExtension($file.Name)
        sourcePath = Convert-ToDataPath $file.FullName
        sourceSha256 = Get-Sha256 $file.FullName
        topology = Get-Attributes $topology
        materialLayers = @($materialLayers)
        vegetationLayers = @($vegetationLayers)
        lua = $luaDefinition
    })
}

$pixelDocument = Read-NoitaXml $pixelScenesPath
$splicedFiles = @($pixelDocument.SelectNodes('/PixelScenes/PixelSceneFiles/File') | ForEach-Object { $_.InnerText.Trim() })
$backgrounds = @($pixelDocument.SelectNodes('/PixelScenes/BackgroundImages/Image') | ForEach-Object { Get-Attributes $_ })
$bufferedScenes = @($pixelDocument.SelectNodes('/PixelScenes/mBufferedPixelScenes/PixelScene') | ForEach-Object {
    $attributes = Get-Attributes $_
    $assets = [ordered]@{
        material = Get-AssetDescriptor $(if ($attributes.Contains('material_filename')) { [string]$attributes['material_filename'] } else { '' })
        colors = Get-AssetDescriptor $(if ($attributes.Contains('colors_filename')) { [string]$attributes['colors_filename'] } else { '' })
        background = Get-AssetDescriptor $(if ($attributes.Contains('background_filename')) { [string]$attributes['background_filename'] } else { '' })
    }
    $attributes.assets = $assets
    $attributes
})

$vegetation = [Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $vegetationRoot -Recurse -File | Sort-Object FullName) {
    $entry = [ordered]@{
        path = Convert-ToDataPath $file.FullName
        sha256 = Get-Sha256 $file.FullName
        length = $file.Length
        extension = $file.Extension.ToLowerInvariant()
    }

    if ($file.Extension.Equals('.xml', [StringComparison]::OrdinalIgnoreCase)) {
        $source = [IO.File]::ReadAllText($file.FullName)
        $entry.references = @(Get-ReferencedDataPaths $source)
    }

    $vegetation.Add($entry)
}

$biomeImplFiles = @(Get-ChildItem -LiteralPath (Join-Path $resolvedDataRoot 'biome_impl') -Recurse -File)
$document = [ordered]@{
    schema = 'pixelengine.noita-world-content/v1'
    reference = [ordered]@{
        steamBuildId = $ReferenceBuildId
        versionHash = $ReferenceVersionHash
        dataRootFileCount = @(Get-ChildItem -LiteralPath $resolvedDataRoot -Recurse -File).Count
        materialsSha256 = Get-Sha256 $materialsPath
        globalPixelScenesSha256 = Get-Sha256 $pixelScenesPath
    }
    statistics = [ordered]@{
        biomes = $biomes.Count
        materialLayers = [int](@($biomes | ForEach-Object { $_.materialLayers.Count } | Measure-Object -Sum).Sum)
        vegetationLayers = [int](@($biomes | ForEach-Object { $_.vegetationLayers.Count } | Measure-Object -Sum).Sum)
        spawnFunctions = [int](@($biomes | ForEach-Object { if ($null -ne $_.lua) { $_.lua.spawnFunctions.Count } else { 0 } } | Measure-Object -Sum).Sum)
        splicedPixelSceneFiles = $splicedFiles.Count
        globalBackgroundImages = $backgrounds.Count
        bufferedPixelScenes = $bufferedScenes.Count
        biomeImplFiles = $biomeImplFiles.Count
        vegetationFiles = $vegetation.Count
    }
    globalPixelScenes = [ordered]@{
        sourcePath = 'data/biome/_pixel_scenes.xml'
        splicedFiles = $splicedFiles
        backgroundImages = $backgrounds
        bufferedScenes = $bufferedScenes
    }
    biomes = @($biomes)
    vegetation = @($vegetation)
}

$parent = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$json = $document | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($resolvedOutputPath, ($json -replace "`r`n", "`n") + "`n", $utf8NoBom)

Write-Host "Noita world content catalog written: $resolvedOutputPath"
Write-Host ($document.statistics | ConvertTo-Json -Compress)
