param(
    [Parameter(Mandatory)][ValidateSet('Install', 'Launch', 'Doctor', 'Finish')][string]$Action,
    [string]$RunDir,
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2',
    [string]$SteamExe = 'D:\Steam\steam.exe',
    [string]$BuildDir,
    [switch]$HostBridge
)
$ErrorActionPreference = 'Stop'
if ($PSBoundParameters.ContainsKey('HostBridge') -and $Action -ne 'Launch') { throw 'HostBridge is supported only for Launch.' }
$project = Split-Path $PSScriptRoot -Parent
$evidenceRoot = Join-Path $project 'artifacts\verification'
$lockPath = Join-Path $evidenceRoot 'active.lock'
$exe = Join-Path $GameDir 'SlayTheSpire2.exe'
function Write-Json($Value, $Path) {
    $Value | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $Path -Encoding utf8
}
function Game-Processes {
    @(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue)
}
function Identity($Process) {
    [ordered]@{ pid=$Process.Id; startedUtc=$Process.StartTime.ToUniversalTime().ToString('o'); executable=$Process.Path }
}
if ($Action -in @('Install', 'Launch')) {
    if (!(Test-Path -LiteralPath $exe)) { throw 'Game executable not found.' }
    if ((Game-Processes).Count) { throw 'A pre-existing game session is running. Leave it untouched.' }
    if (Test-Path -LiteralPath $lockPath) { throw 'Another verification run owns active.lock. Reconcile that run first.' }
    $release = Get-Content -LiteralPath (Join-Path $GameDir 'release_info.json') -Raw | ConvertFrom-Json
    if ($release.version -notin @('v0.113.0', 'v0.113.1')) { throw "Expected a checked v0.113.x release, found $($release.version). Recheck compatibility first." }
}
if ($Action -eq 'Install') {
    if (!$BuildDir) { throw 'BuildDir containing LocalAiTeammate.dll and LocalAiTeammate.json is required.' }
    $BuildDir = (Resolve-Path -LiteralPath $BuildDir).Path
    $files = @('LocalAiTeammate.dll', 'LocalAiTeammate.json')
    foreach ($file in $files) {
        if (!(Test-Path -LiteralPath (Join-Path $BuildDir $file))) { throw "Missing $file in BuildDir." }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $BuildDir 'LocalAiTeammate.json') -Raw | ConvertFrom-Json
    if ($manifest.id -ne 'LocalAiTeammate') { throw 'Unexpected mod identity.' }
    $destination = Join-Path $GameDir 'mods\LocalAiTeammate'
    if ((Test-Path -LiteralPath $destination) -and !(Test-Path -LiteralPath (Join-Path $destination '.local-ai-development'))) {
        throw 'Existing mod directory is not owned by this development install. Leave it untouched.'
    }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Set-Content -LiteralPath (Join-Path $destination '.local-ai-development') -Value $project
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $BuildDir $file) -Destination $destination }
    if (Test-Path -LiteralPath (Join-Path $BuildDir 'config')) {
        Copy-Item -LiteralPath (Join-Path $BuildDir 'config') -Destination $destination -Recurse -Force
    }
    $installed = foreach ($file in $files) { Get-FileHash -LiteralPath (Join-Path $destination $file) -Algorithm SHA256 | Select-Object Path,Hash }
    $installed | ConvertTo-Json
    exit 0
}
if ($Action -eq 'Launch') {
    if (!(Test-Path -LiteralPath (Join-Path $GameDir 'mods\LocalAiTeammate\LocalAiTeammate.dll'))) { throw 'Install the built mod before gameplay verification.' }
    if (!(Test-Path -LiteralPath $SteamExe) -or !(Get-Process -Name steam -ErrorAction SilentlyContinue)) { throw 'Steam must already be running and authenticated.' }
    New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
    $lock = [System.IO.File]::Open($lockPath, 'CreateNew', 'Write', 'None')
    try {
        $RunDir = Join-Path $evidenceRoot ('local-ai-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
        New-Item -ItemType Directory -Path $RunDir | Out-Null
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($RunDir)
        $lock.Write($bytes,0,$bytes.Length)
    } finally { $lock.Dispose() }
    $session = [ordered]@{ scope='local-ai-gameplay'; status='launching'; runDir=$RunDir; requestedUtc=[datetime]::UtcNow.ToString('o'); process=$null; gameDir=$GameDir; gameVersion=$release; log=(Join-Path $RunDir 'game.log'); verification='NOT_TESTED' }
    Write-Json $session (Join-Path $RunDir 'session.json')
    Import-Module (Join-Path $PSScriptRoot 'LocalAiLaunch.psm1') -Force
    $launch = New-LocalAiSteamLaunch -RunDir $RunDir -GameDir $GameDir -HostBridge:$HostBridge
    if ($launch.HostBridge) {
        $session.hostBridge = $launch.HostBridge
        Write-Json $session (Join-Path $RunDir 'session.json')
    }
    Get-ChildItem -LiteralPath (Join-Path $GameDir 'mods') -Directory | Select-Object -ExpandProperty Name | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $RunDir 'installed-mods.json')
    Start-Process -FilePath $SteamExe -ArgumentList $launch.Arguments -WindowStyle Hidden
    $deadline = [datetime]::UtcNow.AddSeconds(45)
    do {
        $games = Game-Processes
        if ($games.Count -gt 1) { throw "Multiple game instances. Reconcile $RunDir; lock retained." }
        if ($games.Count -eq 1) {
            $session.process = Identity $games[0]
            $session.status = 'awaiting-window'
            Write-Json $session (Join-Path $RunDir 'session.json')
            if ($games[0].MainWindowHandle -ne 0) { break }
        }
        Start-Sleep -Milliseconds 250
    } while ([datetime]::UtcNow -lt $deadline)
    if (!$session.process -or !$games[0].MainWindowHandle) { throw "Launch timed out. Inspect $RunDir and any late process before retrying. Lock retained." }
    $session.status = 'awaiting-gameplay-verification'
    Write-Json $session (Join-Path $RunDir 'session.json')
    $RunDir
    exit 0
}
if (!$RunDir) { throw 'RunDir is required.' }
$RunDir = (Resolve-Path -LiteralPath $RunDir).Path
if (!(Test-Path -LiteralPath $lockPath) -or (Get-Content -LiteralPath $lockPath -Raw).Trim() -ne $RunDir) { throw 'This run does not own active.lock.' }
$session = Get-Content -LiteralPath (Join-Path $RunDir 'session.json') -Raw | ConvertFrom-Json
if ($session.scope -ne 'local-ai-gameplay' -or $session.runDir -ne $RunDir -or !$session.process) { throw 'Invalid or unbound gameplay session.' }
$process = Get-Process -Id $session.process.pid -ErrorAction SilentlyContinue
$same = $process -and $process.Path -eq $session.process.executable -and $process.StartTime.ToUniversalTime().Ticks -eq ([datetime]$session.process.startedUtc).ToUniversalTime().Ticks
if ($Action -eq 'Doctor') {
    $doctor = [ordered]@{ sameOwnedProcess=[bool]$same; responding=$false; windowHandle=$null; title=$null; checkedUtc=[datetime]::UtcNow.ToString('o') }
    if ($same) { $doctor.responding=$process.Responding; $doctor.windowHandle=$process.MainWindowHandle.ToInt64(); $doctor.title=$process.MainWindowTitle }
    Write-Json $doctor (Join-Path $RunDir 'doctor.json')
    $doctor | ConvertTo-Json
    if (!$same -or !$doctor.responding -or !$doctor.windowHandle) { exit 2 }
    exit 0
}
if ($same) { throw 'Close the owned instance normally through Computer Use before Finish.' }
if ($process -or (Game-Processes).Count) { throw 'Another or reused game process exists. Leave it untouched and reconcile.' }
$session.status = 'closed'
Write-Json $session (Join-Path $RunDir 'session.json')
Write-Json ([ordered]@{scope='local-ai-gameplay';ownedProcessExited=$true;gameplay='SEE_GAMEPLAY_EVIDENCE';automaticPass=$false;finishedUtc=[datetime]::UtcNow.ToString('o')}) (Join-Path $RunDir 'cleanup.json')
Remove-Item -LiteralPath $lockPath
"Closed owned test process; evidence retained at $RunDir"
