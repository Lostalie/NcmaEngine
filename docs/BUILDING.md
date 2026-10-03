# Building NcmaEngine

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
- Debug C# native bridge: `out/build/windows-ninja-debug/NcmaNative.dll`
- Embedded .NET host and reloadable gameplay assemblies: `out/managed/`
- Editor debug symbols: `out/symbols/NcmaEngine.pdb`

Requirements:

- Visual Studio 2022 with **Desktop development with C++**
- .NET 8 runtime and .NET 8 SDK or newer
- A matching x64 CPython 3.10+ installation with development headers, import library, runtime DLL and standard library

Third-party engine libraries are vendored under `engine/sdk`; builds do not download packages. The active core stack is Eigen 3.4.0, GLFW 3.5.0, Dear ImGui 1.91.9b, spdlog 1.15.3, Box2D 3.1.1, Jolt Physics 5.5.0, and ufbx 0.23.0. CPython is discovered from the installed SDK, not vendored. Its runtime DLL is copied under `out/`; the configured installation still supplies its standard library, so this is not standalone game packaging. CMake builds the engine dependencies from SDK sources and uses the dynamic MSVC runtime.

The `windows-msvc` CMake preset remains available for Visual Studio's native CMake integration. For command-line builds, use `Build.bat`; invoking the Ninja generator directly from a normal terminal does not initialize MSVC.

## C# scene authoring and reload

C# and Python are equal gameplay-language choices. Both now support editor-preview node scripts.
Choose the language per Behaviour attachment; project-wide default-language settings remain unimplemented.
The following section describes C#. See [PYTHON_GAMEPLAY.md](PYTHON_GAMEPLAY.md) for Python.

1. Select a node (for example Character), then use Inspector > C# / Python Behaviours > + Add Behaviour.
2. Choose `Ncma.Gameplay.Sample.RotatorBehaviour`. Speed, Clockwise and Multiplier are reflected
   from its public `[Export]` properties/field. Attach, remove, enable and property edits support Undo/Redo.
3. Save with Ctrl+S to `assets/scenes/EditorScene.ncscene`. Ctrl+O reloads this scene.
4. Play runs scripts on a scene copy. The reference cube previews the selected scripted node's
   world Transform; Pause/Resume controls ticking and Stop discards runtime changes.
5. After editing the sample C# source, run `Build.bat -GameplayOnly -Configuration Debug`, then
   press Ctrl+Shift+R or use Gameplay > Reload C# Assembly. Matching Export values survive rebind;
   private script state is reset. After adding/removing/changing Export members, use the undoable
   Update Export Schema action in edit mode to reconcile saved fields with new metadata.

The full build updates native/host binaries, so close the editor before running a full build.
GameplayOnly updates only the reloadable sample assembly in `out/managed`; it does not rebuild
the engine/API or run the full verification suite. The full build runs nine CTest cases including
animation runtime/ABI, FBX import/reference skinning, embedded host and hidden editor tests,
plus managed/native smoke tests and the Python checks (ten animation/MCP tests with a real
stdio server subprocess, three FBX tooling tests, three project-manifest tests and eight Python
gameplay tests). Native coverage includes embedded Python, editor Python play mode and mixed-language scenes.

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

`python -m ncma_tools.cli inspect .` now emits project-manifest schema v2.
`languages.gameplay` changed from a single string to an array of the two target language choices;
consumers of schema v1 must adapt to this field type. The separate `gameplay.backends` entries
report actual integration status: C# is integrated, Python gameplay is an integrated editor-preview slice.
The MCP protocol version and animation-state JSON schema are unchanged.
