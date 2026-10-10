#pragma once
#include "../contracts/NcmaEnvironmentGpu.h"
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
#include <array>
#include <cmath>
#include <numbers>
#include <bit>
#include <cstring>
#include <stdexcept>
namespace NcmaEngine::Rendering {
#ifdef NCMA_RENDERER_TEST_WAIT
inline int testEnvironmentGpuFailureStage=0; // Test TU only, no production control/export.
#endif
// Numerical GPU resource ownership only. Managed host owns UUID/configuration/policy.
class EnvironmentGpuKernel final {
    Rhi::D3D11RenderBackend& backend;
public:
    std::array<Rhi::TextureHandle,3> textures{};
    Rhi::SamplerHandle sampler{};
    uint32_t scenePins=0;
    NcmaEnvironmentGpuDescriptionV1 layout{}; // values is always null; no caller pointer retained.
    explicit EnvironmentGpuKernel(Rhi::D3D11RenderBackend& b):backend(b){}
    ~EnvironmentGpuKernel(){backend.DestroySampler(sampler);for(auto t:textures)backend.DestroyTexture(t);}
    EnvironmentGpuKernel(const EnvironmentGpuKernel&)=delete;
    EnvironmentGpuKernel& operator=(const EnvironmentGpuKernel&)=delete;
    static bool Describe(const NcmaEnvironmentGpuDescriptionV1& d) noexcept {
        auto power=[](uint32_t n,uint32_t high){return n>=2&&n<=high&&(n&(n-1))==0;};
        if(d.struct_size!=40||d.version!=1||d.reserved||!power(d.cube_size,64)||
            !power(d.irradiance_size,16)||!power(d.lut_size,64))return false;
        uint32_t levels=0,count=6*d.irradiance_size*d.irradiance_size*4+d.lut_size*d.lut_size*2;
        for(uint32_t size=d.cube_size;size;size/=2){levels++;count+=6*size*size*4;}
        return d.levels==levels&&d.float_count==count;
    }
    static bool ValidValues(const NcmaEnvironmentGpuDescriptionV1& d,const std::vector<float>& values) noexcept {
        if(!Describe(d)||values.size()!=d.float_count)return false;
        const uint32_t diffuse=6*d.irradiance_size*d.irradiance_size*4,end=d.float_count-d.lut_size*d.lut_size*2;
        for(uint32_t i=0;i<d.float_count;i++){
            const float v=values[i],high=i<diffuse?65504*std::numbers::pi_v<float>:i<end?65504:2;
            if(!std::isfinite(v)||v<0||v>high||(v==0&&std::signbit(v))||(i<end&&i%4==3&&v!=1))return false;
        }
        return true;
    }
    bool Initialize(const NcmaEnvironmentGpuDescriptionV1& d,const std::vector<float>& values,std::string& error) {
        layout=d;layout.values=nullptr;
        const uint32_t diffuse=6*d.irradiance_size*d.irradiance_size*4;
        for(uint32_t kind=0;kind<3;kind++){
            Rhi::TextureDescription desc;desc.Width=desc.Height=kind==0?d.irradiance_size:kind==1?d.cube_size:d.lut_size;
            desc.Format=kind==2?Rhi::TextureFormat::Rg32Float:Rhi::TextureFormat::Rgba32Float;
            desc.Cube=kind!=2;desc.ArrayLayers=kind==2?1u:6u;desc.MipLevels=kind==1?d.levels:1u;
            desc.DebugName="Environment immutable linear GPU";
            for(uint32_t face=0;face<desc.ArrayLayers;face++){
                uint32_t size=desc.Width,offset=kind==0?face*size*size*4:kind==1?diffuse:d.float_count-d.lut_size*d.lut_size*2;
                for(uint32_t level=0;level<desc.MipLevels;level++){
                    const uint32_t components=kind==2?2u:4u,count=size*size*components;
                    const uint32_t at=kind==1?offset+face*count:offset;
                    desc.InitialMips.push_back({values.data()+at,size*components*4,count*4});
                    offset+=6*count;size/=2;
                }
            }
            textures[kind]=backend.CreateTexture(desc,error);if(!textures[kind])return false;
#ifdef NCMA_RENDERER_TEST_WAIT
            if(testEnvironmentGpuFailureStage==static_cast<int>(kind)+1)throw std::runtime_error("Environment partial GPU candidate fault");
#endif
        }
        Rhi::SamplerDescription s;s.Filter=Rhi::SamplerFilter::Linear;
        s.AddressU=s.AddressV=s.AddressW=Rhi::SamplerAddressMode::ClampToEdge;s.DebugName="Environment linear clamp";
        sampler=backend.CreateSampler(s,error);return static_cast<bool>(sampler);
    }
    struct Capture final {
        std::array<Microsoft::WRL::ComPtr<ID3D11Texture2D>,3> staging;
    };
    bool PrepareCapture(Capture& capture,std::string& error) {
        for(size_t i=0;i<textures.size();i++) {
            auto* view=backend.BorrowTextureView(textures[i]);if(!view){error="Environment texture lifetime";return false;}
            Microsoft::WRL::ComPtr<ID3D11Resource> resource;view->GetResource(&resource);
            Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;
            if(FAILED(resource.As(&texture))){error="Environment resource type";return false;}
            D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);
            desc.BindFlags=desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
            if(FAILED(backend.GetDevice()->CreateTexture2D(&desc,nullptr,&capture.staging[i]))){error="Environment staging allocation";return false;}
            backend.GetDeviceContext()->CopyResource(capture.staging[i].Get(),texture.Get());
        }
        return true;
    }
    // Caller drains the three copies with a bounded event before nonblocking maps.
    bool ReadCapture(const Capture& capture,std::vector<float>& values,std::string& error) {
        auto* dc=backend.GetDeviceContext();const auto& d=layout;const uint32_t diffuse=6*d.irradiance_size*d.irradiance_size*4;
        for(uint32_t kind=0;kind<3;kind++)for(uint32_t face=0;face<(kind==2?1u:6u);face++) {
            uint32_t size=kind==0?d.irradiance_size:kind==1?d.cube_size:d.lut_size;
            uint32_t offset=kind==0?face*size*size*4:kind==1?diffuse:d.float_count-d.lut_size*d.lut_size*2;
            const uint32_t levels=kind==1?d.levels:1u,components=kind==2?2u:4u;
            for(uint32_t level=0;level<levels;level++) {
                const UINT sub=D3D11CalcSubresource(level,face,levels);D3D11_MAPPED_SUBRESOURCE mapped{};
                if(FAILED(dc->Map(capture.staging[kind].Get(),sub,D3D11_MAP_READ,D3D11_MAP_FLAG_DO_NOT_WAIT,&mapped))){error="Environment nonblocking staging map";return false;}
                const uint32_t count=size*size*components,at=kind==1?offset+face*count:offset;
                for(uint32_t y=0;y<size;y++)std::memcpy(values.data()+at+y*size*components,static_cast<const uint8_t*>(mapped.pData)+static_cast<size_t>(y)*mapped.RowPitch,size*components*4);
                dc->Unmap(capture.staging[kind].Get(),sub);offset+=6*count;size/=2;
            }
        }
        return true;
    }
};
}
