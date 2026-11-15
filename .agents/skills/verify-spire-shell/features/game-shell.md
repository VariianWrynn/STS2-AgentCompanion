# Launch, inspect and exit STS2

## Sub-features

- Launch through the installed Steam client.
- Check that the returned game window belongs to the process created by this verification run.
- Capture and visually inspect the actual main menu.
- Close the owned instance and preserve its evidence.

## How to get to it (user POV)

Have Steam running and authenticated, with no STS2 session open. Launch STS2, wait for the main menu, observe the menu and version, then exit. No run is continued or created.

## Driving it with Computer Use and PowerShell

Follow Launch, Doctor, Drive, Evidence and Cleanup in the parent SKILL.md. Pass requires a live launch, matching process/window, an inspected main-menu JPEG whose hash matches, and verified process exit. See the first live report at `D:\salythespire2_agent\artifacts\verification\20261114-202716-73e95d7a\report.json`.

## Gotchas

- Host process inspection and Steam launch require escalated execution; sandbox-only process queries gave false empty inventories.
- Existing sessions cause refusal, not adoption. No parallel game drivers.
- Current profile has a saved run and user mods. This is not a fresh-profile or Mod-isolation test.
- Activate the selected window before capture. A minimized game previously yielded another foreground app's image.
- Images returned by Computer Use were JPEG, not PNG. Preserve actual format.
- Images are staged in `nodeRepl.tmpDir` before an approved copy into the project.
- A Steam error dialog is a failure, even if the process responds.
- Shell PASS provides no evidence that the implemented local AI companion cooperates or plays cards correctly. Follow the separate gameplay guide for those checks.
