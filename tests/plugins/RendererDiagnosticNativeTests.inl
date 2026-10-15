static void RendererDiagnosticNativeTests(const NcmaRendererApiV1_2& api,uint64_t context,const NcmaRendererDescriptionV1& desc) {
    int checks=0;auto pass=[&](bool ok,const char* label){Check(ok,label);++checks;};
    NcmaErrorV1 error{};NcmaRendererFaultApiV1 diagnostics{};
    pass(api.query_scene_render(context,18,&diagnostics,23,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==24,"Diagnostic short table rejects atomically");
    pass(api.query_scene_render(context,18,&diagnostics,24,&error)==NCMA_OK&&diagnostics.struct_size==24&&diagnostics.version==1&&diagnostics.capabilities==1&&diagnostics.read,"Read-only query18 negotiated");
    const auto& base=api.base.base;
    uint64_t handle=0;testStartupValidationFailure=true;
    const auto failed=base.create_renderer(context,&desc,&handle,&error);testStartupValidationFailure=false;
    pass(failed==NCMA_INTERNAL_ERROR&&!handle&&!renderer,"Failed actual device startup retains no published renderer");
    NcmaRendererFaultV1 startup{};
    pass(diagnostics.read(context,0,&startup,&error)==NCMA_OK&&startup.flags==7&&startup.generation&&startup.frame==0&&startup.operation==NCMA_GPU_FAULT_STARTUP&&startup.native_code==E_NOINTERFACE&&startup.device_reason==S_OK,"Startup raw diagnostic copied before candidate device is released");
    auto invalidBackend=desc;invalidBackend.backend=2;NcmaRendererFaultV1 unchanged{};
    pass(base.create_renderer(context,&invalidBackend,&handle,&error)==NCMA_UNSUPPORTED_FEATURE&&diagnostics.read(context,0,&unchanged,&error)==NCMA_OK&&std::memcmp(&unchanged,&startup,sizeof(startup))==0,"Pre-admission rejection does not relabel the preceding failed startup as a new attempt");
    for(const HRESULT code:{E_FAIL,DXGI_ERROR_DEVICE_REMOVED}) {
        pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK,"New renderer after failed startup/retirement");
        NcmaRendererFaultV1 state{};
        pass(diagnostics.read(context,0,&state,&error)==NCMA_OK&&!state.flags&&startup.native_code==E_NOINTERFACE,"New admitted startup clears native slot but not copied previous value");
        pass(diagnostics.read(context,handle,&state,&error)==NCMA_OK&&!state.flags&&state.generation==handle,"Healthy renderer has no invented failure");
        uint32_t foreign=0;std::thread thread([&]{NcmaErrorV1 e{};NcmaRendererFaultV1 s{};foreign=diagnostics.read(context,handle,&s,&e);});thread.join();
        pass(foreign==NCMA_WRONG_THREAD,"Diagnostic owner-thread boundary");
        busy=true;const auto reentry=diagnostics.read(context,handle,&state,&error);busy=false;
        pass(reentry==static_cast<uint32_t>(NCMA_BUSY),"Diagnostic callback reentry rejected");
        NcmaRenderFrameV1 frame{};frame.struct_size=sizeof(frame);frame.pass_count=1;frame.frame=47;frame.viewport[2]=frame.viewport[3]=256;frame.exposure=1;frame.roughness=.5f;
        NcmaRenderPassV1 clear{NCMA_RENDER_CLEAR,0,0,{.2f,.3f,.4f,1}};
        pass(base.submit(context,handle,&frame,&clear,&error)==NCMA_OK,"Actual GPU submission before simulated Present result");
        const auto presents=renderer->stats.presents;testPresentResult=code;
        const auto result=base.present(context,handle,&error);testPresentResult=S_OK;
        pass(result==static_cast<uint32_t>(code==E_FAIL?NCMA_INTERNAL_ERROR:NCMA_DEVICE_LOST)&&renderer->failed&&renderer->stats.presents==presents,"Typed Present failure no fake success/present");
        pass(diagnostics.read(context,handle,&state,&error)==NCMA_OK&&state.flags==7&&state.operation==NCMA_GPU_FAULT_PRESENT&&state.native_code==code&&state.device_reason==S_OK&&state.frame==47&&state.generation==handle,"Original Present HRESULT/frame/device preserved independently of healthy actual removal reason");
        const auto original=state;
        StopRenderer(NCMA_SHUTDOWN_TIMEOUT);
        pass(renderer->faultResult==result&&diagnostics.read(context,handle,&state,&error)==NCMA_OK&&std::memcmp(&state,&original,sizeof(state))==0,"Later stop/cleanup cannot overwrite first diagnostic or first terminal classification");
        pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Explicit failed renderer retirement");
        state.struct_size=123;
        pass(diagnostics.read(context,handle,&state,&error)==NCMA_INVALID_HANDLE&&state.struct_size==123&&original.native_code==code,"Copied diagnostic survives native retirement; stale reads fail atomically");
    }
    pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK,"Backend diagnostic fixture");
    // White-box malformed numerical candidate bypasses the public CPU validator deliberately.
    // The ordinary kernel must capture its actual D3D error without a manual observer call.
    Rendering::UiKernel kernel(*renderer->backend);const uint8_t pixels[4]{255,255,255,255};
    NcmaUiImageV1 invalid{sizeof(invalid),0,1,4,pixels};uint64_t image=123;std::string message;
    {ExecutionScope attempt(107);pass(!kernel.CreateImage(invalid,image,message)&&image==123,"Actual invalid UI allocation fails without publication");attempt.success=true;}
    NcmaRendererFaultV1 backend{};
    pass(diagnostics.read(context,handle,&backend,&error)==NCMA_OK&&backend.native_code==E_INVALIDARG&&backend.operation==NCMA_GPU_FAULT_NATIVE_API&&backend.frame==107&&renderer->lastFrame==0,"Ordinary kernel observer retains actual HRESULT and attempted frame, not last successful frame");
    // Validation diagnostics deliberately contain this bad-call test; do not label it API0/0.
    pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Backend diagnostic fixture shutdown");
    std::cout<<"PASS M7.6 raw GPU diagnostics "<<checks<<" checks; Present/startup injection, actual invalid allocation\n";
}
