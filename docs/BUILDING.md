# Building NcmaEngine

Target direction is now C# runtime/gameplay, independent Python modules and native performance
plugins. The commands below build the C++/ImGui shell and its authoritative C# World/hostfxr
gameplay path. C++ SceneWorld and native world exports are removed. They do NOT yet produce a fully managed
editor/Player or install pythonnet/gRPC/ZeroMQ.
See [FRAMEWORK_REFACTOR.md](FRAMEWORK_REFACTOR.md) for the staged migration.

## Recommended Windows command

Run this from an ordinary PowerShell or Command Prompt terminal at the repository root:

```batch
Build.bat
```

Release build:

```batch
Build.bat -Configuration Release
```

`Build.bat` works even when local PowerShell script execution is disabled. It applies an execution-policy override to this one build process only; it does not change the machine or user policy. The underlying script discovers Visual Studio 2022 automatically, imports the x64 compiler environment, uses the CMake and Ninja bundled with Visual Studio, then builds and verifies C++, C#, the native ABI, and Python tooling.

## Launching the editor

After a Debug build, run or double-click:

```batch
LaunchEditor.cmd
```

The default renderer is Direct3D 11. Renderer selection is explicit and forwarded by the launcher:

```batch
LaunchEditor.cmd --renderer=d3d11
LaunchEditor.cmd --renderer=vulkan
```

Until the Vulkan RHI target is available, requesting Vulkan reports whether the Windows Vulkan loader is installed and exits without silently falling back to D3D11.

The executable is generated at `out/bin/NcmaEngine.exe`. `LaunchEditor.cmd` invokes `Build.bat` automatically when the executable is missing.

Native outputs are placed directly in the selected CMake build directory:

- Debug/Release editor: `out/bin/NcmaEngine.exe` (the latest configuration replaces the previous one)
- Debug native algorithm plugin: `out/build/windows-ninja-debug/NcmaNative.dll`
- Embedded .NET host and reloadable gameplay assemblies: `out/managed/`
- Editor debug symbols: `out/symbols/NcmaEngine.pdb`

Requirements:

- Visual Studio 2022 with **Desktop development with C++**
- .NET 8 runtime and .NET 8 SDK or newer
- Python 3.10+ only for optional CLI/MCP/tool checks (`-SkipPython` omits them); no development headers, import library or interpreter DLL is required by native targets.

Third-party engine libraries are vendored under `engine/sdk`; builds do not download packages. The active core stack is Eigen 3.4.0, GLFW 3.5.0, Dear ImGui 1.91.9b, spdlog 1.15.3, Box2D 3.1.1, Jolt Physics 5.5.0, and ufbx 0.23.0. Python gameplay embedding and interpreter DLL deployment have been removed. CMake builds the engine dependencies from SDK sources and uses the dynamic MSVC runtime.

The `windows-msvc` CMake preset remains available for Visual Studio's native CMake integration. For command-line builds, use `Build.bat`; invoking the Ninja generator directly from a normal terminal does not initialize MSVC.

## C# scene authoring and reload

C# is the only gameplay language. Python gameplay SDK/host, creation language picker and reload
entries have been removed. Python remains optional for independent specialized modules and plugins;
see [PYTHON_MODULES.md](PYTHON_MODULES.md). No module transport or plugin loader is implemented yet.

1. Select or create an object via + GameObject, then Inspector > C# Behaviours > + Add Behaviour.
2. Choose `Ncma.Gameplay.Sample.RotatorBehaviour`. Speed, Clockwise and Multiplier are reflected
   from its public `[Export]` properties/field. Attach, remove, enable and property edits support Undo/Redo.
3. Save with Ctrl+S to `assets/scenes/EditorScene.ncmascene`. Ctrl+O reloads this scene.
4. Play runs scripts on a scene copy. The reference cube previews the selected scripted object's
   world Transform; Pause/Resume controls ticking and Stop discards runtime changes.
5. After editing the sample C# source, run `Build.bat -GameplayOnly -Configuration Debug`, then
   press Ctrl+Shift+R or use Gameplay > Reload C# Assembly. Matching Export values survive rebind;
   private script state is reset. After adding/removing/changing Export members, use the undoable
   Update Export Schema action in edit mode to reconcile saved fields with new metadata.

