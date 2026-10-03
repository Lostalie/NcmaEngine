# NcmaEngine target architecture

NcmaEngine is migrating from a single Visual Studio/OpenGL prototype to a layered engine in which dependencies point inward:

```text
Editor / Game / Agent adapters
        |
C# gameplay API ---- Python gameplay API
        |                   |
        +------ stable C ABI+
                    |
         C++20 engine core
  Scene | Animation | Assets | Physics
                    |
          Render Hardware Interface
             /             \
       Direct3D 11       Vulkan
```

## Language ownership

- C++20 owns memory, threading, scene storage, asset import/runtime data, animation evaluation, physics, rendering, and the stable native ABI.
- C#/.NET 8 and Python are equal primary gameplay-language choices. A user may implement
  game logic entirely in either language; neither frontend replaces the C++ runtime.
  C# API/lifecycle design references ProwlEngine; Python gameplay/tool design references Infernux.
  These are design references, not engine dependencies or already-completed compatibility layers.
- C# gameplay is currently integrated. Game behavior derives from `Ncma.Behaviour`;
  exported fields become inspector properties. The C++ runtime discovers and embeds `hostfxr`
  without an SDK-path build dependency. `Ncma.Managed.Host` remains in the host context while
  gameplay DLLs load from streams into collectible `AssemblyLoadContext` instances, enabling
  rebuild and reload without locking the gameplay assembly.
- Python is also a primary gameplay language, alongside its tools/AI roles. `NcmaPythonGameplay`
  is a separate CPython embedding adapter; its public headers contain no Python API types.
  `ncma_gameplay` supplies Behaviour, scalar Exports and borrowed Node operations over scene C ABI v1.
  Python gameplay hosting and scene attachment are implemented for editor preview; standalone
  distribution, dependency environments and a complete game SDK are NOT implemented.
  The external `ncma_tools` CLI/MCP process remains separate from embedded gameplay.
- Both gameplay frontends must share node/component identity, native API operations, lifecycle
  order, Export metadata, play-scene isolation, and fixed-update semantics. Heavy rendering,
  physics and animation evaluation stay in C++; Python gameplay is not prohibited, but neither
  language is automatically deterministic or sandboxed. Agent permissions remain separate from
  trusted project-script execution.

## Dual-language gameplay status and next integration

| Capability | C# | Python |
|---|---|---|
| Gameplay host and per-frame lifecycle | Implemented | Implemented (editor preview) |
| Node attachment and scalar Inspector Exports | Implemented | Implemented |
| Play-scene isolation and manual hot reload | Implemented | Implemented |
| Fixed-update scheduler | NOT implemented | NOT implemented |
| Standalone game distribution | NOT implemented | NOT implemented |

Language selection per script attachment is implemented in Inspector: types are labelled C# or Python.
A project-wide default-language setting is NOT implemented. The first integration criterion is met:
the same rotation example can run in either frontend against the native scene API. CTest also checks
both hosts updating different nodes in one scene. Current dispatch groups C# before Python; this is
not a general per-component priority scheduler or a cross-language object-reference system.

Attach/remove, enable and scalar property edits use the existing scene undo path. Play executes a
disposable scene copy. Python errors pause playback; failed reload stops play and preserves authored
data. Reload discards private script state and uses saved Exports, not runtime-mutated values.
Remaining work: fixed-update scheduling, project environments/dependency management, imports between
project script modules, more Export types, gameplay services and standalone game packaging.
See [PYTHON_GAMEPLAY.md](PYTHON_GAMEPLAY.md) for the exact supported Python subset and interpreter lifetime.

Python tools/MCP remain a separate role and may be used with either gameplay language.

## Foundation and platform dependencies

- Eigen is the engine math foundation; public transform types are Eigen vectors, quaternions, and matrices.
- GLFW owns window creation, input/event polling, and native-window access for RHI surface creation.
- Dear ImGui owns the immediate-mode editor shell and uses its GLFW platform backend.
- spdlog owns engine and client logging lifecycle.
- Box2D owns 2D simulation through `PhysicsWorld2D`.
- Jolt Physics owns 3D simulation through `PhysicsWorld3D`; global allocator/type registration is reference-counted and happens before any Jolt-backed member allocation.

## Scene semantics

`SceneWorld` deliberately combines two useful models:

- Godot semantics: every object is a named node with hierarchy, local transform, ownership, and lifecycle.
- Unity semantics: nodes gain data and behavior through composable components instead of deep inheritance.

Persistent scene nodes use stable UUIDs while numeric `NodeId` values remain runtime-only handles.
The versioned `.ncscene` v3 format stores hierarchy, names, local transforms, and C#/Python Behaviour
attachments (component UUID, type name, language tag, enable flag, typed Export values).
v1 and v2 remain readable; v2 attachments migrate to C# and all new saves use v3.
The editor resolves selection through UUIDs after load/undo. General component metadata,
prefab/packed-scene inheritance, node enable state, tags/layers, and fixed-update scheduling remain future work.

