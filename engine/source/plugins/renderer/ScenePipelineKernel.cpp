#include "ScenePipelineKernel.h"
#include <cstring>
#include <algorithm>
#include <cmath>
#include <stdexcept>
namespace NcmaEngine::Rendering {
namespace {
constexpr std::string_view shader=R"(
cbuffer C:register(b0){column_major float4x4 MVP,Model,NormalMatrix,LightVP;
 float4 Base,Emissive,Surface,Camera,LightDirection,LightColor,Settings,Channels,ShadowParameters;};
Texture2D BaseTex:register(t0);Texture2D NormalTex:register(t1);Texture2D MetalTex:register(t2);
Texture2D RoughTex:register(t3);Texture2D AOTex:register(t4);Texture2D EmissiveTex:register(t5);
Texture2D ShadowTex:register(t6);SamplerState S:register(s0);SamplerState SS:register(s6);
struct Input{float3 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float4 t:TANGENT;};
struct Output{float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float4 t:TEXCOORD2;float2 uv:TEXCOORD3;};
Output VSMain(Input i){Output o;float4 w=mul(Model,float4(i.p,1));o.p=mul(MVP,float4(i.p,1));o.world=w.xyz;
 o.n=mul((float3x3)NormalMatrix,i.n);o.t=float4(mul((float3x3)Model,i.t.xyz),i.t.w);o.uv=i.uv;return o;}
float4 VSShadow(Input i):SV_POSITION{return mul(LightVP,mul(Model,float4(i.p,1)));}
void PSShadow(Output i){float4 base=Base*BaseTex.Sample(S,i.uv);if(((uint)Settings.w&1)!=0)clip(base.a-Surface.w);}
Output VSShadowAlpha(Input i){Output o=VSMain(i);o.p=mul(LightVP,float4(o.world,1));return o;}
float Visibility(float3 world,float nl){
#ifdef NCMA_NO_SCENE_SHADOW
 return 1;
#else
 if(Settings.z==0)return 1;float4 q=mul(LightVP,float4(world,1));if(abs(q.w)<1e-6)return 1;q.xyz/=q.w;
 float2 uv=q.xy*float2(.5,-.5)+.5;if(any(uv<0)||any(uv>1)||q.z<0||q.z>1)return 1;
 float z=q.z-ShadowParameters.x-ShadowParameters.y*(1-nl);float texel=Channels.w;
 float radius=1;if(ShadowParameters.z==1){float blockers=0,count=0;
  [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++){float b=ShadowTex.SampleLevel(SS,uv+float2(x,y)*texel*ShadowParameters.w,0).r;if(b<z){blockers+=b;count++;}}
  if(count==0)return 1;radius=clamp((z-blockers/count)*ShadowParameters.w*64,1,8);}
 float visibility=0;[unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++)visibility+=z<=ShadowTex.SampleLevel(SS,uv+float2(x,y)*radius*texel,0).r?1:0;return visibility/9;
#endif
}
float4 PSMain(Output i):SV_TARGET{
 float4 base=Base*BaseTex.Sample(S,i.uv);uint flags=(uint)Settings.w;if((flags&1)!=0)clip(base.a-Surface.w);
 float3 n=normalize(i.n);if((flags&2)!=0){float3 t=normalize(i.t.xyz-n*dot(n,i.t.xyz)),b=cross(n,t)*i.t.w;
 float3 m=NormalTex.Sample(S,i.uv).xyz*2-1;m.xy*=Surface.z;if((flags&4)!=0)m.y=-m.y;n=normalize(t*m.x+b*m.y+n*m.z);}
 float metal=saturate(Surface.x*MetalTex.Sample(S,i.uv)[(uint)Channels.x]);float rough=clamp(Surface.y*RoughTex.Sample(S,i.uv)[(uint)Channels.y],.045,1);
 float ao=saturate(AOTex.Sample(S,i.uv)[(uint)Channels.z]);float3 v=normalize(Camera.xyz-i.world),l=normalize(LightDirection.xyz),sum=v+l,h=sum*rsqrt(max(dot(sum,sum),1e-12));
 float nl=saturate(dot(n,l)),nv=max(saturate(dot(n,v)),.0001),nh=saturate(dot(n,h));float a=rough*rough,a2=a*a,d=nh*nh*(a2-1)+1;
 float distribution=a2/max(3.14159265*d*d,.000001),k=(rough+1)*(rough+1)/8;
 float g=nv/(nv*(1-k)+k)*nl/max(nl*(1-k)+k,.0001);float3 f0=lerp(.04.xxx,base.rgb,metal),f=f0+(1-f0)*pow(1-saturate(dot(h,v)),5);
 float3 spec=distribution*g*f/max(4*nv*nl,.0001),diffuse=(1-f)*(1-metal)*base.rgb/3.14159265;
 float3 color=(diffuse+spec)*LightColor.rgb*LightColor.w*nl*Visibility(i.world,nl)+base.rgb*Settings.y*ao+Emissive.rgb*EmissiveTex.Sample(S,i.uv).rgb;
 return float4(clamp(color,0,65504),1);}
struct Full{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
Full VSTone(uint id:SV_VertexID){Full o;o.uv=float2((id<<1)&2,id&2);o.p=float4(o.uv*float2(2,-2)+float2(-1,1),0,1);return o;}
float4 PSTone(Full i):SV_TARGET{float3 c=BaseTex.SampleLevel(S,i.uv,0).rgb*Settings.x;
 c=saturate((c*(2.51*c+.03))/(c*(2.43*c+.59)+.14));c=lerp(12.92*c,1.055*pow(max(c,0),1.0/2.4)-.055,step(.0031308,c));return float4(c,1);}
)";
}
ScenePipelineKernel::~ScenePipelineKernel(){backend.DestroyGraphicsPipeline(geometry);backend.DestroyGraphicsPipeline(shadowPipeline);backend.DestroyGraphicsPipeline(tone);
 backend.DestroyBuffer(constants);backend.DestroySampler(sampler);backend.DestroySampler(shadowSampler);for(auto t:{white,normal,hdr,depth,shadow})backend.DestroyTexture(t);}
std::string_view ScenePipelineKernel::ShaderSource(){return shader;}
std::string_view ScenePipelineKernel::EnvironmentShaderSource(){
 static const std::string source=[](){std::string text(shader);
  auto replace=[&](std::string_view from,std::string_view to){auto at=text.find(from);if(at==std::string::npos)throw std::runtime_error("Environment internal source contract");text.replace(at,from.size(),to);};
  replace("Channels,ShadowParameters;};","Channels,ShadowParameters,EnvironmentSettings;};");
  replace("struct Input{",R"(
TextureCube IrradianceTex:register(t7);TextureCube SpecularTex:register(t8);
Texture2D BrdfTex:register(t9);SamplerState EnvironmentSampler:register(s7);
float3 EnvironmentDirection(float3 d){return float3(EnvironmentSettings.y*d.x-EnvironmentSettings.z*d.z,d.y,EnvironmentSettings.z*d.x+EnvironmentSettings.y*d.z);}
float3 EnvironmentLight(float3 n,float3 v,float3 base,float metal,float rough,float nv,float ao){
 if(EnvironmentSettings.x==0)return 0;
 float3 f0=lerp(.04.xxx,base,metal),f=f0+(max((1-rough).xxx,f0)-f0)*pow(1-nv,5);
 float3 irradiance=IrradianceTex.SampleLevel(EnvironmentSampler,EnvironmentDirection(n),0).rgb;
 float3 prefiltered=SpecularTex.SampleLevel(EnvironmentSampler,EnvironmentDirection(reflect(-v,n)),rough*EnvironmentSettings.w).rgb;
 uint width,height;BrdfTex.GetDimensions(width,height);float2 uv=(float2(nv,rough)*(float2(width,height)-1)+.5)/float2(width,height);
 float2 ab=BrdfTex.SampleLevel(EnvironmentSampler,uv,0).rg;
 return ((1-f)*(1-metal)*base*irradiance/3.14159265+prefiltered*(f0*ab.x+ab.y))*ao*EnvironmentSettings.x;
}
struct Input{)");
  replace("+base.rgb*Settings.y*ao+Emissive.rgb", "+base.rgb*Settings.y*ao+EnvironmentLight(n,v,base.rgb,metal,rough,nv,ao)+Emissive.rgb");
  return text;}();return source;
}
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
 Rhi::DrawDescription d{};d.Pipeline=tone;d.VertexCount=3;d.PixelConstantBuffer=constants;d.PixelTextures[0]=hdr;d.PixelSamplers[0]=sampler;
 d.ViewportX=f.base.viewport[0];d.ViewportY=f.base.viewport[1];d.ViewportWidth=f.base.viewport[2];d.ViewportHeight=f.base.viewport[3];bool result=backend.Draw(d,e);backend.EndRenderPass();return result;
}
}
