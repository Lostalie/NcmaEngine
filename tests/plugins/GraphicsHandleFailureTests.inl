static void GraphicsHandleFailureTests(uint64_t context,uint64_t device) {
    NcmaErrorV1 error{};
    Check(!renderer->ui,"Identity-failure fixture starts before lazy UI initialization");
    auto fail=[&](auto operation) {
        NcmaPlugin::failHandleAllocation=true;
        const auto result=operation();
        NcmaPlugin::failHandleAllocation=false;
        Check(result==NCMA_INTERNAL_ERROR,"Handle allocation failure is contained at C ABI");
    };
    const auto resources=renderer->backend->GetLiveResourceCount();
    const auto groups=renderer->groups.size();
    uint64_t group=123;
    fail([&]{return CreateResources(context,device,&group,&error);});
    Check(group==123&&renderer->groups.size()==groups&&renderer->backend->GetLiveResourceCount()==resources&&!renderer->failed,"Failed group identity leaves output/resources/renderer intact");
    Check(EnsureUi(&error)==NCMA_OK,"Prepare numerical UI for identity failure checks");
    const auto uiBefore=renderer->ui->Stats(device,false);
    NcmaUiKeyV1 target{123,456};NcmaUiTargetDescriptionV1 desc{sizeof(desc),8,8,0};
    const auto targetCount=renderer->uiTargets.size(),targetBytes=renderer->uiTargetBytes;
    fail([&]{return CreateUiTarget(context,device,&desc,&target,&error);});
    Check(target.value==123&&target.generation==456&&renderer->uiTargets.size()==targetCount&&renderer->uiTargetBytes==targetBytes&&renderer->backend->GetLiveResourceCount()==resources,"Failed target identity does not leak texture or publish output");
    const std::array<uint8_t,4> rgba{255,255,255,255};NcmaUiImageV1 image{sizeof(image),1,1,4,rgba.data()};
    NcmaUiKeyV1 imageKey{123,456};
    fail([&]{return CreateUiImage(context,device,&image,&imageKey,&error);});
    auto after=renderer->ui->Stats(device,false);
    Check(imageKey.value==123&&imageKey.generation==456&&after.images==uiBefore.images&&after.resident_bytes==uiBefore.resident_bytes&&after.uploaded_bytes==uiBefore.uploaded_bytes,"Failed UI image identity no publication or accounting");
    Check(CreateUiImage(context,device,&image,&imageKey,&error)==NCMA_OK,"Fresh UI image after failed allocation");
    std::array<NcmaUiVertexV1,3> vertices{};
    for(auto& v:vertices){v.color[0]=v.color[1]=v.color[2]=v.color[3]=1;v.extent[0]=v.extent[1]=1;}
    vertices[1].position[0]=1;vertices[2].position[1]=1;
    NcmaUiBatchV1 batch{0,3,imageKey,{0,0,8,8}};
    NcmaUiListV1 list{sizeof(list),3,1,0,vertices.data(),&batch};NcmaUiKeyV1 listKey{123,456};
    fail([&]{return CreateUiList(context,device,&list,&listKey,&error);});
    Check(listKey.value==123&&listKey.generation==456&&!renderer->ui->Pinned(imageKey.value),"Failed UI list identity retains no image pins");
    Check(DestroyUi(context,device,imageKey,&error)==NCMA_OK,"Image release after list allocation failure");
    after=renderer->ui->Stats(device,false);
    Check(after.images==uiBefore.images&&after.lists==uiBefore.lists&&after.resident_bytes==uiBefore.resident_bytes&&!renderer->failed,"Failed UI allocations leave device healthy and resources balanced");
    // The fixture owns this empty kernel. Restore the original suite's lazy-init precondition.
    renderer->ui.reset();
}
