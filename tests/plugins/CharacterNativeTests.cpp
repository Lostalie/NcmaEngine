// Fault injection stays in this test TU, never an exported production control.
#include "../../engine/source/plugins/physics/PhysicsPlugin.cpp"
#include <iostream>
#include <vector>
static void Check(bool value,const char* name) {if(!value)throw std::runtime_error(name);}
struct Fixture {
    NcmaPhysicsApiV1_2 api{};NcmaCharacterApiV1 chars{};NcmaErrorV1 error{};
    uint64_t context=0,world=0,sequence=0;
    std::vector<uint64_t> characters;
    std::array<NcmaCharacterStateV1,32> states{};
    std::vector<NcmaCharacterContactV1> contacts=std::vector<NcmaCharacterContactV1>(2048);
    NcmaCharacterStepReceiptV1 receipt{};
    explicit Fixture(uint64_t c):context(c) {
        Check(ncma_plugin_get_api(1,2,&api,sizeof(api),&error)==0,"physics 1.2");
        Check(api.query_characters(context,1,0,&chars,sizeof(chars),&error)==0,"character API");
        NcmaPhysicsWorldDescriptionV1 d{sizeof(d),3,4096,1,{0,-9.81F,0},0};
        Check(api.base.base.create_world(context,&d,&world,&error)==0,"world");
    }
    uint64_t Box(float x,float y,float z,float ex,float ey,float ez,uint32_t category=1,bool dynamic=false,float angle=0) {
        NcmaCollisionBoxV1 d{};d.struct_size=sizeof(d);d.category=category;d.dynamic_body=dynamic?1u:0u;
        d.position[0]=x;d.position[1]=y;d.position[2]=z;d.half_extents[0]=ex;d.half_extents[1]=ey;d.half_extents[2]=ez;d.density=1000;
        // Numeric rotated-box fixture (Z rotation); characters themselves remain yaw-only.
        d.rotation[2]=std::sin(angle*.5F);d.rotation[3]=std::cos(angle*.5F);uint64_t id=0;
        Check(chars.create_boxes(context,world,&d,1,&id,1,&error)==0,"box");return id;
    }
    static NcmaCapsuleDescriptionV1 Description(float x=0,float y=.1F,float z=0,uint32_t mask=~0u,float stairs=.4F) {
        NcmaCapsuleDescriptionV1 d{};d.struct_size=sizeof(d);d.category=2;d.mask=mask;
        d.position[0]=x;d.position[1]=y;d.position[2]=z;d.radius=.3F;d.half_height=.6F;
        d.max_slope_radians=.7F;d.step_height=stairs;d.floor_distance=.5F;d.mass=70;d.max_strength=1000;d.padding=.02F;d.correlation=42;return d;
    }
    uint64_t Capsule(float x=0,float y=.1F,float z=0,uint32_t mask=~0u,float stairs=.4F) {
        auto d=Description(x,y,z,mask,stairs);uint64_t id=0;
        Check(chars.create_capsules(context,world,&d,1,&id,1,&error)==0,"capsule");characters.push_back(id);return id;
    }
    void Step(float x=0,float y=-1,float z=0,int times=1) {
        std::array<NcmaCharacterVelocityV1,32> input{};
        for(size_t i=0;i<characters.size();++i){input[i].character=characters[i];input[i].velocity[0]=x;input[i].velocity[1]=y;input[i].velocity[2]=z;input[i].rotation[3]=1;}
        for(int n=0;n<times;++n){auto result=chars.step(context,world,sequence,1.0F/60,input.data(),static_cast<uint32_t>(characters.size()),states.data(),32,contacts.data(),2048,&receipt,&error);
            if(result)throw std::runtime_error("character step: "+std::string(reinterpret_cast<char*>(error.message),error.message_length));
            ++sequence;Check(receipt.sequence==sequence && receipt.states==characters.size(),"receipt");
            for(size_t i=0;i<characters.size();++i)Check(states[i].character==characters[i] && states[i].sequence==sequence && states[i].correlation==42,"state identity");
            for(uint32_t i=0;i<receipt.contacts;++i)Check(contacts[i].sequence==sequence && contacts[i].other && contacts[i].other_kind>=1 && contacts[i].other_kind<=2,"contact identity");
        }
    }
    void Close() {
        if(!world)return;
        Check(chars.destroy_characters(context,world,characters.data(),static_cast<uint32_t>(characters.size()),&error)==0,"close characters");
        characters.clear();Check(api.base.base.destroy_world(context,world,&error)==0,"close world");world=0;
    }
    ~Fixture(){if(world) {try{Close();}catch(...) {}}}
};
int main() {
    try {
        NcmaPhysicsApiV1_2 modernTable{};NcmaErrorV1 e{};uint64_t c=0;
        Check(ncma_plugin_get_api(1,2,&modernTable,152,&e)==NCMA_BUFFER_TOO_SMALL && e.required_bytes==160,"short 1.2");
        Check(ncma_plugin_get_api(1,2,&modernTable,sizeof(modernTable),&e)==0 && modernTable.base.base.module.minor==2 && modernTable.base.base.module.capabilities==255,"1.2 layout");
        auto& base=modernTable.base.base;Check(base.module.initialize(nullptr,0,&c,&e)==0,"initialize");
        NcmaCharacterApiV1 query{};
        Check(modernTable.query_characters(c,2,0,&query,80,&e)==NCMA_ABI_MISMATCH,"query major");
        Check(modernTable.query_characters(c,1,1,&query,80,&e)==NCMA_ABI_MISMATCH,"query minor");
        Check(modernTable.query_characters(c,1,0,&query,79,&e)==NCMA_BUFFER_TOO_SMALL && e.required_bytes==80,"short query");
        Check(modernTable.query_characters(c+1,1,0,&query,80,&e)==NCMA_INVALID_HANDLE,"query context");
        {
            Fixture f(c);auto floor=f.Box(0,-.5F,0,20,.5F,20);auto id=f.Capsule();auto d=Fixture::Description();uint64_t untouched=123;
            Check(f.chars.create_capsules(c,f.world,&d,1,&untouched,0,&e)==NCMA_BUFFER_TOO_SMALL && untouched==123,"create preflight");
            d.radius=std::numeric_limits<float>::quiet_NaN();Check(f.chars.create_capsules(c,f.world,&d,1,&untouched,1,&e)==NCMA_INVALID_ARGUMENT,"NaN capsule");
            d=Fixture::Description();d.category=3;Check(f.chars.create_capsules(c,f.world,&d,1,&untouched,1,&e)==NCMA_INVALID_ARGUMENT,"category");
            NcmaCharacterVelocityV1 in{};in.character=id;in.rotation[3]=1;
            NcmaCharacterStepReceiptV1 receipt{999,999,999};NcmaCharacterStateV1 state{};state.sequence=999;
            std::array<NcmaCharacterContactV1,64> contacts{};
            auto attempt=[&](uint64_t seq,float dt,uint32_t cap=64){return f.chars.step(c,f.world,seq,dt,&in,1,&state,1,contacts.data(),cap,&receipt,&e);};
            Check(attempt(0,1.0F/60,63)==NCMA_BUFFER_TOO_SMALL && state.sequence==999 && receipt.sequence==999,"step capacity no output");
            Check(attempt(1,1.0F/60)==NCMA_INVALID_ARGUMENT && worlds.at(f.world)->stats.sequence==0,"stale seq no step");
            Check(attempt(0,0)==NCMA_INVALID_ARGUMENT && attempt(0,.26F)==NCMA_INVALID_ARGUMENT,"dt");
            in.velocity[0]=std::numeric_limits<float>::infinity();Check(attempt(0,.01F)==NCMA_INVALID_ARGUMENT,"infinite velocity");in.velocity[0]=0;
            in.rotation[0]=.1F;Check(attempt(0,.01F)==NCMA_INVALID_ARGUMENT,"yaw restriction");in.rotation[0]=0;
            in.character=floor;Check(attempt(0,.01F)==NCMA_INVALID_HANDLE,"body is not character");in.character=id;
            Check(base.step(c,f.world,.01F,&untouched,&e)==NCMA_BUSY,"old step cannot bypass character quantum");
            Check(base.destroy_world(c,f.world,&e)==NCMA_BUSY && base.module.shutdown(c,&e)==NCMA_BUSY,"lifetime guard");
            uint32_t wrong=0;std::thread t([&]{NcmaErrorV1 x{};wrong=f.chars.read_states(c,f.world,0,&id,1,&state,1,&x);});t.join();Check(wrong==NCMA_WRONG_THREAD,"owner thread");
            f.Step(0,-1,0,15);Check(std::abs(f.states[0].position[1])<.06F && f.states[0].ground_state==0 && f.states[0].ground_body==floor,"floor grounding");
            Check(f.receipt.contacts>0 && f.contacts[0].other==floor,"copied floor contact");
            Check(f.chars.create_capsules(c,f.world,&d,1,&untouched,1,&e)==NCMA_BUSY,"topology frozen");
            Check(f.chars.read_states(c,f.world,0,&id,1,&state,1,&e)==NCMA_INVALID_ARGUMENT && state.sequence==999,"stale read retains output");
            uint64_t dup[2]{id,id};Check(f.chars.read_states(c,f.world,f.sequence,dup,2,f.states.data(),32,&e)==NCMA_INVALID_ARGUMENT,"duplicate read");
            Fixture other(c);Check(other.chars.read_states(c,other.world,0,&id,1,&state,1,&e)==NCMA_INVALID_HANDLE,"foreign character");other.Close();
            f.Close();Check(query.read_states==nullptr,"short API never wrote output");
            std::cout<<"ABI/preflight/identity/grounding passed\n";
        }
        {
            Fixture f(c);f.Box(0,-.5F,0,30,.5F,30);auto wall=f.Box(2,2,0,.2F,2,20);auto id=f.Capsule(0,.1F,-2,~0u,0);
            f.Step(3,-1,2,90);Check(f.states[0].position[0]<1.51F && f.states[0].position[2]>.8F,"wall sliding");
            NcmaPhysicsRayV1 ray{sizeof(ray),1,{0,1,0},{4,0,0},0};NcmaPhysicsQueryHitV1 hit{};
            Check(f.chars.ray(c,f.world,f.sequence,&ray,&hit,&e)==0 && hit.resource==wall && hit.kind==1 && hit.fraction>.4F && hit.fraction<.5F && hit.normal[0]<-.9F,"ray wall");
            ray.mask=0;Check(f.chars.ray(c,f.world,f.sequence,&ray,&hit,&e)==0 && !hit.hit,"query mask none");
            ray.mask=1;ray.ignore=wall;Check(f.chars.ray(c,f.world,f.sequence,&ray,&hit,&e)==0 && !hit.hit,"query ignore");
            ray.ignore=id+100000;Check(f.chars.ray(c,f.world,f.sequence,&ray,&hit,&e)==NCMA_INVALID_HANDLE,"foreign ignore");
            NcmaPhysicsCapsuleSweepV1 sweep{};sweep.struct_size=sizeof(sweep);sweep.mask=1;sweep.radius=.3F;sweep.half_height=.6F;sweep.rotation[3]=1;sweep.foot[1]=.1F;sweep.displacement[0]=4;
            Check(f.chars.sweep(c,f.world,f.sequence,&sweep,&hit,&e)==0 && hit.resource==wall && hit.fraction>.3F && hit.fraction<.4F && hit.normal[0]<-.9F,"capsule sweep wall");
            ray={sizeof(ray),2,{0,1,f.states[0].position[2]},{4,0,0},0};
            Check(f.chars.ray(c,f.world,f.sequence,&ray,&hit,&e)==0 && hit.resource==id && hit.kind==2,"ray character proxy");
            ray.ignore=id;Check(f.chars.ray(c,f.world,f.sequence,&ray,&hit,&e)==0 && !hit.hit,"ignore character proxy");
            sweep.mask=2;sweep.foot[2]=f.states[0].position[2];Check(f.chars.sweep(c,f.world,f.sequence,&sweep,&hit,&e)==0 && hit.resource==id && hit.kind==2,"sweep character proxy");
            sweep.displacement[0]=0;Check(f.chars.sweep(c,f.world,f.sequence,&sweep,&hit,&e)==NCMA_INVALID_ARGUMENT,"zero sweep");
            f.Close();std::cout<<"wall/sliding/query masks/character queries passed\n";
        }
        {
            Fixture f(c);f.Box(0,-.5F,0,20,.5F,20);f.Box(0,2.5F,0,20,.2F,20);f.Capsule();f.Step(0,-1,0,15);f.Step(0,10,0,10);
            Check(f.states[0].position[1]<.56F,"ceiling blocks upward velocity");f.Close();
            Fixture high(c);high.Box(2,2,0,.01F,2,20);high.Capsule(0,.1F,0,~0u,0);high.Step(1000,0,0);
            Check(high.states[0].position[0]<1.72F,"high speed capsule cast no thin-wall tunnelling");high.Close();
            Fixture masks(c);masks.Box(2,2,0,.2F,2,20,4);masks.Capsule(0,.1F,0,1,0);masks.Step(3,0,0,60);
            Check(masks.states[0].position[0]>2.8F,"character masks exclude wall");masks.Close();
            Fixture overlap(c);overlap.Box(0,-.5F,0,20,.5F,20);overlap.Capsule(0,-.1F,0);overlap.Step(0,-1,0,30);
            Check(overlap.states[0].position[1]>-.03F,"initial shallow penetration recovers");overlap.Close();std::cout<<"ceiling/high speed/masks/penetration passed\n";
        }
        {
            Fixture slope(c);slope.Box(0,-.5F,0,20,.5F,20);slope.Box(0,1,0,4,.2F,4,1,false,.35F);slope.Capsule(0,2,0);slope.Step(0,-3,0,60);
            Check(slope.states[0].ground_state==0 && slope.states[0].ground_normal[1]>.9F && slope.states[0].ground_normal[0]<-.2F,"walkable rotated slope");slope.Close();
            Fixture steep(c);steep.Box(0,1,0,4,.2F,4,1,false,1.0F);steep.Capsule(0,2,0);steep.Step(0,-3,0,30);
            Check(steep.states[0].ground_state!=0 && steep.states[0].position[0]<-.1F,"steep slope unsupported/slides");steep.Close();
            Fixture stairs(c);stairs.Box(0,-.5F,0,20,.5F,20);stairs.Box(2,.1F,0,1,.1F,4);stairs.Capsule();stairs.Step(2,-1,0,65);
            Check(stairs.states[0].position[0]>1.5F && stairs.states[0].position[1]>.15F,"configured step ascends");stairs.Close();
            Fixture noStep(c);noStep.Box(0,-.5F,0,20,.5F,20);noStep.Box(2,.1F,0,1,.1F,4);noStep.Capsule(0,.1F,0,~0u,0);noStep.Step(2,-1,0,65);
            Check(noStep.states[0].position[0]<1 && noStep.states[0].position[1]<.07F,"disabled step blocks");noStep.Close();std::cout<<"slope/steep/stairs passed\n";
        }
        {
            Fixture f(c);auto moving=f.Box(2,2,0,.2F,2,4,1,true);f.Capsule(0,.1F,0,~0u,0);
            NcmaPhysicsVelocity3DV1 v{moving,{-1,0,0},0};Check(base.set_velocities_3d(c,f.world,&v,1,&e)==0,"moving obstacle velocity");f.Step(2,0,0,30);
            NcmaPhysicsBodyState3DV1 b{};Check(base.read_states_3d(c,f.world,f.sequence,&moving,1,&b,1,&e)==0 && b.sequence==30 && b.position[0]<1.8F,"bodies step once alongside character");
            Check(f.states[0].position[0]<b.position[0]-.45F,"moving obstacle blocks character");f.Close();
            std::array<float,2> first{};
            for(int run=0;run<2;++run){Fixture pair(c);pair.Capsule(0,.1F,0,~0u,0);pair.Capsule(2,.1F,0,~0u,0);
                NcmaCharacterVelocityV1 inputs[2]{};inputs[0].character=pair.characters[0];inputs[1].character=pair.characters[1];inputs[0].rotation[3]=inputs[1].rotation[3]=1;inputs[0].velocity[0]=3;
                std::swap(inputs[0],inputs[1]);Check(pair.chars.step(c,pair.world,0,.01F,inputs,2,pair.states.data(),32,pair.contacts.data(),2048,&pair.receipt,&e)==NCMA_INVALID_ARGUMENT,"canonical order preflight");std::swap(inputs[0],inputs[1]);
                Check(pair.chars.destroy_characters(c,pair.world,pair.characters.data(),1,&e)==NCMA_UNSUPPORTED_FEATURE,"partial topology delete rejected");
                for(int n=0;n<60;++n){Check(pair.chars.step(c,pair.world,pair.sequence,1.0F/60,inputs,2,pair.states.data(),32,pair.contacts.data(),2048,&pair.receipt,&e)==0,"pair quantum");++pair.sequence;}
                std::cout<<"pair positions "<<pair.states[0].position[0]<<' '<<pair.states[1].position[0]<<" contacts "<<pair.receipt.contacts<<'\n';
                // Sequential virtual characters may push one another; no immovable-character
                // promise. Verify separation/contact, not a fabricated stationary target.
                Check(pair.states[1].position[0]-pair.states[0].position[0]>.58F && pair.receipt.contacts>0,"character separation/contact");
                if(run==0){first={pair.states[0].position[0],pair.states[1].position[0]};}else Check(std::abs(first[0]-pair.states[0].position[0])<1e-5F && std::abs(first[1]-pair.states[1].position[0])<1e-5F,"canonical-order repeatability on same machine");pair.Close();
            }
            std::cout<<"moving obstacles/character ordering passed\n";
            Fixture filtered(c);filtered.Capsule(0,.1F,0,1,0);filtered.Capsule(2,.1F,0,~0u,0);
            NcmaCharacterVelocityV1 input[2]{};for(int i=0;i<2;++i){input[i].character=filtered.characters[static_cast<size_t>(i)];input[i].rotation[3]=1;}input[0].velocity[0]=3;
            for(int n=0;n<60;++n){Check(filtered.chars.step(c,filtered.world,filtered.sequence,1.0F/60,input,2,filtered.states.data(),32,filtered.contacts.data(),2048,&filtered.receipt,&e)==0,"filtered pair step");++filtered.sequence;}
            Check(filtered.states[0].position[0]>2.9F && std::abs(filtered.states[1].position[0]-2)<.01F && filtered.receipt.contacts==0,"bidirectional character masks");filtered.Close();
            Fixture support(c);support.Box(0,-.5F,0,20,.5F,20);support.Capsule(0,0,0,~0u,0);support.Capsule(0,2,0,~0u,0);
            NcmaCharacterVelocityV1 vertical[2]{};for(int i=0;i<2;++i){vertical[i].character=support.characters[static_cast<size_t>(i)];vertical[i].rotation[3]=1;}vertical[1].velocity[1]=-1;
            for(int n=0;n<30;++n){Check(support.chars.step(c,support.world,support.sequence,1.0F/60,vertical,2,support.states.data(),32,support.contacts.data(),2048,&support.receipt,&e)==0,"support pair step");++support.sequence;}
            std::cout<<"support "<<support.states[1].position[1]<<" ground "<<support.states[1].ground_state<<" kind "<<support.states[1].ground_kind<<" resource "<<support.states[1].ground_body<<'\n';
            Check(support.states[1].ground_state==0 && support.states[1].ground_kind==2 && support.states[1].ground_body==support.characters[0],"copied character support identity");support.Close();
        }
        {
            Fixture dense(c);for(int i=0;i<80;++i)dense.Box(0,-.5F,0,20,.5F,20);auto id=dense.Capsule();
            NcmaCharacterVelocityV1 in{};in.character=id;in.velocity[1]=-1;in.rotation[3]=1;
            NcmaCharacterStateV1 output{};output.sequence=123;NcmaCharacterStepReceiptV1 result{123,0,0};
            Check(dense.chars.step(c,dense.world,0,1.0F/60,&in,1,&output,1,dense.contacts.data(),2048,&result,&e)==NCMA_INTERNAL_ERROR,"real candidate/hit overflow fail-stop");
            Check(output.sequence==123 && result.sequence==123 && worlds.at(dense.world)->stats.state==2 && worlds.at(dense.world)->stats.sequence==0,"overflow no partial published output/sequence");
            dense.Close();std::cout<<"real dense collision budget fail-stop passed\n";
        }
        {
            Fixture partial(c);auto& w=*worlds.at(partial.world);
            for(int i=0;i<4096+static_cast<int>(NCMA_MAX_CHARACTERS)-1;++i)w.three->CreateBox(Vector3(static_cast<float>(i)*2,0,0),Vector3(.25F,.25F,.25F),false);
            NcmaCapsuleDescriptionV1 input[2]{Fixture::Description(),Fixture::Description(2)};uint64_t output[2]{123,456};
            Check(partial.chars.create_capsules(c,partial.world,input,2,output,2,&e)==NCMA_INTERNAL_ERROR,"second capsule resource creation fails");
            Check(output[0]==123 && output[1]==456 && w.characters.empty() && w.stats.state==1,"partial resource creation releases first proxy and retains outputs");partial.Close();
            Fixture close(c);auto id=close.Capsule();auto& domain=*worlds.at(close.world);auto kernel=domain.characters.at(id);domain.characters.at(id)=std::numeric_limits<uint64_t>::max();
            Check(close.chars.destroy_characters(c,close.world,&id,1,&e)==NCMA_INTERNAL_ERROR && domain.characters.size()==1 && domain.stats.state==2,"failed close retains ownership and fail-stops");
            Check(base.module.shutdown(c,&e)==NCMA_BUSY && base.destroy_world(c,close.world,&e)==NCMA_BUSY,"failed close forbids unload");domain.characters.at(id)=kernel;close.Close();
            std::cout<<"partial resource creation / failed close retention passed\n";
        }
        {
            Fixture f(c);for(int i=0;i<32;++i)f.Capsule(static_cast<float>(i)*2,2,0,0,0);
            auto d=Fixture::Description();uint64_t sentinel=123;Check(f.chars.create_capsules(c,f.world,&d,1,&sentinel,1,&e)==NCMA_BUSY && sentinel==123,"character capacity");f.Step(0,0,0);f.Close();
            // Actual native mutation followed by injected copy/mapping failure. No rollback claim.
            Fixture fault(c);auto id=fault.Capsule(0,2,0,0,0);NcmaCharacterVelocityV1 v{};v.character=id;v.velocity[0]=1;v.rotation[3]=1;
            auto& w=*worlds.at(fault.world);auto key=w.characters.at(id);NcmaCharacterStateV1 state{};state.sequence=777;
            NcmaCharacterStepReceiptV1 receipt{777,0,0};std::array<NcmaCharacterContactV1,64> contacts{};
            // Corrupt test-only public map AFTER a genuine quantum, inside the same fail-stop guard.
            try {Execution scope(w);auto input=v;input.character=key;w.three->StepCharacters(.01F,&input,1);w.characters.at(id)=std::numeric_limits<uint64_t>::max();
                (void)CharacterState(w,id,1);}catch(const std::exception&) {}
            w.characters.at(id)=key;
            Check(w.stats.state==2 && w.stats.sequence==0 && w.three->ReadCharacter(key).position[0]>.005F,"irreversible native mutation / no committed sequence");
            Check(fault.chars.step(c,fault.world,0,.01F,&v,1,&state,1,contacts.data(),64,&receipt,&e)==NCMA_INTERNAL_ERROR && state.sequence==777 && receipt.sequence==777,"fault cannot continue/output");
            NcmaPhysicsRayV1 ray{sizeof(ray),1,{0,0,0},{1,0,0},0};NcmaPhysicsQueryHitV1 hit{};Check(fault.chars.ray(c,fault.world,0,&ray,&hit,&e)==NCMA_INTERNAL_ERROR,"fault query rejected");fault.Close();
            for(int cycle=0;cycle<32;++cycle){Fixture life(c);life.Box(0,-.5F,0,20,.5F,20);life.Capsule();life.Step();auto old=life.characters[0];life.Close();
                Check(query.read_states==nullptr,"no query prefix corruption");NcmaModuleStatusV1 status{};Check(base.module.get_status(c,&status,&e)==0 && status.live_resources==0 && status.live_jobs==0,"cycle drained");
                Check(modernTable.query_characters(c,1,0,&query,80,&e)==0,"query after resources closed");Fixture fresh(c);NcmaCharacterStateV1 out{};
                Check(fresh.chars.read_states(c,fresh.world,0,&old,1,&out,1,&e)==NCMA_INVALID_HANDLE,"stale resource never resurrected");fresh.Close();query={};}
            std::cout<<"budgets/fail-stop/32 lifecycle cycles passed\n";
        }
        Check(base.module.shutdown(c,&e)==0,"shutdown");std::cout<<"All actual Jolt Character native tests passed.\n";return 0;
    }catch(const std::exception& x){std::cerr<<x.what()<<'\n';return 1;}
}
