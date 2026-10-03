# NcmaEngine collaboration guide

## Build and verify

- On Windows use Build.bat as the canonical build and verification entry point; it initializes the Visual Studio toolchain and works when direct PowerShell execution is disabled.
- Direct Ninja requires the VS2022 developer environment. Do not recommend it from an uninitialized shell.
- During migration build NcmaCore, NcmaNative and NcmaArchitectureTests before touching excluded legacy targets.
- Run CTest, build managed/Ncma.Managed, run managed/native smoke tests and python -m ncma_tools.cli inspect .; preserve compatibility regression tests.
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
- managed/Ncma.Runtime is an independent headless C# foundation: empty flat objects, optional value components, explicit type/schema registry, managed snapshot v1, owner-thread access, fixed-step Systems and EditSession transactions/history/capabilities. It has no native or Python dependency and is not attached to the current editor. Do not mirror both managed and native authority in one live session.
- Current executable/editor and active SceneWorld remain C++; Ncma.Managed is a native-world wrapper hosted by hostfxr. Managed Editor/Player hosting, managed Behaviour lifecycle on the new World, legacy .ncscene import, independent native rendering/physics plugins and Python module transports are NOT implemented.
- EditSession defaults read-only. Mutations require host-owned exact capability grants, session identity and expected revision; object deletion requires an explicitly approved persistent UUID. Undo/redo still check original permissions. Retry cache is bounded; outputs are structured, not model-generated executable text.
- Managed transactions/Undo restore whole snapshots and invalidate all runtime references. Resolve persistent UUIDs after edits. External World mutations invalidate an EditSession history; fixed-step staged writes abort on errors, but private System state/IO do not roll back. A faulted runner requires explicit recovery.
- The headless component dictionary/boxing/snapshot path is a correctness foundation, not a verified high-performance ECS. Closed-object/scalar/local-$defs schema checks are not a full JSON Schema validator. Extension registration is trusted C# startup code; no arbitrary code loading via capability input.
- Keep the existing Build.bat path, executable output out/bin/NcmaEngine.exe and compatibility ABIs working while replacing ownership incrementally. Preserve unrelated old code/data; Python gameplay source removal is explicitly authorized.
- .ncscene v5 retains its wire layout; language 0 is C# and all nonzero object/binding tags are rejected, even empty/disabled records. C# v1-v4 files still migrate. Loading never writes or silently drops/rewrites unsupported scripts; rejection preserves both destination World and source file. Python game logic requires explicit author conversion to C# or a separate module.
- Native base ABI v1 and World Access API v1 remain; GameObject semantic API is v4 (C# only). Compatibility language exports accept only 0. Old node-named exports are binary shims, not a source Node API.
- World Access batches (max 4096) and Begin -> C# Tick -> Commit/Abort remain. Reads see committed state; duplicate batch targets fail and last submitted write wins across batches. This is not Python module scheduling.
- Borrowed worlds are owner-thread-only. End the C# session before replacing/destroying a World. Abort does not undo private script state, external IO or consumed signals.
- See docs/ARCHITECTURE.md and docs/FRAMEWORK_REFACTOR.md for ownership, current gaps and migration order; docs/PYTHON_MODULES.md defines the proposed independent module boundary.

## Agent-facing changes

- Capabilities need stable names, concise descriptions, JSON input/output schemas, mutation-risk classification and deterministic structured results.
- Prefer read-only inspection then reversible transactions. Destructive tools require explicit authorization and narrow project-relative targets.
- Do not expose arbitrary project Python execution through Agent/MCP. Existing animation MCP controls isolated preview sessions, not the live editor.
- Keep generated/build output under out/, bin/, obj/ or other ignored directories.
