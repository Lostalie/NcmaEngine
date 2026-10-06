#pragma once
#include "contracts/NcmaUiRender.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <vector>
#include <unordered_map>
#include <memory>
#include <string>

namespace NcmaEngine::Rendering {
class UiKernel final {
    template<typename T> using Ptr=Microsoft::WRL::ComPtr<T>;
    ID3D11Device* device;ID3D11DeviceContext* context;
    Ptr<ID3D11VertexShader> vs;Ptr<ID3D11PixelShader> ps;Ptr<ID3D11InputLayout> layout;
    Ptr<ID3D11Buffer> constants;Ptr<ID3D11RasterizerState> raster;Ptr<ID3D11BlendState> blend;
    Ptr<ID3D11DepthStencilState> depth;Ptr<ID3D11SamplerState> sampler;
    struct Image {Ptr<ID3D11ShaderResourceView> srv;uint64_t bytes=0,pins=0;};
    struct List {Ptr<ID3D11Buffer> vertices;std::vector<NcmaUiBatchV1> batches;uint64_t bytes=0;};
    std::unordered_map<uint64_t,std::unique_ptr<Image>> images;
    std::unordered_map<uint64_t,std::unique_ptr<List>> lists;
    uint64_t next=1,bytes=0,uploaded=0,draws=0,submits=0;
public:
    UiKernel(ID3D11Device* d,ID3D11DeviceContext* c):device(d),context(c){}
    bool Initialize(std::string&);
    bool CanImage(const NcmaUiImageV1&) const;
    bool CanList(const NcmaUiListV1&,uint64_t) const;
    bool CreateImage(const NcmaUiImageV1&,uint64_t&,std::string&);
    bool CreateList(const NcmaUiListV1&,uint64_t&,std::string&);
    bool Contains(uint64_t key) const {return images.contains(key)||lists.contains(key);}
    bool HasList(uint64_t key) const {return lists.contains(key);}
    bool Pinned(uint64_t key) const {auto it=images.find(key);return it!=images.end()&&it->second->pins!=0;}
    void Destroy(uint64_t);
    void Draw(uint64_t,uint32_t,uint32_t);
    NcmaUiStatsV1 Stats(uint64_t generation,bool pure) const;
};
}
