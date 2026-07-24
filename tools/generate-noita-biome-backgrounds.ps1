param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDataRoot,
    [string]$BiomeCatalogPath = "demo/PixelEngine.Demo/content/biomes.json",
    [string]$OutputCatalogPath = "demo/PixelEngine.Demo/content/noita-biome-backgrounds.json",
    [string]$OutputAssetDirectory = "demo/PixelEngine.Demo/content/maps/noita/biome-backgrounds",
    [string]$OutputCodePath = "demo/PixelEngine.Demo/scripts/NoitaBiomeBackgrounds.Generated.cs"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Resolve-RepositoryRoot {
    $directory = Resolve-Path (Join-Path $PSScriptRoot "..")
    while ($null -ne $directory) {
        if (Test-Path (Join-Path $directory "PixelEngine.sln")) {
            return $directory.Path
        }

        $parent = Split-Path -Parent $directory
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $directory.Path) { break }
        $directory = Resolve-Path $parent
    }

    throw "无法定位 PixelEngine.sln。"
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Escape-CSharp([string]$Value) {
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

function Get-Attribute([string]$OpeningTag, [string]$Name, [string]$Default = "") {
    $match = [Text.RegularExpressions.Regex]::Match(
        $OpeningTag,
        ('(?<![A-Za-z0-9_]){0}\s*=\s*"([^"]*)"' -f [Text.RegularExpressions.Regex]::Escape($Name)))
    if (-not $match.Success) { return $Default }
    return $match.Groups[1].Value
}

$root = Resolve-RepositoryRoot
$sourceRoot = [IO.Path]::GetFullPath($SourceDataRoot)
$biomeCatalogFullPath = [IO.Path]::GetFullPath((Join-Path $root $BiomeCatalogPath))
$outputCatalogFullPath = [IO.Path]::GetFullPath((Join-Path $root $OutputCatalogPath))
$outputAssetFullPath = [IO.Path]::GetFullPath((Join-Path $root $OutputAssetDirectory))
$outputCodeFullPath = [IO.Path]::GetFullPath((Join-Path $root $OutputCodePath))

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) { throw "Noita data root 不存在：$sourceRoot" }
if (-not (Test-Path -LiteralPath $biomeCatalogFullPath -PathType Leaf)) { throw "biomes.json 不存在：$biomeCatalogFullPath" }

$catalog = Get-Content -LiteralPath $biomeCatalogFullPath -Raw | ConvertFrom-Json -Depth 100
$referenceBiomes = @($catalog.worldTopology.referenceBiomes)
$assetBySource = [ordered]@{}
$definitions = [System.Collections.Generic.List[object]]::new()

foreach ($biome in $referenceBiomes) {
    $referencePath = [string]$biome.referencePath
    $xmlFullPath = Join-Path (Split-Path -Parent $sourceRoot) ($referencePath -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $xmlFullPath -PathType Leaf)) { throw "Biome XML 不存在：$referencePath" }
    $xmlText = [IO.File]::ReadAllText($xmlFullPath)
    # 来源文件存在非法 XML 注释和重复属性；背景字段全部位于首个 Topology opening tag，
    # 因此只解析该受限结构，避免把 Noita 宽松 XML 当作标准 XML 重新解释。
    $topologyMatch = [Text.RegularExpressions.Regex]::Match($xmlText, '<Topology\b[\s\S]*?>')
    if (-not $topologyMatch.Success) { throw "Biome XML 缺少 Topology：$referencePath" }
    $openingTag = $topologyMatch.Value

    $paths = [ordered]@{
        image = Get-Attribute $openingTag "background_image"
        left = Get-Attribute $openingTag "background_edge_left"
        right = Get-Attribute $openingTag "background_edge_right"
        top = Get-Attribute $openingTag "background_edge_top"
        bottom = Get-Attribute $openingTag "background_edge_bottom"
    }
    $assetIndices = [ordered]@{}
    foreach ($entry in $paths.GetEnumerator()) {
        $sourcePath = [string]$entry.Value
        if ([string]::IsNullOrWhiteSpace($sourcePath)) {
            $assetIndices[$entry.Key] = -1
            continue
        }

        if (-not $assetBySource.Contains($sourcePath)) {
            $sourceFullPath = Join-Path (Split-Path -Parent $sourceRoot) ($sourcePath -replace '/', [IO.Path]::DirectorySeparatorChar)
            if (-not (Test-Path -LiteralPath $sourceFullPath -PathType Leaf)) { throw "背景资产不存在：$sourcePath" }
            $image = [Drawing.Image]::FromFile($sourceFullPath)
            try { $width = $image.Width; $height = $image.Height } finally { $image.Dispose() }
            $index = $assetBySource.Count
            $fileName = ('{0:D3}_{1}' -f $index, [IO.Path]::GetFileName($sourceFullPath))
            $contentPath = "maps/noita/biome-backgrounds/$fileName"
            $assetBySource[$sourcePath] = [ordered]@{
                index = $index
                sourcePath = $sourcePath
                sourceSha256 = Get-Sha256 $sourceFullPath
                width = $width
                height = $height
                contentPath = $contentPath
                sourceFullPath = $sourceFullPath
            }
        }

        $assetIndices[$entry.Key] = $assetBySource[$sourcePath].index
    }

    $definitions.Add([ordered]@{
        biomeId = [string]$biome.id
        referencePath = $referencePath
        referenceSha256 = Get-Sha256 $xmlFullPath
        imageAssetIndex = $assetIndices.image
        leftAssetIndex = $assetIndices.left
        rightAssetIndex = $assetIndices.right
        topAssetIndex = $assetIndices.top
        bottomAssetIndex = $assetIndices.bottom
        useNeighbor = (Get-Attribute $openingTag "background_use_neighbor" "1") -ne "0"
        limitImage = (Get-Attribute $openingTag "limit_background_image" "1") -ne "0"
        edgePriority = [int](Get-Attribute $openingTag "background_edge_priority" "0")
    })
}

