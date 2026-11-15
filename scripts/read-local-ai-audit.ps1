param([Parameter(Mandatory)][string]$RunDir)
$ErrorActionPreference = 'Stop'
$RunDir = (Resolve-Path -LiteralPath $RunDir).Path
$session = Get-Content -LiteralPath (Join-Path $RunDir 'session.json') -Raw | ConvertFrom-Json
if ($session.scope -ne 'local-ai-gameplay') { throw 'Expected gameplay verification session.' }
if (!(Test-Path -LiteralPath $session.log)) { throw 'Game log has not been created.' }
$events = @(foreach ($line in Get-Content -LiteralPath $session.log) {
    $position = $line.IndexOf('[LocalAiAudit] ')
    if ($position -ge 0) {
        $json = $line.Substring($position + '[LocalAiAudit] '.Length)
        try { $json | ConvertFrom-Json } catch { throw "Invalid audit record: $json" }
    }
})
$submits = @($events | Where-Object event -eq 'submit')
$settled = @($events | Where-Object event -eq 'settled')
$incorrectOwners = @($submits | Where-Object { [string]$_.actorId -eq [string]$_.hostId -or ![string]$_.actorId -or ![string]$_.hostId })
$actors = @($submits | ForEach-Object { [string]$_.actorId } | Sort-Object -Unique)
$pending = $null
$playCardPairs = 0
$resourceObservations = @()
foreach ($event in $events) {
    if ($event.event -eq 'session') {
        $pending = $null
    } elseif ($event.event -eq 'submit') {
        $pending = $event
    } elseif ($event.event -eq 'settled') {
        $before = $pending
        $pending = $null
        if (!$before -or $before.actionKind -ne 'PlayCard' -or $event.actionKind -ne 'PlayCard') { continue }
        if ([string]$before.actorId -ne [string]$event.actorId -or
            [string]$before.hostId -ne [string]$event.hostId -or
            [string]$before.actionId -ne [string]$event.actionId -or
            $before.phase -ne $event.phase -or $before.round -ne $event.round) { continue }
        $playCardPairs++
        $resources = @($before.aiEnergy, $before.aiHand, $before.hostEnergy, $before.hostHand,
            $event.aiEnergy, $event.aiHand, $event.hostEnergy, $event.hostHand)
        if (@($resources | Where-Object { $null -eq $_ -or ($_ -isnot [int] -and $_ -isnot [long] -and $_ -isnot [double]) }).Count) { continue }
        if (![string]$event.actorId -or ![string]$event.hostId -or [string]$event.actorId -eq [string]$event.hostId) { continue }
        if ($event.aiEnergy -lt $before.aiEnergy -and $event.aiHand -lt $before.aiHand -and
            $event.hostEnergy -eq $before.hostEnergy -and $event.hostHand -eq $before.hostHand) {
            $resourceObservations += [ordered]@{
                actionId = $event.actionId
                round = $event.round
                before = [ordered]@{ aiEnergy = $before.aiEnergy; aiHand = $before.aiHand; hostEnergy = $before.hostEnergy; hostHand = $before.hostHand }
                after = [ordered]@{ aiEnergy = $event.aiEnergy; aiHand = $event.aiHand; hostEnergy = $event.hostEnergy; hostHand = $event.hostHand }
            }
        }
    } elseif ($event.event -eq 'failed') {
        $pending = $null
    }
}
$report = [ordered]@{
    scope = 'local-ai-action-audit'
    sourceLog = $session.log
    recordCount = $events.Count
    submitted = $submits.Count
    settled = $settled.Count
    aiActors = $actors
    submittedWithWrongActor = $incorrectOwners.Count
    actorOwnership = $(if (!$submits.Count) { 'NOT_OBSERVED' } elseif ($incorrectOwners.Count -or $actors.Count -ne 1) { 'FAIL' } else { 'OBSERVED_DISTINCT_AI_ACTOR' })
    observedActionIds = @($submits | ForEach-Object actionId | Sort-Object -Unique)
    pairedPlayCardActions = $playCardPairs
    aiResourceDecreaseWithHostUnchangedCount = $resourceObservations.Count
    playCardResourceObservation = $(if ($resourceObservations.Count) { 'OBSERVED_AI_DECREASE_HOST_UNCHANGED' } else { 'NOT_OBSERVED' })
    resourceEvidence = @($resourceObservations | Select-Object -First 5)
    gameplayCompletion = 'NOT_DETERMINED_BY_ACTION_LOG'
    note = 'Resource evidence describes matched PlayCard snapshots only. It does not prove isolation of all actions, choices, or full-run completion. Inspect screenshots and game state.'
}
$events | ConvertTo-Json -AsArray -Depth 12 | Set-Content -LiteralPath (Join-Path $RunDir 'ai-actions.json')
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $RunDir 'ai-action-audit.json')
$report | ConvertTo-Json -Depth 12
if ($incorrectOwners.Count -or ($submits.Count -gt 0 -and $actors.Count -ne 1)) { exit 2 }
