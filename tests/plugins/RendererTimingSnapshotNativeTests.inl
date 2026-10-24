static void RendererTimingSnapshotNativeTests(const NcmaRendererApiV1_2& api,uint64_t context,const NcmaRendererDescriptionV1& desc) {
    int checks=0;auto pass=[&](bool ok,const char* label){Check(ok,label);++checks;};
    NcmaErrorV1 error{};NcmaRendererTimingSnapshotApiV1 snapshots{};const auto& base=api.base.base;
    pass(api.query_scene_render(context,21,&snapshots,23,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==24&&!snapshots.read,"Snapshot short table atomic");
    pass(api.query_scene_render(context,21,&snapshots,24,&error)==NCMA_OK&&snapshots.struct_size==24&&snapshots.version==1&&snapshots.capabilities==7&&snapshots.read,"Snapshot query21/v1");
    uint64_t handle=0;pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK,"Snapshot renderer");
    auto read=[&](uint32_t domain){NcmaGpuTimingSnapshotV1 s{};pass(snapshots.read(context,handle,domain,&s,&error)==NCMA_OK,"Snapshot copied read");return s;};
    const auto resources=renderer->backend->GetLiveResourceCount();
    for(uint32_t domain:{1u,2u,3u}) {
        auto s=read(domain);pass(!s.current.timing.state&&!s.last_resolved.timing.state&&!s.last_resolved.timing.sequence&&
            s.current.timing.generation==handle&&s.last_resolved.timing.generation==handle&&s.last_resolved.timing.domain==domain,"Snapshot empty domain explicit");
    }
    pass(!renderer->skinKernel&&!renderer->uiTiming&&!renderer->ui&&resources==renderer->backend->GetLiveResourceCount(),"Snapshot read no lazy workload");
    NcmaGpuTimingSnapshotV1 untouched{};untouched.current.timing.struct_size=123;
    for(uint32_t domain:{0u,4u})pass(snapshots.read(context,handle,domain,&untouched,&error)==NCMA_UNSUPPORTED_FEATURE&&untouched.current.timing.struct_size==123,"Snapshot wrong domain atomic");
    pass(snapshots.read(context,handle,1,nullptr,&error)==NCMA_INVALID_ARGUMENT,"Snapshot null");
    uint32_t foreign=0;std::thread t([&]{NcmaErrorV1 e{};foreign=snapshots.read(context,handle,1,&untouched,&e);});t.join();
    pass(foreign==NCMA_WRONG_THREAD&&untouched.current.timing.struct_size==123,"Snapshot owner guard atomic");
    busy=true;auto reentry=snapshots.read(context,handle,1,&untouched,&error);busy=false;
    pass(reentry==NCMA_BUSY&&untouched.current.timing.struct_size==123,"Snapshot nonreentry atomic");
    NcmaRenderFrameV1 frame{};frame.struct_size=sizeof(frame);frame.pass_count=1;frame.viewport[2]=frame.viewport[3]=256;frame.exposure=1;frame.roughness=.5f;
    NcmaRenderPassV1 clear{NCMA_RENDER_CLEAR,0,0,{.2f,.3f,.4f,1}};
    auto submit=[&](uint64_t id){frame.frame=id;pass(base.submit(context,handle,&frame,&clear,&error)==NCMA_OK,"Snapshot real submit");};
    auto present=[&]{pass(base.present(context,handle,&error)==NCMA_OK,"Snapshot real present");};
    auto wait=[&]{pass(base.wait_idle(context,handle,&error)==NCMA_OK,"Snapshot explicit TEST wait only");};
    submit(201);present();wait(); // No host timing read before next Begin overwrites the current slot.
    submit(203);auto saved=read(1);
    pass(saved.current.timing.state==NCMA_GPU_SAMPLE_RECORDING&&saved.current.timing.origin_frame==203&&saved.current.timing.sequence==2&&
        saved.last_resolved.timing.state==NCMA_GPU_SAMPLE_VALID&&saved.last_resolved.timing.origin_frame==201&&saved.last_resolved.timing.sequence==1,
        "Unread resolution survives next Begin with both identities");
    present();wait();testTimingResult=S_FALSE;auto pending=read(1);
    pass(pending.current.timing.state==NCMA_GPU_SAMPLE_PENDING&&pending.last_resolved.timing.origin_frame==201,"Pending never hides retained result");
    auto repeat=read(1);pass(std::memcmp(&pending,&repeat,sizeof(repeat))==0,"Snapshot repeated copied identity stable");
    testTimingResult=S_OK;testTimingDisjoint=true;auto disjoint=read(1);testTimingDisjoint=false;
    pass(disjoint.last_resolved.timing.state==NCMA_GPU_SAMPLE_DISJOINT&&disjoint.last_resolved.timing.sequence==2&&!disjoint.last_resolved.timing.milliseconds,"Latest invalid resolution replaces earlier valid result");
    submit(205);present();wait();testTimingInvalid=true;auto invalid=read(1);testTimingInvalid=false;
    pass(invalid.last_resolved.timing.state==NCMA_GPU_SAMPLE_INVALID&&invalid.last_resolved.timing.sequence==3,"Invalid retained explicitly");
    submit(207);present();wait();testTimingResult=E_FAIL;auto failed=read(1);testTimingResult=S_OK;
    pass(failed.current.timing.state==NCMA_GPU_SAMPLE_FAILED&&failed.current.timing.sequence==4&&
        failed.last_resolved.timing.state==NCMA_GPU_SAMPLE_INVALID&&failed.last_resolved.timing.sequence==3,"Failure visible beside historical resolution");
    pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Snapshot device drain");
    pass(snapshots.read(context,handle,1,&untouched,&error)==NCMA_INVALID_HANDLE&&untouched.current.timing.struct_size==123,"Snapshot retired handle atomic");
    pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK,"Snapshot new generation");
    auto fresh=read(1);pass(!fresh.last_resolved.timing.state&&!fresh.last_resolved.timing.sequence&&fresh.current.timing.generation!=saved.current.timing.generation&&saved.last_resolved.timing.origin_frame==201,"Snapshot new device resets while old copy survives");
    submit(209);present();wait();auto beforeAbandon=read(1);submit(211);
    NcmaRendererFrameApiV1 frames{};pass(api.query_scene_render(context,17,&frames,sizeof(frames),&error)==NCMA_OK&&frames.abandon(context,handle,211,&error)==NCMA_OK,"Snapshot explicit abandon");
    auto abandoned=read(1);pass(abandoned.current.timing.state==NCMA_GPU_SAMPLE_ABANDONED&&abandoned.last_resolved.timing.sequence==beforeAbandon.last_resolved.timing.sequence&&abandoned.last_resolved.timing.origin_frame==209,"Abandon does not erase historical result or mask current state");
    Validation(*renderer);pass(!renderer->stats.validation_errors&&!renderer->stats.validation_warnings,"Snapshot API0/0");
    pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Snapshot final drain");
    std::cout<<"PASS E5.D GPU snapshot "<<checks<<" checks\n";
}
