cmake_minimum_required(VERSION 3.25)

project(NcmaEngine VERSION 0.2.0 LANGUAGES C CXX)

# VS installations with only the Chinese CL resources ignore VSLANG=1033. Some
# CMake probes decode that localized prefix incorrectly; Ninja then loses header deps.
# Match the verified resource fallback, not the user console language.
string(STRIP "$ENV{VSLANG}" _ncma_cl_language)
if(MSVC AND _ncma_cl_language STREQUAL "1033")
    get_filename_component(_ncma_cl_directory "${CMAKE_CXX_COMPILER}" DIRECTORY)
    if(EXISTS "${_ncma_cl_directory}/2052/clui.dll" AND NOT EXISTS "${_ncma_cl_directory}/1033/clui.dll")
        set(CMAKE_CL_SHOWINCLUDES_PREFIX "注意: 包含文件:  ")
    endif()
endif()

if(WIN32)
    enable_language(RC)
endif()

option(NCMA_BUILD_TESTS "Build Ncma architecture tests" ON)
option(NCMA_BUILD_MANAGED "Expose the managed build target when dotnet is available" ON)

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
set(GLFW_LIBRARY_TYPE SHARED CACHE STRING "" FORCE)
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

# Single numerical physics owner, shared by the transitional C++ callers and new C ABI.
add_library(NcmaPhysics SHARED
    engine/source/plugins/physics/PhysicsPlugin.cpp
    engine/source/runtime/physics/runtime/PhysicsWorld2D.cpp
    engine/source/runtime/physics/runtime/PhysicsWorld3D.cpp)
target_include_directories(NcmaPhysics PRIVATE engine/source/plugins engine/source/runtime)
target_compile_definitions(NcmaPhysics PRIVATE NCMA_PHYSICS_EXPORTS)
target_link_libraries(NcmaPhysics PRIVATE Ncma::Eigen box2d Jolt)
if(MSVC)
    target_compile_options(NcmaPhysics PRIVATE /W4 /WX /permissive- /utf-8)
