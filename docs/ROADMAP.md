# Development roadmap

## Product focus — animation-driven action games

Prioritize a real skeletal character pipeline over additional preview effects: model/clip import,
GPU skinning, animation components, collision-aware root motion and C#/Python action gameplay, then
visual graph/Montage authoring. See [ANIMATION.md](ANIMATION.md) for the ordered action-game roadmap
and [ANIMATION_MCP.md](ANIMATION_MCP.md) for the working, isolated preview MCP service.
Character source format is FBX first; the current import/CPU preview is documented in [FBX_IMPORT.md](FBX_IMPORT.md).

## Language direction — equal C# / Python gameplay choices

C++ remains the runtime core. Users must be able to implement primary game logic in either
C# (ProwlEngine design reference) or Python (Infernux design reference). Python also remains
the tooling/AI language. Both languages now have editor-preview gameplay integration, selected per
Behaviour attachment. Existing Python CLI/MCP and legacy prototype files are separate from the new
CPython host. See [ARCHITECTURE.md](ARCHITECTURE.md) and [PYTHON_GAMEPLAY.md](PYTHON_GAMEPLAY.md).

- Completed preview: CPython host, Node/Behaviour API, lifecycle dispatch and exception reporting.
- Completed: language-tagged `.ncscene` v3, scalar Inspector metadata, v1/v2 migration and undo/redo.
- Completed preview: Python play-scene isolation, manual reload cleanup and gameplay regression tests.
- Completed smoke coverage: C# and Python updating separate nodes in the same native scene.
- NOT implemented: project-wide default language, project environments and dependency/module management.
- NOT implemented for either language: fixed-step scheduling and standalone game export.

## Foundation — current slice

- CMake/C++20 `NcmaCore` and `NcmaNative` targets.
- Hybrid scene node/component model with hierarchy invariants and world transforms.
- Stable C ABI plus a compiling C# gameplay API and managed/native smoke test.
- Python tooling package and machine-readable project manifest.
- RHI backend registry/capability contract and headless null backend.
- Typed animation graph, PBR/soft-shadow settings, UI document/design-token models.
- Agent capability registry with JSON schemas and mutation risk.

## Milestone 1 — executable editor shell

Completion: editor launches, creates/saves/reloads a scene, edits hierarchy/transforms through undo/redo,
and runs a behavior authored in either C# or Python with isolated play mode and hot reload.
The C# and Python preview slices work; full gameplay services and game export remain future work.

- Completed: native Win32 `NcmaEngine.exe`, real D3D11 device/swapchain, ImGui editor workspace, hierarchy selection, node creation/deletion, transform inspector, viewport shell, project/console panels, play-state toolbar, resize support, and launch smoke test.
- Completed: D3D11 registered as a real RHI backend with capability reporting.
- Completed: stable node UUIDs, versioned `.ncscene` serialization, editor New/Open/Save,
  and undo/redo for hierarchy plus inspector transform/name edits.
- Add prefab/packed-scene overrides, serializable component metadata, and lifecycle.
- Completed foundation: runtime-discovered `hostfxr` embedding, collectible gameplay
  `AssemblyLoadContext`, lifecycle dispatch, editor play-mode ticking, and assembly reload.
- Completed: UUID-backed per-node C#/Python Behaviour attachment, enable/remove commands, numeric/bool
  Export reflection and Inspector editing, `.ncscene` v3 persistence (v1/v2 compatible), and undo/redo.
- Completed: isolated play scenes, real Node Transform updates over the native ABI, pause/resume,
  and reload/rebind with saved Export values. `Build.bat -GameplayOnly` rebuilds gameplay while the editor runs.
- Add private runtime-state migration, additional Export types, fixed-update scheduling, and file-watcher rebuild.
- Completed foundation: snapshot-backed command stack with selection restored by UUID.
- Extend reflection/property metadata beyond scalar C# Exports; add project settings and asset database.

## Milestone 2 — D3D11/Vulkan PBR parity

Completion: the same reference scene renders within tolerance on both APIs and passes RenderDoc captures without validation errors.

