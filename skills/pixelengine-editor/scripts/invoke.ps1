$ErrorActionPreference = 'Stop'

$cliPath = $env:PIXELENGINE_EDITOR_CLI
if ([string]::IsNullOrWhiteSpace($cliPath)) {
    $installed = Get-Command pixelengine-editor -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $installed) {
        $cliPath = $installed.Source
    }
}

if ([string]::IsNullOrWhiteSpace($cliPath)) {
    $directory = Get-Item -LiteralPath (Get-Location).Path
    while ($null -ne $directory) {
        if (Test-Path -LiteralPath (Join-Path $directory 'PixelEngine.sln')) {
            $base = Join-Path $directory 'tools\PixelEngine.Editor.Cli\bin\Release\net10.0'
            $executable = Join-Path $base 'pixelengine-editor.exe'
            $assembly = Join-Path $base 'pixelengine-editor.dll'
            $cliPath = if (Test-Path -LiteralPath $executable) {
                $executable
            } elseif (Test-Path -LiteralPath $assembly) {
                $assembly
            } else {
                $null
            }
            break
        }

        $directory = $directory.Parent
    }
}

if ([string]::IsNullOrWhiteSpace($cliPath) -or -not (Test-Path -LiteralPath $cliPath)) {
    throw 'pixelengine-editor was not found. Set PIXELENGINE_EDITOR_CLI, install it on PATH, or build the Release CLI in a PixelEngine checkout.'
}

if ([string]::Equals([IO.Path]::GetExtension($cliPath), '.dll', [StringComparison]::OrdinalIgnoreCase)) {
    & dotnet $cliPath @args
} else {
    & $cliPath @args
}

exit $LASTEXITCODE
