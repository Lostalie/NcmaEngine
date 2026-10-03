from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import unittest

from ncma_tools.manifest import inspect_project


ROOT = Path(__file__).resolve().parents[2]


class ProjectManifestTests(unittest.TestCase):
    def test_csharp_gameplay_direction_and_legacy_runtime_status(self) -> None:
        manifest = inspect_project(ROOT)
        self.assertEqual(manifest["schema_version"], 5)
        self.assertEqual(manifest["languages"]["scope"], "target_architecture")
        self.assertEqual(manifest["languages"]["core"], "C#/.NET 8")
        self.assertEqual(manifest["languages"]["gameplay"], ["C#/.NET 8"])
        self.assertEqual(manifest["languages"]["native_plugins"], "C++20")
        self.assertEqual(manifest["gameplay"]["policy"], "csharp_only")
        self.assertTrue(manifest["gameplay"]["csharp_only_runtime_enforced"])
        self.assertEqual(manifest["gameplay"]["current_runtime_policy"], "csharp_only")
        target = manifest["architecture"]["target"]
        for owner in ("application_host", "world_and_components", "system_scheduler", "editor_business_logic",
                      "scene_asset_metadata_serialization", "gameplay"):
            self.assertEqual(target[owner], "C#")
        current = manifest["architecture"]["implemented"]
        self.assertEqual(current["application_host"], "C++")
        self.assertEqual(current["world_and_components"], "C++_SceneWorld")
        self.assertTrue(current["managed_world_is_native_wrapper"])
        self.assertFalse(current["managed_authoritative_world"])
        self.assertFalse(current["managed_application_host"])
        self.assertFalse(current["independent_renderer_physics_plugins"])
        self.assertFalse(manifest["gameplay"]["object_language_selection_implemented"])
        self.assertEqual(manifest["gameplay"]["language_selection_point"], "fixed_csharp")
        self.assertFalse(manifest["gameplay"]["mixed_language_behaviours"])
        self.assertFalse(manifest["gameplay"]["language_change_implemented"])
        self.assertTrue(manifest["gameplay"]["shared_native_core"])
        self.assertEqual(manifest["scene"]["object_api"], "GameObject")
        self.assertFalse(manifest["scene"]["node_frontend_api"])
        self.assertEqual(manifest["scene"]["native_object_api_version"], 4)
        self.assertEqual(manifest["scene"]["organization"], "flat_object_list")
        self.assertFalse(manifest["scene"]["parent_child_ownership"])
        self.assertFalse(manifest["scene"]["transform_inheritance"])
        self.assertEqual(manifest["scene"]["format_version"], 5)
        self.assertFalse(manifest["scene"]["general_component_serialization"])
        self.assertEqual(manifest["gameplay"]["world_access"]["scope"], "csharp_native_world")

    def test_python_gameplay_removed_without_advertising_module_integration(self) -> None:
        manifest = inspect_project(ROOT)
        backends = manifest["gameplay"]["backends"]
        self.assertEqual(backends["csharp"]["status"], "integrated")
        self.assertNotIn("python", backends)
        self.assertTrue(manifest["gameplay"]["python_gameplay_removed"])
        self.assertFalse(manifest["gameplay"]["legacy_compatibility_retained"])
        self.assertFalse(manifest["gameplay"]["binding_language_selection_implemented"])
        self.assertEqual(manifest["gameplay"]["scene_format_version"], 5)
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
        self.assertEqual(manifest["schema_version"], 5)
        self.assertEqual(set(manifest["gameplay"]["backends"]), {"csharp"})

    def test_independent_python_transport_targets_are_not_implemented(self) -> None:
        modules = inspect_project(ROOT)["python_modules"]
        self.assertEqual(modules["role"], "independent_ai_and_specialized_modules")
        self.assertFalse(modules["owns_game_objects"])
        self.assertFalse(modules["gameplay_scripting"])
        self.assertTrue(modules["plugin_development_option"])
        self.assertEqual(modules["recommended_default_transport"], "grpc_worker")
        self.assertFalse(modules["transport_selection_finalized"])
        self.assertFalse(modules["transport_neutral_contract_implemented"])
        self.assertFalse(modules["ai_worker_implemented"])
        self.assertFalse(modules["runtime_result_application_implemented"])
        self.assertEqual(set(modules["transports"]), {"pythonnet", "grpc", "zeromq"})
        for transport in modules["transports"].values():
            self.assertFalse(transport["implemented"])


    def test_managed_foundation_does_not_claim_editor_migration(self) -> None:
        manifest = inspect_project(ROOT)
        runtime = manifest["managed_runtime"]
        self.assertEqual(runtime["authoritative_owner"], "C#")
        self.assertFalse(runtime["native_dependency"])
        self.assertFalse(runtime["python_dependency"])
        self.assertTrue(runtime["fixed_step_systems"])
        self.assertTrue(runtime["undoable_atomic_scene_transactions"])
        self.assertFalse(runtime["editor_integrated"])
        self.assertFalse(runtime["replaces_legacy_world"])
        self.assertFalse(runtime["managed_behaviour_lifecycle"])
        self.assertFalse(runtime["legacy_scene_import"])
        self.assertEqual(manifest["architecture"]["implemented"]["world_and_components"], "C++_SceneWorld")

    def test_managed_ai_gateway_has_narrow_honest_capabilities(self) -> None:
        gateway = inspect_project(ROOT)["agent_contract"]
        self.assertTrue(gateway["managed_gateway_implemented"])
        self.assertFalse(gateway["managed_gateway_transport_implemented"])
        self.assertFalse(gateway["arbitrary_code_execution"])
        self.assertFalse(gateway["project_file_mutation_gateway"])
        self.assertEqual(len(gateway["managed_capabilities"]), 8)
        self.assertIn("ncma.scene.transaction", gateway["managed_capabilities"])
        self.assertEqual(gateway["domains"]["ui"], "model_only_no_editing_gateway")


if __name__ == "__main__":
    unittest.main()
