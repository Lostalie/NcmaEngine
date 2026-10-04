// White-box fault injection is restricted to this test TU; no production fault/debug export.
#include "../../engine/source/plugins/renderer/RendererPlugin.cpp"
#include "../../engine/source/plugins/contracts/NcmaPlatform.h"
#include <iostream>
#include <filesystem>
static void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
int main(int argc,char** argv) {
    try{
        if(argc!=2)return 2;
        const auto path=std::filesystem::path(argv[1]);
        HMODULE library=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
        Check(library!=nullptr,"Platform load failed.");
        auto get=reinterpret_cast<NcmaGetApiV1>(GetProcAddress(library,"ncma_plugin_get_api"));
        NcmaErrorV1 error{};NcmaPlatformApiV1 platform{};NcmaRendererApiV1 api{};
        Check(get && get(1,0,&platform,sizeof(platform),&error)==0,"Platform API");
        Check(ncma_plugin_get_api(1,0,&api,sizeof(api),&error)==0,"Renderer API");
        NcmaRendererApiV1_1 modern{};
        Check(ncma_plugin_get_api(1,1,&modern,sizeof(modern),&error)==NCMA_OK && modern.base.module.minor==1 && modern.configure_reference,"Renderer 1.1 reference controls");
        Check(ncma_plugin_get_api(1,2,&modern,sizeof(modern),&error)==NCMA_ABI_MISMATCH,"Unknown renderer minor");
        uint64_t platformModule=0,renderModule=0,window=0,handle=0,group=0;
        Check(platform.module.initialize(nullptr,0,&platformModule,&error)==0,"Platform init");
        Check(api.module.initialize(nullptr,0,&renderModule,&error)==0,"Renderer init");
        const uint8_t title[]="Renderer native faults";
        NcmaWindowDescriptionV1 windowDesc{sizeof(windowDesc),256,256,0,title,sizeof(title)-1,0};
        Check(platform.create_window(platformModule,&windowDesc,&window,&error)==0,"Window create");
        NcmaRendererDescriptionV1 desc{sizeof(desc),1,1,0,platformModule,window,256,256,{0,0}};
        Check(api.create_renderer(renderModule,&desc,&handle,&error)==0,"Renderer create");
        uint32_t foreign=0;
        std::thread thread([&]{NcmaRendererStatsV1 status{};NcmaErrorV1 threadError{};foreign=api.stats(renderModule,handle,&status,&threadError);});thread.join();
        Check(foreign==NCMA_WRONG_THREAD,"Wrong thread rejected");
        Rhi::GraphicsPipelineDescription shader{};
        shader.VertexShaderSource="syntax_error";shader.PixelShaderSource="syntax_error";
        shader.VertexLayout.push_back({"POSITION",0,Rhi::VertexFormat::Float3,0});
        std::string diagnostic;
        Check(!renderer->backend->CreateGraphicsPipeline(shader,diagnostic),"Invalid shader unexpectedly compiled.");
        Check(!diagnostic.empty(),"Shader compilation diagnostic missing.");
        // Remove intentionally generated compile diagnostic from validation before successful draw.
        renderer->validation->ClearStoredMessages();
        Check(api.create_resources(renderModule,handle,&group,&error)==0,"Reference resource create");
        NcmaRenderFrameV1 frame{sizeof(frame),3,1,{},{0,0,256,256},1,.35F,.28F,0};
        for(int i=0;i<16;++i)frame.model[i]=i%5==0?1.0F:0.0F;
        NcmaRenderPassV1 passes[3]{{1,1,group,{}},{2,1,group,{}},{3,2,group,{}}};
        auto invalid=frame;invalid.pass_count=65;
        Check(api.submit(renderModule,handle,&invalid,passes,&error)==NCMA_INVALID_ARGUMENT,"Pass budget");
        passes[0].resources=handle;
        Check(api.submit(renderModule,handle,&frame,passes,&error)==NCMA_INVALID_HANDLE,"Wrong type handle");
        passes[0].resources=group;passes[2].shader_contract=1;
        Check(api.submit(renderModule,handle,&frame,passes,&error)==NCMA_INVALID_ARGUMENT,"Shader contract");
        passes[2].shader_contract=2;
        Check(api.submit(renderModule,handle,&frame,passes,&error)==0,"Submit");
        Check(api.destroy_resources(renderModule,handle,group,&error)==NCMA_BUSY,"Active frame retained");
        Check(api.present(renderModule,handle,&error)==0,"Present");
        Check(api.wait_idle(renderModule,handle,&error)==0,"Wait GPU");
        try {ExecutionScope scope;throw std::runtime_error("Injected post-validation execution failure.");}catch(const std::runtime_error&){}
        frame.frame=2;
        Check(api.submit(renderModule,handle,&frame,passes,&error)==NCMA_INTERNAL_ERROR,"Execution exception fail-stop");
        renderer->failed=true;renderer->faultResult=NCMA_DEVICE_LOST;renderer->stats.state=2; // Device-lost state machine injection, not a driver reset.
        frame.frame=2;
        Check(api.submit(renderModule,handle,&frame,passes,&error)==NCMA_DEVICE_LOST,"Lost submit fail-stop");
        Check(api.present(renderModule,handle,&error)==NCMA_DEVICE_LOST,"Lost present fail-stop");
        Check(api.resize(renderModule,handle,300,300,&error)==NCMA_DEVICE_LOST,"Lost resize fail-stop");
        Check(api.destroy_resources(renderModule,handle,group,&error)==0,"Faulted release");
        Check(api.destroy_resources(renderModule,handle,group,&error)==NCMA_INVALID_HANDLE,"Stale group");
        Check(renderer->backend->GetLiveResourceCount()==0,"Native GPU resource maps leaked.");
        Check(api.destroy_renderer(renderModule,handle,&error)==0,"Destroy");
        Check(api.stats(renderModule,handle,nullptr,&error)==NCMA_INVALID_HANDLE,"Stale renderer");
        Check(api.module.shutdown(renderModule,&error)==0,"Renderer shutdown");
        Check(platform.destroy_window(platformModule,window,&error)==0,"Window destroy");
        Check(platform.module.shutdown(platformModule,&error)==0,"Platform shutdown");
        FreeLibrary(library);
        std::cout<<"Native render ABI bounds/thread/shader rejection and injected fault state passed; no real device reset performed.\n";return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