New-Item -ItemType Directory -Force -Path $outputAssetFullPath | Out-Null
Get-ChildItem -LiteralPath $outputAssetFullPath -File -ErrorAction SilentlyContinue | Remove-Item -Force
$assets = [System.Collections.Generic.List[object]]::new()
foreach ($asset in $assetBySource.Values) {
    $destination = Join-Path $outputAssetFullPath ([IO.Path]::GetFileName($asset.contentPath))
    Copy-Item -LiteralPath $asset.sourceFullPath -Destination $destination -Force
    $assets.Add([ordered]@{
        sourcePath = $asset.sourcePath
        sourceSha256 = $asset.sourceSha256
        width = $asset.width
        height = $asset.height
        contentPath = $asset.contentPath
        contentSha256 = Get-Sha256 $destination
    })
}

$output = [ordered]@{
    schemaVersion = 1
    referenceBuildId = "17130612"
    referenceVersionHash = [string]$catalog.worldTopology.referenceVersionHash
    biomeCount = $definitions.Count
    assetCount = $assets.Count
    assets = $assets
    biomes = $definitions
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputCatalogFullPath) | Out-Null
[IO.File]::WriteAllText($outputCatalogFullPath, ($output | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("// <auto-generated />")
$lines.Add("using PixelEngine.Scripting;")
$lines.Add("")
$lines.Add("namespace PixelEngine.Demo;")
$lines.Add("")
$lines.Add("internal static partial class NoitaBiomeBackgroundCatalog")
$lines.Add("{")
$lines.Add("    private static readonly NoitaBiomeBackgroundAssetDefinition[] AssetValues =")
$lines.Add("    [")
for ($i = 0; $i -lt $assets.Count; $i++) {
    $asset = $assets[$i]
    $lines.Add(('        new({0}, {1}, "{2}", "{3}", new ScriptAssetReference(ScriptAssetKind.Texture, "noita-biome-background-{4}", "{5}")),' -f
        $asset.width, $asset.height, (Escape-CSharp $asset.sourcePath), $asset.contentSha256, $i, (Escape-CSharp $asset.contentPath)))
}
$lines.Add("    ];")
$lines.Add("")
$lines.Add("    private static readonly NoitaBiomeBackgroundDefinition[] BiomeValues =")
$lines.Add("    [")
foreach ($definition in $definitions) {
    $lines.Add(('        new("{0}", {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}),' -f
        (Escape-CSharp $definition.biomeId), $definition.imageAssetIndex, $definition.leftAssetIndex,
        $definition.rightAssetIndex, $definition.topAssetIndex, $definition.bottomAssetIndex,
        $definition.useNeighbor.ToString().ToLowerInvariant(), $definition.limitImage.ToString().ToLowerInvariant(), $definition.edgePriority))
}
$lines.Add("    ];")
$lines.Add("}")
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputCodeFullPath) | Out-Null
[IO.File]::WriteAllLines($outputCodeFullPath, $lines, [Text.UTF8Encoding]::new($false))

Write-Host "Generated Noita biome backgrounds: biomes=$($definitions.Count) assets=$($assets.Count)"
