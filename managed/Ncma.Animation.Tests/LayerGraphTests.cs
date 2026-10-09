using System.Text;
using Ncma.Animation;

internal static class LayerGraphTests
{
    private static void Check(bool v,string reason=""){if(!v)throw new Exception("M6.7-B graph: "+reason);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or System.Text.Json.JsonException){return;}throw new Exception("Invalid layer graph accepted.");}
    internal static AnimationGraphDefinition Graph(bool additive=true,bool cached=true)
    {
        var d=GraphTests.Simple();var basis=d.Nodes[0];var overlay=basis with{Id=Guid.NewGuid(),ClipId=Guid.NewGuid(),Name="Upper"};
        var mask=new AnimationBoneMask(Guid.NewGuid(),d.SkeletonId,new('A',64),[new("root/arm",1)]);
        var layer=AnimationGraphNode.Create(Guid.NewGuid(),"Layer",additive?AnimationNodeKind.LayerAdditive:AnimationNodeKind.LayerOverride) with{Weight=.5,Layer=new(mask,additive?basis.ClipId:Guid.Empty,0)};
        if(!cached)return d with{Nodes=[basis,overlay,layer,d.Nodes[1]],Links=[new(Guid.NewGuid(),basis.Id,"pose",layer.Id,"a"),new(Guid.NewGuid(),overlay.Id,"pose",layer.Id,"b"),new(Guid.NewGuid(),layer.Id,"pose",d.Nodes[1].Id,"pose")]};
        var cache=AnimationGraphNode.Create(Guid.NewGuid(),"Cache base",AnimationNodeKind.CachePose);var blend=AnimationGraphNode.Create(Guid.NewGuid(),"Composite",AnimationNodeKind.Blend) with{Weight=.5};
        return d with{Nodes=[basis,overlay,cache,layer,blend,d.Nodes[1]],Links=[new(Guid.NewGuid(),basis.Id,"pose",cache.Id,"pose"),new(Guid.NewGuid(),cache.Id,"pose",layer.Id,"a"),new(Guid.NewGuid(),overlay.Id,"pose",layer.Id,"b"),new(Guid.NewGuid(),cache.Id,"pose",blend.Id,"a"),new(Guid.NewGuid(),layer.Id,"pose",blend.Id,"b"),new(Guid.NewGuid(),blend.Id,"pose",d.Nodes[1].Id,"pose")]};
    }
    internal static AnimationProgram Compile(AnimationGraphDefinition d,AnimationSkeletonDescriptor? layout=null)=>AnimationProgram.Compile(d,1,AnimationGraphValidation.ClipIds(d).Select(id=>new AnimationClipDescriptor(id,d.SkeletonId,1,1)).ToArray(),skeleton:layout??new(d.SkeletonId,new('A',64),[new("root",-1),new("root/arm",0)]));
    private static void Step(AnimationGraphInstance instance){var t=instance.Prepare(instance.Frame.Context,.1);instance.Commit(t,instance.Frame.Context with{Tick=instance.Frame.Context.Tick+1});}
    public static void Add(List<(string,Action)> cases)
    {
        cases.Add(("M6.7-B strict v5 layers/cache/reference and removed formats/neutral closed fields",()=>{
            foreach(bool additive in new[]{false,true}){var d=Graph(additive);var bytes=AnimationGraphCodec.Encode(d);Check(AnimationGraphCodec.Decode(bytes).Version==AnimationGraphCodec.CurrentVersion&&AnimationGraphValidation.ClipIds(d).Length==2);string json=Encoding.UTF8.GetString(bytes);
                foreach(int old in new[]{1,2,3,4})Reject(()=>AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(json.Replace("\"version\":5","\"version\":"+old))));Reject(()=>AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(json.Replace(",\"layer\":null",""))));
                var n=d.Nodes.Single(v=>v.Layer is not null);Reject(()=>AnimationGraphCodec.Encode(d with{Nodes=d.Nodes.Select(v=>v.Id==n.Id?v with{Layer=v.Layer! with{ReferenceClip=additive?Guid.Empty:d.Nodes[0].ClipId}}:v).ToArray()}));
                var owned=new AnimationGraphDocument(d);d.Nodes.Single(v=>v.Layer is not null).Layer!.Mask.Bones[0]=new("root/arm",0);Check(owned.CopyBytes().SequenceEqual(bytes));
            }
            var cycle=Graph();var cache=cycle.Nodes.Single(n=>n.Kind==AnimationNodeKind.CachePose);Reject(()=>AnimationGraphCodec.Encode(cycle with{Links=cycle.Links.Select(l=>l.To==cache.Id?l with{From=cycle.Nodes.Single(n=>n.Layer is not null).Id}:l).ToArray()}));
        }));
        cases.Add(("M6.7-B exact skeleton layout/hash/reference actual duration required at preparation",()=>{
            var d=Graph();var clips=AnimationGraphValidation.ClipIds(d).Select(id=>new AnimationClipDescriptor(id,d.SkeletonId,1,1)).ToArray();Reject(()=>AnimationProgram.Compile(d,1,clips));Reject(()=>Compile(d,new(d.SkeletonId,new('B',64),[new("root",-1),new("root/arm",0)])));
            var n=d.Nodes.Single(v=>v.Layer is not null);Reject(()=>Compile(d with{Nodes=d.Nodes.Select(v=>v.Id==n.Id?v with{Layer=v.Layer! with{ReferenceTime=1.1}}:v).ToArray()}));
            var program=Compile(d);var copy=program.CopyLayers();copy[0].Weights[1]=0;copy[0].Definition.Mask.Bones[0]=new("root/arm",0);Check(program.CopyLayers()[0].Weights[1]==1&&program.CopyLayers()[0].Definition.Mask.Bones[0].Weight==1);
        }));
        cases.Add(("M6.7-B cache shares one source per candidate, Commit/Abort/instances and warm allocation0",()=>{
            var d=Graph();var p=Compile(d);var cache=p.CopyCacheLifetimes().Single();Check(cache.ConsumerCount==2&&cache.LastConsumer>cache.PreparedOrder);
            var i=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));Step(i);var plan=new AnimationPoseInstruction[p.MaximumPlanInstructions];int count=i.CopyCommittedPlan(plan);AnimationPoseRecipe.Validate(plan.AsSpan(0,count),i.Frame.Output);
            Check(count==5&&plan.Take(count).Count(r=>r.Operation==AnimationPoseOperation.Clip)==3,"Base/overlay once plus static reference");Check(i.CacheStatistics==new AnimationCacheStatistics(1,2,1));
            var token=i.Prepare(i.Frame.Context,.1);Reject(()=>{_=i.CacheStatistics;});i.Abort(token);Check(i.CacheStatistics.Tick==1);Step(i);Check(i.CacheStatistics.Tick==2);
            var other=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));Check(other.CacheStatistics.Tick==0&&other.Frame.InstanceId!=i.Frame.InstanceId);Step(other);Check(other.CacheStatistics.Tick==1);
            for(int n=0;n<256;n++)Step(i);long before=GC.GetAllocatedBytesForCurrentThread();for(int n=0;n<1024;n++)Step(i);Check(GC.GetAllocatedBytesForCurrentThread()==before);
        }));
        cases.Add(("M6.7-B recipe rejects malformed unused layer/reference and compact scratch reuses lifetimes",()=>{
            var p=Compile(Graph());var instance=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));Step(instance);var rows=new AnimationPoseInstruction[p.MaximumPlanInstructions];int count=instance.CopyCommittedPlan(rows);var plan=rows.Take(count).ToArray();var layout=new AnimationPoseScratchLayout(p.MaximumPlanInstructions);layout.Prepare(plan,instance.Frame.Output);Check(layout.Slots<count);
            long before=GC.GetAllocatedBytesForCurrentThread();for(int n=0;n<1024;n++)layout.Prepare(plan,instance.Frame.Output);Check(GC.GetAllocatedBytesForCurrentThread()==before);
            int at=Array.FindIndex(plan,r=>r.Operation==AnimationPoseOperation.LayerAdditive);var invalid=(AnimationPoseInstruction[])plan.Clone();invalid[at]=invalid[at] with{SourceC=at};Reject(()=>AnimationPoseRecipe.Validate(invalid,0));invalid[at]=plan[at] with{SourceC=plan[at].SourceB};Reject(()=>AnimationPoseRecipe.Validate(invalid,0));
            invalid[at]=plan[at] with{Operation=AnimationPoseOperation.LayerOverride};Reject(()=>AnimationPoseRecipe.Validate(invalid,0));Reject(()=>layout.Prepare(plan,-1));
            int output=instance.Frame.Output;Check(Task.Run(()=>{try{layout.Prepare(plan,output);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());
        }));
    }
}
