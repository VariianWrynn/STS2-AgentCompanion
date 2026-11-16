$ErrorActionPreference = 'Stop'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $repoRoot 'artifacts/verification/issued-turn-red'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $evidence 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $evidence 'packages'
& python (Join-Path $PSScriptRoot 'generate.py')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$project = Join-Path $evidence 'Regression.csproj'
& dotnet restore $project --configfile (Join-Path $evidence 'NuGet.Config') --ignore-failed-sources 2>&1 | Tee-Object (Join-Path $evidence 'restore.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet run --project $project --no-restore 2>&1 | Tee-Object (Join-Path $evidence 'run.log')
exit $LASTEXITCODE
