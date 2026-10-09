using System.Text;
using System.Text.Json;
using Ncma.Animation;

internal static class MontagePersistentGraphTests
{
    private static void Check(bool v){if(!v)throw new Exception("Persistent Slot assertion.");}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or JsonException){return;}throw new Exception("Invalid persistent Slot accepted.");}
    private static AnimationGraphDefinition Definition(bool startup=true){var g=GraphTests.Simple();var m=MontageDataTests.Definition();m=m with{SkeletonId=g.SkeletonId,Sections=m.Sections.Select(s=>s with{ClipId=g.Nodes[0].ClipId}).ToArray()};return MontageGraphTests.Attach(g,m,startup);}
    private static AnimationProgram Compile(AnimationGraphDefinition g)=>AnimationProgram.Compile(g,1,AnimationGraphValidation.ClipIds(g).Select(id=>new AnimationClipDescriptor(id,g.SkeletonId,1,1)).ToArray());
    private static void Step(AnimationGraphInstance i,double dt=.1){var token=i.Prepare(i.Frame.Context,dt);i.Commit(token,i.Frame.Context with{Tick=i.Frame.Context.Tick+1});}
    public static void Add(List<(string,Action)> cases){
        cases.Add(("M6.8-B2b-2 strict v5 nested Montage/required neutral Slot fields/no old compatibility",()=>{
            var d=Definition();var bytes=AnimationGraphCodec.Encode(d);string json=Encoding.UTF8.GetString(bytes);Check(bytes.SequenceEqual(AnimationGraphCodec.Encode(AnimationGraphCodec.Decode(bytes))));
            foreach(int old in new[]{1,2,3,4})Reject(()=>AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(json.Replace("\"version\":5","\"version\":"+old))));
            foreach(string bad in new[]{json.Replace("\"montage\":","\"unknownMontage\":"),json.Replace("\"slotId\":","\"unknownSlot\":"),json.Replace(",\"playOnStart\":false",""),json.Replace("\"priority\":10","\"priority\":10,\"worldHandle\":1")})Reject(()=>AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(bad)));
            Reject(()=>AnimationGraphCodec.Encode(d with{Nodes=d.Nodes.Select(n=>n.Kind!=AnimationNodeKind.Slot?n with{PlayOnStart=true}:n).ToArray()}));
        }));
        cases.Add(("M6.8-B2b-2 Montage closure/owned draft/actual duration/full final Slot chain",()=>{
            var d=Definition();var section=d.Montage!.Sections[0];var external=Guid.NewGuid();d=d with{Montage=d.Montage with{Sections=d.Montage.Sections.Select(s=>s with{ClipId=external}).ToArray()}};Check(AnimationGraphValidation.ClipIds(d).Length==2);
            var copied=AnimationGraphEdits.CopyDraft(d);copied.Montage!.Sections[0]=section;Check(d.Montage.Sections[0].ClipId==external);var program=Compile(d);d.Montage.Sections[0]=section;Check(program.Montage!.CopyDefinition().Sections.All(s=>s.ClipId==external));
            var valid=Definition();Reject(()=>Compile(valid with{Montage=valid.Montage! with{Sections=valid.Montage.Sections.Select(s=>s with{End=1.1}).ToArray()}}));
            Reject(()=>AnimationGraphCodec.Encode(valid with{Montage=null}));Reject(()=>AnimationGraphCodec.Encode(valid with{Nodes=valid.Nodes.Select(n=>n.Kind==AnimationNodeKind.Slot?n with{SlotId=Guid.NewGuid()}:n).ToArray()}));
            var slot=valid.Nodes.Single(n=>n.Kind==AnimationNodeKind.Slot);var output=valid.Nodes.Single(n=>n.Kind==AnimationNodeKind.Output);var cache=AnimationGraphNode.Create(Guid.NewGuid(),"Forbidden post-Slot cache",AnimationNodeKind.CachePose);
            Reject(()=>AnimationGraphCodec.Encode(valid with{Nodes=[..valid.Nodes,cache],Links=[..valid.Links.Where(l=>l.To!=output.Id),new(Guid.NewGuid(),slot.Id,"pose",cache.Id,"pose"),new(Guid.NewGuid(),cache.Id,"pose",output.Id,"pose")]}));
        }));
        cases.Add(("M6.8-B2b-2 persistent startup/static final sample/terminal interval and next-step base",()=>{
            var d=Definition();var p=Compile(d);var i=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));Guid slot=d.Montage!.Slots[0].Id;Check(!i.ReadMontageSlot(slot).Active);Step(i,.8);
            var plan=new AnimationPoseInstruction[p.MaximumPlanInstructions];int count=i.CopyCommittedPlan(plan);var row=plan.AsSpan(0,count).ToArray().Single(r=>r.Operation==AnimationPoseOperation.Slot);Check(row.Weight==0&&!i.ReadMontageSlot(slot).Active);p.ValidateSlotPose(row,plan[row.SourceB]);Check(plan[row.SourceB].Previous==plan[row.SourceB].Current&&!plan[row.SourceB].Loop);
            Check(i.CopyCommittedMontageIntervals(new MontageInterval[528])==2);Step(i);count=i.CopyCommittedPlan(plan);Check(!plan.AsSpan(0,count).ToArray().Any(r=>r.Operation==AnimationPoseOperation.Slot));
        }));
        cases.Add(("M6.8-B2b-2 Section Notify only traversed intervals/Abort/Cancel/Jump no catch-up",()=>{
            var d=Definition();Guid clip=d.Nodes[0].ClipId;d=d with{Events=[new(Guid.NewGuid(),clip,.15,"Attack"),new(Guid.NewGuid(),clip,.6,"Recovery")]};var p=Compile(d);var i=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));Guid node=d.Nodes.Single(n=>n.Kind==AnimationNodeKind.Slot).Id,slot=d.Montage!.Slots[0].Id;
            var token=i.Prepare(i.Frame.Context,.1);i.Abort(token);Check(i.Frame.Context.Tick==0);Step(i);var events=i.ReadDebug(i.Frame.Context).Events;Check(events.Count(e=>e.NodeId==node)==1&&events.All(e=>e.Context.Tick==1));
            i.RequestMontage(new(Guid.NewGuid(),i.Frame.InstanceId,i.Frame.Context,slot,MontageRequestKind.Jump,d.Montage.Sections.Single(s=>s.Name=="Recovery").Id,10));Step(i,.1);Check(!i.ReadDebug(i.Frame.Context).Events.Any(e=>e.NodeId==node));
            i.RequestMontage(new(Guid.NewGuid(),i.Frame.InstanceId,i.Frame.Context,slot,MontageRequestKind.Cancel,Guid.Empty,10));Step(i,.1);Check(i.CopyCommittedMontageIntervals(new MontageInterval[528])==0&&!i.ReadDebug(i.Frame.Context).Events.Any(e=>e.NodeId==node));
        }));
        cases.Add(("M6.8-B2b-2 Slot closed recipe/resource binding rejects malformed unused rows",()=>{
            var p=Compile(Definition());var i=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));Step(i);var plan=new AnimationPoseInstruction[p.MaximumPlanInstructions];int n=i.CopyCommittedPlan(plan);int at=Array.FindIndex(plan,0,n,r=>r.Operation==AnimationPoseOperation.Slot);var row=plan[at];
            foreach(var bad in new[]{row with{SlotId=Guid.Empty},row with{SourceC=0},row with{SourceB=at},row with{ClipId=Guid.NewGuid()}}){var copy=plan.AsSpan(0,n).ToArray();copy[at]=bad;Reject(()=>AnimationPoseRecipe.Validate(copy,0));}
            Reject(()=>p.ValidateSlotPose(row with{SlotId=Guid.NewGuid()},plan[row.SourceB]));Reject(()=>p.ValidateSlotPose(row,plan[row.SourceB] with{Current=.9,Previous=.9}));
        }));
    }
}
