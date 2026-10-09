using Ncma.Animation;
using Ncma.Assets;
using Ncma.Characters;
using System.Numerics;
using System.Text.Json;

internal static unsafe partial class Program
{
    private static void TestMontageRootRecipes(string output)
    {
        var source=SyntheticModel();var skeleton=new SkeletonPayload(source.Bones);Guid right=Guid.NewGuid(),left=Guid.NewGuid(),slot=Guid.NewGuid(),a=Guid.NewGuid(),b=Guid.NewGuid(),rig=Guid.NewGuid();
        var definition=new AnimationMontageDefinition(1,Guid.NewGuid(),"Interval root candidate",rig,
            [new(slot,"Body",a,10,true,true,.05,.1)],
            [new(a,"Attack",slot,right,.1,.25,b),new(b,"Recovery",slot,left,.4,.65,Guid.Empty)]);
        var program=new AnimationMontageProgram(definition,1,[new(right,rig,1,1),new(left,rig,1,1)]);
        var tracks=new Dictionary<Guid,RootMotionTrack>{[right]=new(skeleton,new(2,source.Clips[0]),0),[left]=new(skeleton,new(2,source.Clips[1]),0)};
        var recipe=new MontageRootMotionRecipe(program,tracks);tracks.Clear();
        var playback=new AnimationMontagePlayback(program,new(Guid.NewGuid(),Guid.NewGuid(),0));var intervals=new MontageInterval[528];var results=new MontageRootContribution[16];
        playback.Request(new(Guid.NewGuid(),playback.InstanceId,playback.Frame.Context,slot,MontageRequestKind.Play,Guid.Empty,10));
        void Step(double dt){var token=playback.Prepare(playback.Frame.Context,dt);playback.Commit(token,playback.Frame.Context with{Tick=playback.Frame.Context.Tick+1});}
        Step(.25);int count=playback.CopyCommittedIntervals(intervals);Check(count==2&&recipe.Evaluate(intervals.AsSpan(0,count),.25,results)==1,"Cross-Clip root preparation");
        Check(Math.Abs(results[0].Delta.Translation.X-.025)<1e-6&&Math.Abs(results[0].Coverage-.9)<1e-6,"Independent linear root and envelope oracle");
        Step(.25);count=playback.CopyCommittedIntervals(intervals);recipe.Evaluate(intervals.AsSpan(0,count),.25,results);
        Check(!playback.ReadSlot(slot).Active&&Math.Abs(results[0].Delta.Translation.X+.1)<1e-6&&Math.Abs(results[0].Coverage-.4)<1e-6,"Terminal mean root contribution survives final Slot weight0, uncovered base fraction remains");
        var valid=intervals.AsSpan(0,count).ToArray();var sentinel=new MontageRootContribution(Guid.NewGuid(),new(new(999,0,0),0),.123f);
        foreach(var bad in new[]{valid[0] with{SlotId=Guid.NewGuid()},valid[0] with{SectionId=a},valid[0] with{ClipId=right},valid[0] with{RootMotion=false},
            valid[0] with{Previous=.3},valid[0] with{Current=.7},valid[0] with{Current=double.NaN},valid[0] with{AverageWeight=float.NaN},
            valid[0] with{AverageWeight=-.1f},valid[0] with{AverageWeight=1.1f},valid[0] with{StepFraction=double.NaN},valid[0] with{StepFraction=0},valid[0] with{StepFraction=.1}}){
            results[0]=sentinel;Reject(()=>recipe.Evaluate([bad],.25,results));Check(results[0]==sentinel,"Invalid interval never copies partial root output");}
        results[0]=sentinel;Reject(()=>recipe.Evaluate([valid[0],valid[0]],.25,results));Check(results[0]==sentinel,"Repeated/oversubscribed interval refused atomically");
        Reject(()=>recipe.Evaluate(valid,.25,[]));Reject(()=>recipe.Evaluate(valid,double.NaN,results));Reject(()=>recipe.Evaluate(new MontageInterval[529],.25,results));
        Task.Run(()=>Reject(()=>recipe.Evaluate(valid,.25,results))).GetAwaiter().GetResult();
        recipe.Evaluate(valid,.25,results);Check(Math.Abs(results[0].Delta.Translation.X+.1)<1e-6,"Rejected scratch does not contaminate next result");
        var disabled=definition with{Slots=[definition.Slots[0] with{RootMotion=false}]};var disabledProgram=new AnimationMontageProgram(disabled,1,[new(right,rig,1,1),new(left,rig,1,1)]);
        var disabledRecipe=new MontageRootMotionRecipe(disabledProgram,new Dictionary<Guid,RootMotionTrack>{[right]=new(skeleton,new(2,source.Clips[0]),0),[left]=new(skeleton,new(2,source.Clips[1]),0)});
        disabledRecipe.Evaluate(valid.Select(i=>i with{RootMotion=false}).ToArray(),.25,results);Check(results[0].Delta==default&&results[0].Coverage==0,"Authored root opt-out retains full base intent");
        recipe.Evaluate([], .25,results);Check(results[0].Delta==default&&results[0].Coverage==0,"Cancel/no intervals cannot acquire root authority");
        Reject(()=>new MontageRootMotionRecipe(program,new Dictionary<Guid,RootMotionTrack>{[right]=new(skeleton,new(2,source.Clips[0]),0)}));
        Reject(()=>new MontageRootMotionRecipe(program,new Dictionary<Guid,RootMotionTrack>{[right]=new(skeleton,new(2,source.Clips[0] with{Duration=2}),0),[left]=new(skeleton,new(2,source.Clips[1]),0)}));

        // Independent row-vector transform oracle: second local interval follows the first interval's heading.
        ClipPayload Turn()=>new(2,new("Turn",1,[new(0,[new(0,new(Vector3.Zero,Quaternion.Identity,Vector3.One)),new(1,new(Vector3.UnitX,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),Vector3.One))])]));
        var turnDefinition=definition with{Slots=[definition.Slots[0] with{BlendIn=0,BlendOut=0}],Sections=[definition.Sections[0] with{Start=0,End=.5},definition.Sections[1] with{Start=0,End=.5}]};
        var turnProgram=new AnimationMontageProgram(turnDefinition,1,[new(right,rig,1,1),new(left,rig,1,1)]);
        var turnRecipe=new MontageRootMotionRecipe(turnProgram,new Dictionary<Guid,RootMotionTrack>{[right]=new(skeleton,Turn(),0),[left]=new(skeleton,new(2,source.Clips[0]),0)});
        MontageInterval[] turnIntervals=[new(slot,a,right,0,.5,true){AverageWeight=1,StepFraction=.5},new(slot,b,left,0,.5,true){AverageWeight=1,StepFraction=.5}];
        turnRecipe.Evaluate(turnIntervals,1,results);
        var expected=Matrix4x4.CreateTranslation(.5f,0,0)*(Matrix4x4.CreateRotationY(MathF.PI/4)*Matrix4x4.CreateTranslation(.5f,0,0));
        Check(Vector3.Distance(results[0].Delta.Translation,expected.Translation)<1e-6&&Math.Abs(results[0].Delta.Yaw-MathF.PI/4)<1e-6&&results[0].Coverage==1,"Section root transforms follow accumulated heading, not unrotated vector sum");
        var cyclic=definition with{Slots=[definition.Slots[0] with{BlendIn=0,BlendOut=0}],Sections=[definition.Sections[0] with{Start=0,End=.001,NextSection=a}]};
        var cycleProgram=new AnimationMontageProgram(cyclic,1,[new(right,rig,1,1)]);var cycleRecipe=new MontageRootMotionRecipe(cycleProgram,new Dictionary<Guid,RootMotionTrack>{[right]=new(skeleton,new(2,source.Clips[0]),0)});
        var cycles=Enumerable.Range(0,33).Select(_=>new MontageInterval(slot,a,right,0,.001,true){AverageWeight=1,StepFraction=1d/33}).ToArray();results[0]=sentinel;Reject(()=>cycleRecipe.Evaluate(cycles,.033,results));Check(results[0]==sentinel,"33 actual boundaries rejected without output");
        var fullSlots=new AnimationMontageSlot[16];var fullSections=new AnimationMontageSection[16];for(int i=0;i<16;i++){Guid id=Guid.NewGuid(),entry=Guid.NewGuid();fullSlots[i]=new(id,"Body"+i,entry,10,true,true,0,0);fullSections[i]=new(entry,"Cycle",id,right,0,.001,entry);}
        var fullProgram=new AnimationMontageProgram(cyclic with{Slots=fullSlots,Sections=fullSections},1,[new(right,rig,1,1)]);var fullPlayback=new AnimationMontagePlayback(fullProgram,new(Guid.NewGuid(),Guid.NewGuid(),0));
        foreach(var s in fullSlots)fullPlayback.Request(new(Guid.NewGuid(),fullPlayback.InstanceId,fullPlayback.Frame.Context,s.Id,MontageRequestKind.Play,Guid.Empty,10));
        var fullToken=fullPlayback.Prepare(fullPlayback.Frame.Context,.0325);fullPlayback.Commit(fullToken,fullPlayback.Frame.Context with{Tick=1});Check(fullPlayback.CopyCommittedIntervals(intervals)==528,"All16 Slots emit528 intervals");
        var fullRecipe=new MontageRootMotionRecipe(fullProgram,new Dictionary<Guid,RootMotionTrack>{[right]=new(skeleton,new(2,source.Clips[0]),0)});Check(fullRecipe.Evaluate(intervals,.0325,results)==16,"Complete16 root output");
        Check(results.All(r=>Math.Abs(r.Delta.Translation.X-.0325)<1e-6&&Math.Abs(r.Coverage-1)<1e-6),"Independent maximum-budget linear oracle");
        var frozen=results.ToArray();var corrupt=intervals.ToArray();corrupt[^1]=corrupt[^1] with{ClipId=Guid.NewGuid()};Reject(()=>fullRecipe.Evaluate(corrupt,.0325,results));Check(frozen.SequenceEqual(results),"Last of528 invalid rows preserves all16 destinations");
        for(int i=0;i<32;i++)turnRecipe.Evaluate(turnIntervals,1,results);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1024;i++)turnRecipe.Evaluate(turnIntervals,1,results);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;Check(allocated==0,"Warm interval root recipe allocates0");
        File.WriteAllBytes(Path.Combine(output,"montage-root-recipe-results.json"),JsonSerializer.SerializeToUtf8Bytes(new{
            schema=1,crossClip=true,terminalPartialCoverage=true,independentLinearOracle=true,independentHeadingOracle=true,rootOptOut=true,invalidWholeOutputAtomic=true,
            ownerThread=true,boundary33Rejected=true,slots16Intervals528=true,lastInvalidRowAtomic=true,warmEvaluations=1024,allocatedBytes=allocated,
            policy="per-traversal mean envelope times whole extracted delta; nonlinear root/envelope integration and whole-host frame invariance are not claimed",
            animatorMovementIntegration=false,montagePose=false,notify=false,persistedMontagePackage=false,manualAcceptance=false}));
    }
}
