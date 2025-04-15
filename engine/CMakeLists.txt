set(ENGINE_ROOT_DIR "{ROOT_DIR}/engine")
set(THIRD_PARTY_DIR "{ROOT_DIR}/thirdparty")

set(ENGINE_ASSET_DIR "/asset")
set(CONFIG_DIR "/config")

option(ENABLE_PHYSICS_DEBUG_RENDERER "Enable Physics Debug Renderer" OFF)

if(CMAKE_CXX_COMPILER_ID STREQUAL "MSVC")
    add_compile_options("/MP")
    set_property(DIRECTORY ${CMAKE_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT NcmeEngine)
endif()


set(SHADER_COMPILE_TARGET ShaderCompile)
add_subdirectory(shader)

add_subdirectory(thirdparty)

add_subdirectory(source/runtime)
add_subdirectory(source/editor)