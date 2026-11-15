---
name: verify-spire-shell
description: Verify the local Windows Slay the Spire 2 game shell by launching through Steam, checking the owned process/window, capturing and inspecting the main menu, and closing only that test instance. Does not verify gameplay or companion behavior.
---

# Verify the STS2 game shell

Use the installed `computer-use:computer-use` skill and its documented `@oai/sky` API for all window observation and input. Use PowerShell 7 for process checks and Steam launch. Commands involving host processes must run outside the Codex sandbox with the normal tool escalation mechanism; sandbox process queries previously returned empty results while the game was running.

This is an Agent-driven verification recipe, not a standalone unattended screenshot classifier. The Agent inspects the image. `recordVisualReview` records that observation; it does not recognize the main menu itself.

## Scope and known environment

- Project: `D:\salythespire2_agent`; the local rule AI second-player mod is in `src/LocalAiTeammate/`.
- Game: `E:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe`.
- Steam: `D:\Steam\steam.exe`, application ID `2868840`.
- Historical shell baseline: game `v0.113.0`, Steam build `26625880`, PowerShell `7.6.7`, Chinese main menu, with existing user mods enabled. Read the installed version and mod list recorded on every launch. This baseline does not establish compatibility with a later game release.
- Do not claim a clean profile or clean Mod environment. This smoke test only visits the main menu; never continue, abandon, overwrite or create a run.
- This recipe checks the shell only. Use [gameplay verification](../../../docs/gameplay-verification.md) to test the local AI companion. Shell PASS never establishes gameplay or companion correctness.

## Launch

Steam must already be running and authenticated. Never automate authentication. The script refuses an existing STS2 session and concurrent runs; do not adopt or close a user's existing game without their instruction. Only one test may own the game at a time.

In an escalated `exec_command`, run:

```powershell
& 'D:\salythespire2_agent\.agents\skills\verify-spire-shell\scripts\verify.ps1' -Action Launch
```

The command starts Steam with `-applaunch 2868840`, waits up to 45 seconds for a game window and prints a unique evidence directory. It records PID, process start time and executable path in `session.json`. Window discovery is not readiness: inspect the screenshot below before passing.

Direct executable launch previously failed with `No appID found`; use Steam. If startup times out, retain the lock and evidence, inspect any late process, and reconcile before another launch. Never blindly retry.

## Doctor

Read the current run directory from the lock and check process identity, responsiveness and window handle:

```powershell
$run = Get-Content -LiteralPath 'D:\salythespire2_agent\artifacts\verification\active.lock' -Raw
& 'D:\salythespire2_agent\.agents\skills\verify-spire-shell\scripts\verify.ps1' -Action Doctor -RunDir $run
```

Doctor writes `doctor.json`. A nonzero exit or `sameOwnedProcess: false` stops input. Doctor checks process ownership, responsiveness and the window handle. It does not establish the selected profile, a ready main menu or companion correctness. Timestamp comparison uses UTC ticks because PowerShell JSON parsing can convert timestamps into DateTime values. Never weaken process identity to PID alone.

## Drive

Initialize `sky` according to the Computer Use skill. In `node_repl`, select a window only from returned objects:

```javascript
globalThis.fs = await import('node:fs/promises');
globalThis.runDir = (await fs.readFile('D:/salythespire2_agent/artifacts/verification/active.lock','utf8')).trim();
globalThis.doctor = JSON.parse(await fs.readFile(runDir + '/doctor.json','utf8'));
globalThis.candidates = (await sky.list_windows()).filter(w =>
  /SlayTheSpire2\.exe$/i.test(w.app) && w.id === doctor.windowHandle);
if (candidates.length !== 1) throw Error('Expected exactly one owned game window');
globalThis.gameWindow = await sky.get_window({id:candidates[0].id, app:candidates[0].app});
await sky.activate_window({window:gameWindow});
globalThis.gameState = await sky.get_window_state({window:gameWindow});
```

