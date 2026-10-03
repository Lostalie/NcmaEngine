# NcmaEngine collaboration guide

## Build and verify

- On Windows, use `Build.bat` as the canonical build and verification entry point. It initializes the Visual Studio toolchain itself and works when direct PowerShell script execution is disabled.
- Direct Ninja commands require the Visual Studio 2022 developer environment; do not instruct users to invoke the Ninja preset from an uninitialized shell.
- Build `NcmaCore`, `NcmaNative`, and `NcmaArchitectureTests` before touching legacy targets.
- Run CTest, build `managed/Ncma.Managed`, run the managed/native smoke test, and run `python -m ncma_tools.cli inspect .`.
- Treat warnings as defects in new code. Do not claim a graphics backend is implemented until it renders the reference scene and passes API validation.

## Architecture boundaries

- Keep platform and graphics API headers behind their modules. No D3D/Vulkan/OpenGL call may leak into scene, animation, UI, scripting, or gameplay code.
- C# and Python are equal gameplay-language choices; users must be able to author game logic in either language. C# design references ProwlEngine; Python design references Infernux. Python also owns tooling/AI.
- Keep simulation, rendering, physics, asset storage, and animation evaluation in C++. Both language frontends must use the same versioned native contracts and lifecycle semantics. Python gameplay now has an embedded CPython preview host, language-tagged scene bindings, scalar Exports and isolated play mode; fixed-update scheduling and standalone export remain unimplemented. Do not count legacy Python prototype files as current support.
- Embedded Python gameplay and native scene access are main-thread only. End both gameplay sessions before destroying or replacing their borrowed play world. Trusted project scripts are not sandboxed; they must not be exposed as arbitrary Agent/MCP execution tools.
- Cross-language calls use the versioned C ABI. Do not expose STL types or C++ exceptions across it.
- Persistent assets use UUIDs; runtime handles are not serialized.
- Editor mutations must be undoable commands. Agent mutations use the same command path.

## Agent-facing changes

- Capabilities require stable names, concise descriptions, JSON input/output schemas, mutation-risk classification, and deterministic structured results.
- Prefer read-only inspection, then reversible transactions. Destructive tools require explicit user authorization and narrow project-relative targets.
- Keep generated/build output under `out/`, `bin/`, `obj/`, or other ignored directories.
