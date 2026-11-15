# Third party notices

LocalAiTeammate adapts the C# mod from [brekkylab/sts2-llm-coop-mod](https://github.com/brekkylab/sts2-llm-coop-mod), commit `01362aeba5806aed0c70286186a94b9fae0a8d71`. That project is based on [SallyHong2347/sts2-ai-teammate](https://github.com/SallyHong2347/sts2-ai-teammate). Both are provided under the MIT license. The source mod license is preserved in `src/LocalAiTeammate/LICENSE`; the coop repository license is preserved in `src/LocalAiTeammate/LICENSE.coop-mod`.

The port retains player/session control, legal action discovery, local deterministic AI evaluation, room selection flows and character setup UI. It removes the external LLM bridge, HTTP server, BaseLib settings, plan approval, test map and modified event pool, and adapts APIs for Windows STS2 v0.113.x. The retained source namespace records its upstream origin.

Game assemblies and Harmony are referenced from the user's installed game and are not redistributed. Godot.NET.Sdk and Krafs.Publicizer are build dependencies restored through NuGet.