Inspect the displayed screenshot before continuing. Main menu success means the STS2 logo, version and menu entries such as multiplayer, settings and exit are visible, without a blocking error. `Continue` instead of `Singleplayer` is valid for an existing save; do not click it. A minimized window once produced a screenshot of the foreground Codex app; activation and recapture fixed it. Such a screenshot is not game evidence and must not be approved or published.

If loading is still in progress, take another state snapshot. If an error or permission dialog blocks startup, record failure and clean up the owned instance; do not bypass the dialog or classify its existence as a pass.

## Evidence

After visually inspecting the main menu, save the actual returned JPEG and window metadata. Do not create an image from a description. Screenshot persistence uses a temporary directory because `node_repl` could not write directly to drive D in this environment.

```javascript
globalThis.evidence = await import('file:///D:/salythespire2_agent/.agents/skills/verify-spire-shell/scripts/evidence.mjs');
globalThis.captureDir = nodeRepl.tmpDir + '/spire-shell-' + runDir.split('\\').at(-1);
nodeRepl.write(JSON.stringify(await evidence.saveScreenshot(runDir,gameState,captureDir)));
```

The helper checks the captured window against Doctor and initially records `mainMenuVisible: false`. In a separate cell, call `recordVisualReview(captureDir, boolean, note)` with the result actually observed; describe visible menu labels and any error. Never set it true just because a screenshot was returned.

Then emit the exact safe PowerShell copy command for these two fixed evidence filenames:

```javascript
nodeRepl.write("Copy-Item -LiteralPath '" + captureDir.replaceAll("'","''") + "\\main-menu.jpg','" + captureDir.replaceAll("'","''") + "\\visual-review.json' -Destination '" + runDir.replaceAll("'","''") + "'");
```

Execute that returned command using escalated `exec_command`. Source and destination are locally generated paths, never game/model text. The screenshot SHA256 is stored in `visual-review.json` and checked again during Finish. Screenshot width and visual details are not a substitute for inspecting its content.

Evidence lives under `artifacts/verification/<run>/`: `session.json`, `game-version.json`, `installed-mods.json`, `doctor.json`, `main-menu.jpg`, `visual-review.json`, and `report.json`. Treat screenshots as local artifacts; do not upload user profile details without authorization.

## Cleanup

On success or failure, close only the run-owned instance. Rerun Doctor; verify the current returned window is still the owned handle. Reobserve it with `sky.get_window_state`, inspect the current state, then in a separate cell send `sky.press_key({window:gameState.window,key:'Alt_L+F4'})` using that fresh state. If user input invalidated observation, reobserve rather than retry stale input.

Do not kill by process name. Do not stop Steam or change user saves. If normal close does not work, report the owned PID and leave the run incomplete instead of force-killing unrelated processes.

After normal close, use escalated `exec_command`:

```powershell
$run = Get-Content -LiteralPath 'D:\salythespire2_agent\artifacts\verification\active.lock' -Raw
& 'D:\salythespire2_agent\.agents\skills\verify-spire-shell\scripts\verify.ps1' -Action Finish -RunDir $run
```

If the process is still exiting, check again before rerunning Finish. Finish confirms process exit, rejects PID reuse or another game instance, validates evidence, writes `report.json` and removes only this run's lock. It never deletes evidence. Missing or rejected visual evidence results in FAIL after cleanup, not PASS. A launch failure without a bound process needs explicit reconciliation; inspect the lock/session and late processes before releasing that run's lock.

## Helpers and maintenance

- `scripts/verify.ps1`: executable PowerShell entry point, actions Launch, Doctor and Finish, as shown above.
- `scripts/evidence.mjs`: import in `node_repl`; `saveScreenshot` and `recordVisualReview` are the only exports.
- [Feature map](features/README.md).
- First full live run: `artifacts/verification/20261114-202716-73e95d7a/report.json`. It passed shell smoke only, with `companionStatus` and `gameActionsStatus` both `NOT_TESTED`.

Use `maintain-verification-skill` when game, Mod, paths or companion features change. Keep the shell feature map separate from gameplay evidence. Add a verified gameplay journey only after exercising it through the real game UI.
