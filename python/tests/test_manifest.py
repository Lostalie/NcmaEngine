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
        self.assertEqual(manifest["schema_version"], 11)
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
        self.assertEqual(current["scope"], "production_default_entry")
        self.assertEqual(current["application_host"], "C++")
        self.assertEqual(current["world_and_components"], "C#_Runtime.World")
        self.assertFalse(current["managed_world_is_native_wrapper"])
        self.assertTrue(current["managed_authoritative_world"])
        self.assertFalse(current["managed_application_host"])
        self.assertFalse(current["independent_renderer_physics_plugins"])
        candidate = manifest["architecture"]["candidate"]
        self.assertEqual(candidate["application_host"], "C#")
        self.assertTrue(candidate["managed_application_host"])
        self.assertEqual(candidate["native_module_abi"], {"major": 1, "minor": 0})
        for plugin in ("platform", "gui", "renderer", "physics"):
            self.assertTrue(candidate[f"independent_{plugin}_plugin"])
        self.assertEqual(candidate["physics_module_abi"], {"major": 1, "minor": 1})
        self.assertTrue((ROOT / candidate["physics_report"]).is_file())
        self.assertTrue(candidate["physics_automated_gate_passed"])
        self.assertTrue(candidate["managed_physics_service"])
        self.assertFalse(candidate["physics_mcp_tools_implemented"])
        self.assertTrue((ROOT / "managed/Ncma.Physics/PhysicsService.cs").is_file())
        self.assertEqual(candidate["renderer_scope"], "DX11_reference_scene_only")
        self.assertTrue(candidate["automated_gate_passed"])
        for pending in ("physics_scene_integration", "vulkan_renderer_implemented",
                        "h5_accepted", "visible_third_party_client_acceptance_passed",
                        "manual_acceptance_passed", "default_entry_switched"):
            self.assertFalse(candidate[pending])
        self.assertTrue((ROOT / candidate["entry_project"] / "Ncma.Editor.App.csproj").is_file())
        self.assertTrue((ROOT / candidate["report"]).is_file())
        self.assertEqual(candidate["editor_business_migration"], "candidate_A_B_C_D_E_F_implemented_manual_gate_pending")
        self.assertTrue(candidate["editor_business_panels_migrated"])
        self.assertTrue(candidate["live_editor_mcp_migrated"])
        self.assertEqual(candidate["renderer_module_abi"], {"major": 1, "minor": 1})
        self.assertEqual(candidate["animation_kernel_abi"], 2)
        self.assertIn("native_console_pages", candidate["editor_business_slice"])
        self.assertEqual(candidate["gui_module_abi"], {"major": 1, "minor": 2})
        self.assertEqual(candidate["fbx_character_abi"], 2)
        self.assertIn("large_component_json_pages", candidate["editor_business_slice"])
        self.assertIn("reference_render_controls", candidate["editor_business_slice"])
        self.assertEqual(candidate["fbx_preview_history_owner"], "C#")
        self.assertEqual(candidate["fbx_preview_history_limit"], 8)
        self.assertTrue(candidate["fbx_preview_wireframe_migrated"])
        self.assertFalse(candidate["fbx_preview_scene_integration"])
        self.assertTrue((ROOT / candidate["fbx_preview_report"]).is_file())
        self.assertTrue((ROOT / candidate["editor_business_report"]).is_file())
        self.assertFalse(manifest["gameplay"]["object_language_selection_implemented"])
        self.assertEqual(manifest["gameplay"]["language_selection_point"], "fixed_csharp")
        self.assertFalse(manifest["gameplay"]["mixed_language_behaviours"])
        self.assertFalse(manifest["gameplay"]["language_change_implemented"])
        self.assertFalse(manifest["gameplay"]["shared_native_core"])
        self.assertTrue(candidate["managed_runtime_service"])
        self.assertTrue(candidate["player_headless_implemented"])
        self.assertTrue(candidate["player_framework_dependent_package"])
        self.assertFalse(candidate["player_self_contained_verified"])
        self.assertFalse(candidate["player_h7_complete"])
        self.assertEqual(candidate["player_graphical_scope"], "DX11_reference_scene_only")
        self.assertTrue((ROOT / candidate["player_project"] / "Ncma.Player.App.csproj").is_file())
        self.assertTrue((ROOT / candidate["player_report"]).is_file())
        self.assertEqual(manifest["scene"]["object_api"], "GameObject")
        self.assertFalse(manifest["scene"]["node_frontend_api"])
        self.assertIsNone(manifest["scene"]["native_object_api_version"])
        self.assertEqual(manifest["scene"]["native_plugin_abi_version"], 2)
        self.assertFalse(manifest["scene"]["legacy_native_exports"])
        self.assertEqual(manifest["scene"]["organization"], "flat_object_list")
        self.assertFalse(manifest["scene"]["parent_child_ownership"])
        self.assertFalse(manifest["scene"]["transform_inheritance"])
        self.assertEqual(manifest["scene"]["format_version"], 1)
        self.assertTrue(manifest["scene"]["general_component_serialization"])
        self.assertTrue(manifest["scene"]["complete_in_memory_component_snapshots"])
        self.assertFalse(manifest["scene"]["legacy_scene_compatibility"])
        self.assertEqual(manifest["scene"]["legacy_scene_versions_readable"], [])
        self.assertEqual(manifest["scene"]["file_extension"], ".ncmascene")
        self.assertTrue(manifest["scene"]["atomic_file_save"])
        self.assertEqual(manifest["gameplay"]["world_access"]["scene_host_bridge_version"], 6)
        self.assertEqual(manifest["gameplay"]["world_access"]["scope"], "csharp_managed_world")

    def test_m2_8_preflight_does_not_promote_or_authorize_cleanup(self) -> None:
        candidate = inspect_project(ROOT)["architecture"]["candidate"]
        self.assertTrue(candidate["m2_8_read_only_preflight"])
        self.assertTrue(candidate["m2_8_frozen_semantic_references"])
        self.assertTrue(candidate["m2_8_kernel_reference_fixture"])
        self.assertEqual(candidate["m2_8_kernel_reference_scope"], "shared_numerical_shaders_not_independent_algorithm_oracle")
        for gate in ("h8_accepted", "consolidated_cleanup_completed", "default_entry_switched", "player_h7_complete"):
            self.assertFalse(candidate[gate])
        self.assertTrue((ROOT / candidate["m2_8_report"]).is_file())
        for configuration in ("Debug", "Release"):
            reference = json.loads((ROOT / f"tests/assets/m2/runtime-historical-reference-{configuration}.json").read_text(encoding="utf-8"))
            self.assertEqual(reference["configuration"], configuration)
            self.assertEqual(len(reference["rows"]), 14)

    def test_python_gameplay_removed_without_advertising_module_integration(self) -> None:
        manifest = inspect_project(ROOT)
        backends = manifest["gameplay"]["backends"]
        self.assertEqual(backends["csharp"]["status"], "integrated")
        self.assertNotIn("python", backends)
        self.assertTrue(manifest["gameplay"]["python_gameplay_removed"])
        self.assertFalse(manifest["gameplay"]["legacy_compatibility_retained"])
        self.assertFalse(manifest["gameplay"]["binding_language_selection_implemented"])
        self.assertEqual(manifest["gameplay"]["scene_format_version"], 1)
        self.assertFalse(manifest["gameplay"]["standalone_game_export"])
        self.assertFalse(manifest["gameplay"]["project_language_selection_implemented"])
        self.assertFalse(manifest["mcp"]["live_editor_connection"])
        self.assertTrue(manifest["animation"]["model_import"])
        self.assertEqual(manifest["animation"]["model_import_formats"], ["FBX"])
        self.assertFalse(manifest["animation"]["gpu_skinning"])
        self.assertEqual(manifest["animation"]["character_resource_abi"], 2)

    def test_inspect_cli_emits_versioned_manifest(self) -> None:
        result = subprocess.run([sys.executable, "-m", "ncma_tools.cli", "inspect", str(ROOT)],
                                capture_output=True, text=True, encoding="utf-8", timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        manifest = json.loads(result.stdout)
        self.assertEqual(manifest["schema_version"], 11)
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


    def test_managed_commands_and_fixed_step_integrated_without_claiming_transport(self) -> None:
        manifest = inspect_project(ROOT)
        runtime = manifest["managed_runtime"]
        self.assertEqual(runtime["authoritative_owner"], "C#")
        self.assertFalse(runtime["native_dependency"])
        self.assertFalse(runtime["python_dependency"])
        self.assertTrue(runtime["fixed_step_systems"])
        self.assertTrue(runtime["undoable_atomic_scene_transactions"])
        self.assertTrue(runtime["editor_integrated"])
        self.assertTrue(runtime["edit_session_live_editor_integrated"])
        self.assertEqual(runtime["editor_history_owner"], "C#")
        self.assertFalse(runtime["native_scene_command_stack"])
        self.assertTrue(runtime["world_runner_editor_integrated"])
        self.assertEqual(manifest["scene"]["gameplay_host_bridge_version"], 5)
        self.assertTrue(manifest["gameplay"]["backends"]["csharp"]["fixed_update_scheduler"])
        play = manifest["gameplay"]["play_session"]
        self.assertEqual(play["owner"], "C#")
        self.assertEqual(play["on_update_world_access"], "read_only")
        for unfinished in ("input_snapshot_implemented", "render_interpolation_implemented",
                           "runtime_structural_commands_implemented", "transactional_signal_consumption_implemented",
                           "reload_preflight_implemented"):
            self.assertTrue(play[unfinished])
        self.assertTrue(runtime["replaces_legacy_world"])
        self.assertTrue(runtime["managed_behaviour_lifecycle"])
        self.assertFalse(runtime["legacy_scene_import"])
        self.assertEqual(runtime["scene_file_codec_owner"], "C#")
        self.assertEqual(runtime["snapshot_format"], "scene_document_json_v1")
        self.assertTrue(runtime["complete_scene_document"])
        self.assertFalse(runtime["complete_snapshot_native_undo_and_play"])
        self.assertTrue(runtime["complete_snapshot_managed_undo_and_play"])
        self.assertEqual(manifest["architecture"]["implemented"]["world_and_components"], "C#_Runtime.World")

    def test_managed_ai_gateway_has_narrow_honest_capabilities(self) -> None:
        manifest = inspect_project(ROOT)
        gateway = manifest["agent_contract"]
        self.assertTrue(gateway["managed_gateway_implemented"])
        self.assertEqual(gateway["managed_contract_version"], 2)
        self.assertEqual(gateway["domains"]["scene"], "active_editor_scoped_MCP_owner_thread")
        self.assertTrue(gateway["managed_gateway_transport_implemented"])
        self.assertTrue(manifest["editor_mcp"]["remote_mutations_implemented"])
        self.assertTrue(manifest["editor_mcp"]["trusted_scope_grants_implemented"])
        self.assertFalse(manifest["editor_mcp"]["enabled_by_default"])
        self.assertFalse(gateway["arbitrary_code_execution"])
        self.assertFalse(gateway["project_file_mutation_gateway"])
        self.assertEqual(len(gateway["managed_capabilities"]), 10)
        self.assertIn("ncma.scene.transaction", gateway["managed_capabilities"])
        self.assertEqual(gateway["domains"]["ui"], "model_only_no_editing_gateway")


if __name__ == "__main__":
    unittest.main()