Gameplay bridge ABI v2 exchanges only C function pointers, opaque world handles, integers,
UTF-8 buffers and fixed-layout metadata. It checks its version before loading gameplay; scene
calls continue through NcmaNative ABI v1. Types are reflected at load, but instances are created
only for attached nodes in a disposable play-scene copy. Public writable fields/properties
marked `[Export]` support `float`, `double`, `int`, and `bool`. Constructors must not access Node;
Node is assigned before OnCreate. Properties are applied before OnCreate/OnEnable. Disabled
instances receive OnCreate/OnDestroy but not OnEnable/OnUpdate/OnDisable. Stop calls shutdown
callbacks before disposing the borrowed managed world wrapper. Edits are disabled during play;
the authored scene and its undo history remain intact. Reload creates new instances with the
stored Export values; arbitrary private C# state is not migrated. The current host supports
one gameplay session per process and main-thread lifecycle calls.

## Render architecture

Only the RHI may mention Direct3D 11 or Vulkan. Higher layers submit API-neutral render graphs, resources, pipelines, descriptors, and command lists. HLSL is the canonical shader language; DXC produces DXBC/DXIL as appropriate for D3D11 and SPIR-V for Vulkan. Reflection generates one binding layout shared by both backends.

Renderer selection is explicit at startup (`--renderer=d3d11` or `--renderer=vulkan`). The editor communicates only through `IRenderBackend`; native D3D11 objects remain inside its backend. The shared RHI now defines API-neutral buffer, texture, and sampler descriptions, opaque handles, validation, and explicit lifetime operations. Texture usage is a composable flag set so resources such as shadow maps can be both depth attachments and shader inputs. The editor smoke test creates and destroys a real D3D11 vertex buffer, HDR render target, four-layer sampled depth texture, and PCF comparison sampler so CI exercises this boundary.

Graphics pipeline descriptions contain API-neutral vertex layouts, topology, raster, depth, and blend state plus shader entry points. The current D3D11 backend compiles HLSL shader model 5 source, creates shaders and input layouts, owns the fixed-function states, and executes validated indexed or non-indexed draw commands.

The draw contract now supports 16-bit and 32-bit index buffers plus vertex/pixel constant-buffer bindings. CPU-to-GPU buffers use validated whole-buffer discard updates without exposing D3D11 mapping. The swap chain owns a resize-aware D32 depth target. The editor reference preview uses Eigen to build a model-view-projection matrix and renders a rotating indexed cube with depth testing.

The Vulkan path currently includes backend registration plus loader/runtime capability probing. It intentionally reports an actionable failure until a Vulkan SDK is supplied and its device, swap-chain, and resource implementation is complete; Vulkan rendering is not claimed yet.

The initial physically based renderer is deferred/cluster-ready and uses metallic-roughness materials, image-based lighting, HDR linear lighting, ACES-style tone mapping, and GPU-driven-friendly draw packets. Directional lights use cascaded shadow maps; PCF is the baseline soft-shadow filter and PCSS is an optional quality tier.

## Editor architecture

The editor is a separate executable linked against engine/editor modules, not gameplay code. All edits are commands with `Do/Undo`, and the same command boundary is exposed to automation. Major panels are hierarchy, inspector, asset browser, viewport, console, animation graph, and UI canvas.

The current editor shell uses a fixed workspace because the vendored Dear ImGui 1.91.9b build does not expose docking APIs. Panel implementations remain independent; upgrading the dependency to the docking branch will enable draggable docking without changing the scene/editor service boundaries.

Animation graphs and UI documents remain data models independent of their future visual editors.
Typed-node compilation, a full graph editor and a retained UI canvas/layout solver are planned,
not implemented by the current action laboratory. The laboratory evaluates clips through the
separate animation runtime and exposes state debugging, not arbitrary graph authoring.

## Agent-ready contract

The action animation runtime now evaluates validated immutable skeleton/clip assets, cached-pose
crossfades, masked pose blends, root motion, notifies and skin matrices without renderer dependencies.
The editor's action laboratory and C# / MCP preview sessions share `ActionAnimationWorkspace` commands
and undo history semantics; they are separate instances, not shared live-editor state. Animation ABI v1
is an independent NcmaNative extension. See [ANIMATION.md](ANIMATION.md) for implementation boundaries.

Agent integration is capability-based rather than provider-based. Every tool has a stable name, description, JSON input/output schemas, and a mutation risk (`read_only`, `reversible`, or `destructive`). Mutating tools execute through the editor command/undo system and require explicit project-scoped permissions. This makes local models, cloud models, IDE agents, and scripted automation interchangeable.
