[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [Parameter(Mandatory = $true)]
    [string] $NoitaInstallRoot,

    [string] $OutputPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-wang-terrain.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$ReferenceBuildId = '17130612'
$ReferenceVersionHash = '9dbd52ced019a643169a2db02f46c77f8766c6e5'
$SemanticEmpty = 0
$SemanticPrimary = 1
$SemanticSecondary = 2
$SemanticLoose = 3
$SemanticStructure = 4
$SemanticHazard = 5
$SemanticPool = 6
$SemanticRandomBinary = 9
$SemanticMarkerBase = 32

$specifications = @(
    [ordered]@{ id = 'coalmine'; biome = 'biome/coalmine.xml'; wang = 'wang_tiles/coalmine.png'; bindings = @('coalmine') },
    [ordered]@{ id = 'coalmine-alt'; biome = 'biome/coalmine_alt.xml'; wang = 'wang_tiles/coalmine_alt.png'; bindings = @('coalmine-alt') },
    [ordered]@{ id = 'excavationsite'; biome = 'biome/excavationsite.xml'; wang = 'wang_tiles/excavationsite.png'; bindings = @('excavationsite', 'excavationsite-cube-chamber') },
    [ordered]@{ id = 'fungicave'; biome = 'biome/fungicave.xml'; wang = 'wang_tiles/fungicave.png'; bindings = @('fungicave') },
    [ordered]@{ id = 'fungiforest'; biome = 'biome/fungiforest.xml'; wang = 'wang_tiles/fungiforest.png'; bindings = @('fungiforest') },
    [ordered]@{ id = 'snowcave'; biome = 'biome/snowcave.xml'; wang = 'wang_tiles/snowcave.png'; bindings = @('snowcave', 'snowcave-secret-chamber') },
    [ordered]@{ id = 'snowcastle'; biome = 'biome/snowcastle.xml'; wang = 'wang_tiles/snowcastle.png'; bindings = @('snowcastle', 'snowcastle-hourglass-chamber', 'snowcastle-cavern') },
    [ordered]@{ id = 'rainforest'; biome = 'biome/rainforest.xml'; wang = 'wang_tiles/rainforest.png'; bindings = @('rainforest') },
    [ordered]@{ id = 'rainforest-open'; biome = 'biome/rainforest_open.xml'; wang = 'wang_tiles/rainforest_open.png'; bindings = @('rainforest-open') },
    [ordered]@{ id = 'rainforest-dark'; biome = 'biome/rainforest_dark.xml'; wang = 'wang_tiles/rainforest_dark.png'; bindings = @('rainforest-dark') },
    [ordered]@{ id = 'vault'; biome = 'biome/vault.xml'; wang = 'wang_tiles/vault.png'; bindings = @('vault') },
    [ordered]@{ id = 'vault-frozen'; biome = 'biome/vault_frozen.xml'; wang = 'wang_tiles/vault_frozen.png'; bindings = @('vault-frozen') },
    [ordered]@{ id = 'crypt'; biome = 'biome/crypt.xml'; wang = 'wang_tiles/crypt.png'; bindings = @('crypt') },
    [ordered]@{ id = 'wandcave'; biome = 'biome/wandcave.xml'; wang = 'wang_tiles/wand.png'; bindings = @('wandcave') },
    [ordered]@{ id = 'wizardcave'; biome = 'biome/wizardcave.xml'; wang = 'wang_tiles/wizardcave.png'; bindings = @('wizardcave', 'wizardcave-entrance') }
)

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Convert-ToSlashPath([string] $Path) {
    return $Path.Replace('\', '/')
}

function Convert-HexColor([string] $Value) {
    $normalized = $Value.Trim()
    if ($normalized.StartsWith('0x', [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(2)
    }

    if ($normalized.Length -eq 6) {
        $normalized = 'ff' + $normalized
    }

    if ($normalized.Length -ne 8 -or $normalized -notmatch '^[0-9a-fA-F]{8}$') {
        throw "Invalid color '$Value'."
    }

    return [uint32]::Parse(
        $normalized,
        [Globalization.NumberStyles]::HexNumber,
        [Globalization.CultureInfo]::InvariantCulture)
}

function Format-Color([uint32] $Color) {
    return $Color.ToString('x8', [Globalization.CultureInfo]::InvariantCulture)
}

function Format-Rgb([uint32] $Color) {
    return ($Color -band [uint32]0x00ffffff).ToString('x6', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-MaterialSemantic([string] $Name) {
    $nameLower = $Name.ToLowerInvariant()
    if ($nameLower -match 'lava|acid|radioactive|oil|gunpowder|poison|toxic|plasma|fire') {
        return $SemanticHazard
    }

    if ($nameLower -match 'water|teleportatium|slime|blood|liquid') {
        return $SemanticPool
    }

    if ($nameLower -match 'wood|steel|metal|temple|wizard|brick|slab') {
        return $SemanticStructure
    }

    if ($nameLower -match 'sand|snow|coal|gravel|soil|fungi|fungus|root|vine|gold|powder|slush') {
        return $SemanticLoose
    }

    return $SemanticSecondary
}

function Add-MaterialAlias(
    [hashtable] $Aliases,
    [System.Collections.Generic.List[string]] $Conflicts,
    [string] $ColorText,
    [string] $MaterialName,
    [string] $Origin,
    [int] $Priority) {
    if ([string]::IsNullOrWhiteSpace($ColorText)) {
        return
    }

    $color = Convert-HexColor $ColorText
    $key = Format-Rgb $color
    $semantic = Get-MaterialSemantic $MaterialName
    $candidate = [pscustomobject]@{
        color = ('ff' + $key)
        material = $MaterialName
        semantic = $semantic
        origin = $Origin
        priority = $Priority
    }

    if (-not $Aliases.ContainsKey($key)) {
        $Aliases[$key] = $candidate
        return
    }

    $existing = $Aliases[$key]
    if ($Priority -gt $existing.priority) {
        $Aliases[$key] = $candidate
        return
    }

    if ($Priority -eq $existing.priority -and $semantic -ne $existing.semantic) {
        $Conflicts.Add("${key}:$($existing.material)/$MaterialName")
    }
}

function Read-Bitmap([string] $Path) {
    $source = [Drawing.Bitmap]::new($Path)
    try {
        $rectangle = [Drawing.Rectangle]::new(0, 0, $source.Width, $source.Height)
        $bitmap = $source.Clone($rectangle, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    }
    finally {
        $source.Dispose()
    }

    try {
        $rectangle = [Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height)
        $locked = $bitmap.LockBits(
            $rectangle,
            [Drawing.Imaging.ImageLockMode]::ReadOnly,
            [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            if ($locked.Stride -le 0) {
                throw "Unsupported negative bitmap stride in '$Path'."
            }

            $bytes = [byte[]]::new($locked.Stride * $bitmap.Height)
            [Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $bytes, 0, $bytes.Length)
            return [pscustomobject]@{
                width = $bitmap.Width
                height = $bitmap.Height
                stride = $locked.Stride
                bytes = $bytes
            }
        }
        finally {
            $bitmap.UnlockBits($locked)
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-PixelArgb($Image, [int] $X, [int] $Y) {
    $offset = ($Y * $Image.stride) + ($X * 4)
    return [uint32](
        ([uint32]$Image.bytes[$offset + 3] -shl 24) -bor
        ([uint32]$Image.bytes[$offset + 2] -shl 16) -bor
        ([uint32]$Image.bytes[$offset + 1] -shl 8) -bor
        [uint32]$Image.bytes[$offset])
}

function Read-WangHeader($Image) {
    $header = [byte[]]::new(9)
    for ($i = 0; $i -lt $header.Length; $i++) {
        $x = $Image.width - 1 - [Math]::Floor($i / 3)
        $pixel = Get-PixelArgb $Image $x 0
        $encoded = switch ($i % 3) {
            0 { [byte]($pixel -band 0xff) }
            1 { [byte](($pixel -shr 8) -band 0xff) }
            2 { [byte](($pixel -shr 16) -band 0xff) }
        }

        $header[$i] = [byte]($encoded -bxor (($i * 55) -band 0xff))
    }

    if ($header[7] -ne 0xc0) {
        throw 'This extractor revision only accepts Noita corner-mode Herringbone Wang templates.'
    }

    if ($header[6] -eq 0 -or $header[4] -eq 0 -or $header[5] -eq 0) {
        throw 'Invalid Herringbone Wang header.'
    }

    for ($i = 0; $i -lt 4; $i++) {
        if ($header[$i] -eq 0 -or $header[$i] -gt 32) {
            throw 'Invalid corner color count in Herringbone Wang header.'
        }
    }

    return [pscustomobject]@{
        colors = @([int]$header[0], [int]$header[1], [int]$header[2], [int]$header[3])
        varyX = [int]$header[4]
        varyY = [int]$header[5]
        shortSide = [int]$header[6]
    }
}

function Add-TemplateRow(
    [System.Collections.Generic.List[object]] $Destination,
    [bool] $Horizontal,
    [int] $Y,
    [int] $ShortSide,
    [int] $A0, [int] $A1,
    [int] $B0, [int] $B1,
    [int] $C0, [int] $C1,
    [int] $D0, [int] $D1,
    [int] $E0, [int] $E1,
    [int] $F0, [int] $F1,
    [int] $Variants) {
    $x = 0
    for ($variant = 0; $variant -lt $Variants; $variant++) {
        for ($f = $F0; $f -le $F1; $f++) {
            for ($e = $E0; $e -le $E1; $e++) {
                for ($d = $D0; $d -le $D1; $d++) {
                    for ($c = $C0; $c -le $C1; $c++) {
                        for ($b = $B0; $b -le $B1; $b++) {
                            for ($a = $A0; $a -le $A1; $a++) {
                                $key = [uint32](
                                    $a -bor
                                    ($b -shl 5) -bor
                                    ($c -shl 10) -bor
                                    ($d -shl 15) -bor
                                    ($e -shl 20) -bor
                                    ($f -shl 25))
                                $Destination.Add([pscustomobject]@{
                                    key = $key
                                    x = $x + 1
                                    y = $Y + 1
                                    ordinal = $Destination.Count
                                })
                                $x += if ($Horizontal) { (2 * $ShortSide) + 3 } else { $ShortSide + 3 }
                            }
                        }
                    }
                }
            }
        }
    }
}

function Get-CornerTemplateTiles($Header, $Image) {
    $n = $Header.shortSide
    $colors = $Header.colors
    $horizontal = [System.Collections.Generic.List[object]]::new()
    $vertical = [System.Collections.Generic.List[object]]::new()
    $y = 2

    for ($k = 0; $k -lt $colors[2]; $k++) {
        for ($j = 0; $j -lt $colors[1]; $j++) {
            for ($i = 0; $i -lt $colors[0]; $i++) {
                for ($q = 0; $q -lt $Header.varyY; $q++) {
                    Add-TemplateRow $horizontal $true $y $n `
                        0 ($colors[1] - 1) 0 ($colors[2] - 1) 0 ($colors[3] - 1) `
                        $i $i $j $j $k $k $Header.varyX
                    $y += $n + 3
                }
            }
        }
    }

    $y += 2
    for ($k = 0; $k -lt $colors[3]; $k++) {
        for ($j = 0; $j -lt $colors[0]; $j++) {
            for ($i = 0; $i -lt $colors[1]; $i++) {
                for ($q = 0; $q -lt $Header.varyX; $q++) {
                    Add-TemplateRow $vertical $false $y $n `
                        0 ($colors[0] - 1) 0 ($colors[3] - 1) 0 ($colors[2] - 1) `
                        $i $i $j $j $k $k $Header.varyY
                    $y += (2 * $n) + 3
                }
            }
        }
    }

    if ($y -ne $Image.height) {
        throw "Template height mismatch: parsed=$y, image=$($Image.height)."
    }

    return [pscustomobject]@{
        horizontal = $horizontal
        vertical = $vertical
    }
}

function Get-TileColors(
    $Image,
    [System.Collections.Generic.List[object]] $Horizontal,
    [System.Collections.Generic.List[object]] $Vertical,
    [int] $ShortSide) {
    $colors = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($tile in $Horizontal) {
        for ($y = 0; $y -lt $ShortSide; $y++) {
            for ($x = 0; $x -lt 2 * $ShortSide; $x++) {
                [void]$colors.Add((Format-Color (Get-PixelArgb $Image ($tile.x + $x) ($tile.y + $y))))
            }
        }
    }

    foreach ($tile in $Vertical) {
        for ($y = 0; $y -lt 2 * $ShortSide; $y++) {
            for ($x = 0; $x -lt $ShortSide; $x++) {
                [void]$colors.Add((Format-Color (Get-PixelArgb $Image ($tile.x + $x) ($tile.y + $y))))
            }
        }
    }

    return $colors
}

function Get-ImageColors($Image) {
    $colors = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    for ($y = 0; $y -lt $Image.height; $y++) {
        for ($x = 0; $x -lt $Image.width; $x++) {
            [void]$colors.Add((Format-Color (Get-PixelArgb $Image $x $y)))
        }
    }

    return $colors
}

function Read-SpawnFunctions([string] $Path) {
    $functions = @{}
    foreach ($line in [IO.File]::ReadLines($Path)) {
        if ($line.TrimStart().StartsWith('--', [StringComparison]::Ordinal)) {
            continue
        }

        $match = [regex]::Match(
            $line,
            'RegisterSpawnFunction\s*\(\s*0x(?<color>[0-9a-fA-F]{8})\s*,\s*"(?<function>[^"]+)"')
        if ($match.Success) {
            $functions[$match.Groups['color'].Value.ToLowerInvariant()] = $match.Groups['function'].Value
        }
    }

    return $functions
}

function Get-RandomColorInputs([xml] $BiomeXml) {
    $inputs = @{}
    foreach ($node in $BiomeXml.SelectNodes('//RandomMaterials/RandomColor')) {
        $outputs = @(([string]$node.output_colors).Split(',', [StringSplitOptions]::RemoveEmptyEntries))
        if ($outputs.Count -eq 0) {
            throw 'RandomColor output_colors cannot be empty.'
        }

        foreach ($output in $outputs) {
            $rgb = Format-Rgb (Convert-HexColor $output)
            if ($rgb -ne '000000' -and $rgb -ne 'ffffff') {
                throw "Unsupported non-binary RandomColor output '$output'."
            }
        }

        $input = Convert-HexColor ([string]$node.input_color)
        $inputs[(Format-Rgb $input)] = $true
    }

    return $inputs
}

function Read-NoitaBiomeXml([string] $Path) {
    $source = [IO.File]::ReadAllText($Path)
    try {
        return [xml]$source
    }
    catch [Management.Automation.PSInvalidCastException] {
        # Noita's own loader accepts the duplicate limit_background_image in vault.xml.
        $sanitized = [regex]::Replace(
            $source,
            '(?s)(<Topology\b[^>]*?\blimit_background_image="[^"]*")(?<middle>[^>]*?)\s+limit_background_image="[^"]*"(?<tail>[^>]*>)',
            '${1}${middle}${tail}',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if ([string]::Equals($sanitized, $source, [StringComparison]::Ordinal)) {
            throw
        }

        return [xml]$sanitized
    }
}

function Get-XmlIntAttribute($Node, [string] $Name) {
    $attribute = $Node.Attributes[$Name]
    if ($null -eq $attribute) {
        return 0
    }

    return [int]::Parse(
        $attribute.Value,
        [Globalization.NumberStyles]::Integer,
        [Globalization.CultureInfo]::InvariantCulture)
}

function Get-XmlDoubleAttribute($Node, [string] $Name) {
    $attribute = $Node.Attributes[$Name]
    if ($null -eq $attribute) {
        return 0.0
    }

    return [double]::Parse(
        $attribute.Value,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture)
}

function Get-SemanticName([int] $Semantic) {
    switch ($Semantic) {
        0 { 'empty' }
        1 { 'primary' }
        2 { 'secondary' }
        3 { 'loose' }
        4 { 'structure' }
        5 { 'hazard' }
        6 { 'pool' }
        9 { 'random-binary' }
        default { throw "Unknown semantic $Semantic." }
    }
}

function Compress-Brotli([byte[]] $Bytes) {
    $output = [IO.MemoryStream]::new()
    try {
        $brotli = [IO.Compression.BrotliStream]::new(
            $output,
            [IO.Compression.CompressionLevel]::Optimal,
            $true)
        try {
            $brotli.Write($Bytes, 0, $Bytes.Length)
        }
        finally {
            $brotli.Dispose()
        }

        return $output.ToArray()
    }
    finally {
        $output.Dispose()
    }
}

function Get-ByteSha256([byte[]] $Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$resolvedInstallRoot = (Resolve-Path -LiteralPath $NoitaInstallRoot).Path
$materialsPath = Join-Path $resolvedDataRoot 'materials.xml'
$licensePath = Join-Path $resolvedInstallRoot 'licenses\stb_herringbone_wang_tile.txt'
if (-not (Test-Path -LiteralPath $materialsPath -PathType Leaf)) {
    throw "Noita materials.xml not found under '$resolvedDataRoot'."
}
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) {
    throw "Noita STB license not found under '$resolvedInstallRoot'."
}

[xml]$materialsXml = [IO.File]::ReadAllText($materialsPath)
$materialAliases = @{}
$materialConflicts = [System.Collections.Generic.List[string]]::new()
foreach ($material in $materialsXml.SelectNodes('//*[@name and @wang_color]')) {
    $materialName = [string]$material.name
    Add-MaterialAlias $materialAliases $materialConflicts ([string]$material.wang_color) $materialName 'wang-color' 2
    foreach ($graphics in $material.SelectNodes('.//Graphics[@color]')) {
        Add-MaterialAlias $materialAliases $materialConflicts ([string]$graphics.color) $materialName 'graphics-color' 1
    }
}

$sets = [System.Collections.Generic.List[object]]::new()
foreach ($specification in $specifications) {
    $biomePath = Join-Path $resolvedDataRoot $specification.biome
    $wangPath = Join-Path $resolvedDataRoot $specification.wang
    if (-not (Test-Path -LiteralPath $biomePath -PathType Leaf)) {
        throw "Missing source biome '$biomePath'."
    }
    if (-not (Test-Path -LiteralPath $wangPath -PathType Leaf)) {
        throw "Missing Wang template '$wangPath'."
    }

    [xml]$biomeXml = Read-NoitaBiomeXml $biomePath
    $topology = $biomeXml.Biome.Topology
    if ([string]$topology.type -ne 'BIOME_WANG_TILE') {
        throw "Biome '$($specification.id)' is not BIOME_WANG_TILE."
    }

    $declaredWang = [string]$topology.wang_template_file
    $expectedWang = 'data/' + (Convert-ToSlashPath $specification.wang)
    if ($declaredWang -ne $expectedWang) {
        throw "Biome '$($specification.id)' Wang path mismatch: '$declaredWang'."
    }

    $spawnSourcePath = [string]$topology.lua_script
    if ([string]::IsNullOrWhiteSpace($spawnSourcePath) -or -not $spawnSourcePath.StartsWith('data/', [StringComparison]::Ordinal)) {
        throw "Biome '$($specification.id)' has no data/ Lua source."
    }

    $spawnPath = Join-Path $resolvedDataRoot $spawnSourcePath.Substring(5)
    $spawnFunctions = Read-SpawnFunctions $spawnPath
    $randomInputs = Get-RandomColorInputs $biomeXml
    $image = Read-Bitmap $wangPath
    $header = Read-WangHeader $image
    $tiles = Get-CornerTemplateTiles $header $image
    $tileColors = Get-TileColors $image $tiles.horizontal $tiles.vertical $header.shortSide
    $semanticSourceColors = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($colorText in $tileColors) {
        [void]$semanticSourceColors.Add($colorText)
    }

    $bitmapCavesNode = $topology.SelectSingleNode('./BitmapCaves')
    $bitmapStructureSources = [System.Collections.Generic.List[object]]::new()
    if ($null -ne $bitmapCavesNode) {
        foreach ($structureNode in $bitmapCavesNode.SelectNodes('./structures/CaveStructure')) {
            $sourceImagePath = [string]$structureNode.image_file
            if ([string]::IsNullOrWhiteSpace($sourceImagePath) -or
                -not $sourceImagePath.StartsWith('data/', [StringComparison]::Ordinal)) {
                throw "Biome '$($specification.id)' CaveStructure has invalid image_file '$sourceImagePath'."
            }

            $sourceImageFile = Join-Path $resolvedDataRoot $sourceImagePath.Substring(5)
            if (-not (Test-Path -LiteralPath $sourceImageFile -PathType Leaf)) {
                throw "Biome '$($specification.id)' CaveStructure image not found: '$sourceImageFile'."
            }

            $structureImage = Read-Bitmap $sourceImageFile
            foreach ($colorText in (Get-ImageColors $structureImage)) {
                [void]$semanticSourceColors.Add($colorText)
            }

            $bitmapStructureSources.Add([pscustomobject]@{
                node = $structureNode
                sourceImagePath = $sourceImagePath
                sourceImageFile = $sourceImageFile
                image = $structureImage
            })
        }
    }

    $markerCandidates = @{}
    foreach ($colorText in $semanticSourceColors) {
        $color = Convert-HexColor $colorText
        $rgb = Format-Rgb $color
        if ($rgb -eq '000000' -or $randomInputs.ContainsKey($rgb) -or $materialAliases.ContainsKey($rgb)) {
            continue
        }

        $r = ($color -shr 16) -band 0xff
        $g = ($color -shr 8) -band 0xff
        $b = $color -band 0xff
        if ($r -eq $g -and $g -eq $b) {
            continue
        }

        $normalizedColor = Format-Color $color
        $markerCandidates[$normalizedColor] = if ($spawnFunctions.ContainsKey($normalizedColor)) {
            [pscustomobject]@{ function = $spawnFunctions[$normalizedColor]; origin = 'lua' }
        }
        else {
            [pscustomobject]@{ function = "builtin-or-unresolved-$normalizedColor"; origin = 'builtin-or-unresolved' }
        }
    }

    foreach ($colorText in $semanticSourceColors) {
        $normalizedColor = $colorText.ToLowerInvariant()
        if ($spawnFunctions.ContainsKey($normalizedColor) -and -not $markerCandidates.ContainsKey($normalizedColor)) {
            $markerCandidates[$normalizedColor] = [pscustomobject]@{
                function = $spawnFunctions[$normalizedColor]
                origin = 'lua'
            }
        }
    }

    $markerColors = @($markerCandidates.Keys | Sort-Object)
    if ($markerColors.Count -gt (256 - $SemanticMarkerBase)) {
        throw "Biome '$($specification.id)' has too many marker colors: $($markerColors.Count)."
    }

    $markerSemantics = @{}
    $markerDefinitions = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $markerColors.Count; $i++) {
        $colorText = $markerColors[$i]
        $markerSemantics[$colorText] = $SemanticMarkerBase + $i
        $marker = $markerCandidates[$colorText]
        $markerDefinitions.Add([ordered]@{
            color = $colorText
            function = $marker.function
            origin = $marker.origin
        })
    }

    $usedMaterialMappings = @{}
    function Resolve-Semantic([uint32] $Color) {
        $colorText = Format-Color $Color
        $rgb = Format-Rgb $Color
        if ($rgb -eq '000000') {
            return $SemanticEmpty
        }
        if ($randomInputs.ContainsKey($rgb)) {
            return $SemanticRandomBinary
        }
        if ($markerSemantics.ContainsKey($colorText)) {
            return [int]$markerSemantics[$colorText]
        }
        if ($materialAliases.ContainsKey($rgb)) {
            $mapping = $materialAliases[$rgb]
            $usedMaterialMappings[$rgb] = $mapping
            return [int]$mapping.semantic
        }

        $r = ($Color -shr 16) -band 0xff
        $g = ($Color -shr 8) -band 0xff
        $b = $Color -band 0xff
        if ($r -eq $g -and $g -eq $b) {
            return $SemanticPrimary
        }

        throw "Unclassified color '$colorText' in '$($specification.id)'."
    }

    $bitmapCavesDefinition = $null
    if ($null -ne $bitmapCavesNode) {
        $bitmapStructureDefinitions = [System.Collections.Generic.List[object]]::new()
        foreach ($source in $bitmapStructureSources) {
            $structureImage = $source.image
            $semanticPixels = [byte[]]::new($structureImage.width * $structureImage.height)
            for ($y = 0; $y -lt $structureImage.height; $y++) {
                for ($x = 0; $x -lt $structureImage.width; $x++) {
                    $semanticPixels[($y * $structureImage.width) + $x] =
                        [byte](Resolve-Semantic (Get-PixelArgb $structureImage $x $y))
                }
            }

            $compressedStructure = Compress-Brotli $semanticPixels
            $structureNode = $source.node
            $bitmapStructureDefinitions.Add([ordered]@{
                sourceImagePath = $source.sourceImagePath
                sourceImageSha256 = Get-Sha256 $source.sourceImageFile
                sourceWidth = $structureImage.width
                sourceHeight = $structureImage.height
                aabbMinX = Get-XmlIntAttribute $structureNode 'aabb_min_x'
                aabbMaxX = Get-XmlIntAttribute $structureNode 'aabb_max_x'
                aabbMinY = Get-XmlIntAttribute $structureNode 'aabb_min_y'
                aabbMaxY = Get-XmlIntAttribute $structureNode 'aabb_max_y'
                countMin = Get-XmlIntAttribute $structureNode 'count_min'
                countMax = Get-XmlIntAttribute $structureNode 'count_max'
                strengthMin = Get-XmlDoubleAttribute $structureNode 'strength_min'
                strengthMax = Get-XmlDoubleAttribute $structureNode 'strength_max'
                encoding = 'brotli-pebitmap-v1'
                decodedLength = $semanticPixels.Length
                decodedSha256 = Get-ByteSha256 $semanticPixels
                data = [Convert]::ToBase64String($compressedStructure)
            })
        }

        $bitmapCavesDefinition = [ordered]@{
            sizeX = Get-XmlIntAttribute $bitmapCavesNode 'size_x'
            sizeY = Get-XmlIntAttribute $bitmapCavesNode 'size_y'
            spawnPercent = Get-XmlDoubleAttribute $bitmapCavesNode 'spawn_percent'
            blobCavesCountMin = Get-XmlIntAttribute $bitmapCavesNode 'blob_caves_count_min'
            blobCavesCountMax = Get-XmlIntAttribute $bitmapCavesNode 'blob_caves_count_max'
            blobCavesRadiusMin = Get-XmlDoubleAttribute $bitmapCavesNode 'blob_caves_radius_min'
            blobCavesRadiusMax = Get-XmlDoubleAttribute $bitmapCavesNode 'blob_caves_radius_max'
            blobCavesStrengthMin = Get-XmlDoubleAttribute $bitmapCavesNode 'blob_caves_strength_min'
            blobCavesStrengthMax = Get-XmlDoubleAttribute $bitmapCavesNode 'blob_caves_strength_max'
            caveChildsMin = Get-XmlIntAttribute $bitmapCavesNode 'cave_childs_min'
            caveChildsMax = Get-XmlIntAttribute $bitmapCavesNode 'cave_childs_max'
            caveCountMin = Get-XmlIntAttribute $bitmapCavesNode 'cave_count_min'
            caveCountMax = Get-XmlIntAttribute $bitmapCavesNode 'cave_count_max'
            caveStrengthMin = Get-XmlDoubleAttribute $bitmapCavesNode 'cave_strength_min'
            caveStrengthMax = Get-XmlDoubleAttribute $bitmapCavesNode 'cave_strength_max'
            mountainCountMin = Get-XmlIntAttribute $bitmapCavesNode 'mountain_count_min'
            mountainCountMax = Get-XmlIntAttribute $bitmapCavesNode 'mountain_count_max'
            mountainSizeMin = Get-XmlDoubleAttribute $bitmapCavesNode 'mountain_size_min'
            mountainSizeMax = Get-XmlDoubleAttribute $bitmapCavesNode 'mountain_size_max'
            surfaceCaveChildsMin = Get-XmlIntAttribute $bitmapCavesNode 'surface_cave_childs_min'
            surfaceCaveChildsMax = Get-XmlIntAttribute $bitmapCavesNode 'surface_cave_childs_max'
            surfaceCavesCountMin = Get-XmlIntAttribute $bitmapCavesNode 'surface_caves_count_min'
            surfaceCavesCountMax = Get-XmlIntAttribute $bitmapCavesNode 'surface_caves_count_max'
            structures = @($bitmapStructureDefinitions)
        }
    }

    $horizontal = @($tiles.horizontal | Sort-Object key, ordinal)
    $vertical = @($tiles.vertical | Sort-Object key, ordinal)
    $decodedStream = [IO.MemoryStream]::new()
    try {
        $writer = [IO.BinaryWriter]::new($decodedStream, [Text.Encoding]::ASCII, $true)
        try {
            $writer.Write([Text.Encoding]::ASCII.GetBytes('PWH1'))
            $writer.Write([byte]$header.shortSide)
            foreach ($count in $header.colors) {
                $writer.Write([byte]$count)
            }
            $writer.Write([byte]$header.varyX)
            $writer.Write([byte]$header.varyY)
            $writer.Write([int]$horizontal.Count)
            $writer.Write([int]$vertical.Count)

            foreach ($tile in $horizontal) {
                $writer.Write([uint32]$tile.key)
                for ($y = 0; $y -lt $header.shortSide; $y++) {
                    for ($x = 0; $x -lt 2 * $header.shortSide; $x++) {
                        $semantic = Resolve-Semantic (Get-PixelArgb $image ($tile.x + $x) ($tile.y + $y))
                        $writer.Write([byte]$semantic)
                    }
                }
            }

            foreach ($tile in $vertical) {
                $writer.Write([uint32]$tile.key)
                for ($y = 0; $y -lt 2 * $header.shortSide; $y++) {
                    for ($x = 0; $x -lt $header.shortSide; $x++) {
                        $semantic = Resolve-Semantic (Get-PixelArgb $image ($tile.x + $x) ($tile.y + $y))
                        $writer.Write([byte]$semantic)
                    }
                }
            }
        }
        finally {
            $writer.Dispose()
        }

        $decoded = $decodedStream.ToArray()
    }
    finally {
        $decodedStream.Dispose()
    }

    $compressed = Compress-Brotli $decoded
    $mappingDefinitions = @(
        $usedMaterialMappings.Values |
            Sort-Object color, material |
            ForEach-Object {
                [ordered]@{
                    color = $_.color
                    material = $_.material
                    semantic = Get-SemanticName $_.semantic
                    origin = $_.origin
                }
            })
    $sets.Add([ordered]@{
        id = $specification.id
        referenceBiomeIds = @($specification.bindings)
        sourceBiomePath = 'data/' + (Convert-ToSlashPath $specification.biome)
        sourceBiomeSha256 = Get-Sha256 $biomePath
        sourceWangPath = $expectedWang
        sourceWangSha256 = Get-Sha256 $wangPath
        spawnSourcePath = $spawnSourcePath
        spawnSourceSha256 = Get-Sha256 $spawnPath
        sourceWidth = $image.width
        sourceHeight = $image.height
        shortSide = $header.shortSide
        cornerColors = @($header.colors)
        varyX = $header.varyX
        varyY = $header.varyY
        horizontalTileCount = $horizontal.Count
        verticalTileCount = $vertical.Count
        randomBinaryColors = @($randomInputs.Keys | Sort-Object | ForEach-Object { 'ff' + $_ })
        materialMappings = $mappingDefinitions
        markers = @($markerDefinitions)
        bitmapCaves = $bitmapCavesDefinition
        encoding = 'brotli-pewh-v1'
        decodedLength = $decoded.Length
        decodedSha256 = Get-ByteSha256 $decoded
        data = [Convert]::ToBase64String($compressed)
    })

    Write-Host (
        '{0,-18} {1}x{2} n={3} H={4} V={5} markers={6} decoded={7} compressed={8}' -f
        $specification.id,
        $image.width,
        $image.height,
        $header.shortSide,
        $horizontal.Count,
        $vertical.Count,
        $markerDefinitions.Count,
        $decoded.Length,
        $compressed.Length)
}

$document = [ordered]@{
    schemaVersion = 1
    referenceBuildId = $ReferenceBuildId
    referenceVersionHash = $ReferenceVersionHash
    algorithm = 'stb-herringbone-wang-corner-v1'
    algorithmLicensePath = 'licenses/stb_herringbone_wang_tile.txt'
    algorithmLicenseSha256 = Get-Sha256 $licensePath
    sourceMaterialsPath = 'data/materials.xml'
    sourceMaterialsSha256 = Get-Sha256 $materialsPath
    materialAliasConflicts = @($materialConflicts | Sort-Object -Unique)
    sets = @($sets)
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput))
$json = $document | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($resolvedOutput, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $resolvedOutput"
