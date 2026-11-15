# STS2 companion project

The local rule AI second-player mod is under `src/LocalAiTeammate/`. Build it with `scripts/build.ps1`. The source is adapted from the pinned upstream described in `THIRD_PARTY_NOTICES.md`.

For launch, window, screenshot and cleanup checks, read `.agents/skills/verify-spire-shell/SKILL.md`. Use pstack for development; this file adds no separate workflow.

Do not equate shell-smoke PASS with gameplay or companion correctness. Keep generated evidence local under `artifacts/verification/`. Never close a pre-existing user session or modify saves during smoke verification.

Gameplay verification uses `scripts/verify-local-ai.ps1` and a separate empty test profile. Check the actual installed game version on each run. Never replace an in-progress user run with a test run.
