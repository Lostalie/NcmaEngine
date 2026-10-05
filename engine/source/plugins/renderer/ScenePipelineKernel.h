#pragma once
#include "StaticMeshKernel.h"
#include "contracts/NcmaScenePipeline.h"
namespace NcmaEngine::Rendering {
class ScenePipelineKernel final {
    Rhi::IRenderBackend& backend;
    Rhi::GraphicsPipelineHandle geometry,shadowPipeline,tone;
    Rhi::BufferHandle constants;
    Rhi::SamplerHandle sampler,shadowSampler;
    Rhi::TextureHandle white,normal;
public:
    Rhi::TextureHandle hdr,depth,shadow;
    uint32_t width=0,height=0,resolution=0;
    explicit ScenePipelineKernel(Rhi::IRenderBackend& b):backend(b){}
    ~ScenePipelineKernel();
    bool Initialize(const NcmaScenePipelineDescriptionV4&,std::string&);
    bool BeginShadow(std::string&);
    bool BeginGeometry(const NcmaSceneFrameV4&,std::string&);
    bool Draw(const StaticMesh&,const NcmaSceneDrawV4&,const NcmaMaterialDescriptionV3&,
        const std::array<Rhi::TextureHandle,6>&,const NcmaSceneFrameV4&,bool,bool,float,std::string&);
    bool Tone(const NcmaSceneFrameV4&,Rhi::TextureHandle,float,std::string&);
};
}
