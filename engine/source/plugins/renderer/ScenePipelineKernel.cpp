#include "ScenePipelineKernel.h"
#include "ClosedShaderSources.h"
#include <cstring>
#include <algorithm>
#include <cmath>
#include <stdexcept>
namespace NcmaEngine::Rendering {
namespace {
constexpr auto shader=ClosedShaderSources::Scene;
}
ScenePipelineKernel::~ScenePipelineKernel(){backend.DestroyGraphicsPipeline(geometry);backend.DestroyGraphicsPipeline(shadowPipeline);backend.DestroyGraphicsPipeline(tone);
 backend.DestroyBuffer(constants);backend.DestroySampler(sampler);backend.DestroySampler(shadowSampler);for(auto t:{white,normal,hdr,depth,shadow})backend.DestroyTexture(t);}
std::string_view ScenePipelineKernel::ShaderSource(){return shader;}
std::string_view ScenePipelineKernel::EnvironmentShaderSource(){return ClosedShaderSources::Environment();}
Rhi::GraphicsPipelineHandle ScenePipelineKernel::PrepareTone(const NcmaShaderPairV1& pair,std::string& e){
 Rhi::GraphicsPipelineDescription p{};p.VertexBytecode={pair.vertex,pair.vertex_bytes};p.PixelBytecode={pair.pixel,pair.pixel_bytes};
 p.Cull=Rhi::CullMode::None;p.DebugName="Scene.RegisteredTone.v1";return backend.CreateGraphicsPipeline(p,e);
}
bool ScenePipelineKernel::PrepareShaders(const NcmaSceneShadersV1& shaders,Programs& result,std::string& e){
 Rhi::GraphicsPipelineDescription p{};p.Cull=Rhi::CullMode::None;p.DepthTest=p.DepthWrite=true;
 p.VertexLayout={{"POSITION",0,Rhi::VertexFormat::Float3,0},{"NORMAL",0,Rhi::VertexFormat::Float3,12},{"TEXCOORD",0,Rhi::VertexFormat::Float2,24},{"TANGENT",0,Rhi::VertexFormat::Float4,32}};
 p.VertexBytecode={shaders.geometry.vertex,shaders.geometry.vertex_bytes};p.PixelBytecode={shaders.geometry.pixel,shaders.geometry.pixel_bytes};p.DebugName="Scene.RegisteredGeometry.v1";
 result.geometry=backend.CreateGraphicsPipeline(p,e);if(!result.geometry)return false;
 if(resolution){p.VertexBytecode={shaders.shadow.vertex,shaders.shadow.vertex_bytes};p.PixelBytecode={shaders.shadow.pixel,shaders.shadow.pixel_bytes};p.DebugName="Scene.RegisteredShadow.v1";result.shadow=backend.CreateGraphicsPipeline(p,e);if(!result.shadow)return false;}
 result.tone=PrepareTone(shaders.tone,e);return static_cast<bool>(result.tone);
}
bool ScenePipelineKernel::Initialize(const NcmaScenePipelineDescriptionV4& d,std::string& e,const NcmaShaderPairV1* pair,const NcmaSceneShadersV1* shaders,bool environment){
 environmentCapable=environment;if(environment&&!shaders){e="Environment scene requires complete registered group";return false;}
 width=d.width;height=d.height;resolution=d.shadow_resolution;
 const std::string variant=resolution?std::string(shader):"#define NCMA_NO_SCENE_SHADOW\n"+std::string(shader);
 Rhi::GraphicsPipelineDescription p{};p.VertexShaderSource=variant;p.PixelShaderSource=variant;p.Cull=Rhi::CullMode::None;p.DepthTest=true;p.DepthWrite=true;
 p.VertexLayout={{"POSITION",0,Rhi::VertexFormat::Float3,0},{"NORMAL",0,Rhi::VertexFormat::Float3,12},{"TEXCOORD",0,Rhi::VertexFormat::Float2,24},{"TANGENT",0,Rhi::VertexFormat::Float4,32}};
 if(shaders){Programs candidate{};struct Scope{ScenePipelineKernel& scene;Programs& candidate;~Scope(){scene.ReleasePrograms(candidate);}} retained{*this,candidate};if(!PrepareShaders(*shaders,candidate,e))return false;PublishShaders(candidate);}
 else {
 p.DebugName="Scene.HDR.v4";geometry=backend.CreateGraphicsPipeline(p,e);if(!geometry)return false;
 if(resolution){p.VertexEntryPoint="VSShadowAlpha";p.PixelEntryPoint="PSShadow";p.DebugName="Scene.Shadow.v4";shadowPipeline=backend.CreateGraphicsPipeline(p,e);if(!shadowPipeline)return false;}
 p.VertexEntryPoint="VSTone";p.PixelEntryPoint="PSTone";p.VertexLayout.clear();p.DepthTest=false;p.DepthWrite=false;p.DebugName="Scene.Tone.v4";tone=pair?PrepareTone(*pair,e):backend.CreateGraphicsPipeline(p,e);if(!tone)return false;
 }
 Rhi::BufferDescription b{};b.Size=ConstantBytes();b.Usage=Rhi::BufferUsage::Constant;b.Memory=Rhi::MemoryUsage::CpuToGpu;constants=backend.CreateBuffer(b,nullptr,e);if(!constants)return false;
 Rhi::SamplerDescription s{};s.Filter=Rhi::SamplerFilter::Nearest;sampler=backend.CreateSampler(s,e);if(!sampler)return false;
 if(resolution){s.AddressU=s.AddressV=Rhi::SamplerAddressMode::ClampToBorder;for(float& c:s.BorderColor)c=1;shadowSampler=backend.CreateSampler(s,e);if(!shadowSampler)return false;}
 const uint8_t w[4]{255,255,255,255},n[4]{128,128,255,255};Rhi::TextureDescription t{};t.InitialMips={{w,4,4}};white=backend.CreateTexture(t,e);if(!white)return false;t.InitialMips={{n,4,4}};normal=backend.CreateTexture(t,e);if(!normal)return false;
 t.InitialMips.clear();t.Width=width;t.Height=height;t.Format=Rhi::TextureFormat::Rgba16Float;t.Usage=Rhi::TextureUsage::Sampled|Rhi::TextureUsage::RenderTarget;hdr=backend.CreateTexture(t,e);if(!hdr)return false;
 t.Format=Rhi::TextureFormat::D32Float;t.Usage=Rhi::TextureUsage::Sampled|Rhi::TextureUsage::DepthStencil;depth=backend.CreateTexture(t,e);if(!depth)return false;
 if(resolution){t.Width=t.Height=resolution;shadow=backend.CreateTexture(t,e);return static_cast<bool>(shadow);}return true;
}
bool ScenePipelineKernel::BeginShadow(std::string& e){Rhi::RenderPassDescription p{};p.UseColorTarget=false;p.DepthTarget=shadow;p.ClearDepth=true;return backend.BeginRenderPass(p,e);}
bool ScenePipelineKernel::BeginGeometry(const NcmaSceneFrameV4& f,std::string& e){Rhi::RenderPassDescription p{};p.ColorTarget=hdr;p.DepthTarget=depth;p.ClearColor=p.ClearDepth=true;
 std::memcpy(p.ClearColorValue,f.base.clear,16);return backend.BeginRenderPass(p,e);}
bool ScenePipelineKernel::Draw(const StaticMesh& mesh,const NcmaSceneDrawV4& d,const NcmaMaterialDescriptionV3& material,
 const std::array<Rhi::TextureHandle,6>& textures,const NcmaSceneFrameV4& f,bool isShadow,bool shadows,float ambient,std::string& e){
 float c[104]{};std::memcpy(c,d.draw.model_view_projection,64);std::memcpy(c+16,d.draw.model,64);std::memcpy(c+32,d.draw.normal_matrix,64);std::memcpy(c+48,f.light_view_projection,64);
 std::memcpy(c+64,material.base_color,16);std::memcpy(c+68,material.emissive,16);std::memcpy(c+72,material.surface,16);
 if(d.override_surface[2]!=0){c[72]=d.override_surface[0];c[73]=d.override_surface[1];}
 std::memcpy(c+76,f.base.camera,16);std::memcpy(c+80,f.base.light_direction,16);std::memcpy(c+84,f.base.light_color,16);
 if(d.override_surface[3]!=0)c[87]=0;
 c[88]=f.base.exposure;c[89]=ambient;c[90]=shadows?1.f:0.f;c[91]=static_cast<float>(material.flags);
 c[92]=static_cast<float>(material.channels[0]&255);c[93]=static_cast<float>((material.channels[0]>>8)&255);c[94]=static_cast<float>((material.channels[0]>>16)&255);c[95]=resolution?1.f/static_cast<float>(resolution):0.f;std::memcpy(c+96,f.shadow,16);
 if(environmentCapable)std::memcpy(c+100,environmentSettings.data(),16);
 if(!backend.UpdateBuffer(constants,c,ConstantBytes(),0,e))return false;Rhi::DrawDescription draw{};draw.Pipeline=isShadow?shadowPipeline:geometry;draw.VertexBuffer=mesh.vertices;draw.IndexBuffer=mesh.indices;draw.VertexConstantBuffer=draw.PixelConstantBuffer=constants;
 for(size_t i=0;i<6;++i){draw.PixelTextures[i]=textures[i]?textures[i]:(i==1?normal:white);draw.PixelSamplers[i]=sampler;}
 if(!isShadow&&shadows){draw.PixelTextures[6]=shadow;draw.PixelSamplers[6]=shadowSampler;}
 if(!isShadow&&environmentKey.value)for(size_t i=0;i<3;i++){draw.PixelTextures[7+i]=environmentTextures[i];draw.PixelSamplers[7+i]=environmentSampler;}
 if(!isShadow&&environmentCapable&&!environmentKey.value){draw.AllowSamplerOnlyBindings=true;draw.PixelSamplers[7]=sampler;}
 draw.VertexStride=mesh.vertexStride;draw.IndexStride=4;draw.FirstIndex=d.draw.first_index;draw.IndexCount=d.draw.index_count;
 draw.ViewportWidth=static_cast<float>(isShadow?resolution:width);draw.ViewportHeight=static_cast<float>(isShadow?resolution:height);return backend.Draw(draw,e);
}
bool ScenePipelineKernel::Tone(const NcmaSceneFrameV4& f,Rhi::TextureHandle output,float exposure,std::string& e){
 Rhi::RenderPassDescription p{};p.ColorTarget=output;p.ClearColor=static_cast<bool>(output);
 // Independent offscreen targets have no swapchain BeginFrame clear. Deterministic letterbox
 // pixels use the same linear clear -> tone encoding as the scene rectangle.
 for(int i=0;i<3;++i){float x=f.base.clear[i]*exposure;x=std::clamp(x*(2.51f*x+.03f)/(x*(2.43f*x+.59f)+.14f),0.f,1.f);p.ClearColorValue[i]=x<=.0031308f?12.92f*x:1.055f*std::pow(x,1.f/2.4f)-.055f;}p.ClearColorValue[3]=1;
 if(!backend.BeginRenderPass(p,e))return false;
 float c[104]{};c[88]=exposure;if(!backend.UpdateBuffer(constants,c,ConstantBytes(),0,e))return false;
 Rhi::DrawDescription d{};d.Pipeline=tone;d.VertexCount=3;d.PixelConstantBuffer=constants;d.PixelTextures[0]=post?post->Output():hdr;d.PixelSamplers[0]=sampler;
 d.ViewportX=f.base.viewport[0];d.ViewportY=f.base.viewport[1];d.ViewportWidth=f.base.viewport[2];d.ViewportHeight=f.base.viewport[3];bool result=backend.Draw(d,e);backend.EndRenderPass();return result;
}
}
