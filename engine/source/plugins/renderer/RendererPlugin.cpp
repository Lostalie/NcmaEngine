#include "ClosedShaderReference.h"
#include "../contracts/NcmaRenderer.h"
#include "../contracts/NcmaRendererFrame.h"
#include "../contracts/NcmaRendererFault.h"
#include "../contracts/NcmaRendererTiming.h"
#include "../contracts/NcmaRendererResourceLedger.h"
#include "../PluginSupport.h"
#include "../WindowsHandle.h"
#include "../platform/PlatformPrivate.h"
#include "RendererPrivate.h"
#include "ReferenceKernel.h"
#include "StaticMeshKernel.h"
#include "ResourceKernel.h"
#include "ScenePipelineKernel.h"
#include "SkinKernel.h"
#include "UiKernel.h"
#include "EnvironmentCookKernel.h"
#include "EnvironmentGpuKernel.h"
#include "../contracts/NcmaUiTarget.h"
#include "../contracts/NcmaShader.h"
#include "../contracts/NcmaShaderPipeline.h"
#include "../contracts/NcmaUiShaders.h"
#include "../contracts/NcmaRuntimeShaders.h"
#include "../contracts/NcmaEnvironmentScene.h"
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
#include <span>
#include <algorithm>
#include <bit>
#include <charconv>
#include <unordered_set>
#include <d3dcompiler.h>
#include <d3d11shader.h>
#include <Eigen/LU>
using namespace NcmaEngine;
using Microsoft::WRL::ComPtr;
namespace {
const auto loadingThread=std::this_thread::get_id();
uint64_t module=0;
bool busy=false;
struct TextureGpu {Rhi::D3D11RenderBackend& backend;Rhi::TextureHandle texture;uint32_t format=0;uint64_t bytes=0;
    explicit TextureGpu(Rhi::D3D11RenderBackend& b):backend(b){}~TextureGpu(){backend.DestroyTexture(texture);}};
struct MaterialGpu {NcmaMaterialDescriptionV3 description{};std::array<Rhi::TextureHandle,6> textures{};};
struct TargetGpu {Rhi::D3D11RenderBackend& backend;Rhi::TextureHandle color,depth;uint32_t width=0,height=0;uint64_t guiPins=0,lastFrame=0;
    explicit TargetGpu(Rhi::D3D11RenderBackend& b):backend(b){}~TargetGpu(){backend.DestroyTexture(color);backend.DestroyTexture(depth);}};
struct Renderer {
    // Fixed closed reference contracts, renderer lifetime; no user shader/source cache.
    struct ShaderReference { std::string source,entry,target; ComPtr<ID3D11ShaderReflection> reflection; };
    std::vector<ShaderReference> shaderReferences;
    std::vector<ShaderReference> environmentShaderReferences;
    std::array<ComPtr<ID3D11ShaderReflection>,3> postReferences;
    NcmaPostStatsV1 postStats{72,1};
    struct UiTarget { Rhi::TextureHandle color; uint32_t width=0,height=0; uint64_t content=0,produced=0,leases=0; };
    struct UiLease { uint64_t target=0,content=0,produced=0,frame=0,pins=0; };
    std::unordered_map<uint64_t,UiTarget> uiTargets;
    std::unordered_map<uint64_t,UiLease> uiLeases;
    uint64_t uiTargetBytes=0,uiProductions=0,uiPresentations=0;
    bool pureUi=false;
    uint64_t lastUiFrame=0;
    std::unique_ptr<Rendering::UiKernel> ui;
    uint64_t handle=0,platform=0,window=0,borrows=0,lastFrame=0,diagnosticFrame=0;
    bool active=false,failed=false,vsync=false;
    uint64_t abandonedFrames=0;
    uint32_t faultResult=NCMA_INTERNAL_ERROR;
    NcmaRendererFaultV1 firstFault{sizeof(NcmaRendererFaultV1),1};
    uint32_t diagnosticOperation=NCMA_GPU_FAULT_STARTUP;
    std::unique_ptr<Rhi::D3D11RenderBackend> backend;
    std::unordered_map<uint64_t,std::unique_ptr<Rendering::ReferenceKernel>> groups;
    std::unique_ptr<Rendering::StaticMeshKernel> meshKernel;
    std::unordered_map<uint64_t,std::unique_ptr<Rendering::StaticMesh>> meshes;
    NcmaSceneRenderStatsV1 meshStats{sizeof(NcmaSceneRenderStatsV1)};
    std::unique_ptr<Rendering::ResourceKernel> resourceKernel;
    std::unordered_map<uint64_t,std::unique_ptr<TextureGpu>> textures;
    std::unordered_map<uint64_t,std::unique_ptr<MaterialGpu>> materials;
    std::unordered_map<uint64_t,std::unique_ptr<TargetGpu>> targets;
    NcmaResourceStatsV3 resourceStats{sizeof(NcmaResourceStatsV3)};
    std::unordered_map<uint64_t,std::unique_ptr<Rendering::ScenePipelineKernel>> scenePipelines;
    NcmaScenePipelineStatsV4 sceneStats{sizeof(NcmaScenePipelineStatsV4),4096};
    std::unordered_map<uint64_t,std::unique_ptr<Rendering::EnvironmentGpuKernel>> environments;
    NcmaEnvironmentGpuStatsV1 environmentStats{64,1,0,0,0,0,0,0,32ull*1024*1024};
    NcmaGpuWorkSampleV1 skinSample{{sizeof(NcmaGpuSampleV1),1}},uiSample{{sizeof(NcmaGpuSampleV1),1}};
    NcmaGpuWorkSampleV1 skinResolved{{sizeof(NcmaGpuSampleV1),1}},uiResolved{{sizeof(NcmaGpuSampleV1),1}};
    std::unique_ptr<Rendering::SkinKernel> skinKernel;
    std::unique_ptr<Rendering::GpuWorkTimingKernel> uiTiming;
    uint64_t lastUiProductionFrame=0;
    std::unordered_map<uint64_t,std::unique_ptr<Rendering::SkinInstance>> skins;
    NcmaSkinStatsV5 skinStats{sizeof(NcmaSkinStatsV5),32};
    uint64_t lastSkinFrame=0;
    ComPtr<ID3D11InfoQueue> validation;
    ComPtr<ID3D11Query> gpuDisjoint,gpuBegin,gpuEnd;
    bool measuring=false,timingPending=false,timingFailed=false;
    NcmaGpuSampleV1 gpuSample{sizeof(NcmaGpuSampleV1),1},gpuResolved{sizeof(NcmaGpuSampleV1),1};
    std::string diagnostics;
    NcmaRendererStatsV1 stats{sizeof(NcmaRendererStatsV1)};
};
std::unique_ptr<Renderer> renderer;
NcmaRendererFaultV1 startupFault{sizeof(NcmaRendererFaultV1),1};
uint32_t CompletionError(HRESULT operation,HRESULT removed) noexcept;
void RecordFault(Renderer& r,uint32_t operation,uint32_t result,HRESULT code=S_OK,bool hasCode=false) noexcept {
    if(r.firstFault.flags)return;
    auto* device=r.backend?r.backend->GetDevice():nullptr;
    const HRESULT removed=device?device->GetDeviceRemovedReason():S_OK;
    r.firstFault={sizeof(r.firstFault),1,1u|(hasCode?2u:0u)|(device?4u:0u),result,r.handle,r.diagnosticFrame?r.diagnosticFrame:r.lastFrame,operation,
        hasCode?static_cast<int32_t>(code):0,device?static_cast<int32_t>(removed):0,1};
}
void BackendFault(void* owner,HRESULT result) noexcept {
    auto& r=*static_cast<Renderer*>(owner);auto* device=r.backend->GetDevice();
    RecordFault(r,r.diagnosticOperation,CompletionError(result,device?device->GetDeviceRemovedReason():S_OK),result,true);
}
struct BusyScope { BusyScope(){busy=true;} ~BusyScope(){busy=false;} };
struct DiagnosticScope {
    uint32_t previous;
    explicit DiagnosticScope(uint32_t operation):previous(renderer->diagnosticOperation){renderer->diagnosticOperation=operation;}
    ~DiagnosticScope(){renderer->diagnosticOperation=previous;}
};
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
void StopRenderer(uint32_t result) noexcept {
    RecordFault(*renderer,NCMA_GPU_FAULT_EXECUTION,result);
    if(!renderer->failed)renderer->faultResult=result;
    renderer->failed=true; renderer->stats.state=2;renderer->active=false;
    // A capture/present/device failure can occur after submit, outside ExecutionScope.
    // Abort that frame so fail-stop still permits deterministic lease draining and shutdown.
    auto* dc=renderer->backend->GetDeviceContext();
    if(renderer->measuring) {
        dc->End(renderer->gpuEnd.Get());dc->End(renderer->gpuDisjoint.Get());
        renderer->measuring=false;
    }
    dc->ClearState();
    renderer->timingPending=false;renderer->stats.gpu_sample_valid=0;renderer->stats.gpu_ms=0;
    renderer->gpuSample.state=NCMA_GPU_SAMPLE_FAILED;renderer->gpuSample.milliseconds=0;
    if(renderer->skinKernel)renderer->skinKernel->InvalidateTiming(NCMA_GPU_SAMPLE_FAILED);
    if(renderer->uiTiming)renderer->uiTiming->Invalidate(NCMA_GPU_SAMPLE_FAILED);
    renderer->skinStats.gpu_sample_valid=0;renderer->skinStats.gpu_ms=0;
}
uint32_t Failure(NcmaErrorV1* error,const std::string& message,uint32_t operation=NCMA_GPU_FAULT_EXECUTION,HRESULT code=S_OK,bool hasCode=false) {
    const bool lost=FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason());
    const uint32_t result=lost||(hasCode&&CompletionError(code,S_OK)==NCMA_DEVICE_LOST)?NCMA_DEVICE_LOST:NCMA_INTERNAL_ERROR;
    RecordFault(*renderer,operation,result,code,hasCode);
    StopRenderer(result);
    return NcmaPlugin::Error(error,renderer->faultResult,message);
}
#ifdef NCMA_RENDERER_TEST_WAIT
bool testWaitTimeout=false; // Only the white-box test TU; absent from shipped module/API.
bool testWaitQueryFailure=false;
bool testWaitPollFailure=false;
bool testWaitRemovedBefore=false,testWaitRemovedDuring=false;
bool testTonePreparationThrow=false;
HRESULT testPresentResult=S_OK;
bool testStartupValidationFailure=false;
#endif
// Typed results are shared by resize, updates, readback and retirement. Never infer
// the cause from diagnostic text or label every failed completion as a timeout.
uint32_t CompletionError(HRESULT operation,HRESULT removed) noexcept {
    return FAILED(removed)||operation==DXGI_ERROR_DEVICE_REMOVED||operation==DXGI_ERROR_DEVICE_RESET||
        operation==DXGI_ERROR_DEVICE_HUNG||operation==DXGI_ERROR_DRIVER_INTERNAL_ERROR?NCMA_DEVICE_LOST:NCMA_INTERNAL_ERROR;
}
uint32_t Wait(Renderer& r,std::string& error) {
#ifdef NCMA_RENDERER_TEST_WAIT
    if(testWaitTimeout){RecordFault(r,NCMA_GPU_FAULT_WAIT_TIMEOUT,NCMA_SHUTDOWN_TIMEOUT);error="Injected completion timeout; resources retained.";return NCMA_SHUTDOWN_TIMEOUT;}
#endif
    auto* device=r.backend->GetDevice(); auto* context=r.backend->GetDeviceContext();
    HRESULT removed=device->GetDeviceRemovedReason();
#ifdef NCMA_RENDERER_TEST_WAIT
    if(testWaitRemovedBefore)removed=DXGI_ERROR_DEVICE_REMOVED;
#endif
    if(FAILED(removed)) { RecordFault(r,NCMA_GPU_FAULT_DEVICE_REMOVED,NCMA_DEVICE_LOST,removed,true);error="Device lost before GPU completion."; return NCMA_DEVICE_LOST; }
    D3D11_QUERY_DESC desc{D3D11_QUERY_EVENT,0}; ComPtr<ID3D11Query> query;
    HRESULT created;
#ifdef NCMA_RENDERER_TEST_WAIT
    created=testWaitQueryFailure?E_OUTOFMEMORY:device->CreateQuery(&desc,&query);
#else
    created=device->CreateQuery(&desc,&query);
#endif
    if(FAILED(created)) {const auto result=CompletionError(created,device->GetDeviceRemovedReason());RecordFault(r,NCMA_GPU_FAULT_WAIT_CREATE,result,created,true);error="GPU completion query creation failed; resources retained.";return result;}
    context->End(query.Get()); context->Flush();
    const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(2);
    for(;;) {
        HRESULT result;
#ifdef NCMA_RENDERER_TEST_WAIT
        result=testWaitRemovedDuring?DXGI_ERROR_DEVICE_REMOVED:testWaitPollFailure?E_FAIL:context->GetData(query.Get(),nullptr,0,0);
#else
        result=context->GetData(query.Get(),nullptr,0,0);
#endif
        if(result==S_OK) return NCMA_OK;
        if(FAILED(result)) {const auto failure=CompletionError(result,device->GetDeviceRemovedReason());RecordFault(r,NCMA_GPU_FAULT_WAIT_POLL,failure,result,true);error="GPU completion query polling failed; resources retained.";return failure;}
        if(std::chrono::steady_clock::now()>=deadline) {
            error="GPU completion timed out; resources retained.";
            const auto failure=FAILED(device->GetDeviceRemovedReason())?NCMA_DEVICE_LOST:NCMA_SHUTDOWN_TIMEOUT;
            RecordFault(r,NCMA_GPU_FAULT_WAIT_TIMEOUT,failure);return failure;
        }
        std::this_thread::yield();
    }
}
uint32_t CompleteGpu(NcmaErrorV1* error,bool retirement=false) {
    // Preserve the existing removed-device retirement policy: only an actually
    // removed device needs no completion fence. It does not bypass leases/pins.
    if(retirement&&FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())) {RecordFault(*renderer,NCMA_GPU_FAULT_DEVICE_REMOVED,NCMA_DEVICE_LOST,renderer->backend->GetDevice()->GetDeviceRemovedReason(),true);StopRenderer(NCMA_DEVICE_LOST);return NCMA_OK;}
    std::string message;const auto result=Wait(*renderer,message);
    if(result==NCMA_DEVICE_LOST)StopRenderer(result);
    return result==NCMA_OK?NCMA_OK:NcmaPlugin::Error(error,result,message);
}
struct ExecutionScope {
    bool success=false;
    explicit ExecutionScope(uint64_t frame){renderer->diagnosticFrame=frame;}
    ~ExecutionScope() noexcept {
        if(!renderer)return;
        if(success){renderer->diagnosticFrame=0;return;}
        const auto result=renderer->failed?renderer->faultResult:
            FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?NCMA_DEVICE_LOST:NCMA_INTERNAL_ERROR;
        StopRenderer(result);
        renderer->diagnosticFrame=0;
    }
};
#ifdef NCMA_RENDERER_TEST_WAIT
HRESULT testTimingResult=S_OK;
bool testTimingDisjoint=false,testTimingInvalid=false;
#endif
void ResolveTiming(Renderer& r) {
    if(!r.timingPending)return;
    D3D11_QUERY_DATA_TIMESTAMP_DISJOINT data{};UINT64 begin=0,end=0;
    auto* dc=r.backend->GetDeviceContext();
    auto read=[&](ID3D11Query* query,void* output,UINT bytes) {
        HRESULT result;
#ifdef NCMA_RENDERER_TEST_WAIT
        result=testTimingResult!=S_OK?testTimingResult:dc->GetData(query,output,bytes,D3D11_ASYNC_GETDATA_DONOTFLUSH);
#else
        result=dc->GetData(query,output,bytes,D3D11_ASYNC_GETDATA_DONOTFLUSH);
#endif
        if(FAILED(result))RecordFault(r,NCMA_GPU_FAULT_TIMING,CompletionError(result,r.backend->GetDevice()->GetDeviceRemovedReason()),result,true);
        if(FAILED(result)){r.timingPending=false;r.timingFailed=true;r.gpuSample.state=NCMA_GPU_SAMPLE_FAILED;r.gpuSample.milliseconds=0;r.stats.gpu_sample_valid=0;r.stats.gpu_ms=0;}
        return result;
    };
    if(read(r.gpuDisjoint.Get(),&data,sizeof(data))!=S_OK ||
       read(r.gpuBegin.Get(),&begin,sizeof(begin))!=S_OK ||read(r.gpuEnd.Get(),&end,sizeof(end))!=S_OK)return;
#ifdef NCMA_RENDERER_TEST_WAIT
    if(testTimingDisjoint)data.Disjoint=TRUE;
    if(testTimingInvalid)data.Frequency=0;
#endif
    const bool valid=!data.Disjoint&&data.Frequency&&end>=begin;
    r.gpuSample.state=data.Disjoint?NCMA_GPU_SAMPLE_DISJOINT:valid?NCMA_GPU_SAMPLE_VALID:NCMA_GPU_SAMPLE_INVALID;
    r.gpuSample.milliseconds=valid?static_cast<double>(end-begin)*1000.0/static_cast<double>(data.Frequency):0;
    r.gpuResolved=r.gpuSample;
    r.stats.gpu_sample_valid=valid?1u:0u;r.stats.gpu_ms=r.gpuSample.milliseconds;
    r.timingPending=false;
}
void BeginTiming(Renderer& r,uint64_t frame) {
    ResolveTiming(r);
    if(r.timingPending||r.timingFailed){if(r.gpuSample.skipped!=UINT64_MAX)++r.gpuSample.skipped;return;}
    if(r.measuring)return; // Overlay keeps the original main submission scope.
    if(r.gpuSample.sequence==UINT64_MAX)throw std::overflow_error("GPU sample sequence exhausted.");
    ++r.gpuSample.sequence;r.gpuSample.origin_frame=frame;r.gpuSample.state=NCMA_GPU_SAMPLE_RECORDING;
    r.gpuSample.milliseconds=0;r.stats.gpu_sample_valid=0;r.stats.gpu_ms=0;
    auto* dc=r.backend->GetDeviceContext();dc->Begin(r.gpuDisjoint.Get());dc->End(r.gpuBegin.Get());r.measuring=true;
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
        module=NcmaPlugin::NewWindowsHandle();startupFault={sizeof(startupFault),1}; *output=module;return NCMA_OK;
    });
}
uint32_t NCMA_CALL Shutdown(uint64_t context,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Validate(context,error);if(valid)return valid;
        if(renderer)return NcmaPlugin::Error(error,NCMA_BUSY); module=0;return NCMA_OK;});
}
uint32_t NCMA_CALL Status(uint64_t context,NcmaModuleStatusV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Validate(context,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto ui=renderer&&renderer->ui?renderer->ui->Stats(renderer->handle,renderer->pureUi):NcmaUiStatsV1{};
        *output={sizeof(*output),renderer&&renderer->failed?2u:1u,renderer?1+renderer->groups.size()+renderer->scenePipelines.size()+renderer->environments.size()+renderer->meshes.size()+renderer->textures.size()+renderer->materials.size()+renderer->targets.size()+renderer->uiTargets.size()+renderer->uiLeases.size()+ui.images+ui.lists:0,0,renderer?renderer->stats.presents:0};return NCMA_OK;});
}
uint32_t NCMA_CALL Diagnostic(uint64_t context,uint8_t* output,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Validate(context,error);if(valid)return valid;
        if(!required || (capacity && !output)) return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto message=renderer?std::string_view(renderer->diagnostics):std::string_view{};
        *required=static_cast<uint32_t>(message.size());
        if(capacity<message.size()) return NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);
        if(!message.empty())std::memcpy(output,message.data(),message.size());return NCMA_OK;});
}
uint32_t CreateImpl(uint64_t context,const NcmaRendererDescriptionV1* desc,uint64_t* output,NcmaErrorV1* error,bool pureUi) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Validate(context,error);if(valid)return valid;
        if(!desc||!output||desc->struct_size!=sizeof(*desc)||desc->validation>1||desc->vsync>1||
            desc->reserved[0]||desc->reserved[1]||desc->width==0||desc->height==0||desc->width>4096||desc->height>4096)
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(desc->backend!=1)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Only DX11 reference operation set is implemented.");
        if(renderer)return NcmaPlugin::Error(error,NCMA_BUSY);
        auto candidate=std::make_unique<Renderer>();
        candidate->handle=NcmaPlugin::NewWindowsHandle();
        GLFWwindow* window=nullptr;
        valid=ncma_platform_borrow_window_v1(desc->platform_module,desc->window,&window,error);if(valid)return valid;
        BusyScope scope;
        startupFault={sizeof(startupFault),1};
        try {
            candidate->backend=std::make_unique<Rhi::D3D11RenderBackend>();
            candidate->backend->SetFailureObserver(candidate.get(),BackendFault);
            Rhi::BackendCreateInfo info{glfwGetWin32Window(window),desc->width,desc->height,desc->validation!=0,desc->vsync!=0};
            info.EnableDefaultDepth=!pureUi;candidate->pureUi=pureUi;
            std::string message;
            if(!candidate->backend->Initialize(info,message)) throw std::runtime_error(message);
            if(desc->validation) {
                HRESULT validation=candidate->backend->GetDevice()->QueryInterface(IID_PPV_ARGS(&candidate->validation));
#ifdef NCMA_RENDERER_TEST_WAIT
                if(testStartupValidationFailure)validation=E_NOINTERFACE;
#endif
                if(FAILED(validation)){RecordFault(*candidate,NCMA_GPU_FAULT_STARTUP,NCMA_INTERNAL_ERROR,validation,true);throw std::runtime_error("Requested DX11 validation unavailable; no fallback.");}
            }
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
            auto prepareQuery=[&](ID3D11Query** output) {
                const HRESULT result=candidate->backend->GetDevice()->CreateQuery(&queryDesc,output);
                if(FAILED(result)){RecordFault(*candidate,NCMA_GPU_FAULT_STARTUP,CompletionError(result,candidate->backend->GetDevice()->GetDeviceRemovedReason()),result,true);throw std::runtime_error("Timestamp query failed.");}
            };
            prepareQuery(candidate->gpuDisjoint.GetAddressOf());
            queryDesc.Query=D3D11_QUERY_TIMESTAMP;
            prepareQuery(candidate->gpuBegin.GetAddressOf());prepareQuery(candidate->gpuEnd.GetAddressOf());
            candidate->platform=desc->platform_module;candidate->window=desc->window;candidate->vsync=desc->vsync!=0;
            candidate->stats.width=desc->width;candidate->stats.height=desc->height;candidate->stats.state=1;
            candidate->diagnosticOperation=NCMA_GPU_FAULT_NATIVE_API;
            *output=candidate->handle;renderer=std::move(candidate);return NCMA_OK;
        } catch(...) {RecordFault(*candidate,NCMA_GPU_FAULT_STARTUP,NCMA_INTERNAL_ERROR);startupFault=candidate->firstFault;NcmaErrorV1 ignored{}; (void)ncma_platform_release_window_v1(desc->platform_module,desc->window,&ignored); throw; }
    });
}
uint32_t NCMA_CALL Create(uint64_t context,const NcmaRendererDescriptionV1* desc,uint64_t* output,NcmaErrorV1* error) noexcept {return CreateImpl(context,desc,output,error,false);}
uint32_t NCMA_CALL CreateUiRenderer(uint64_t context,const NcmaRendererDescriptionV1* desc,uint64_t* output,NcmaErrorV1* error) noexcept {return CreateImpl(context,desc,output,error,true);}
uint32_t NCMA_CALL CreateResources(uint64_t context,uint64_t handle,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->pureUi)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"UI-only renderer forbids 3D resources.");
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active||renderer->groups.size()>=8)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;auto group=std::make_unique<Rendering::ReferenceKernel>(*renderer->backend);std::string message;
        if(!group->Initialize(message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        const auto id=NcmaPlugin::NewWindowsHandle();
        renderer->groups.emplace(id,std::move(group));*output=id;return NCMA_OK;});
}
uint32_t NCMA_CALL DestroyResources(uint64_t context,uint64_t handle,uint64_t group,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!renderer->groups.contains(group))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;
        // Device loss permits releasing resources only after unbinding, no recovery promised.
        if(const auto completion=CompleteGpu(error,true);completion!=NCMA_OK)return completion;
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
        ExecutionScope execution(frame->frame);
        // Prepare resource groups once, never a per-object managed/native call.
        for(const auto& [id,state]:initialized) {
            (void)state;
            if(!renderer->groups.at(id)->Prepare(static_cast<uint32_t>(frame->viewport[2]),static_cast<uint32_t>(frame->viewport[3]),
                frame->model,frame->exposure,frame->metallic,frame->roughness,message))return Failure(error,message);
        }
        BeginTiming(*renderer,frame->frame);
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
            renderer->gpuSample.state=NCMA_GPU_SAMPLE_PENDING;
        }
        HRESULT result;
