#include "PostProcessKernel.h"
#include <cstring>
#include <algorithm>
namespace NcmaEngine::Rendering {
bool PostProcessKernel::Initialize(const NcmaPostDescriptionV1& d,std::string& e){
    dimensions.assign(d.targets,d.targets+d.target_count);passes.assign(d.passes,d.passes+d.pass_count);output=d.output;bytes=16;
    targets.resize(static_cast<size_t>(d.target_count)+1);programs.resize(d.program_count);code.resize(d.program_count);
    for(uint32_t i=0;i<d.program_count;i++){
        Rhi::GraphicsPipelineDescription p{};p.Cull=Rhi::CullMode::None;p.DebugName="Scene.HdrPost.v1";
        const auto& s=d.programs[i].shaders;p.VertexBytecode={s.vertex,s.vertex_bytes};p.PixelBytecode={s.pixel,s.pixel_bytes};
        code[i].inputs=d.programs[i].inputs;code[i].vertex.assign(s.vertex,s.vertex+s.vertex_bytes);code[i].pixel.assign(s.pixel,s.pixel+s.pixel_bytes);
        programs[i]=backend.CreateGraphicsPipeline(p,e);if(!programs[i])return false;
    }
    for(uint32_t i=0;i<d.target_count;i++){
        Rhi::TextureDescription t{};t.Width=dimensions[i].width;t.Height=dimensions[i].height;t.Format=Rhi::TextureFormat::Rgba16Float;
        t.Usage=Rhi::TextureUsage::Sampled|Rhi::TextureUsage::RenderTarget;targets[i+1]=backend.CreateTexture(t,e);if(!targets[i+1])return false;
        bytes+=static_cast<uint64_t>(t.Width)*t.Height*8;
    }
    Rhi::BufferDescription b{};b.Size=16;b.Usage=Rhi::BufferUsage::Constant;b.Memory=Rhi::MemoryUsage::CpuToGpu;
    constants=backend.CreateBuffer(b,nullptr,e);if(!constants)return false;
    Rhi::SamplerDescription s{};s.Filter=Rhi::SamplerFilter::Linear;s.AddressU=s.AddressV=s.AddressW=Rhi::SamplerAddressMode::ClampToEdge;
    sampler=backend.CreateSampler(s,e);return static_cast<bool>(sampler);
}
bool PostProcessKernel::Execute(Rhi::TextureHandle hdr,std::string& e){
    // Resource0 is borrowed for this call, not owned by this kernel.
    for(const auto& p:passes){
        Rhi::RenderPassDescription pass{};pass.ColorTarget=targets[p.output];if(!backend.BeginRenderPass(pass,e))return false;
        if(!backend.UpdateBuffer(constants,p.parameters,16,0,e)){backend.EndRenderPass();return false;}
        Rhi::DrawDescription draw{};draw.Pipeline=programs[p.program];draw.VertexCount=3;draw.PixelConstantBuffer=constants;
        draw.PixelTextures[0]=p.input0?targets[p.input0]:hdr;draw.PixelSamplers[0]=sampler;
        if(p.input1!=UINT32_MAX){draw.PixelTextures[1]=p.input1?targets[p.input1]:hdr;draw.PixelSamplers[1]=sampler;}
        draw.ViewportWidth=static_cast<float>(dimensions[p.output-1].width);draw.ViewportHeight=static_cast<float>(dimensions[p.output-1].height);
        const bool ok=backend.Draw(draw,e);backend.EndRenderPass();if(!ok)return false;
    }return true;
}
bool PostProcessKernel::SameLayout(const NcmaPostDescriptionV1& d) const noexcept {
    if(d.program_count!=code.size()||d.target_count!=dimensions.size()||d.pass_count!=passes.size()||d.output!=output)return false;
    for(size_t i=0;i<code.size();i++){const auto& p=d.programs[i];const auto& c=code[i];if(p.inputs!=c.inputs||p.shaders.vertex_bytes!=c.vertex.size()||p.shaders.pixel_bytes!=c.pixel.size()||
        std::memcmp(c.vertex.data(),p.shaders.vertex,c.vertex.size())||std::memcmp(c.pixel.data(),p.shaders.pixel,c.pixel.size()))return false;}
    for(size_t i=0;i<dimensions.size();i++)if(std::memcmp(&dimensions[i],&d.targets[i],sizeof(NcmaPostTargetV1)))return false;
    for(size_t i=0;i<passes.size();i++)if(std::memcmp(&passes[i],&d.passes[i],16))return false;return true;
}
void PostProcessKernel::UpdateParameters(const NcmaPostDescriptionV1& d) noexcept {for(size_t i=0;i<passes.size();i++)std::copy_n(d.passes[i].parameters,4,passes[i].parameters);}
}
