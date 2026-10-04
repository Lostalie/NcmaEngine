#include "animation/ActionAnimationWorkspace.h"
#include "interop/NcmaAnimationApi.h"

#include <cmath>
#include <iostream>
#include <limits>
#include <stdexcept>

namespace
{
    using namespace NcmaEngine;
    using namespace NcmaEngine::Animation;

    void Check(bool condition, const char* message)
    {
        if (!condition) throw std::runtime_error(message);
    }
    bool Near(double a, double b) { return std::abs(a - b) < 0.00001; }
    template<class F> void Reject(F&& action)
    {
        bool rejected = false;
        try { action(); } catch (const std::exception&) { rejected = true; }
        Check(rejected, "Invalid input was accepted");
    }

    void SamplingAndValidation()
    {
        const auto library = CreateActionDemoLibrary();
        const auto count = library->GetSkeleton().Bones.size();
        std::vector<Transform> pose(count), base(count);
        std::vector<Matrix4> model(count), skin(count);
        library->Sample(0, 0, pose);
        library->BuildMatrices(pose, model, skin);
        for (const auto& matrix : skin) Check(matrix.isApprox(Matrix4::Identity(), 0.0001F), "Bind skin matrices are not identity");
        library->Sample(library->FindClip("Run"), 0.5, pose, false);
        Check(Near(pose[0].Position.z(), 1), "Track interpolation failed");
        library->Sample(library->FindClip("Run"), 0.5, pose);
        Check(pose[0].Position.isZero(), "Root motion applied twice to pose");
        library->Sample(library->FindClip("Idle"), 0, base);
        std::vector<float> mask(count, 0);
        mask[8] = 1;
        BlendPoses(base, pose, 1, base, mask);
        Check(base[8].Rotation.isApprox(pose[8].Rotation), "Per-bone blend failed");
        Check(base[4].Rotation.isApprox(Quaternion::Identity()), "Bone mask leaked into another bone");
        mask[0] = std::numeric_limits<float>::quiet_NaN();
        Reject([&] { BlendPoses(base, pose, 1, base, mask); });
        Reject([&] { library->Sample(0, -1, pose); });
        Reject([&] { (void)library->FindClip("missing"); });

        auto skeleton = library->GetSkeleton();
        auto clips = library->GetClips();
        skeleton.Bones[1].Parent = 11;
        Reject([&] { AnimationLibrary invalid(skeleton, clips); });
        skeleton = library->GetSkeleton();
        clips[0].Tracks[0].Keys[1].Time = 0;
        Reject([&] { AnimationLibrary invalid(skeleton, clips); });
        clips = library->GetClips();
        clips[0].SkeletonId = SceneUuid::New();
        Reject([&] { AnimationLibrary invalid(skeleton, clips); });
        clips = library->GetClips();
        clips[0].Tracks[0].Keys[0].Value.Rotation.coeffs().setZero();
        Reject([&] { AnimationLibrary invalid(skeleton, clips); });
        clips = library->GetClips();
        clips[0].Notifies = {{0, "invalid"}};
        Reject([&] { AnimationLibrary invalid(skeleton, clips); });
        clips = library->GetClips();
        clips[0].Duration = std::numeric_limits<double>::infinity();
        Reject([&] { AnimationLibrary invalid(skeleton, clips); });
    }

    void RootMotionAndNotifies()
    {
        const auto library = CreateActionDemoLibrary();
        const auto run = library->FindClip("Run");
        Check(Near(library->ExtractMotion(run, 0.9, 2.3).Position.z(), 2.8), "Root motion lost at loop boundary");
        std::vector<FiredNotify> events;
        library->CollectNotifies(run, 0.9, 2.3, events);
        Check(events.size() == 3 && events[0].Name == "Footstep.L" && Near(events[0].Offset, 0.35), "Multi-loop notifies failed");
        library->CollectNotifies(run, 0, 0.25, events);
        Check(events.size() == 1, "Notify endpoint was skipped");
        library->CollectNotifies(run, 0.25, 0.25, events);
        Check(events.empty(), "Zero update re-fired notify");
        library->CollectNotifies(run, 0.25, 0.75, events);
        Check(events.size() == 1 && events[0].Name == "Footstep.R", "Notify fired twice on boundary");
        Reject([&] { (void)library->ExtractMotion(run, 0, 65); });

        Skeleton skeleton{SceneUuid::New(), {{"root", -1, {}}}};
        Transform end;
        end.Position = {1, 0, 0};
        end.Rotation = Quaternion(Eigen::AngleAxisf(1.57079632679F, Vector3::UnitZ()));
        AnimationClip turn{SceneUuid::New(), skeleton.Id, "Turn", 1, true, true,
            {{0, {{0, {}}, {1, end}}}}, {{1, "End"}}};
        AnimationLibrary turns(skeleton, {turn});
        const auto delta = turns.ExtractMotion(0, 0, 2);
        Check(delta.Position.isApprox(Vector3(1, 1, 0), 0.0001F), "Rotational root motion composition failed");
        const auto split = Transform::Combine(turns.ExtractMotion(0, 0.75, 1), turns.ExtractMotion(0, 1, 1.25));
        Check(split.ToMatrix().isApprox(turns.ExtractMotion(0, 0.75, 1.25).ToMatrix(), 0.0001F), "Root delta depends on tick partition");
        turns.CollectNotifies(0, 0, 2, events);
        Check(events.size() == 2, "End notifies must fire exactly once per loop");
    }