- Completed foundation: explicit backend selection, Vulkan loader probing, API-neutral buffer/texture/sampler contracts, D3D11 resource/view implementations, and real-device smoke coverage for HDR targets and sampled cascade shadow maps.
- Completed first draw path: API-neutral graphics pipeline and draw contracts, D3D11 HLSL compilation/input layouts/fixed-function state, and a persistent viewport preview draw.
- Completed 3D preview foundation: indexed draws, dynamic constant-buffer updates, resize-aware swap-chain depth, Eigen MVP transforms, and a rotating depth-tested cube.
- Implement resource/command/pipeline/swapchain interfaces for D3D11 first, then Vulkan.
- Add DXC shader compilation/reflection and cross-backend binding validation.
- Extend render graph, imported FBX mesh rendering, metallic-roughness PBR, IBL, HDR, tone mapping, and post-processing.
- Completed soft-shadow slice: four stabilized directional cascades, 10% cascade blending,
  and runtime-selectable 3x3/5x5 PCF quality levels.
- Completed PCSS slice: blocker search, receiver/blocker penumbra estimation, bounded
  variable-radius filtering, and editor control for the directional-light angular radius.
- Completed contact-shadow slice: sampled viewport depth, bounded view-space ray marching,
  thickness/distance fading, and editor controls for steps, reach, thickness, and strength.
- Maintain golden-image tests for both backends.

## Milestone 3 — animation authoring

Completion: import a skeletal character, build a locomotion state machine visually, preview it, save it, and run it identically in game.

- Completed runtime slice: validated immutable skeleton/clip data with UUIDs, keyframe sampling,
  quaternion and per-bone masked pose blending, model/skin matrices, interruptible cached-pose
  transitions, loop-safe translational/rotational root motion, and interval-based notifies.
- Completed action laboratory: procedural 12-bone Idle/Run/Attack/Dodge preview, action completion,
  combo/hit/invulnerability windows, bone visualization, deterministic stepping and undo/redo.
- Completed preview bindings: animation C ABI v1 and C# ActionAnimationSession. These do not yet
  bind imported skinned meshes or scene components. No UE asset compatibility is claimed.
- Completed FBX import slice: ASCII/binary, unit/axis conversion, skeletal hierarchy, triangulated
  mesh/UV/material slots, linear skin weights/bind matrices, sampled animation stacks and stable
  subasset UUIDs with an explicit asset identity. CPU-skinned wireframe editor preview uses
  undoable import/playback commands; native reference comparisons cover bind and animated poses.
  GPU skinning, texture loading, asset persistence, scene binding and live-editor MCP remain pending.

- Real-character compatibility tests, asset persistence, GPU skinning, compression and retargeting.
- Node graph editor with typed pins, search, subgraphs, state machines, blend spaces, layered blending, IK, debugging, and live preview.
- Compile authoring graphs into allocation-free runtime programs.

## Milestone 4 — Figma-style UI authoring

Completion: build a reusable responsive HUD with frames, auto-layout, components/instances, variants, constraints, tokens, preview, and undo/redo.

- Retained runtime UI renderer, text shaping/font atlas, input/focus/accessibility basics.
- Infinite canvas, selection/gizmos, snapping, rulers, zoom, layers, inspector, auto-layout, constraints.
- Components/instances, variants, shared styles/design tokens, nine-slice images, animation, and localization preview.

## Milestone 5 — native AI/Agent integration

Completion: an Agent can inspect a project, propose a reversible change, apply it through an undoable transaction, run validation, and report structured evidence.

- Completed first MCP slice: local stdio initialize/tools/list/tools/call, eight animation tools,
  input/output schemas, risk metadata, default read-only permission, revision guards and structured results.
  Python calls the native ABI and the same animation command path as the editor laboratory.
  Scope is an isolated procedural preview, NOT the active editor; live project transactions remain future work.

- Project/query/edit/build/test/play/inspect capability families.
- Permission scopes, dry-run plans, transactions, audit log, cancellation, budgets, and deterministic tool results.
- Python adapters for local inference and provider-neutral remote inference; no provider SDK in the C++ core.
- Scene/asset semantic index and optional runtime AI inference service.
