#pragma once
#include "StaticMeshKernel.h"
#include "contracts/NcmaPostProcess.h"
namespace NcmaEngine::Rendering {
class PostProcessKernel final {
    Rhi::IRenderBackend& backend;
    std::vector<Rhi::GraphicsPipelineHandle> programs;
    std::vector<Rhi::TextureHandle> targets;
    std::vector<NcmaPostTargetV1> dimensions;
    std::vector<NcmaPostPassV1> passes;
    struct ProgramCopy {uint32_t inputs;std::vector<uint8_t> vertex,pixel;};
    std::vector<ProgramCopy> code;
    Rhi::BufferHandle constants;
    Rhi::SamplerHandle sampler;
    uint32_t output=0;
public:
    uint64_t bytes=0;
    explicit PostProcessKernel(Rhi::IRenderBackend& b):backend(b){}
    ~PostProcessKernel(){for(auto p:programs)backend.DestroyGraphicsPipeline(p);for(auto t:targets)backend.DestroyTexture(t);backend.DestroyBuffer(constants);backend.DestroySampler(sampler);}
    uint64_t TargetCount() const noexcept {return dimensions.size();}
    uint64_t ProgramCount() const noexcept {return programs.size();}
    uint64_t PassCount() const noexcept {return passes.size();}
    Rhi::TextureHandle Output() const noexcept {return targets[output];}
    bool Initialize(const NcmaPostDescriptionV1&,std::string&);
    bool Execute(Rhi::TextureHandle,std::string&);
    bool SameLayout(const NcmaPostDescriptionV1&) const noexcept;
    void UpdateParameters(const NcmaPostDescriptionV1&) noexcept;
};
}
