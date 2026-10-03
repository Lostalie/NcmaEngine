from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import unittest

from ncma_tools.manifest import inspect_project


ROOT = Path(__file__).resolve().parents[2]


class ProjectManifestTests(unittest.TestCase):
    def test_equal_gameplay_language_choices(self) -> None:
        manifest = inspect_project(ROOT)
        self.assertEqual(manifest["schema_version"], 2)
        self.assertEqual(manifest["languages"]["core"], "C++20")
        self.assertEqual(manifest["languages"]["gameplay"], ["C#/.NET 8", "Python"])
        self.assertEqual(manifest["gameplay"]["policy"], "either_language")
        self.assertTrue(manifest["gameplay"]["shared_native_core"])

    def test_python_preview_does_not_advertise_unimplemented_features(self) -> None:
        manifest = inspect_project(ROOT)
        backends = manifest["gameplay"]["backends"]
        self.assertEqual(backends["csharp"]["status"], "integrated")
        self.assertEqual(backends["python"]["status"], "integrated_preview")
        self.assertTrue(backends["python"]["scene_binding"])
        self.assertTrue(manifest["gameplay"]["binding_language_selection_implemented"])
        self.assertEqual(manifest["gameplay"]["scene_format_version"], 3)
        self.assertFalse(manifest["gameplay"]["standalone_game_export"])
        self.assertFalse(manifest["gameplay"]["project_language_selection_implemented"])
        self.assertFalse(manifest["mcp"]["live_editor_connection"])
        self.assertTrue(manifest["animation"]["model_import"])
        self.assertEqual(manifest["animation"]["model_import_formats"], ["FBX"])
        self.assertFalse(manifest["animation"]["gpu_skinning"])

    def test_inspect_cli_emits_versioned_manifest(self) -> None:
        result = subprocess.run([sys.executable, "-m", "ncma_tools.cli", "inspect", str(ROOT)],
                                capture_output=True, text=True, encoding="utf-8", timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        manifest = json.loads(result.stdout)
        self.assertEqual(manifest["schema_version"], 2)
        self.assertEqual(manifest["gameplay"]["backends"]["python"]["reference"], "Infernux")


if __name__ == "__main__":
    unittest.main()
