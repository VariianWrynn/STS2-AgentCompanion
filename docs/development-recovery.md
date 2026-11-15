# Development recovery

Keep each change small enough to review and verify independently. Use a separate branch for each pull request. This document records recovery rules for the local AI second-player project. It does not include the mod implementation or claim gameplay verification.

## Before resuming

1. Read the latest user instructions and saved development checkpoint.
2. Inspect the Git branch, uncommitted files, game process, and verification lock.
3. Check the installed game version before testing against its assemblies.
4. Confirm the selected profile is the dedicated test profile before starting gameplay.
5. Record the installed mod identity before attributing results to a new build.

## When a dependency is unavailable

| Condition | Continue with | Resume the blocked step when |
| --- | --- | --- |
| GitHub is unreachable or authentication fails | Local changes, commits, and available tests | Repository access is restored |
| Package restore fails | Cached builds where dependencies are complete | The package source is available |
| Steam needs authentication | Source review and local builds | The user completes authentication |
| The game updates | Compatibility investigation | The installed version is checked |
| An old verification lock exists | Process identity and session reconciliation | The owning run is safely finished |
| Model allowance is exhausted | Persisted progress from the last completed step | Allowance permits another run |

## Preserve evidence and progress

Keep screenshots, logs, and machine-specific session data local. Save the current commit, worktree status, verified result, remaining uncertainty, and next action at each completed step. Do not put credentials or game assemblies in a pull request.

A successful build or main-menu check does not prove multiplayer behavior. Verify independent player resources and each supported gameplay flow separately.

Scheduled local continuation depends on the computer remaining available, the app running, and sufficient account allowance. It cannot guarantee recovery from sleep, a locked desktop, shutdown, authentication loss, or exhausted weekly allowance. An unavailable optional model provider does not block work that can continue locally.
