# Stop assigning vulnerable to unrelated potions

The local rule AI second player should not expect fire, poison, or weak potions to amplify a subsequent attack through vulnerable. `BuildPlannableAction` previously assigned 1 vulnerable to every action classified as an offensive potion.

The production change stays in that method. An exact ID switch maps `VULNERABLE_POTION` to 3 and `POTION_OF_BINDING` to 1. Other potion IDs receive no invented vulnerable. Existing card effects, including BASH, retain their resolved vulnerable amounts. Potion scoring, damage estimation, and `LineNode.Apply` do not change.

## Native evidence

The installed `release_info.json` reports v0.113.1, commit `d09d48e5`. The test reads IDs through the installed DLL's `ModelDb.GetId(Type)` and reads canonical power amounts and target types from the native potion classes.

- `VulnerablePotion`, ID `VULNERABLE_POTION`, targets `AnyEnemy` and applies 3 vulnerable.
- `PotionOfBinding`, ID `POTION_OF_BINDING`, targets `AllEnemies` and applies 1 weak and 1 vulnerable. It is not a no-vulnerable counterexample.
- Native `FirePotion`, `PoisonPotion`, `WeakPotion`, and `BlockPotion` do not apply vulnerable. Their decompiled implementations are retained locally.
- Native BASH has 8 damage and 2 vulnerable. Its normalized card fixture retains the vulnerable effect and the following attack's bonus.

## Verification

From this worktree, run the following commands. The test resolves game assemblies and Harmony from the installed directory without copying them into its output.

```powershell
./scripts/build.ps1
dotnet run --project tests/PlannerPotionVulnerableRegression -- artifacts/build/LocalAiTeammate/LocalAiTeammate.dll 'E:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64'
dotnet run --project tests/PlannerDexterityRegression -- artifacts/build/LocalAiTeammate/LocalAiTeammate.dll 'E:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64'
```

The potion harness calls the built production `BuildPlannableAction` and `LineNode.Apply`. It checks both vulnerable metadata and the observed damage increment after a subsequent attack. Neither production method is patched or copied. Only the unrelated `CombatActionScorer.Score` is replaced in the test process with a neutral score through the installed Harmony assembly. This avoids its Godot-dependent logger initialization and means these checks do not verify scoring.

Before the fix, the potion checks reported 15 passed and 10 failed, with exit 1. Fire, poison, and weak potions each produced an incorrect 9-damage follow-up instead of 6. The vulnerable potion also had 1 vulnerable instead of 3. After the fix, all 25 potion checks passed with exit 0. The existing 10 dexterity checks also passed. `scripts/build.ps1 -NoRestore` completed with 0 warnings and 0 errors.

Local evidence, including `before.txt`, `after.txt`, `build-after.txt`, `dexterity-after.txt`, the native type decompilations, and release information, is under `artifacts/verification/potion-vulnerable/`.

## Scope and limits

Binding's vulnerable metadata is correct, but its normal action has no single `TargetId`. The unchanged planner does not simulate vulnerable on every enemy. The test confirms that this action does not invent a selected target or an arbitrary single-target damage bonus. It does not claim that Binding's real benefit is fully modeled.

The BASH fixture checks resolved vulnerable and subsequent attack damage. It does not certify the existing timing of BASH's own damage. The native card damages first and applies vulnerable afterward, while the current planner records vulnerable before calculating that card's damage. That separate issue remains unchanged.

These are deterministic planner regression checks, not gameplay verification. Native potion effects are inspected, not executed. Direct potion damage, Slippery, potion scoring, runtime potion modifiers, and broader status simulation remain outside this patch.

Fix Root Causes places the correction where the wrong status is created. Model the Domain keeps the existing vulnerable amount and effect flag instead of adding another abstraction. Prove It Works and Sequence Work into Verifiable Units require an executed production-method failure before the fix and the same checks afterward. The change stays in one method, so no new cross-function architecture was introduced. The unavailable `deslop` plugin and specialized Comment Sicko task were not run or claimed as passed. Manual diff, comment, dependency-output, and whitespace review substitute for those unavailable gates. The coordinator will independently review before push or PR creation.
