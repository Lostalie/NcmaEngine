#include "UiKernel.h"
#include <d3dcompiler.h>
#include <cmath>
#include <cstring>
#include <algorithm>

namespace NcmaEngine::Rendering {
using Microsoft::WRL::ComPtr;
std::string_view UiKernel::ShaderSource() noexcept {
    return R"(
cbuffer Frame : register(b0) {float2 Size;float2 Pad;};
Texture2D Image : register(t0);SamplerState Linear : register(s0);
struct V {float2 p:POSITION;float2 uv:TEXCOORD0;float4 c:COLOR0;float2 local:TEXCOORD1;float2 extent:TEXCOORD2;float radius:TEXCOORD3;};
struct O {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float4 c:COLOR0;float2 local:TEXCOORD1;float2 extent:TEXCOORD2;float radius:TEXCOORD3;};
O VSMain(V v){O o;o.p=float4(v.p.x/Size.x*2-1,1-v.p.y/Size.y*2,0,1);o.uv=v.uv;o.c=v.c;o.local=v.local;o.extent=v.extent;o.radius=v.radius;return o;}
float4 PSMain(O v):SV_TARGET {float2 q=abs(v.local-v.extent*.5)-(v.extent*.5-v.radius);float d=length(max(q,0))+min(max(q.x,q.y),0)-v.radius;float coverage=saturate(.5-d/max(fwidth(d),.001));return Image.Sample(Linear,v.uv)*v.c*float4(1,1,1,coverage);}
)";
}
bool UiKernel::PrepareShaders(const NcmaShaderPairV1& code,Programs& candidate,std::string& error) {
    auto check=[&](HRESULT hr,const char* operation){if(FAILED(hr)){error=operation;return false;}return true;};
    if(!check(device->CreateVertexShader(code.vertex,code.vertex_bytes,nullptr,&candidate.vs),"UI VS")||!check(device->CreatePixelShader(code.pixel,code.pixel_bytes,nullptr,&candidate.ps),"UI PS"))return false;
    const D3D11_INPUT_ELEMENT_DESC attributes[]={{"POSITION",0,DXGI_FORMAT_R32G32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},{"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,8,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"COLOR",0,DXGI_FORMAT_R32G32B32A32_FLOAT,0,16,D3D11_INPUT_PER_VERTEX_DATA,0},{"TEXCOORD",1,DXGI_FORMAT_R32G32_FLOAT,0,32,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"TEXCOORD",2,DXGI_FORMAT_R32G32_FLOAT,0,40,D3D11_INPUT_PER_VERTEX_DATA,0},{"TEXCOORD",3,DXGI_FORMAT_R32_FLOAT,0,48,D3D11_INPUT_PER_VERTEX_DATA,0}};
    return check(device->CreateInputLayout(attributes,6,code.vertex,code.vertex_bytes,&candidate.layout),"UI layout");
}
void UiKernel::PublishShaders(Programs& candidate) noexcept {
    vs.Swap(candidate.vs);ps.Swap(candidate.ps);layout.Swap(candidate.layout);
}
bool UiKernel::Initialize(std::string& error,const NcmaShaderPairV1* registered) {
    ComPtr<ID3DBlob> vb,pb,messages;
    auto check=[&](HRESULT hr,const char* operation){if(FAILED(hr)){error=operation;return false;}return true;};
    NcmaShaderPairV1 defaults{};
    if(!registered){auto source=ShaderSource();constexpr auto flags=D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_WARNINGS_ARE_ERRORS|D3DCOMPILE_OPTIMIZATION_LEVEL3;
        if(!check(D3DCompile(source.data(),source.size(),nullptr,nullptr,nullptr,"VSMain","vs_5_0",flags,0,&vb,&messages),"UI vertex shader")||
           !check(D3DCompile(source.data(),source.size(),nullptr,nullptr,nullptr,"PSMain","ps_5_0",flags,0,&pb,&messages),"UI pixel shader"))return false;
        defaults={32,1,static_cast<uint32_t>(vb->GetBufferSize()),static_cast<uint32_t>(pb->GetBufferSize()),static_cast<const uint8_t*>(vb->GetBufferPointer()),static_cast<const uint8_t*>(pb->GetBufferPointer())};registered=&defaults;
    }
    Programs candidate;if(!PrepareShaders(*registered,candidate,error))return false;PublishShaders(candidate);
    D3D11_BUFFER_DESC b{};b.ByteWidth=16;b.Usage=D3D11_USAGE_DEFAULT;b.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    if(!check(device->CreateBuffer(&b,nullptr,&constants),"UI constants"))return false;
    D3D11_RASTERIZER_DESC r{};r.FillMode=D3D11_FILL_SOLID;r.CullMode=D3D11_CULL_NONE;r.ScissorEnable=TRUE;r.DepthClipEnable=TRUE;
    if(!check(device->CreateRasterizerState(&r,&raster),"UI raster"))return false;
    D3D11_BLEND_DESC a{};auto& rt=a.RenderTarget[0];rt.BlendEnable=TRUE;rt.SrcBlend=D3D11_BLEND_SRC_ALPHA;rt.DestBlend=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOp=D3D11_BLEND_OP_ADD;
    rt.SrcBlendAlpha=D3D11_BLEND_ONE;rt.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOpAlpha=D3D11_BLEND_OP_ADD;rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    if(!check(device->CreateBlendState(&a,&blend),"UI blend"))return false;
    D3D11_DEPTH_STENCIL_DESC z{};if(!check(device->CreateDepthStencilState(&z,&depth),"UI depth-disabled state"))return false;
    D3D11_SAMPLER_DESC s{};s.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;s.AddressU=s.AddressV=s.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;s.MaxLOD=D3D11_FLOAT32_MAX;
    return check(device->CreateSamplerState(&s,&sampler),"UI sampler");
}
bool UiKernel::CanImage(const NcmaUiImageV1& d) const {
    return d.struct_size==sizeof(d)&&d.width&&d.height&&d.width<=4096&&d.height<=4096&&d.pixels&&d.byte_count==static_cast<uint64_t>(d.width)*d.height*4&&images.size()<128&&bytes+d.byte_count<=128ull*1024*1024;
}
bool UiKernel::CanList(const NcmaUiListV1& d,uint64_t generation) const {
    if(d.struct_size!=sizeof(d)||d.reserved||!d.vertex_count||d.vertex_count>65536||!d.batch_count||d.batch_count>4096||!d.vertices||!d.batches||lists.size()>=64||
        bytes+static_cast<uint64_t>(d.vertex_count)*sizeof(NcmaUiVertexV1)>128ull*1024*1024)return false;
    for(uint32_t i=0;i<d.vertex_count;i++) {const auto& v=d.vertices[i];float values[13];std::memcpy(values,&v,sizeof(v));
        for(uint32_t j=0;j<13;j++)if(!std::isfinite(values[j])||std::abs(values[j])>65536)return false;
        for(uint32_t j=0;j<4;j++)if(v.color[j]<0||v.color[j]>1)return false;
        if(v.extent[0]<0||v.extent[1]<0||v.radius<0||v.radius>std::min(v.extent[0],v.extent[1])*.5f)return false;
    }
    uint32_t consumed=0;
    for(uint32_t i=0;i<d.batch_count;i++) {const auto& b=d.batches[i];
        if(b.first_vertex!=consumed||!b.vertex_count||b.vertex_count%3||b.vertex_count>d.vertex_count-consumed||b.image.generation!=generation||!images.contains(b.image.value))return false;
        for(float c:b.clip)if(!std::isfinite(c)||c<0||c>16384)return false;
        consumed+=b.vertex_count;
    }
    return consumed==d.vertex_count;
}
bool UiKernel::CreateImage(const NcmaUiImageV1& d,uint64_t& key,std::string& error) {
    auto image=std::make_unique<Image>();ComPtr<ID3D11Texture2D> texture;
    D3D11_TEXTURE2D_DESC desc{};desc.Width=d.width;desc.Height=d.height;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_IMMUTABLE;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA data{d.pixels,d.width*4,0};
    if(FAILED(device->CreateTexture2D(&desc,&data,&texture))||FAILED(device->CreateShaderResourceView(texture.Get(),nullptr,&image->srv))){error="UI image allocation";return false;}
    image->bytes=d.byte_count;key=0x5549000000000000ull|next++;
    images.emplace(key,std::move(image));bytes+=d.byte_count;uploaded+=d.byte_count;return true;
}
bool UiKernel::CreateList(const NcmaUiListV1& d,uint64_t& key,std::string& error) {
    auto list=std::make_unique<List>();list->batches.assign(d.batches,d.batches+d.batch_count);list->bytes=static_cast<uint64_t>(d.vertex_count)*sizeof(NcmaUiVertexV1);
    D3D11_BUFFER_DESC desc{};desc.ByteWidth=static_cast<UINT>(list->bytes);desc.Usage=D3D11_USAGE_IMMUTABLE;desc.BindFlags=D3D11_BIND_VERTEX_BUFFER;D3D11_SUBRESOURCE_DATA initial{d.vertices,0,0};
    if(FAILED(device->CreateBuffer(&desc,&initial,&list->vertices))){error="UI display list allocation";return false;}
    key=0x554c000000000000ull|next++;lists.emplace(key,std::move(list));
    for(const auto& b:lists.at(key)->batches)images.at(b.image.value)->pins++;
    bytes+=desc.ByteWidth;uploaded+=desc.ByteWidth;return true;
}
void UiKernel::Destroy(uint64_t key) {
    if(auto i=lists.find(key);i!=lists.end()){for(const auto& b:i->second->batches)images.at(b.image.value)->pins--;bytes-=i->second->bytes;lists.erase(i);}
    else if(auto image=images.find(key);image!=images.end()){bytes-=image->second->bytes;images.erase(image);}
}
void UiKernel::Draw(uint64_t key,uint32_t width,uint32_t height) {
    const auto& list=*lists.at(key);float data[4]{static_cast<float>(width),static_cast<float>(height),0,0};context->UpdateSubresource(constants.Get(),0,nullptr,data,0,0);
    D3D11_VIEWPORT viewport{0,0,static_cast<float>(width),static_cast<float>(height),0,1};context->RSSetViewports(1,&viewport);context->RSSetState(raster.Get());
    context->OMSetBlendState(blend.Get(),nullptr,0xffffffff);context->OMSetDepthStencilState(depth.Get(),0);context->IASetInputLayout(layout.Get());context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    UINT stride=sizeof(NcmaUiVertexV1),offset=0;ID3D11Buffer* buffer=list.vertices.Get();context->IASetVertexBuffers(0,1,&buffer,&stride,&offset);context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(ps.Get(),nullptr,0);
    ID3D11Buffer* cb=constants.Get();context->VSSetConstantBuffers(0,1,&cb);ID3D11SamplerState* sampling=sampler.Get();context->PSSetSamplers(0,1,&sampling);
    for(const auto& b:list.batches){D3D11_RECT clip{static_cast<LONG>(std::ceil(b.clip[0])),static_cast<LONG>(std::ceil(b.clip[1])),static_cast<LONG>(std::floor(b.clip[0]+b.clip[2])),static_cast<LONG>(std::floor(b.clip[1]+b.clip[3]))};
        context->RSSetScissorRects(1,&clip);ID3D11ShaderResourceView* image=images.at(b.image.value)->srv.Get();context->PSSetShaderResources(0,1,&image);context->Draw(b.vertex_count,b.first_vertex);draws++;}
    ID3D11ShaderResourceView* empty=nullptr;context->PSSetShaderResources(0,1,&empty);submits++;
}
NcmaUiStatsV1 UiKernel::Stats(uint64_t generation,bool pure) const {return {sizeof(NcmaUiStatsV1),pure?1u:0u,generation,images.size(),lists.size(),bytes,uploaded,draws,submits};}
}
