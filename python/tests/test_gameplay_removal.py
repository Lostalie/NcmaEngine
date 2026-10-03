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

    def test_native_scene_world_and_world_exports_removed(self) -> None:
        for path in ("engine/source/runtime/scene/SceneWorld.cpp", "engine/source/runtime/scene/SceneWorld.h",
                     "tests/WorldAccessTests.cpp", "engine/source/runtime/scene/SceneSerializer.cpp",
                     "engine/source/runtime/scene/SceneSerializer.h", "engine/source/runtime/scene/SceneSnapshot.h",
                     "managed/Ncma.Scene/LegacySceneAdapter.cs", "managed/Ncma.Scene/SceneSnapshotCodec.cs"):
            self.assertFalse((ROOT / path).exists())
        native = (ROOT / "engine/source/runtime/interop/NcmaNativeApi.h").read_text(encoding="utf-8")
        self.assertNotIn("ncma_world_", native)
        managed = (ROOT / "managed/Ncma.Managed/Native.cs").read_text(encoding="utf-8")
        self.assertNotIn("ncma_world_", managed)
        current = inspect_project(ROOT)["architecture"]["implemented"]
        self.assertTrue(current["managed_authoritative_world"])
        self.assertFalse(current["managed_world_is_native_wrapper"])

    def test_single_managed_editor_history_and_no_native_bypass(self) -> None:
        for path in ("managed/Ncma.Runtime/Editing.cs", "engine/source/runtime/scene/SceneCommandStack.h",
                     "engine/source/runtime/scene/SceneCommandStack.cpp"):
            self.assertFalse((ROOT / path).exists())
        editor = (ROOT / "engine/source/editor/EditorApplication.cpp").read_text(encoding="utf-8")
        for removed in ("m_SceneCommands", "CaptureEditorState", "MarkSaved", "m_Scene.UpdateBehaviour", "m_Scene.SetObjectName", "m_Scene.RestoreDocument"):
            self.assertNotIn(removed, editor)
        cmake = (ROOT / "CMakeLists.txt").read_text(encoding="utf-8")
        self.assertNotIn("SceneCommandStack", cmake)
        self.assertIn("NcmaEditorCoreTests", cmake)

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
