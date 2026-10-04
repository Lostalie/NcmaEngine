from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import unittest

from ncma_tools.fbx import ImportedCharacter, inspect_fbx


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "tests" / "assets" / "fbx"


class FbxToolsTests(unittest.TestCase):
    def test_native_inspection(self) -> None:
        for filename, binary in (("blender_279_sausage_6100_ascii.fbx", False),
                                 ("blender_279_sausage_7400_binary.fbx", True)):
            with self.subTest(filename=filename):
                result = inspect_fbx(ROOT, FIXTURES / filename)
                self.assertEqual(result["schema_version"], 1)
                self.assertEqual(result["binary"], binary)
                self.assertEqual(result["target_unit_meters"], 1)
                self.assertGreaterEqual(result["bones"], 3)
                self.assertGreaterEqual(len(result["clips"]), 2)
                self.assertGreater(result["meshes"][0]["triangles"], 0)

    def test_invalid_sample_rate_and_missing_source(self) -> None:
        source = FIXTURES / "blender_279_sausage_7400_binary.fbx"
        with self.assertRaises(RuntimeError):
            inspect_fbx(ROOT, source, float("nan"))
        with self.assertRaises(FileNotFoundError):
            inspect_fbx(ROOT, FIXTURES / "missing.fbx")

    def test_immutable_report_copy_and_disposal(self) -> None:
        character = ImportedCharacter(ROOT, FIXTURES / "blender_279_sausage_7400_binary.fbx")
        try:
            copied = character.inspect()
            copied["bones"] = -1
            self.assertGreaterEqual(character.inspect()["bones"], 3)
        finally:
            character.close()
        character.close()
        with self.assertRaises(RuntimeError):
            character.inspect()

    def test_cli(self) -> None:
        base = [sys.executable, "-m", "ncma_tools.cli", "import-fbx"]
        source = str(FIXTURES / "blender_279_sausage_7400_binary.fbx")
        result = subprocess.run(base + [source, "--root", str(ROOT)],
                                capture_output=True, text=True, encoding="utf-8", timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["format"], "FBX")
        rejected = subprocess.run(base + [source, "--root", str(ROOT), "--sample-rate", "0"],
                                  capture_output=True, text=True, encoding="utf-8", timeout=30)
        self.assertEqual(rejected.returncode, 1)
        self.assertIn("sample rate", rejected.stderr)


if __name__ == "__main__":
    unittest.main()
