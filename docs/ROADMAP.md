# Development roadmap

## Current direction — C# runtime, independent Python modules, C++ plugins

C# owns gameplay, World/components, scheduling, scene/asset metadata and editor business logic.
Python is an independent AI/specialized module, not a GameObject language. C++ remains only in
performance-critical native plugins. This is a migration target, not a completed port.
See [ARCHITECTURE.md](ARCHITECTURE.md), [FRAMEWORK_REFACTOR.md](FRAMEWORK_REFACTOR.md)
and [PYTHON_MODULES.md](PYTHON_MODULES.md).

## Migration milestones — headless foundation landed, integration NOT implemented

1. Managed headless World/GameObject/component schemas, snapshots, safe references and fixed-step
   WorldRunner, with no native World or Python dependency. Initial Ncma.Runtime prototype implemented;
   type pools/query APIs and Behaviour lifecycle remain unimplemented.
2. Managed SceneAsset/serialization/editor commands and Undo/Play isolation; preserve old v1-v5
   C# records; reject legacy Python bindings without modifying their source, with explicit author conversion.
   Headless managed JSON v1, atomic EditSession and bounded Undo/Redo implemented; .ncscene import/live editor migration not implemented.
3. C# application main loop loading a native Renderer plugin; reuse DX11 code, prove resource
   lifetime/window/input handling. Vulkan rendering and validation remain future work.
4. Physics plugin plus C# action/animation/character policies; batch pose/math kernels only when
   justified. Complete FBX scene character, GPU skinning and collision-aware root motion.
5. One independent Python request/result adapter (proposed gRPC worker), validated asynchronous
   results at tick boundaries, timeouts/cancellation/restart/backpressure. pythonnet/ZeroMQ optional.
6. Managed Editor/Player distribution and plugin lifecycle; retire old C++ hostfxr entry/SceneWorld
   after asset/consumer migration and explicit cleanup authorization. Python gameplay host/SDK are already removed.

Keep flat objects, composition and independent networking; network application/session services
are C# by default, not component RPC/replication. Existing Build.bat/EXE and compatibility tests
remain active until replacements actually pass.

## Product focus — animation-driven action games

Prioritize imported skeletal characters, GPU skinning, animation components, collision-aware
root motion and C# action gameplay, then visual graph/Montage authoring. Python AI returns
proposals, not movement authority. See [ANIMATION.md](ANIMATION.md), [FBX_IMPORT.md](FBX_IMPORT.md)
and [ANIMATION_MCP.md](ANIMATION_MCP.md).

## Current legacy/compatibility baseline — retained, not the new target

Implemented native SceneWorld v5, flat objects, C#-only bindings, C# hostfxr gameplay,
isolated Play, scalar Exports, manual reload, UUID migration/backup/Undo,
World Access ABI v1 batches/references/signals and CLI/MCP remain available for regression.
Python gameplay hosts/SDK/examples and object language selection have been removed.
Python remains optional for tools, specialized modules and plugins; those transports and managed editor integration are NOT implemented. An independent managed headless World now exists.
The following feature milestones record existing native baseline and remaining product work;
their implementation ownership must follow the new direction above.

## Foundation — current slice

Framework target: managed World + GameObject + Component + independent Systems. The legacy active implementation
uses SceneWorld/GameObject. Scene Node source APIs have been removed and Transform is now a native
component. Independent managed registered-component schemas and fixed-step Systems are implemented;
full World/asset separation, generic queries, Behaviour migration and editor integration remain planned.
See [FRAMEWORK_REFACTOR.md](FRAMEWORK_REFACTOR.md) for sequence and compatibility gates.

- CMake/C++20 `NcmaCore` and `NcmaNative` targets.
- Independent C# Ncma.Runtime: optional value components, schema registry, UUIDs, snapshots and fixed-step Systems.
- Shared headless Editor/Agent EditSession: 8 capabilities, revision/permission guards, atomic transactions and Undo/Redo.
  No live editor connection or managed MCP transport yet; see [AI_DEVELOPMENT.md](AI_DEVELOPMENT.md).
