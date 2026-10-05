#include "ResourceKernel.h"
#include <cstring>
namespace NcmaEngine::Rendering {
ResourceKernel::~ResourceKernel(){backend.DestroyGraphicsPipeline(pipeline);backend.DestroyBuffer(constants);backend.DestroySampler(sampler);backend.DestroyTexture(white);backend.DestroyTexture(normal);}
bool ResourceKernel::Initialize(std::string& error) {
    constexpr std::string_view shader=R"(
cbuffer C : register(b0) {
 column_major float4x4 MVP,Model,NormalMatrix;
 float4 Base,Emissive,Surface,Camera,LightDirection,LightColor,Settings,Channels;
};
Texture2D BaseTex:register(t0);Texture2D NormalTex:register(t1);Texture2D MetalTex:register(t2);
Texture2D RoughTex:register(t3);Texture2D AOTex:register(t4);Texture2D EmissiveTex:register(t5);
SamplerState S:register(s0);
struct Input {float3 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float4 t:TANGENT;};
struct Output {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float4 t:TEXCOORD2;float2 uv:TEXCOORD3;};
Output VSMain(Input i){Output o;o.p=mul(MVP,float4(i.p,1));o.world=mul(Model,float4(i.p,1)).xyz;
 o.n=mul((float3x3)NormalMatrix,i.n);o.t=float4(mul((float3x3)Model,i.t.xyz),i.t.w);o.uv=i.uv;return o;}
float3 Encode(float3 c){return lerp(12.92*c,1.055*pow(max(c,0),1.0/2.4)-.055,step(.0031308,c));}
float3 ACES(float3 x){return saturate((x*(2.51*x+.03))/(x*(2.43*x+.59)+.14));}
float4 PSMain(Output i):SV_TARGET {
 float4 base=Base*BaseTex.Sample(S,i.uv);uint flags=(uint)Settings.w;
 if((flags&1)!=0)clip(base.a-Surface.w);
 float3 n=normalize(i.n);
 if((flags&2)!=0){float3 t=normalize(i.t.xyz-n*dot(n,i.t.xyz));float3 b=cross(n,t)*i.t.w;
  float3 mapped=NormalTex.Sample(S,i.uv).xyz*2-1;mapped.xy*=Surface.z;if((flags&4)!=0)mapped.y=-mapped.y;
  n=normalize(t*mapped.x+b*mapped.y+n*mapped.z);}
 if(Settings.z==2)return float4(saturate(n*.5+.5),1);
 if(Settings.z==1)return float4(Encode(saturate(base.rgb)),1);
 float metallic=saturate(Surface.x*MetalTex.Sample(S,i.uv)[(uint)Channels.x]);
 float roughness=clamp(Surface.y*RoughTex.Sample(S,i.uv)[(uint)Channels.y],.045,1);
 float ao=saturate(AOTex.Sample(S,i.uv)[(uint)Channels.z]);
 float3 v=normalize(Camera.xyz-i.world),l=normalize(LightDirection.xyz),h=normalize(v+l);
 float nl=saturate(dot(n,l)),nv=max(saturate(dot(n,v)),.0001),nh=saturate(dot(n,h));
 float a=roughness*roughness,a2=a*a,d=(nh*nh*(a2-1)+1);float distribution=a2/max(3.14159265*d*d,.000001);
 float k=(roughness+1)*(roughness+1)/8;
 float geometry=nv/(nv*(1-k)+k)*nl/max(nl*(1-k)+k,.0001);
 float3 f0=lerp(.04.xxx,base.rgb,metallic),f=f0+(1-f0)*pow(1-saturate(dot(h,v)),5);
 float3 spec=distribution*geometry*f/max(4*nv*nl,.0001);
 float3 diffuse=(1-f)*(1-metallic)*base.rgb/3.14159265;
 float3 hdr=(diffuse+spec)*LightColor.rgb*LightColor.w*nl+base.rgb*Settings.y*ao+Emissive.rgb*EmissiveTex.Sample(S,i.uv).rgb;
 return float4(Encode(ACES(max(hdr,0)*Settings.x)),1);
})";
    Rhi::GraphicsPipelineDescription p{};p.VertexShaderSource=shader;p.PixelShaderSource=shader;
    p.VertexLayout={{"POSITION",0,Rhi::VertexFormat::Float3,0},{"NORMAL",0,Rhi::VertexFormat::Float3,12},{"TEXCOORD",0,Rhi::VertexFormat::Float2,24},{"TANGENT",0,Rhi::VertexFormat::Float4,32}};
    p.Cull=Rhi::CullMode::None;p.DepthTest=true;p.DepthWrite=true;p.DebugName="Resource.PBR.v3";
    pipeline=backend.CreateGraphicsPipeline(p,error);if(!pipeline)return false;
    Rhi::BufferDescription b{};b.Size=320;b.Usage=Rhi::BufferUsage::Constant;b.Memory=Rhi::MemoryUsage::CpuToGpu;b.DebugName="Resource.DrawConstants";
    constants=backend.CreateBuffer(b,nullptr,error);if(!constants)return false;
    Rhi::SamplerDescription s{};s.Filter=Rhi::SamplerFilter::Nearest;s.DebugName="Resource.NearestRepeat";sampler=backend.CreateSampler(s,error);if(!sampler)return false;
    const uint8_t pixels[4]{255,255,255,255},flat[4]{128,128,255,255};Rhi::TextureDescription t{};t.InitialMips={{pixels,4,4}};
    white=backend.CreateTexture(t,error);if(!white)return false;t.InitialMips={{flat,4,4}};normal=backend.CreateTexture(t,error);return static_cast<bool>(normal);
}
bool ResourceKernel::Draw(const StaticMesh& mesh,const NcmaResourceDrawV3& draw,const NcmaMaterialDescriptionV3& material,
    const std::array<Rhi::TextureHandle,6>& textures,const NcmaResourceFrameV3& frame,std::string& error) {
    float c[80]{};std::memcpy(c,draw.model_view_projection,64);std::memcpy(c+16,draw.model,64);std::memcpy(c+32,draw.normal_matrix,64);
    std::memcpy(c+48,material.base_color,16);std::memcpy(c+52,material.emissive,16);std::memcpy(c+56,material.surface,16);
    std::memcpy(c+60,frame.camera,16);std::memcpy(c+64,frame.light_direction,16);std::memcpy(c+68,frame.light_color,16);
    c[72]=frame.exposure;c[73]=frame.ambient;c[74]=static_cast<float>(frame.mode);c[75]=static_cast<float>(material.flags);
    c[76]=static_cast<float>(material.channels[0]&255);c[77]=static_cast<float>((material.channels[0]>>8)&255);c[78]=static_cast<float>((material.channels[0]>>16)&255);
    if(!backend.UpdateBuffer(constants,c,sizeof(c),0,error))return false;
    Rhi::DrawDescription d{};d.Pipeline=pipeline;d.VertexBuffer=mesh.vertices;d.IndexBuffer=mesh.indices;d.VertexConstantBuffer=constants;d.PixelConstantBuffer=constants;
    for(size_t i=0;i<textures.size();++i){d.PixelTextures[i]=textures[i]?textures[i]:(i==1?normal:white);d.PixelSamplers[i]=sampler;}
    d.VertexStride=mesh.vertexStride;d.IndexStride=4;d.FirstIndex=draw.first_index;d.IndexCount=draw.index_count;
    d.ViewportX=frame.viewport[0];d.ViewportY=frame.viewport[1];d.ViewportWidth=frame.viewport[2];d.ViewportHeight=frame.viewport[3];return backend.Draw(d,error);
}
}
