void TestPrecisionWait(NcmaGetApiV1 getPlatform,const NcmaPlatformApiV1& base,uint64_t context)
{
    NcmaErrorV1 e{};NcmaPlatformApiV1_1 api{};
    assert(getPlatform(1,1,&api,sizeof(api),&e)==NCMA_OK&&api.base.module.minor==1&&api.base.module.struct_size==144);
    assert(getPlatform(1,1,&api,120,&e)==NCMA_BUFFER_TOO_SMALL&&e.required_bytes==144);
    assert(getPlatform(1,3,&api,sizeof(api),&e)==NCMA_ABI_MISMATCH);
    assert(getPlatform(2,1,&api,sizeof(api),&e)==NCMA_ABI_MISMATCH);
    NcmaModuleStatusV1 state{};uint64_t timer=0,duplicate=123;
    assert(base.module.get_status(context,&state,&e)==NCMA_OK&&state.live_resources==0);
    assert(api.create_precision_wait(context,nullptr,&e)==NCMA_INVALID_ARGUMENT);
    assert(api.create_precision_wait(0,&timer,&e)==NCMA_INVALID_HANDLE&&timer==0);
    assert(api.create_precision_wait(context,&timer,&e)==NCMA_OK&&timer!=0);
    assert(base.module.get_status(context,&state,&e)==NCMA_OK&&state.live_resources==1);
    assert(api.create_precision_wait(context,&duplicate,&e)==NCMA_BUSY&&duplicate==123);
    assert(base.module.shutdown(context,&e)==NCMA_BUSY);
    assert(api.precision_wait(context,timer,0,&e)==NCMA_INVALID_ARGUMENT);
    assert(api.precision_wait(context,timer,8001,&e)==NCMA_INVALID_ARGUMENT);
    assert(api.precision_wait(context,0,1,&e)==NCMA_INVALID_HANDLE);
    assert(api.precision_wait(context,timer,1,nullptr)==NCMA_INVALID_ARGUMENT);
    uint32_t wrong=NCMA_OK;
    std::thread worker([&]{NcmaErrorV1 local{};wrong=api.precision_wait(context,timer,1,&local);});worker.join();
    assert(wrong==NCMA_WRONG_THREAD);
    for(uint32_t delay:{1u,1000u,8000u})assert(api.precision_wait(context,timer,delay,&e)==NCMA_OK);
    assert(api.destroy_precision_wait(context,0,&e)==NCMA_INVALID_HANDLE);
    const uint64_t old=timer;
    assert(api.destroy_precision_wait(context,timer,&e)==NCMA_OK);
    assert(api.destroy_precision_wait(context,timer,&e)==NCMA_INVALID_HANDLE);
    for(int i=0;i<32;++i){
        assert(api.create_precision_wait(context,&timer,&e)==NCMA_OK&&timer!=old);
        assert(api.precision_wait(context,old,1,&e)==NCMA_INVALID_HANDLE);
        assert(api.destroy_precision_wait(context,timer,&e)==NCMA_OK);
    }
    assert(base.module.get_status(context,&state,&e)==NCMA_OK&&state.live_resources==0);
    std::cout<<"PASS Platform1.1 precision wait boundaries/thread/stale/32cycles; no timing acceptance\n";
}
