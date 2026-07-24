[CmdletBinding()]
param(
    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-material-catalog.json'),
    [string] $WangCatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-wang-terrain.json'),
    [string] $MaterialsPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\materials.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Convert-ArgbToBgraUInt([string] $Value) {
    $normalized = $Value.Trim().TrimStart('#')
    if ($normalized.Length -eq 6) {
        $normalized = 'ff' + $normalized
    }
    if ($normalized.Length -ne 8 -or $normalized -notmatch '^[0-9a-fA-F]{8}$') {
        throw "Invalid ARGB color '$Value'."
    }

    $a = $normalized.Substring(0, 2)
    $r = $normalized.Substring(2, 2)
    $g = $normalized.Substring(4, 2)
    $b = $normalized.Substring(6, 2)
    return [Convert]::ToUInt32($a + $b + $g + $r, 16)
}

function Get-Attribute($Attributes, [string] $Name, $Fallback = $null) {
    $property = $Attributes.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Fallback
    }
    return $property.Value
}

function Merge-Attributes($Parent, $Child) {
    $merged = [ordered]@{}
    if ($null -ne $Parent) {
        foreach ($property in $Parent.PSObject.Properties) {
            $merged[$property.Name] = $property.Value
        }
    }
    foreach ($property in $Child.PSObject.Properties) {
        $merged[$property.Name] = $property.Value
    }
    return [pscustomobject]$merged
}

function Parse-Tags([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }
    return @([regex]::Matches($Value, '\[([^\]]+)\]') | ForEach-Object { $_.Groups[1].Value })
}

function Get-Graphics($Declaration, $ResolvedParent) {
    $source = @($Declaration.childXml | Where-Object { $_ -like '<Graphics*' } | Select-Object -Last 1)
    if ($source.Count -eq 0 -and $null -ne $ResolvedParent) {
        return $ResolvedParent.Graphics
    }
    if ($source.Count -eq 0) {
        return $null
    }
    return [xml]$source[0]
}

$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
$wangCatalog = Get-Content -LiteralPath $WangCatalogPath -Raw | ConvertFrom-Json
$materialsDocument = Get-Content -LiteralPath $MaterialsPath -Raw | ConvertFrom-Json

$declarations = @{}
foreach ($declaration in $catalog.declarations) {
    $declarations[$declaration.name] = $declaration
}

$resolved = @{}
function Resolve-Declaration([string] $Name) {
    if ($resolved.ContainsKey($Name)) {
        return $resolved[$Name]
    }
    if (-not $declarations.ContainsKey($Name)) {
        throw "Noita material '$Name' has no declaration."
    }

    $declaration = $declarations[$Name]
    $parent = if ([string]::IsNullOrWhiteSpace([string]$declaration.parent)) {
        $null
    } else {
        Resolve-Declaration ([string]$declaration.parent)
    }
    $parentAttributes = if ($null -eq $parent) { $null } else { $parent.Attributes }
    $entry = [pscustomobject]@{
        Attributes = Merge-Attributes $parentAttributes $declaration.attributes
        Graphics = Get-Graphics $declaration $parent
    }
    $resolved[$Name] = $entry
    return $entry
}

$requiredNames = @($wangCatalog.sets.materialMappings.material | Sort-Object -Unique)
$reusedNames = @('lava', 'oil', 'sand', 'water')
$existingByName = @{}
foreach ($material in $materialsDocument.materials) {
    $existingByName[$material.name] = $material
}
$materialsDocument.materials = @(
    $materialsDocument.materials |
        Where-Object { $_.name -notin $requiredNames -or $_.name -in $reusedNames })
$existingByName = @{}
foreach ($material in $materialsDocument.materials) {
    $existingByName[$material.name] = $material
}

