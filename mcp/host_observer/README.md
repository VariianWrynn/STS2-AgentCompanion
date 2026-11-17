# Observe and submit a local host choice through MCP

This stdio server exposes `observe_host()` and `submit_host(sequence, observation, choice)`. Observation returns detached host and AI state plus opaque offered native `EndTurn` and bounded `PlayCard` choices from the optional [host bridge](../../docs/host-mcp.md). Every observation retains `Compatibility: runtime_unverified`. The adapter never selects an action and never addresses the AI actor.

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

The adapter sends authenticated requests to `127.0.0.1` at the configured port. Observation uses GET `/v1/host/observation`. Submission uses a zero-body POST `/v1/host/submit` with bounded sequence, observation, and choice headers. It has no URL or actor input. It ignores environment proxies and refuses redirects. A socket deadline bounds each request to three seconds, and the response body is limited to 65536 bytes.

The observer validates the detached record schema and compares the reported assembly hash with the configured hash. Errors expose fixed messages rather than backend bodies, credentials, or configuration paths. Hash comparison alone does not establish ownership. The external verifier must attest the PID, process start time, executable, installed assembly, and empty test profile before consuming observations.

Use the observed `Submission.NextSequence`, `Submission.Observation`, and the chosen `Submission.Choices` entry's `Key` only when the gate is `available`. A fresh observation invalidates the prior offer. The bridge revalidates native host identity and readiness on the game thread before dispatch. Card choices cover exact StrikeIronclad, DefendIronclad, Bash, TwinStrike, Thunderclap, Uppercut, Inflame, ShrugItOff, and PommelStrike types, including upgrades. Enchantments, afflictions, energy X and star X costs are excluded, and each type must retain its expected targeting. AnyEnemy binds a living hittable enemy. Self and AllEnemies each use one choice with a null native target and null combat ID. AllEnemies does not offer one choice per enemy. All cards still require native playability. They use the same three-argument submission tool. There is no launch, profile, lobby, or shell tool. Offline success does not establish native lifecycle, gameplay compatibility, or companion correctness.

After a lost response, timeout, or `in_flight` receipt, retry exactly the same sequence, observation, and choice. Identical latest retries return the original terminal receipt before freshness checks. Never replace an uncertain submission with a new sequence. `submitted` means native queue submission, not settled gameplay. Poll observations to read the pending gate and nullable `Submission.Diagnostic`. Detected hand selection reports `host_choice_required` and stays blocked. Other unresolved native choices can remain generic pending. A completed card still in its original hand reports `completed_card_in_original_hand` and stays blocked. `indeterminate` blocks further host mutations and requires ending the owned test and reviewing evidence before a fresh test process. See the [full receipt and sequence contract](../../docs/host-mcp.md#submit-an-offered-host-choice).

## Run the offline regression

Run the regression with the same isolated Python environment that contains the pinned SDK.

```powershell
& artifacts/verification/host-observer-venv/Scripts/python.exe tests/HostObserverRegression/test_observer.py
```

The regression uses the real SDK `Client` over stdio and a test-owned fake loopback backend. It checks both tool schemas, structured observations and receipts, all scope values, bounded card choices, strict card kinds, pending diagnostics, strict submission arguments, actor and extra-argument rejection, zero-body POST headers, authorization, proxy bypass, redirect refusal, unavailable backend, timeout, slow headers, malformed data, size limits, identity mismatch, and sanitized failures. It does not launch the game or open a profile.
