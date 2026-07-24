param(
    [Parameter(Mandatory = $true)]
    [string] $ArchivePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputRoot,

    [string[]] $Prefix = @('data/'),

    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($output) | Out-Null
$outputBoundary = $output.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar

$normalizedPrefixes = @($Prefix | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_)) {
        throw 'WAK prefix cannot be empty.'
    }

    $_.Replace('\', '/').TrimStart('/')
})

$stream = [IO.File]::Open($archive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
$reader = [IO.BinaryReader]::new($stream, [Text.Encoding]::UTF8, $true)
try {
    if ($stream.Length -lt 16) {
        throw 'Noita WAK is shorter than its 16-byte header.'
    }

    $format = $reader.ReadUInt32()
    $entryCount = $reader.ReadUInt32()
    $dataOffset = $reader.ReadUInt32()
    $reserved = $reader.ReadUInt32()
    if ($format -ne 0 -or $reserved -ne 0 -or $entryCount -eq 0 -or $dataOffset -lt 16 -or $dataOffset -gt $stream.Length) {
        throw "Unsupported Noita WAK header: format=$format entries=$entryCount dataOffset=$dataOffset reserved=$reserved."
    }

    $entries = [Collections.Generic.List[object]]::new([int]$entryCount)
    for ($index = 0; $index -lt $entryCount; $index++) {
        if ($stream.Position + 12 -gt $dataOffset) {
            throw "Noita WAK entry table ended early at entry $index."
        }

        $offset = $reader.ReadUInt32()
        $length = $reader.ReadUInt32()
        $pathLength = $reader.ReadUInt32()
        if ($pathLength -eq 0 -or $pathLength -gt 32KB -or $stream.Position + $pathLength -gt $dataOffset) {
            throw "Noita WAK entry $index has invalid path length $pathLength."
        }

        $path = [Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$pathLength)).Replace('\', '/')
        if ($path.StartsWith('/') -or $path.Contains('../') -or $path.Contains('/..') -or [IO.Path]::IsPathRooted($path)) {
            throw "Noita WAK entry $index contains an unsafe path: $path"
        }
        if ([uint64]$offset + [uint64]$length -gt [uint64]$stream.Length -or $offset -lt $dataOffset) {
            throw "Noita WAK entry $index is outside archive bounds: $path"
        }

        if ($normalizedPrefixes.Where({ $path.StartsWith($_, [StringComparison]::Ordinal) }, 'First').Count -gt 0) {
            $entries.Add([pscustomobject]@{ Path = $path; Offset = [long]$offset; Length = [int]$length })
        }
    }

    if ($stream.Position -ne $dataOffset) {
        throw "Noita WAK index ended at $($stream.Position), expected data offset $dataOffset."
    }

    $buffer = [byte[]]::new(1MB)
    $writtenBytes = 0L
    foreach ($entry in $entries) {
        $target = [IO.Path]::GetFullPath((Join-Path $output $entry.Path.Replace('/', [IO.Path]::DirectorySeparatorChar)))
        if (-not $target.StartsWith($outputBoundary, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Noita WAK entry escaped output root: $($entry.Path)"
        }
        if ((Test-Path -LiteralPath $target) -and -not $Force) {
            throw "Output file already exists; pass -Force to replace it: $target"
        }

        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        $stream.Position = $entry.Offset
        $remaining = $entry.Length
        $destination = [IO.File]::Open($target, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            while ($remaining -gt 0) {
                $read = $stream.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))
                if ($read -le 0) {
                    throw "Unexpected EOF while extracting $($entry.Path)."
                }

                $destination.Write($buffer, 0, $read)
                $remaining -= $read
                $writtenBytes += $read
            }
        }
        finally {
            $destination.Dispose()
        }
    }

    Write-Host "Noita WAK extracted: entries=$($entries.Count) bytes=$writtenBytes output=$output"
}
finally {
    $reader.Dispose()
    $stream.Dispose()
}
