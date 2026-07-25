[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\sprites\noita\marker-enemies')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$dataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$definitions = @(
    [pscustomobject]@{ Name = 'fungus'; Source = 'enemies_gfx\fungus.png'; Sprite = 'enemies_gfx\fungus.xml'; Entity = 'entities\animals\fungus.xml'; X = 0; Y = 1; Width = 16; Height = 16; Frames = 12; FrameWait = 0.12 },
    [pscustomobject]@{ Name = 'fungus_big'; Source = 'enemies_gfx\fungus_big.png'; Sprite = 'enemies_gfx\fungus_big.xml'; Entity = 'entities\animals\fungus_big.xml'; X = 0; Y = 0; Width = 34; Height = 34; Frames = 8; FrameWait = 0.18 }
)

$catalog = [Collections.Generic.List[object]]::new()
foreach ($definition in $definitions) {
    $sourcePath = Join-Path $dataRoot $definition.Source
    $frames = [Collections.Generic.List[object]]::new()
    $source = [Drawing.Bitmap]::new($sourcePath)
    try {
        for ($frame = 0; $frame -lt $definition.Frames; $frame++) {
            $rectangle = [Drawing.Rectangle]::new(
                $definition.X + ($frame * $definition.Width),
                $definition.Y,
                $definition.Width,
                $definition.Height)
            $cropped = $source.Clone($rectangle, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $target = Join-Path $outputRoot ("{0}_stand_{1:D2}.png" -f $definition.Name, $frame)
                $cropped.Save($target, [Drawing.Imaging.ImageFormat]::Png)
                $frames.Add([ordered]@{
                    contentPath = 'sprites/noita/marker-enemies/' + [IO.Path]::GetFileName($target)
                    contentSha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
                })
            }
            finally {
                $cropped.Dispose()
            }
        }
    }
    finally {
        $source.Dispose()
    }

    $catalog.Add([ordered]@{
        name = $definition.Name
        entityPath = 'data/' + $definition.Entity.Replace('\', '/')
        entitySha256 = (Get-FileHash -LiteralPath (Join-Path $dataRoot $definition.Entity) -Algorithm SHA256).Hash.ToLowerInvariant()
        spritePath = 'data/' + $definition.Sprite.Replace('\', '/')
        spriteSha256 = (Get-FileHash -LiteralPath (Join-Path $dataRoot $definition.Sprite) -Algorithm SHA256).Hash.ToLowerInvariant()
        sourcePath = 'data/' + $definition.Source.Replace('\', '/')
        sourceSha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        frameWidth = $definition.Width
        frameHeight = $definition.Height
        frameWait = $definition.FrameWait
        frames = $frames
    })
}

$provenance = [ordered]@{
    schemaVersion = 1
    referenceBuildId = '17130612'
    assets = $catalog
}
[IO.File]::WriteAllText(
    (Join-Path $outputRoot 'provenance.json'),
    ($provenance | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
