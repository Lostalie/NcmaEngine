using System.Text;
using System.Text.Json;
using Ncma.Animation;

internal static class GraphEventFormatTests
{
    private static void Check(bool v){if(!v)throw new Exception("M6.5-C format assertion.");}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or JsonException){return;}throw new Exception("Invalid event format accepted.");}
    public static void Add(List<(string,Action)> cases)
    {
        cases.Add(("M6.5-C strict v2 requires event/policy fields and removes v1 without fallback",()=>{
            var d=GraphTests.Simple();string json=Encoding.UTF8.GetString(AnimationGraphCodec.Encode(d));
            foreach(string bad in new[]{json.Replace("\"version\":2","\"version\":1"),json.Replace(",\"events\":[]",""),json.Replace(",\"interruptTransitions\":false",""),json.Replace("\"events\":[]","\"events\":null"),json.Replace("\"interruptTransitions\":false","\"interruptTransitions\":0")})Reject(()=>AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(bad)));
            Reject(()=>AnimationGraphCodec.Encode(d with{Version=1}));Check(AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(json)).Version==2);
        }));
        cases.Add(("M6.5-C canonical owned persistent markers and actual-duration compile",()=>{
            var d=GraphTests.Simple();var a=new AnimationEventMarker(Guid.NewGuid(),d.Nodes[0].ClipId,.1,"footstep");var b=a with{Id=Guid.NewGuid(),Time=.2};d=d with{Events=[a,b]};
            var bytes=AnimationGraphCodec.Encode(d);Check(bytes.SequenceEqual(AnimationGraphCodec.Encode(d with{Events=[b,a]})));var doc=new AnimationGraphDocument(d);d.Events[0]=a with{Name="changed"};Check(doc.CopyBytes().SequenceEqual(bytes));
            var copy=doc.CopyDefinition();Guid clip=copy.Nodes.Single(n=>n.Kind==AnimationNodeKind.Clip).ClipId;var p=AnimationProgram.Compile(copy,1,[new(clip,copy.SkeletonId,1,.2)]);var i=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));var token=i.Prepare(i.Frame.Context,.2);i.Commit(token,i.Frame.Context with{Tick=1});Check(i.ReadDebug(i.Frame.Context).Events.Count==2);
            Reject(()=>AnimationProgram.Compile(copy,1,[new(clip,copy.SkeletonId,1,.15)]));Reject(()=>AnimationProgram.Compile(copy,1,[new(clip,copy.SkeletonId,1,.2)],[a]));
        }));
        cases.Add(("M6.5-C event/policy semantic edits are owned closed drafts, not execution",()=>{
            var d=GraphTests.Machine();var marker=new AnimationEventMarker(Guid.NewGuid(),d.Nodes[0].ClipId,.1,"event");var edit=AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="event.upsert",marker},new{op="graph.interruptions",enabled=true}));
            Check(edit.Events.Length==1&&edit.InterruptTransitions&&d.Events.Length==0&&!d.InterruptTransitions);var copy=AnimationGraphEdits.CopyDraft(edit);copy.Events[0]=marker with{Name="changed"};Check(edit.Events[0].Name=="event");
            var removed=AnimationGraphEdits.Apply(edit,AnimationGraphEdits.Operations(new{op="event.delete",id=marker.Id}));Check(removed.Events.Length==0);
            foreach(var bad in new[]{marker with{Id=d.AssetId},marker with{ClipId=Guid.NewGuid()},marker with{Time=0},marker with{Time=double.NaN},marker with{Name="script\n"}})Reject(()=>AnimationGraphCodec.Encode(d with{Events=[bad]}));
            Reject(()=>AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="graph.interruptions",enabled=1})));Reject(()=>AnimationGraphCodec.Encode(GraphTests.Simple() with{InterruptTransitions=true}));
        }));
        cases.Add(("M6.5-C closed sequence codec owns exact camelCase typed data",()=>{
            var d=GraphTests.Machine();var input=new AnimationSequenceCase(.1,3,[new(1,d.Parameters[0].Id,AnimationParameterKind.Bool,1)],[new(1,AnimationSequenceAssertionKind.State,d.States[1].Id,0)]);
            var json=AnimationSequenceCodec.Encode(input);var decoded=AnimationSequenceCodec.Decode(json);Check(decoded.Writes[0].Kind==AnimationParameterKind.Bool&&decoded.Assertions[0].Kind==AnimationSequenceAssertionKind.State);
            foreach(string bad in new[]{json.GetRawText().Replace("\"bool\"","1"),json.GetRawText().Replace("\"bool\"","\"Bool\""),json.GetRawText().Replace("\"fixedDelta\":0.1","\"fixedDelta\":0.1,\"fixedDelta\":0.2"),json.GetRawText().Insert(1,"\"code\":\"run\",")}){using var doc=JsonDocument.Parse(bad);Reject(()=>AnimationSequenceCodec.Decode(doc.RootElement));}
        }));
    }
}
