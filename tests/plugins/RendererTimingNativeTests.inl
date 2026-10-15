static void RendererTimingNativeTests(const NcmaRendererApiV1_2& api,uint64_t context,const NcmaRendererDescriptionV1& desc) {
    int checks=0;auto pass=[&](bool ok,const char* label){Check(ok,label);++checks;};
    NcmaErrorV1 error{};NcmaRendererTimingApiV1 timing{};
    pass(api.query_scene_render(context,19,&timing,23,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==24&&!timing.read,"Timing short table atomic");
    pass(api.query_scene_render(context,19,&timing,24,&error)==NCMA_OK&&timing.struct_size==24&&timing.version==1&&timing.capabilities==1&&timing.read,"Timing query19/v1");
    const auto& base=api.base.base;uint64_t handle=0;
    pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK,"Timing actual renderer");
    auto read=[&]{NcmaGpuSampleV1 s{};pass(timing.read(context,handle,&s,&error)==NCMA_OK,"Timing read");return s;};
    auto empty=read();pass(empty.generation==handle&&empty.state==NCMA_GPU_SAMPLE_UNAVAILABLE&&!empty.sequence&&!empty.origin_frame&&!empty.milliseconds&&empty.domain==1,"No fabricated initial sample");
    NcmaGpuSampleV1 untouched{};untouched.struct_size=123;
    uint32_t foreign=0;std::thread worker([&]{NcmaErrorV1 e{};foreign=timing.read(context,handle,&untouched,&e);});worker.join();
    pass(foreign==NCMA_WRONG_THREAD&&untouched.struct_size==123,"Timing foreign thread atomic");
    busy=true;const auto reentry=timing.read(context,handle,&untouched,&error);busy=false;
    pass(reentry==NCMA_BUSY&&untouched.struct_size==123,"Timing reentry atomic");
    pass(timing.read(context,handle,nullptr,&error)==NCMA_INVALID_ARGUMENT,"Timing null output");
    NcmaRenderFrameV1 frame{};frame.struct_size=sizeof(frame);frame.pass_count=1;frame.viewport[2]=frame.viewport[3]=256;frame.exposure=1;frame.roughness=.5f;
    NcmaRenderPassV1 clear{NCMA_RENDER_CLEAR,0,0,{.2f,.3f,.4f,1}};
    auto submit=[&](uint64_t id){frame.frame=id;pass(base.submit(context,handle,&frame,&clear,&error)==NCMA_OK,"Timing actual submit");};
    auto present=[&]{pass(base.present(context,handle,&error)==NCMA_OK,"Timing actual Present");};
    auto complete=[&]{pass(base.wait_idle(context,handle,&error)==NCMA_OK,"Explicit TEST synchronization, not read wait");};
    submit(101);auto recording=read();pass(recording.state==NCMA_GPU_SAMPLE_RECORDING&&recording.origin_frame==101&&recording.sequence==1&&!recording.milliseconds,"Exact admitted origin while recording");present();
    testTimingResult=S_FALSE;
    auto pending=read();pass(pending.state==NCMA_GPU_SAMPLE_PENDING&&pending.sequence==1&&pending.origin_frame==101&&pending.observed_frame==101&&!pending.milliseconds,"Pending distinct from valid");
    for(uint64_t id=103;id<=107;id+=2){submit(id);present();}
    auto delayed=read();pass(delayed.state==NCMA_GPU_SAMPLE_PENDING&&delayed.origin_frame==101&&delayed.observed_frame==107&&delayed.sequence==1&&delayed.skipped==3,"Bounded busy slot does not relabel late result");
    testTimingResult=S_OK;complete();auto valid=read();
    pass(valid.state==NCMA_GPU_SAMPLE_VALID&&valid.origin_frame==101&&valid.observed_frame==107&&valid.sequence==1&&std::isfinite(valid.milliseconds)&&valid.milliseconds>=0,"Actual late GPU result attributed to source frame");
    pass(std::memcmp(&valid,&(delayed=read()),sizeof(valid))==0,"Repeated read stable no duplicate sample");
    for(uint32_t state:{uint32_t(NCMA_GPU_SAMPLE_DISJOINT),uint32_t(NCMA_GPU_SAMPLE_INVALID),uint32_t(NCMA_GPU_SAMPLE_FAILED)}) {
        submit(110+state);present();complete();
        testTimingDisjoint=state==NCMA_GPU_SAMPLE_DISJOINT;testTimingInvalid=state==NCMA_GPU_SAMPLE_INVALID;testTimingResult=state==NCMA_GPU_SAMPLE_FAILED?E_FAIL:S_OK;
        auto rejected=read();testTimingDisjoint=testTimingInvalid=false;testTimingResult=S_OK;
        pass(rejected.state==state&&!rejected.milliseconds&&rejected.origin_frame==110+state&&rejected.sequence==state-2,"Invalid/disjoint/failure never reuse old milliseconds");
    }
    submit(121);present();auto failed=read();
    pass(failed.state==NCMA_GPU_SAMPLE_FAILED&&failed.sequence==4&&failed.origin_frame==116&&failed.skipped==4&&!failed.milliseconds,"Query failure disables slot reuse, not ordinary rendering");
    if(renderer->stats.validation_errors||renderer->stats.validation_warnings)std::cerr<<renderer->diagnostics<<'\n';
    pass(renderer->stats.validation_errors==0&&renderer->stats.validation_warnings==0,"Failed query never reuses uncollected timestamps; API0/0");
    pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Failed timing device closes normally");
    const uint64_t failedGeneration=handle;pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK&&handle!=failedGeneration,"New device permits new timing after query failure");
    submit(123);NcmaRendererFrameApiV1 frames{};pass(api.query_scene_render(context,17,&frames,sizeof(frames),&error)==NCMA_OK,"Frame query");
    pass(frames.abandon(context,handle,123,&error)==NCMA_OK,"Actual incomplete frame explicit abandon");
    auto abandoned=read();pass(abandoned.state==NCMA_GPU_SAMPLE_ABANDONED&&abandoned.origin_frame==123&&abandoned.sequence==1&&!abandoned.milliseconds,"Abandoned is not GPU cost");
    if(renderer->stats.validation_errors||renderer->stats.validation_warnings)std::cerr<<renderer->diagnostics<<'\n';
    pass(renderer->stats.validation_errors==0&&renderer->stats.validation_warnings==0,"Timing healthy device API0/0");
    pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Timing ordinary close");
    pass(timing.read(context,handle,&untouched,&error)==NCMA_INVALID_HANDLE&&untouched.struct_size==123,"Timing retired handle atomic");
    uint64_t previous=handle;pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK,"Timing fresh generation");
    empty=read();pass(empty.generation!=previous&&empty.state==NCMA_GPU_SAMPLE_UNAVAILABLE&&!empty.sequence&&!empty.origin_frame&&!empty.skipped&&abandoned.generation==previous,"Fresh timing reset does not corrupt copied previous sample");
    pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Timing fresh close");
    std::cout<<"PASS M7.7 GPU source sample "<<checks<<" checks\n";
}
