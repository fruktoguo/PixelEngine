param(
    [string]$Configuration = "Release",
    [string[]]$Project = @(),
    [string]$Filter = "",
    [switch]$SkipBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot {
    $directory = Resolve-Path (Join-Path $PSScriptRoot "..")
    while ($null -ne $directory) {
        if (Test-Path (Join-Path $directory "PixelEngine.sln")) {
            return $directory.Path
        }

        $parent = Split-Path -Parent $directory
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $directory.Path) {
            break
        }

        $directory = Resolve-Path $parent
    }

    throw "无法定位 PixelEngine.sln。"
}

function Resolve-RepositoryPath {
    param(
        [string]$Root,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Project path must not be empty."
    }

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$root = Resolve-RepositoryRoot

if ($Project.Count -eq 0) {
    $Project = Get-ChildItem -LiteralPath (Join-Path $root "tests") -Recurse -Filter "*.csproj" |
        Sort-Object FullName |
        ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) }
}

$testProjects = @(
    foreach ($entry in $Project) {
        $path = Resolve-RepositoryPath -Root $root -Path $entry
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Test project 不存在：$entry"
        }

        $path
    }
)

Push-Location $root
try {
    Invoke-DotNet -Arguments @("build-server", "shutdown")

    if (-not $SkipBuild) {
        $buildArguments = @("build", "PixelEngine.sln", "-c", $Configuration, "--disable-build-servers", "-m:1")
        if ($NoRestore) {
            $buildArguments += "--no-restore"
        }

        Invoke-DotNet $buildArguments
        Invoke-DotNet -Arguments @("build-server", "shutdown")
    }

    foreach ($projectPath in $testProjects) {
        Write-Host "Running tests: $([IO.Path]::GetRelativePath($root, $projectPath))"
        $testArguments = @(
            "test",
            $projectPath,
            "-c",
            $Configuration,
            "--no-build",
            "--disable-build-servers",
            "-m:1",
            "--logger",
            "console;verbosity=minimal"
        )

        if ($NoRestore -or -not $SkipBuild) {
            $testArguments += "--no-restore"
        }

        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $testArguments += @("--filter", $Filter)
        }

        Invoke-DotNet $testArguments
    }
}
finally {
    Pop-Location
}
