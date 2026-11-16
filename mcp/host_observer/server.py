"""MCP adapter for the optional local host observation and bounded native command bridge."""

import argparse
from dataclasses import dataclass, field
import http.client
import json
from pathlib import Path
import re
import socket
import sys
import threading
import time
from typing import Literal

from mcp.server import MCPServer
from mcp.server.mcpserver.exceptions import ToolError
from mcp.types import CallToolResult, TextContent, ToolAnnotations
from pydantic import BaseModel, ConfigDict, Field, ValidationError, model_validator


MAX_RESPONSE_BYTES = 65536
HTTP_TIMEOUT_SECONDS = 3.0


class ObserverError(ToolError):
    """An error whose fixed message is safe to expose over MCP."""


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON property")
        result[key] = value
    return result


@dataclass(frozen=True)
class BridgeConfig:
    port: int
    token: str = field(repr=False)
    expected_assembly_sha256: str

    @classmethod
    def read(cls, path: str):
        try:
            source = Path(path)
            if not source.is_absolute() or str(source).startswith(("\\\\", "//")):
                raise ValueError()
            with source.open("rb") as stream:
                data = stream.read(4097)
            if len(data) > 4096:
                raise ValueError()
            value = json.loads(data, object_pairs_hook=unique_object)
            if not isinstance(value, dict) or set(value) != {"Port", "Token", "ExpectedAssemblySha256"}:
                raise ValueError()
            port, token, digest = value["Port"], value["Token"], value["ExpectedAssemblySha256"]
            if type(port) is not int or not 1024 <= port <= 65535:
                raise ValueError()
            if not isinstance(token, str) or re.fullmatch(r"[A-Za-z0-9]{32,128}", token) is None:
                raise ValueError()
            if not isinstance(digest, str) or re.fullmatch(r"[A-Fa-f0-9]{64}", digest) is None:
                raise ValueError()
            return cls(port, token, digest.lower())
        except (OSError, ValueError, TypeError):
            raise ObserverError("Host observer configuration is invalid or unavailable.") from None


class DetachedRecord(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False)


class HostProcess(DetachedRecord):
    Pid: int = Field(gt=0)
    StartTimeUtc: str
    Executable: str | None
    AssemblyPath: str
    AssemblySha256: str = Field(pattern=r"^[A-Fa-f0-9]{64}$")
    ReleaseVersion: Literal["v0.113.1"]


class HostPlayer(DetachedRecord):
    NetId: int = Field(ge=0, le=18446744073709551615)
    Phase: str | None
    Ready: bool | None
    Round: int | None
    Hp: float
    MaxHp: float
    Gold: float
    Energy: float | None
    HandCount: int | None


class HostChoice(DetachedRecord):
    Key: str = Field(pattern=r"^[a-f0-9]{32}$")
    Kind: Literal["EndTurn", "PlayCard"]
    Label: str = Field(min_length=1, max_length=128)


class HostChoices(DetachedRecord):
    Observation: str = Field(pattern=r"^[a-f0-9]{32}$")
    NextSequence: int = Field(gt=0, le=9223372036854775807)
    Gate: Literal["available", "unavailable", "pending", "indeterminate"]
    Choices: list[HostChoice] = Field(max_length=256)
    Diagnostic: Literal["native_pending", "host_choice_required", "completed_card_in_original_hand",
                        "awaiting_host_readiness", "native_failure", "enqueue_ambiguous"] | None = None

    @model_validator(mode="after")
    def check_gate(self):
        if (self.Gate == "available") != bool(self.Choices):
            raise ValueError("Choice availability disagrees with gate")
        return self


class HostReceipt(DetachedRecord):
    Sequence: int = Field(gt=0, lt=9223372036854775807)
    Status: Literal["submitted", "stale", "denied", "indeterminate", "expired", "in_flight",
                    "conflict", "sequence_rejected", "shutdown", "busy", "malformed"]


class HostSubmit(DetachedRecord):
    sequence: int = Field(gt=0, lt=9223372036854775807)
    observation: str = Field(pattern=r"^[a-f0-9]{32}$")
    choice: str = Field(pattern=r"^[a-f0-9]{32}$")


class Observation(DetachedRecord):
    Capability: Literal["observe_host"]
    Compatibility: Literal["runtime_unverified"]
    Process: HostProcess
    Scope: Literal["host", "no_companion_run", "no_run_state", "host_scope_denied"]
    Room: str | None
    Floor: int | None
    Host: HostPlayer | None
    Ai: HostPlayer | None
    Submission: HostChoices

    @model_validator(mode="after")
    def check_scope(self):
        if self.Scope != "host" and any(value is not None for value in (self.Room, self.Floor, self.Host, self.Ai)):
            raise ValueError("Unavailable scope contains player state")
        if self.Scope == "host" and (self.Host is None or self.Ai is None):
            raise ValueError("Host scope is missing participants")
        if self.Scope != "host" and self.Submission.Choices:
            raise ValueError("Unavailable scope contains choices")
        return self


