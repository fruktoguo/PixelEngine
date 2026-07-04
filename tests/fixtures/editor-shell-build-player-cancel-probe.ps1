$ErrorActionPreference = 'Stop'

$outputIndex = [Array]::IndexOf($args, '-Output')
if ($outputIndex -lt 0 -or $outputIndex + 1 -ge $args.Count) {
    throw 'cancel probe build-player requires -Output.'
}

$output = [IO.Path]::GetFullPath([string]$args[$outputIndex + 1])
New-Item -ItemType Directory -Path $output -Force | Out-Null

$marker = Join-Path $output 'cancel-first-run.marker'
$childPidPath = Join-Path $output 'cancel-child.pid'

if (-not (Test-Path -LiteralPath $marker)) {
    Set-Content -LiteralPath $marker -Value 'started' -Encoding UTF8
    Write-Output (@{
        schema = 'pixelengine.build/v1'
        kind = 'Progress'
        phase = 'publish'
        percent = 35
        level = 'Info'
        message = 'cancel probe spawned dotnet child and is waiting'
        ts = (Get-Date).ToUniversalTime().ToString('O')
    } | ConvertTo-Json -Compress)

    $sleeperRoot = Join-Path $output 'cancel-dotnet-sleeper'
    New-Item -ItemType Directory -Path $sleeperRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $sleeperRoot 'CancelProbeSleeper.csproj') -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
    Set-Content -LiteralPath (Join-Path $sleeperRoot 'Program.cs') -Encoding UTF8 -Value @'
Thread.Sleep(TimeSpan.FromMinutes(10));
'@

    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $child = Start-Process -FilePath $dotnet -ArgumentList @('run', '--project', $sleeperRoot, '-c', 'Release') -PassThru -WindowStyle Hidden
    Set-Content -LiteralPath $childPidPath -Value $child.Id.ToString([Globalization.CultureInfo]::InvariantCulture) -Encoding ASCII
    while ($true) {
        Start-Sleep -Seconds 1
    }
}

$real = Join-Path (Get-Location) 'tools/build-player.ps1'
& $real @args
exit $LASTEXITCODE
