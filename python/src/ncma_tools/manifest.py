from __future__ import annotations

import json
from pathlib import Path
from typing import Any


def inspect_project(root: Path) -> dict[str, Any]:
    root = root.resolve()
    return {
        "schema_version": 2,
        "engine": "NcmaEngine",
        "root": str(root),
        "languages": {
            "core": "C++20",
            "gameplay": ["C#/.NET 8", "Python"],
            "tools_and_ai": "Python 3.10+",
        },
        "gameplay": {
            "policy": "either_language",
            "shared_native_core": True,
            "project_language_selection_implemented": False,
            "backends": {
                "csharp": {
                    "reference": "ProwlEngine",
                    "status": "integrated",
                    "scene_binding": True,
                    "fixed_update_scheduler": False,
                },
                "python": {
                    "reference": "Infernux",
                    "status": "integrated_preview",
                    "scene_binding": True,
                    "fixed_update_scheduler": False,
                },
            },
            "binding_language_selection_implemented": True,
            "scene_format_version": 3,
            "standalone_game_export": False,
        },
        "render_backends": ["Direct3D11", "Vulkan"],
        "agent_contract": {
            "format": "JSON Schema capabilities",
            "mutation_risks": ["read_only", "reversible", "destructive"],
        },
        "animation": {
            "runtime": "C++ skeletal sampling, root motion, notifies and action preview",
            "native_abi": 1,
            "scene_component": False,
            "model_import": True,
            "model_import_formats": ["FBX"],
            "gpu_skinning": False,
        },
        "mcp": {
            "transport": "stdio",
            "protocol_version": "2025-11-25",
            "entrypoint": "python -m ncma_tools.cli mcp --root <project>",
            "scope": "isolated_animation_preview",
            "live_editor_connection": False,
            "default_permission": "read_only",
            "mutation_opt_in": "--allow-mutations",
            "tool_prefix": "ncma.animation.",
        },
        "paths": {
            "native": str(root / "engine" / "source" / "runtime"),
            "managed": str(root / "managed" / "Ncma.Managed"),
            "python": str(root / "python"),
        },
    }


def write_manifest(root: Path, output: Path) -> None:
    output.write_text(
        json.dumps(inspect_project(root), ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
