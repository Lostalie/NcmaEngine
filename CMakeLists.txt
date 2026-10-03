cmake_minimum_required(VERSION 3.25)

project(NcmaEngine VERSION 0.2.0 LANGUAGES C CXX)

if(WIN32)
    enable_language(RC)
endif()

option(NCMA_BUILD_TESTS "Build Ncma architecture tests" ON)
option(NCMA_BUILD_MANAGED "Expose the managed build target when dotnet is available" ON)
option(NCMA_BUILD_EDITOR "Build the NcmaEditor executable" ON)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Vendored dependencies. NcmaEngine never downloads build dependencies.
add_library(NcmaUfbx STATIC engine/sdk/ufbx/ufbx.c)
target_include_directories(NcmaUfbx SYSTEM PUBLIC engine/sdk/ufbx)
set_target_properties(NcmaUfbx PROPERTIES POSITION_INDEPENDENT_CODE ON)

add_library(NcmaEigen INTERFACE)
target_include_directories(NcmaEigen SYSTEM INTERFACE engine/sdk/eigen)
add_library(Ncma::Eigen ALIAS NcmaEigen)

set(GLFW_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_DOCS OFF CACHE BOOL "" FORCE)
set(GLFW_INSTALL OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_WIN32 ON CACHE BOOL "" FORCE)
set(USE_MSVC_RUNTIME_LIBRARY_DLL ON CACHE BOOL "" FORCE)
add_subdirectory(engine/sdk/glfw EXCLUDE_FROM_ALL)