The full build updates native/host binaries, so close the editor before running a full build.
GameplayOnly updates only the reloadable sample assembly in `out/managed`; it does not rebuild
the engine/API or run the full verification suite. The full build runs nine CTest cases including
animation runtime/ABI, FBX import/reference skinning, C# host and hidden editor tests,
including NcmaManagedHeadlessTests (independent C# World/Systems/transaction/permission cases)
and NcmaSceneDocumentTests (complete components/Behaviour snapshots and failure guards),
plus managed/native smoke tests and Python tooling/MCP/FBX/schema-v7 checks. Python gameplay
removal is covered by rejecting language-tagged old scenes without modifying source
files or destination Worlds. Passing this suite does not imply independent module transports exist.

## Action animation and AI controls

Open Window > Action Animation Lab (or Project > Animation Graph > Open Action Animation Lab).
This is an isolated procedural skeleton preview, not yet imported skinned-mesh authoring.
See [ANIMATION.md](ANIMATION.md) and [ANIMATION_MCP.md](ANIMATION_MCP.md) for semantics and MCP client setup.
Close native MCP sessions before a full build so `out/managed/NcmaNative.dll` can be updated.

## FBX characters

Open Window > FBX Character Import, then Open FBX or Load Test Character.
The imported mesh is CPU-skinned in a dedicated wireframe preview with clip selection,
Play/Pause, Step and local Undo/Redo. It is not yet a GPU/PBR character or a scene component.
See [FBX_IMPORT.md](FBX_IMPORT.md) for import constraints and the read-only Python command.

## Project inspection manifest

`python -m ncma_tools.cli inspect .` emits project-manifest schema v8.
Target and implemented ownership remain separate: the EXE/ImGui shell remains native, while World/components and gameplay
is actually C# only (`gameplay.csharp_only_runtime_enforced=true`). Python gameplay backend and
object language selection are removed; `python_modules` preserves specialized-module/plugin options,
with pythonnet/gRPC/ZeroMQ explicitly NOT implemented. Scene assets use only .ncmascene SceneDocument JSON v1,
including all registered components and script configuration. Old .ncscene v1-v6 compatibility and migration are removed;
unsupported input is rejected without rewriting it or mutating the live document. Native plugin ABI is v2; World/GameObject exports are removed. Scene host v4 and Gameplay host v3 use opaque managed tokens. Rebuild old consumers.
The MCP protocol version and animation-state JSON schema are unchanged.

## Authoritative C# World and headless foundation

Ncma.Runtime and Ncma.Runtime.Tests are included in NcmaEngine.sln and the canonical Build.bat path.
The library now owns active editor and Play Worlds; no native object/component authority is retained.
Ncma.Runtime itself references no NcmaNative, graphics or Python dependency. TreatWarningsAsErrors is enabled.
C# SceneDocument JSON v1 (.ncmascene) is the only supported scene asset format, with complete component/binding persistence and atomic saves. The old .ncscene v1-v6 codec and migration paths have been removed. Behaviour lifecycle now uses this World; editor OnFixedUpdate is not scheduled.
Ncma.Editor.Core owns the active ImGui scene commands and complete-document history. The native scene command stack was removed. Draft previews do not write World, and Play freezes the edit document. The shared v2 capability API does not yet expose a live MCP/IPC server. See [AI_DEVELOPMENT.md](AI_DEVELOPMENT.md).

The editor now requires the deployed managed host, Ncma.Managed.dll, Ncma.Runtime.dll, Ncma.Scene.dll and Ncma.Editor.Core.dll in out/managed.
Do not use -SkipManaged for a fresh editor build: it only skips deployment and runs the native-only test subset.
The full Build.bat builds NcmaCore/NcmaNative/NcmaArchitectureTests first, deploys the C# host,
then runs all managed-dependent editor tests. Missing hosts fail with an actionable startup error.

## M1.1 implemented document boundary

Complete Ncma.Scene document snapshots v1 cover all registered components and Behaviour/Export metadata. The retained C++/ImGui shell submits UUID commands to Editor.Core and uses opaque snapshots for Play; C# .ncmascene JSON v1 files persist complete documents with atomic saves. Old .ncscene compatibility is removed. M1.2 shared managed commands/history are implemented; asset references/pipeline, fixed-step editor scheduling and live MCP remain pending. See [M1.1 implementation](M1_1_SCENE_DOCUMENT.md).
