# Prevent phantom block after dexterity setup

The local rule AI should evaluate its second-player turn without inventing block from attacks, utility actions, or potions after gaining dexterity.

`CombatTurnLinePlanner.LineNode.Apply` previously added the line's accumulated permanent and temporary dexterity to every action. A line that gained 2 dexterity and then played two attacks therefore reported 4 block without a block effect.

The fix adds that dexterity only when the action resolves to a card with a `GainBlock` effect. It preserves zero-amount block effects. Checking `action.Block > 0` would incorrectly exclude those effects. No signatures, action types, potion scoring, or damage rules change.

## Evidence

- The installed `release_info.json` reports v0.113.1, commit `d09d48e5`.
- `CardCatalogBuilder.GetDynamicVars` reads canonical `DynamicVar.BaseValue`. `CardResolver.ResolveEffects` applies upgrade and stored overlays. This path does not include the player's existing dexterity.
- The fallback reads `DynamicVar.IntValue`. The installed game's getter returns `(int)BaseValue`, independently of `PreviewValue`. A special calculated variable can have its own semantics.
- The installed `DexterityPower.ModifyBlockAdditive` modifies a block operation only when its value properties satisfy `IsPoweredCardOrMonsterMoveBlock()`. It does not create block on unrelated actions.
- The installed `BlockPotion` uses `ValueProp.Unpowered`. Potion actions do not resolve to cards here and do not acquire the line's dexterity bonus.

The data shape stays the existing `ResolvedCardView.Effects` list. Its `GainBlock` descriptor, rather than a positive amount, identifies a modeled block action. This follows Model the Domain without introducing a second flag. Fix Root Causes places the correction at block accumulation. Prove It Works and Sequence Work into Verifiable Units require the same production method to fail before the fix and pass afterward.

## Reproduce and verify

From this worktree, run the following commands. The harness loads the built mod and resolves the installed game assemblies in place. It does not copy game dependencies or start the game.

```powershell
./scripts/build.ps1
dotnet run --project tests/PlannerDexterityRegression -- artifacts/build/LocalAiTeammate/LocalAiTeammate.dll 'E:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64'
```

The harness constructs normalized card effects and action fixtures. It calls production `ResolvedCardViewExtensions.GetEstimatedBlock` and private `LineNode.Apply` from the actual built DLL through reflection. It checks accumulated block and does not reproduce the production formula in the test.

Before the fix, 5 of the initial 9 checks failed and the process exited 1. The coordinator requested an additional 12-block potion fixture. With that fixture, the baseline DLL from `codex/local-ai-baseline` fails 6 of 10 checks, including an incorrect 14-block result for the potion. The fixed DLL passes all 10 checks and exits 0. The checks cover permanent and temporary dexterity, attacks, utility actions, potion actions with and without modeled block, repeated attacks, ordinary block, zero-amount block effects, and block without added dexterity. The project build completed with 0 warnings and 0 errors.

Local evidence is under `artifacts/verification/dexterity/`, including `before.txt`, `before-extended.txt`, `after.txt`, `build-after.txt`, and `DexterityPower.cs.txt`.

## Limits and review

This is a regression check of production block accumulation, not gameplay verification or proof of full card resolution. The fixture supplies the dexterity gains and normalized effects. It does not exercise Godot, action execution, target selection, or the complete turn planner.

The existing extractors omit native zero-valued block effects when they create no `GainBlock` descriptor. This change preserves represented zero-amount effects but does not add support for omitted native effects. The existing normalized format also lacks block value properties, so this change does not establish general handling of unpowered card block or multiple block operations. Initial player dexterity and other existing estimation gaps remain outside this patch.

The fix remains inside one production method, so no cross-boundary architecture change was needed. The `architect` design comparison was limited to an amount check versus the existing effect descriptor. No external-model design review was run. The unavailable `deslop` plugin and specialized Comment Sicko task were not claimed as passed. Manual diff, comment, dependency, and whitespace checks substitute for those unavailable gates. The coordinator will independently review before any push or PR.
