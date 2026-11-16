# Host observation and submission protocol

The optional host bridge reports detached host and AI state for a local companion run and offers one native host action, `EndTurn`. The [MCP adapter](../mcp/host_observer/README.md) exposes `observe_host()` and `submit_host(sequence, observation, choice)`. It never chooses an action. The second player's existing local rule controller runs independently. Runtime and full-game compatibility remain unverified.

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

The listener binds only `http://127.0.0.1:<Port>/`. Routes are `GET /v1/host/observation` and `POST /v1/host/submit`. Every request requires `Authorization: Bearer <Token>`. Both routes reject bodies, query parameters, and browser `Origin` headers. There is no actor parameter. Authorization failures return HTTP 401. Non-loopback peers return HTTP 403. Unsupported requests return HTTP 400.

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
		"Ai": "<same player object shape as Host>",
		"Submission": {
			"Observation": "0123456789abcdef0123456789abcdef",
			"NextSequence": 1,
			"Gate": "available",
			"Choices": [{"Key": "abcdef0123456789abcdef0123456789", "Kind": "EndTurn", "Label": "End turn"}]
		}
	}
}
```

The values above illustrate the schema. They are not captured game evidence. `Executable` can be null if the runtime cannot report the module path. `Room`, `Floor`, `Host`, and `Ai` are null when the scope is unavailable. `Submission` remains available for diagnosis and reconnect. Combat fields are nullable outside combat. `Round` is a combat round. Native submission uses `host.PlayerCombatState.TurnNumber` instead.

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

The read mailbox stores at most 64 queued observations and drains at most ten entries per frame. The command mailbox holds one latest request and receipt. Each frame claims at most one host submission before draining reads. Work moves atomically from queued to claimed to done, or from queued to expired. Deadlines use a monotonic clock. Expired queued requests never execute on a later frame. Expired read entries retain their bounded queue slots until a frame drains them or shutdown clears them.

All native checks and reads run synchronously on the initialization thread through `SceneTree.ProcessFrame`. Worker threads receive detached record values. The bridge reads existing room and player fields only. It does not click a chest, open merchant inventory, create peer state, or invoke the AI controller.

A claimed read cannot be interrupted. Its caller can time out while the capture completes. Tree exit closes the listener, removes the callback, and completes queued and claimed waiters. Listener failure stops admission and completes mailbox waiters. Shutdown cannot make a blocked native getter return.

## Submit an offered host choice

Read an observation and attest its process identity before submitting a choice. Pass its `NextSequence`, `Observation`, and choice `Key` to `submit_host`. The MCP tool accepts exactly these three arguments. It rejects actor selectors and extra arguments before any HTTP request.

The HTTP equivalent is a zero-body POST with these headers.

| Header | Accepted value |
| --- | --- |
| `X-Host-Sequence` | Canonical decimal integer from 1 through 9223372036854775806. |
| `X-Host-Observation` | The offered opaque 32-character lowercase hexadecimal observation. |
| `X-Host-Choice` | The offered opaque 32-character lowercase hexadecimal choice key. |
| `Content-Length` | Absent or `0`. |

Each fresh observation invalidates the previous offer. An offer expires after two monotonic seconds. Dispatch rechecks exact session, run, host, authoritative native combat state, native hand, combat scene, native player turn, and shared combat state. Combat and queue change events invalidate the shared-state revision, including AI progress. No native references cross the HTTP thread boundary.

EndTurn requires an alive host in local `Play` phase on the correct side, no submitted readiness, enabled actions, no executing player action, an active native combat room, and the room as the current screen. The native hand must be in `Play` mode with no card play or card selection. Extra turns must include the host. `ActiveScreenContext.IsCurrent(room)` checks the native modal, overlay, and screen priority system. The bridge enqueues `new EndPlayerTurnAction(host, host.PlayerCombatState.TurnNumber)` through the native `ActionQueueSynchronizer`. It never assigns readiness, phase, or local sender.

Submission replies use HTTP 200 with `{"Sequence": 1, "Status": "submitted"}`. A receipt acknowledges bridge submission, not whole-turn settlement, victory, or full-game correctness.

| `Status` | Meaning |
| --- | --- |
| `submitted` | The native action was handed to the native synchronizer. |
| `stale` | The choice, context, or offer deadline no longer matches. No dispatch occurred. |
| `denied` | A prior native action remains pending. No dispatch occurred. |
| `expired` | Queued work expired before claim. It cannot execute later. |
| `in_flight` | Work was claimed and the caller wait ended. Retry the identical payload only. |
| `indeterminate` | A native effect may have occurred, or the native action failed or was canceled. Further host mutations stay blocked. |
| `conflict` | The latest sequence was reused with a different payload. |
| `sequence_rejected` | The sequence is older than the latest sequence or skips the next sequence. |
| `busy` | Prior work is queued or claimed. This request did not consume a sequence. |
| `malformed` | Invalid domain arguments. No sequence was consumed. HTTP header validation normally rejects these with HTTP 400. |
| `shutdown` | Admission stopped, or queued work was canceled by shutdown. |

One driver owns a process-scoped monotonic sequence. The bridge retains only the latest normalized payload and immutable terminal receipt. An identical latest retry returns that receipt before any freshness check, even after a newer observation. Conflicting reuse, older sequences, and gaps never dispatch. All admitted terminal outcomes consume a sequence. Malformed, Busy, and rejected admission do not. Session replacement invalidates choices while retaining replay protection. `NextSequence` reports the next sequence for a new request; an in-flight retry must retain the prior payload and sequence.

After a timeout, lost response, or `in_flight`, retry only the identical request. A claimed request is never executed twice. The caller waits at most two seconds. A queued timeout becomes `expired`, while a claimed timeout stays in flight until its original receipt is available. An uncertain native effect never clears its gate on a timer.

`Submission.Gate` is `available`, `unavailable`, `pending`, or `indeterminate`. Only `available` has a choice. Within the same combat, a pending action requires successful native completion plus observed host readiness, a phase transition, or a new native player turn before the gate clears. Successful completion also clears the old pending action when a valid observation establishes a different native combat, run, or session. A new combat can start at the same player turn number and reuse UI objects. Hand or scene replacement alone cannot settle the action. Unknown context, incomplete actions, timeouts, native failure, cancellation, and indeterminate effects never clear the gate. Context replacement preserves the process sequence and latest immutable receipt. Completion alone does not imply whole-combat settlement. Observations remain available. This slice has no reconciliation mutation API; stop the owned test and use a fresh test process after reviewing an indeterminate result.

Queue listeners attach when native run setup creates the queue and rebind when it changes. Tree exit removes combat and queue listeners. No card, potion, shop, lobby, menu, profile, or save action exists in this slice.

## Offline regression

Run `dotnet run --project tests/HostBridgeRegression/HostBridgeRegression.csproj -c Release` from the repository root. The program compiles the production mailboxes, command domain, identity policy, detached records, and transport. It opens a test-owned loopback listener with fake native boundaries. It does not start the game or open a profile. Coverage includes read frame budgets, overflow, expiry, unavailable frames, exceptions, claimed timeout, shutdown, identity denial, disabled activation, authorization, invalid forms, fragmentation, header limits, slow headers, exact JSON output, command replay, sequence conflicts, context invalidation, pending and uncertain gates, and no late dispatch. A source assertion checks the native player-turn binding; it does not execute native gameplay.

Run `./scripts/build.ps1` to compile the real mod against the installed assembly. Offline regressions and a successful build do not verify Godot callback lifetime, gameplay, or companion correctness. Those checks require a separately owned empty-profile game session through the project's gameplay verifier.
