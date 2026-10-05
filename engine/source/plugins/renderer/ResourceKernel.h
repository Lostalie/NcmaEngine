#pragma once
#include "StaticMeshKernel.h"
#include "contracts/NcmaResourceRender.h"
namespace NcmaEngine::Rendering {
class ResourceKernel final {
    Rhi::IRenderBackend& backend;
    Rhi::GraphicsPipelineHandle pipeline;
    Rhi::BufferHandle constants;
    Rhi::SamplerHandle sampler;
    Rhi::TextureHandle white,normal;
public:
    explicit ResourceKernel(Rhi::IRenderBackend& b):backend(b){}
    ~ResourceKernel();
    bool Initialize(std::string&);
    bool Draw(const StaticMesh&,const NcmaResourceDrawV3&,const NcmaMaterialDescriptionV3&,
        const std::array<Rhi::TextureHandle,6>&,const NcmaResourceFrameV3&,std::string&);
};
}
