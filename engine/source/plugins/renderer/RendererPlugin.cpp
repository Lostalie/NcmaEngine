#include "../contracts/NcmaRenderer.h"
#include "../PluginSupport.h"
#include "../platform/PlatformPrivate.h"
#include "RendererPrivate.h"
#include "ReferenceKernel.h"
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
#define GLFW_EXPOSE_NATIVE_WIN32
#include <GLFW/glfw3.h>
#include <GLFW/glfw3native.h>
#include <stdexcept>
#include <d3d11sdklayers.h>
#include <array>
#include <chrono>
#include <cmath>
#include <memory>
#include <unordered_map>
using namespace NcmaEngine;
using Microsoft::WRL::ComPtr;
namespace {
const auto loadingThread=std::this_thread::get_id();
uint64_t module=0,nextModule=1,nextRenderer=1,nextGroup=1;
bool busy=false;
struct Renderer {
    uint64_t handle=0,platform=0,window=0,borrows=0,lastFrame=0;
    bool active=false,failed=false,vsync=false;
    uint32_t faultResult=NCMA_INTERNAL_ERROR;
    std::unique_ptr<Rhi::D3D11RenderBackend> backend;
    std::unordered_map<uint64_t,std::unique_ptr<Rendering::ReferenceKernel>> groups;
    ComPtr<ID3D11InfoQueue> validation;
    ComPtr<ID3D11Query> gpuDisjoint,gpuBegin,gpuEnd;
    bool measuring=false,timingPending=false;
    std::string diagnostics;
    NcmaRendererStatsV1 stats{sizeof(NcmaRendererStatsV1)};
};
std::unique_ptr<Renderer> renderer;
struct BusyScope { BusyScope(){busy=true;} ~BusyScope(){busy=false;} };
uint32_t Validate(uint64_t context,NcmaErrorV1* error) {
    if(std::this_thread::get_id()!=loadingThread) return NcmaPlugin::Error(error,NCMA_WRONG_THREAD);
    if(!module||context!=module) return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
    if(busy) return NcmaPlugin::Error(error,NCMA_BUSY);
    return NCMA_OK;
}
uint32_t Instance(uint64_t context,uint64_t handle,NcmaErrorV1* error) {
    auto valid=Validate(context,error); if(valid) return valid;
    if(!renderer||renderer->handle!=handle) return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
    return NCMA_OK;
}
uint32_t Failure(NcmaErrorV1* error,const std::string& message) {
    renderer->failed=true; renderer->stats.state=2;
    const bool lost=FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason());
    renderer->faultResult=lost?NCMA_DEVICE_LOST:NCMA_INTERNAL_ERROR;
    return NcmaPlugin::Error(error,renderer->faultResult,message);
}
bool Wait(Renderer& r,std::string& error) {
    auto* device=r.backend->GetDevice(); auto* context=r.backend->GetDeviceContext();
    if(FAILED(device->GetDeviceRemovedReason())) { error="Device lost."; return false; }
    D3D11_QUERY_DESC desc{D3D11_QUERY_EVENT,0}; ComPtr<ID3D11Query> query;
    if(FAILED(device->CreateQuery(&desc,&query))) {error="GPU completion query failed.";return false;}
    context->End(query.Get()); context->Flush();
    const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(2);
    for(;;) {
        const HRESULT result=context->GetData(query.Get(),nullptr,0,0);
        if(result==S_OK) return true;
        if(FAILED(result)) {error="GPU completion failed.";return false;}
        if(std::chrono::steady_clock::now()>=deadline) {error="GPU completion timed out; resources retained.";return false;}
        std::this_thread::yield();
    }
}
struct ExecutionScope {
    bool success=false;
    ~ExecutionScope() noexcept {
        if(success || !renderer)return;
        renderer->failed=true;renderer->stats.state=2;renderer->active=false;
        auto* dc=renderer->backend->GetDeviceContext();
        if(renderer->measuring) {
            dc->End(renderer->gpuEnd.Get());dc->End(renderer->gpuDisjoint.Get());
            renderer->measuring=false;renderer->timingPending=true;
        }
        dc->ClearState();
    }
};
void ResolveTiming(Renderer& r) {
    if(!r.timingPending)return;
    D3D11_QUERY_DATA_TIMESTAMP_DISJOINT data{};UINT64 begin=0,end=0;
    auto* dc=r.backend->GetDeviceContext();
    if(dc->GetData(r.gpuDisjoint.Get(),&data,sizeof(data),D3D11_ASYNC_GETDATA_DONOTFLUSH)!=S_OK ||
       dc->GetData(r.gpuBegin.Get(),&begin,sizeof(begin),D3D11_ASYNC_GETDATA_DONOTFLUSH)!=S_OK ||
       dc->GetData(r.gpuEnd.Get(),&end,sizeof(end),D3D11_ASYNC_GETDATA_DONOTFLUSH)!=S_OK)return;
    r.stats.gpu_sample_valid=!data.Disjoint&&data.Frequency&&end>=begin?1u:0u;
    if(r.stats.gpu_sample_valid)r.stats.gpu_ms=static_cast<double>(end-begin)*1000.0/static_cast<double>(data.Frequency);
    r.timingPending=false;
}
void Validation(Renderer& r) {
    if(!r.validation) return;
    const auto count=r.validation->GetNumStoredMessagesAllowedByRetrievalFilter();
    for(UINT64 i=0;i<count;++i) {
        SIZE_T size=0; if(FAILED(r.validation->GetMessage(i,nullptr,&size))||size>65536) continue;
        std::vector<uint8_t> data(size);
        auto* message=reinterpret_cast<D3D11_MESSAGE*>(data.data());
        if(FAILED(r.validation->GetMessage(i,message,&size))) continue;
        if(message->Severity<=D3D11_MESSAGE_SEVERITY_WARNING && r.diagnostics.size()<60000) {
            r.diagnostics.append(message->pDescription,std::min<size_t>(message->DescriptionByteLength ? message->DescriptionByteLength-1 : 0,2048)); r.diagnostics += '\n';
        }
        if(message->Severity<=D3D11_MESSAGE_SEVERITY_ERROR) r.stats.validation_errors++;
        else if(message->Severity==D3D11_MESSAGE_SEVERITY_WARNING) r.stats.validation_warnings++;
    }
    r.validation->ClearStoredMessages();
}
uint32_t NCMA_CALL Initialize(const uint8_t* input,uint32_t length,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        if(std::this_thread::get_id()!=loadingThread) return NcmaPlugin::Error(error,NCMA_WRONG_THREAD);
        if(!output||input||length) return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(module) return NcmaPlugin::Error(error,NCMA_BUSY);
        module=0x524D000000000000ull|nextModule++; *output=module;return NCMA_OK;
    });
}
uint32_t NCMA_CALL Shutdown(uint64_t context,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Validate(context,error);if(valid)return valid;
        if(renderer)return NcmaPlugin::Error(error,NCMA_BUSY); module=0;return NCMA_OK;});
}
uint32_t NCMA_CALL Status(uint64_t context,NcmaModuleStatusV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Validate(context,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output={sizeof(*output),renderer&&renderer->failed?2u:1u,renderer?1+renderer->groups.size():0,0,renderer?renderer->stats.presents:0};return NCMA_OK;});
}
uint32_t NCMA_CALL Diagnostic(uint64_t context,uint8_t* output,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Validate(context,error);if(valid)return valid;
        if(!required || (capacity && !output)) return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto message=renderer?std::string_view(renderer->diagnostics):std::string_view{};
        *required=static_cast<uint32_t>(message.size());
        if(capacity<message.size()) return NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);
        if(!message.empty())std::memcpy(output,message.data(),message.size());return NCMA_OK;});
}
uint32_t NCMA_CALL Create(uint64_t context,const NcmaRendererDescriptionV1* desc,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Validate(context,error);if(valid)return valid;
        if(!desc||!output||desc->struct_size!=sizeof(*desc)||desc->validation>1||desc->vsync>1||
            desc->reserved[0]||desc->reserved[1]||desc->width==0||desc->height==0||desc->width>4096||desc->height>4096)
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(desc->backend!=1)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Only DX11 reference operation set is implemented.");
        if(renderer)return NcmaPlugin::Error(error,NCMA_BUSY);
        auto candidate=std::make_unique<Renderer>();
        GLFWwindow* window=nullptr;
        valid=ncma_platform_borrow_window_v1(desc->platform_module,desc->window,&window,error);if(valid)return valid;
        BusyScope scope;
        try {
            candidate->backend=std::make_unique<Rhi::D3D11RenderBackend>();
            Rhi::BackendCreateInfo info{glfwGetWin32Window(window),desc->width,desc->height,desc->validation!=0,desc->vsync!=0};
            std::string message;
            if(!candidate->backend->Initialize(info,message)) throw std::runtime_error(message);
            if(desc->validation && FAILED(candidate->backend->GetDevice()->QueryInterface(IID_PPV_ARGS(&candidate->validation))))
                throw std::runtime_error("Requested DX11 validation unavailable; no fallback.");
            ComPtr<IDXGIDevice> dxgiDevice; ComPtr<IDXGIAdapter> adapter; DXGI_ADAPTER_DESC adapterDesc{};
            LARGE_INTEGER driver{};
            if(SUCCEEDED(candidate->backend->GetDevice()->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) &&
               SUCCEEDED(dxgiDevice->GetAdapter(&adapter)) && SUCCEEDED(adapter->GetDesc(&adapterDesc))) {
                (void)adapter->CheckInterfaceSupport(__uuidof(IDXGIDevice),&driver);
                char hardware[512]{};
                const int count=WideCharToMultiByte(CP_UTF8,0,adapterDesc.Description,-1,hardware,512,nullptr,nullptr);
                if(count>0)candidate->diagnostics=std::string(hardware)+ "; driver=" + std::to_string(driver.QuadPart) +
                    "; feature_level=" + std::to_string(candidate->backend->GetDevice()->GetFeatureLevel()) +
                    "; validation=" + std::to_string(desc->validation) + "; vsync=" + std::to_string(desc->vsync) + "\n";
            }
            D3D11_QUERY_DESC queryDesc{D3D11_QUERY_TIMESTAMP_DISJOINT,0};
            if(FAILED(candidate->backend->GetDevice()->CreateQuery(&queryDesc,&candidate->gpuDisjoint))) throw std::runtime_error("Timestamp disjoint query failed.");
            queryDesc.Query=D3D11_QUERY_TIMESTAMP;
            if(FAILED(candidate->backend->GetDevice()->CreateQuery(&queryDesc,&candidate->gpuBegin)) || FAILED(candidate->backend->GetDevice()->CreateQuery(&queryDesc,&candidate->gpuEnd))) throw std::runtime_error("Timestamp query failed.");
            candidate->platform=desc->platform_module;candidate->window=desc->window;candidate->vsync=desc->vsync!=0;
            candidate->stats.width=desc->width;candidate->stats.height=desc->height;candidate->stats.state=1;
            candidate->handle=0x5244000000000000ull|nextRenderer++;
            *output=candidate->handle;renderer=std::move(candidate);return NCMA_OK;
        } catch(...) { NcmaErrorV1 ignored{}; (void)ncma_platform_release_window_v1(desc->platform_module,desc->window,&ignored); throw; }
    });
}
uint32_t NCMA_CALL CreateResources(uint64_t context,uint64_t handle,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active||renderer->groups.size()>=8)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;auto group=std::make_unique<Rendering::ReferenceKernel>(*renderer->backend);std::string message;
        if(!group->Initialize(message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        const auto id=0x5253000000000000ull|nextGroup++;
        renderer->groups.emplace(id,std::move(group));*output=id;return NCMA_OK;});
}
uint32_t NCMA_CALL DestroyResources(uint64_t context,uint64_t handle,uint64_t group,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!renderer->groups.contains(group))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;
        // Device loss permits releasing resources only after unbinding, no recovery promised.
        if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
        renderer->backend->GetDeviceContext()->ClearState(); renderer->groups.erase(group);return NCMA_OK;});
}
uint32_t NCMA_CALL ConfigureReference(uint64_t context,uint64_t handle,uint64_t group,const NcmaReferenceSettingsV1* input,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        if(!input || !renderer->groups.contains(group))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto s=*input;
        auto range=[](float v,float min,float max){return std::isfinite(v)&&v>=min&&v<=max;};
        if(s.struct_size!=sizeof(s)||s.reserved||s.shadow_enabled>1||s.shadow_filter>2||s.contact_enabled>1||s.contact_steps<4||s.contact_steps>32||
            !range(s.base_red,0,1)||!range(s.base_green,0,1)||!range(s.base_blue,0,1)||!range(s.light_intensity,0,20)||!range(s.ambient,0,.25F)||
            !range(s.constant_bias,0,.01F)||!range(s.slope_bias,0,8)||!range(s.max_distance,5,100)||!range(s.cascade_lambda,0,1)||
            !range(s.light_radius,.001F,.2F)||!range(s.contact_distance,.05F,3)||!range(s.contact_thickness,.005F,.3F)||!range(s.contact_strength,0,1))
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        renderer->groups.at(group)->Configure(s);return NCMA_OK;
    });
}
uint32_t NCMA_CALL Submit(uint64_t context,uint64_t handle,const NcmaRenderFrameV1* frame,const NcmaRenderPassV1* passes,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(!frame)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const NcmaRenderFrameV1 copiedFrame=*frame;frame=&copiedFrame;
        if(!frame||!passes||frame->struct_size!=sizeof(*frame)||frame->pass_count==0||frame->pass_count>64||
           frame->frame<=renderer->lastFrame||frame->reserved!=0)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        for(float v:frame->model)if(!std::isfinite(v))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float v:frame->viewport)if(!std::isfinite(v))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(frame->viewport[0]<0||frame->viewport[1]<0||frame->viewport[2]<1||frame->viewport[3]<1||
           frame->viewport[0]+frame->viewport[2]>renderer->stats.width||frame->viewport[1]+frame->viewport[3]>renderer->stats.height||
           !std::isfinite(frame->exposure)||frame->exposure<0.01F||frame->exposure>16||
           !std::isfinite(frame->metallic)||frame->metallic<0||frame->metallic>1||
           !std::isfinite(frame->roughness)||frame->roughness<0.04F||frame->roughness>1)
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        std::array<NcmaRenderPassV1,64> copied{};std::copy_n(passes,frame->pass_count,copied.begin());
        std::unordered_map<uint64_t,uint32_t> initialized;
        bool output=false;
        for(uint32_t i=0;i<frame->pass_count;++i) {
            const auto& p=copied[i];
            if(p.operation<1||p.operation>4)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE);
            if(p.operation==4) {
                if(p.resources||p.shader_contract)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
                for(float v:p.color)if(!std::isfinite(v)||v<0||v>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
                output=true;continue;
            }
            if(!renderer->groups.contains(p.resources))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
            for(float v:p.color) if(!std::isfinite(v)) return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            if(p.operation==3 && p.color[0]!=0 && (p.color[0]<0.01F || p.color[0]>16)) return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            if(p.shader_contract!=(p.operation==3?2u:1u))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Shader contract mismatch.");
            if(p.operation==1)initialized[p.resources]|=1;
            else if(p.operation==2) {if(!(initialized[p.resources]&1))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Uninitialized shadow."); initialized[p.resources]|=2;}
            else {if(!(initialized[p.resources]&2))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Uninitialized HDR/depth.");output=true;}
        }
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Missing output.");
        BusyScope scope;std::string message;const auto started=std::chrono::steady_clock::now();
        ExecutionScope execution;
        // Prepare resource groups once, never a per-object managed/native call.
        for(const auto& [id,state]:initialized) {
            (void)state;
            if(!renderer->groups.at(id)->Prepare(static_cast<uint32_t>(frame->viewport[2]),static_cast<uint32_t>(frame->viewport[3]),
                frame->model,frame->exposure,frame->metallic,frame->roughness,message))return Failure(error,message);
        }
        ResolveTiming(*renderer);
        if(!renderer->timingPending) {
            auto* dc=renderer->backend->GetDeviceContext();dc->Begin(renderer->gpuDisjoint.Get());dc->End(renderer->gpuBegin.Get());renderer->measuring=true;
        }
        if(!renderer->backend->BeginFrame(message))return Failure(error,message);
        for(uint32_t i=0;i<frame->pass_count;++i) {
            const auto& p=copied[i];bool ok=false;
            if(p.operation==4) {Rhi::RenderPassDescription desc{};desc.ClearColor=true;std::copy_n(p.color,4,desc.ClearColorValue);
                ok=renderer->backend->BeginRenderPass(desc,message);renderer->backend->EndRenderPass();}
            else {
                auto& group=*renderer->groups.at(p.resources);
                if(p.operation==1)ok=group.Shadow(message);
                else if(p.operation==2)ok=group.Geometry(message);
                else ok=group.ToneMap(p.color[0],frame->viewport[0],frame->viewport[1],frame->viewport[2],frame->viewport[3],message);
            }
            if(!ok)return Failure(error,message);
        }
        renderer->active=true;renderer->lastFrame=frame->frame;renderer->stats.submitted_frames++;
        renderer->stats.submit_ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();
        Validation(*renderer);execution.success=true;return NCMA_OK;
    });
}
uint32_t NCMA_CALL Present(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(!renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;const auto started=std::chrono::steady_clock::now();
        if(renderer->measuring) {
            auto* dc=renderer->backend->GetDeviceContext();dc->End(renderer->gpuEnd.Get());dc->End(renderer->gpuDisjoint.Get());
            renderer->measuring=false;renderer->timingPending=true;
        }
        const HRESULT result=renderer->backend->GetSwapChain()->Present(renderer->vsync?1:0,0);
        renderer->active=false;
        if(FAILED(result))return Failure(error,"Present failed.");
        renderer->stats.presents++;renderer->stats.present_ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();
        Validation(*renderer);return NCMA_OK;});
}
uint32_t NCMA_CALL Resize(uint64_t context,uint64_t handle,uint32_t width,uint32_t height,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!width||!height||width>4096||height>4096)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        if(width==renderer->stats.width&&height==renderer->stats.height)return NCMA_OK;
        BusyScope scope;std::string message;
        if(!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
        if(!renderer->backend->ResizeChecked(width,height,message))return Failure(error,message);
        renderer->stats.width=width;renderer->stats.height=height;Validation(*renderer);return NCMA_OK;});
}
uint32_t NCMA_CALL Stats(uint64_t context,uint64_t handle,NcmaRendererStatsV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        ResolveTiming(*renderer);Validation(*renderer);renderer->stats.live_groups=renderer->groups.size();*output=renderer->stats;return NCMA_OK;});
}
uint32_t NCMA_CALL WaitIdle(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        BusyScope scope;std::string message;
        if(!Wait(*renderer,message))return NcmaPlugin::Error(error,FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?NCMA_DEVICE_LOST:NCMA_SHUTDOWN_TIMEOUT,message);
        return NCMA_OK;});
}
uint32_t NCMA_CALL Destroy(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->borrows||!renderer->groups.empty()||renderer->backend->GetLiveResourceCount()!=0)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;
        if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
        NcmaErrorV1 platformError{};
        valid=ncma_platform_release_window_v1(renderer->platform,renderer->window,&platformError);if(valid){*error=platformError;return valid;}
        renderer->backend->Shutdown();renderer.reset();return NCMA_OK;});
}
uint32_t NCMA_CALL Capture(uint64_t context,uint64_t handle,uint8_t* output,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!required)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *required=renderer->stats.width*renderer->stats.height*4;
        if(!output||capacity<*required){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=*required;return NCMA_BUFFER_TOO_SMALL;}
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(!renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;ComPtr<ID3D11Texture2D> source,target;auto* device=renderer->backend->GetDevice();auto* dc=renderer->backend->GetDeviceContext();
        if(FAILED(renderer->backend->GetSwapChain()->GetBuffer(0,IID_PPV_ARGS(&source))))return Failure(error,"Readback source failed.");
        D3D11_TEXTURE2D_DESC desc{};source->GetDesc(&desc);desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&target)))return Failure(error,"Readback allocation failed.");
        dc->CopyResource(target.Get(),source.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(dc->Map(target.Get(),0,D3D11_MAP_READ,0,&mapped)))return Failure(error,"Readback map failed.");
        for(uint32_t y=0;y<desc.Height;++y)std::memcpy(output+y*desc.Width*4,static_cast<const uint8_t*>(mapped.pData)+y*mapped.RowPitch,desc.Width*4);
        dc->Unmap(target.Get(),0);Validation(*renderer);return NCMA_OK;});
}
}
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_borrow_dx11_v1(uint64_t context,uint64_t handle,ID3D11Device** device,ID3D11DeviceContext** dc,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!device||!dc)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        *device=renderer->backend->GetDevice();*dc=renderer->backend->GetDeviceContext();renderer->borrows++;return NCMA_OK;});
}
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_release_dx11_v1(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!renderer->borrows)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        renderer->borrows--;return NCMA_OK;});
}
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_validate_gui_frame_v1(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(!renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        return NCMA_OK;});
}
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major,uint32_t minor,void* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    const NcmaRendererApiV1 table{{sizeof(NcmaRendererApiV1),1,0,NCMA_RENDERER,15,Initialize,Shutdown,Status,Diagnostic},
        Create,CreateResources,DestroyResources,Submit,Present,Resize,Stats,WaitIdle,Destroy,Capture};
    if(minor==0)return NcmaPlugin::CopyApi(major,minor,output,capacity,error,table);
    NcmaRendererApiV1_1 modern{table,ConfigureReference}; modern.base.module.struct_size=sizeof(modern);modern.base.module.minor=1;
    return NcmaPlugin::CopyApi(major,minor,output,capacity,error,modern,1);
}
