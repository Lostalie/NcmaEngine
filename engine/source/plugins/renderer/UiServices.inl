// Renderer-private glue. Whole batch validation precedes resource/GPU mutation.
uint32_t EnsureUi(NcmaErrorV1* error) {
    if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
    if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
    if(!renderer->ui){auto kernel=std::make_unique<Rendering::UiKernel>(renderer->backend->GetDevice(),renderer->backend->GetDeviceContext());std::string message;
        if(!kernel->Initialize(message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);renderer->ui=std::move(kernel);}
    return NCMA_OK;
}
uint32_t NCMA_CALL CreateUiImage(uint64_t context,uint64_t handle,const NcmaUiImageV1* input,NcmaUiKeyV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!input||!output||input->struct_size!=sizeof(*input)||!input->pixels||!input->width||!input->height||input->width>4096||input->height>4096||input->byte_count!=static_cast<uint64_t>(input->width)*input->height*4)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        valid=EnsureUi(error);if(valid)return valid;
        if(!renderer->ui->CanImage(*input)||renderer->uiTargetBytes+renderer->ui->Stats(handle,renderer->pureUi).resident_bytes+input->byte_count>128ull*1024*1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        BusyScope scope;uint64_t key=0;std::string message;
        if(!renderer->ui->CreateImage(*input,key,message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        *output={key,handle};return NCMA_OK;});
}
uint32_t NCMA_CALL CreateUiList(uint64_t context,uint64_t handle,const NcmaUiListV1* input,NcmaUiKeyV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!input||!output||!renderer->ui)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        if(!renderer->ui->CanList(*input,handle)||renderer->uiTargetBytes+renderer->ui->Stats(handle,renderer->pureUi).resident_bytes+static_cast<uint64_t>(input->vertex_count)*sizeof(NcmaUiVertexV1)+static_cast<uint64_t>(input->batch_count)*sizeof(NcmaUiBatchV1)>128ull*1024*1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        BusyScope scope;uint64_t key=0;std::string message;
        if(!renderer->ui->CreateList(*input,key,message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        *output={key,handle};return NCMA_OK;});
}
uint32_t NCMA_CALL DestroyUi(uint64_t context,uint64_t handle,NcmaUiKeyV1 key,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!renderer->ui||key.generation!=handle||!renderer->ui->Contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->active||renderer->ui->Pinned(key.value))return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;
        if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
        renderer->backend->GetDeviceContext()->ClearState();renderer->ui->Destroy(key.value);return NCMA_OK;});
}
uint32_t NCMA_CALL SubmitUi(uint64_t context,uint64_t handle,const NcmaUiFrameV1* input,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!input||input->struct_size!=sizeof(*input)||input->overlay>1||!input->frame||input->list.generation!=handle||!renderer->ui||!renderer->ui->HasList(input->list.value))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto f=*input;for(float c:f.clear)if(!std::isfinite(c)||c<0||c>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(f.frame<=renderer->lastUiFrame)return NcmaPlugin::Error(error,NCMA_BUSY,"UI frame already submitted.");
        if(f.overlay?(!renderer->active||f.frame!=renderer->lastFrame):(renderer->active||f.frame<=renderer->lastFrame))return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;ExecutionScope execution;std::string message;
        if(!f.overlay){Rhi::RenderPassDescription pass{};pass.ClearColor=true;std::copy_n(f.clear,4,pass.ClearColorValue);
            if(!renderer->backend->BeginRenderPass(pass,message))return Failure(error,message);}
        renderer->ui->Draw(f.list.value,renderer->stats.width,renderer->stats.height);
        if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"UI device lost");
        if(!f.overlay){renderer->active=true;renderer->lastFrame=f.frame;renderer->stats.submitted_frames++;}renderer->lastUiFrame=f.frame;
        Validation(*renderer);execution.success=true;return NCMA_OK;});
}
uint32_t NCMA_CALL UiStats(uint64_t context,uint64_t handle,NcmaUiStatsV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output=renderer->ui?renderer->ui->Stats(handle,renderer->pureUi):NcmaUiStatsV1{sizeof(NcmaUiStatsV1),renderer->pureUi?1u:0u,handle};return NCMA_OK;});
}
