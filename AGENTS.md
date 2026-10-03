# NcmaEngine collaboration guide

## Build and verify

- On Windows use Build.bat as the canonical build and verification entry point; it initializes the Visual Studio toolchain and works when direct PowerShell execution is disabled.
- Direct Ninja requires the VS2022 developer environment. Do not recommend it from an uninitialized shell.
- During migration build NcmaCore, NcmaNative and NcmaArchitectureTests before touching excluded legacy targets.
- Run CTest, build managed/Ncma.Managed, run managed/native smoke tests and python -m ncma_tools.cli inspect .; run strict new-format and removed-format rejection tests.
- Treat warnings in new code as defects. Do not claim a graphics backend is implemented until it renders the reference scene and passes API validation.

## Target architecture (supersedes the former equal-language/native-core design)

- C# owns gameplay and the main runtime: World/GameObject/component storage, behaviour lifecycle, System scheduling, scene/asset metadata, serialization, editor business logic, undo/transactions and application services.
- Python is an independent AI/tooling/scientific or other Python-advantaged module, NOT a GameObject/Behaviour gameplay language. Do not add new Python gameplay components or a new per-object language picker. Python gameplay hosts, SDKs, per-object language selection and preview execution have been removed. Python remains optional for tooling, specialized modules and plugins, not an implemented AI service.
- Python service adapters communicate with C# through a transport-neutral, versioned request/result contract. Proposed default: asynchronous out-of-process gRPC; pythonnet is opt-in for trusted in-process modules; ZeroMQ is an optional measured-workload transport. None is implemented yet. Never advertise ctypes or the removed C++ Python gameplay host as pythonnet/gRPC/ZeroMQ.
- Python receives bounded snapshots/observations and returns proposals/results. Only C# validates permissions, session/world identity, tick/revision and applies accepted commands at safe boundaries. No Python module owns live GameObjects or writes live world memory. No synchronous AI inference or IPC wait in a simulation/render tick.
- pythonnet must have one explicit interpreter owner, serialized/GIL-safe calls, bounded queues and shutdown rules. Do not introduce a second interpreter owner or restore an embedded Python gameplay host. Trusted process execution is not a security sandbox.
- C++ is restricted to native plugins for genuinely performance-critical subsystems (rendering, physics and profiled animation/import/math kernels). Do not add a general-purpose C++ World, gameplay framework, editor service or networking service. Move high-level policy to C#; keep native code only where justified.
- Plugins use versioned C ABI, opaque resource handles, fixed-layout POD and bounded batch buffers. No STL, C++ exceptions, C# object references or Python objects cross the plugin ABI. Specify allocation/release, thread affinity and plugin shutdown; invalidate resources before unload.
- Keep graphics/platform headers behind native plugin modules. No D3D/Vulkan/OpenGL calls leak into managed scene/gameplay or module contracts. Eigen, GLFW, ImGui, spdlog, Box2D, Jolt and ufbx remain current native dependencies; do not blindly use them as managed runtime dependencies.
- Preserve World + GameObject + Component + independent Systems, flat object organization, composition and single-object deletion. No scene parent tree, transform inheritance, Godot Node aliases or Actor replication model. Bone/animation/UI internal hierarchies remain independent.
- Networking remains an optional standalone service with application-owned adapters, now C# by default. Do not put RPC, replication flags, network roles/ownership or connection lifecycle in GameObject/component/Behaviour. Neither network nor AI callbacks mutate a live World.
- Persistent object/assets use UUIDs; runtime handles are not serialized. Editor and Agent mutations share undoable commands and reversible transactions.

## Current implementation and migration safeguards

