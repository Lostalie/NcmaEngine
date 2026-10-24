#pragma once
// Build-host definitions only. Product lookup uses generated exact-source metadata.
#include "ClosedShaderSources.h"
#include <array>
#include <d3dcompiler.h>
namespace NcmaEngine::Rendering::ClosedShaderDefinitions {
inline constexpr unsigned Flags =
    D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_WARNINGS_ARE_ERRORS | D3DCOMPILE_OPTIMIZATION_LEVEL3;
struct Definition {
    std::string_view source;
    const char* entry;
    const char* target;
    const char* sourceName;
};
inline const std::array<Definition, 16>& All() {
    static const std::string sceneNoShadow = "#define NCMA_NO_SCENE_SHADOW\n" + std::string(ClosedShaderSources::Scene);
    static const std::string environmentNoShadow = "#define NCMA_NO_SCENE_SHADOW\n" + std::string(ClosedShaderSources::Environment());
    static const std::array<Definition, 16> definitions{{
        {ClosedShaderSources::Scene, "VSMain", "vs_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Scene, "PSMain", "ps_5_0", "Ncma.ClosedStage.v1"},
        {sceneNoShadow, "VSMain", "vs_5_0", "Ncma.ClosedStage.v1"},
        {sceneNoShadow, "PSMain", "ps_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Scene, "VSShadowAlpha", "vs_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Scene, "PSShadow", "ps_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Skin, "CSMain", "cs_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Ui, "VSMain", "vs_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Ui, "PSMain", "ps_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Environment(), "VSMain", "vs_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Environment(), "PSMain", "ps_5_0", "Ncma.ClosedStage.v1"},
        {environmentNoShadow, "VSMain", "vs_5_0", "Ncma.ClosedStage.v1"},
        {environmentNoShadow, "PSMain", "ps_5_0", "Ncma.ClosedStage.v1"},
        {ClosedShaderSources::Post, "VSPost", "vs_5_0", "Ncma.PostContract.v1"},
        {ClosedShaderSources::Post, "PSOne", "ps_5_0", "Ncma.PostContract.v1"},
        {ClosedShaderSources::Post, "PSTwo", "ps_5_0", "Ncma.PostContract.v1"}
    }};
    return definitions;
}
}