    void TransitionsAndCommands()
    {
        AnimationPlayer player(CreateActionDemoLibrary());
        player.Play("Run", 0.2);
        player.Advance(0.1);
        Check(Near(player.BlendWeight(), 0.5), "Crossfade clock failed");
        const auto visible = player.LocalPose();
        player.Play("Attack", 0.2);
        for (std::size_t i = 0; i < visible.size(); ++i)
            Check(player.LocalPose()[i].ToMatrix().isApprox(visible[i].ToMatrix()), "Interrupted transition popped");
        Reject([&] { player.Advance(-1); });
        Reject([&] { player.Play("missing"); });
        Check(player.Clip().Name == "Attack" && Near(player.Time(), 0), "Failed command mutated player");
        player.Advance(1);
        Check(player.Finished() && Near(player.Time(), 0.8), "Non-looping clip did not clamp");
        player.Advance(1);
        Check(player.Events().empty() && player.RootDelta().Position.isZero(), "Finished clip kept firing events/moving");

        ActionAnimationWorkspace workspace;
        workspace.Execute(AnimationCommand::TriggerAction, 0, "Attack");
        workspace.Execute(AnimationCommand::Step, 0.2);
        Check(workspace.InspectJson().find("\"hit_window\":true") != std::string::npos, "Hit window failed");
        const auto before = workspace.InspectJson();
        Reject([&] { workspace.Execute(AnimationCommand::TriggerAction, 0, "Dodge"); });
        Reject([&] { workspace.Execute(AnimationCommand::SetSpeed, 10); });
        Check(before == workspace.InspectJson(), "Rejected command changed state/history");
        workspace.Execute(AnimationCommand::Step, 0.25);
        Check(workspace.InspectJson().find("\"combo_window\":true") != std::string::npos, "Combo window failed");
        workspace.Execute(AnimationCommand::TriggerAction, 0, "Attack");
        Check(Near(workspace.Player().Time(), 0), "Combo chain did not restart action");
        workspace.Execute(AnimationCommand::Undo);
        Check(Near(workspace.Player().Time(), 0.45), "Undo did not restore player time");
        workspace.Execute(AnimationCommand::Redo);
        Check(Near(workspace.Player().Time(), 0), "Redo did not restore player time");
        workspace.Execute(AnimationCommand::Reset);
        workspace.Execute(AnimationCommand::SetSpeed, 1);
        workspace.Execute(AnimationCommand::TriggerAction, 0, "Attack");
        workspace.Execute(AnimationCommand::Step, 1);
        Check(workspace.Player().Clip().Name == "Run" && Near(workspace.Player().Time(), 0.2), "Action overshoot was dropped");
        Check(Near(workspace.ActorTransform().Position.z(), 1.2), "Action plus locomotion root motion failed");
        Check(workspace.InspectJson().find("Hit.Start") != std::string::npos, "Automatic transition lost events");
        workspace.Execute(AnimationCommand::Undo);
        Check(workspace.ActorTransform().Position.isZero(), "Undo did not restore root motion");
        workspace.Execute(AnimationCommand::SetSpeed, 0);
        Check(!workspace.CanRedo(), "History branch did not invalidate redo");
        const auto paused = workspace.InspectJson();
        workspace.Tick(0.1);
        Check(workspace.InspectJson() == paused, "Paused preview advanced");

        ActionAnimationWorkspace whole, partitioned;
        for (auto* instance : {&whole, &partitioned})
        {
            instance->Execute(AnimationCommand::SetSpeed, 1);
            instance->Execute(AnimationCommand::TriggerAction, 0, "Attack");
        }
        whole.Execute(AnimationCommand::Step, 1);
        std::size_t hits = 0;
        for (int i = 0; i < 60; ++i)
        {
            partitioned.Execute(AnimationCommand::Step, 1.0 / 60);
            if (partitioned.InspectJson().find("Hit.Start") != std::string::npos) ++hits;
        }
        Check(hits == 1, "Partitioned ticks missed or duplicated the attack notify");
        Check(partitioned.ActorTransform().ToMatrix().isApprox(whole.ActorTransform().ToMatrix(), 0.00001F),
            "Root motion changes with update partitioning");
        Check(Near(partitioned.Player().Time(), whole.Player().Time()), "State transition dropped subframe time");

        for (int i = 0; i < 140; ++i) whole.Execute(AnimationCommand::SetSpeed, 0);
        for (int i = 0; i < 128; ++i) whole.Execute(AnimationCommand::Undo);
        Check(!whole.CanUndo(), "Undo history is not bounded");
    }

