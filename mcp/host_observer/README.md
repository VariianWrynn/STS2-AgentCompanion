# Observe the local companion host through MCP

This stdio server exposes one tool, `observe_host`, with no arguments. The tool returns detached host and AI state from the optional [host observation bridge](../../docs/host-mcp.md). Every result retains `Compatibility: runtime_unverified`.

## Start the observer

Use Python 3.11 or later. Install the pinned SDK into an isolated environment.

```powershell
python -m venv artifacts/verification/host-observer-venv
& artifacts/verification/host-observer-venv/Scripts/python.exe -m pip install -r mcp/host_observer/requirements.txt
& artifacts/verification/host-observer-venv/Scripts/python.exe mcp/host_observer/server.py --config D:/absolute/private-bridge-config.json
```

Pass the same private configuration file used to activate the bridge. The file has case-sensitive `Port`, `Token`, and `ExpectedAssemblySha256` fields. Its path must be absolute and local. The observer rejects unknown fields, duplicate JSON properties, invalid values, and files larger than 4096 bytes. Keep the token and configuration outside version control.

For an MCP client, use the environment's absolute Python executable as the stdio command. Pass the absolute `server.py` path, `--config`, and the absolute private configuration path as separate arguments. This package does not register itself in Codex or start a game.

## Observation limits

The observer sends an authenticated GET to `127.0.0.1` at the configured port and the fixed `/v1/host/observation` route. It has no URL or actor input. It ignores environment proxies and refuses redirects. A socket deadline bounds each request to three seconds, and the response body is limited to 65536 bytes.

The observer validates the detached record schema and compares the reported assembly hash with the configured hash. Errors expose fixed messages rather than backend bodies, credentials, or configuration paths. Hash comparison alone does not establish ownership. The external verifier must attest the PID, process start time, executable, installed assembly, and empty test profile before consuming observations.

This adapter observes only. It has no game control, launch, profile, lobby, shell, or action tools. Offline success does not establish native host control, Godot callback behavior, gameplay compatibility, or companion correctness.

## Run the offline regression

Run the regression with the same isolated Python environment that contains the pinned SDK.

```powershell
& artifacts/verification/host-observer-venv/Scripts/python.exe tests/HostObserverRegression/test_observer.py
```

The regression uses the real SDK `Client` over stdio and a test-owned fake loopback backend. It checks the sole no-argument tool, structured observations, all scope values, argument rejection, authorization headers, proxy bypass, redirect refusal, unavailable backend, timeout, slow headers, malformed data, size limits, identity mismatch, and sanitized failures. It does not launch the game or open a profile.