class BridgeReply(DetachedRecord):
    Status: Literal["ok"]
    Observation: Observation


def request_bridge(config: BridgeConfig, submission: HostSubmit | None = None):
    deadline = time.monotonic() + HTTP_TIMEOUT_SECONDS
    connection = http.client.HTTPConnection("127.0.0.1", config.port, timeout=HTTP_TIMEOUT_SECONDS)
    timer = None
    try:
        connection.connect()
        transport_socket = connection.sock
        def expire():
            try:
                transport_socket.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
        timer = threading.Timer(max(0.001, deadline - time.monotonic()), expire)
        timer.daemon = True
        timer.start()
        headers = {"Authorization": "Bearer " + config.token}
        if submission is not None:
            headers.update({"X-Host-Sequence": str(submission.sequence), "X-Host-Observation": submission.observation,
                            "X-Host-Choice": submission.choice, "Content-Length": "0"})
        connection.request("GET" if submission is None else "POST",
                           "/v1/host/observation" if submission is None else "/v1/host/submit", headers=headers)
        transport_socket.settimeout(max(0.001, deadline - time.monotonic()))
        response = connection.getresponse()
        if response.status != 200:
            if response.status == 401:
                raise ObserverError("Host bridge authorization failed.")
            raise ObserverError("Host bridge returned a non-success response.")
        body = bytearray()
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError()
            transport_socket.settimeout(remaining)
            chunk = response.read1(min(8192, MAX_RESPONSE_BYTES + 1 - len(body)))
            body.extend(chunk)
            if len(body) > MAX_RESPONSE_BYTES:
                raise ObserverError("Host bridge response exceeds the size limit.")
            if not chunk or response.isclosed():
                break
        try:
            value = json.loads(body, object_pairs_hook=unique_object)
            if submission is not None:
                receipt = HostReceipt.model_validate(value)
                if receipt.Sequence != submission.sequence:
                    raise ValueError("Sequence mismatch")
                return receipt
            reply = BridgeReply.model_validate(value)
        except (ValueError, ValidationError, TypeError):
            raise ObserverError("Host bridge returned an invalid observation." if submission is None else
                                "Host bridge returned an invalid receipt. Retry only the identical submission payload.") from None
        if reply.Observation.Process.AssemblySha256.lower() != config.expected_assembly_sha256:
            raise ObserverError("Host bridge assembly identity does not match configuration.")
        return reply.Observation
    except (TimeoutError, socket.timeout):
        raise ObserverError("Host bridge observation timed out." if submission is None else
                            "Host bridge submission timed out. Retry only the identical submission payload.") from None
    except (OSError, http.client.HTTPException):
        if time.monotonic() >= deadline:
            raise ObserverError("Host bridge observation timed out." if submission is None else
                                "Host bridge submission timed out. Retry only the identical submission payload.") from None
        raise ObserverError("Host bridge is unavailable.") from None
    finally:
        if timer is not None:
            timer.cancel()
        connection.close()


def observe(config: BridgeConfig) -> Observation:
    return request_bridge(config)


def make_server(config: BridgeConfig) -> MCPServer:
    async def reject_arguments(context, call_next):
        if context.method == "tools/call" and isinstance(context.params, dict):
            name, arguments = context.params.get("name"), context.params.get("arguments")
            if name == "observe_host" and arguments not in (None, {}):
                return CallToolResult(is_error=True, content=[TextContent(type="text", text="observe_host accepts no arguments.")])
            if name == "submit_host":
                try:
                    HostSubmit.model_validate(arguments)
                except (ValueError, ValidationError, TypeError):
                    return CallToolResult(is_error=True, content=[TextContent(type="text", text="submit_host requires exactly sequence, observation, and choice.")])
        return await call_next(context)

    server = MCPServer("STS2 host observer", log_level="CRITICAL", middleware=[reject_arguments])

    @server.tool(structured_output=True, annotations=ToolAnnotations(read_only_hint=True, destructive_hint=False, open_world_hint=False))
    def observe_host() -> Observation:
        """Read detached host and AI state. Runtime compatibility remains unverified."""
        return observe(config)

    @server.tool(structured_output=True, annotations=ToolAnnotations(read_only_hint=False, destructive_hint=True,
                                                                    idempotent_hint=True, open_world_hint=False))
    def submit_host(sequence: int, observation: str, choice: str) -> HostReceipt:
        """Submit one offered native host choice. Retry only the identical payload after uncertainty. Submitted is not settled."""
        payload = HostSubmit.model_validate({"sequence": sequence, "observation": observation, "choice": choice})
        return request_bridge(config, payload)

    return server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, help="Absolute path to the local bridge configuration JSON")
    args = parser.parse_args()
    try:
        config = BridgeConfig.read(args.config)
    except ObserverError as error:
        print(str(error), file=sys.stderr)
        return 2
    make_server(config).run(transport="stdio")
    return 0


if __name__ == "__main__":
    sys.exit(main())
