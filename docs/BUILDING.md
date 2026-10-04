# Building NcmaEngine

The default Editor is now the C#/.NET apphost with native ImGui/rendering/physics/resource plugins.
Complete Build.bat regression stages, verifies and deploys its entire framework-dependent package
to out/bin, with a recoverable backup and maintenance journal in out/deployment.
SkipTests/SkipManaged/SkipPython never deploy. LaunchEditor.cmd refuses interrupted deployment;
use scripts/Recover-Editor.bat for explicit recovery, never silent runtime fallback.
Old C++ Editor and custom CLR/scene bridges are removed. Python transports remain unimplemented.
Manual UI/MCP, self-contained target environment and long-run acceptance remain pending under
the user's adjusted automatic-regression threshold. Older verification descriptions below are historical.
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

The default renderer is Direct3D 11. Open a project explicitly with:

```batch
LaunchEditor.cmd --editor --project "F:\NcmaEngine\out\bin\sample\sample.ncmaproject"
```

Renderer choice is in the project configuration. Non-Direct3D11 projects fail explicitly;
Vulkan drawing is not implemented. The retired native --renderer flags are not supported.

The executable is generated at `out/bin/NcmaEngine.exe`. `LaunchEditor.cmd` invokes `Build.bat` automatically when the executable is missing.

Native outputs are placed directly in the selected CMake build directory:

- Debug/Release editor: `out/bin/NcmaEngine.exe` (the latest configuration replaces the previous one)
- Debug native algorithm plugin: `out/build/windows-ninja-debug/NcmaNative.dll`
- Independent tooling/native smoke resources and gameplay assembly: `out/managed/`
- Installed editor managed symbols: `out/bin/NcmaEngine.pdb`
- Complete editor package: `out/bin/` (managed DLLs, `plugins/`, icon, licenses and sample)

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
3. Use the editor's Save/Open actions for `.ncmascene` files. Project association is explicit.
4. Play runs scripts on a scene copy. The reference cube previews the selected scripted object's
   world Transform; Pause/Resume controls ticking and Stop discards runtime changes.
5. For development, configure your project's gameplay assembly path to the reloadable
   `out/managed/Ncma.Gameplay.Sample.dll`. After editing its source, run
   `Build.bat -GameplayOnly -Configuration Debug`, then use Reload configured gameplay.
   GameplayOnly does not overwrite a deployed package's hash-checked sample DLL; the installed
   sample project needs a new full package build to receive source changes.
   Matching Export values survive rebind;
   private script state is reset. After adding/removing/changing Export members, use the undoable
   Update Export Schema action in edit mode to reconcile saved fields with new metadata.

