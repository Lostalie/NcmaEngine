"""Local stdio MCP animation server, protocol 2025-11-25.

No network listener, shell execution, file-edit tool, provider SDK, or Python gameplay.
One isolated native animation session per subprocess. Stdout contains JSON-RPC only.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any

from .animation import AnimationSession

PROTOCOL_VERSION = "2025-11-25"
MAX_MESSAGE_BYTES = 1_048_576
PREFIX = "ncma.animation."
OUTPUT_SCHEMA = {
    "type": "object",
    "properties": {
        "ok": {"type": "boolean"},
        "snapshot": {
            "type": "object",
            "properties": {
                "schema_version": {"const": 1}, "revision": {"type": "integer"},
                "state": {"type": "string"}, "time": {"type": "number"},
                "bones": {"type": "array", "items": {"type": "object"}},
                "events": {"type": "array", "items": {"type": "object"}},
            },
            "required": ["schema_version", "revision", "state", "time", "bones", "events"],
        },
        "error": {"type": "object", "properties": {"code": {"type": "string"}, "message": {"type": "string"}},
                  "required": ["code", "message"], "additionalProperties": False},
    },
    "required": ["ok"], "additionalProperties": False,
    "oneOf": [{"properties": {"ok": {"const": True}}, "required": ["snapshot"]},
              {"properties": {"ok": {"const": False}}, "required": ["error"]}],
}

# operation, description, command ID, required property schemas
SPECS = (
    ("inspect", "Inspect the isolated native animation preview: poses, clips, root motion, events and history.", 0, {}),
    ("set_speed", "Set locomotion speed (0..1); transition between Idle and Run. Undoable preview change.", 1,
     {"speed": {"type": "number", "minimum": 0, "maximum": 1}}),
    ("trigger_action", "Trigger Attack or Dodge; Attack chains only in the combo window. Undoable.", 2,
     {"action": {"type": "string", "enum": ["Attack", "Dodge"]}}),
    ("set_paused", "Set the preview pause flag. MCP time advances only through explicit step calls.", 3,
     {"paused": {"type": "boolean"}}),
    ("step", "Advance 0..1 seconds deterministically, even while paused; return notifies/root motion. Undoable.", 4,
     {"seconds": {"type": "number", "minimum": 0, "maximum": 1}}),
    ("reset", "Reset this preview session to Idle; no files/scenes are changed. Undoable.", 5, {}),
    ("undo", "Undo the last preview command, including time, events and root displacement.", 6, {}),
    ("redo", "Redo the last undone preview command.", 7, {}),
)


def tools_manifest() -> list[dict[str, Any]]:
    tools = []
    for name, description, command, required in SPECS:
        properties = dict(required)
        if command:
            properties["expected_revision"] = {"type": "integer", "minimum": 0,
                                               "description": "Optional optimistic concurrency guard from inspect."}
        tools.append({
            "name": PREFIX + name, "description": description,
            "inputSchema": {"type": "object", "properties": properties,
                            "required": list(required), "additionalProperties": False},
            "outputSchema": OUTPUT_SCHEMA,
            "annotations": {"readOnlyHint": not bool(command), "destructiveHint": False,
                            "idempotentHint": not bool(command), "openWorldHint": False},
            "_meta": {"ncma/mutationRisk": "reversible" if command else "read_only",
                      "ncma/scope": "isolated_animation_preview", "ncma/commandAbi": 1},
        })
    return tools


class RpcError(Exception):
    def __init__(self, code: int, message: str):
        super().__init__(message)
        self.code = code


def _tool_result(payload: dict[str, Any]) -> dict[str, Any]:
    return {"content": [{"type": "text", "text": json.dumps(payload, ensure_ascii=False, allow_nan=False)}],
            "structuredContent": payload, "isError": not payload["ok"]}


class AnimationMcpServer:
    def __init__(self, session: AnimationSession, allow_mutations: bool = False):
        self.session = session
        self.allow_mutations = allow_mutations
        self.initialized = False
        self.ready = False

    def _call(self, params: dict[str, Any]) -> dict[str, Any]:
        name = params.get("name")
        spec = next((entry for entry in SPECS if PREFIX + entry[0] == name), None)
        if spec is None:
            raise RpcError(-32602, "Unknown animation tool")
        _, _, command, required = spec
        arguments = params.get("arguments", {})
        if not isinstance(arguments, dict):
            raise RpcError(-32602, "arguments must be an object")
        allowed = set(required) | ({"expected_revision"} if command else set())
        if set(arguments) - allowed or set(required) - set(arguments):
            raise RpcError(-32602, "Missing or unknown tool arguments")
        for key, schema in required.items():
            value = arguments[key]
            valid = True
            if schema["type"] == "number":
                valid = type(value) in (int, float) and 0 <= value <= 1 and math.isfinite(value)
            elif schema["type"] == "boolean":
                valid = type(value) is bool
            else:
                valid = value in schema["enum"]
            if not valid:
                raise RpcError(-32602, f"Invalid {key}")
        revision = arguments.get("expected_revision")
        if "expected_revision" in arguments and (type(revision) is not int or revision < 0):
            raise RpcError(-32602, "expected_revision must be a non-negative integer")
        try:
            if command and not self.allow_mutations:
                return _tool_result({"ok": False, "error": {"code": "permission_denied",
                    "message": "Preview mutations disabled; launch with --allow-mutations to opt in."}})
            if revision is not None and revision != self.session.inspect()["revision"]:
                return _tool_result({"ok": False, "error": {"code": "revision_conflict",
                    "message": "Preview changed; inspect and retry with the current revision."}})
            if command:
                snapshot = self.session.command(command,
                    float(arguments.get("speed", arguments.get("seconds", arguments.get("paused", 0)))),
                    arguments.get("action", ""))
            else:
                snapshot = self.session.inspect()
            return _tool_result({"ok": True, "snapshot": snapshot})
        except (RuntimeError, ValueError) as error:
            return _tool_result({"ok": False, "error": {"code": "animation_command_failed", "message": str(error)}})

    def handle(self, message: Any) -> dict[str, Any] | None:
        request_id = message.get("id") if isinstance(message, dict) else None
        try:
            if not isinstance(message, dict) or message.get("jsonrpc") != "2.0" or not isinstance(message.get("method"), str):
                raise RpcError(-32600, "Invalid JSON-RPC request")
            if "id" in message and (type(request_id) not in (str, int)):
                raise RpcError(-32600, "Request id must be a string or integer")
            params = message.get("params", {})
            if not isinstance(params, dict):
                raise RpcError(-32602, "params must be an object")
            method = message["method"]
            if "id" not in message:
                if method == "notifications/initialized" and self.initialized:
                    self.ready = True
                return None
            if method == "initialize":
                if self.initialized:
                    raise RpcError(-32600, "Session is already initialized")
                if not isinstance(params.get("protocolVersion"), str) or not isinstance(params.get("capabilities"), dict) or not isinstance(params.get("clientInfo"), dict):
                    raise RpcError(-32602, "initialize requires protocolVersion, capabilities and clientInfo")
                client = params["clientInfo"]
                if not isinstance(client.get("name"), str) or not isinstance(client.get("version"), str):
                    raise RpcError(-32602, "clientInfo requires name and version strings")
                self.initialized = True
                result = {"protocolVersion": PROTOCOL_VERSION, "capabilities": {"tools": {"listChanged": False}},
                          "serverInfo": {"name": "ncma-animation", "version": "0.3.0"},
                          "instructions": "Controls an isolated C++ action-animation preview, NOT the live editor or project files. Inspect first; changes are undoable and require --allow-mutations."}
            elif method == "ping":
                result = {}
            elif not self.ready:
                raise RpcError(-32002, "Initialize and send notifications/initialized first")
            elif method == "tools/list":
                if params.get("cursor"):
                    raise RpcError(-32602, "Unknown pagination cursor")
                result = {"tools": tools_manifest()}
            elif method == "tools/call":
                result = self._call(params)
            else:
                raise RpcError(-32601, "Method not found")
            return {"jsonrpc": "2.0", "id": request_id, "result": result}
        except RpcError as error:
            if isinstance(message, dict) and "id" not in message and message.get("jsonrpc") == "2.0" and isinstance(message.get("method"), str):
                return None
            if type(request_id) not in (str, int):
                request_id = None
            return {"jsonrpc": "2.0", "id": request_id, "error": {"code": error.code, "message": str(error)}}


def serve(root: Path, allow_mutations: bool = False) -> int:
    try:
        with AnimationSession(root) as session:
            server = AnimationMcpServer(session, allow_mutations)
            while line := sys.stdin.buffer.readline(MAX_MESSAGE_BYTES + 1):
                if len(line) > MAX_MESSAGE_BYTES:
                    print("MCP message exceeds 1 MiB; closing session", file=sys.stderr)
                    return 2
                try:
                    def reject_constant(value: str) -> None:
                        raise ValueError(f"Non-JSON number: {value}")
                    message = json.loads(line.decode("utf-8"), parse_constant=reject_constant)
                    response = server.handle(message)
                except (UnicodeError, ValueError, RecursionError):
                    response = {"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "Parse error"}}
                if response is not None:
                    sys.stdout.buffer.write((json.dumps(response, ensure_ascii=False, allow_nan=False, separators=(",", ":")) + "\n").encode("utf-8"))
                    sys.stdout.buffer.flush()
        return 0
    except (OSError, RuntimeError, ValueError) as error:
        print(f"Ncma MCP: {error}", file=sys.stderr)
        return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--allow-mutations", action="store_true", help="Allow undoable edits to this isolated preview only")
    args = parser.parse_args(argv)
    return serve(args.root, args.allow_mutations)


if __name__ == "__main__":
    raise SystemExit(main())