#ifdef NCMA_RENDERER_TEST_WAIT
        result=FAILED(testPresentResult)?testPresentResult:renderer->backend->GetSwapChain()->Present(renderer->vsync?1:0,0);
#else
        result=renderer->backend->GetSwapChain()->Present(renderer->vsync?1:0,0);
#endif
        renderer->active=false;
        if(FAILED(result))return Failure(error,"Present failed.",NCMA_GPU_FAULT_PRESENT,result,true);
        renderer->stats.presents++;renderer->stats.present_ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();
        Validation(*renderer);return NCMA_OK;});
}
uint32_t NCMA_CALL ReadRendererFrame(uint64_t context,uint64_t handle,NcmaRendererFrameStateV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output={sizeof(*output),1,(renderer->active?1u:0u)|(renderer->failed?2u:0u),0,handle,renderer->lastFrame,renderer->abandonedFrames};return NCMA_OK;
    });
}
uint32_t NCMA_CALL ReadRendererFault(uint64_t context,uint64_t handle,NcmaRendererFaultV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        const auto valid=handle?Instance(context,handle,error):Validate(context,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output=handle?renderer->firstFault:startupFault;
        if(handle&&!output->flags){output->generation=handle;output->backend=1;}
        return NCMA_OK;
    });
}
uint32_t NCMA_CALL AbandonRendererFrame(uint64_t context,uint64_t handle,uint64_t frame,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(!renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        if(!frame||frame!=renderer->lastFrame)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        BusyScope scope;
        if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"Device lost before explicit frame abandonment.");
        RecordFault(*renderer,NCMA_GPU_FAULT_ABANDON,NCMA_INTERNAL_ERROR);StopRenderer(NCMA_INTERNAL_ERROR);
        renderer->gpuSample.state=NCMA_GPU_SAMPLE_ABANDONED;
        if(renderer->skinKernel)renderer->skinKernel->InvalidateTiming(NCMA_GPU_SAMPLE_ABANDONED);
        if(renderer->uiTiming)renderer->uiTiming->Invalidate(NCMA_GPU_SAMPLE_ABANDONED);
        ++renderer->abandonedFrames;Validation(*renderer);return NCMA_OK;
    });
}
uint32_t NCMA_CALL Resize(uint64_t context,uint64_t handle,uint32_t width,uint32_t height,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!width||!height||width>4096||height>4096)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        if(width==renderer->stats.width&&height==renderer->stats.height)return NCMA_OK;
        BusyScope scope;std::string message;
        if(const auto completion=CompleteGpu(error);completion!=NCMA_OK)return completion;
        DiagnosticScope diagnostic(NCMA_GPU_FAULT_RESIZE);
        if(!renderer->backend->ResizeChecked(width,height,message))return Failure(error,message,NCMA_GPU_FAULT_RESIZE);
        renderer->stats.width=width;renderer->stats.height=height;Validation(*renderer);return NCMA_OK;});
}
uint32_t NCMA_CALL Stats(uint64_t context,uint64_t handle,NcmaRendererStatsV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        ResolveTiming(*renderer);Validation(*renderer);renderer->stats.live_groups=renderer->groups.size()+renderer->scenePipelines.size();*output=renderer->stats;return NCMA_OK;});
}
uint32_t NCMA_CALL ReadGpuSample(uint64_t context,uint64_t handle,NcmaGpuSampleV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        ResolveTiming(*renderer);auto sample=renderer->gpuSample;
        sample.generation=handle;sample.observed_frame=renderer->lastFrame;sample.domain=1;
        *output=sample;return NCMA_OK;
    });
}
uint32_t NCMA_CALL WaitIdle(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        BusyScope scope;std::string message;
        if(const auto completion=CompleteGpu(error);completion!=NCMA_OK)return completion;
        return NCMA_OK;});
}
uint32_t NCMA_CALL Destroy(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        const auto ui=renderer->ui?renderer->ui->Stats(handle,renderer->pureUi):NcmaUiStatsV1{};
        if(renderer->borrows||!renderer->groups.empty()||!renderer->meshes.empty()||!renderer->uiTargets.empty()||!renderer->uiLeases.empty()||renderer->backend->GetLiveResourceCount()!=0||ui.images||ui.lists)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;
        if(const auto completion=CompleteGpu(error,true);completion!=NCMA_OK)return completion;
        NcmaErrorV1 platformError{};
        valid=ncma_platform_release_window_v1(renderer->platform,renderer->window,&platformError);if(valid){*error=platformError;return valid;}
        renderer->ui.reset();renderer->backend->Shutdown();renderer.reset();return NCMA_OK;});
}
uint32_t NCMA_CALL Capture(uint64_t context,uint64_t handle,uint8_t* output,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!required)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *required=renderer->stats.width*renderer->stats.height*4;
        if(!output||capacity<*required){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=*required;return NCMA_BUFFER_TOO_SMALL;}
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(!renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;ComPtr<ID3D11Texture2D> source,target;auto* device=renderer->backend->GetDevice();auto* dc=renderer->backend->GetDeviceContext();
        HRESULT result=renderer->backend->GetSwapChain()->GetBuffer(0,IID_PPV_ARGS(&source));
        if(FAILED(result))return Failure(error,"Readback source failed.",NCMA_GPU_FAULT_READBACK,result,true);
        D3D11_TEXTURE2D_DESC desc{};source->GetDesc(&desc);desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        result=device->CreateTexture2D(&desc,nullptr,&target);
        if(FAILED(result))return Failure(error,"Readback allocation failed.",NCMA_GPU_FAULT_READBACK,result,true);
        dc->CopyResource(target.Get(),source.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
        result=dc->Map(target.Get(),0,D3D11_MAP_READ,0,&mapped);
        if(FAILED(result))return Failure(error,"Readback map failed.",NCMA_GPU_FAULT_READBACK,result,true);
        for(uint32_t y=0;y<desc.Height;++y)std::memcpy(output+y*desc.Width*4,static_cast<const uint8_t*>(mapped.pData)+y*mapped.RowPitch,desc.Width*4);
        dc->Unmap(target.Get(),0);Validation(*renderer);return NCMA_OK;});
}
uint32_t CreateMeshImpl(uint64_t context,uint64_t handle,const NcmaMeshDescriptionV1* input,NcmaGpuMeshV1* output,NcmaErrorV1* error,bool bindPose) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->pureUi)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"UI-only renderer forbids 3D resources.");
        if(!input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto d=*input;
        const uint64_t bytes=static_cast<uint64_t>(d.vertex_bytes)+d.index_bytes;
        if(d.struct_size!=sizeof(d)||d.layout!=(bindPose?2u:1u)||d.stride!=(bindPose?80u:48u)||d.reserved||!d.vertices||!d.indices||
           !d.vertex_count||!d.index_count||d.index_count%3||
           static_cast<uint64_t>(d.vertex_count)*d.stride!=d.vertex_bytes||static_cast<uint64_t>(d.index_count)*4!=d.index_bytes||
           bytes>64ull*1024*1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Invalid static mesh layout/count/budget.");
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        if(renderer->meshes.size()>=128||bytes>256ull*1024*1024-renderer->meshStats.resident_bytes)
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Renderer mesh budget exceeded.");
        // Unaligned byte input is legal; inspect by copy, never cast to float pointers.
        for(uint32_t i=0;i<d.vertex_count;++i) {
            float v[12];std::memcpy(v,d.vertices+static_cast<size_t>(i)*d.stride,48);
            for(float x:v)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Nonfinite mesh vertex.");
            const float n=v[3]*v[3]+v[4]*v[4]+v[5]*v[5],t=v[8]*v[8]+v[9]*v[9]+v[10]*v[10];
            const float dot=v[3]*v[8]+v[4]*v[9]+v[5]*v[10];
            if(!std::isfinite(n)||!std::isfinite(t)||std::abs(n-1)>0.002f||std::abs(t-1)>0.002f||std::abs(dot)>0.002f||std::abs(v[11])!=1)
                return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Invalid normal/tangent basis.");
        }
        for(uint32_t i=0;i<d.index_count;++i)if(d.indices[i]>=d.vertex_count)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Mesh index out of range.");
        BusyScope scope;std::string message;
        auto mesh=std::make_unique<Rendering::StaticMesh>(*renderer->backend);
        std::unique_ptr<Rendering::StaticMeshKernel> kernel;
        if(!renderer->meshKernel) {
            kernel=std::make_unique<Rendering::StaticMeshKernel>(*renderer->backend);
            if(!kernel->Initialize(message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        }
        if(!mesh->Initialize(d,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        // Diagnostics may allocate: do this while candidate resources are still private/RAII-owned.
        Validation(*renderer);
        const uint64_t id=NcmaPlugin::NewWindowsHandle();
        renderer->meshes.emplace(id,std::move(mesh));
        if(kernel)renderer->meshKernel=std::move(kernel);
        renderer->meshStats.resident_bytes+=bytes;renderer->meshStats.uploaded_bytes+=bytes;renderer->meshStats.mesh_creates++;
        *output={id,handle};return NCMA_OK;
    });
}
uint32_t NCMA_CALL CreateMesh(uint64_t context,uint64_t handle,const NcmaMeshDescriptionV1* input,NcmaGpuMeshV1* output,NcmaErrorV1* error) noexcept {
    return CreateMeshImpl(context,handle,input,output,error,false);
}
uint32_t NCMA_CALL CreateBindPoseMesh(uint64_t context,uint64_t handle,const NcmaBindPoseMeshDescriptionV2* input,NcmaGpuMeshV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto d=*input;const auto& mesh=d.mesh;
        if(mesh.struct_size!=sizeof(d)||mesh.layout!=2||mesh.stride!=80||!mesh.vertices||!mesh.vertex_count||
           static_cast<uint64_t>(mesh.vertex_count)*80!=mesh.vertex_bytes||
           static_cast<uint64_t>(mesh.vertex_bytes)+mesh.index_bytes>64ull*1024*1024||
           !d.palette||!d.palette_count||d.palette_count>4096||d.palette_bytes!=d.palette_count*64)
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Invalid bind-pose layout/palette budget.");
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        for(uint32_t i=0;i<d.palette_count;++i) {
            float m[16];std::memcpy(m,reinterpret_cast<const uint8_t*>(d.palette)+static_cast<size_t>(i)*64,64);
            for(float x:m)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Nonfinite bind palette.");
            if(m[3]!=0||m[7]!=0||m[11]!=0||m[15]!=1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Nonaffine bind palette.");
        }
        for(uint32_t i=0;i<mesh.vertex_count;++i) {
            const auto* row=mesh.vertices+static_cast<size_t>(i)*80;uint16_t joints[4];float weights[4];uint32_t padding[2];
            std::memcpy(joints,row+48,8);std::memcpy(weights,row+56,16);std::memcpy(padding,row+72,8);
            float total=0;
            for(uint32_t k=0;k<4;++k) {
                if(!std::isfinite(weights[k])||weights[k]<0||weights[k]>1||(weights[k]>0&&joints[k]>=d.palette_count))
                    return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Invalid bind-pose influence.");
                total+=weights[k];
            }
            if(std::abs(total-1)>0.001f||padding[0]||padding[1])return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Invalid bind-pose weights/padding.");
        }
        auto geometry=mesh;geometry.struct_size=sizeof(geometry);
        return CreateMeshImpl(context,handle,&geometry,output,error,true);
    });
}
uint32_t NCMA_CALL DestroyMesh(uint64_t context,uint64_t handle,NcmaGpuMeshV1 mesh,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(mesh.generation!=handle||!renderer->meshes.contains(mesh.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->skins.contains(mesh.value))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"GPU skin requires query-5 release.");
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;
        if(const auto completion=CompleteGpu(error,true);completion!=NCMA_OK)return completion;
        renderer->backend->GetDeviceContext()->ClearState();
        renderer->meshStats.resident_bytes-=renderer->meshes.at(mesh.value)->bytes;
        renderer->meshes.erase(mesh.value);if(renderer->meshes.empty())renderer->meshKernel.reset();
        Validation(*renderer);return NCMA_OK;
    });
}
uint32_t NCMA_CALL SubmitMeshes(uint64_t context,uint64_t handle,const NcmaMeshFrameV1* input,const NcmaMeshDrawV1* draws,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto f=*input;
        if(f.struct_size!=sizeof(f)||!f.frame||f.frame<=renderer->lastFrame||f.draw_count>4096||
           (f.draw_count&&!draws)||f.generation!=handle||f.reserved[0]||f.reserved[1])return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        for(float x:f.viewport)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(f.viewport[0]<0||f.viewport[1]<0||f.viewport[2]<=0||f.viewport[3]<=0||
           f.viewport[0]+f.viewport[2]>renderer->stats.width||f.viewport[1]+f.viewport[3]>renderer->stats.height)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float x:f.clear)if(!std::isfinite(x)||x<0||x>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        std::vector<NcmaMeshDrawV1> copied;
        if(f.draw_count)copied.assign(draws,draws+f.draw_count);
        for(const auto& d:copied) {
            if(d.mesh.generation!=handle||!renderer->meshes.contains(d.mesh.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
            if(renderer->skins.contains(d.mesh.value))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"GPU skin requires query-4 scene draws.");
            const uint32_t count=renderer->meshes.at(d.mesh.value)->indexCount;
            if(d.reserved[0]||d.reserved[1]||!d.index_count||d.index_count%3||d.first_index%3||d.first_index>count||d.index_count>count-d.first_index)
                return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Invalid mesh triangle range.");
            for(float x:d.model_view_projection)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            for(float x:d.color)if(!std::isfinite(x)||x<0||x>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        }
        BusyScope scope;ExecutionScope execution(f.frame);std::string message;const auto started=std::chrono::steady_clock::now();
        BeginTiming(*renderer,f.frame);
        const auto encode=[](float x){return x<=0.0031308f?12.92f*x:1.055f*std::pow(x,1.0f/2.4f)-0.055f;};
        renderer->backend->SetClearColor(encode(f.clear[0]),encode(f.clear[1]),encode(f.clear[2]),f.clear[3]);
        const bool begun=renderer->backend->BeginFrame(message);
        renderer->backend->SetClearColor(0.035f,0.039f,0.052f,1);
        if(!begun)return Failure(error,message);
        for(const auto& d:copied)if(!renderer->meshKernel->Draw(*renderer->meshes.at(d.mesh.value),d,f.viewport,message))return Failure(error,message);
        renderer->active=true;renderer->lastFrame=f.frame;renderer->stats.submitted_frames++;renderer->meshStats.draws+=f.draw_count;
        renderer->stats.submit_ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();
        Validation(*renderer);execution.success=true;return NCMA_OK;
    });
}
uint32_t NCMA_CALL MeshStats(uint64_t context,uint64_t handle,NcmaSceneRenderStatsV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        renderer->meshStats.generation=handle;renderer->meshStats.live_meshes=renderer->meshes.size();*output=renderer->meshStats;return NCMA_OK;
    });
}
#include "ResourceServices.inl"
#include "SkinServices.inl"
#include "ScenePipelineServices.inl"
#include "UiServices.inl"
#include "UiTargetServices.inl"
#include "ShaderServices.inl"
#include "ShaderPipelineServices.inl"
#include "ShaderStagesServices.inl"
#include "UiShadersServices.inl"
#include "RuntimeShadersServices.inl"
#include "EnvironmentCookServices.inl"
#include "EnvironmentGpuServices.inl"
#include "EnvironmentSceneServices.inl"
#include "PostProcessServices.inl"
uint32_t NCMA_CALL ReadGpuWorkSample(uint64_t context,uint64_t handle,uint32_t domain,NcmaGpuWorkSampleV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(domain!=2&&domain!=3)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE);
        if(domain==2&&renderer->skinKernel)renderer->skinKernel->ResolveTiming(renderer->skinStats);
        if(domain==3&&renderer->uiTiming)renderer->uiTiming->Resolve();
        auto sample=domain==2?renderer->skinSample:renderer->uiSample;
        sample.timing.generation=handle;sample.timing.domain=domain;
        sample.timing.observed_frame=domain==2?renderer->lastSkinFrame:renderer->lastUiProductionFrame;
        *output=sample;return NCMA_OK;
    });
}
uint32_t NCMA_CALL ReadGpuTimingSnapshot(uint64_t context,uint64_t handle,uint32_t domain,NcmaGpuTimingSnapshotV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(domain<1||domain>3)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE);
        NcmaGpuTimingSnapshotV1 snapshot{};
        uint64_t observed=0;
        if(domain==1) {
            ResolveTiming(*renderer);
            snapshot.current.timing=renderer->gpuSample;snapshot.last_resolved.timing=renderer->gpuResolved;
            observed=renderer->lastFrame;
        } else {
            if(domain==2&&renderer->skinKernel)renderer->skinKernel->ResolveTiming(renderer->skinStats);
            if(domain==3&&renderer->uiTiming)renderer->uiTiming->Resolve();
            snapshot.current=domain==2?renderer->skinSample:renderer->uiSample;
            snapshot.last_resolved=domain==2?renderer->skinResolved:renderer->uiResolved;
            observed=domain==2?renderer->lastSkinFrame:renderer->lastUiProductionFrame;
        }
        for(auto* sample:{&snapshot.current,&snapshot.last_resolved}) {
            sample->timing.generation=handle;sample->timing.domain=domain;sample->timing.observed_frame=observed;
        }
        *output=snapshot;return NCMA_OK;
    });
}
uint32_t NCMA_CALL ReadResourceLedger(uint64_t context,uint64_t handle,NcmaRendererResourceLedgerV1* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(capacity<sizeof(*output)){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=sizeof(*output);return NCMA_BUFFER_TOO_SMALL;}
        const auto ui=renderer->ui?renderer->ui->Stats(handle,renderer->pureUi):NcmaUiStatsV1{};
        const NcmaRendererResourceLedgerV1 snapshot{sizeof(snapshot),1,handle,
            (renderer->pureUi?1u:0u)|(renderer->failed?2u:0u)|(renderer->active?4u:0u),0,
            {renderer->meshStats.resident_bytes,renderer->skinStats.resident_bytes,renderer->resourceStats.resident_bytes,
             renderer->sceneStats.resident_bytes,renderer->environmentStats.resident_bytes,ui.resident_bytes,
             renderer->uiTargetBytes,renderer->postStats.resident_bytes},
            {renderer->meshes.size(),renderer->skins.size(),renderer->textures.size()+renderer->materials.size()+renderer->targets.size(),
             renderer->scenePipelines.size(),renderer->environments.size(),ui.images+ui.lists,renderer->uiTargets.size(),renderer->postStats.bindings}};
        *output=snapshot;return NCMA_OK;
    });
}
uint32_t NCMA_CALL QuerySceneRender(uint64_t context,uint32_t version,void* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Validate(context,error);if(valid)return valid;
        if(version<1||version>22)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(version==22){
            const NcmaRendererResourceLedgerApiV1 ledger{sizeof(ledger),1,1,ReadResourceLedger};
            if(capacity<sizeof(ledger)){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=sizeof(ledger);return NCMA_BUFFER_TOO_SMALL;}
            std::memcpy(output,&ledger,sizeof(ledger));return NCMA_OK;
        }
        const uint32_t required=version==1?sizeof(NcmaSceneRenderApiV1):version==2?sizeof(NcmaSceneRenderApiV2):version==3?sizeof(NcmaResourceRenderApiV3):version==4?sizeof(NcmaScenePipelineApiV4):version==5?sizeof(NcmaSkinApiV5):version==6?sizeof(NcmaUiApiV1):version==7?sizeof(NcmaUiTargetApiV1):version==8?sizeof(NcmaShaderApiV1):version==9?sizeof(NcmaShaderPipelineApiV1):version==10?sizeof(NcmaShaderStagesApiV1):version==11?sizeof(NcmaUiShadersApiV1):version==12?sizeof(NcmaRuntimeShadersApiV1):version==13?sizeof(NcmaEnvironmentCookApiV1):version==16?sizeof(NcmaPostApiV1):version==17?sizeof(NcmaRendererFrameApiV1):version==18?sizeof(NcmaRendererFaultApiV1):version==19?sizeof(NcmaRendererTimingApiV1):version==20?sizeof(NcmaRendererWorkTimingApiV1):version==21?sizeof(NcmaRendererTimingSnapshotApiV1):sizeof(NcmaEnvironmentGpuApiV1);
        if(capacity<required) {
            NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=required;return NCMA_BUFFER_TOO_SMALL;
        }
        const NcmaSceneRenderApiV1 table{sizeof(table),1,1,CreateMesh,DestroyMesh,SubmitMeshes,MeshStats};
        if(version==1)std::memcpy(output,&table,sizeof(table));
        else if(version==2) {NcmaSceneRenderApiV2 extended{table,CreateBindPoseMesh};extended.base.struct_size=sizeof(extended);extended.base.version=2;extended.base.capabilities=3;
            std::memcpy(output,&extended,sizeof(extended));}
        else if(version==3) {const NcmaResourceRenderApiV3 resources{sizeof(resources),3,0x3c,CreateTexture,CreateMaterial,CreateTarget,DestroyResource,SubmitResources,ResourceStats,CaptureTarget};std::memcpy(output,&resources,sizeof(resources));}
        else if(version==4) {const NcmaScenePipelineApiV4 scene{sizeof(scene),4,0x7,CreateScenePipeline,DestroyScenePipeline,SubmitScenePipeline,ScenePipelineStats};std::memcpy(output,&scene,sizeof(scene));}
        else if(version==5) {const NcmaSkinApiV5 skin{sizeof(skin),5,3,CreateSkin,DestroySkin,UpdateSkin,CaptureSkin,SkinStats};std::memcpy(output,&skin,sizeof(skin));}
        else if(version==6) {const NcmaUiApiV1 ui{sizeof(ui),1,7,CreateUiRenderer,CreateUiImage,CreateUiList,DestroyUi,SubmitUi,UiStats};std::memcpy(output,&ui,sizeof(ui));}
        else if(version==7) {const NcmaUiTargetApiV1 ui{sizeof(ui),1,7,CreateUiTarget,DestroyUiTarget,SubmitUiTarget,CaptureUiTarget,AcquireUiTarget,ReleaseUiTarget,UiTargetStats};std::memcpy(output,&ui,sizeof(ui));}
        else if(version==8) {const NcmaShaderApiV1 shader{sizeof(shader),1,1,CompileShader,ValidateShaderPreparation};std::memcpy(output,&shader,sizeof(shader));}
        else if(version==9) {const NcmaShaderPipelineApiV1 shader{sizeof(shader),1,1,CopyToneSource,CreateRegisteredScene,ReplaceRegisteredTone};std::memcpy(output,&shader,sizeof(shader));}
        else if(version==10) {const NcmaShaderStagesApiV1 shader{sizeof(shader),1,3,CopyStageSource,CreateRegisteredStages,ReplaceRegisteredStages,CreateRegisteredSkin,ReplaceRegisteredSkin};std::memcpy(output,&shader,sizeof(shader));}
        else if(version==11) {const NcmaUiShadersApiV1 shader{sizeof(shader),1,1,CopyUiShaderSource,CreateUiShaders,ReplaceUiShaders};std::memcpy(output,&shader,sizeof(shader));}
        else if(version==12) {const NcmaRuntimeShadersApiV1 shader{sizeof(shader),1,1,ValidateRuntimeShaders};std::memcpy(output,&shader,sizeof(shader));}
        else if(version==13) {const NcmaEnvironmentCookApiV1 cook{sizeof(cook),1,1,CookEnvironment,ValidateEnvironmentCook};std::memcpy(output,&cook,sizeof(cook));}
        else if(version==14) {const NcmaEnvironmentGpuApiV1 gpu{sizeof(gpu),1,3,PublishEnvironmentGpu,DestroyEnvironmentGpu,CaptureEnvironmentGpu,EnvironmentGpuStats,ValidateEnvironmentGpu};std::memcpy(output,&gpu,sizeof(gpu));}
        else if(version==15) {const NcmaEnvironmentSceneApiV1 env{sizeof(env),1,3,CopyEnvironmentSceneSource,ValidateEnvironmentShaders,CreateEnvironmentScene,ReplaceEnvironmentScene,BindEnvironmentScene};std::memcpy(output,&env,sizeof(env));}
        else if(version==16) {const NcmaPostApiV1 post{sizeof(post),1,3,ValidatePostPrograms,BindPostProcess,PostProcessStats};std::memcpy(output,&post,sizeof(post));}
        else if(version==17) {const NcmaRendererFrameApiV1 frame{sizeof(frame),1,1,ReadRendererFrame,AbandonRendererFrame};std::memcpy(output,&frame,sizeof(frame));}
        else if(version==18) {const NcmaRendererFaultApiV1 fault{sizeof(fault),1,1,ReadRendererFault};std::memcpy(output,&fault,sizeof(fault));}
        else if(version==19) {const NcmaRendererTimingApiV1 timing{sizeof(timing),1,1,ReadGpuSample};std::memcpy(output,&timing,sizeof(timing));}
        else if(version==20) {const NcmaRendererWorkTimingApiV1 timing{sizeof(timing),1,3,ReadGpuWorkSample};std::memcpy(output,&timing,sizeof(timing));}
        else {const NcmaRendererTimingSnapshotApiV1 timing{sizeof(timing),1,7,ReadGpuTimingSnapshot};std::memcpy(output,&timing,sizeof(timing));}
        return NCMA_OK;
    });
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
        renderer->backend->BindGuiBackbuffer();return NCMA_OK;});
}
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_gui_image_v1(uint64_t context,uint64_t handle,NcmaGpuResourceV3 token,uint64_t frame,uint32_t retain,ID3D11ShaderResourceView** output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output||retain>1||(retain&&frame))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(token.generation!=handle||!renderer->targets.contains(token.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        auto& target=*renderer->targets.at(token.value);
        if(frame&&(!renderer->active||renderer->lastFrame!=frame||target.lastFrame!=frame))return NcmaPlugin::Error(error,NCMA_BUSY,"GUI image requires exact submitted target frame.");
        auto* view=renderer->backend->BorrowTextureView(target.color);
        if(!view)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(retain){if(target.guiPins>=64)return NcmaPlugin::Error(error,NCMA_BUSY,"GUI image pin budget.");target.guiPins++;}
        *output=view;return NCMA_OK;
    });
}
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_release_gui_image_v1(uint64_t context,uint64_t handle,NcmaGpuResourceV3 token,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(token.generation!=handle||!renderer->targets.contains(token.value)||!renderer->targets.at(token.value)->guiPins)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        renderer->targets.at(token.value)->guiPins--;return NCMA_OK;
    });
}
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_gui_cached_image_v1(uint64_t context,uint64_t handle,NcmaGpuResourceV3 token,uint64_t frame,uint32_t retain,ID3D11ShaderResourceView** output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!output||retain>1||!frame)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        auto it=renderer->uiLeases.find(token.value);
        if(token.generation!=handle||it==renderer->uiLeases.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        auto& lease=it->second;auto& target=renderer->uiTargets.at(lease.target);
        if(lease.frame!=frame||target.content!=lease.content||target.produced!=lease.produced||
            (renderer->active?renderer->lastFrame!=frame:renderer->lastFrame>=frame))return NcmaPlugin::Error(error,NCMA_BUSY,"Stale cached image lease.");
        auto* view=renderer->backend->BorrowTextureView(target.color);if(!view)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(retain){if(lease.pins>=64)return NcmaPlugin::Error(error,NCMA_BUSY);lease.pins++;}
        *output=view;return NCMA_OK;
    });
}
extern "C" NCMA_RENDERER_INTERNAL uint32_t NCMA_CALL ncma_renderer_release_gui_cached_image_v1(uint64_t context,uint64_t handle,NcmaGpuResourceV3 token,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        auto it=renderer->uiLeases.find(token.value);if(token.generation!=handle||it==renderer->uiLeases.end()||!it->second.pins)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        it->second.pins--;return NCMA_OK;});
}
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major,uint32_t minor,void* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    const NcmaRendererApiV1 table{{sizeof(NcmaRendererApiV1),1,0,NCMA_RENDERER,15,Initialize,Shutdown,Status,Diagnostic},
        Create,CreateResources,DestroyResources,Submit,Present,Resize,Stats,WaitIdle,Destroy,Capture};
    if(minor==0)return NcmaPlugin::CopyApi(major,minor,output,capacity,error,table);
    NcmaRendererApiV1_1 modern{table,ConfigureReference}; modern.base.module.struct_size=sizeof(modern);modern.base.module.minor=1;
    if(minor==1)return NcmaPlugin::CopyApi(major,minor,output,capacity,error,modern,1);
    NcmaRendererApiV1_2 latest{modern,QuerySceneRender};latest.base.base.module.struct_size=sizeof(latest);latest.base.base.module.minor=2;
    return NcmaPlugin::CopyApi(major,minor,output,capacity,error,latest,2);
}
