// Fault injection is confined to this TU: no production fault/debug export.
#include "../../engine/source/plugins/physics/PhysicsPlugin.cpp"
#include <iostream>
#include <vector>
static void Check(bool value,const char* message) {if(!value)throw std::runtime_error(message);}
int main() {
    try {
        NcmaPhysicsApiV1 table{};NcmaErrorV1 error{};uint64_t context=0,two=0,three=0;
        Check(ncma_plugin_get_api(2,0,&table,sizeof(table),&error)==NCMA_ABI_MISMATCH,"major rejected");
        Check(ncma_plugin_get_api(1,2,&table,sizeof(table),&error)==NCMA_ABI_MISMATCH,"minor rejected");
        Check(ncma_plugin_get_api(1,0,&table,56,&error)==NCMA_BUFFER_TOO_SMALL && error.required_bytes==144,"short table");
        Check(ncma_plugin_get_api(1,0,&table,sizeof(table),&error)==0,"API");
        NcmaPhysicsApiV1_1 modern{};
        Check(ncma_plugin_get_api(1,1,&modern,144,&error)==NCMA_BUFFER_TOO_SMALL && error.required_bytes==152,"1.1 short table");
        Check(ncma_plugin_get_api(1,1,&modern,sizeof(modern),&error)==0 && modern.base.module.minor==1,"1.1 API");
        Check(table.module.minor==0 && table.module.struct_size==144,"1.0 contract preserved");
        Check(table.module.initialize(nullptr,0,&context,&error)==0,"initialize");
        NcmaPhysicsWorldDescriptionV1 desc{sizeof(desc),2,4096,4,{0,-9.81F,0},0};
        Check(table.create_world(context,&desc,&two,&error)==0,"2D");
        desc.dimension=3;desc.sub_steps=1;
        Check(table.create_world(context,&desc,&three,&error)==0,"3D");
        NcmaPhysicsBox2DV1 box{{0,4},{.5F,.5F},1,1,42};uint64_t id=0;
        Check(table.create_boxes_2d(context,two,&box,1,&id,0,&error)==NCMA_BUFFER_TOO_SMALL,"create output preflight");
        Check(worlds.at(two)->bodies.empty(),"create preflight no side effect");
        Check(table.create_boxes_2d(context,two,&box,1,&id,1,&error)==0,"create box");
        uint64_t seq=99;Check(table.step(context,two,1.0F/60,nullptr,&error)==NCMA_INVALID_ARGUMENT,"step output preflight");
        Check(worlds.at(two)->stats.sequence==0,"no extraneous step");
        Check(table.step(context,two,1.0F/60,&seq,&error)==0 && seq==1,"step");
        NcmaPhysicsCountersV1 raw{};
        Check(modern.read_counters(context,two,&raw,&error)==0 && raw.struct_size==72 && raw.sequence==1 && raw.live_bodies==1 && std::isfinite(raw.last_step_ms),"1.1 raw counters");
        Check(modern.read_counters(context,two,nullptr,&error)==NCMA_INVALID_ARGUMENT,"1.1 output preflight");
        NcmaPhysicsBodyState2DV1 state{};state.sequence=99;
        Check(table.read_states_2d(context,two,seq,&id,1,&state,0,&error)==NCMA_BUFFER_TOO_SMALL,"read output preflight");
        Check(state.sequence==99 && worlds.at(two)->stats.sequence==1,"read no partial output or step");
        Check(table.read_states_2d(context,two,seq,&id,1,&state,1,&error)==0 && state.correlation==42,"read");
        uint64_t duplicate[]{id,id};Check(table.destroy_bodies(context,two,duplicate,2,&error)==NCMA_INVALID_ARGUMENT,"duplicates");
        uint64_t invalid[]{id,three};Check(table.destroy_bodies(context,two,invalid,2,&error)==NCMA_INVALID_HANDLE,"all destroy targets preflight");
        Check(worlds.at(two)->bodies.size()==1,"destroy atomic preflight");
        Check(table.destroy_bodies(context,three,&id,1,&error)==NCMA_INVALID_HANDLE,"foreign body");
        Check(table.create_boxes_2d(context,three,&box,1,&id,1,&error)==NCMA_INVALID_ARGUMENT,"dimension");
        uint32_t foreign=0;std::thread t([&] {NcmaErrorV1 e{};NcmaPhysicsStatsV1 s{};foreign=table.stats(context,two,&s,&e);});t.join();
        Check(foreign==NCMA_WRONG_THREAD,"thread rejected");
        Check(table.module.shutdown(context,&error)==NCMA_BUSY,"unload resources retained");
        // Force real Jolt kernel exhaustion mid-batch, after one successful body:
        // test-only invisible bodies consume 4095 slots; ABI map is empty.
        auto& w=*worlds.at(three);std::vector<uint64_t> internal;
        for(int i=0;i<4095;++i)internal.push_back(w.three->CreateBox(Vector3(static_cast<float>(i)*2,0,0),Vector3(.25F,.25F,.25F),false));
        NcmaPhysicsBox3DV1 boxes[2]{{{0,4,0},{.5F,.5F,.5F},1,1,1},{{2,4,0},{.5F,.5F,.5F},1,1,2}};
        uint64_t outputs[2]{123,456};
        Check(table.create_boxes_3d(context,three,boxes,2,outputs,2,&error)==NCMA_INTERNAL_ERROR,"create failure");
        Check(outputs[0]==123 && outputs[1]==456 && w.bodies.empty() && w.three->GetBodyCount()==4095 && w.stats.state==1,"creation rollback");
        for(auto body:internal)w.three->DestroyBody(body);
        // Exercise the same execution fail-stop guard used around actual solver mutation.
        try {Execution scope(*worlds.at(two));throw std::runtime_error("Injected solver failure.");}catch(const std::runtime_error&) {}
        Check(table.step(context,two,1.0F/60,&seq,&error)==NCMA_INTERNAL_ERROR,"faulted step");
        Check(table.create_boxes_2d(context,two,&box,1,&id,1,&error)==NCMA_INTERNAL_ERROR,"faulted create");
        Check(table.destroy_bodies(context,two,&id,1,&error)==NCMA_INTERNAL_ERROR,"faulted mutate");
        NcmaPhysicsStatsV1 stats{};Check(table.stats(context,two,&stats,&error)==0 && stats.state==2,"faulted stats");
        Check(table.destroy_world(context,two,&error)==0 && table.destroy_world(context,three,&error)==0,"destroy faulted and ready");
        Check(table.stats(context,two,&stats,&error)==NCMA_INVALID_HANDLE,"stale world");
        NcmaModuleStatusV1 status{};Check(table.module.get_status(context,&status,&error)==0 && status.live_resources==0 && status.live_jobs==0,"drained");
        Check(table.module.shutdown(context,&error)==0,"shutdown");
        uint64_t old=context;Check(table.module.initialize(nullptr,0,&context,&error)==0 && context!=old,"context generation");
        Check(table.module.get_status(old,&status,&error)==NCMA_INVALID_HANDLE,"stale context");
        Check(table.module.shutdown(context,&error)==0,"final shutdown");
        std::cout<<"Physics native ABI / rollback / fail-stop / lifetime checks passed.\n";return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