    void Abi()
    {
        Check(ncma_animation_abi_version()==2,"Animation ABI mismatch");
        Check(ncma_animation_create(1)==0 && ncma_animation_create(99)==0,"Old/unknown ABI accepted");
        const auto resource=ncma_animation_create(2);
        Check(resource!=0,"Immutable resource create failed");
        std::uint32_t count=0;
        Check(ncma_animation_read_library(resource,nullptr,0,&count)==2 && count>2,"Metadata query failed");
        std::vector<char> report(count);
        Check(ncma_animation_read_library(resource,report.data(),count,&count)==1,"Metadata copy failed");
        Check(std::string(report.data()).find("\"schema_version\":2")!=std::string::npos,"Metadata schema");
        const auto library=CreateActionDemoLibrary(); AnimationPlayer player(library);
        player.Play("Attack",0); player.Advance(.2);
        std::vector<float> sample(12*26,-999);
        Check(ncma_animation_sample(resource,2,.2,nullptr,0,1,nullptr,0,&count)==2 && count==sample.size(),"Pose query");
        Check(ncma_animation_sample(resource,2,.2,nullptr,0,1,sample.data(),1,&count)==2 && sample[0]==-999,"Short buffer wrote");
        Check(ncma_animation_sample(resource,2,.2,nullptr,0,1,sample.data(),count,&count)==1,"Pose sample");
        for(std::size_t i=0;i<12;i++)
            for(std::size_t j=0;j<16;j++) Check(Near(sample[120+i*16+j],player.ModelMatrices()[i].data()[j]),"Pose reference mismatch");
        float motion[10]{};
        Check(ncma_animation_motion(resource,2,0,.2,motion,10,&count)==1 && Near(motion[2],.5),"Root reference mismatch");
        Check(ncma_animation_notifies(resource,2,0,.2,nullptr,0,&count)==2,"Notify query");
        std::vector<char> events(count);
        Check(ncma_animation_notifies(resource,2,0,.2,events.data(),count,&count)==1 &&
            std::string(events.data()).find("Hit.Start")!=std::string::npos,"Notify reference mismatch");
        const auto saved=sample;
        Check(ncma_animation_sample(resource,99,0,nullptr,0,1,sample.data(),static_cast<std::uint32_t>(sample.size()),&count)==0 && saved==sample,"Invalid clip partially wrote");
        Check(ncma_animation_sample(resource,0,std::numeric_limits<double>::quiet_NaN(),nullptr,0,1,sample.data(),static_cast<std::uint32_t>(sample.size()),&count)==0,"NaN accepted");
        Check(ncma_animation_command(resource,4,.1,nullptr)==0 && ncma_animation_inspect(resource)==nullptr,"Legacy policy accepted");
        ncma_animation_destroy(resource);
        Check(ncma_animation_read_library(resource,nullptr,0,&count)==0,"Disposed generation accepted");
        const auto next=ncma_animation_create(2); Check(next>resource,"Generation reused"); ncma_animation_destroy(next);

    }
}

int main(int argc, char** argv)
{
    if (argc==2 && std::string(argv[1])=="--action-reference") {
        ActionAnimationWorkspace w;
        std::cout << '[' << w.InspectJson();
        auto run=[&](AnimationCommand c,double value=0,const char* name="") {
            w.Execute(c,value,name); std::cout << ',' << w.InspectJson();
        };
        run(AnimationCommand::SetSpeed,1);
        run(AnimationCommand::Step,.1);
        run(AnimationCommand::TriggerAction,0,"Attack");
        run(AnimationCommand::Step,.2);
        run(AnimationCommand::Step,.25);
        run(AnimationCommand::TriggerAction,0,"Attack");
        run(AnimationCommand::Undo);
        run(AnimationCommand::Redo);
        run(AnimationCommand::Step,1);
        run(AnimationCommand::Reset);
        run(AnimationCommand::TriggerAction,0,"Dodge");
        run(AnimationCommand::Step,.1);
        run(AnimationCommand::Step,.8);
        run(AnimationCommand::Undo);
        run(AnimationCommand::Redo);
        std::cout << "]"; return 0;
    }
    try
    {
        SamplingAndValidation(); RootMotionAndNotifies(); TransitionsAndCommands(); Abi();
        std::cout << "Animation: sampling, skin matrices, masks, root motion, events, transitions, history and ABI passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "Animation test failed: " << error.what() << '\n';
        return 1;
    }
}
