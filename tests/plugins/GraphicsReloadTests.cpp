// This executable deliberately links NO engine DLL. Only POD tokens survive unload.
#include <Windows.h>
#include "contracts/NcmaPlatform.h"
#include "contracts/NcmaRenderer.h"
#include "contracts/NcmaGui.h"
#include "contracts/NcmaText.h"
#include "contracts/NcmaUiTarget.h"
#include <array>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>

static void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
struct Tokens { uint64_t platform{}, renderer{}, gui{}, text{}, window{}, device{}, group{}, guiInstance{}, font{}, run{}; NcmaUiKeyV1 target{},image{},list{},lease{}; };
struct Generation {
    std::array<HMODULE, 4> libraries{};
    NcmaPlatformApiV1 platform{}; NcmaRendererApiV1_2 renderer{};
    NcmaGuiApiV1 gui{}; NcmaTextApiV1 text{}; NcmaUiTargetApiV1 targets{};
    NcmaUiApiV1 ui{};
    NcmaErrorV1 error{}; Tokens tokens{};
    void Open(const std::array<std::filesystem::path, 4>& paths,const std::vector<uint8_t>& font) {
        for (size_t i=0;i<paths.size();++i) {
            Check(GetModuleHandleW(paths[i].filename().c_str())==nullptr,"Engine DLL was already loaded before test generation");
            libraries[i]=LoadLibraryExW(paths[i].c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
            Check(libraries[i]!=nullptr,"Load exact graphics DLL");
        }
        auto table=[&](size_t i,uint32_t minor,void* output,uint32_t size) {
            auto get=reinterpret_cast<NcmaGetApiV1>(GetProcAddress(libraries[i],"ncma_plugin_get_api"));
            Check(get&&get(1,minor,output,size,&error)==NCMA_OK,"Fresh function table");
        };
        table(0,0,&platform,sizeof(platform));table(1,2,&renderer,sizeof(renderer));
        table(2,0,&gui,sizeof(gui));table(3,0,&text,sizeof(text));
        auto& r=renderer.base.base;
        Check(platform.module.initialize(nullptr,0,&tokens.platform,&error)==NCMA_OK,"Platform init");
        Check(r.module.initialize(nullptr,0,&tokens.renderer,&error)==NCMA_OK,"Renderer init");
        Check(gui.module.initialize(nullptr,0,&tokens.gui,&error)==NCMA_OK,"GUI init");
        Check(text.module.initialize(nullptr,0,&tokens.text,&error)==NCMA_OK,"Text init");
        const uint8_t title[]="Ncma actual DLL reload";
        NcmaWindowDescriptionV1 w{sizeof(w),64,64,0,title,sizeof(title)-1,0};
        Check(platform.create_window(tokens.platform,&w,&tokens.window,&error)==NCMA_OK,"Window create");
        NcmaRendererDescriptionV1 d{sizeof(d),1,1,0,tokens.platform,tokens.window,64,64,{0,0}};
        Check(r.create_renderer(tokens.renderer,&d,&tokens.device,&error)==NCMA_OK,"Renderer create");
        Check(r.create_resources(tokens.renderer,tokens.device,&tokens.group,&error)==NCMA_OK,"Reference group create");
        Check(renderer.query_scene_render(tokens.renderer,7,&targets,sizeof(targets),&error)==NCMA_OK,"Target API");
        NcmaUiTargetDescriptionV1 target{sizeof(target),8,8,0};
        Check(targets.create_target(tokens.renderer,tokens.device,&target,&tokens.target,&error)==NCMA_OK,"Target create");
        NcmaGuiDescriptionV1 g{};g.struct_size=sizeof(g);g.platform_module=tokens.platform;g.window=tokens.window;g.font_size=13;
        Check(gui.create_gui(tokens.gui,&g,&tokens.guiInstance,&error)==NCMA_OK,"GUI create");
        Check(text.create_font(tokens.text,font.data(),static_cast<uint32_t>(font.size()),&tokens.font,&error)==NCMA_OK,"Owned font create");
        const uint8_t content[]="Reload";NcmaTextRequestV1 request{sizeof(request),sizeof(content)-1,tokens.font,content,13,100,30,1,0,0};NcmaTextMetricsV1 metrics{};
        Check(text.prepare(tokens.text,&request,&tokens.run,&metrics,&error)==NCMA_OK,"Text run prepare");
        Check(renderer.query_scene_render(tokens.renderer,6,&ui,sizeof(ui),&error)==NCMA_OK,"UI API");
        const uint8_t rgba[]{255,255,255,255};NcmaUiImageV1 image{sizeof(image),1,1,4,rgba};
        Check(ui.create_image(tokens.renderer,tokens.device,&image,&tokens.image,&error)==NCMA_OK,"UI image create");
        std::array<NcmaUiVertexV1,3> vertices{};
        for(auto& v:vertices){v.color[0]=v.color[1]=v.color[2]=v.color[3]=1;v.extent[0]=v.extent[1]=1;}
        vertices[1].position[0]=1;vertices[2].position[1]=1;
        NcmaUiBatchV1 batch{0,3,tokens.image,{0,0,8,8}};
        NcmaUiListV1 list{sizeof(list),3,1,0,vertices.data(),&batch};
        Check(ui.create_list(tokens.renderer,tokens.device,&list,&tokens.list,&error)==NCMA_OK,"UI list create");
        NcmaUiTargetFrameV1 f{sizeof(f),0,1,1,tokens.target,tokens.list,{0,0,0,0}};
        Check(targets.submit(tokens.renderer,tokens.device,&f,&error)==NCMA_OK,"UI target produce");
        Check(targets.acquire(tokens.renderer,tokens.device,tokens.target,1,1,&tokens.lease,&error)==NCMA_OK,"UI lease acquire");
    }
    void Probe(const Tokens& old,std::vector<const char*>& accepted) {
        auto reject=[&](uint32_t result,const char* label) {
            if(result!=NCMA_INVALID_HANDLE) { accepted.push_back(label);std::cout<<"STALE ACCEPTED "<<label<<" result="<<result<<'\n'; }
        };
        NcmaModuleStatusV1 status{};NcmaRendererStatsV1 stats{};NcmaWindowStateV1 window{};
        reject(platform.module.get_status(old.platform,&status,&error),"platform context");
        reject(renderer.base.base.module.get_status(old.renderer,&status,&error),"renderer context");
        reject(gui.module.get_status(old.gui,&status,&error),"gui context");
        reject(text.module.get_status(old.text,&status,&error),"text context");
        reject(platform.get_window_state(tokens.platform,old.window,&window,&error),"window with fresh context");
        reject(renderer.base.base.stats(tokens.renderer,old.device,&stats,&error),"renderer with fresh context");
        reject(renderer.base.base.destroy_resources(tokens.renderer,tokens.device,old.group,&error),"old group with fresh owners");
        reject(gui.destroy_gui(tokens.gui,old.guiInstance,&error),"old GUI instance with fresh context");
        reject(text.release(tokens.text,old.run,&error),"old text run with fresh context");
        reject(text.release(tokens.text,old.font,&error),"old font with fresh context");
        // Capture rejects the old generation before examining target contents.
        std::array<uint8_t,256> pixels{};
        reject(targets.capture(tokens.renderer,tokens.device,old.target,pixels.data(),static_cast<uint32_t>(pixels.size()),&error),"UI target full token");
        reject(targets.capture(tokens.renderer,tokens.device,{old.target.value,tokens.device},pixels.data(),static_cast<uint32_t>(pixels.size()),&error),"old target value cannot be rebound to fresh generation");
        reject(ui.destroy(tokens.renderer,tokens.device,old.list,&error),"old UI list full token");
        reject(ui.destroy(tokens.renderer,tokens.device,{old.list.value,tokens.device},&error),"old UI list value");
        reject(ui.destroy(tokens.renderer,tokens.device,{old.image.value,tokens.device},&error),"old UI image value");
        reject(targets.release(tokens.renderer,tokens.device,{old.lease.value,tokens.device},&error),"old presentation lease value");
    }
    void Healthy() {
        auto& r=renderer.base.base;
        Check(targets.release(tokens.renderer,tokens.device,tokens.lease,&error)==NCMA_OK,"Fresh lease release");
        NcmaRenderFrameV1 frame{};frame.struct_size=sizeof(frame);frame.pass_count=1;frame.frame=1;
        frame.model[0]=frame.model[5]=frame.model[10]=frame.model[15]=1;
        frame.viewport[2]=frame.viewport[3]=64;frame.exposure=1;frame.roughness=.5f;
        NcmaRenderPassV1 pass{NCMA_RENDER_CLEAR,0,0,{.25f,.5f,.75f,1}};
        Check(r.submit(tokens.renderer,tokens.device,&frame,&pass,&error)==NCMA_OK,"Fresh renderer clear");
        std::vector<uint8_t> pixels(64*64*4);uint32_t required=0;
        Check(r.capture(tokens.renderer,tokens.device,pixels.data(),static_cast<uint32_t>(pixels.size()),&required,&error)==NCMA_OK&&required==pixels.size(),"Fresh GPU readback");
        Check(pixels[0]>=63&&pixels[0]<=64&&pixels[1]>=127&&pixels[1]<=128&&pixels[2]>=191&&pixels[2]<=192&&pixels[3]==255,"Fresh clear exact color");
        Check(r.present(tokens.renderer,tokens.device,&error)==NCMA_OK,"Fresh present");
        Check(r.wait_idle(tokens.renderer,tokens.device,&error)==NCMA_OK,"Fresh completion");
        NcmaRendererStatsV1 stats{};
        Check(r.stats(tokens.renderer,tokens.device,&stats,&error)==NCMA_OK&&stats.validation_errors==0&&stats.validation_warnings==0,"API validation 0/0");
    }
    void Close(const std::array<std::filesystem::path,4>& paths) {
        auto& r=renderer.base.base;
        Check(gui.destroy_gui(tokens.gui,tokens.guiInstance,&error)==NCMA_OK,"GUI close");
        Check(text.release(tokens.text,tokens.run,&error)==NCMA_OK,"Text run close");
        Check(text.release(tokens.text,tokens.font,&error)==NCMA_OK,"Font close");
        Check(ui.destroy(tokens.renderer,tokens.device,tokens.list,&error)==NCMA_OK,"UI list close");
        Check(ui.destroy(tokens.renderer,tokens.device,tokens.image,&error)==NCMA_OK,"UI image close");
        Check(targets.destroy_target(tokens.renderer,tokens.device,tokens.target,&error)==NCMA_OK,"Target close");
        Check(r.destroy_resources(tokens.renderer,tokens.device,tokens.group,&error)==NCMA_OK,"Group close");
        Check(r.destroy_renderer(tokens.renderer,tokens.device,&error)==NCMA_OK,"Renderer close");
        Check(platform.destroy_window(tokens.platform,tokens.window,&error)==NCMA_OK,"Window close");
        Check(text.module.shutdown(tokens.text,&error)==NCMA_OK,"Text shutdown");
        Check(gui.module.shutdown(tokens.gui,&error)==NCMA_OK,"GUI shutdown");
        Check(r.module.shutdown(tokens.renderer,&error)==NCMA_OK,"Renderer shutdown");
        Check(platform.module.shutdown(tokens.platform,&error)==NCMA_OK,"Platform shutdown");
        // Erase EVERY function pointer before actual unload; never call a stale code address.
        platform={};renderer={};gui={};text={};targets={};ui={};
        for(size_t i=libraries.size();i-->0;) {Check(FreeLibrary(libraries[i])!=0,"FreeLibrary");libraries[i]=nullptr;}
        for(const auto& path:paths) Check(GetModuleHandleW(path.filename().c_str())==nullptr,"Actual engine DLL unload");
    }
};
int main(int argc,char** argv) {
    try {
        if(argc!=6)return 2;
        std::array<std::filesystem::path,4> paths;
        for(size_t i=0;i<paths.size();++i)paths[i]=std::filesystem::canonical(argv[i+1]);
        const auto fontPath=std::filesystem::canonical(argv[5]);const auto size=std::filesystem::file_size(fontPath);
        Check(size>=12&&size<=32*1024*1024,"Bounded owned font fixture");
        std::ifstream file(fontPath,std::ios::binary);std::vector<uint8_t> font(static_cast<size_t>(size));
        Check(static_cast<bool>(file.read(reinterpret_cast<char*>(font.data()),static_cast<std::streamsize>(size))),"Read fixture font");file.close();
        std::vector<Tokens> history;std::vector<const char*> accepted;
        for(int cycle=0;cycle<3;++cycle) {
            Generation current;current.Open(paths,font);
            for(const auto& old:history)current.Probe(old,accepted);
            current.Healthy();history.push_back(current.tokens);current.Close(paths);
            std::cout<<"Generation "<<cycle+1<<": healthy GPU/API 0/0; all four DLLs actually unloaded\n";
        }
        Check(accepted.empty(),"Old raw handles accepted through fresh tables after actual DLL reload");
        std::cout<<"Graphics actual-DLL reload checks passed\n";return 0;
    } catch(const std::exception& error) {std::cerr<<error.what()<<'\n';return 1;}
}
