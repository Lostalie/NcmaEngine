static void RendererWorkTimingNativeTests(const NcmaRendererApiV1_2& api,uint64_t context,const NcmaRendererDescriptionV1& desc) {
    int checks=0;auto pass=[&](bool ok,const char* label){Check(ok,label);++checks;};
    using Timing=NcmaEngine::Rendering::GpuWorkTimingKernel;
    NcmaErrorV1 error{};NcmaRendererWorkTimingApiV1 work{};const auto& base=api.base.base;
    pass(api.query_scene_render(context,20,&work,23,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==24&&!work.read,"Work timing short table atomic");
    pass(api.query_scene_render(context,20,&work,24,&error)==NCMA_OK&&work.version==1&&work.capabilities==3&&work.read,"Work timing query20");
    NcmaRendererTimingSnapshotApiV1 snapshots{};
    pass(api.query_scene_render(context,21,&snapshots,sizeof(snapshots),&error)==NCMA_OK,"Work snapshot API");
    uint64_t handle=0;pass(base.create_renderer(context,&desc,&handle,&error)==NCMA_OK,"Work timing device");
    auto read=[&](uint32_t domain){NcmaGpuWorkSampleV1 s{};pass(work.read(context,handle,domain,&s,&error)==NCMA_OK,"Work timing copied read");return s;};
    auto snapshot=[&](uint32_t domain){NcmaGpuTimingSnapshotV1 s{};pass(snapshots.read(context,handle,domain,&s,&error)==NCMA_OK,"Work snapshot read");return s;};
    const auto resources=renderer->backend->GetLiveResourceCount();
    for(uint32_t domain:{2u,3u}){auto s=read(domain);pass(!s.timing.state&&!s.timing.sequence&&s.timing.generation==handle&&s.timing.domain==domain&&!s.resource&&!s.content_revision,"Missing work domain explicitly unavailable");}
    pass(!renderer->skinKernel&&!renderer->uiTiming&&!renderer->ui&&renderer->backend->GetLiveResourceCount()==resources,"Read never initializes missing workloads");
    NcmaGpuWorkSampleV1 untouched{};untouched.timing.struct_size=123;
    pass(work.read(context,handle,1,&untouched,&error)==NCMA_UNSUPPORTED_FEATURE&&untouched.timing.struct_size==123,"Wrong domain atomic");
    uint32_t foreign=0;std::thread thread([&]{NcmaErrorV1 e{};foreign=work.read(context,handle,2,&untouched,&e);});thread.join();
    pass(foreign==NCMA_WRONG_THREAD&&untouched.timing.struct_size==123,"Work timing owner thread");
    busy=true;const auto reentry=work.read(context,handle,2,&untouched,&error);busy=false;pass(reentry==NCMA_BUSY&&untouched.timing.struct_size==123,"Work timing nonreentry");
    NcmaSkinApiV5 skin{};pass(api.query_scene_render(context,5,&skin,sizeof(skin),&error)==NCMA_OK,"Work skin API");
    uint32_t indices[]{0,1,2};std::array<uint8_t,240> vertices{};
    for(int i=0;i<3;i++){float row[12]{static_cast<float>(i==1),static_cast<float>(i==2),0,0,0,1,0,0,1,0,0,1};float weight=1;std::memcpy(vertices.data()+i*80,row,48);std::memcpy(vertices.data()+i*80+64,&weight,4);}
    NcmaSkinMeshV5 meshDesc{{56,3,3,3,240,12,80,0,vertices.data(),indices},1,0};NcmaGpuMeshV1 mesh{};
    pass(skin.create(context,handle,&meshDesc,&mesh,&error)==NCMA_OK,"Actual timed skin mesh");
    NcmaSkinPaletteV5 palette{};for(int i:{0,5,10,15})palette.model[i]=palette.normal[i]=1;
    auto update=[&](uint64_t frame){NcmaSkinBatchV5 batch{32,1,frame,handle,1,0};NcmaSkinRequestV5 job{mesh,0,1};pass(skin.update(context,handle,&batch,&job,&palette,&error)==NCMA_OK,"Actual skin dispatch, timing independent from fence");};
    auto wait=[&]{pass(base.wait_idle(context,handle,&error)==NCMA_OK,"Explicit test GPU completion");};
    update(5);wait();auto first=read(2);pass(first.timing.state==NCMA_GPU_SAMPLE_VALID&&first.timing.origin_frame==5&&first.timing.sequence==1&&!first.resource&&!first.content_revision&&!renderer->stats.submitted_frames,"Skin attributed without a main frame");
    update(9);wait();Timing::testResult=S_FALSE;
    for(uint64_t frame=11;frame<=19;frame+=2){update(frame);wait();}
    auto delayed=read(2);pass(delayed.timing.state==NCMA_GPU_SAMPLE_PENDING&&delayed.timing.origin_frame==9&&delayed.timing.observed_frame==19&&delayed.timing.skipped==5,"More than three palette buffers proceed while timing unavailable");
    auto retainedSkin=snapshot(2);pass(retainedSkin.current.timing.state==NCMA_GPU_SAMPLE_PENDING&&retainedSkin.last_resolved.timing.state==NCMA_GPU_SAMPLE_VALID&&retainedSkin.last_resolved.timing.origin_frame==5&&retainedSkin.last_resolved.timing.sequence==1,"Pending skin retains previous batch");
    Timing::testResult=S_OK;auto late=read(2);pass(late.timing.state==NCMA_GPU_SAMPLE_VALID&&late.timing.origin_frame==9&&late.timing.sequence==2,"Late skin timing has exact original batch");
    update(21);wait();Timing::testResult=E_FAIL;auto failed=read(2);Timing::testResult=S_OK;
    palette.model[12]=.25f;update(23);wait();auto still=read(2);
    pass(failed.timing.state==NCMA_GPU_SAMPLE_FAILED&&still.timing.state==NCMA_GPU_SAMPLE_FAILED&&still.timing.origin_frame==21&&still.timing.sequence==3,"Failed skin timestamp quarantined, dispatch continues");
    std::array<uint8_t,144> captured{};pass(skin.capture(context,handle,mesh,captured.data(),144,&error)==NCMA_OK,"Actual skin output after timing failure");float x=0;std::memcpy(&x,captured.data(),4);pass(x==.25f,"Skin numerical output still advances with failed telemetry");
    pass(skin.destroy(context,handle,mesh,&error)==NCMA_OK&&read(2).timing.state==NCMA_GPU_SAMPLE_ABANDONED,"Retired skin owner invalidates live timing");
    auto retiredSkin=snapshot(2);pass(retiredSkin.current.timing.state==NCMA_GPU_SAMPLE_ABANDONED&&retiredSkin.last_resolved.timing.origin_frame==9,"Retired kernel retains historical copy");
    pass(skin.create(context,handle,&meshDesc,&mesh,&error)==NCMA_OK,"Skin owner recreated");update(25);wait();auto recreated=read(2);
    pass(recreated.timing.state==NCMA_GPU_SAMPLE_VALID&&recreated.timing.sequence==4&&recreated.timing.origin_frame==25&&recreated.timing.generation==first.timing.generation,"Same device new owner never resets sequence");
    update(27);wait();update(29);wait(); // No timing read between these two real batches.
    Timing::testResult=S_FALSE;auto unreadSkin=snapshot(2);Timing::testResult=S_OK;
    pass(unreadSkin.current.timing.state==NCMA_GPU_SAMPLE_PENDING&&unreadSkin.current.timing.origin_frame==29&&
        unreadSkin.last_resolved.timing.state==NCMA_GPU_SAMPLE_VALID&&unreadSkin.last_resolved.timing.origin_frame==27&&unreadSkin.last_resolved.timing.sequence==5,"Unread skin resolution survives next batch");
    pass(skin.destroy(context,handle,mesh,&error)==NCMA_OK,"Skin drain");
    NcmaUiApiV1 ui{};NcmaUiTargetApiV1 targets{};pass(api.query_scene_render(context,6,&ui,sizeof(ui),&error)==NCMA_OK&&api.query_scene_render(context,7,&targets,sizeof(targets),&error)==NCMA_OK,"UI APIs");
    uint8_t white[]{255,255,255,255};NcmaUiImageV1 imageDesc{sizeof(imageDesc),1,1,4,white};NcmaUiKeyV1 image{},list{},a{},b{};
    pass(ui.create_image(context,handle,&imageDesc,&image,&error)==NCMA_OK,"UI image");
    NcmaUiVertexV1 v[3]{{{0,0},{0,0},{1,0,0,1},{0,0},{32,32},0},{{32,0},{1,0},{1,0,0,1},{32,0},{32,32},0},{{0,32},{0,1},{1,0,0,1},{0,32},{32,32},0}};
    NcmaUiBatchV1 batch{0,3,image,{0,0,32,32}};NcmaUiListV1 listDesc{sizeof(listDesc),3,1,0,v,&batch};
    pass(ui.create_list(context,handle,&listDesc,&list,&error)==NCMA_OK,"UI list");NcmaUiTargetDescriptionV1 td{16,32,32,0};
    pass(targets.create_target(context,handle,&td,&a,&error)==NCMA_OK&&targets.create_target(context,handle,&td,&b,&error)==NCMA_OK,"Two UI targets one timing owner");
    auto produce=[&](NcmaUiKeyV1 target,uint64_t frame,uint64_t revision){NcmaUiTargetFrameV1 f{sizeof(f),0,frame,revision,target,list,{0,0,0,0}};pass(targets.submit(context,handle,&f,&error)==NCMA_OK,"Actual offscreen production");wait();};
    produce(a,33,1);produce(b,33,1); // No timing read of A before B begins.
    Timing::testResult=S_FALSE;auto retainedUi=snapshot(3);Timing::testResult=S_OK;
    auto sa=retainedUi.last_resolved;
    pass(retainedUi.current.timing.state==NCMA_GPU_SAMPLE_PENDING&&retainedUi.current.resource==b.value&&sa.timing.state==NCMA_GPU_SAMPLE_VALID&&sa.resource==a.value&&sa.content_revision==1&&sa.timing.sequence==1,"Unread UI result keeps previous target identity");
    auto sb=read(3);
    pass(sa.timing.origin_frame==33&&sb.timing.origin_frame==33&&sb.timing.sequence==sa.timing.sequence+1&&sa.resource==a.value&&sb.resource==b.value&&sb.content_revision==1,"Same frame independent target productions retain exact identity");
    NcmaUiKeyV1 lease{};pass(targets.acquire(context,handle,b,1,34,&lease,&error)==NCMA_OK&&targets.release(context,handle,lease,&error)==NCMA_OK,"Cached presentation lease");
    auto cached=read(3);pass(std::memcmp(&cached,&sb,sizeof(sb))==0,"Cached reuse is not a new production timing");
    NcmaUiFrameV1 main{sizeof(main),0,34,list,{0,0,0,0}};pass(ui.submit(context,handle,&main,&error)==NCMA_OK&&base.present(context,handle,&error)==NCMA_OK,"Independent main UI frame");wait();
    pass(read(3).timing.origin_frame==33&&renderer->lastFrame==34,"Main presentation does not relabel offscreen source");
    for(uint32_t state:{uint32_t(NCMA_GPU_SAMPLE_DISJOINT),uint32_t(NCMA_GPU_SAMPLE_INVALID),uint32_t(NCMA_GPU_SAMPLE_FAILED)}) {
        produce(a,37+2*state,state-2);Timing::testDisjoint=state==NCMA_GPU_SAMPLE_DISJOINT;Timing::testInvalid=state==NCMA_GPU_SAMPLE_INVALID;Timing::testResult=state==NCMA_GPU_SAMPLE_FAILED?E_FAIL:S_OK;
        auto s=read(3);Timing::testDisjoint=Timing::testInvalid=false;Timing::testResult=S_OK;pass(s.timing.state==state&&!s.timing.milliseconds&&s.resource==a.value&&s.content_revision==state-2,"Invalid UI sample retains source but never cost");
    }
    produce(a,53,5);pass(read(3).timing.state==NCMA_GPU_SAMPLE_FAILED,"Failed UI timing does not block target updates");
    pass(targets.destroy_target(context,handle,a,&error)==NCMA_OK&&targets.destroy_target(context,handle,b,&error)==NCMA_OK&&read(3).timing.state==NCMA_GPU_SAMPLE_ABANDONED,"Last UI target retires timing owner");
    auto retiredUi=snapshot(3);pass(retiredUi.current.timing.state==NCMA_GPU_SAMPLE_ABANDONED&&retiredUi.last_resolved.timing.state==NCMA_GPU_SAMPLE_INVALID&&retiredUi.last_resolved.resource==a.value&&retiredUi.last_resolved.content_revision==3,"UI invalid result retained after failure and resource retirement");
    pass(ui.destroy(context,handle,list,&error)==NCMA_OK&&ui.destroy(context,handle,image,&error)==NCMA_OK,"UI owners drain");
    Validation(*renderer);if(renderer->stats.validation_errors||renderer->stats.validation_warnings)std::cerr<<renderer->diagnostics;
    pass(!renderer->stats.validation_errors&&!renderer->stats.validation_warnings,"Work timing API0/0");
    pass(base.destroy_renderer(context,handle,&error)==NCMA_OK,"Work timing renderer close");
    pass(work.read(context,handle,3,&untouched,&error)==NCMA_INVALID_HANDLE&&untouched.timing.struct_size==123,"Retired renderer work read atomic");
    std::cout<<"PASS M7.7 work timing "<<checks<<" checks\n";
}
