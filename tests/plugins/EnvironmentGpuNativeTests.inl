static void EnvironmentGpuNativeTests(uint64_t context,uint64_t handle) {
 NcmaErrorV1 error{};NcmaEnvironmentGpuApiV1 api{};NcmaGpuResourceV3 key{123,456};
 auto query=[](uint64_t m,uint32_t cap,NcmaEnvironmentGpuApiV1* out,NcmaErrorV1* e){return QuerySceneRender(m,14,out,cap,e);};
 Check(query(context,55,&api,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==56&&api.struct_size==0,"Environment GPU short table atomic");
 Check(query(context,56,&api,&error)==NCMA_OK&&api.version==1&&api.capabilities==3&&api.publish&&api.destroy&&api.capture&&api.stats&&api.validate_preparation,"Environment GPU query14/API1");
 std::vector<float> input(224,1); // cube2/full2 levels=120, irradiance2=96, LUT2=8 =>224 floats.
 for(size_t i=0;i<input.size();i++)input[i]=i<216&&i%4==3?1.f:static_cast<float>(i%13)*.05f;
 NcmaEnvironmentGpuDescriptionV1 d{40,1,2,2,2,2,224,0,input.data()};
 auto publish=[&](NcmaGpuResourceV3 old={}){return api.publish(context,handle,old,&d,&key,&error);};
 auto stats=[&](){NcmaEnvironmentGpuStatsV1 s{};Check(api.stats(context,handle,&s,&error)==NCMA_OK,"Environment GPU stats");return s;};
 const auto initial=stats();Check(initial.live==0&&initial.resident_bytes==0&&renderer->backend->GetLiveResourceCount()==0,"Off initializes no environment resources");
 auto unchanged=[&](){const auto s=stats();return std::memcmp(&s,&initial,64)==0&&key.value==123&&key.generation==456&&renderer->backend->GetLiveResourceCount()==0;};
 for(int stage=1;stage<=3;stage++){Rendering::testEnvironmentGpuFailureStage=stage;Check(publish()==NCMA_INTERNAL_ERROR&&unchanged(),"Partial GPU candidate fault releases all resources atomically");}Rendering::testEnvironmentGpuFailureStage=0;
 testEnvironmentGpuPublicationFault=true;Check(publish()==NCMA_INTERNAL_ERROR&&unchanged(),"GPU diagnostic exception before publication");testEnvironmentGpuPublicationFault=false;
 d.reserved=1;Check(publish()==NCMA_INVALID_ARGUMENT&&unchanged(),"GPU reserved rejection");d.reserved=0;
 for(auto invalid:std::array<float,5>{-1.f,-0.f,3.f,std::numeric_limits<float>::quiet_NaN(),std::numeric_limits<float>::infinity()}){const auto last=input.back();input.back()=invalid;Check(publish()==NCMA_INVALID_ARGUMENT&&unchanged(),"GPU last LUT value rejection before any allocation");input.back()=last;}
 d.float_count--;Check(publish()==NCMA_INVALID_ARGUMENT&&unchanged(),"GPU exact closure count");d.float_count++;
 for(auto member:std::array<uint32_t NcmaEnvironmentGpuDescriptionV1::*,6>{&NcmaEnvironmentGpuDescriptionV1::struct_size,&NcmaEnvironmentGpuDescriptionV1::version,&NcmaEnvironmentGpuDescriptionV1::cube_size,&NcmaEnvironmentGpuDescriptionV1::irradiance_size,&NcmaEnvironmentGpuDescriptionV1::lut_size,&NcmaEnvironmentGpuDescriptionV1::levels}) {
     const auto original=d.*member;d.*member=99;Check(publish()==NCMA_INVALID_ARGUMENT&&unchanged(),"Closed GPU descriptor field");d.*member=original;
 }
 for(auto [index,value]:std::array<std::pair<size_t,float>,3>{{{215,.5f},{212,65505.f},{0,65504*std::numbers::pi_v<float>+1}}}){
     const auto original=input[index];input[index]=value;Check(publish()==NCMA_INVALID_ARGUMENT&&unchanged(),"Complete alpha/specular/irradiance range rejection");input[index]=original;
 }
 uint32_t foreign=0;std::thread worker([&](){NcmaErrorV1 e{};foreign=api.publish(context,handle,{},&d,&key,&e);});worker.join();Check(foreign==NCMA_WRONG_THREAD&&unchanged(),"GPU wrong thread");
 renderer->active=true;Check(publish()==NCMA_BUSY&&unchanged(),"GPU active frame");renderer->active=false;
 renderer->failed=true;Check(publish()==NCMA_INTERNAL_ERROR&&unchanged(),"GPU fail-stop preparation");renderer->failed=false;
 renderer->pureUi=true;Check(publish()==NCMA_UNSUPPORTED_FEATURE&&unchanged(),"GPU pureUI rejects without allocations");renderer->pureUi=false;
 busy=true;Check(publish()==NCMA_BUSY,"GPU native nonreentry");busy=false;Check(unchanged(),"GPU reentry unchanged");
 Check(publish({0,handle})==NCMA_INVALID_HANDLE&&unchanged(),"GPU zero token generation closed");
 Check(publish()==NCMA_OK&&key.generation==handle,"GPU complete candidate publication");const auto token=key;
 const auto baseline=stats();Check(baseline.live==1&&baseline.resident_bytes==896&&baseline.publications==1&&baseline.uploaded_bytes==896&&renderer->backend->GetLiveResourceCount()==4,"GPU accounting three textures and sampler");
 auto& environment=*renderer->environments.at(token.value);Check(environment.layout.values==nullptr,"No caller pointer retained");
 for(size_t i=0;i<3;i++){D3D11_SHADER_RESOURCE_VIEW_DESC view{};renderer->backend->BorrowTextureView(environment.textures[i])->GetDesc(&view);
  Check(view.ViewDimension==(i==2?D3D11_SRV_DIMENSION_TEXTURE2D:D3D11_SRV_DIMENSION_TEXTURECUBE),"Actual cube/2D view dimension");
  Check(view.Format==(i==2?DXGI_FORMAT_R32G32_FLOAT:DXGI_FORMAT_R32G32B32A32_FLOAT),"Actual linear FP32 view format");}
 std::vector<float> output(input.size(),-7);auto capture=[&](uint32_t count){return api.capture(context,handle,token,output.data(),count,&error);};
 Check(capture(223)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==896&&std::all_of(output.begin(),output.end(),[](float v){return v==-7;})&&stats().captures==0,"GPU short capture atomic");
 Check(capture(225)==NCMA_INVALID_ARGUMENT&&output[0]==-7&&stats().captures==0,"GPU oversize capture closed");
 Check(api.capture(context,handle,token,nullptr,224,&error)==NCMA_INVALID_ARGUMENT&&stats().captures==0,"GPU null capture closed");
 testEnvironmentGpuPublicationFault=true;Check(capture(224)==NCMA_INTERNAL_ERROR&&std::all_of(output.begin(),output.end(),[](float v){return v==-7;})&&stats().captures==0,"Actual GPU readback late fault atomic");testEnvironmentGpuPublicationFault=false;
 testWaitTimeout=true;Check(capture(224)==NCMA_SHUTDOWN_TIMEOUT&&output[0]==-7&&stats().captures==0,"GPU capture bounded drain timeout atomic");testWaitTimeout=false;
 Check(capture(224)==NCMA_OK&&output==input,"Actual full face/mip/LUT GPU staging readback");const auto active=stats();
 auto retained=[&](){const auto s=stats();return std::memcmp(&s,&active,64)==0&&renderer->backend->GetLiveResourceCount()==4&&key.value==token.value;};
 input[0]=2;
 testWaitTimeout=true;Check(publish(token)==NCMA_SHUTDOWN_TIMEOUT&&retained(),"Replacement GPU timeout retains old all counters");
 Check(api.destroy(context,handle,token,&error)==NCMA_SHUTDOWN_TIMEOUT&&retained(),"Destroy timeout keeps retryable resources");testWaitTimeout=false;
 testEnvironmentGpuPublicationFault=true;Check(publish(token)==NCMA_INTERNAL_ERROR&&retained(),"Replacement diagnostic fault retains old");testEnvironmentGpuPublicationFault=false;
 Check(publish({token.value,handle+1})==NCMA_INVALID_HANDLE&&retained(),"Foreign generation rejected before GPU");
 Check(capture(224)==NCMA_OK&&output[0]!=2,"Old pixels retained after rejected replacement");
 Check(publish(token)==NCMA_OK&&key.value==token.value&&stats().live==1&&stats().publications==2&&stats().retirements==1,"Noexcept replacement publishes stable identity");
 Check(capture(224)==NCMA_OK&&output==input,"Replacement actual GPU values");
 Check(Destroy(context,handle,&error)==NCMA_BUSY,"Renderer cannot unload with environments");
 renderer->failed=true;Check(api.destroy(context,handle,token,&error)==NCMA_OK,"Fail-stop cleanup available");renderer->failed=false;
 Check(stats().live==0&&stats().resident_bytes==0&&stats().retirements==2&&renderer->backend->GetLiveResourceCount()==0,"Environment cleanup complete");
 Check(api.destroy(context,handle,token,&error)==NCMA_INVALID_HANDLE&&capture(224)==NCMA_INVALID_HANDLE,"Released token rejected");
 Check(renderer->stats.validation_errors==0&&renderer->stats.validation_warnings==0,"Environment actual GPU API0/0");
}
