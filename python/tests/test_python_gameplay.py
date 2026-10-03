from __future__ import annotations

import ctypes
import math
import os
from pathlib import Path
import shutil
import sys
import tempfile
import threading
import unittest

from ncma_gameplay import Behaviour, ExportKind, export
from ncma_gameplay.api import Native, Node, _WorldLease
from ncma_gameplay.host import Host


ROOT = Path(__file__).resolve().parents[2]
LIBRARY = ROOT / "out" / "managed" / "NcmaNative.dll"


class PythonGameplayTests(unittest.TestCase):
    def setUp(self) -> None:
        output = ROOT / "out" / "tests"
        output.mkdir(parents=True, exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(prefix="python-gameplay-", dir=output)
        self.scripts = Path(self.directory.name)
        self.native = Native(LIBRARY)
        self.dll = self.native.library
        self.dll.ncma_world_create.argtypes = [ctypes.c_char_p]
        self.dll.ncma_world_create.restype = ctypes.c_void_p
        self.dll.ncma_world_destroy.argtypes = [ctypes.c_void_p]
        self.dll.ncma_world_destroy.restype = None
        self.world = self.dll.ncma_world_create(b"PythonTest")
        self.assertTrue(self.world)
        self.node_id = self.native.check(self.dll.ncma_world_create_node(self.world, b"Probe", 0))
        self.host = None

    def tearDown(self) -> None:
        if self.host:
            try:
                self.host.close()
            except BaseException:
                pass
        self.dll.ncma_world_destroy(self.world)
        self.directory.cleanup()

    def load(self, code: str, filename: str = "probe.py") -> Host:
        (self.scripts / filename).write_text(code, encoding="utf-8")
        self.host = Host(str(self.scripts), str(LIBRARY))
        return self.host

    def node(self) -> Node:
        return Node(_WorldLease(self.native, self.world), self.node_id)

    def test_lifecycle_disabled_instances_and_stale_node(self) -> None:
        host = self.load("""from ncma_gameplay import Behaviour, export
class Probe(Behaviour):
    speed = export(2.0)
    events = []
    def on_create(self): self.events.append('create')
    def on_enable(self): self.events.append('enable')
    def on_update(self, dt):
        value = self.node.local_transform
        value.position.x += self.speed * dt
        self.node.local_transform = value
        self.events.append('update')
    def on_disable(self): self.events.append('disable')
    def on_destroy(self): self.events.append('destroy')
""")
        off_id = self.native.check(self.dll.ncma_world_create_node(self.world, b"Off", 0))
        host.bind(self.world, [(self.node_id, "probe.Probe", True, [("speed", 2, 4)]),
                               (off_id, "probe.Probe", False, [])])
        stale = host._instances[0][0].node
        script = host._types["probe.Probe"][0]
        host.tick(0.5)
        self.assertAlmostEqual(stale.local_transform.position.x, 2)
        self.assertEqual(script.events, ['create', 'enable', 'create', 'update'])
        host.end_scene()
        self.assertEqual(script.events[-3:], ['destroy', 'disable', 'destroy'])
        with self.assertRaisesRegex(RuntimeError, "ended play session"):
            _ = stale.local_transform
        self.assertAlmostEqual(self.node().local_transform.position.x, 2)

    def test_failed_bind_and_system_exit_are_contained(self) -> None:
        host = self.load("""from ncma_gameplay import Behaviour
class Probe(Behaviour):
    def on_enable(self): raise SystemExit('activation failed')
""")
        with self.assertRaises(SystemExit):
            host.bind(self.world, [(self.node_id, "probe.Probe", True, [])])
        self.assertEqual(host._instances, [])
        self.assertIsNone(host._lease)
        self.assertEqual(host.bind(self.world, [(self.node_id, "probe.Probe", False, [])]), 1)
        host.tick(0.2)

    def test_cleanup_failure_invalidates_all_nodes(self) -> None:
        host = self.load("""from ncma_gameplay import Behaviour
class Probe(Behaviour):
    events = []
    def on_disable(self): raise RuntimeError('cleanup failed')
    def on_destroy(self): self.events.append('destroy')
""")
        host.bind(self.world, [(self.node_id, "probe.Probe", True, [])])
        script, stale = host._types["probe.Probe"][0], host._instances[0][0].node
        with self.assertRaisesRegex(RuntimeError, "cleanup failed"):
            host.close()
        self.assertEqual(script.events, ['destroy'])
        self.assertEqual(host._types, {})
        with self.assertRaises(RuntimeError):
            _ = stale.local_transform

    def test_reload_reads_source_and_removes_project_modules(self) -> None:
        source = self.scripts / "probe.py"
        code = "from ncma_gameplay import Behaviour, export\nclass Probe(Behaviour):\n    speed = export(1.0)\n"
        host = self.load(code)
        previous_modules = host._modules.copy()
        timestamp = source.stat().st_mtime
        host.close()
        self.assertTrue(all(name not in sys.modules for name in previous_modules))
        source.write_text(code.replace("1.0", "2.0"), encoding="utf-8")
        os.utime(source, (timestamp, timestamp))
        self.host = Host(str(self.scripts), str(LIBRARY))
        self.assertEqual(self.host.describe()[0][1][0][2], 2)
        self.assertFalse((self.scripts / "__pycache__").exists())

    def test_export_validation_and_atomic_schema_rejection(self) -> None:
        for default, kind in ((float("nan"), None), (2**40, ExportKind.INTEGER), (1, ExportKind.BOOLEAN)):
            with self.assertRaises(ValueError):
                export(default, kind=kind)
        host = self.load("from ncma_gameplay import Behaviour, export\nclass Probe(Behaviour):\n    speed = export(1.0)\n")
        with self.assertRaisesRegex(ValueError, "changed Python Export"):
            host.bind(self.world, [(self.node_id, "probe.Probe", True, [("speed", 3, 1)])])
        self.assertEqual(host._instances, [])
        with self.assertRaises(RuntimeError):
            _ = Behaviour().node

    def test_unicode_script_and_sample_rotation(self) -> None:
        shutil.copyfile(ROOT / "gameplay" / "python" / "rotator.py", self.scripts / "角色.py")
        self.host = Host(str(self.scripts), str(LIBRARY))
        self.host.bind(self.world, [(self.node_id, "角色.RotatorBehaviour", True,
                                    [("degrees_per_second", 2, 90), ("multiplier", 3, 2), ("clockwise", 4, 1)])])
        self.host.tick(0.5)
        self.assertAlmostEqual(self.node().local_transform.rotation_y, -math.sqrt(0.5), places=5)

    def test_native_operations_and_thread_guard(self) -> None:
        node = self.node()
        child = node.create_child("Child")
        child.set_parent(None)
        child.set_parent(node)
        child.destroy()
        with self.assertRaises(RuntimeError):
            _ = child.local_transform
        value = node.local_transform
        value.rotation_w = 0
        with self.assertRaises(ValueError):
            node.local_transform = value
        errors = []
        def worker():
            try:
                _ = node.local_transform
            except RuntimeError as error:
                errors.append(str(error))
        thread = threading.Thread(target=worker)
        thread.start()
        thread.join(timeout=5)
        self.assertFalse(thread.is_alive())
        self.assertIn("play-session thread", errors[0])

    def test_bad_source_does_not_leave_registered_modules(self) -> None:
        before = set(sys.modules)
        (self.scripts / "broken.py").write_text("this is not valid Python!", encoding="utf-8")
        with self.assertRaises(SyntaxError):
            Host(str(self.scripts), str(LIBRARY))
        leaked = [name for name in set(sys.modules) - before if name.startswith("_ncma_gameplay_")]
        self.assertEqual(leaked, [])
