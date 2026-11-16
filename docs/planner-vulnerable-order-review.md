# Vulnerable timing in planned attack lines

The local rule AI should estimate each second-player attack using the status present when that attack deals damage. Bash previously estimated 12 damage on a fresh target instead of 8 because the planner applied its new Vulnerable before calculating its own hit.

`LineNode.Apply` now reads the incoming node's Vulnerable set for `BASH`, `BEAM_CELL`, and `BREAK` damage. The outgoing node still records their Vulnerable for later attacks. The existing incoming and outgoing sets represent this timing without a new field or function boundary. Unknown card IDs retain their previous behavior.

## Native evidence

The installed game reports v0.113.1, commit `d09d48e5`. Its native `Bash.OnPlay`, `BeamCell.OnPlay`, and `Break.OnPlay` await damage before applying Vulnerable. Their base damage and Vulnerable amounts are 8 and 2, 3 and 1, and 20 and 5. Upgrade methods change these amounts without changing command order. Native model IDs are also read from the installed assembly by the regression program.

The normalized card effect list groups effects by category. It does not describe native execution order, so this change does not infer timing for every mixed-effect card. Targeted native inspection and the detailed explanation remain local under `artifacts/verification/vulnerable-order-review-20261116/`.

## Verification

Run these commands from this worktree.

```powershell
./scripts/build.ps1 -NoRestore
dotnet run --project tests/PlannerPotionVulnerableRegression -- artifacts/build/LocalAiTeammate/LocalAiTeammate.dll 'E:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64'
dotnet run --project tests/PlannerDexterityRegression -- artifacts/build/LocalAiTeammate/LocalAiTeammate.dll 'E:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64'
```

The potion regression program executes production `BuildPlannableAction` and `LineNode.Apply`. Only the unrelated scorer is replaced with a neutral score to avoid Godot logging. These checks do not verify scoring or live combat.

Before this timing fix, the program reported 27 passes and 8 failures. Afterward, all 35 checks passed. Bash alone estimates 8 damage, Bash then Bash estimates 20, strike then Bash estimates 14, and Bash then strike estimates 17. Prior Vulnerable still amplifies Bash, each newly covered card still amplifies the following strike, and the unknown-ID control retains its previous result. The existing 10 dexterity checks passed. The final build completed with 0 warnings and 0 errors.

The first fresh restore encountered NU1900 because the vulnerability service was unreachable. A forced restore through the existing local proxy succeeded without warnings. No audit setting or global proxy configuration changed. Raw before, after, restore, and build outputs remain local.

## Limits

This patch does not model all-enemy Vulnerable, Artifact, reactive effects, live pre-existing powers, or hit-by-hit rounding. Thunderclap has the same native timing, but its targetless all-enemy action still bypasses the planner's single-target damage branch. Uppercut's generic Power variable is a separate normalization gap. Original v0.113.0 compatibility and a complete run remain unverified.

The independent timing investigation completed, but its coordinator receipt missed the lease deadline. The ledger records that timeout rather than a timely success. The coordinator inspected the resulting evidence separately before implementation.

The specialized Comment Sicko agent and deslop plugin are unavailable in this environment. An ordinary independent native agent reviews the final diff, and the coordinator checks comments, prose, whitespace, and staged files. Those substitutes are not a claim that the unavailable gates ran.
