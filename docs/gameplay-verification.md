# Verify the local AI second player

Run these commands in PowerShell 7 from the repository root. Keep logs and screenshots under `artifacts/verification/`. Do not commit evidence that contains user profile details.

## Build and install

Check the installed version before each run. The current helper accepts `v0.113.0` and `v0.113.1`. Acceptance alone does not prove compatibility.

```powershell
$gameDir = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2'
Get-Content -LiteralPath (Join-Path $gameDir 'release_info.json')
& .\scripts\build.ps1
& .\scripts\verify-local-ai.ps1 -Action Install -GameDir $gameDir -BuildDir .\artifacts\build\LocalAiTeammate
```

Install refuses an existing game process and an unowned mod directory. It copies the built mod into the development-owned installation. Override `-GameDir` and `-BuildDir` when those directories differ. Build accepts `-Sts2DataDir` for a different game assembly directory.

## Launch and confirm the test profile

Leave existing game sessions untouched. Do not replace an in-progress user run. Start Steam and sign in before launching.

```powershell
$run = & .\scripts\verify-local-ai.ps1 -Action Launch -GameDir $gameDir -SteamExe 'D:\Steam\steam.exe'
& .\scripts\verify-local-ai.ps1 -Action Doctor -RunDir $run
```

Launch refuses an existing STS2 process or verification lock. On a timeout, inspect the saved session and any late process before retrying. Do not delete the lock to retry blindly.

Doctor checks process identity, responsiveness and the window handle. It does not confirm the profile or mod behavior. Through Computer Use, inspect the owned window and select a separate empty test profile. Confirm the selected profile in the UI before creating a run. Never infer profile isolation from the launch command or Doctor output.

Create a two-player game with the local AI as the second player. Inspect both characters, AI readiness, card play, turn completion, rewards and navigation. Record the actual observed journey and failures. For screenshots, import `scripts/capture-gameplay.mjs` in the Computer Use Node session and call `saveFrame(runDir, state, outputDir, name, note)` with a fresh owned-window state. Execute the returned copy command to retain the image and its metadata in the run directory.

## Inspect the action audit

```powershell
& .\scripts\read-local-ai-audit.ps1 -RunDir $run
```

Inspect `ai-action-audit.json` alongside screenshots and game state. `OBSERVED_DISTINCT_AI_ACTOR` establishes distinct submitted actor IDs in this log. `OBSERVED_AI_DECREASE_HOST_UNCHANGED` describes matched PlayCard snapshots only. Neither result proves isolation for all actions or completion of the run. No submissions produce `NOT_OBSERVED`. Wrong actor ownership exits with code 2. A zero exit alone is not a gameplay PASS.

## Close the owned test instance

Rerun Doctor and inspect the current owned window before closing it normally through Computer Use. Leave pre-existing processes alone. Then finish the run.

```powershell
& .\scripts\verify-local-ai.ps1 -Action Finish -RunDir $run
```

Finish verifies process exit and releases this run's lock. Its cleanup record has `automaticPass: false`. Summarize gameplay separately with the observed version, journey and evidence. For main-menu checks without gameplay, use the [shell verification skill](../.agents/skills/verify-spire-shell/SKILL.md).
