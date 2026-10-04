from __future__ import annotations

from pathlib import Path
import unittest

from ncma_tools.manifest import inspect_project


ROOT = Path(__file__).resolve().parents[2]


class GameplayRemovalTests(unittest.TestCase):
    def test_old_entry_and_bridge_sources_and_build_consumers_removed(self) -> None:
        for relative in ("engine/source/editor/EditorMain.cpp", "engine/source/runtime/script/runtime/ManagedHost.cpp",
                         "engine/source/runtime/scene/ManagedSceneClient.cpp", "engine/source/runtime/interop/NcmaGameplayBridge.h",
                         "managed/Ncma.Managed.Host/Ncma.Managed.Host.csproj", "tests/plugins/LegacyReferenceCapture.cpp"):
            self.assertFalse((ROOT / relative).exists())
        for relative in ("CMakeLists.txt", "NcmaEngine.sln", "NcmaEngine.vcxproj", "scripts/Build.ps1"):
            text = (ROOT / relative).read_text(encoding="utf-8-sig")
            for removed in ("Ncma.Managed.Host", "ManagedSceneClient", "DotNetGameplayRuntime", "NcmaLegacyReferenceCapture"):
                self.assertNotIn(removed, text)
        self.assertTrue((ROOT / "scripts/EditorDeployment.ps1").is_file())
        self.assertTrue((ROOT / "tests/assets/m2/render-legacy-reference.json").is_file())

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
        self.assertFalse((ROOT / "engine/source/editor/EditorApplication.h").exists())

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
        self.assertFalse((ROOT / "engine/source/editor/EditorApplication.cpp").exists())
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
