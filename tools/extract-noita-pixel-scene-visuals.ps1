[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-world-content.json'),

    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\maps\noita\global-scenes'),

    [string] $GeneratedCodePath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\scripts\NoitaPixelSceneVisuals.Generated.cs'),

    [string] $ProvenancePath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\maps\noita\global-scenes\provenance.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'NoitaFastLzDecoder.cs')
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$catalog = Get-Content -Raw -LiteralPath $CatalogPath | ConvertFrom-Json -Depth 30
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($resolvedOutputRoot) | Out-Null

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Convert-PlzToPng([string] $SourceFile, [string] $OutputFile) {
    $bytes = [IO.File]::ReadAllBytes($SourceFile)
    if ($bytes.Length -lt 24 -or [BitConverter]::ToInt32($bytes, 0) -ne 1 -or
        [BitConverter]::ToInt32($bytes, 12) -ne 4) {
        throw "Unsupported Noita PLZ header: $SourceFile"
    }

    $encodedWidth = [BitConverter]::ToInt32($bytes, 4)
    $encodedHeight = [BitConverter]::ToInt32($bytes, 8)
    $width = [Math]::Abs($encodedWidth)
    $height = [Math]::Abs($encodedHeight)
    $payloadLength = [BitConverter]::ToInt32($bytes, 16)
    $decodedLength = [BitConverter]::ToInt32($bytes, 20)
    if ($payloadLength -ne $bytes.Length - 24 -or $decodedLength -ne $width * $height * 4) {
        throw "Invalid Noita PLZ lengths: $SourceFile"
    }

    $payload = [byte[]]::new($payloadLength)
    [Array]::Copy($bytes, 24, $payload, 0, $payloadLength)
    $rgba = [PixelEngine.Tools.Noita.NoitaFastLzDecoder]::Decode($payload, $decodedLength)
    $bgra = [byte[]]::new($decodedLength)
    $flipX = $encodedWidth -lt 0
    $flipY = $encodedHeight -lt 0
    for ($sourceIndex = 0; $sourceIndex -lt $width * $height; $sourceIndex++) {
        $sourceX = $sourceIndex % $width
        $sourceY = [Math]::Floor($sourceIndex / $width)
        $targetX = $(if ($flipX) { $width - 1 - $sourceX } else { $sourceX })
        $targetY = $(if ($flipY) { $height - 1 - $sourceY } else { $sourceY })
        $sourceOffset = $sourceIndex * 4
        $targetOffset = (($targetY * $width) + $targetX) * 4
        $bgra[$targetOffset] = $rgba[$sourceOffset + 2]
        $bgra[$targetOffset + 1] = $rgba[$sourceOffset + 1]
        $bgra[$targetOffset + 2] = $rgba[$sourceOffset]
        $bgra[$targetOffset + 3] = $rgba[$sourceOffset + 3]
    }

    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $rectangle = [Drawing.Rectangle]::new(0, 0, $width, $height)
        $bits = $bitmap.LockBits($rectangle, [Drawing.Imaging.ImageLockMode]::WriteOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            if ($bits.Stride -ne $width * 4) { throw "Unexpected bitmap stride for $SourceFile" }
            [Runtime.InteropServices.Marshal]::Copy($bgra, 0, $bits.Scan0, $bgra.Length)
        }
        finally { $bitmap.UnlockBits($bits) }
        $bitmap.Save($OutputFile, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

function Add-Visual($Scene, [int] $Ordinal, [string] $Kind, [Collections.Generic.List[object]] $Destination) {
    $asset = $Scene.assets.PSObject.Properties[$Kind].Value
    if ($null -eq $asset) { return }
    $sourcePath = [string]$asset.path
    $sourceFile = Join-Path $resolvedDataRoot $sourcePath.Substring(5)
    $sourceSha256 = Get-Sha256 $sourceFile
    if (-not [string]::Equals($sourceSha256, [string]$asset.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Source hash mismatch for '$sourcePath'."
    }

    $fileName = '{0:d2}-{1}.png' -f $Ordinal, $Kind
    $outputFile = Join-Path $resolvedOutputRoot $fileName
    $extension = [IO.Path]::GetExtension($sourceFile).ToLowerInvariant()
    if ($extension -eq '.png') {
        Copy-Item -LiteralPath $sourceFile -Destination $outputFile -Force
    }
    elseif ($extension -eq '.plz') {
        Convert-PlzToPng $sourceFile $outputFile
    }
    else {
        throw "Unsupported Noita visual asset '$sourcePath'."
    }
    $outputSha256 = Get-Sha256 $outputFile
    if ($extension -eq '.png' -and -not [string]::Equals($sourceSha256, $outputSha256, [StringComparison]::Ordinal)) {
        throw "Copied visual hash mismatch for '$sourcePath'."
    }

    $Destination.Add([ordered]@{
        ordinal = $Ordinal
        kind = $Kind
        sourcePath = $sourcePath
        sourceSha256 = $sourceSha256
        sourceEncoding = $extension.Substring(1)
        contentPath = "maps/noita/global-scenes/$fileName"
        contentSha256 = $outputSha256
        worldX = [int]$Scene.pos_x
        worldY = [int]$Scene.pos_y
        width = [int]$asset.width
        height = [int]$asset.height
    })
}

$visuals = [Collections.Generic.List[object]]::new()
$scenes = @($catalog.globalPixelScenes.bufferedScenes) + @($catalog.globalPixelScenes.splicedScenes)
for ($i = 0; $i -lt $scenes.Count; $i++) {
    Add-Visual $scenes[$i] $i 'background' $visuals
}
for ($i = 0; $i -lt $scenes.Count; $i++) {
    Add-Visual $scenes[$i] $i 'colors' $visuals
}

$provenance = [ordered]@{
    schemaVersion = 1
    referenceBuildId = [string]$catalog.reference.steamBuildId
    referenceVersionHash = [string]$catalog.reference.versionHash
    generatedBy = 'tools/extract-noita-pixel-scene-visuals.ps1'
    assetCount = $visuals.Count
    assets = $visuals
}
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($ProvenancePath),
    ($provenance | ConvertTo-Json -Depth 10) + "`n",
    $utf8NoBom)

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('// <auto-generated />')
$lines.Add('using PixelEngine.Hosting;')
$lines.Add('using PixelEngine.Scripting;')
$lines.Add('')
$lines.Add('namespace PixelEngine.Demo;')
$lines.Add('')
$lines.Add('internal static partial class NoitaPixelSceneVisualCatalog')
$lines.Add('{')
$lines.Add('    private static readonly NoitaPixelSceneVisualDefinition[] LayerValues =')
$lines.Add('    [')
foreach ($visual in $visuals) {
    $layer = if ($visual.kind -eq 'background') { 'WorldVisualLayerKind.Background' } else { 'WorldVisualLayerKind.Decoration' }
    $assetId = "noita-global-scene-$($visual.ordinal)-$($visual.kind)"
    $lines.Add(('        new(new ScriptAssetReference(ScriptAssetKind.Texture, "{0}", "{1}"), {2}, {3}, {4}, {5}, {6}, "{7}", "{8}"),' -f
        $assetId, $visual.contentPath, $visual.worldX, $visual.worldY, $visual.width, $visual.height,
        $layer, $visual.sourcePath, $visual.sourceSha256))
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static ReadOnlySpan<NoitaPixelSceneVisualDefinition> Layers => LayerValues;')
$lines.Add('}')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($GeneratedCodePath), ($lines -join "`n") + "`n", $utf8NoBom)
Write-Host "Extracted $($visuals.Count) Noita pixel scene visual layers."
