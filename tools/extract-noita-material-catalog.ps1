[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DataRoot,

    [string] $OutputPath = (Join-Path $PSScriptRoot '..\demo\PixelEngine.Demo\content\noita-material-catalog.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ReferenceBuildId = '17130612'
$ReferenceVersionHash = '9dbd52ced019a643169a2db02f46c77f8766c6e5'
$ExpectedMaterialsSha256 = '122df34514edaf312e1a15a619b3d6a44d49ce605c929d5950c9051a57429d04'
$ExpectedDeclarationCount = 468
$ExpectedUniqueMaterialCount = 466
$ExpectedReactionCount = 325
$ExpectedRequiredReactionCount = 5

function Convert-Attributes {
    param([System.Xml.XmlElement] $Element)

    $attributes = [ordered]@{}
    foreach ($attribute in @($Element.Attributes | Sort-Object LocalName)) {
        $attributes[$attribute.LocalName] = $attribute.Value
    }

    return $attributes
}

function Convert-ChildXml {
    param([System.Xml.XmlElement] $Element)

    $children = [System.Collections.Generic.List[string]]::new()
    foreach ($child in @($Element.ChildNodes)) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $children.Add($child.OuterXml)
        }
    }

    return $children.ToArray()
}

$resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
$materialsPath = Join-Path $resolvedDataRoot 'materials.xml'
if (-not (Test-Path -LiteralPath $materialsPath -PathType Leaf)) {
    throw "Noita materials.xml not found under '$resolvedDataRoot'."
}

$sourceHash = (Get-FileHash -LiteralPath $materialsPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceHash -ne $ExpectedMaterialsSha256) {
    throw "materials.xml SHA256 mismatch. Expected $ExpectedMaterialsSha256, actual $sourceHash."
}

$document = [System.Xml.XmlDocument]::new()
$document.PreserveWhitespace = $false
$document.Load($materialsPath)
if ($null -eq $document.DocumentElement -or $document.DocumentElement.Name -ne 'Materials') {
    throw 'materials.xml root must be Materials.'
}

$declarations = [System.Collections.Generic.List[object]]::new()
$reactions = [System.Collections.Generic.List[object]]::new()
$requiredReactions = [System.Collections.Generic.List[object]]::new()
$materialNames = [System.Collections.Generic.List[string]]::new()

$declarationOrdinal = 0
$reactionOrdinal = 0
$requiredReactionOrdinal = 0
foreach ($node in @($document.DocumentElement.ChildNodes)) {
    if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) {
        continue
    }

    $element = [System.Xml.XmlElement]$node
    switch ($element.LocalName) {
        'CellData' {
            if ([string]::IsNullOrWhiteSpace($element.GetAttribute('name'))) {
                throw "CellData declaration $declarationOrdinal has no name."
            }

            $materialNames.Add($element.GetAttribute('name'))
            $declarations.Add([ordered]@{
                ordinal = $declarationOrdinal++
                kind = 'CellData'
                name = $element.GetAttribute('name')
                parent = $null
                inheritReactions = $false
                attributes = Convert-Attributes $element
                childXml = @((Convert-ChildXml $element))
            })
        }
        'CellDataChild' {
            if ([string]::IsNullOrWhiteSpace($element.GetAttribute('name'))) {
                throw "CellDataChild declaration $declarationOrdinal has no name."
            }

            $parent = $element.GetAttribute('_parent')
            if ([string]::IsNullOrWhiteSpace($parent)) {
                throw "CellDataChild '$($element.GetAttribute('name'))' has no _parent."
            }

            $materialNames.Add($element.GetAttribute('name'))
            $declarations.Add([ordered]@{
                ordinal = $declarationOrdinal++
                kind = 'CellDataChild'
                name = $element.GetAttribute('name')
                parent = $parent
                inheritReactions = $element.GetAttribute('_inherit_reactions') -eq '1'
                attributes = Convert-Attributes $element
                childXml = @((Convert-ChildXml $element))
            })
        }
        'Reaction' {
            $reactions.Add([ordered]@{
                ordinal = $reactionOrdinal++
                attributes = Convert-Attributes $element
                childXml = @((Convert-ChildXml $element))
            })
        }
        'ReqReaction' {
            $requiredReactions.Add([ordered]@{
                ordinal = $requiredReactionOrdinal++
                attributes = Convert-Attributes $element
                childXml = @((Convert-ChildXml $element))
            })
        }
        default {
            throw "Unsupported materials.xml element '$($element.LocalName)'."
        }
    }
}

$uniqueNames = @($materialNames | Sort-Object -Unique)
$duplicateNames = @(
    $materialNames |
        Group-Object |
        Where-Object Count -gt 1 |
        Sort-Object Name |
        ForEach-Object {
            [ordered]@{
                name = $_.Name
                declarationCount = $_.Count
            }
        }
)

if ($declarations.Count -ne $ExpectedDeclarationCount) {
    throw "Material declaration count mismatch. Expected $ExpectedDeclarationCount, actual $($declarations.Count)."
}
if ($uniqueNames.Count -ne $ExpectedUniqueMaterialCount) {
    throw "Unique material count mismatch. Expected $ExpectedUniqueMaterialCount, actual $($uniqueNames.Count)."
}
if ($reactions.Count -ne $ExpectedReactionCount) {
    throw "Reaction count mismatch. Expected $ExpectedReactionCount, actual $($reactions.Count)."
}
if ($requiredReactions.Count -ne $ExpectedRequiredReactionCount) {
    throw "ReqReaction count mismatch. Expected $ExpectedRequiredReactionCount, actual $($requiredReactions.Count)."
}

$catalog = [ordered]@{
    schemaVersion = 1
    reference = [ordered]@{
        game = 'Noita'
        buildId = $ReferenceBuildId
        versionHash = $ReferenceVersionHash
        sourcePath = 'data/materials.xml'
        sourceSha256 = $sourceHash
    }
    counts = [ordered]@{
        declarations = $declarations.Count
        uniqueMaterials = $uniqueNames.Count
        reactions = $reactions.Count
        requiredReactions = $requiredReactions.Count
    }
    duplicateNames = $duplicateNames
    declarations = $declarations.ToArray()
    reactions = $reactions.ToArray()
    requiredReactions = $requiredReactions.ToArray()
}

$json = $catalog | ConvertTo-Json -Depth 100
$json = $json.Replace("`r`n", "`n") + "`n"
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[System.IO.File]::WriteAllText(
    $resolvedOutput,
    $json,
    [System.Text.UTF8Encoding]::new($false))

$outputHash = (Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256).Hash.ToLowerInvariant()
[pscustomobject]@{
    OutputPath = $resolvedOutput
    OutputSha256 = $outputHash
    Declarations = $declarations.Count
    UniqueMaterials = $uniqueNames.Count
    Reactions = $reactions.Count
    RequiredReactions = $requiredReactions.Count
}
