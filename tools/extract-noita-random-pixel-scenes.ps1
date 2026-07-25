[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $WorldCatalogPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-world-content.json'),

    [string] $OutputPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-random-pixel-scenes.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$world = Get-Content -LiteralPath $WorldCatalogPath -Raw | ConvertFrom-Json -Depth 30

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Asset([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $file = Join-Path $resolvedDataRoot $Path.Substring(5)
    if (-not [IO.File]::Exists($file)) { throw "Missing random Pixel Scene asset '$Path'." }
    return [ordered]@{
        path = $Path
        length = [IO.FileInfo]::new($file).Length
        sha256 = Get-Sha256 $file
    }
}

function Find-MatchingBrace([string] $Text, [int] $OpenIndex) {
    $depth = 0
    $quote = [char]0
    $lineComment = $false
    $blockComment = $false
    for ($i = $OpenIndex; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        $next = if ($i + 1 -lt $Text.Length) { $Text[$i + 1] } else { [char]0 }
        if ($lineComment) {
            if ($c -eq "`n") { $lineComment = $false }
            continue
        }
        if ($blockComment) {
            if ($c -eq ']' -and $next -eq ']') { $blockComment = $false; $i++ }
            continue
        }
        if ($quote -ne [char]0) {
            if ($c -eq '\') { $i++; continue }
            if ($c -eq $quote) { $quote = [char]0 }
            continue
        }
        if (($c -eq '"') -or ($c -eq "'")) { $quote = $c; continue }
        if ($c -eq '-' -and $next -eq '-') {
            if ($i + 3 -lt $Text.Length -and $Text[$i + 2] -eq '[' -and $Text[$i + 3] -eq '[') {
                $blockComment = $true
                $i += 3
            }
            else { $lineComment = $true; $i++ }
            continue
        }
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') {
            $depth--
            if ($depth -eq 0) { return $i }
        }
    }
    throw "Unbalanced Lua table starting at offset $OpenIndex."
}

function Get-StringField([string] $Block, [string] $Name) {
    $match = [regex]::Match($Block, "(?m)\b$Name\s*=\s*`"([^`"]*)`"")
    return $(if ($match.Success) { $match.Groups[1].Value } else { '' })
}

$biomeByScript = @{}
foreach ($biome in $world.biomes) {
    if ($null -eq $biome.lua) { continue }
    $sourceProperty = $biome.lua.PSObject.Properties['path']
    if ($null -ne $sourceProperty -and -not [string]::IsNullOrWhiteSpace([string]$sourceProperty.Value)) {
        $biomeByScript[[string]$sourceProperty.Value] = [string]$biome.id
    }
}

$catalogs = [Collections.Generic.List[object]]::new()
$scriptRoot = Join-Path $resolvedDataRoot 'scripts\biomes'
foreach ($file in Get-ChildItem -LiteralPath $scriptRoot -Filter '*.lua' -Recurse -File | Sort-Object FullName) {
    $sourcePath = 'data/' + [IO.Path]::GetRelativePath($resolvedDataRoot, $file.FullName).Replace('\', '/')
    $text = [IO.File]::ReadAllText($file.FullName)
    $bindings = @{}
    foreach ($call in [regex]::Matches($text, 'load_random_pixel_scene\s*\(\s*(g_[A-Za-z0-9_]+)\s*,')) {
        $functionMatches = [regex]::Matches(
            $text.Substring(0, $call.Index),
            '(?m)^\s*function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(')
        if ($functionMatches.Count -eq 0) { continue }
        $function = $functionMatches[$functionMatches.Count - 1].Groups[1].Value
        $table = $call.Groups[1].Value
        if (-not $bindings.ContainsKey($table)) { $bindings[$table] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
        [void]$bindings[$table].Add($function)
    }
    if ($bindings.Count -eq 0) { continue }

    foreach ($tableName in @($bindings.Keys | Sort-Object)) {
        $header = [regex]::Match($text, "(?m)^\s*$tableName\s*=\s*\{")
        if (-not $header.Success) { continue }
        $open = $text.IndexOf('{', $header.Index)
        $close = Find-MatchingBrace $text $open
        $body = $text.Substring($open + 1, $close - $open - 1)
        $entries = [Collections.Generic.List[object]]::new()
        $cursor = 0
        while ($cursor -lt $body.Length) {
            $entryOpen = $body.IndexOf('{', $cursor)
            if ($entryOpen -lt 0) { break }
            $entryClose = Find-MatchingBrace $body $entryOpen
            $block = $body.Substring($entryOpen + 1, $entryClose - $entryOpen - 1)
            $material = Get-StringField $block 'material_file'
            if (-not [string]::IsNullOrWhiteSpace($material)) {
                $probability = [regex]::Match($block, '(?m)\bprob\s*=\s*([-+0-9.eE]+)')
                $unique = [regex]::Match($block, '(?m)\bis_unique\s*=\s*([01])')
                $colorMaterial = [regex]::Match($block, '(?m)\bcolor_material\s*=\s*([A-Za-z_][A-Za-z0-9_]*)')
                $entries.Add([ordered]@{
                    probability = $(if ($probability.Success) { [double]::Parse($probability.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { 1.0 })
                    material = Get-Asset $material
                    visual = Get-Asset (Get-StringField $block 'visual_file')
                    background = Get-Asset (Get-StringField $block 'background_file')
                    isUnique = $unique.Success -and $unique.Groups[1].Value -eq '1'
                    colorMaterialTable = $(if ($colorMaterial.Success) { $colorMaterial.Groups[1].Value } else { '' })
                })
            }
            $cursor = $entryClose + 1
        }
        if ($entries.Count -eq 0) { continue }
        $catalogs.Add([ordered]@{
            biomeId = $(if ($biomeByScript.ContainsKey($sourcePath)) { $biomeByScript[$sourcePath] } else { '' })
            sourcePath = $sourcePath
            sourceSha256 = Get-Sha256 $file.FullName
            table = $tableName
            functions = @($bindings[$tableName] | Sort-Object)
            entries = $entries
        })
    }
}

$result = [ordered]@{
    schema = 'pixelengine.noita-random-pixel-scenes/v1'
    reference = [ordered]@{
        steamBuildId = [string]$world.reference.steamBuildId
        versionHash = [string]$world.reference.versionHash
    }
    statistics = [ordered]@{
        catalogs = $catalogs.Count
        entries = @($catalogs | ForEach-Object entries).Count
        sourceScripts = @($catalogs | ForEach-Object sourcePath | Sort-Object -Unique).Count
        boundBiomes = @($catalogs | ForEach-Object biomeId | Where-Object { $_ } | Sort-Object -Unique).Count
        materialAssets = @($catalogs | ForEach-Object entries | ForEach-Object material | ForEach-Object path | Sort-Object -Unique).Count
        visualAssets = @($catalogs | ForEach-Object entries | ForEach-Object visual | Where-Object { $null -ne $_ } | ForEach-Object path | Sort-Object -Unique).Count
        backgroundAssets = @($catalogs | ForEach-Object entries | ForEach-Object background | Where-Object { $null -ne $_ } | ForEach-Object path | Sort-Object -Unique).Count
    }
    catalogs = $catalogs
}
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($OutputPath),
    ($result | ConvertTo-Json -Depth 20) + "`n",
    $utf8NoBom)
$result.statistics
