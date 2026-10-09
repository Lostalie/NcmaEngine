using System.Text.Json;
using Ncma.Animation;

internal static class MontageAuthoringTests
{
    private static void Check(bool v){if(!v)throw new Exception("Montage authoring assertion.");}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or JsonException){return;}throw new Exception("Invalid Montage authoring accepted.");}
    public static void Add(List<(string,Action)> cases)
    {
        cases.Add(("M6.8-C1 shared typed Montage operations / copied incomplete drafts / strict complete",()=>{
            var g=GraphTests.Simple();var m=MontageDataTests.Definition();m=m with{SkeletonId=g.SkeletonId,Sections=m.Sections.Select(s=>s with{ClipId=g.Nodes[0].ClipId}).ToArray()};g=MontageGraphTests.Attach(g,m);
            var slot=m.Slots[0] with{Priority=22,BlendIn=.25};var edited=AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.slot.upsert",slot}));Check(edited.Montage!.Slots[0].Priority==22&&g.Montage!.Slots[0].Priority!=22);
            var section=m.Sections[0];var incomplete=AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.section.delete",id=section.Id}),false);Reject(()=>AnimationGraphCodec.Encode(incomplete));Reject(()=>AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.section.delete",id=section.Id})));
            var restored=AnimationGraphEdits.Apply(incomplete,AnimationGraphEdits.Operations(new{op="montage.section.upsert",section}));Check(AnimationGraphCodec.Encode(restored).SequenceEqual(AnimationGraphCodec.Encode(g)));
            var empty=AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.upsert",montage=m with{Slots=[],Sections=[]}}),false);Reject(()=>AnimationGraphCodec.Encode(empty));
            var copy=AnimationGraphEdits.CopyDraft(edited);copy.Montage!.Slots[0]=slot with{Name="Owned copy"};Check(edited.Montage.Slots[0].Name!=copy.Montage.Slots[0].Name);
            Reject(()=>AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.upsert",montage=m with{SkeletonId=Guid.NewGuid()}}),false));
        }));
        cases.Add(("M6.8-C1 Montage draft scalar/identity/budget/closed negatives, no silent unlink",()=>{
            var g=GraphTests.Simple();var m=MontageDataTests.Definition();m=m with{SkeletonId=g.SkeletonId,Sections=m.Sections.Select(s=>s with{ClipId=g.Nodes[0].ClipId}).ToArray()};g=MontageGraphTests.Attach(g,m);
            foreach(var bad in new[]{m.Slots[0] with{Priority=256},m.Slots[0] with{BlendIn=double.NaN},m.Slots[0] with{Id=g.Nodes[0].Id}})Reject(()=>AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.slot.upsert",slot=bad}),false));
            foreach(var bad in new[]{m.Sections[0] with{Start=-1},m.Sections[0] with{End=m.Sections[0].Start},m.Sections[0] with{ClipId=Guid.Empty},m.Sections[0] with{SlotId=Guid.Empty}})Reject(()=>AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.section.upsert",section=bad}),false));
            Reject(()=>AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.slot.upsert",slot=m.Slots[0],seek=true}),false));
            using var duplicate=JsonDocument.Parse("[{\"op\":\"montage.delete\",\"id\":\""+m.AssetId+"\",\"id\":\""+m.AssetId+"\"}]");Reject(()=>AnimationGraphEdits.Apply(g,duplicate.RootElement,false));
            var deleted=AnimationGraphEdits.Apply(g,AnimationGraphEdits.Operations(new{op="montage.delete",id=m.AssetId}),false);Check(deleted.Nodes.Length==g.Nodes.Length&&deleted.Links.Length==g.Links.Length);Reject(()=>AnimationGraphCodec.Encode(deleted));
            var clips=AnimationGraphValidation.ClipIds(g).Select(id=>new AnimationClipDescriptor(id,g.SkeletonId,1,.2)).ToArray();Reject(()=>AnimationProgram.Compile(g,1,clips));
        }));
    }
}