- SceneWorld flat object/component store with independent world transforms and single-object deletion.
- Stable C ABI plus a compiling C# gameplay API and managed/native smoke test.
- Python tooling package and machine-readable project manifest.
- RHI backend registry/capability contract and headless null backend.
- Typed animation graph, PBR/soft-shadow settings, UI document/design-token models.
- Agent capability registry with JSON schemas and mutation risk.

## Milestone 1 — executable editor shell

Completion: editor launches, creates/saves/reloads a scene, edits flat objects/transforms through undo/redo,
and runs C# gameplay with isolated play mode and hot reload. Python gameplay removal is guarded by rejection tests; the managed editor integration, full gameplay services and game export remain future work.

- Completed: native Win32 `NcmaEngine.exe`, real D3D11 device/swapchain, ImGui editor workspace, flat object selection, object creation/deletion, transform inspector, viewport shell, project/console panels, play-state toolbar, resize support, and launch smoke test.
- Completed: D3D11 registered as a real RHI backend with capability reporting.
- Completed: stable object UUIDs, versioned `.ncscene` serialization, editor New/Open/Save,
  and undo/redo for object list plus inspector transform/name edits.
- Add Prefab instances/overrides, serializable component metadata, and lifecycle.
- Completed foundation: runtime-discovered `hostfxr` embedding, collectible gameplay
  `AssemblyLoadContext`, lifecycle dispatch, editor play-mode ticking, and assembly reload.
- Completed: UUID-backed per-object C# Behaviour attachment, enable/remove commands, numeric/bool
  Export reflection and Inspector editing, `.ncscene` v5 persistence (C# v1-v4 readable; Python language tags rejected atomically), and undo/redo.
- Completed: isolated play scenes, real GameObject Transform updates over the native ABI, pause/resume,
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

## Milestone 5 — independent Python AI and Agent integration

Completion: an Agent can inspect a project, propose a reversible change, apply it through an undoable transaction, run validation, and report structured evidence.

- Completed first MCP slice: local stdio initialize/tools/list/tools/call, eight animation tools,
  input/output schemas, risk metadata, default read-only permission, revision guards and structured results.
  Python calls the native ABI and the same animation command path as the editor laboratory.
  Scope is an isolated procedural preview, NOT the active editor; live project transactions remain future work.

- Project/query/edit/build/test/play/inspect capability families.
- Permission scopes, dry-run plans, transactions, audit log, cancellation, budgets, and deterministic tool results.
- Independent Python adapters for local/remote inference behind a C# module contract; no provider SDK in native plugins.
- Scene/asset semantic index and optional runtime AI inference service.

## Independent networking — planned, NOT implemented

Networking is a standalone optional C# service, with native transport kernels only when justified.
It must not be built into Actor/GameObject/component/
Behaviour semantics, automatic scene replication, RPC annotations or base-class network ownership.
See [NETWORKING.md](NETWORKING.md). This direction does not change the current character/animation priority.

- Separate transport/session/protocol library with no scene, renderer or scripting-host dependency.
- Explicit message schemas, bounded queues, validation, connection lifecycle and version negotiation.
- Application-owned adapters and session-scoped identities; apply inputs only at simulation tick boundaries.
- Explicit C# service/message interfaces, not Behaviour RPC. Python module IPC remains a separate contract;
  a versioned native ABI is needed only for optional native transport kernels.
- Choose transport/security/authentication requirements before implementing a backend; no protocol is selected yet.
- Later action-game policies: input sequencing, authority, snapshot interpolation, prediction/reconciliation
  and lag compensation in separate gameplay synchronization services, not scene/component base classes.
- Verify headless loopback, optional/offline operation, malformed-input rejection, disconnect/reconnect,
  stale identity handling and deterministic synchronization under simulated loss/latency.
