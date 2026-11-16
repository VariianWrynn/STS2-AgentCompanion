# Host observation protocol

The optional host bridge reports detached host and AI state for a local companion run. It exposes observation only. It does not provide MCP tools, control the host, choose actions, or establish gameplay compatibility.

## Activation

The bridge starts only when the game command line includes one `--local-ai-host-bridge-config=<absolute-path>` argument. The JSON file has these case-sensitive fields.

| Field | Accepted value |
| --- | --- |
| `Port` | Integer from 1024 through 65535. |
| `Token` | 32 through 128 ASCII letters or digits. Use a fresh random token for each test process. |
| `ExpectedAssemblySha256` | 64 hexadecimal digits for the installed `sts2.dll`. |

The file is limited to 4096 bytes. Relative paths, UNC paths, unknown fields, and duplicate activation arguments are rejected. Failed activation logs the exception type without the token, path, or exception message. Ordinary launches do not create a listener or frame callback.

Activation compares the SHA256 of the loaded game assembly's file with the configured hash. It also requires `v0.113.1` in the adjacent game's `release_info.json`. These checks identify the installed files. They do not prove runtime compatibility. The report always says `runtime_unverified` in this slice.

The external test launcher owns process and profile attestation. Before using the bridge, that launcher must match the returned PID, process start time, executable path, and assembly hash to its owned test process. The bridge does not prove profile emptiness or ownership. It does not start runs, select profiles, attach to an existing process, or write saves.

## HTTP contract

The listener binds only `http://127.0.0.1:<Port>/`. The sole route is `GET /v1/host/observation`. Every request requires `Authorization: Bearer <Token>`. The route rejects bodies, query parameters, other methods, and browser `Origin` headers. There is no actor parameter. Authorization failures return HTTP 401. Non-loopback peers return HTTP 403. Unsupported requests return HTTP 400.

JSON property names are case-sensitive PascalCase. Successful responses have this shape.

```json
{
	"Status": "ok",
	"Observation": {
		"Capability": "observe_host",
		"Compatibility": "runtime_unverified",
		"Process": {
			"Pid": 1234,
			"StartTimeUtc": "2026-11-16T00:00:00Z",
			"Executable": "<absolute executable path>",
			"AssemblyPath": "<absolute sts2.dll path>",
			"AssemblySha256": "<64 hexadecimal digits>",
			"ReleaseVersion": "v0.113.1"
		},
		"Scope": "host",
		"Room": "CombatRoom",
		"Floor": 1,
		"Host": {
			"NetId": 1,
			"Phase": "Play",
			"Ready": false,
			"Round": 1,
			"Hp": 80,
			"MaxHp": 80,
			"Gold": 99,
			"Energy": 3,
			"HandCount": 5
		},
		"Ai": "<same player object shape as Host>"
	}
}
```

The numbers above illustrate the schema. They are not captured game evidence. `Executable` can be null if the runtime cannot report the module path. `Room`, `Floor`, `Host`, and `Ai` are null when the scope is unavailable. Combat fields are nullable outside combat. `Round` is a combat round, not a native player turn identifier. It must not be used to construct future end-turn actions.

`Scope` is `host`, `no_companion_run`, `no_run_state`, or `host_scope_denied`. A scope denial returns HTTP 200 with no player state. Native checks require a live connected local loopback host, matching registry/local/resolved/sender host identity, matching run participants, and exactly one AI participant and controller. The bridge never repairs or impersonates the sender.

| `Status` | HTTP code | Meaning |
| --- | --- | --- |
| `ok` | 200 | A main-thread read returned an observation, including scope denials. |
| `busy` | 503 | The mailbox is full. |
| `expired` | 504 | The queued read expired without execution. |
| `timeout` | 504 | A claimed read exceeded the caller wait. |
| `capture_failed` | 503 | Native capture threw. Exception details stay private. |
| `shutdown` | 503 | The bridge stopped. |

Non-success JSON replies contain `Status` and null `Observation`. Transport saturation closes the connection. The bridge admits at most eight active HTTP handlers. Each observation waits at most two seconds for the mailbox, apart from task scheduling and response transmission. Clients need their own HTTP timeout.

The adapter uses `TcpListener` and accepts one HTTP/1.1 request per connection. Every response closes the connection. Headers are limited to 8192 ASCII bytes. Each connection has a five-second deadline, including header reads and response transmission. Malformed headers have a separate one-second limit for the error response. Oversized headers or stalled connections can close without a response. Duplicate headers, transfer encoding, absolute URL targets, and nonzero content length are rejected. It does not implement a general HTTP server.

## Scheduling and lifetime

The mailbox stores at most 64 queued observations and drains at most ten entries per frame. The request state moves atomically from queued to claimed to done, or from queued to expired. Deadlines use a monotonic clock. Expired queued requests never execute on a later frame. Expired entries retain their bounded queue slots until a frame drains them or shutdown clears them.

All native checks and reads run synchronously on the initialization thread through `SceneTree.ProcessFrame`. Worker threads receive detached record values. The bridge reads existing room and player fields only. It does not click a chest, open merchant inventory, create peer state, or invoke the AI controller.

A claimed read cannot be interrupted. Its caller can time out while the capture completes. Tree exit closes the listener, removes the callback, and completes queued and claimed waiters. Listener failure stops admission and completes mailbox waiters. Shutdown cannot make a blocked native getter return.

## Offline regression

Run `dotnet run --project tests/HostBridgeRegression/HostBridgeRegression.csproj -c Release` from the repository root. The program compiles the production mailbox, identity policy, detached records, and transport. It opens a test-owned loopback listener with a fake observation provider. It does not start the game or open a profile. Coverage includes frame budgets, overflow, expiry, unavailable frames, capture exceptions, claimed timeout, shutdown, identity denial, disabled activation, authorization, invalid request forms, fragmentation, header limits, slow headers, exact JSON output, and HTTP 504 without a late read.

Run `./scripts/build.ps1` to compile the real mod against the installed assembly. Offline regressions and a successful build do not verify Godot callback lifetime, gameplay, or companion correctness. Those checks require a separately owned empty-profile game session through the project's gameplay verifier.
