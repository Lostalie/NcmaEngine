from __future__ import annotations

from pathlib import Path
import unittest

from ncma_tools.manifest import inspect_project


ROOT = Path(__file__).resolve().parents[2]


class GameplayRemovalTests(unittest.TestCase):
    def test_no_python_gameplay_source_or_packaged_api(self) -> None:
        for relative in (
            "python/src/ncma_gameplay/api.py",
            "python/src/ncma_gameplay/host.py",
            "gameplay/python/rotator.py",
            "scripts/NcmaEngine/component.py",
            "engine/source/runtime/script/PythonHost.cpp",
            "engine/source/runtime/script/runtime/PythonGameplayRuntime.cpp",
        ):
            with self.subTest(path=relative):
                self.assertFalse((ROOT / relative).exists())
        packaging = (ROOT / "python/pyproject.toml").read_text(encoding="utf-8")
        self.assertIn('include = ["ncma_tools", "ncma_tools.*"]', packaging)

    def test_native_build_has_no_embedded_python_dependency(self) -> None:
        cmake = (ROOT / "CMakeLists.txt").read_text(encoding="utf-8")
        for removed in ("NcmaPythonGameplay", "Development.Embed", "Python3::Python", "ncma_deploy_python"):
            self.assertNotIn(removed, cmake)
        header = (ROOT / "engine/source/editor/EditorApplication.h").read_text(encoding="utf-8")
        self.assertNotIn("PythonGameplayRuntime", header)
        self.assertNotIn("ReloadPythonGameplay", header)

    def test_tools_and_isolated_mcp_remain_without_gameplay_backend(self) -> None:
        manifest = inspect_project(ROOT)
        self.assertEqual(set(manifest["gameplay"]["backends"]), {"csharp"})
        self.assertTrue(manifest["gameplay"]["csharp_only_runtime_enforced"])
        self.assertTrue(manifest["python_modules"]["plugin_development_option"])
        self.assertFalse(manifest["python_modules"]["gameplay_scripting"])
        self.assertEqual(manifest["mcp"]["scope"], "isolated_animation_preview")
        self.assertFalse(manifest["mcp"]["live_editor_connection"])


if __name__ == "__main__":
    unittest.main()
