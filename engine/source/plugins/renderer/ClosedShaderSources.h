#pragma once
// Single source of truth for runtime source APIs and build-host reference compilation.
// Moving these literals must not change their bytes or the environment transformation.
#include <string>
#include <string_view>
#include <stdexcept>
namespace NcmaEngine::Rendering::ClosedShaderSources {
inline constexpr std::string_view Scene=R"(
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
inline constexpr std::string_view Skin=R"(
struct Input {float3 p;float3 n;float2 uv;float4 t;uint4 joints;float4 weights;};
struct Palette {column_major float4x4 model;column_major float4x4 normal;};
StructuredBuffer<Input> Source:register(t0);StructuredBuffer<Palette> Bones:register(t1);
RWByteAddressBuffer Output:register(u0);
cbuffer Settings:register(b0){uint VertexCount,Offset,Unused,Unused2;};
float3 Unit(float3 v,float3 fallback){float length2=dot(v,v);return length2>1e-12?v*rsqrt(length2):fallback;}
[numthreads(64,1,1)]void CSMain(uint3 id:SV_DispatchThreadID){if(id.x>=VertexCount)return;
 Input v=Source[id.x];float3 p=0,n=0,t=0;
 [unroll]for(uint k=0;k<4;k++){Palette b=Bones[Offset+v.joints[k]];
 p+=mul(b.model,float4(v.p,1)).xyz*v.weights[k];n+=mul((float3x3)b.normal,v.n)*v.weights[k];t+=mul((float3x3)b.model,v.t.xyz)*v.weights[k];}
 n=Unit(n,float3(0,1,0));float3 fallback=Unit(cross(abs(n.y)<.9?float3(0,1,0):float3(1,0,0),n),float3(1,0,0));t=Unit(t-n*dot(n,t),fallback);
 uint at=id.x*48;Output.Store3(at,asuint(p));Output.Store3(at+12,asuint(n));Output.Store2(at+24,asuint(v.uv));Output.Store4(at+32,asuint(float4(t,v.t.w)));
})";
inline constexpr std::string_view Ui=R"(
cbuffer Frame : register(b0) {float2 Size;float2 Pad;};
Texture2D Image : register(t0);SamplerState Linear : register(s0);
struct V {float2 p:POSITION;float2 uv:TEXCOORD0;float4 c:COLOR0;float2 local:TEXCOORD1;float2 extent:TEXCOORD2;float radius:TEXCOORD3;};
struct O {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float4 c:COLOR0;float2 local:TEXCOORD1;float2 extent:TEXCOORD2;float radius:TEXCOORD3;};
O VSMain(V v){O o;o.p=float4(v.p.x/Size.x*2-1,1-v.p.y/Size.y*2,0,1);o.uv=v.uv;o.c=v.c;o.local=v.local;o.extent=v.extent;o.radius=v.radius;return o;}
float4 PSMain(O v):SV_TARGET {float coverage=1;if(v.radius>0){float2 q=abs(v.local-v.extent*.5)-(v.extent*.5-v.radius);float d=length(max(q,0))+min(max(q.x,q.y),0)-v.radius;coverage=saturate(.5-d/max(fwidth(d),.001));}return Image.Sample(Linear,v.uv)*v.c*float4(1,1,1,coverage);}
)";
inline constexpr const char* Post=R"(
cbuffer Post:register(b0){float4 Params;};
Texture2D Input0:register(t0);Texture2D Input1:register(t1);SamplerState LinearClamp:register(s0);
struct Full{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
Full VSPost(uint id:SV_VertexID){Full o;o.uv=float2((id<<1)&2,id&2);o.p=float4(o.uv*float2(2,-2)+float2(-1,1),0,1);return o;}
float4 PSOne(Full i):SV_TARGET{return Input0.SampleLevel(LinearClamp,i.uv,0)*Params;}
float4 PSTwo(Full i):SV_TARGET{return Input0.SampleLevel(LinearClamp,i.uv,0)+Input1.SampleLevel(LinearClamp,i.uv,0)*Params;}
)";
inline std::string_view Environment(){
 static const std::string source=[](){std::string text(Scene);
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

}
