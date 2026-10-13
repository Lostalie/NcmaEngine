namespace PostProcess {
constexpr uint64_t Budget=128ull*1024*1024;
#ifdef NCMA_RENDERER_TEST_WAIT
bool testThrow=false;
bool testDiagnostic=false;
#endif
// Closed binding reflection fixture, not the official/user effect algorithm.
constexpr const char* source=R"(
cbuffer Post:register(b0){float4 Params;};
Texture2D Input0:register(t0);Texture2D Input1:register(t1);SamplerState LinearClamp:register(s0);
struct Full{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
Full VSPost(uint id:SV_VertexID){Full o;o.uv=float2((id<<1)&2,id&2);o.p=float4(o.uv*float2(2,-2)+float2(-1,1),0,1);return o;}
float4 PSOne(Full i):SV_TARGET{return Input0.SampleLevel(LinearClamp,i.uv,0)*Params;}
float4 PSTwo(Full i):SV_TARGET{return Input0.SampleLevel(LinearClamp,i.uv,0)+Input1.SampleLevel(LinearClamp,i.uv,0)*Params;}
)";
struct OwnedPrograms {
    std::vector<NcmaPostProgramV1> programs;
    std::vector<std::vector<uint8_t>> data;
    bool Copy(const NcmaPostProgramV1* input,uint32_t count){
        if(!input||!count||count>8)return false;programs.assign(input,input+count);data.resize(static_cast<size_t>(count)*2);uint64_t bytes=0;
        for(auto& p:programs){if(!RegisteredStages::Pair(p.shaders)||p.reserved||p.inputs<1||p.inputs>2)return false;bytes+=static_cast<uint64_t>(p.shaders.vertex_bytes)+p.shaders.pixel_bytes;}
        if(bytes>8*1024*1024)return false;
        for(size_t i=0;i<programs.size();i++){auto& p=programs[i].shaders;data[i*2].assign(p.vertex,p.vertex+p.vertex_bytes);data[i*2+1].assign(p.pixel,p.pixel+p.pixel_bytes);p.vertex=data[i*2].data();p.pixel=data[i*2+1].data();}return true;
    }
};
uint32_t Code(const uint8_t* data,uint32_t bytes,uint32_t kind,NcmaErrorV1* error){
    auto& expected=renderer->postReferences[kind];
    if(!expected){ComPtr<ID3DBlob> code;const char* entry=kind==0?"VSPost":kind==1?"PSOne":"PSTwo";
        if(FAILED(D3DCompile(source,std::strlen(source),"Ncma.PostContract.v1",nullptr,nullptr,entry,kind==0?"vs_5_0":"ps_5_0",ShaderCompilation::Flags,0,&code,nullptr))||
           FAILED(D3DReflect(code->GetBufferPointer(),code->GetBufferSize(),__uuidof(ID3D11ShaderReflection),&expected)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"post_internal_reflection");}
    ComPtr<ID3D11ShaderReflection> actual;const char* reason=nullptr;
    if(FAILED(D3DReflect(data,bytes,__uuidof(ID3D11ShaderReflection),&actual))||!RegisteredStages::SameBindings(actual.Get(),expected.Get(),reason))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"post_actual_bindings");return NCMA_OK;
}
uint32_t Validate(const OwnedPrograms& owned,NcmaErrorV1* error){
    for(const auto& p:owned.programs){auto v=Code(p.shaders.vertex,p.shaders.vertex_bytes,0,error);if(v)return v;v=Code(p.shaders.pixel,p.shaders.pixel_bytes,p.inputs,error);if(v)return v;}return NCMA_OK;
}
bool Graph(const NcmaPostDescriptionV1& d,uint64_t& bytes){
    if(!d.width||!d.height||d.width>4096||d.height>4096||!d.target_count||d.target_count>32||!d.pass_count||d.pass_count>32||!d.targets||!d.passes||!d.output||d.output>d.target_count)return false;
    bytes=16;for(uint32_t i=0;i<d.target_count;i++){const auto& t=d.targets[i];if(!t.width||!t.height||t.width>4096||t.height>4096||t.format!=1||t.color_space!=1)return false;bytes+=static_cast<uint64_t>(t.width)*t.height*8;}
    if(bytes>Budget||d.targets[d.output-1].width!=d.width||d.targets[d.output-1].height!=d.height)return false;
    std::array<bool,33> ready{},used{};ready[0]=true;
    for(uint32_t i=0;i<d.pass_count;i++){const auto& p=d.passes[i];
        if(p.program>=d.program_count||p.input0>d.target_count||!ready[p.input0]||!p.output||p.output>d.target_count||ready[p.output]||p.input0==p.output)return false;
        if(d.programs[p.program].inputs==1?p.input1!=UINT32_MAX:p.input1>d.target_count||!ready[p.input1]||p.input1==p.output)return false;
        for(float x:p.parameters)if(!std::isfinite(x)||std::abs(x)>65504)return false;ready[p.output]=true;
    }
    if(d.passes[d.pass_count-1].output!=d.output)return false;used[d.output]=true;
    for(uint32_t i=d.pass_count;i>0;i--){const auto& p=d.passes[i-1];if(!used[p.output])return false;used[p.input0]=true;if(p.input1!=UINT32_MAX)used[p.input1]=true;}
    for(uint32_t i=1;i<=d.target_count;i++)if(!ready[i]||!used[i])return false;return true;
}
}
uint32_t NCMA_CALL ValidatePostPrograms(uint64_t context,uint64_t handle,const NcmaPostProgramV1* programs,uint32_t count,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ResourceReady(context,handle,error);if(v)return v;BusyScope busyScope;PostProcess::OwnedPrograms copy;
        if(!copy.Copy(programs,count))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"post_program_budget");return PostProcess::Validate(copy,error);});
}
uint32_t NCMA_CALL BindPostProcess(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,const NcmaPostDescriptionV1* input,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        auto v=ResourceReady(context,handle,error);if(v)return v;
        if(key.generation!=handle||!renderer->scenePipelines.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        auto& scene=*renderer->scenePipelines.at(key.value);BusyScope busyScope;std::string message;std::unique_ptr<Rendering::PostProcessKernel> candidate;
        bool parametersOnly=false;std::vector<NcmaPostPassV1> parameterCandidate;
        if(input){const auto d=*input;if(d.struct_size!=56||d.version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
            if(d.width!=scene.width||d.height!=scene.height||d.target_count>32||d.pass_count>32||!d.target_count||!d.pass_count||!d.targets||!d.passes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"post_dimensions");
            PostProcess::OwnedPrograms copied;if(!copied.Copy(d.programs,d.program_count))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"post_program_budget");
            std::vector<NcmaPostTargetV1> targets(d.targets,d.targets+d.target_count);std::vector<NcmaPostPassV1> passes(d.passes,d.passes+d.pass_count);
            auto owned=d;owned.programs=copied.programs.data();owned.targets=targets.data();owned.passes=passes.data();uint64_t bytes=0;
            if(!PostProcess::Graph(owned,bytes))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"post_graph");
            if((v=PostProcess::Validate(copied,error)))return v;
            parametersOnly=scene.post&&scene.post->SameLayout(owned);
            if(parametersOnly)parameterCandidate=std::move(passes);
            else {if(bytes>PostProcess::Budget-renderer->postStats.resident_bytes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"post_live_candidate_budget");
                candidate=std::make_unique<Rendering::PostProcessKernel>(*renderer->backend);if(!candidate->Initialize(owned,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"post_device_failure"):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"post_candidate_failed");}
        }
#ifdef NCMA_RENDERER_TEST_WAIT
        if(PostProcess::testThrow)throw std::runtime_error("Injected post prepublication failure");
        if(PostProcess::testDiagnostic&&renderer->validation)renderer->validation->AddApplicationMessage(D3D11_MESSAGE_SEVERITY_WARNING,"Injected post candidate diagnostic");
#endif
        Validation(*renderer);
        if(renderer->stats.validation_errors||renderer->stats.validation_warnings)return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"post_candidate_diagnostics; old_binding_retained");
        if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"post_device_failure");
        if(parametersOnly){NcmaPostDescriptionV1 d{};d.passes=parameterCandidate.data();scene.post->UpdateParameters(d);++renderer->postStats.publications;return NCMA_OK;}
        if(!Wait(*renderer,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"post_device_failure"):NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,"post_drain_old_retained");
        if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"post_device_failure");
        Validation(*renderer);
        if(renderer->stats.validation_errors||renderer->stats.validation_warnings)return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"post_completion_diagnostics; old_binding_retained");
        renderer->backend->GetDeviceContext()->ClearState();auto& stats=renderer->postStats;
        if(scene.post){--stats.bindings;stats.targets-=scene.post->TargetCount();stats.programs-=scene.post->ProgramCount();stats.resident_bytes-=scene.post->bytes;}
        scene.post.swap(candidate);
        if(scene.post){++stats.bindings;stats.targets+=scene.post->TargetCount();stats.programs+=scene.post->ProgramCount();stats.resident_bytes+=scene.post->bytes;}
        ++stats.publications;return NCMA_OK;
    });
}
uint32_t NCMA_CALL PostProcessStats(uint64_t context,uint64_t handle,NcmaPostStatsV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=Instance(context,handle,error);if(v)return v;if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);renderer->postStats.generation=handle;*output=renderer->postStats;return NCMA_OK;});
}
