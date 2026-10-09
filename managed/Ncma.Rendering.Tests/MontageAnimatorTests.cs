using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    // B2a candidate: real NCA pins and the formal shared Animator lifecycle, NOT Montage pose/root rendering.
    private static void TestMontageAnimatorBindings(string output)
    {
        var f=new SkinFixture(output);var graph=AnimatorGraph(f);
        File.WriteAllBytes(Path.Combine(f.Root,"assets/montage-binding.ncmaanim"),AnimationGraphCodec.Encode(graph));
        var (source,_,sourceIds)=f.Scene();source.World.FindObject(sourceIds[0]).Remove<ClipPlaybackData>();source.World.FindObject(sourceIds[0]).Set(new AnimatorData(graph.AssetId,graph.SkeletonId));
        using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,source.CaptureSnapshot(),true);
        Guid slot=Guid.NewGuid(),a=Guid.NewGuid(),b=Guid.NewGuid();
        var montage=new AnimationMontageDefinition(1,Guid.NewGuid(),"Pinned Section candidate",graph.SkeletonId,
            [new(slot,"Body",a,10,true,true,.05,.1)],
            [new(a,"Attack",slot,f.Manifest.Clips[0],.1,.25,b),new(b,"Recovery",slot,f.Manifest.Clips[1],.4,.65,Guid.Empty)]);
        var retained=(RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph);
        var program=retained.PrepareMontage(assets.Assets,montage);
        Check(program.ResourceGeneration==f.Plan.Record.Generation!.Number&&assets.Assets.PinnedGenerations==1,"Montage actual exact retained NCA generation");
        Reject(()=>retained.PrepareMontage(assets.Assets,montage with{SkeletonId=Guid.NewGuid()}));
        Reject(()=>retained.PrepareMontage(assets.Assets,montage with{Sections=montage.Sections.Select(s=>s with{End=1.1}).ToArray()}));
        Reject(()=>retained.PrepareMontage(assets.Assets,montage with{Sections=montage.Sections.Select(s=>s with{ClipId=Guid.NewGuid()}).ToArray()}));
        string moved=Path.Combine(output,"montage-binding-moved",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(moved,"assets"));
        File.WriteAllBytes(Path.Combine(moved,"assets/game.ncpak"),SceneAssetPreparation.CreateRuntimePackage(f.Root,f.Project,source.CaptureSnapshot(),[]));
        using var packed=SceneAssetPreparation.Prepare(moved,f.Project,source.CaptureSnapshot(),true,"assets/game.ncpak");
        Check(((RuntimeAnimationGraphAsset)packed.Assets.Require(graph.AssetId,AssetKind.AnimationGraph)).PrepareMontage(packed.Assets,montage).ContentHash==program.ContentHash,"Host-supplied copied candidate can prepare from source-free real NCP1 NCA; Montage itself NOT cooked");
        var evidence=new List<object>();
        foreach(int count in new[]{1,8,32}){
            var (d,_,ids)=f.Scene(count);foreach(Guid id in ids){var obj=d.World.FindObject(id);obj.Remove<ClipPlaybackData>();obj.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));}
            using var play=new PlaySession(d,FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps);
            using var animator=SceneAnimatorRuntime.Compose(play,packed)!;
            Reject(()=>animator.BindMontage(ids[0],montage with{SkeletonId=Guid.NewGuid()}));
            foreach(Guid id in ids)animator.BindMontage(id,montage);Reject(()=>animator.BindMontage(ids[0],montage));
            play.Start(_=>throw new Exception("No behaviours"));play.Pause();
            try{
                var contexts=ids.Select(animator.ReadFrame).ToArray();Check(contexts.Select(c=>c.InstanceId).Distinct().Count()==count,"One graph/Montage identity per actor");
                foreach(Guid id in ids){var frame=animator.ReadFrame(id);Check(animator.ReadMontageFrame(id).InstanceId==frame.InstanceId,"No second instance owner");animator.RequestMontage(id,new(Guid.NewGuid(),frame.InstanceId,frame.Context,slot,MontageRequestKind.Play,Guid.Empty,10));}
                if(count>1){var frame=animator.ReadFrame(ids[0]);Reject(()=>animator.RequestMontage(ids[1],new(Guid.NewGuid(),frame.InstanceId,frame.Context,slot,MontageRequestKind.Cancel,Guid.Empty,10)));}
                Reject(()=>animator.BindMontage(ids[0],montage));var rows=new MontageInterval[528];int terminal=0;
                for(int step=0;step<4;step++){
                    Check(play.Step().State==PlayState.Paused,"Shared Animator Montage quantum "+play.Fault?.Code);
                    foreach(Guid id in ids){var frame=animator.ReadFrame(id);Check(animator.ReadMontageFrame(id).Context==frame.Context&&frame.Context.Tick==play.Tick,"SAME committed tick");int n=animator.CopyCommittedMontageIntervals(id,rows);if(step==1)Check(n==2&&rows[0].ClipId!=rows[1].ClipId,"ALL crossed actual Section Clip intervals retained");if(step==3){Check(!animator.ReadMontageSlot(id,slot).Active&&n==1,"Final interval survives inactive Slot");terminal++;}}
                }
                Guid old=animator.ReadFrame(ids[0]).InstanceId;ulong tick=play.Tick;play.Reload(_=>throw new Exception("No behaviours"));
                Check(play.State==PlayState.Paused&&play.Tick==tick&&animator.ReadFrame(ids[0]).InstanceId!=old&&!animator.ReadMontageSlot(ids[0],slot).Active,"Reload rebuilds joint state with fresh identity and retained tick chronology");
                var next=animator.ReadFrame(ids[0]);Reject(()=>animator.RequestMontage(ids[0],new(Guid.NewGuid(),old,next.Context,slot,MontageRequestKind.Play,Guid.Empty,10)));
                animator.RequestMontage(ids[0],new(Guid.NewGuid(),next.InstanceId,next.Context,slot,MontageRequestKind.Play,Guid.Empty,10));Check(play.Step().State==PlayState.Paused&&animator.ReadMontageSlot(ids[0],slot).Active,"Fresh explicit request after Reload");
                evidence.Add(new{actors=count,ticks=4,terminal,uniqueInstances=true,reload=true,allSectionIntervals=true});
            }finally{play.Stop();}Reject(()=>animator.ReadMontageFrame(ids[0]));
        }
        foreach(int fault in new[]{0,1,2,3}){
            var d=source.CreateIsolatedCopy();using var play=new PlaySession(d,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var animator=SceneAnimatorRuntime.Compose(play,assets)!;animator.BindMontage(sourceIds[0],montage);
            MontageRequest illegal=default;
            play.AddSystem(new GraphFailure(_=>{
                if(fault==0)throw new InvalidOperationException("Prepared Montage downstream failure");
                if(fault==3)return;
                try{if(fault==1)animator.RequestMontage(sourceIds[0],illegal);else animator.BindMontage(sourceIds[0],montage);}catch(InvalidOperationException){}
            }));
            if(fault==3)play.AddCommittedObserver(new MontageObserver((_,_)=>throw new InvalidOperationException("After joint managed commit")));
            play.Start(_=>throw new Exception("No behaviours"));
            try{var frame=animator.ReadFrame(sourceIds[0]);illegal=new(Guid.NewGuid(),frame.InstanceId,frame.Context,slot,MontageRequestKind.Play,Guid.Empty,10);animator.RequestMontage(sourceIds[0],illegal);byte[] before=d.CaptureBytes();
                Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==(fault==3?1ul:0ul),"Failed quantum/observer exact commit chronology");if(fault!=3)Check(before.SequenceEqual(d.CaptureBytes()),"No failed candidate World publication");Reject(()=>animator.ReadMontageFrame(sourceIds[0]));Reject(()=>animator.ReadMontageSlot(sourceIds[0],slot));
            }finally{play.Stop();}
            // Explicit Stop/new Play is recovery. Queue and state never leak into the new owner.
            play.Start(_=>throw new Exception("No behaviours"));try{Check(!animator.ReadMontageSlot(sourceIds[0],slot).Active&&animator.ReadMontageFrame(sourceIds[0]).ReceiptCount==0,"Recovered owner starts from copied binding, no consumed/fault queue");}finally{play.Stop();}
        }
        File.WriteAllText(Path.Combine(output,"m6-8-b2a-montage-binding-results.json"),JsonSerializer.Serialize(new{schema=1,realNca=true,hostSuppliedMontage=true,packedNcaOnly=true,rows=evidence,caughtTickControlsPoison=true,failures=4,montagePoseRootPackageImplemented=false,manualAccepted=false}));
        Console.WriteLine("PASS M6.8-B2a actual NCA/shared Animator/1-8-32 owners/all Section intervals/source-free NCA/Reload/caught control/fault/Stop recovery");
    }
    private sealed class MontageObserver(Action<World,double> action):ICommittedStepObserver
    {public void StepCommitted(World world,double delta)=>action(world,delta);}
}
