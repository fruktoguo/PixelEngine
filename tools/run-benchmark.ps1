param(
    [string]$Project = "bench/PixelEngine.Benchmarks/PixelEngine.Benchmarks.csproj",
    [string]$Artifacts = "artifacts/benchmark-run",
    [switch]$KeepWorkspace,
    [string[]]$BenchmarkDotNetArgs = @(),
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingBenchmarkDotNetArgs = @()
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
        throw "Path must not be empty."
    }

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Test-IsPathInside {
    param(
        [string]$Parent,
        [string]$Child
    )

    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $childFull = [IO.Path]::GetFullPath($Child).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)
}

function Expand-BenchmarkDotNetArguments {
    param([string[]]$Arguments)

    foreach ($argument in $Arguments) {
        $separator = $argument.IndexOf('=', [StringComparison]::Ordinal)
        if ($argument.StartsWith('--', [StringComparison]::Ordinal) -and $separator -gt 2) {
            $argument.Substring(0, $separator)
            $argument.Substring($separator + 1)
        } else {
            $argument
        }
    }
}

function Copy-RepositoryForBenchmark {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    $excludedDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @(
        ".git",
        ".claude",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "out",
        "artifacts",
        "BenchmarkDotNet.Artifacts",
        "TestResults",
        "app",
        "publish"
    )) {
        $excludedDirectories.Add($name) | Out-Null
    }

    $sourceRootFull = [IO.Path]::GetFullPath($SourceRoot)
    $stack = [System.Collections.Generic.Stack[string]]::new()
    $stack.Push($sourceRootFull)

    while ($stack.Count -gt 0) {
        $current = $stack.Pop()
        $relative = [IO.Path]::GetRelativePath($sourceRootFull, $current)
        $targetDirectory = if ($relative -eq ".") { $DestinationRoot } else { Join-Path $DestinationRoot $relative }
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null

        foreach ($entry in Get-ChildItem -LiteralPath $current -Force) {
            if ($entry.PSIsContainer) {
                if (-not $excludedDirectories.Contains($entry.Name)) {
                    $stack.Push($entry.FullName)
                }

                continue
            }

            Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $targetDirectory $entry.Name) -Force
        }
    }
}

$BenchmarkDotNetArgs = @(
    Expand-BenchmarkDotNetArguments -Arguments @($BenchmarkDotNetArgs + $RemainingBenchmarkDotNetArgs)
)

if ($BenchmarkDotNetArgs -contains "--artifacts") {
    throw "请通过 -Artifacts 指定输出目录，不要在 BenchmarkDotNetArgs 中直接传 --artifacts。"
}

$root = Resolve-RepositoryRoot
$projectPath = Resolve-RepositoryPath -Root $root -Path $Project
$artifactsPath = Resolve-RepositoryPath -Root $root -Path $Artifacts

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Benchmark project 不存在：$Project"
}

if (-not (Test-IsPathInside -Parent $root -Child $artifactsPath)) {
    throw "Benchmark artifacts path 必须位于仓库内：$artifactsPath"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("pixelengine-benchmark-workspace-" + [Guid]::NewGuid().ToString("N"))
$isolatedArtifacts = Join-Path $tempRoot "BenchmarkDotNet.Artifacts"

try {
    Copy-RepositoryForBenchmark -SourceRoot $root -DestinationRoot $tempRoot

    $relativeProject = [IO.Path]::GetRelativePath($root, $projectPath)
    $isolatedProject = Join-Path $tempRoot $relativeProject

    if (Test-Path -LiteralPath $artifactsPath) {
        Remove-Item -LiteralPath $artifactsPath -Recurse -Force
    }

    $arguments = @("run", "--project", $isolatedProject, "-c", "Release", "--") + $BenchmarkDotNetArgs + @("--artifacts", $isolatedArtifacts)

    Push-Location $tempRoot
    try {
        & dotnet @arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if (Test-Path -LiteralPath $isolatedArtifacts) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $artifactsPath) | Out-Null
        Copy-Item -LiteralPath $isolatedArtifacts -Destination $artifactsPath -Recurse -Force
    }

    if ($exitCode -ne 0) {
        throw "BenchmarkDotNet run failed with exit code $exitCode."
    }

    if (-not (Test-Path -LiteralPath $artifactsPath -PathType Container)) {
        throw "BenchmarkDotNet artifacts were not produced. See requested path: $artifactsPath"
    }

    $reportFiles = @(Get-ChildItem -LiteralPath $artifactsPath -Recurse -File -Include "*report*.md", "*report*.csv", "*report*.html" -ErrorAction SilentlyContinue)
    if ($reportFiles.Count -eq 0) {
        throw "BenchmarkDotNet produced no report files. See artifacts: $artifactsPath"
    }

    $diagnosticFiles = @(Get-ChildItem -LiteralPath $artifactsPath -Recurse -File -Include "*.log", "*report*.md", "*report*.html", "*report*.csv" -ErrorAction SilentlyContinue)
    $diagnostics = ($diagnosticFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
    if ($diagnostics.Contains("ERROR(S):") -or
        $diagnostics.Contains("Generate Exception") -or
        $diagnostics.Contains("There are not any results runs") -or
        $diagnostics.Contains("No Workload Results were obtained") -or
        $diagnostics.Contains("Benchmarks with issues") -or
        $diagnostics.Contains("DllNotFoundException") -or
        $diagnostics -notmatch "executed benchmarks:\s*[1-9][0-9]*\b") {
        throw "BenchmarkDotNet run produced no executable benchmark results. See artifacts: $artifactsPath"
    }
}
finally {
    if ($KeepWorkspace) {
        Write-Host "Kept isolated benchmark workspace: $tempRoot"
    }
    elseif (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
