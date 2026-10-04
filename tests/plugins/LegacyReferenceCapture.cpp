#include "EditorApplication.h"
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
#include <fstream>
#include <iostream>
#include <stdexcept>
namespace NcmaEngine::Editor {
int RunLegacyReferenceFixture(EditorApplication& app,const std::filesystem::path& output) {
    if(!app.Initialize())throw std::runtime_error("Legacy reference initialization failed.");
    app.CancelInspectorEdit();
    app.m_Scene.SetEditorTransform(app.m_Scene.GetPersistentId(app.m_SelectedObject),Transform{}); // Fixed identity pose fixture, no rotation/Play.
    app.m_ShowAnimationLab=false;app.m_ShowFbxCharacter=false;
    app.m_PreviewDraw.ViewportWidth=256;app.m_PreviewDraw.ViewportHeight=256;
    app.m_ToneMapDraw.ViewportX=0;app.m_ToneMapDraw.ViewportY=0;
    app.m_ToneMapDraw.ViewportWidth=256;app.m_ToneMapDraw.ViewportHeight=256;
    std::string error;
    if(!app.m_Renderer->BeginFrame(error))throw std::runtime_error(error);
    app.DrawViewportPreview(); // Unmodified legacy resource/shader/graph drawing path.
    auto& dx=dynamic_cast<Rhi::D3D11RenderBackend&>(*app.m_Renderer);
    Microsoft::WRL::ComPtr<ID3D11Texture2D> source,target;
    if(FAILED(dx.GetSwapChain()->GetBuffer(0,IID_PPV_ARGS(&source))))throw std::runtime_error("Legacy readback source.");
    D3D11_TEXTURE2D_DESC desc{};source->GetDesc(&desc);
    desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    if(FAILED(dx.GetDevice()->CreateTexture2D(&desc,nullptr,&target)))throw std::runtime_error("Legacy staging.");
    auto* context=dx.GetDeviceContext();context->CopyResource(target.Get(),source.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if(FAILED(context->Map(target.Get(),0,D3D11_MAP_READ,0,&mapped)))throw std::runtime_error("Legacy map.");
    std::filesystem::create_directories(output.parent_path());
    std::ofstream file(output,std::ios::binary|std::ios::trunc);
    for(uint32_t y=0;y<256;++y)file.write(static_cast<const char*>(mapped.pData)+y*mapped.RowPitch,256*4);
    context->Unmap(target.Get(),0);
    if(!file)throw std::runtime_error("Legacy image output failed.");
    return 0;
}
}
int main(int argc,char** argv) {
    try {
        if(argc!=2)return 2;
        NcmaEngine::Editor::EditorApplication app(GetModuleHandleW(nullptr),NcmaEngine::Rhi::BackendType::Direct3D11,true);
        return NcmaEngine::Editor::RunLegacyReferenceFixture(app,std::filesystem::path(argv[1]));
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