- A is the confirmed direction; AI must support scene, animation, UI, tools and managed extensions through bounded capabilities, not just code completion. All mutations go through the same editor command/transaction service, never direct Agent World access.
- managed/Ncma.Runtime is an independent headless C# foundation: empty flat objects, optional value components, explicit type/schema registry, managed snapshot v1, owner-thread access, fixed-step Systems. Ncma.Editor.Core -> Ncma.Scene -> Ncma.Runtime owns complete-document commands/history/capabilities. It has no native or Python dependency and now owns the editor and play scenes through hostfxr/ManagedSceneClient. The client holds an opaque token and copied DTOs only; never add native object/component storage.
- C++ SceneWorld and native world exports have been removed. Ncma.Managed is now a pure managed gameplay facade over Ncma.Runtime.World. The executable/ImGui shell remains C++ and submits UUID intents through Scene host v6. The native scene command stack and Runtime.EditSession were removed; Editor.Core owns the sole command history. Behaviour lifecycle/Exports/reload are bridged; Ncma.Gameplay.PlaySession now owns live WorldRunner scheduling, OnFixedUpdate, read-only OnUpdate and Pause/Resume/Step/Faulted. C# owns scene file IO. M1.3 input/interpolation/runtime commands/transactional signals/reload and M1.4 scoped live MCP are implemented. Managed Editor/Player entry, independent rendering/physics plugins and Python transports are NOT implemented; manual third-party client acceptance remains open.
- EditSession defaults read-only. Mutations require host-owned exact capability grants, session identity and expected revision; object deletion requires an explicitly approved persistent UUID. Undo/redo still check original permissions. Retry cache is bounded; outputs are structured, not model-generated executable text.
- Managed transactions/Undo restore whole snapshots and invalidate all runtime references. Resolve persistent UUIDs after edits. External World mutations invalidate an EditSession history; fixed-step staged writes abort on errors, but private System state/IO do not roll back. A faulted runner requires explicit recovery.
- The headless component dictionary/boxing/snapshot path is a correctness foundation, not a verified high-performance ECS. Closed-object/scalar/local-$defs schema checks are not a full JSON Schema validator. Extension registration is trusted C# startup code; no arbitrary code loading via capability input.
- Keep Build.bat and out/bin/NcmaEngine.exe working. Native ABI 2 intentionally removes world exports; animation/character APIs retain their own v1 contracts. Rebuild all old consumers; never forward old world handles to the new managed scene bridge. Preserve unrelated data; Python gameplay and C++ SceneWorld removal are authorized.
- Old .ncscene v1-v6 support is removed: no codec, legacy DTO, migration, backup/import fallback or language tags. Only .ncmascene SceneDocument JSON v1 assets are accepted. Preserve rejected input and the live document; never recreate old compatibility. Save all registered components and bindings through the C# bounded/atomic file service.
- Native plugin ABI is v2; gameplay host bridge is v5 (opaque uint64 managed scene token, not a native pointer). Scene host binary exchange is v6, bounded 4 MiB caller-owned buffers, explicit lengths and exception-to-error translation. Release after EndScene/Stop. Runtime handles are never serialized.
- Managed World Access batches (max 4096) remain. Only managed PlaySession/WorldRunner begins/commits/aborts fixed steps; old native phase operations 16/17/18 and unmanaged Tick were removed. Reads see committed state; duplicate batch targets fail and last submitted write wins across batches. This is not Python module scheduling.
- Managed worlds are owner-thread-only; the CLR bootstrap is process-lifetime and owner-thread-only. End the C# session before replacing/releasing a World. Abort rolls back staged signal consumption/sends, not private script state or external IO.
- See docs/ARCHITECTURE.md and docs/FRAMEWORK_REFACTOR.md for ownership, current gaps and migration order; docs/PYTHON_MODULES.md defines the proposed independent module boundary.

## Agent-facing changes

- Capabilities need stable names, concise descriptions, JSON input/output schemas, mutation-risk classification and deterministic structured results.
- Prefer read-only inspection then reversible transactions. Destructive tools require explicit authorization and narrow project-relative targets.
- Do not expose arbitrary project Python execution through Agent/MCP. Existing animation MCP controls isolated preview sessions, not the live editor.
- Keep generated/build output under out/, bin/, obj/ or other ignored directories.

## M1.1 scene document (implemented)

- Keep the C++/Dear ImGui shell for now. Ncma.Scene owns complete managed document snapshots v1, Behaviour metadata and document revisions over the single Runtime.World. The host facade borrows that World.
- Old SceneSnapshot/SceneObjectSnapshot DTOs and restore entry points are removed. Use SceneDocumentCodec for complete document JSON; do not restore SceneSnapshotCodec aliases or legacy converters. SceneDocumentSnapshot and internal WorldSnapshot are current snapshots, not legacy formats.
- Native Scene host bridge is now v6; opaque complete blobs drive isolated Play and C# history. SceneView is inspection-only and has no restore API. SceneDocumentFiles owns strict .ncmascene JSON v1 loading and same-directory atomic saves; old operations 4/14 are removed.
- World restore preparation blocks reentrant writes. Document snapshots enforce 4 MiB, owned data and owner-thread/safe-boundary access. Run NcmaSceneDocumentTests as part of Build.bat/CTest.
- Legacy Scene/Entity/component pools and their unused physics component/event wrappers were removed. Keep native solver/render/animation/import kernels, Editor.Core history only (the replacement passed A/B and final regression gates). See docs/M1_1_SCENE_DOCUMENT.md.

## M1.3 runtime (implemented)

- Ncma.Gameplay depends on Managed SDK/Scene/Runtime, never Editor.Core or native/Python. Host owns assembly metadata/bootstrap; PlaySession owns instances/lifecycle and the sole WorldRunner.
- OnFixedUpdate/Systems may stage attached component writes; OnUpdate and cleanup callbacks are read-only. SceneDocument metadata, signal sends/consumption and SDK disposal honor the same read-only guard.
- Default h=1/60, max 8 steps; strict mode preflight rejects overbudget without mutation, interactive mode caps accepted dt=0.25 and reports explicit dropped time. Pause/Resume clear debt; Step only works Paused and has no OnUpdate.
- Faults retain prior successful steps and abort the failed step's staged components/sends, not private fields/IO. No automatic resume; Stop/new Play is the default recovery. Signal receive rollback and bounded runtime structural commands are implemented.
- Gameplay bridge v5 uses a caller-owned 120-byte status POD and Play UUID/scene-token checks. Scene bridge v6 rejects external phase control and native direct writes to bound Play scenes.
- Input/interpolation, transactional signals, bounded runtime commands and safe reload preflight are implemented. M1.4 live scene MCP defaults off/read-only; trusted owner-thread UI approves exact scoped proposals for 60s. Never expose pairing/approval as Agent tools; queued/cache/history/install paths must recheck authority. Full M1 manual UI/client acceptance is pending; do not claim that it passed.
