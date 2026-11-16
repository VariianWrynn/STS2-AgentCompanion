"""Offline stdio regression. The backend is an owned fake, never the game."""

import asyncio
from copy import deepcopy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest

from mcp import Client
from mcp.client.stdio import StdioServerParameters


SERVER = Path(__file__).resolve().parents[2] / "mcp" / "host_observer" / "server.py"
spec = importlib.util.spec_from_file_location("host_observer", SERVER)
observer = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = observer
spec.loader.exec_module(observer)
TOKEN = "RegressionSecretNeverEcho" + "A" * 16
DIGEST = "a" * 64
OBSERVATION = {
    "Capability": "observe_host", "Compatibility": "runtime_unverified",
    "Process": {"Pid": 1234, "StartTimeUtc": "2026-11-16T00:00:00Z", "Executable": None,
                "AssemblyPath": "D:/fake/sts2.dll", "AssemblySha256": DIGEST, "ReleaseVersion": "v0.113.1"},
    "Scope": "no_companion_run", "Room": None, "Floor": None, "Host": None, "Ai": None,
    "Submission": {"Observation": "a" * 32, "NextSequence": 1, "Gate": "unavailable", "Choices": []},
}


class Backend(BaseHTTPRequestHandler):
    mode = "ok"
    calls = []

    def log_message(self, *args):
        pass

    def do_POST(self):
        type(self).calls.append((self.path, self.headers.get("Authorization"), self.headers.get("X-Host-Sequence"),
                                 self.headers.get("X-Host-Observation"), self.headers.get("X-Host-Choice"),
                                 self.headers.get("Content-Length")))
        sequence = int(self.headers["X-Host-Sequence"])
        status = type(self).mode
        if status == "wrong_sequence":
            sequence, status = sequence + 1, "submitted"
        body = json.dumps({"Sequence": sequence, "Status": status}).encode()
        self.send_response(200)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        type(self).calls.append((self.path, self.headers.get("Authorization")))
        mode = type(self).mode
        if mode == "timeout":
            time.sleep(4)
            return
        if mode == "slow_headers":
            try:
                self.connection.sendall(b"HTTP/1.1 200 OK\r\nX-Slow: ")
                for _ in range(50):
                    self.connection.sendall(b"a")
                    time.sleep(0.1)
            except OSError:
                pass
            return
        observation = deepcopy(OBSERVATION)
        if mode == "host":
            player = {"NetId": 1, "Phase": "Play", "Ready": False, "Round": 1,
                      "Hp": 80, "MaxHp": 80, "Gold": 99, "Energy": 3, "HandCount": 5}
            observation.update(Scope="host", Room="CombatRoom", Floor=1, Host=player, Ai=player | {"NetId": 2})
        if mode in ("no_run_state", "host_scope_denied"):
            observation["Scope"] = mode
        if mode == "hash":
            observation["Process"]["AssemblySha256"] = "b" * 64
        if mode == "scope":
            observation["Floor"] = 1
        if mode == "compatibility":
            observation["Compatibility"] = "verified"
        body = json.dumps({"Status": "ok", "Observation": observation}).encode()
        status = 200
        if mode == "malformed":
            body = ("malformed " + TOKEN).encode()
        if mode == "oversized":
            body = b"x" * 65537
        if mode == "auth":
            status, body = 401, TOKEN.encode()
        if mode == "redirect":
            status, body = 302, TOKEN.encode()
        if mode == "busy":
            status, body = 503, TOKEN.encode()
        self.send_response(status)
        self.send_header("Location", "http://192.0.2.1/" + TOKEN)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except OSError:
            pass


