param(
    [string]$Configuration = "Release",
    [ValidateSet("Auto", "Fast", "UI", "Physics", "Performance", "Full")]
    [string]$Profile = "Auto",
    [string[]]$Project = @(),
    [string]$Filter = "",
    [switch]$ListOnly,
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

function Get-ChangedPaths([string]$Root) {
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($arguments in @(
        @("diff", "--name-only", "--diff-filter=ACMRTUXB"),
        @("diff", "--cached", "--name-only", "--diff-filter=ACMRTUXB")
    )) {
        $lines = & git -C $Root @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "无法读取 Git 改动路径。"
        }
        foreach ($line in $lines) {
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                [void]$paths.Add($line.Replace('\', '/'))
            }
        }
    }
    return @($paths | Sort-Object)
}

function Add-TestProject(
    [Collections.Generic.HashSet[string]]$Projects,
    [string]$Name
) {
    [void]$Projects.Add("tests/$Name/$Name.csproj")
}

function Resolve-ProfileProjects([string]$Root, [string]$SelectedProfile) {
    $projects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    switch ($SelectedProfile) {
        "Fast" {
            foreach ($name in @(
                "PixelEngine.Core.Tests",
                "PixelEngine.Content.Tests",
                "PixelEngine.Serialization.Tests",
                "PixelEngine.World.Tests"
            )) { Add-TestProject $projects $name }
        }
        "UI" {
            foreach ($name in @(
                "PixelEngine.UI.Tests",
                "PixelEngine.Rendering.Tests",
                "PixelEngine.Hosting.Tests"
            )) { Add-TestProject $projects $name }
        }
        "Physics" {
            foreach ($name in @(
                "PixelEngine.Physics.Tests",
                "PixelEngine.Simulation.Tests",
                "PixelEngine.World.Tests"
            )) { Add-TestProject $projects $name }
        }
        "Full" {
            Get-ChildItem -LiteralPath (Join-Path $Root "tests") -Recurse -Filter "*.csproj" |
                Sort-Object FullName |
                ForEach-Object { [void]$projects.Add([IO.Path]::GetRelativePath($Root, $_.FullName)) }
        }
        "Auto" {
            $changedPaths = Get-ChangedPaths $Root
            foreach ($path in $changedPaths) {
                switch -Regex ($path) {
                    '^src/PixelEngine\.Core/' { Add-TestProject $projects "PixelEngine.Core.Tests"; continue }
                    '^src/PixelEngine\.Content/' { Add-TestProject $projects "PixelEngine.Content.Tests"; continue }
                    '^src/PixelEngine\.Serialization/' { Add-TestProject $projects "PixelEngine.Serialization.Tests"; continue }
                    '^src/PixelEngine\.World/' { Add-TestProject $projects "PixelEngine.World.Tests"; continue }
                    '^src/PixelEngine\.Simulation/' { Add-TestProject $projects "PixelEngine.Simulation.Tests"; continue }
                    '^src/PixelEngine\.Physics/' { Add-TestProject $projects "PixelEngine.Physics.Tests"; continue }
                    '^src/PixelEngine\.Audio/' { Add-TestProject $projects "PixelEngine.Audio.Tests"; continue }
                    '^src/PixelEngine\.Scripting/' { Add-TestProject $projects "PixelEngine.Scripting.Tests"; continue }
                    '^src/PixelEngine\.UI/|^native/ui_native/' { Add-TestProject $projects "PixelEngine.UI.Tests"; Add-TestProject $projects "PixelEngine.Hosting.Tests"; continue }
                    '^src/PixelEngine\.Rendering/' { Add-TestProject $projects "PixelEngine.Rendering.Tests"; continue }
                    '^src/PixelEngine\.Editor\.Automation' { Add-TestProject $projects "PixelEngine.Editor.Automation.Tests"; continue }
                    '^src/PixelEngine\.Editor/|^apps/PixelEngine\.Editor\.Shell/' { Add-TestProject $projects "PixelEngine.Editor.Tests"; Add-TestProject $projects "PixelEngine.Hosting.Tests"; continue }
                    '^src/PixelEngine\.Hosting/' { Add-TestProject $projects "PixelEngine.Hosting.Tests"; continue }
                    '^demo/PixelEngine\.Demo/' { Add-TestProject $projects "PixelEngine.Demo.Tests"; continue }
                    '^tools/|^schema/|^installer/' { Add-TestProject $projects "PixelEngine.Hosting.Tests"; continue }
                    '^tests/(?<name>PixelEngine\.[^/]+\.Tests)/' { Add-TestProject $projects $Matches.name; continue }
                }
            }
            Write-Host "Auto profile changed paths: $($changedPaths.Count); selected projects: $($projects.Count)"
        }
    }

    return @($projects | Sort-Object)
}

$root = Resolve-RepositoryRoot

if ($Project.Count -eq 0) {
    if ($Profile -eq "Performance") {
        & (Join-Path $PSScriptRoot "run-benchmark.ps1")
        if ($LASTEXITCODE -ne 0) {
            throw "Performance profile failed with exit code $LASTEXITCODE."
        }
        exit 0
    }
    $Project = Resolve-ProfileProjects -Root $root -SelectedProfile $Profile
}

if ($Project.Count -eq 0) {
    Write-Host "No tests selected for profile '$Profile'."
    exit 0
}

if ($ListOnly) {
    Write-Host "Test profile: $Profile"
    $Project | ForEach-Object { Write-Host "  $_" }
    exit 0
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

    if (-not $SkipBuild -and $Profile -eq "Full" -and $Project.Count -gt 0) {
        $buildArguments = @("build", "PixelEngine.sln", "-c", $Configuration, "--disable-build-servers", "-m:1")
        if ($NoRestore) { $buildArguments += "--no-restore" }
        Invoke-DotNet $buildArguments
        Invoke-DotNet -Arguments @("build-server", "shutdown")
    }

    foreach ($projectPath in $testProjects) {
        if (-not $SkipBuild -and $Profile -ne "Full") {
            $projectBuildArguments = @("build", $projectPath, "-c", $Configuration, "--disable-build-servers", "-m:1")
            if ($NoRestore) { $projectBuildArguments += "--no-restore" }
            Invoke-DotNet $projectBuildArguments
            Invoke-DotNet -Arguments @("build-server", "shutdown")
        }
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
