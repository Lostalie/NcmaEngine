using System.Text;
using Ncma.Animation;

internal static class BlendSpaceRuntimeTests
{
    private static void Check(bool value,string reason=""){if(!value)throw new Exception("BlendSpace runtime: "+reason);}
    private static void Reject(Action f){try{f();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or System.Text.Json.JsonException){return;}throw new Exception("Invalid BlendSpace runtime accepted.");}
    internal static AnimationGraphDefinition Graph(bool two=false)
    {
        var d=GraphTests.Simple();var axis=new AnimationParameter(Guid.NewGuid(),"Speed",AnimationParameterKind.Float,.5,0,false);var turn=axis with{Id=Guid.NewGuid(),Name="Turn",FloatDefault=.2};
        var s=BlendSpaceTests.Definition((0,0),(1,0),(0,1));s=s with{Dimensions=two?2:1,AxisX=s.AxisX with{ParameterId=axis.Id},AxisY=two?s.AxisY! with{ParameterId=turn.Id}:null,Samples=two?s.Samples:s.Samples.Take(2).ToArray()};
        var n=AnimationGraphNode.Create(d.Nodes[0].Id,"Space",AnimationNodeKind.BlendSpace) with{BlendSpace=s,Loop=true,Speed=1};return d with{Nodes=[n,d.Nodes[1]],Parameters=two?[axis,turn]:[axis]};
    }
    private static AnimationProgram Compile(AnimationGraphDefinition d)=>AnimationProgram.Compile(d,7,AnimationGraphValidation.ClipIds(d).Select((id,i)=>new AnimationClipDescriptor(id,d.SkeletonId,7,i==0?1:2)).ToArray());
    private static AnimationPoseInstruction[] Plan(AnimationGraphInstance i){var rows=new AnimationPoseInstruction[256];int count=i.CopyCommittedPlan(rows);return rows.Take(count).ToArray();}
    private static void Step(AnimationGraphInstance i,double delta=.1){var t=i.Prepare(i.Frame.Context,delta);i.Commit(t,i.Frame.Context with{Tick=i.Frame.Context.Tick+1});}
    private static AnimationGraphInstance Instance(AnimationProgram p)=>new(p,new(Guid.NewGuid(),Guid.NewGuid(),0));
    public static void Add(List<(string,Action)> cases)
    {
        cases.Add(("M6.6-B owned samples/closed fields/resource closure and removed graph formats rejected",()=>{
            var d=Graph(true);byte[] encoded=AnimationGraphCodec.Encode(d);var copy=AnimationGraphCodec.Decode(encoded);Check(copy.Version==AnimationGraphCodec.CurrentVersion&&AnimationGraphValidation.Dependencies(copy).Count==4);Check(encoded.SequenceEqual(AnimationGraphCodec.Encode(d with{Nodes=d.Nodes.Select(n=>n.BlendSpace is{} s?n with{BlendSpace=s with{Samples=s.Samples.Reverse().ToArray()}}:n).ToArray()})));
            foreach(string bad in new[]{Encoding.UTF8.GetString(encoded).Replace("\"version\":4","\"version\":2"),Encoding.UTF8.GetString(encoded).Replace("\"version\":4","\"version\":3"),Encoding.UTF8.GetString(encoded).Replace("\"blendSpace\":null", "\"blendSpace\":null,\"blendSpace\":null"),Encoding.UTF8.GetString(encoded).Replace(",\"blendSpace\":null",""),Encoding.UTF8.GetString(encoded).Replace("\"dimensions\":2","\"dimensions\":3"),Encoding.UTF8.GetString(encoded).Replace("\"cycleSeconds\":1","\"cycleSeconds\":1,\"code\":\"run\"")})Reject(()=>AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(bad)));
            var owned=new AnimationGraphDocument(d);d.Nodes[0].BlendSpace!.Samples[0]=d.Nodes[0].BlendSpace!.Samples[0] with{X=.3};Check(owned.CopyBytes().SequenceEqual(encoded));
            Reject(()=>AnimationProgram.Compile(copy,7,AnimationGraphValidation.ClipIds(copy).Select(id=>new AnimationClipDescriptor(id,copy.SkeletonId,8,1)).ToArray()));
        }));
        cases.Add(("M6.6-B 1D/2D recipes share normalized phase across unequal clip durations",()=>{
            foreach(bool two in new[]{false,true}){var d=Graph(two);var p=Compile(d);var i=Instance(p);Step(i);var plan=Plan(i);Check(plan[^1].Operation==AnimationPoseOperation.RootSource&&plan[plan[^1].SourceB].Operation==AnimationPoseOperation.Clip);var clips=plan.Where(r=>r.Operation==AnimationPoseOperation.Clip).ToArray();Check(clips.Length==(two?3:2)&&clips.All(r=>Math.Abs(r.Current/r.Duration-.1)<1e-10)&&clips.All(r=>r.Previous==0));
                Step(i);Check(Plan(i).Where(r=>r.Operation==AnimationPoseOperation.Clip).All(r=>Math.Abs(r.Current/r.Duration-.2)<1e-10));}
        }));
        cases.Add(("M6.6-B same-group peer nodes advance once and enforce exact cycle/loop/speed",()=>{
            var d=Graph();var n=d.Nodes[0];var peer=n with{Id=Guid.NewGuid(),Name="Peer",BlendSpace=n.BlendSpace! with{Id=Guid.NewGuid(),Samples=n.BlendSpace.Samples.Select(s=>s with{Id=Guid.NewGuid()}).ToArray()}};Guid group=Guid.NewGuid();n=n with{BlendSpace=n.BlendSpace! with{SyncGroup=group}};peer=peer with{BlendSpace=peer.BlendSpace! with{SyncGroup=group}};var blend=AnimationGraphNode.Create(Guid.NewGuid(),"Mix",AnimationNodeKind.Blend) with{Weight=.5};var end=d.Nodes[1];d=d with{Nodes=[n,peer,blend,end],Links=[new(Guid.NewGuid(),n.Id,"pose",blend.Id,"a"),new(Guid.NewGuid(),peer.Id,"pose",blend.Id,"b"),new(Guid.NewGuid(),blend.Id,"pose",end.Id,"pose")]};var i=Instance(Compile(d));Step(i);Check(Plan(i).Where(r=>r.Operation==AnimationPoseOperation.Clip).All(r=>Math.Abs(r.Current/r.Duration-.1)<1e-10));
            foreach(var bad in new[]{peer with{Speed=2},peer with{Loop=false},peer with{BlendSpace=peer.BlendSpace! with{CycleSeconds=2}}})Reject(()=>AnimationGraphCodec.Encode(d with{Nodes=[n,bad,blend,end]}));
        }));
        cases.Add(("M6.6-B Abort/typed safe boundary/nonloop endpoints and 32 phase crossings",()=>{
            var d=Graph();var i=Instance(Compile(d));Step(i);var before=Plan(i);var token=i.Prepare(i.Frame.Context,.2);Reject(()=>i.SetFloat(d.Parameters[0].Id,.2));i.Abort(token);Check(before.SequenceEqual(Plan(i)));Step(i);Check(Plan(i).Where(r=>r.Operation==AnimationPoseOperation.Clip).All(r=>Math.Abs(r.Previous/r.Duration-.1)<1e-10&&Math.Abs(r.Current/r.Duration-.2)<1e-10));
            var nonloop=d with{Nodes=d.Nodes.Select(n=>n.Kind==AnimationNodeKind.BlendSpace?n with{Loop=false}:n).ToArray()};i=Instance(Compile(nonloop));Step(i,1);Step(i,1);Check(Plan(i).Where(r=>r.Operation==AnimationPoseOperation.Clip).All(r=>r.Previous==r.Duration&&r.Current==r.Duration));
            var shortCycle=d with{Nodes=d.Nodes.Select(n=>n.BlendSpace is{} s?n with{BlendSpace=s with{CycleSeconds=.001}}:n).ToArray()};i=Instance(Compile(shortCycle));Reject(()=>i.Prepare(i.Frame.Context,.1));Check(!i.ReadDebug(i.Frame.Context).SnapshotValid&&i.Frame.Context.Tick==0);
        }));
        cases.Add(("M6.6-B changing primary sends only new target interval, not duplicate/catch-up events",()=>{
            var d=Graph();var s=d.Nodes[0].BlendSpace!;d=d with{Events=[new(Guid.NewGuid(),s.Samples[0].ClipId,.1,"A"),new(Guid.NewGuid(),s.Samples[1].ClipId,.2,"B")]};var p=AnimationProgram.Compile(d,7,[new(s.Samples[0].ClipId,d.SkeletonId,7,1),new(s.Samples[1].ClipId,d.SkeletonId,7,2)]);var i=Instance(p);i.SetFloat(d.Parameters[0].Id,0);Step(i);Check(i.ReadDebug(i.Frame.Context).Events.Single().Name=="A");i.SetFloat(d.Parameters[0].Id,1);Step(i);Check(i.ReadDebug(i.Frame.Context).Events.Count==0);Step(i,.8);Check(i.ReadDebug(i.Frame.Context).Events.Count==0);Step(i);Check(i.ReadDebug(i.Frame.Context).Events.Single().Name=="B");
        }));
        cases.Add(("M6.6-B state reentry restarts target phase and exit time uses declared cycle",()=>{
            var d=Graph();var space=d.Nodes[0];var clip=AnimationGraphNode.Create(Guid.NewGuid(),"Clip",AnimationNodeKind.Clip) with{ClipId=Guid.NewGuid(),Loop=true,Speed=1};var machine=AnimationGraphNode.Create(Guid.NewGuid(),"States",AnimationNodeKind.StateMachine);var a=new AnimationGraphState(Guid.NewGuid(),"Space",space.Id);var b=new AnimationGraphState(Guid.NewGuid(),"Clip",clip.Id);var flag=new AnimationParameter(Guid.NewGuid(),"Switch",AnimationParameterKind.Bool,0,0,false);d=d with{Parameters=[..d.Parameters,flag],Nodes=[space,clip,machine,d.Nodes[1]],EntryState=a.Id,States=[a,b],Links=[new(Guid.NewGuid(),machine.Id,"pose",d.Nodes[1].Id,"pose")],Transitions=[new(Guid.NewGuid(),a.Id,b.Id,0,0,.5,[new(flag.Id,AnimationComparison.Equal,0,0,true)]),new(Guid.NewGuid(),b.Id,a.Id,0,0,null,[new(flag.Id,AnimationComparison.Equal,0,0,false)])]};var i=Instance(Compile(d));i.SetBool(flag.Id,true);for(int step=0;step<4;step++){Step(i);Check(i.Frame.StateId==a.Id);}Step(i);Check(i.Frame.StateId==b.Id);i.SetBool(flag.Id,false);Step(i);Check(i.Frame.StateId==a.Id&&Plan(i).Where(r=>r.Operation==AnimationPoseOperation.Clip).All(r=>r.Previous==0&&Math.Abs(r.Current/r.Duration-.1)<1e-10));
        }));
        cases.Add(("M6.6-B 32 isolated graph actors and warmed committed sample recipe allocates zero",()=>{
            var d=Graph(true);var p=Compile(d);var actors=Enumerable.Range(0,32).Select(_=>Instance(p)).ToArray();for(int n=0;n<actors.Length;n++){actors[n].SetFloat(d.Parameters[0].Id,n/31d);Step(actors[n]);}var first=Plan(actors[0]);var last=Plan(actors[^1]);Check(actors.Select(i=>i.Frame.InstanceId).Distinct().Count()==32&&first[first[^1].SourceB].ClipId!=last[last[^1].SourceB].ClipId);
            var i=Instance(p);for(int n=0;n<512;n++)Step(i,.01);long before=GC.GetAllocatedBytesForCurrentThread();for(int n=0;n<1024;n++)Step(i,.01);Check(GC.GetAllocatedBytesForCurrentThread()==before&&i.Frame.Context.Tick==1536);
        }));
    }
}
