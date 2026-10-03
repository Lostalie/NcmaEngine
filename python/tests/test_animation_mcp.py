from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import unittest

from ncma_tools.animation import AnimationSession
from ncma_tools.mcp_server import AnimationMcpServer, PROTOCOL_VERSION, tools_manifest

ROOT = Path(__file__).resolve().parents[2]


def initialize() -> dict:
    return {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
        "protocolVersion": PROTOCOL_VERSION, "capabilities": {},
        "clientInfo": {"name": "ncma-test", "version": "1"}}}


class AnimationMcpTests(unittest.TestCase):
    def setUp(self):
        self.session = AnimationSession(ROOT)
        self.server = AnimationMcpServer(self.session, allow_mutations=True)
        self.server.handle(initialize())
        self.server.handle({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def tearDown(self):
        self.session.close()

    def call(self, name: str, arguments: dict | None = None) -> dict:
        return self.server.handle({"jsonrpc": "2.0", "id": 2, "method": "tools/call",
            "params": {"name": "ncma.animation." + name, "arguments": arguments or {}}})

    def snapshot(self, name: str, arguments: dict | None = None) -> dict:
        response = self.call(name, arguments)["result"]
        self.assertFalse(response["isError"])
        self.assertEqual(json.loads(response["content"][0]["text"]), response["structuredContent"])
        return response["structuredContent"]["snapshot"]

    def test_real_runtime_and_undo(self):
        initial = self.snapshot("inspect")
        self.assertEqual(initial["state"], "Idle")
        self.assertEqual(len(initial["bones"]), 12)
        self.snapshot("trigger_action", {"action": "Attack", "expected_revision": 0})
        attack = self.snapshot("step", {"seconds": 0.2})
        self.assertTrue(attack["hit_window"])
        self.assertGreater(attack["root_delta"]["position"][2], 0)
        self.assertEqual(attack["events"][0]["name"], "Hit.Start")
        self.assertEqual(self.snapshot("undo")["time"], 0)
        restored = self.snapshot("redo")
        self.assertEqual(restored["events"], attack["events"])
        self.assertEqual(restored["actor"], attack["actor"])
        self.snapshot("reset")
        self.assertEqual(self.snapshot("inspect")["state"], "Idle")

    def test_default_readonly(self):
        self.server.allow_mutations = False
        result = self.call("set_speed", {"speed": 1})["result"]
        self.assertTrue(result["isError"])
        self.assertEqual(result["structuredContent"]["error"]["code"], "permission_denied")
        self.assertEqual(self.snapshot("inspect")["revision"], 0)

    def test_revision_guard(self):
        self.snapshot("set_speed", {"speed": 1})
        result = self.call("step", {"seconds": 0.5, "expected_revision": 0})["result"]
        self.assertEqual(result["structuredContent"]["error"]["code"], "revision_conflict")
        self.assertEqual(self.snapshot("inspect")["time"], 0)

    def test_schema_validation_is_atomic(self):
        cases = [("step", {"seconds": -1}), ("step", {"seconds": float("nan")}),
                 ("step", {"seconds": True}), ("set_speed", {"speed": 10**400}),
                 ("set_paused", {"paused": 1}), ("trigger_action", {"action": "arbitrary"}),
                 ("inspect", {"path": "../../"}), ("step", {}),
                 ("reset", {"expected_revision": None}), ("undo", {"expected_revision": True})]
        before = self.snapshot("inspect")
        for name, arguments in cases:
            with self.subTest(name=name, arguments=arguments):
                self.assertEqual(self.call(name, arguments)["error"]["code"], -32602)
                self.assertEqual(self.snapshot("inspect"), before)

    def test_native_errors_are_tool_errors(self):
        self.assertTrue(self.call("undo")["result"]["isError"])
        self.snapshot("trigger_action", {"action": "Dodge"})
        self.assertTrue(self.call("trigger_action", {"action": "Attack"})["result"]["isError"])

    def test_lifecycle(self):
        server = AnimationMcpServer(self.session)
        request = {"jsonrpc": "2.0", "id": "list", "method": "tools/list"}
        self.assertEqual(server.handle(request)["error"]["code"], -32002)
        handshake = initialize()
        handshake["params"]["protocolVersion"] = "unsupported-version"
        self.assertEqual(server.handle(handshake)["result"]["protocolVersion"], PROTOCOL_VERSION)
        self.assertEqual(server.handle(request)["error"]["code"], -32002)
        self.assertIsNone(server.handle({"jsonrpc": "2.0", "method": "notifications/initialized"}))
        self.assertEqual(len(server.handle(request)["result"]["tools"]), 8)
        self.assertEqual(server.handle(initialize())["error"]["code"], -32600)

    def test_rpc_errors_and_notifications(self):
        self.assertEqual(self.server.handle([])["error"]["code"], -32600)
        self.assertIsNone(self.server.handle({"jsonrpc": "2.0", "method": "notifications/cancelled", "params": {"requestId": 7}}))
        self.assertEqual(self.server.handle({"jsonrpc": "2.0", "id": 8, "method": "unknown"})["error"]["code"], -32601)
        self.assertEqual(self.server.handle({"jsonrpc": "2.0", "id": 9, "method": "ping"})["result"], {})
        self.assertEqual(self.server.handle({"jsonrpc": "2.0", "id": 8, "method": "tools/call", "params": []})["error"]["code"], -32602)

    def test_capabilities_have_schema_and_risk(self):
        tools = tools_manifest()
        self.assertEqual(len({tool["name"] for tool in tools}), 8)
        for tool in tools:
            self.assertEqual(tool["inputSchema"]["type"], "object")
            self.assertFalse(tool["inputSchema"]["additionalProperties"])
            self.assertEqual(tool["outputSchema"]["type"], "object")
            self.assertFalse(tool["annotations"]["destructiveHint"])
            self.assertIn(tool["_meta"]["ncma/mutationRisk"], ("read_only", "reversible"))

    def test_independent_sessions_and_disposal(self):
        with AnimationSession(ROOT) as other:
            self.snapshot("trigger_action", {"action": "Attack"})
            self.assertEqual(other.inspect()["state"], "Idle")
            other.command(2, text="Attack")
            other.command(4, 0.4)
            self.assertEqual(self.snapshot("step", {"seconds": 0.4}), other.inspect())
        with self.assertRaises(RuntimeError):
            other.inspect()

    def test_stdio_subprocess(self):
        requests = [initialize(), {"jsonrpc": "2.0", "method": "notifications/initialized"},
                    {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
                    {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {
                        "name": "ncma.animation.trigger_action", "arguments": {"action": "Dodge"}}},
                    {"jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": {
                        "name": "ncma.animation.step", "arguments": {"seconds": 0.1}}}]
        env = dict(os.environ, PYTHONPATH=str(ROOT / "python" / "src"))
        completed = subprocess.run([sys.executable, "-m", "ncma_tools.cli", "mcp", "--root", str(ROOT), "--allow-mutations"],
            input=("{bad json}\n" + "\n".join(json.dumps(item) for item in requests) + "\n").encode(),
            capture_output=True, timeout=15, env=env, check=True)
        responses = [json.loads(line) for line in completed.stdout.splitlines()]
        self.assertEqual(len(responses), 5)  # No notification response or native stdout pollution.
        self.assertEqual(responses[0]["error"]["code"], -32700)
        self.assertEqual(responses[1]["result"]["protocolVersion"], PROTOCOL_VERSION)
        snapshot = responses[-1]["result"]["structuredContent"]["snapshot"]
        self.assertEqual(snapshot["state"], "Dodge")
        self.assertTrue(snapshot["invulnerable"])
        self.assertEqual(completed.stderr, b"")
        oversized = subprocess.run([sys.executable, "-m", "ncma_tools.cli", "mcp", "--root", str(ROOT)],
            input=b" " * 1_048_577 + b"\n", capture_output=True, timeout=15, env=env)
        self.assertEqual(oversized.returncode, 2)
        self.assertEqual(oversized.stdout, b"")
        self.assertIn(b"exceeds 1 MiB", oversized.stderr)


if __name__ == "__main__":
    unittest.main()
