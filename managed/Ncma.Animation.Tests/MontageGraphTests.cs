using Ncma.Animation;

internal static class MontageGraphTests
{
    private static void Check(bool value,string detail=""){if(!value)throw new Exception("M6.8-B2a same graph transaction: "+detail);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){return;}throw new Exception("Invalid joined Montage accepted.");}
    private static (AnimationProgram Graph,AnimationMontageDefinition Definition,AnimationMontageProgram Montage) Prepare(bool tiny=false)
    {
        var graph=GraphTests.Simple();Guid clip=graph.Nodes.Single(n=>n.Kind==AnimationNodeKind.Clip).ClipId;
        var descriptors=new[]{new AnimationClipDescriptor(clip,graph.SkeletonId,1,1)};
        var d=MontageDataTests.Definition();
        d=d with{SkeletonId=graph.SkeletonId,Sections=d.Sections.Select(s=>s with{ClipId=clip}).ToArray()};
        if(tiny)d=d with{Sections=[d.Sections[0] with{Start=0,End=.001,NextSection=d.Sections[0].Id}],Slots=[d.Slots[0] with{BlendIn=0,BlendOut=0}]};
        var program=AnimationProgram.Compile(Attach(graph,d),1,descriptors);return(program,d,program.Montage!);
    }
    internal static AnimationGraphDefinition Attach(AnimationGraphDefinition graph,AnimationMontageDefinition montage,bool startup=false)
    {
        var output=graph.Nodes.Single(n=>n.Kind==AnimationNodeKind.Output);var edge=graph.Links.Single(l=>l.To==output.Id);var nodes=graph.Nodes.ToList();var links=graph.Links.Where(l=>l.Id!=edge.Id).ToList();Guid previous=edge.From;
        foreach(var slot in montage.Slots){var node=AnimationGraphNode.Create(Guid.NewGuid(),slot.Name,AnimationNodeKind.Slot) with{SlotId=slot.Id,PlayOnStart=startup};nodes.Add(node);links.Add(new(Guid.NewGuid(),previous,"pose",node.Id,"pose"));previous=node.Id;}
        links.Add(new(Guid.NewGuid(),previous,"pose",output.Id,"pose"));return graph with{Montage=montage,Nodes=nodes.ToArray(),Links=links.ToArray()};
    }
    private static MontageRequest Request(AnimationGraphInstance instance,AnimationMontageDefinition d)=>
        new(Guid.NewGuid(),instance.Frame.InstanceId,instance.Frame.Context,d.Slots[0].Id,MontageRequestKind.Play,Guid.Empty,10);
    private static void Step(AnimationGraphInstance instance,double delta=.1)
    {var t=instance.Prepare(instance.Frame.Context,delta);instance.Commit(t,instance.Frame.Context with{Tick=instance.Frame.Context.Tick+1});}
    public static void Add(List<(string,Action)> tests)
    {
        tests.Add(("M6.8-B2a graph owns Montage identity/token/clock/requests and exact joint commit",()=>{
            var p=Prepare();var c=new AnimationStepContext(Guid.NewGuid(),Guid.NewGuid(),0);var i=new AnimationGraphInstance(p.Graph,c);
            Check(i.ReadMontageFrame().InstanceId==i.Frame.InstanceId&&i.ReadMontageFrame().Context==c);var r=Request(i,p.Definition);Check(i.RequestMontage(r)&&!i.RequestMontage(r));
            var t=i.Prepare(c,.5);Check(i.PreparedMontageSlot(t,p.Definition.Slots[0].Id).Active);var rows=new MontageInterval[528];Check(i.CopyPreparedMontageIntervals(t,rows)==2);
            Reject(()=>i.ReadMontageSlot(p.Definition.Slots[0].Id));Reject(()=>i.RequestMontage(r));Reject(()=>i.CopyPreparedMontageIntervals(t with{InstanceId=Guid.NewGuid()},rows));Reject(()=>i.Commit(t,c));i.Abort(t);
            Check(i.Frame.Context.Tick==0&&!i.ReadMontageSlot(p.Definition.Slots[0].Id).Active&&i.ReadMontageFrame().ReceiptCount==0);Step(i,.5);
            Check(i.ReadMontageFrame().Context==i.Frame.Context&&i.ReadMontageFrame().Sequence==1+1&&i.CopyCommittedMontageIntervals(rows)==2);
            var receipt=new MontageReceipt[64];Check(i.CopyCommittedMontageReceipts(receipt)==1&&receipt[0].RequestId==r.RequestId);Reject(()=>i.RequestMontage(r));
            Step(i,.3);Check(!i.ReadMontageSlot(p.Definition.Slots[0].Id).Active&&i.CopyCommittedMontageIntervals(rows)==1&&rows[0].Current==.8,"Terminal interval retained in SAME successful graph quantum.");
            Reject(()=>i.Prepare(i.Frame.Context,double.NaN));Step(i);
            Check(i.ReadMontageFrame().Sequence==i.ReadDebug(i.Frame.Context).CommittedSequence,"Graph preflight failure cannot create a second Montage sequence chronology.");
        }));
        tests.Add(("M6.8-B2a graph preparation failure aborts prepared Montage without consuming requests",()=>{
            var d=GraphTests.Mixed();var descriptors=AnimationGraphValidation.ClipIds(d).Select(id=>new AnimationClipDescriptor(id,d.SkeletonId,1,1)).ToArray();
            var m=MontageDataTests.Definition();m=m with{SkeletonId=d.SkeletonId,Sections=m.Sections.Select(s=>s with{ClipId=descriptors[0].Id}).ToArray()};var graph=AnimationProgram.Compile(Attach(d,m),1,descriptors);var i=new AnimationGraphInstance(graph,new(Guid.NewGuid(),Guid.NewGuid(),0));
            i.RequestMontage(Request(i,m));i.SetFloat(d.Parameters[0].Id,2);Reject(()=>i.Prepare(i.Frame.Context,.1));Check(i.Frame.Context.Tick==0&&!i.ReadDebug(i.Frame.Context).SnapshotValid);
            Reject(()=>i.ReadMontageFrame());Reject(()=>i.CopyCommittedMontageIntervals(new MontageInterval[528]));i.SetFloat(d.Parameters[0].Id,.25);Step(i);
            Check(i.ReadMontageSlot(m.Slots[0].Id).Active&&Math.Abs(i.ReadMontageSlot(m.Slots[0].Id).Time-.2)<1e-12&&i.ReadMontageFrame().ReceiptCount==1);
        }));
        tests.Add(("M6.8-B2a Montage overflow rejects graph quantum and does not publish graph time",()=>{
            var p=Prepare(true);var i=new AnimationGraphInstance(p.Graph,new(Guid.NewGuid(),Guid.NewGuid(),0));i.RequestMontage(Request(i,p.Definition));Step(i,.032);
            var before=new AnimationPoseInstruction[p.Graph.MaximumPlanInstructions];int count=i.CopyCommittedPlan(before);Reject(()=>i.Prepare(i.Frame.Context,.033));
            var after=new AnimationPoseInstruction[before.Length];Check(i.CopyCommittedPlan(after)==count&&before.SequenceEqual(after)&&i.Frame.Context.Tick==1);Reject(()=>i.ReadMontageFrame());Step(i,.001);
            Check(i.ReadMontageFrame().Context.Tick==2&&i.ReadMontageFrame().IntervalCount==1);
        }));
        tests.Add(("M6.8-B2a absent/incompatible binding and foreign instance requests rejected",()=>{
            var p=Prepare();var c=new AnimationStepContext(Guid.NewGuid(),Guid.NewGuid(),0);var plain=GraphTests.Simple();var none=new AnimationGraphInstance(AnimationProgram.Compile(plain,1,[new(plain.Nodes[0].ClipId,plain.SkeletonId,1,1)]),c);Reject(()=>none.ReadMontageFrame());Reject(()=>none.RequestMontage(Request(none,p.Definition)));
            var i=new AnimationGraphInstance(p.Graph,c);var other=new AnimationGraphInstance(p.Graph,c);Reject(()=>other.RequestMontage(Request(i,p.Definition)));Check(!other.ReadMontageSlot(p.Definition.Slots[0].Id).Active);
            Reject(()=>AnimationGraphCodec.Encode(Attach(plain,p.Definition with{SkeletonId=Guid.NewGuid()})));
            Reject(()=>new AnimationMontageProgram(p.Definition,2,[new(p.Definition.Sections[0].ClipId,p.Definition.SkeletonId,1,1)]));
            Check(Task.Run(()=>{try{i.ReadMontageFrame();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());
        }));
        tests.Add(("M6.8-B2a warmed joint graph/Montage requests and1024 fixed candidates allocate0",()=>{
            var p=Prepare();var i=new AnimationGraphInstance(p.Graph,new(Guid.NewGuid(),Guid.NewGuid(),0));
            for(int n=0;n<1024;n++){i.RequestMontage(Request(i,p.Definition));Step(i);}long before=GC.GetAllocatedBytesForCurrentThread();
            for(int n=0;n<1024;n++){i.RequestMontage(Request(i,p.Definition));Step(i);Check(i.ReadMontageFrame().Context==i.Frame.Context);}
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"Only warmed cooperating numerical state, not whole World/tools performance.");
        }));
    }
}
