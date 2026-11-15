param(
    [string]$Sts2DataDir = 'E:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64',
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget/packages'
$projectFile = Join-Path $projectRoot 'src/LocalAiTeammate/LocalAiTeammate.csproj'
$buildArgs = @('build', $projectFile, '--nologo', '-c', 'Release', "-p:Sts2DataDir=$Sts2DataDir")
if ($NoRestore) { $buildArgs += '--no-restore' }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "LocalAiTeammate build failed ($LASTEXITCODE)." }
$output = Join-Path $projectRoot 'artifacts/build/LocalAiTeammate'
foreach ($name in @('LocalAiTeammate.dll', 'LocalAiTeammate.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $output $name))) { throw "Missing build artifact $name" }
}
Write-Output "Build artifacts ready at $output"
