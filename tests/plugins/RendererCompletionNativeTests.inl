// These faults override only completion-query results in this white-box test TU.
// A healthy real DX11 device is used; this is NOT physical device-loss recovery.
static void RendererCompletionNativeTests(const NcmaRendererApiV1& api,const NcmaResourceRenderApiV3& typed,
    uint64_t context,const NcmaRendererDescriptionV1& description,uint64_t& handle) {
    int checks=0;auto pass=[&](bool ok,const char* label){Check(ok,label);++checks;};
    auto inject=[](int kind){testWaitQueryFailure=kind==0;testWaitPollFailure=kind==1;testWaitTimeout=kind==2;
        testWaitRemovedBefore=kind==3;testWaitRemovedDuring=kind==4;};
    NcmaGpuResourceV3 stale{};uint64_t previous=handle;
    for(int operation=0;operation<4;++operation)for(int kind=0;kind<5;++kind) {
        NcmaErrorV1 error{};inject(-1);
        pass(api.create_renderer(context,&description,&handle,&error)==NCMA_OK&&handle!=previous,"Completion fresh renderer generation");
        NcmaRendererStatsV1 stats{};
        pass(api.stats(context,previous,&stats,&error)==NCMA_INVALID_HANDLE,"Old renderer cannot observe new generation");
        if(stale.value)pass(typed.destroy_resource(context,handle,stale,&error)==NCMA_INVALID_HANDLE,"Old resource generation cannot retire a new device resource");
        uint64_t frameId=0;
        auto capture=[&]() {
            NcmaRenderFrameV1 frame{sizeof(frame),1,++frameId,{},{0,0,static_cast<float>(renderer->stats.width),static_cast<float>(renderer->stats.height)},1,.35F,.28F,0};
            NcmaRenderPassV1 clear{NCMA_RENDER_CLEAR,0,0,{.25F,.5F,.75F,1}};
            pass(api.submit(context,handle,&frame,&clear,&error)==NCMA_OK,"Real clear after generation creation or permitted resize");
            std::vector<uint8_t> pixels(renderer->stats.width*renderer->stats.height*4);uint32_t required=0;
            pass(api.capture(context,handle,pixels.data(),static_cast<uint32_t>(pixels.size()),&required,&error)==NCMA_OK&&required==pixels.size(),"Actual swapchain dimensions/readback");
            pass(api.present(context,handle,&error)==NCMA_OK,"Healthy clear present");return pixels;
        };
        const auto before=capture();
        NcmaGpuResourceV3 target{};const NcmaTargetDescriptionV3 targetDescription{16,8,8,0};
        if(operation!=3)pass(typed.create_target(context,handle,&targetDescription,&target,&error)==NCMA_OK,"Retirement fixture target");
        const auto resident=renderer->backend->GetLiveResourceCount(),presents=renderer->stats.presents;
        inject(kind);
        const auto result=operation==0?api.resize(context,handle,311,193,&error):operation==1?api.wait_idle(context,handle,&error):
            operation==2?typed.destroy_resource(context,handle,target,&error):api.destroy_renderer(context,handle,&error);
        const uint32_t expected=kind<2?NCMA_INTERNAL_ERROR:kind==2?NCMA_SHUTDOWN_TIMEOUT:NCMA_DEVICE_LOST;
        pass(result==expected&&error.code==expected&&error.message_length!=0,"Completion query/poll/timeout/removal has exact structured result");
        NcmaRendererFaultV1 fault{};
        pass(ReadRendererFault(context,handle,&fault,&error)==NCMA_OK&&fault.struct_size==48&&fault.version==1&&fault.generation==handle&&fault.frame==frameId&&fault.result==expected&&fault.backend==1,"Copied first completion diagnostic identifies exact device/frame/result");
        const HRESULT code=kind==0?E_OUTOFMEMORY:kind==1?E_FAIL:kind>=3?DXGI_ERROR_DEVICE_REMOVED:S_OK;
        const uint32_t op=kind==0?NCMA_GPU_FAULT_WAIT_CREATE:kind==1?NCMA_GPU_FAULT_WAIT_POLL:kind==2?NCMA_GPU_FAULT_WAIT_TIMEOUT:kind==3?NCMA_GPU_FAULT_DEVICE_REMOVED:NCMA_GPU_FAULT_WAIT_POLL;
        pass(fault.operation==op&&fault.flags==(kind==2?5u:7u)&&fault.native_code==code&&fault.device_reason==S_OK,"Raw HRESULT bits preserved; timeout has no fabricated HRESULT; actual healthy device reason remains S_OK");
        inject(-1);
        pass(renderer&&renderer->handle==handle&&renderer->stats.width==256&&renderer->stats.height==256&&
            renderer->backend->GetLiveResourceCount()==resident&&renderer->stats.presents==presents,"Failed operation retains exact renderer/size/resources without Present");
        if(target.value)pass(renderer->targets.contains(target.value),"Failed completion never retires retained target");
        if(kind>=3) {
            pass(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason()),"Injected removal is explicitly NOT real device removal");
            pass(renderer->failed&&renderer->faultResult==NCMA_DEVICE_LOST&&!renderer->active&&!renderer->measuring&&!renderer->timingPending&&
                renderer->stats.state==2&&!renderer->stats.gpu_sample_valid&&renderer->stats.gpu_ms==0,"Latched removal fail-stop invalidates GPU timing");
            StopRenderer(NCMA_INTERNAL_ERROR);
            pass(renderer->faultResult==NCMA_DEVICE_LOST,"Later cleanup failure must not overwrite first renderer stop cause");
            pass(api.resize(context,handle,311,193,&error)==NCMA_DEVICE_LOST&&api.present(context,handle,&error)==NCMA_DEVICE_LOST,"Clearing test injection never resumes failed renderer");
            NcmaGpuResourceV3 rejected{123,456};
            pass(typed.create_target(context,handle,&targetDescription,&rejected,&error)==NCMA_DEVICE_LOST&&rejected.value==123&&rejected.generation==456,"Failed renderer refuses allocation with atomic output");
        } else {
            pass(!renderer->failed&&renderer->stats.state==1,"Ordinary query failure or timeout is not fabricated device loss");
            pass(capture()==before,"Exact old image retained after failed completion");
            pass(api.resize(context,handle,311,193,&error)==NCMA_OK&&renderer->stats.width==311&&renderer->stats.height==193,"Explicit healthy resize retry publishes new dimensions");
            pass(capture().size()==311u*193u*4u,"Resized swapchain actually renders/readbacks");
            pass(api.resize(context,handle,0,193,&error)==NCMA_INVALID_ARGUMENT&&renderer->stats.width==311,"Zero-size native resize refuses without destroying last valid target");
        }
        if(target.value){stale=target;pass(typed.destroy_resource(context,handle,target,&error)==NCMA_OK,"Explicit retained resource close retry");}
        pass(api.stats(context,handle,&stats,&error)==NCMA_OK&&!stats.validation_errors&&!stats.validation_warnings&&renderer->backend->GetLiveResourceCount()==0,"Completion final resource zero and real API0/0");
        pass(api.destroy_renderer(context,handle,&error)==NCMA_OK&&!renderer,"Explicit renderer close retry before next generation");previous=handle;
        NcmaRendererFaultV1 staleOutput{};staleOutput.struct_size=123;
        pass(ReadRendererFault(context,handle,&staleOutput,&error)==NCMA_INVALID_HANDLE&&staleOutput.struct_size==123&&fault.generation==handle&&fault.native_code==code,"Copied value survives close; old live handle rejected atomically");
    }
    std::cout<<"PASS M7.6 typed GPU completion/resize/retirement/new-generation "<<checks<<" checks; injected removal only\n";
}