set(SPDLOG_BUILD_EXAMPLE OFF CACHE BOOL "" FORCE)
set(SPDLOG_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(SPDLOG_BUILD_BENCH OFF CACHE BOOL "" FORCE)
set(SPDLOG_INSTALL OFF CACHE BOOL "" FORCE)
set(SPDLOG_USE_STD_FORMAT ON CACHE BOOL "" FORCE)
add_subdirectory(engine/sdk/spdlog EXCLUDE_FROM_ALL)

set(BOX2D_SAMPLES OFF CACHE BOOL "" FORCE)
set(BOX2D_BENCHMARKS OFF CACHE BOOL "" FORCE)
set(BOX2D_DOCS OFF CACHE BOOL "" FORCE)
set(BOX2D_UNIT_TESTS OFF CACHE BOOL "" FORCE)
set(BOX2D_VALIDATE OFF CACHE BOOL "" FORCE)
set(BOX2D_COMPILE_WARNING_AS_ERROR OFF CACHE BOOL "" FORCE)
add_subdirectory(engine/sdk/box2d EXCLUDE_FROM_ALL)

set(USE_ASSERTS OFF CACHE BOOL "" FORCE)
set(OVERRIDE_CXX_FLAGS OFF CACHE BOOL "" FORCE)
set(INTERPROCEDURAL_OPTIMIZATION OFF CACHE BOOL "" FORCE)
set(FLOATING_POINT_EXCEPTIONS_ENABLED OFF CACHE BOOL "" FORCE)
set(ENABLE_ALL_WARNINGS OFF CACHE BOOL "" FORCE)
set(DEBUG_RENDERER_IN_DEBUG_AND_RELEASE OFF CACHE BOOL "" FORCE)
set(PROFILER_IN_DEBUG_AND_RELEASE OFF CACHE BOOL "" FORCE)
set(ENABLE_INSTALL OFF CACHE BOOL "" FORCE)
set(USE_STATIC_MSVC_RUNTIME_LIBRARY OFF CACHE BOOL "" FORCE)
add_subdirectory(engine/sdk/JoltPhysics/Build EXCLUDE_FROM_ALL)

add_library(NcmaCore STATIC
    engine/source/runtime/assets/FbxCharacterImporter.cpp
    engine/source/runtime/ai/AgentCapabilityRegistry.cpp
    engine/source/runtime/animation/AnimationGraph.cpp
    engine/source/runtime/animation/AnimationRuntime.cpp
    engine/source/runtime/animation/ActionAnimationWorkspace.cpp
    engine/source/runtime/renderer/rhi/RenderBackendRegistry.cpp
    engine/source/runtime/renderer/rhi/RhiResources.cpp
    engine/source/runtime/renderer/rendergraph/RenderGraph.cpp
    engine/source/runtime/renderer/rhi/vulkan/VulkanRuntimeProbe.cpp
    engine/source/runtime/scene/SceneCommandStack.cpp
    engine/source/runtime/scene/SceneSerializer.cpp
    engine/source/runtime/scene/SceneUuid.cpp
    engine/source/runtime/scene/SceneWorld.cpp
    engine/source/runtime/script/runtime/ScriptRuntimeRegistry.cpp
    engine/source/runtime/core/log/Log.cpp
    engine/source/runtime/physics/runtime/PhysicsWorld2D.cpp
    engine/source/runtime/physics/runtime/PhysicsWorld3D.cpp
)

if(WIN32)
    target_sources(NcmaCore PRIVATE
        engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp
        engine/source/runtime/script/runtime/DotNetGameplayRuntime.cpp
    )
    target_link_libraries(NcmaCore PUBLIC d3d11 dxgi d3dcompiler advapi32)
endif()


target_include_directories(NcmaCore PUBLIC engine/source/runtime)
target_link_libraries(NcmaCore PUBLIC Ncma::Eigen spdlog::spdlog box2d Jolt PRIVATE NcmaUfbx)
target_compile_features(NcmaCore PUBLIC cxx_std_20)
set_target_properties(NcmaCore PROPERTIES POSITION_INDEPENDENT_CODE ON)

if(MSVC)
    target_compile_options(NcmaCore PRIVATE /W4 /permissive- /utf-8)
else()
    target_compile_options(NcmaCore PRIVATE -Wall -Wextra -Wpedantic)
endif()

add_library(NcmaNative SHARED
    engine/source/runtime/interop/NcmaNativeApi.cpp
    engine/source/runtime/interop/NcmaAnimationApi.cpp
    engine/source/runtime/interop/NcmaCharacterApi.cpp
)
target_link_libraries(NcmaNative PRIVATE NcmaCore)
target_include_directories(NcmaNative PUBLIC engine/source/runtime)
target_compile_definitions(NcmaNative PRIVATE NCMA_NATIVE_EXPORTS)
if(MSVC)
    target_compile_options(NcmaNative PRIVATE /W4 /permissive- /utf-8)
endif()

if(NCMA_BUILD_MANAGED)
    find_program(DOTNET_EXECUTABLE dotnet)
    if(DOTNET_EXECUTABLE)
        add_custom_target(NcmaManaged
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Runtime.Tests/Ncma.Runtime.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release>
                    --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Managed/Ncma.Managed.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release>
                    --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Managed.Host/Ncma.Managed.Host.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release>
                    --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Gameplay.Sample/Ncma.Gameplay.Sample.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release>
                    --nologo
            WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
            COMMENT "Building the C# runtime foundation, gameplay API, host, and sample"
            VERBATIM
        )
    endif()
endif()

if(NCMA_BUILD_TESTS)
    enable_testing()
    add_executable(NcmaArchitectureTests tests/ArchitectureTests.cpp tests/SceneFlatTests.cpp tests/WorldAccessTests.cpp)
    target_link_libraries(NcmaArchitectureTests PRIVATE NcmaCore NcmaNative)
    # Tests contain assertions with operations: keep these active in Release too.
    if(MSVC)
        target_compile_options(NcmaArchitectureTests PRIVATE /UNDEBUG)
    else()
        target_compile_options(NcmaArchitectureTests PRIVATE -UNDEBUG)
    endif()
    add_test(NAME NcmaArchitectureTests COMMAND NcmaArchitectureTests)
    set_tests_properties(NcmaArchitectureTests PROPERTIES TIMEOUT 30)
    if(NCMA_BUILD_MANAGED AND DOTNET_EXECUTABLE)
        add_test(NAME NcmaManagedHeadlessTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Runtime.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Runtime.Tests.dll")
        set_tests_properties(NcmaManagedHeadlessTests PROPERTIES TIMEOUT 30)
    endif()
    add_executable(NcmaAnimationTests tests/AnimationTests.cpp)
    target_link_libraries(NcmaAnimationTests PRIVATE NcmaCore NcmaNative)
    if(MSVC)
        target_compile_options(NcmaAnimationTests PRIVATE /W4 /permissive- /utf-8)
    endif()
    add_test(NAME NcmaAnimationTests COMMAND NcmaAnimationTests)
    add_executable(NcmaFbxImportTests tests/FbxImportTests.cpp)
    target_link_libraries(NcmaFbxImportTests PRIVATE NcmaCore NcmaNative NcmaUfbx)
    if(MSVC)
        target_compile_options(NcmaFbxImportTests PRIVATE /W4 /permissive- /utf-8)
    endif()
    add_test(NAME NcmaFbxImportTests COMMAND NcmaFbxImportTests
        "${CMAKE_SOURCE_DIR}/tests/assets/fbx" "${CMAKE_SOURCE_DIR}/out/tests/fbx")
    if(WIN32)
        add_executable(NcmaManagedHostTests tests/ManagedHostTests.cpp)
        target_link_libraries(NcmaManagedHostTests PRIVATE NcmaCore)
    endif()
endif()

if(WIN32 AND NCMA_BUILD_EDITOR)
    add_library(NcmaImGui STATIC
        engine/sdk/imgui/imgui.cpp
        engine/sdk/imgui/imgui_draw.cpp
        engine/sdk/imgui/imgui_tables.cpp
        engine/sdk/imgui/imgui_widgets.cpp
        engine/sdk/imgui/backends/imgui_impl_glfw.cpp
        engine/sdk/imgui/backends/imgui_impl_dx11.cpp
    )
    target_include_directories(NcmaImGui PUBLIC
        engine/sdk/imgui
        engine/sdk/imgui/backends
    )
    target_compile_definitions(NcmaImGui PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_link_libraries(NcmaImGui PUBLIC glfw d3d11 dxgi d3dcompiler)

    add_executable(NcmaEditor WIN32
        engine/source/editor/EditorMain.cpp
        engine/source/editor/EditorApplication.cpp
        engine/source/editor/EditorAnimation.cpp
        engine/source/editor/EditorFbxCharacter.cpp
        engine/source/editor/EditorApplication.h
        engine/source/editor/NcmaEditor.rc
    )
    target_include_directories(NcmaEditor PRIVATE
        engine/source/editor
        engine/sdk/imgui
        engine/sdk/imgui/backends
    )
    target_compile_definitions(NcmaEditor PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_link_libraries(NcmaEditor PRIVATE NcmaCore NcmaImGui glfw user32 gdi32 shell32 comdlg32)
    add_dependencies(NcmaEditor NcmaNative)
    set_target_properties(NcmaEditor PROPERTIES
        OUTPUT_NAME "NcmaEngine"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/out/bin"
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_SOURCE_DIR}/out/bin"
        RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_SOURCE_DIR}/out/bin"
        PDB_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/out/symbols"
        PDB_OUTPUT_DIRECTORY_DEBUG "${CMAKE_SOURCE_DIR}/out/symbols"
        PDB_OUTPUT_DIRECTORY_RELEASE "${CMAKE_SOURCE_DIR}/out/symbols"
    )
    if(MSVC)
        target_compile_options(NcmaEditor PRIVATE /W4 /permissive- /utf-8)
        target_link_options(NcmaEditor PRIVATE /INCREMENTAL:NO)
        set_property(TARGET NcmaEditor PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
    endif()
    if(NCMA_BUILD_TESTS)
        add_test(NAME NcmaEditorSmokeTest COMMAND NcmaEditor --smoke-test)
        add_test(NAME NcmaEditorFbxSmokeTest COMMAND NcmaEditor --fbx-smoke-test)
        set_tests_properties(NcmaEditorFbxSmokeTest PROPERTIES TIMEOUT 30)
        set_tests_properties(NcmaEditorSmokeTest PROPERTIES TIMEOUT 20
            FAIL_REGULAR_EXPRESSION "Render pass .* failed")
        add_test(
            NAME NcmaManagedHostSmokeTest
            COMMAND NcmaManagedHostTests
                "${CMAKE_SOURCE_DIR}/out/managed/Ncma.Managed.Host.runtimeconfig.json"
                "${CMAKE_SOURCE_DIR}/out/managed/Ncma.Managed.Host.dll"
                "${CMAKE_SOURCE_DIR}/out/managed/Ncma.Gameplay.Sample.dll")
        set_tests_properties(NcmaManagedHostSmokeTest PROPERTIES TIMEOUT 20)
        add_test(NAME NcmaEditorGameplaySmokeTest COMMAND NcmaEditor --gameplay-smoke-test)
        set_tests_properties(NcmaEditorGameplaySmokeTest PROPERTIES TIMEOUT 30
            FAIL_REGULAR_EXPRESSION "Render pass .* failed")
    endif()
endif()
