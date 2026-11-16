# Issued player turn regression

Run from PowerShell:

```powershell
& ./tests/IssuedPlayerTurnRegression/run.ps1
```

The package-free executable targets net9.0. It returns 1 for failed behavior assertions and 0 when all pass. A restore or compilation error is not regression evidence. Generated source, build output, logs and SHA-256 source provenance stay in `artifacts/verification/issued-turn-red/`.

The generator extracts the complete production combat EndTurn producer, controller suppression/reset/settlement methods, pending settlement model and selected fields without semantic edits. It compiles the actual common action models. `Stubs.cs` substitutes external engine/native boundaries and unrelated controller dependencies. The native constructor stub records its player and turn argument. The known installed native contract requires the per-player turn number, which can differ from the global round after an extra turn. No decompiled vendor code is included.

Tests check normal and skewed constructor payloads, same-identity duplicate suppression, a new player turn in the same round, completion observed after a turn transition, and a fresh combat with matching counters. State transitions are injected deterministically. Native scheduling, readiness, effect reachability and gameplay are outside this check.

Source structure changes may require updating extraction selectors and fixture dependencies. Keep the expected behavior assertions when adapting extraction. Extraction fails explicitly when a selected field or method is absent.
