param(
    [Parameter(Mandatory)][ValidateSet('Launch','Doctor','Finish')][string]$Action,
    [string]$RunDir,
    [string]$ProjectRoot = 'D:\salythespire2_agent',
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2',
    [string]$SteamExe = 'D:\Steam\steam.exe'
)
$ErrorActionPreference = 'Stop'
function Write-Json($Value, $Path) {
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8
}
function Game-Processes {
    @(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue)
}
function Identity($Process) {
    [ordered]@{ pid=$Process.Id; startedUtc=$Process.StartTime.ToUniversalTime().ToString('o'); executable=$Process.Path }
}
if ($Action -eq 'Launch') {
    if (!(Test-Path -LiteralPath (Join-Path $GameDir 'SlayTheSpire2.exe'))) { throw 'Game executable not found.' }
    if (!(Test-Path -LiteralPath $SteamExe)) { throw 'Steam executable not found.' }
    if ((Game-Processes).Count -ne 0) { throw 'Existing game session: refuse to launch or adopt it. Leave it untouched.' }
    if (@(Get-Process -Name steam -ErrorAction SilentlyContinue).Count -eq 0) { throw 'Steam is not running. Start and authenticate Steam yourself, then retry.' }
    $evidenceRoot = Join-Path $ProjectRoot 'artifacts\verification'
    New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
    $lockPath = Join-Path $evidenceRoot 'active.lock'
    $lock = [System.IO.File]::Open($lockPath, 'CreateNew', 'Write', 'None')
    try {
        $runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
        $RunDir = Join-Path $evidenceRoot $runName
        New-Item -ItemType Directory -Path $RunDir | Out-Null
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($RunDir)
        $lock.Write($bytes,0,$bytes.Length)
    } finally { $lock.Dispose() }
    $session = [ordered]@{
        scope='game-shell-smoke'; status='launching'; runDir=$RunDir
        requestedUtc=[DateTime]::UtcNow.ToString('o'); steamExe=$SteamExe; gameDir=$GameDir
        baselineGamePids=@(); process=$null; companionStatus='NOT_TESTED'
        lockPath=$lockPath
    }
    Write-Json $session (Join-Path $RunDir 'session.json')
    Get-Content -LiteralPath (Join-Path $GameDir 'release_info.json') | Set-Content -LiteralPath (Join-Path $RunDir 'game-version.json') -Encoding utf8
    @(Get-ChildItem -LiteralPath (Join-Path $GameDir 'mods') -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $RunDir 'installed-mods.json') -Encoding utf8
    Start-Process -FilePath $SteamExe -ArgumentList '-applaunch','2868840' -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $games = Game-Processes
        if ($games.Count -gt 1) { throw "Multiple game processes; stop and reconcile using $RunDir. Lock retained." }
        if ($games.Count -eq 1) {
            $session.process = Identity $games[0]
            $session.status = 'awaiting-window'
            Write-Json $session (Join-Path $RunDir 'session.json')
            if ($games[0].MainWindowHandle -ne 0) { break }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$session.process -or $games.Count -ne 1 -or $games[0].MainWindowHandle -eq 0) {
        $session.status='launch-timeout'
        Write-Json $session (Join-Path $RunDir 'session.json')
        throw "Launch timeout. Do not retry blindly; inspect $RunDir and any late game process. Lock retained."
    }
    $session.status='window-found-awaiting-visual-check'
    Write-Json $session (Join-Path $RunDir 'session.json')
    $RunDir
    exit 0
}
if (!$RunDir) { throw 'RunDir is required for Doctor and Finish.' }
$RunDir = (Resolve-Path -LiteralPath $RunDir).Path
$sessionPath = Join-Path $RunDir 'session.json'
$session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
if ($session.runDir -ne $RunDir -or $session.scope -ne 'game-shell-smoke') { throw 'Invalid run directory.' }
$process = if ($session.process) { Get-Process -Id $session.process.pid -ErrorAction SilentlyContinue } else { $null }
$same = $process -and $process.Path -eq $session.process.executable -and $process.StartTime.ToUniversalTime().Ticks -eq ([datetime]$session.process.startedUtc).ToUniversalTime().Ticks
if ($Action -eq 'Doctor') {
    $doctor = [ordered]@{ checkedUtc=[DateTime]::UtcNow.ToString('o'); sameOwnedProcess=[bool]$same; responding=$false; title=$null; windowHandle=$null }
    if ($same) { $doctor.responding=$process.Responding; $doctor.title=$process.MainWindowTitle; $doctor.windowHandle=$process.MainWindowHandle.ToInt64() }
    Write-Json $doctor (Join-Path $RunDir 'doctor.json')
    $doctor | ConvertTo-Json
    if (!$same -or !$doctor.responding -or !$doctor.windowHandle) { exit 2 }
    exit 0
}
# Finish verifies cleanup; window closure itself uses the supported Computer Use API.
if ($same) { throw 'Owned game is still running. Reobserve its window, close normally using Computer Use, then retry Finish.' }
if ($process) { throw 'PID has been reused. Leave that process untouched and reconcile manually.' }
if ((Game-Processes).Count -ne 0) { throw 'Another game instance is present. Leave it untouched; run is inconclusive.' }
$reviewPath = Join-Path $RunDir 'visual-review.json'
$review = if (Test-Path -LiteralPath $reviewPath) { Get-Content -LiteralPath $reviewPath -Raw | ConvertFrom-Json } else { $null }
$screenshotPath = Join-Path $RunDir 'main-menu.jpg'
$validEvidence = $false
if ($review -and (Test-Path -LiteralPath $screenshotPath)) {
    $hash = (Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $validEvidence = $review.screenshotSha256 -eq $hash -and $review.mainMenuVisible -eq $true -and $review.note -and $review.pid -eq $session.process.pid -and $review.windowId -eq $review.doctorWindowHandle
}
$report = [ordered]@{
    scope='game-shell-smoke'; status=$(if($validEvidence){'PASS'}else{'FAIL'})
    launchObserved=[bool]$session.process; ownedProcessExited=$true; evidenceRetained=[bool]$validEvidence
    visualCheck='Agent inspected captured window; not an automatic image classifier.'
    companionStatus='NOT_TESTED'; gameActionsStatus='NOT_TESTED'; screenshot=$screenshotPath
    finishedUtc=[DateTime]::UtcNow.ToString('o')
}
Write-Json $report (Join-Path $RunDir 'report.json')
$session.status=$report.status
Write-Json $session $sessionPath
if ((Test-Path -LiteralPath $session.lockPath) -and (Get-Content -LiteralPath $session.lockPath -Raw) -eq $RunDir) { Remove-Item -LiteralPath $session.lockPath }
$report | ConvertTo-Json
if (!$validEvidence) { exit 2 }