else()
    target_compile_options(NcmaPhysics PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
add_custom_command(TARGET NcmaPhysics POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${CMAKE_BINARY_DIR}/m2/plugins"
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different $<TARGET_FILE:NcmaPhysics> "${CMAKE_BINARY_DIR}/m2/plugins/"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${CMAKE_BINARY_DIR}/bin"
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different $<TARGET_FILE:NcmaPhysics> "${CMAKE_BINARY_DIR}/bin/")

add_library(NcmaCore STATIC
    engine/source/runtime/assets/FbxCharacterImporter.cpp
    engine/source/runtime/ai/AgentCapabilityRegistry.cpp
    engine/source/runtime/animation/AnimationGraph.cpp
    engine/source/runtime/animation/AnimationRuntime.cpp
    engine/source/runtime/animation/ActionDemoLibrary.cpp
    engine/source/runtime/animation/ActionAnimationWorkspace.cpp
    engine/source/runtime/renderer/rhi/RenderBackendRegistry.cpp
    engine/source/runtime/renderer/rhi/RhiResources.cpp
    engine/source/runtime/renderer/rendergraph/RenderGraph.cpp
    engine/source/runtime/renderer/rhi/vulkan/VulkanRuntimeProbe.cpp
    engine/source/runtime/scene/SceneUuid.cpp
    engine/source/runtime/script/runtime/ScriptRuntimeRegistry.cpp
    engine/source/runtime/core/log/Log.cpp
)

if(WIN32)
    target_sources(NcmaCore PRIVATE
        engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp
    )
    target_link_libraries(NcmaCore PUBLIC d3d11 dxgi d3dcompiler advapi32)
endif()


target_include_directories(NcmaCore PUBLIC engine/source/runtime)
target_link_libraries(NcmaCore PUBLIC Ncma::Eigen spdlog::spdlog NcmaPhysics PRIVATE NcmaUfbx)
target_compile_features(NcmaCore PUBLIC cxx_std_20)
set_target_properties(NcmaCore PROPERTIES POSITION_INDEPENDENT_CODE ON)

if(MSVC)
    target_compile_options(NcmaCore PRIVATE /W4 /permissive- /utf-8)
else()
    target_compile_options(NcmaCore PRIVATE -Wall -Wextra -Wpedantic)
endif()

add_library(NcmaNative SHARED
    engine/source/runtime/core/log/NativeDiagnostics.cpp
    engine/source/runtime/interop/NcmaNativeApi.cpp
    engine/source/runtime/interop/NcmaAnimationApi.cpp
    engine/source/runtime/interop/NcmaCharacterApi.cpp
)

# Independent numerical import context: no NcmaCore, physics, renderer, GUI or global Character ABI registry.
add_library(NcmaAnimationKernel SHARED engine/source/plugins/animation/PoseKernel.cpp)
target_include_directories(NcmaAnimationKernel PRIVATE engine/source/plugins)
target_compile_definitions(NcmaAnimationKernel PRIVATE NCMA_POSE_EXPORTS)
target_link_libraries(NcmaAnimationKernel PRIVATE Ncma::Eigen)
if(MSVC)
    target_compile_options(NcmaAnimationKernel PRIVATE /W4 /WX /permissive- /utf-8)
else()
    target_compile_options(NcmaAnimationKernel PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

add_library(NcmaImportKernel SHARED
    engine/source/plugins/import/ImportKernel.cpp
    engine/source/plugins/import/ImageDecode.cpp
    engine/source/runtime/assets/FbxCharacterImporter.cpp
    engine/source/runtime/animation/AnimationRuntime.cpp
    engine/source/runtime/scene/SceneUuid.cpp)
target_include_directories(NcmaImportKernel PRIVATE engine/source/plugins engine/source/runtime)
target_compile_definitions(NcmaImportKernel PRIVATE NCMA_IMPORT_EXPORTS)
target_link_libraries(NcmaImportKernel PRIVATE Ncma::Eigen NcmaUfbx)
if(WIN32)
    target_compile_definitions(NcmaImportKernel PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_link_libraries(NcmaImportKernel PRIVATE windowscodecs ole32)
endif()
if(MSVC)
    target_compile_options(NcmaImportKernel PRIVATE /W4 /WX /permissive- /utf-8)
else()
    target_compile_options(NcmaImportKernel PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
target_link_libraries(NcmaNative PRIVATE NcmaCore)
target_include_directories(NcmaNative PUBLIC engine/source/runtime)
target_compile_definitions(NcmaNative PRIVATE NCMA_NATIVE_EXPORTS)
if(MSVC)
    target_compile_options(NcmaNative PRIVATE /W4 /permissive- /utf-8)
endif()

if(WIN32)
    add_library(NcmaPlatform SHARED engine/source/plugins/platform/PlatformPlugin.cpp)
    target_include_directories(NcmaPlatform PRIVATE engine/source/plugins)
    target_compile_definitions(NcmaPlatform PRIVATE NCMA_PLATFORM_EXPORTS NOMINMAX WIN32_LEAN_AND_MEAN)
    target_link_libraries(NcmaPlatform PRIVATE glfw user32)
    target_compile_options(NcmaPlatform PRIVATE /W4 /WX /permissive- /utf-8)
    set_target_properties(NcmaPlatform PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/plugins")

    add_library(NcmaRenderer SHARED
        engine/source/plugins/renderer/RendererPlugin.cpp
        engine/source/plugins/renderer/StaticMeshKernel.cpp
        engine/source/plugins/renderer/ResourceKernel.cpp
        engine/source/plugins/renderer/ScenePipelineKernel.cpp
        engine/source/plugins/renderer/SkinKernel.cpp
        engine/source/plugins/renderer/ReferenceKernel.cpp
        engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp
        engine/source/runtime/renderer/rhi/RhiResources.cpp)
    target_include_directories(NcmaRenderer PRIVATE engine/source/plugins engine/source/runtime)
    target_compile_definitions(NcmaRenderer PRIVATE NCMA_RENDERER_EXPORTS NOMINMAX WIN32_LEAN_AND_MEAN GLFW_INCLUDE_NONE)
    target_link_libraries(NcmaRenderer PRIVATE NcmaPlatform glfw Ncma::Eigen d3d11 dxgi d3dcompiler)
    target_compile_options(NcmaRenderer PRIVATE /W4 /WX /permissive- /utf-8)
    set_target_properties(NcmaRenderer PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/plugins")

    add_library(NcmaGuiCore STATIC
        engine/sdk/imgui/imgui.cpp engine/sdk/imgui/imgui_draw.cpp
        engine/sdk/imgui/imgui_tables.cpp engine/sdk/imgui/imgui_widgets.cpp
        engine/sdk/imgui/backends/imgui_impl_glfw.cpp
        engine/sdk/imgui/backends/imgui_impl_dx11.cpp)
    target_include_directories(NcmaGuiCore PUBLIC engine/sdk/imgui engine/sdk/imgui/backends)
    target_link_libraries(NcmaGuiCore PRIVATE glfw d3d11 dxgi d3dcompiler)
    target_compile_definitions(NcmaGuiCore PRIVATE GLFW_INCLUDE_NONE)
    add_library(NcmaGui SHARED engine/source/plugins/gui/GuiPlugin.cpp)
    target_include_directories(NcmaGui PRIVATE engine/source/plugins engine/sdk/imgui engine/sdk/imgui/backends)
    target_compile_definitions(NcmaGui PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN GLFW_INCLUDE_NONE)
    target_link_libraries(NcmaGui PRIVATE NcmaGuiCore NcmaPlatform NcmaRenderer glfw)
    # Third-party ImGui sources keep their own warnings; strict /WX applies to our adapter.
    set_source_files_properties(engine/source/plugins/gui/GuiPlugin.cpp PROPERTIES COMPILE_OPTIONS "/W4;/WX;/permissive-;/utf-8")
    set_target_properties(NcmaGui PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/plugins")
    add_custom_command(TARGET NcmaPlatform POST_BUILD COMMAND "${CMAKE_COMMAND}" -E copy_if_different
        "$<TARGET_FILE:glfw>" "${CMAKE_BINARY_DIR}/m2/plugins/$<TARGET_FILE_NAME:glfw>")
endif()

if(WIN32 AND NCMA_BUILD_TESTS)
    add_executable(NcmaPresentationNativeTests tests/plugins/PresentationNativeTests.cpp)
    target_link_libraries(NcmaPresentationNativeTests PRIVATE NcmaGuiCore NcmaPlatform NcmaRenderer glfw user32)
    target_compile_definitions(NcmaPresentationNativeTests PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_compile_options(NcmaPresentationNativeTests PRIVATE /W4 /WX "/FI${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h" /permissive- /utf-8)
    set_target_properties(NcmaPresentationNativeTests PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/plugins")
    add_executable(NcmaPhysicsNativeTests tests/plugins/PhysicsNativeTests.cpp)
    target_include_directories(NcmaPhysicsNativeTests PRIVATE engine/source/plugins engine/source/runtime)
    target_link_libraries(NcmaPhysicsNativeTests PRIVATE NcmaPhysics Ncma::Eigen)
    target_compile_options(NcmaPhysicsNativeTests PRIVATE /W4 /WX /permissive- /utf-8)
    set_target_properties(NcmaPhysicsNativeTests PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/plugins")
    add_executable(NcmaCharacterNativeTests tests/plugins/CharacterNativeTests.cpp)
    target_include_directories(NcmaCharacterNativeTests PRIVATE engine/source/plugins engine/source/runtime)
    target_link_libraries(NcmaCharacterNativeTests PRIVATE NcmaPhysics Ncma::Eigen)
    target_compile_options(NcmaCharacterNativeTests PRIVATE /W4 /WX /permissive- /utf-8)
    set_target_properties(NcmaCharacterNativeTests PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/plugins")
    add_executable(NcmaRendererNativeTests tests/plugins/RendererNativeTests.cpp
        engine/source/plugins/renderer/SkinKernel.cpp
        engine/source/plugins/renderer/StaticMeshKernel.cpp
        engine/source/plugins/renderer/ResourceKernel.cpp
        engine/source/plugins/renderer/ScenePipelineKernel.cpp
        engine/source/plugins/renderer/ReferenceKernel.cpp engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp engine/source/runtime/renderer/rhi/RhiResources.cpp)
    target_include_directories(NcmaRendererNativeTests PRIVATE engine/source/runtime engine/source/plugins)
    target_compile_definitions(NcmaRendererNativeTests PRIVATE NCMA_RENDERER_EXPORTS NOMINMAX WIN32_LEAN_AND_MEAN GLFW_INCLUDE_NONE)
    target_compile_options(NcmaRendererNativeTests PRIVATE /W4 /WX /permissive- /utf-8)
    target_link_libraries(NcmaRendererNativeTests PRIVATE NcmaPlatform glfw Ncma::Eigen d3d11 dxgi d3dcompiler)
    set_target_properties(NcmaRendererNativeTests PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/plugins")
    add_executable(NcmaKernelReferenceCapture tests/plugins/KernelReferenceCapture.cpp
        engine/source/plugins/renderer/ReferenceKernel.cpp
        engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp
        engine/source/runtime/renderer/rhi/RhiResources.cpp)
    target_include_directories(NcmaKernelReferenceCapture PRIVATE engine/source/runtime engine/source/plugins)
    target_compile_definitions(NcmaKernelReferenceCapture PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN GLFW_INCLUDE_NONE)
    target_compile_options(NcmaKernelReferenceCapture PRIVATE /W4 /WX /permissive- /utf-8)
    target_link_libraries(NcmaKernelReferenceCapture PRIVATE glfw Ncma::Eigen d3d11 dxgi d3dcompiler)
    set_target_properties(NcmaKernelReferenceCapture PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
    add_executable(NcmaRendererValidationProbe tests/plugins/RendererValidationProbe.cpp)
    target_link_libraries(NcmaRendererValidationProbe PRIVATE d3d11)
    target_compile_options(NcmaRendererValidationProbe PRIVATE /W4 /WX /permissive- /utf-8)
    add_executable(NcmaPluginAbiTests tests/plugins/PluginAbiTests.cpp)
    target_include_directories(NcmaPluginAbiTests PRIVATE engine/source/plugins/contracts)
    target_compile_definitions(NcmaPluginAbiTests PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_compile_options(NcmaPluginAbiTests PRIVATE /W4 /WX "/FI${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h" /permissive- /utf-8)
    add_executable(NcmaPluginContractC tests/plugins/PluginContractC.c)
    set_target_properties(NcmaPluginContractC PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON)
    target_include_directories(NcmaPluginContractC PRIVATE engine/source/plugins/contracts)
    foreach(_fixture IN ITEMS Good WrongMajor Short Fail Timeout Missing)
        if(_fixture STREQUAL "Good")
            set(_mode 0)
        elseif(_fixture STREQUAL "WrongMajor")
            set(_mode 1)
        elseif(_fixture STREQUAL "Short")
            set(_mode 2)
        elseif(_fixture STREQUAL "Fail")
            set(_mode 3)
        elseif(_fixture STREQUAL "Timeout")
            set(_mode 4)
        else()
            set(_mode 6)
        endif()
        add_library(NcmaFixture${_fixture} SHARED tests/plugins/Fixture.cpp)
        target_include_directories(NcmaFixture${_fixture} PRIVATE engine/source/plugins)
        target_compile_definitions(NcmaFixture${_fixture} PRIVATE NCMA_FIXTURE_MODE=${_mode})
        target_compile_options(NcmaFixture${_fixture} PRIVATE /W4 /WX /permissive- /utf-8)
        set_target_properties(NcmaFixture${_fixture} PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/m2/fixtures")
    endforeach()
endif()

if(NCMA_BUILD_MANAGED)
    find_program(DOTNET_EXECUTABLE dotnet)
    if(DOTNET_EXECUTABLE)
        add_custom_target(NcmaManaged
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Animation.Tests/Ncma.Animation.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Asset.Import.Tests/Ncma.Asset.Import.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Assets.Tests/Ncma.Assets.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Scene.Tests/Ncma.Scene.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Runtime.Tests/Ncma.Runtime.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release>
                    --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Managed/Ncma.Managed.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release>
                    --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Gameplay.Sample/Ncma.Gameplay.Sample.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release>
                    --nologo
            WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.Core.Tests/Ncma.Editor.Core.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Gameplay.Tests/Ncma.Gameplay.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.Transport.Tests/Ncma.Editor.Transport.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Application.Tests/Ncma.Application.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Interop.Tests/Ncma.Interop.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Rendering.Tests/Ncma.Rendering.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Scene.Rendering.Tests/Ncma.Scene.Rendering.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Presentation.Tests/Ncma.Presentation.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.App/Ncma.Editor.App.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.Services.Tests/Ncma.Editor.Services.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMAND "${DOTNET_EXECUTABLE}" build
                    "${CMAKE_SOURCE_DIR}/managed/Ncma.Physics.Tests/Ncma.Physics.Tests.csproj"
                    --configuration $<IF:$<CONFIG:Debug>,Debug,Release> --nologo
            COMMENT "Building the C# runtime, complete document editor, Play session, host, and sample"
            VERBATIM
        )
    endif()
endif()

if(NCMA_BUILD_TESTS)
    enable_testing()
    add_executable(NcmaImportKernelTests tests/plugins/ImportKernelTests.cpp)
    add_executable(NcmaPoseKernelTests tests/plugins/PoseKernelTests.cpp)
    target_include_directories(NcmaPoseKernelTests PRIVATE engine/source/plugins)
    target_link_libraries(NcmaPoseKernelTests PRIVATE NcmaAnimationKernel Ncma::Eigen)
    if(MSVC)
        target_compile_options(NcmaPoseKernelTests PRIVATE /W4 /WX /permissive- /utf-8 "/FI${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h")
    else()
        target_compile_options(NcmaPoseKernelTests PRIVATE -Wall -Wextra -Wpedantic -Werror -include "${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h")
    endif()
    add_test(NAME NcmaPoseKernelTests COMMAND NcmaPoseKernelTests)
    target_include_directories(NcmaImportKernelTests PRIVATE engine/source/plugins)
    target_link_libraries(NcmaImportKernelTests PRIVATE NcmaImportKernel NcmaCore)
    add_test(NAME NcmaImportKernelTests COMMAND NcmaImportKernelTests "${CMAKE_SOURCE_DIR}")
    set_tests_properties(NcmaImportKernelTests PROPERTIES TIMEOUT 60)
    add_executable(NcmaImportContractC tests/plugins/ImportContractC.c)
    target_include_directories(NcmaImportContractC PRIVATE engine/source/plugins)
    set_target_properties(NcmaImportContractC PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON)
    add_test(NAME NcmaImportContractC COMMAND NcmaImportContractC)
    if(MSVC)
        target_compile_options(NcmaImportKernelTests PRIVATE /W4 /WX /permissive- /utf-8 "/FI${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h")
        target_compile_options(NcmaImportContractC PRIVATE /W4 /WX /utf-8)
    else()
        target_compile_options(NcmaImportKernelTests PRIVATE -Wall -Wextra -Wpedantic -Werror -include "${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h")
    endif()
    if(WIN32)
        add_test(NAME NcmaPhysicsNativeTests COMMAND NcmaPhysicsNativeTests)
        set_tests_properties(NcmaPhysicsNativeTests PROPERTIES TIMEOUT 120)
        add_test(NAME NcmaCharacterNativeTests COMMAND NcmaCharacterNativeTests)
        set_tests_properties(NcmaCharacterNativeTests PROPERTIES TIMEOUT 120)
        add_test(NAME NcmaKernelReferenceCapture COMMAND NcmaKernelReferenceCapture "${CMAKE_SOURCE_DIR}" $<IF:$<CONFIG:Debug>,Debug,Release>)
        set_tests_properties(NcmaKernelReferenceCapture PROPERTIES FIXTURES_SETUP kernel_reference_image TIMEOUT 30)
        add_test(NAME NcmaRendererNativeTests COMMAND NcmaRendererNativeTests $<TARGET_FILE:NcmaPlatform>)
        set_tests_properties(NcmaRendererNativeTests PROPERTIES TIMEOUT 30)
        add_test(NAME NcmaPresentationNativeTests COMMAND NcmaPresentationNativeTests $<TARGET_FILE:NcmaPlatform> "${CMAKE_SOURCE_DIR}/engine/build/resources/NcmaEngine.ico")
        set_tests_properties(NcmaPresentationNativeTests PROPERTIES TIMEOUT 30)
        add_test(NAME NcmaPluginAbiTests COMMAND NcmaPluginAbiTests $<TARGET_FILE:NcmaFixtureGood> $<TARGET_FILE:NcmaFixtureWrongMajor>)
        add_test(NAME NcmaPluginContractC COMMAND NcmaPluginContractC)
    endif()
    add_executable(NcmaArchitectureTests tests/ArchitectureTests.cpp)
    target_link_libraries(NcmaArchitectureTests PRIVATE NcmaCore NcmaNative)
    # Keep test assertions active without conflicting /DNDEBUG and /UNDEBUG (MSVC D9025).
    if(MSVC)
        target_compile_options(NcmaArchitectureTests PRIVATE "/FI${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h")
    else()
        target_compile_options(NcmaArchitectureTests PRIVATE -include "${CMAKE_CURRENT_SOURCE_DIR}/tests/TestAssertions.h")
    endif()
    add_test(NAME NcmaArchitectureTests COMMAND NcmaArchitectureTests)
    set_tests_properties(NcmaArchitectureTests PROPERTIES TIMEOUT 30)
    if(NCMA_BUILD_MANAGED AND DOTNET_EXECUTABLE)
        if(WIN32)
            add_test(NAME NcmaPhysicsTests COMMAND "${DOTNET_EXECUTABLE}"
                "${CMAKE_SOURCE_DIR}/managed/Ncma.Physics.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Physics.Tests.dll"
                "${CMAKE_BINARY_DIR}/m2/plugins" "${CMAKE_SOURCE_DIR}/out/verification/m2/physics-$<IF:$<CONFIG:Debug>,Debug,Release>")
            set_tests_properties(NcmaPhysicsTests PROPERTIES TIMEOUT 120)
            add_test(NAME NcmaRenderingTests COMMAND "${DOTNET_EXECUTABLE}"
                "${CMAKE_SOURCE_DIR}/managed/Ncma.Rendering.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Rendering.Tests.dll"
                "${CMAKE_BINARY_DIR}/m2/plugins" "${CMAKE_SOURCE_DIR}/out/verification/m2-8/kernel-$<IF:$<CONFIG:Debug>,Debug,Release>/reference.rgba"
                "${CMAKE_SOURCE_DIR}/out/verification/m2/render-$<IF:$<CONFIG:Debug>,Debug,Release>"
                "${CMAKE_SOURCE_DIR}/tests/assets/m2/render-legacy-reference.json"
                "${CMAKE_SOURCE_DIR}" "$<TARGET_FILE:NcmaImportKernel>")
            set_tests_properties(NcmaRenderingTests PROPERTIES TIMEOUT 240 FIXTURES_REQUIRED kernel_reference_image)
            add_test(NAME NcmaCandidateGraphicsSmoke COMMAND "${DOTNET_EXECUTABLE}"
                "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.App/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/NcmaEngine.dll"
                --smoke-test --plugins "${CMAKE_BINARY_DIR}/m2/plugins")
            set_tests_properties(NcmaCandidateGraphicsSmoke PROPERTIES TIMEOUT 60)
            add_test(NAME NcmaPresentationTests COMMAND "${DOTNET_EXECUTABLE}"
                "${CMAKE_SOURCE_DIR}/managed/Ncma.Presentation.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Presentation.Tests.dll" "${CMAKE_BINARY_DIR}/m2/plugins")
            set_tests_properties(NcmaPresentationTests PROPERTIES TIMEOUT 60)
            add_test(NAME NcmaCandidatePresentationSmoke COMMAND "${DOTNET_EXECUTABLE}"
                "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.App/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/NcmaEngine.dll"
                --smoke-test --cpu-only --plugins "${CMAKE_BINARY_DIR}/m2/plugins")
            set_tests_properties(NcmaCandidatePresentationSmoke PROPERTIES TIMEOUT 30)
            add_test(NAME NcmaInteropTests COMMAND "${DOTNET_EXECUTABLE}"
                "${CMAKE_SOURCE_DIR}/managed/Ncma.Interop.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Interop.Tests.dll" "${CMAKE_BINARY_DIR}/m2/fixtures")
            set_tests_properties(NcmaInteropTests PROPERTIES TIMEOUT 30)
        endif()
        add_test(NAME NcmaApplicationServiceTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Application.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Application.Tests.dll" "${CMAKE_SOURCE_DIR}")
        set_tests_properties(NcmaApplicationServiceTests PROPERTIES TIMEOUT 30)
        add_test(NAME NcmaPoseManagedTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Animation.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Animation.Tests.dll" "$<TARGET_FILE:NcmaAnimationKernel>" "${CMAKE_SOURCE_DIR}" "$<IF:$<CONFIG:Debug>,Debug,Release>")
        set_tests_properties(NcmaPoseManagedTests PROPERTIES TIMEOUT 60)
        add_test(NAME NcmaEditorServiceTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.Services.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Editor.Services.Tests.dll" "${CMAKE_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/m2/plugins")
        # Includes bounded real-worker import + normal Editor offscreen/restart workflow.
        set_tests_properties(NcmaEditorServiceTests PROPERTIES TIMEOUT 180)
        add_test(NAME NcmaPlayerTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Player.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Player.Tests.dll"
            "${CMAKE_SOURCE_DIR}" "$<IF:$<CONFIG:Debug>,Debug,Release>" "${CMAKE_BINARY_DIR}/m2/plugins")
        set_tests_properties(NcmaPlayerTests PROPERTIES TIMEOUT 120)
        if(WIN32)
            add_test(NAME NcmaEditorDeploymentTests COMMAND powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass
                -File "${CMAKE_SOURCE_DIR}/scripts/Test-EditorDeployment.ps1" -Configuration "$<IF:$<CONFIG:Debug>,Debug,Release>")
            set_tests_properties(NcmaEditorDeploymentTests PROPERTIES TIMEOUT 120)
        endif()
        add_test(NAME NcmaManagedHeadlessTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Runtime.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Runtime.Tests.dll")
        set_tests_properties(NcmaManagedHeadlessTests PROPERTIES TIMEOUT 30)
        add_test(NAME NcmaSceneDocumentTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Scene.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Scene.Tests.dll")
        set_tests_properties(NcmaSceneDocumentTests PROPERTIES TIMEOUT 30)
        add_test(NAME NcmaSceneRenderingTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Scene.Rendering.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Scene.Rendering.Tests.dll")
        set_tests_properties(NcmaSceneRenderingTests PROPERTIES TIMEOUT 60)
        add_test(NAME NcmaEditorCoreTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.Core.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Editor.Core.Tests.dll")
        set_tests_properties(NcmaEditorCoreTests PROPERTIES TIMEOUT 30)
        add_test(NAME NcmaAssetTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Assets.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Assets.Tests.dll")
        # Includes 64 MiB descriptor parsing and 16384 real file creations; keep all boundary assertions.
        set_tests_properties(NcmaAssetTests PROPERTIES TIMEOUT 180)
        if(WIN32)
            add_test(NAME NcmaAssetImportTests COMMAND "${DOTNET_EXECUTABLE}"
                "${CMAKE_SOURCE_DIR}/managed/Ncma.Asset.Import.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Asset.Import.Tests.dll"
                "${CMAKE_SOURCE_DIR}" "${CMAKE_BINARY_DIR}" $<IF:$<CONFIG:Debug>,Debug,Release>)
            set_tests_properties(NcmaAssetImportTests PROPERTIES TIMEOUT 120)
        endif()
        add_test(NAME NcmaGameplayTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Gameplay.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Gameplay.Tests.dll")
        set_tests_properties(NcmaGameplayTests PROPERTIES TIMEOUT 30)
        add_test(NAME NcmaEditorTransportTests COMMAND "${DOTNET_EXECUTABLE}"
            "${CMAKE_SOURCE_DIR}/managed/Ncma.Editor.Transport.Tests/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0/Ncma.Editor.Transport.Tests.dll")
        set_tests_properties(NcmaEditorTransportTests PROPERTIES TIMEOUT 30 WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}")
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
endif()