The full build updates the entire package, so close the editor before running a full build.
GameplayOnly updates only the reloadable sample assembly in `out/managed`; it does not rebuild
the engine/API or run the full verification suite. The current full build runs 24 CTest registrations (8 native then 16 managed/combinations, including kernel image and deployment fixtures), including
animation runtime/ABI, FBX import/reference skinning, direct C# service and hidden editor tests,
including NcmaManagedHeadlessTests (independent C# World/Systems/transaction/permission cases)
and NcmaSceneDocumentTests (complete components/Behaviour snapshots and failure guards),
plus managed/native smoke tests and Python tooling/MCP/FBX/schema-v12 checks. Python gameplay
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

`python -m ncma_tools.cli inspect .` emits project-manifest schema v12.
Target and implemented ownership remain separate: the EXE is the C# apphost with a native ImGui presentation plugin, while World/components and gameplay
is actually C# only (`gameplay.csharp_only_runtime_enforced=true`). Python gameplay backend and
object language selection are removed; `python_modules` preserves specialized-module/plugin options,
with pythonnet/gRPC/ZeroMQ explicitly NOT implemented. Scene assets use only .ncmascene SceneDocument JSON v1,
including all registered components and script configuration. Old .ncscene v1-v6 compatibility and migration are removed;
unsupported input is rejected without rewriting it or mutating the live document. Native resource ABI is v2; World/GameObject exports and old Scene/Gameplay host bridges are removed. Current performance modules use versioned C ABIs. Rebuild old consumers; do not restore compatibility bridges.
The MCP protocol version and animation-state JSON schema are unchanged.

## Authoritative C# World and headless foundation

Ncma.Runtime and Ncma.Runtime.Tests are included in NcmaEngine.sln and the canonical Build.bat path.
The library now owns active editor and Play Worlds; no native object/component authority is retained.
Ncma.Runtime itself references no NcmaNative, graphics or Python dependency. TreatWarningsAsErrors is enabled.
C# SceneDocument JSON v1 (.ncmascene) is the only supported scene asset format, with complete component/binding persistence and atomic saves. The old .ncscene v1-v6 codec and migration paths have been removed. Ncma.Gameplay.PlaySession now owns the live editor WorldRunner and dispatches OnFixedUpdate; OnUpdate has read-only World access. Pause/Resume/Step and bounded strict/interactive timing are implemented. Input snapshots, render interpolation, runtime structural commands and reload preflight are implemented; real manual UI/MCP acceptance remains pending.
Ncma.Editor.Core owns the active ImGui scene commands and complete-document history. The native scene command stack was removed. Draft previews do not write World, and Play freezes the edit document. The shared v2 API has a default-off scoped local stdio/IPC server; see EDITOR_MCP.md. See [AI_DEVELOPMENT.md](AI_DEVELOPMENT.md).

The editor requires the complete checked package in out/bin, not an embedded host in out/managed.
Do not use Skip flags for a fresh editor build: they do not deploy a new entry.
The full Build.bat builds NcmaCore/NcmaNative/NcmaArchitectureTests first, runs both CTest subsets,
managed/native smoke, Python tests/inspect and audit, then stages and deploys the selected C# package
and verifies the formal path. Unrecognized user files or locked installation files stop deployment.

## Historical M1 migration records

The following M1 entry/host references describe earlier evidence, not current build wiring.

### M1.1 implemented document boundary

Complete Ncma.Scene document snapshots v1 cover all registered components and Behaviour/Export metadata. The retained C++/ImGui shell submits UUID commands to Editor.Core and uses opaque snapshots for Play; C# .ncmascene JSON v1 files persist complete documents with atomic saves. Old .ncscene compatibility is removed. M1.2 shared managed commands/history are implemented; M1.3 runtime/input/interpolation/commands/reload and M1.4 scoped live MCP are implemented; asset references/pipeline remain pending. See [M1.1 implementation](M1_1_SCENE_DOCUMENT.md).

### Historical M1.3-A/B verification

Build.bat now builds Ncma.Gameplay.Tests and includes NcmaGameplayTests in CTest.
The independent gameplay tests cover fixed-frame-rate invariance, pending writes, read-only
callbacks, lifecycle failures, owner-thread/reentrancy checks, pause/single-step, bounded
catch-up and time-counter overflow. The native host smoke verifies the 120-byte bridge v4
state, session rejection, removed external phase operations and no native active-Play writes.

OnFixedUpdate is the simulation mutation callback; move gameplay writes out of OnUpdate.
Faulted sessions require Stop followed by Play; component rollback does not undo private
script fields or external IO. This records the earlier A/B gate only. Current full runtime/MCP evidence is in M1_DELIVERY_REPORT.md.

### Historical M1 final verification

Build.bat -Configuration Debug and Build.bat -Configuration Release run the complete matrix (no Skip flags), including Gameplay.Tests, collectible catalog checks, real IPC/stdio tests and actual ImGui MCP smoke. All source projects are in NcmaEngine.sln; the canonical build initializes VS itself. Outputs remain out/bin/NcmaEngine.exe and out/managed/editor-mcp/. See [M1 report](M1_DELIVERY_REPORT.md) for actual results and outstanding manual acceptance.

Native Debug/Release link artifacts are isolated inside out/build/windows-ninja-<configuration>/{bin,symbols}; Build.bat always deploys the selected executable to out/bin/NcmaEngine.exe. This avoids cross-configuration timestamp reuse. The public executable path is unchanged. Optional -CleanNative cleans only generated Ninja outputs before the same complete verification. Chinese-only VS CL resource detection corrects the /showIncludes dependency prefix; installing another language pack is not required.

## M2.8 preflight (not final acceptance)

Full Build.bat (without SkipTests/SkipManaged/SkipPython) records a read-only source/consumer/package audit
under out/verification/m2-8/<configuration>/<uuid>. Full builds also run three rounds of the retained
Gameplay benchmark/pressure fixtures (8 warmups, 32 samples per case), writing profiles-<configuration>/.
Historical measurements are tracked JSON fixtures, so fresh builds do not require ignored M1 logs.
Frozen legacy/kernel/managed image comparisons and resource validation remain enabled.
These profiles compare historical runtime measurements, not simultaneous old/new entry performance.
audit_passed only means preflight succeeded;
h8_accepted/cleanup_authorized_by_this_report remain false; production_promoted reflects
the actual checked installed C# apphost, independently of manual acceptance.
There is no deletion/promotion action in this tool. The copied LastTest.log covers the final CTest invocation
only; use the complete canonical build log for both native and managed rounds.
For a standalone audit after the matching full build (PowerShell):

~~~powershell
$env:PYTHONPATH = 'F:\NcmaEngine\python\src'
python -m ncma_tools.m2_audit --root F:\NcmaEngine --configuration Release
~~~

Run Debug/Release sequentially: builds share the latest default deployment and out/managed.
Do not run them concurrently or use the other configuration's latest deployment as acceptance evidence.
See [M2.8 evidence and remaining gates](M2_8_DELIVERY_REPORT.md) and [cleanup preflight](M2_8_CLEANUP_AUDIT.md).
