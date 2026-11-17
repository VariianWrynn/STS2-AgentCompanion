# Native host card boundary regressions

Run from the repository root with the .NET SDK installed.

```powershell
dotnet run --project tests/HostNativeBoundaryRegression/HostNativeBoundaryRegression.csproj
```

For an offline run, restore against an empty local feed using cached framework assets, then pass `--no-restore` to the command above. The project has no package dependencies.

The project links every complete production source file under `src/LocalAiTeammate/HostTest/`, including `HostTestBridge.cs`, `HostCardPolicy.cs` and `HostTurnCommands.cs`. It constructs the actual private nested `NativeBoundary` through reflection with a null bridge argument and executes its `Cards`, `CanPlay` and `InOriginalHand` methods. It never calls that fixture's `Read` or `Dispatch`. A wrapper supplies stable context and counts dispatches while the actual `HostTurnCommands` revalidates submitted offers.

`NativeStubs.cs` declares explicit native and Godot substitutes. Hand membership, enemy references, combat IDs and holder state are mutable test inputs. `CardModel.CanPlayTargeting` records its target and returns a configured result. Card classes declare the expected native category. The upgrade flag represents an upgrade on the same runtime type; no native upgrade effect is executed. The Armaments stub represents an unlisted selector card solely to check exact-type denial. No selector behavior runs. Unused engine, queue and lifecycle entry points throw.

The assertions verify host admission, target identity, current hand and holder guards, the 255-choice bound and submit-time revalidation. They do not verify native cost calculation, card effects, combat hooks, draw, powers, animations, queue settlement, Godot object lifetime or gameplay. The separate build against the installed game assembly checks native compilation. Neither result is a gameplay PASS. The existing `HostBridgeRegression` and actual SDK `HostObserverRegression` suites remain separate and unchanged.