class Regression(unittest.TestCase):
    def test_config_boundary(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "config.json"
            valid = {"Port": 12345, "Token": TOKEN, "ExpectedAssemblySha256": DIGEST}
            path.write_text(json.dumps(valid), encoding="utf-8")
            config = observer.BridgeConfig.read(str(path))
            self.assertNotIn(TOKEN, repr(config))
            for change in ({"Port": True}, {"port": 12345}, {"Token": "invalid"}, {"ExpectedAssemblySha256": "bad"}):
                path.write_text(json.dumps(valid | change), encoding="utf-8")
                with self.assertRaisesRegex(observer.ObserverError, "configuration is invalid"):
                    observer.BridgeConfig.read(str(path))
            for data in ('{"Port":12345,"Port":12346}', "x" * 4097):
                path.write_text(data, encoding="utf-8")
                with self.assertRaises(observer.ObserverError):
                    observer.BridgeConfig.read(str(path))
            with self.assertRaises(observer.ObserverError):
                observer.BridgeConfig.read("relative.json")

    def test_actual_sdk_stdio(self):
        asyncio.run(self.exercise_stdio())

    async def exercise_stdio(self):
        backend = ThreadingHTTPServer(("127.0.0.1", 0), Backend)
        backend.daemon_threads = True
        thread = threading.Thread(target=backend.serve_forever, daemon=True)
        thread.start()
        try:
            with tempfile.TemporaryDirectory() as directory:
                config = Path(directory) / "private.json"
                config.write_text(json.dumps({"Port": backend.server_port, "Token": TOKEN,
                                             "ExpectedAssemblySha256": DIGEST}), encoding="utf-8")
                parameters = StdioServerParameters(command=sys.executable, args=[str(SERVER), "--config", str(config)],
                                                    env={"HTTP_PROXY": "http://192.0.2.1:1", "NO_PROXY": ""})
                async with Client(parameters, read_timeout_seconds=12) as client:
                    tools = (await client.list_tools()).tools
                    self.assertEqual([tool.name for tool in tools], ["observe_host", "submit_host"])
                    self.assertEqual(tools[0].input_schema.get("properties", {}), {})
                    self.assertNotIn("actor", json.dumps(tools[0].input_schema))
                    self.assertEqual(set(tools[1].input_schema["properties"]), {"sequence", "observation", "choice"})
                    self.assertFalse(tools[1].annotations.read_only_hint)
                    Backend.mode = "ok"
                    result = await client.call_tool("observe_host", {})
                    self.assertFalse(result.is_error)
                    self.assertEqual(result.structured_content, OBSERVATION)
                    self.assertEqual(Backend.calls[-1], ("/v1/host/observation", "Bearer " + TOKEN))
                    for mode in ("host", "no_run_state", "host_scope_denied"):
                        Backend.mode = mode
                        result = await client.call_tool("observe_host", {})
                        self.assertFalse(result.is_error, mode)
                        self.assertEqual(result.structured_content["Scope"], mode)
                        self.assertEqual(result.structured_content["Compatibility"], "runtime_unverified")
                    call_count = len(Backend.calls)
                    result = await client.call_tool("observe_host", {"actor": "host"})
                    self.assertTrue(result.is_error)
                    self.assertEqual(len(Backend.calls), call_count)
                    payload = {"sequence": 1, "observation": "a" * 32, "choice": "b" * 32}
                    for malformed in (payload | {"actor": 10002}, payload | {"sequence": True},
                                      payload | {"sequence": "1"}, payload | {"observation": "bad"},
                                      payload | {"choice": "b" * 33}, payload | {"sequence": 0},
                                      payload | {"sequence": 9223372036854775807}, {"sequence": 1}):
                        result = await client.call_tool("submit_host", malformed)
                        self.assertTrue(result.is_error)
                    self.assertEqual(len(Backend.calls), call_count)
                    for status in ("submitted", "stale", "denied", "indeterminate", "expired", "in_flight", "conflict",
                                   "sequence_rejected", "shutdown", "busy", "malformed"):
                        Backend.mode = status
                        result = await client.call_tool("submit_host", payload)
                        self.assertFalse(result.is_error, status)
                        self.assertEqual(result.structured_content, {"Sequence": 1, "Status": status})
                        self.assertEqual(Backend.calls[-1], ("/v1/host/submit", "Bearer " + TOKEN, "1", "a" * 32, "b" * 32, "0"))
                    for mode in ("unknown_receipt", "wrong_sequence"):
                        Backend.mode = mode
                        result = await client.call_tool("submit_host", payload)
                        self.assertTrue(result.is_error)
                        self.assertIn("invalid receipt", result.content[0].text)
                        self.assertNotIn(TOKEN, result.content[0].text)
                    for mode, expected in [("malformed", "invalid observation"), ("hash", "identity"),
                                           ("scope", "invalid observation"), ("compatibility", "invalid observation"),
                                           ("oversized", "size limit"), ("auth", "authorization failed"),
                                           ("redirect", "non-success"), ("busy", "non-success"),
                                           ("timeout", "timed out"), ("slow_headers", "timed out")]:
                        Backend.mode = mode
                        result = await client.call_tool("observe_host", {})
                        self.assertTrue(result.is_error, mode)
                        text = " ".join(getattr(item, "text", "") for item in result.content)
                        self.assertIn(expected, text, mode)
                        self.assertNotIn(TOKEN, text, mode)
                        self.assertNotIn(str(config), text, mode)
                    backend.shutdown()
                    backend.server_close()
                    result = await client.call_tool("observe_host", {})
                    self.assertTrue(result.is_error)
                    self.assertIn("unavailable", result.content[0].text)
        finally:
            backend.shutdown()
            backend.server_close()
            thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
