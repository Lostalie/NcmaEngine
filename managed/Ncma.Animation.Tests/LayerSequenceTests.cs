using Ncma.Animation;
internal static class LayerSequenceTests
{
    private static void Check(bool v){if(!v)throw new Exception("M6.7-C cache sequence assertion.");}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or System.Text.Json.JsonException){return;}throw new Exception("Invalid layer edit/check accepted.");}
    public static void Add(List<(string,Action)> tests)
    {
        tests.Add(("M6.7-C shared bone edits own arrays, closed shapes, incomplete draft and complete rejection",()=>{
            var d=LayerGraphTests.Graph();var n=d.Nodes.Single(n=>n.Layer is not null);var changed=AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="layer.bone.upsert",nodeId=n.Id,bone=new AnimationBoneWeight("root/arm",.25f)}));
            Check(changed.Nodes.Single(v=>v.Id==n.Id).Layer!.Mask.Bones.Single().Weight==.25f&&n.Layer!.Mask.Bones.Single().Weight==1);
            var empty=AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="layer.bone.delete",nodeId=n.Id,bonePath="root/arm"}),false);Check(empty.Nodes.Single(v=>v.Id==n.Id).Layer!.Mask.Bones.Length==0);Reject(()=>AnimationGraphCodec.Encode(empty));
            Reject(()=>AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="layer.bone.upsert",nodeId=n.Id,bone=new AnimationBoneWeight("root/arm",float.NaN)})));Reject(()=>AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="layer.bone.delete",nodeId=n.Id,bonePath="other"})));
            Reject(()=>AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="layer.bone.upsert",nodeId=n.Id,bone=new AnimationBoneWeight("root/arm",0),code="execute"})));
        }));
        tests.Add(("M6.7-C independent committed cache diagnostics and closed integral assertions",()=>{
            var d=LayerGraphTests.Graph();var p=LayerGraphTests.Compile(d);var result=AnimationGraphSequence.Run(p,new(.1,3,[],[new(1,AnimationSequenceAssertionKind.CacheHits,Guid.Empty,1),new(2,AnimationSequenceAssertionKind.CacheRequests,Guid.Empty,2)]));
            Check(result.Passed&&result.Timeline.All(t=>t.Cache.Tick==t.Frame.Context.Tick&&t.Cache.Hits==1&&t.Cache.Requests==2)&&!result.ResourcesPrepared);
            Check(!AnimationGraphSequence.Run(p,new(.1,1,[],[new(1,AnimationSequenceAssertionKind.CacheHits,Guid.Empty,2)])).Passed);
            Reject(()=>AnimationGraphSequence.Run(p,new(.1,1,[],[new(1,AnimationSequenceAssertionKind.CacheHits,Guid.NewGuid(),1)])));Reject(()=>AnimationGraphSequence.Run(p,new(.1,1,[],[new(1,AnimationSequenceAssertionKind.CacheRequests,Guid.Empty,1.5)])));
        }));
    }
}
