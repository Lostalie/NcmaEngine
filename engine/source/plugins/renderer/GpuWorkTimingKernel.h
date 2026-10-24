#pragma once
#include "contracts/NcmaRendererTiming.h"
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
#include <stdexcept>
#include <memory>
namespace NcmaEngine::Rendering {
// Numerical scope only; no host policy, frame scheduling or GPU waits.
class GpuWorkTimingKernel final {
    Rhi::D3D11RenderBackend& backend;
    NcmaGpuWorkSampleV1& sample;
    NcmaGpuWorkSampleV1& lastResolved;
    Microsoft::WRL::ComPtr<ID3D11Query> disjoint,begin,end;
    bool measuring=false,pending=false,failed=false;
public:
#ifdef NCMA_GPU_TIMING_TEST
    // Target-wide definition on the white-box executable only (all its TUs).
    static inline HRESULT testResult=S_OK;
    static inline bool testDisjoint=false,testInvalid=false;
#endif
    GpuWorkTimingKernel(Rhi::D3D11RenderBackend& b,NcmaGpuWorkSampleV1& s,NcmaGpuWorkSampleV1& resolved):backend(b),sample(s),lastResolved(resolved){}
    bool Initialize(std::string& error) {
        D3D11_QUERY_DESC desc{D3D11_QUERY_TIMESTAMP_DISJOINT,0};
        for(auto* query:{std::addressof(disjoint),std::addressof(begin),std::addressof(end)}) {
            const HRESULT hr=backend.GetDevice()->CreateQuery(&desc,query->GetAddressOf());
            if(FAILED(hr)){backend.ReportApiFailure(hr);error="GPU work timing query creation failed.";return false;}
            desc.Query=D3D11_QUERY_TIMESTAMP;
        }
        return true;
    }
    void Resolve() {
        if(!pending)return;
        D3D11_QUERY_DATA_TIMESTAMP_DISJOINT data{};UINT64 first=0,last=0;
        auto read=[&](ID3D11Query* q,void* output,UINT size) {
            HRESULT hr;
#ifdef NCMA_GPU_TIMING_TEST
            hr=testResult!=S_OK?testResult:backend.GetDeviceContext()->GetData(q,output,size,D3D11_ASYNC_GETDATA_DONOTFLUSH);
#else
            hr=backend.GetDeviceContext()->GetData(q,output,size,D3D11_ASYNC_GETDATA_DONOTFLUSH);
#endif
            if(FAILED(hr)){backend.ReportApiFailure(hr);Invalidate(NCMA_GPU_SAMPLE_FAILED);}
            return hr;
        };
        if(read(disjoint.Get(),&data,sizeof(data))!=S_OK||read(begin.Get(),&first,sizeof(first))!=S_OK||read(end.Get(),&last,sizeof(last))!=S_OK)return;
#ifdef NCMA_GPU_TIMING_TEST
        if(testDisjoint)data.Disjoint=TRUE;
        if(testInvalid)data.Frequency=0;
#endif
        const bool valid=!data.Disjoint&&data.Frequency&&last>=first;
        sample.timing.state=data.Disjoint?NCMA_GPU_SAMPLE_DISJOINT:valid?NCMA_GPU_SAMPLE_VALID:NCMA_GPU_SAMPLE_INVALID;
        sample.timing.milliseconds=valid?static_cast<double>(last-first)*1000.0/static_cast<double>(data.Frequency):0;
        lastResolved=sample;
        pending=false;
    }
    void Begin(uint64_t frame,uint64_t resource=0,uint64_t revision=0) {
        Resolve();
        if(pending||failed){if(sample.timing.skipped!=UINT64_MAX)++sample.timing.skipped;return;}
        if(measuring)throw std::logic_error("Nested GPU work timing scope.");
        if(sample.timing.sequence==UINT64_MAX)throw std::overflow_error("GPU work sample sequence exhausted.");
        ++sample.timing.sequence;sample.timing.origin_frame=frame;sample.timing.state=NCMA_GPU_SAMPLE_RECORDING;sample.timing.milliseconds=0;
        sample.resource=resource;sample.content_revision=revision;
        auto* dc=backend.GetDeviceContext();dc->Begin(disjoint.Get());dc->End(begin.Get());measuring=true;
    }
    void End() {
        if(!measuring)return;
        auto* dc=backend.GetDeviceContext();dc->End(end.Get());dc->End(disjoint.Get());
        measuring=false;pending=true;sample.timing.state=NCMA_GPU_SAMPLE_PENDING;
    }
    void Invalidate(uint32_t state) noexcept {
        if(measuring){auto* dc=backend.GetDeviceContext();dc->End(end.Get());dc->End(disjoint.Get());}
        measuring=pending=false;failed=true;sample.timing.state=state;sample.timing.milliseconds=0;
    }
};
}
