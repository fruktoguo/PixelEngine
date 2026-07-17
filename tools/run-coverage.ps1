[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$OutputRoot = 'artifacts/coverage',

    [string]$FullTestResultsDirectory = '',

    [string]$RunId = '',

    [switch]$SkipBuild,

    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryRoot {
    $root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $root 'PixelEngine.sln') -PathType Leaf)) {
        throw "无法定位 PixelEngine.sln：$root"
    }

    return $root
}

function Resolve-AbsolutePath {
    param([string]$Root, [string]$Path)

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

$repoRoot = Resolve-RepositoryRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$output = Resolve-AbsolutePath -Root $repoRoot -Path $OutputRoot
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Coverage 输出必须位于 artifacts/ 内：$output"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
[void][IO.Directory]::CreateDirectory($output)

$policyPath = Join-Path $repoRoot 'tools/coverage-policy.json'
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ($policy.schema -ne 'pixelengine.coverage-policy/v1') {
    throw "不支持的 coverage policy schema：$($policy.schema)"
}

$behaviorResults = Join-Path $output 'behavior-tests'
[void][IO.Directory]::CreateDirectory($behaviorResults)
$fullResults = if ([string]::IsNullOrWhiteSpace($FullTestResultsDirectory)) {
    $path = Join-Path $output 'full-tests'
    [void][IO.Directory]::CreateDirectory($path)
    $path
}
else {
    Resolve-AbsolutePath -Root $repoRoot -Path $FullTestResultsDirectory
}

$commitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw '无法读取当前完整 Git commit。'
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID)) {
        "github-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)"
    }
    else {
        'local-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture)
    }
}

Push-Location $repoRoot
try {
    Invoke-DotNet -Arguments @('build-server', 'shutdown')
    if (-not $SkipBuild) {
        $buildArguments = @('build', 'PixelEngine.sln', '-c', $Configuration, '--disable-build-servers', '-m:1')
        if ($NoRestore) {
            $buildArguments += '--no-restore'
        }
        Invoke-DotNet -Arguments $buildArguments
        Invoke-DotNet -Arguments @('build-server', 'shutdown')
    }

    if ([string]::IsNullOrWhiteSpace($FullTestResultsDirectory)) {
        $fullArguments = @(
            'test', 'PixelEngine.sln', '-c', $Configuration, '--no-build', '--disable-build-servers', '-m:1',
            '--logger', 'trx', '--results-directory', $fullResults
        )
        if ($NoRestore -or -not $SkipBuild) {
            $fullArguments += '--no-restore'
        }
        Invoke-DotNet -Arguments $fullArguments
    }

    $behaviorArguments = @(
        'test', 'PixelEngine.sln', '-c', $Configuration, '--no-build', '--disable-build-servers', '-m:1',
        '--settings', (Join-Path $repoRoot 'tools/coverage.runsettings'),
        '--collect', 'XPlat Code Coverage',
        '--filter', [string]$policy.testClassification.behaviorFilter,
        '--logger', 'trx',
        '--results-directory', $behaviorResults
    )
    if ($NoRestore -or -not $SkipBuild) {
        $behaviorArguments += '--no-restore'
    }
    Invoke-DotNet -Arguments $behaviorArguments

    & (Join-Path $repoRoot 'tools/merge-coverage.ps1') `
        -CoverageResultsDirectory $behaviorResults `
        -FullTestResultsDirectory $fullResults `
        -BehaviorTestResultsDirectory $behaviorResults `
        -OutputDirectory (Join-Path $output 'report') `
        -PolicyPath $policyPath `
        -SourceProjectsRoot (Join-Path $repoRoot 'src') `
        -RunId $RunId `
        -CommitSha $commitSha
    if (-not $?) {
        throw 'Coverage 聚合失败。'
    }
}
finally {
    Pop-Location
}

Write-Host "Coverage gate passed: $(Join-Path $output 'report/coverage-summary.md')"
