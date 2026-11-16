param([string]$InstalledGameDir)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalAiLaunch.psm1') -Force
$project = Split-Path $PSScriptRoot -Parent
$root = Join-Path $project ('artifacts\verification\host-launcher\checks with spaces-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$checks = [System.Collections.Generic.List[string]]::new()
function Assert($Condition, $Message) {
    if (!$Condition) { throw $Message }
    $checks.Add($Message)
}
$game = Join-Path $root 'fixture game'
$data = Join-Path $game 'data_sts2_windows_x86_64'
New-Item -ItemType Directory -Path $data | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $data 'sts2.dll'), [byte[]](1, 2, 3, 4, 5))
$plain = New-LocalAiSteamLaunch -RunDir $root -GameDir $game
Assert ($null -eq $plain.HostBridge -and $plain.Arguments.Count -eq 4) 'Ordinary launch preserves its four Steam arguments.'
Assert (!(Test-Path -LiteralPath (Join-Path $root 'host-bridge.json'))) 'Ordinary launch creates no bridge config.'
Assert (($plain.Arguments -join ' ') -eq ('-applaunch 2868840 --log-file "' + (Join-Path $root 'game.log') + '"')) 'Ordinary log argument preserves spaces.'
$tokens = [System.Collections.Generic.HashSet[string]]::new()
foreach ($index in 1..8) {
    $run = Join-Path $root "run $index"
    New-Item -ItemType Directory -Path $run | Out-Null
    $plan = New-LocalAiSteamLaunch -RunDir $run -GameDir $game -HostBridge
    $configPath = $plan.HostBridge.configPath
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    Assert (($config.PSObject.Properties.Name -join ',') -ceq 'Port,Token,ExpectedAssemblySha256') "Config $index contains the exact case-sensitive schema."
    Assert ($config.Token -cmatch '^[0-9A-F]{32}$' -and $tokens.Add($config.Token)) "Config $index has a unique 32-hex token."
    Assert ($config.ExpectedAssemblySha256 -eq (Get-FileHash -LiteralPath (Join-Path $data 'sts2.dll')).Hash) "Config $index hashes the selected game assembly."
    Assert ($config.Port -ge 1024 -and $config.Port -le 65535 -and (Get-Item -LiteralPath $configPath).Length -lt 4096) "Config $index meets bridge port and file-size bounds."
    Assert ([System.IO.Path]::IsPathFullyQualified($configPath) -and (Split-Path $configPath -Parent) -eq $run) "Config $index stays in its owned absolute run directory."
    $public = $plan | ConvertTo-Json -Depth 5
    Assert (!$public.Contains($config.Token) -and $public -cnotmatch '"Token"') "Launch plan $index exposes no token."
}
$capture = Join-Path $root 'received arguments.json'
$receiver = Join-Path $root 'receive arguments.ps1'
Set-Content -LiteralPath $receiver -Value ('[Environment]::GetCommandLineArgs() | ConvertTo-Json | Set-Content -LiteralPath ''' + $capture.Replace("'", "''") + '''')
$pwsh = (Get-Process -Id $PID).Path
$child = Start-Process -FilePath $pwsh -ArgumentList (@('-NoProfile', '-File', ('"' + $receiver + '"')) + $plan.Arguments) -WindowStyle Hidden -Wait -PassThru
Assert ($child.ExitCode -eq 0) 'Native argument receiver exits successfully.'
$received = @(Get-Content -LiteralPath $capture -Raw | ConvertFrom-Json)
Assert ($received[-1] -ceq ('--local-ai-host-bridge-config=' + $plan.HostBridge.configPath)) 'Native Windows argument parsing preserves the whole config path with spaces.'
Assert ($received[-2] -ceq '--' -and $received[-3] -ceq (Join-Path $run 'game.log')) 'The bridge follows the user-argument separator and the quoted log path.'
foreach ($action in @('Install', 'Doctor', 'Finish')) {
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'verify-local-ai.ps1') -Action $action -HostBridge } catch { $rejected = $_.Exception.Message -eq 'HostBridge is supported only for Launch.' }
    Assert $rejected "HostBridge rejects $action before game or session checks."
}
$rejected = $false
try { New-LocalAiSteamLaunch -RunDir $run -GameDir $game -HostBridge | Out-Null } catch { $rejected = $true }
Assert $rejected 'Existing bridge config is never overwritten.'
if ($InstalledGameDir) {
    $run = Join-Path $root 'installed assembly'
    New-Item -ItemType Directory -Path $run | Out-Null
    $plan = New-LocalAiSteamLaunch -RunDir $run -GameDir $InstalledGameDir -HostBridge
    $release = Get-Content -LiteralPath (Join-Path $InstalledGameDir 'release_info.json') -Raw | ConvertFrom-Json
    Assert ($plan.HostBridge.expectedAssemblySha256 -eq (Get-FileHash -LiteralPath (Join-Path $InstalledGameDir 'data_sts2_windows_x86_64\sts2.dll')).Hash) 'Installed assembly hash matches the generated public launch metadata.'
    $checks.Add('Installed release read without launching the game: ' + $release.version)
}
$report = [ordered]@{ status = 'PASS'; scope = 'offline-launcher'; gameLaunched = $false; steamDeliveryVerified = $false; checks = $checks.ToArray() }
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'report.json')
"PASS: $($checks.Count) offline checks. Evidence: $root"