$generated = [System.Collections.Generic.List[object]]::new()
foreach ($name in $requiredNames) {
    if ($existingByName.ContainsKey($name)) {
        continue
    }

    $source = Resolve-Declaration $name
    $attributes = $source.Attributes
    $tags = Parse-Tags ([string](Get-Attribute $attributes 'tags' ''))
    $cellType = [string](Get-Attribute $attributes 'cell_type' 'liquid')
    $liquidStatic = (Get-Attribute $attributes 'liquid_static' '0') -eq '1'
    $liquidSand = (Get-Attribute $attributes 'liquid_sand' '0') -eq '1'
    $type = if ($cellType -eq 'fire') {
        'Fire'
    } elseif ($cellType -eq 'gas') {
        'Gas'
    } elseif ($cellType -eq 'solid' -or $liquidStatic) {
        'Solid'
    } elseif ($liquidSand) {
        'Powder'
    } else {
        'Liquid'
    }

    $graphicsElement = if ($null -eq $source.Graphics) { $null } else { $source.Graphics.DocumentElement }
    $colorText = if ($null -ne $graphicsElement -and $graphicsElement.HasAttribute('color')) {
        $graphicsElement.GetAttribute('color')
    } else {
        [string](Get-Attribute $attributes 'wang_color' 'ff808080')
    }
    $baseColor = Convert-ArgbToBgraUInt $colorText
    $opacity = [byte](($baseColor -shr 24) -band 0xff)
    $density = [Math]::Clamp(
        [int][Math]::Round([double](Get-Attribute $attributes 'density' '10') * 10.0),
        1,
        255)
    $hp = [Math]::Max(0, [int][double](Get-Attribute $attributes 'hp' '0'))
    $burnable = (Get-Attribute $attributes 'burnable' '0') -eq '1'
    $glow = [Math]::Clamp([int][double](Get-Attribute $attributes 'gfx_glow' '0'), 0, 255)
    $runtimeTags = [System.Collections.Generic.List[string]]::new()
    foreach ($tag in $tags) {
        $runtimeTag = switch -Regex ($tag) {
            '^corrodible$' { 'corrodible'; break }
            '^static$' { 'static'; break }
            '^fire$' { 'fire'; break }
            '^acid$' { 'acid'; break }
            '^frozen$' { 'cold'; break }
            '^meltable' { 'meltable'; break }
            default { $null }
        }
        if ($null -ne $runtimeTag -and -not $runtimeTags.Contains($runtimeTag)) {
            $runtimeTags.Add($runtimeTag)
        }
    }
    if ((Get-Attribute $attributes 'electrical_conductivity' '0') -eq '1' -and
        -not $runtimeTags.Contains('conductive')) {
        $runtimeTags.Add('conductive')
    }
    if ($type -eq 'Solid' -and -not $runtimeTags.Contains('static')) {
        $runtimeTags.Add('static')
    }
    if ($glow -gt 0 -and -not $runtimeTags.Contains('emissive')) {
        $runtimeTags.Add('emissive')
    }
    $dispersion = if ($type -eq 'Liquid') { 4 } elseif ($type -eq 'Powder') { 1 } else { 0 }
    $flammability = if ($burnable) { 100 } else { 0 }
    $legendCategory = if ($type -eq 'Liquid' -or $type -eq 'Gas') { $type } else { 'Terrain' }

    $material = [ordered]@{
        name = $name
        type = $type
        density = $density
        dispersion = $dispersion
        liquidStatic = $liquidStatic
        liquidSand = $liquidSand
        flammability = $flammability
        autoIgnitionTemp = [Math]::Clamp([int][double](Get-Attribute $attributes 'autoignition_temperature' '0'), 0, 65535)
        fireHp = [int][double](Get-Attribute $attributes 'fire_hp' '0')
        temperatureOfFire = [Math]::Clamp([int][double](Get-Attribute $attributes 'temperature_of_fire' '0'), 0, 255)
        generatesSmoke = [Math]::Clamp([int][double](Get-Attribute $attributes 'generates_smoke' '0'), 0, 255)
        heatConduct = 32
        heatCapacity = 1.0
        durability = [Math]::Clamp([int][Math]::Ceiling($hp / 500.0), 0, 255)
        integrity = [Math]::Clamp([int][Math]::Ceiling($hp / 10.0), 0, 65535)
        renderStyle = $type
        legendCategory = $legendCategory
        edgeColor = 0
        opacity = $opacity
        highlightColor = 0
        displayName = $name.Replace('_', ' ')
        legendVisible = $false
        baseColor = $baseColor
        colorNoise = [Math]::Clamp([int][Math]::Round([double](Get-Attribute $attributes 'wang_noise_percent' '0') * 32.0), 0, 32)
        tags = $runtimeTags.ToArray()
    }
    $generated.Add([pscustomobject]$material)
}

$materialsDocument.materials = @($materialsDocument.materials) + @($generated)
$json = ($materialsDocument | ConvertTo-Json -Depth 20).Replace("`r`n", "`n") + "`n"
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($MaterialsPath),
    $json,
    [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    RequiredWangMaterials = $requiredNames.Count
    ExistingMaterialsReused = $requiredNames.Count - $generated.Count
    GeneratedMaterials = $generated.Count
    TotalRuntimeMaterials = $materialsDocument.materials.Count
}
