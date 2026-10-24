#include "../../engine/source/plugins/renderer/ClosedShaderDefinitions.h"
static void CheckClosedShaderReferences() {
    using namespace NcmaEngine::Rendering;
    const auto& definitions = ClosedShaderDefinitions::All();
    Check(definitions.size() == 16 && std::size(ClosedShaderBytes::Entries) == 16, "All closed references present");
    Check(ScenePipelineKernel::ShaderSource() == ClosedShaderSources::Scene &&
        ScenePipelineKernel::EnvironmentShaderSource() == ClosedShaderSources::Environment() &&
        SkinKernel::ShaderSource() == ClosedShaderSources::Skin &&
        UiKernel::ShaderSource() == ClosedShaderSources::Ui, "Source APIs share exact reference sources");
    for (const auto& definition : definitions) {
        const auto bytes = ClosedShaderReference::Find(definition.source, definition.entry, definition.target, definition.sourceName);
        Check(!bytes.empty(), "Missing embedded reference");
        ComPtr<ID3DBlob> legacy, diagnostic;
        Check(SUCCEEDED(D3DCompile(definition.source.data(), definition.source.size(), definition.sourceName,
            nullptr, nullptr, definition.entry, definition.target, ShaderCompilation::Flags, 0, &legacy, &diagnostic)),
            "Independent legacy reference compilation");
        Check(legacy && bytes.size() == legacy->GetBufferSize() &&
            std::memcmp(bytes.data(), legacy->GetBufferPointer(), bytes.size()) == 0,
            "Embedded bytes equal legacy runtime compile");
        ComPtr<ID3D11ShaderReflection> a, b;
        Check(SUCCEEDED(D3DReflect(bytes.data(), bytes.size(), __uuidof(ID3D11ShaderReflection), &a)) &&
            SUCCEEDED(D3DReflect(legacy->GetBufferPointer(), legacy->GetBufferSize(), __uuidof(ID3D11ShaderReflection), &b)),
            "Both closed references reflect");
        const char* reason = nullptr;
        Check(RegisteredStages::SameBindings(a.Get(), b.Get(), reason) &&
            RegisteredStages::SameBindings(b.Get(), a.Get(), reason), "Bidirectional reference shape equality");
        Check(ClosedShaderReference::Find(std::string(definition.source) + "\n", definition.entry,
            definition.target, definition.sourceName).empty(), "Stale source rejected");
        Check(ClosedShaderReference::Find(definition.source, "MissingEntry", definition.target,
            definition.sourceName).empty(), "Wrong entry rejected");
        Check(ClosedShaderReference::Find(definition.source, definition.entry, "vs_4_0",
            definition.sourceName).empty(), "Wrong target rejected");
        Check(ClosedShaderReference::Find(definition.source, definition.entry, definition.target,
            "Other.Contract").empty(), "Wrong source name rejected");
    }
    std::cout << "Closed shader references: 16 exact legacy-byte/reflection comparisons; 64 key negatives; source APIs unchanged.\n";
}
