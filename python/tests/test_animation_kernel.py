"""Consumer-owned policy parity; numerical DLL never receives action/undo commands."""
import json
import math
import os
import subprocess
import threading
import unittest
from pathlib import Path
from ncma_tools.animation import AnimationSession

ROOT = Path(__file__).resolve().parents[2]


class AnimationKernelTests(unittest.TestCase):
    def test_reference_sequence(self):
        build = Path(os.environ.get("NCMA_TEST_NATIVE_BUILD", ROOT / "out/build/windows-ninja-debug")).resolve(strict=True)
        self.assertTrue(build.is_relative_to(ROOT))
        fixture = build / "NcmaAnimationTests.exe"
        result = subprocess.run([str(fixture), "--action-reference"], capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stderr)
        reference = json.loads(result.stdout)
        self._compare_reference(reference)

    def _compare_reference(self, reference):
        commands = [(1, 1, ""), (4, .1, ""), (2, 0, "Attack"), (4, .2, ""), (4, .25, ""),
                    (2, 0, "Attack"), (6, 0, ""), (7, 0, ""), (4, 1, ""), (5, 0, ""),
                    (2, 0, "Dodge"), (4, .1, ""), (4, .8, ""), (6, 0, ""), (7, 0, "")]

        def compare(a, b):
            if isinstance(a, dict):
                self.assertEqual(set(a), set(b))
                for key in a:
                    compare(a[key], b[key])
            elif isinstance(a, list):
                self.assertEqual(len(a), len(b))
                for left, right in zip(a, b):
                    compare(left, right)
            elif isinstance(a, (float, int)) and not isinstance(a, bool):
                self.assertTrue(math.isclose(a, b, abs_tol=2e-5), (a, b))
            else:
                self.assertEqual(a, b)

        with AnimationSession(ROOT) as session:
            compare(reference[0], session.inspect())
            for expected, command in zip(reference[1:], commands):
                compare(expected, session.command(*command))

    def test_failure_copy_history_and_affinity(self):
        with AnimationSession(ROOT) as session:
            before = session.inspect()
            session.command(2, 0, "Attack")
            session.command(4, .2)
            self.assertEqual(before["state"], "Idle")
            current = session.inspect()
            for command in [(4, float("nan"), ""), (1, 2, ""), (2, 0, "Dodge")]:
                with self.assertRaises((ValueError, RuntimeError)):
                    session.command(*command)
                self.assertEqual(current, session.inspect())
            session.command(5)
            for _ in range(140):
                session.command(1, 0)
            for _ in range(128):
                session.command(6)
            with self.assertRaises(RuntimeError):
                session.command(6)
            denied = []
            def wrong_thread():
                try:
                    session.inspect()
                except RuntimeError:
                    denied.append(True)
            thread = threading.Thread(target=wrong_thread)
            thread.start()
            thread.join()
            self.assertEqual(denied, [True])

    def test_old_policy_version_rejected_and_copied_result_survives_close(self):
        with AnimationSession(ROOT) as session:
            self.assertEqual(session._native.ncma_animation_create(1), 0)
            self.assertEqual(session._native.ncma_animation_create(99), 0)
            copied = session.inspect()
        self.assertEqual(copied["state"], "Idle")
        with self.assertRaises(RuntimeError):
            session.inspect()


if __name__ == "__main__":
    unittest.main()
