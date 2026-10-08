using System.Text.Json;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class GraphSequenceTests
{
    private static void Check(bool v,string message=""){if(!v)throw new Exception("M6.5-C sequence/editor assertion: "+message);}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or JsonException or IOException){return;}throw new Exception("Invalid sequence authority accepted.");}
    private static AnimationSequenceCase Case()=>new(.1,3,[],[new(1,AnimationSequenceAssertionKind.EventCount,Guid.Empty,0)]);
    internal static IEnumerable<(string,Action)> Cases(string output,string repository)
    {
        yield return("M6.5-D trusted local typed cases obey the same 48KiB wire budget",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var w=f.Writer!;var a=new AnimationParameter(Guid.NewGuid(),"X",AnimationParameterKind.Float,0,0,false);var b=a with{Id=Guid.NewGuid(),Name="Y"};
            w.ApproveFileWrite(w.Stamp,w.Path!,f.Graph,true);w.Begin(w.Stamp);w.ApplyDraft(AnimationGraphEdits.Operations(new{op="parameter.upsert",parameter=a},new{op="parameter.upsert",parameter=b}));Guid p=w.PrepareLocal();var review=w.CaptureReview(p);w.Approve(review,review.Fingerprint,review.Graph,true);w.CommitLocal(p);w.Sequences.PrepareTrusted();
            var test=new AnimationSequenceCase(.1,256,Enumerable.Range(1,256).SelectMany(i=>new[]{new AnimationSequenceWrite(i,a.Id,AnimationParameterKind.Float,1000000),new(i,b.Id,AnimationParameterKind.Float,1000000)}).ToArray(),Enumerable.Range(1,64).Select(i=>new AnimationSequenceAssertion(i,AnimationSequenceAssertionKind.Parameter,a.Id,1000000)).ToArray());
            Check(System.Text.Encoding.UTF8.GetByteCount(AnimationSequenceCodec.Encode(test).GetRawText())>48*1024);Reject(()=>w.Sequences.ProposeLocal(test));Check(w.Sequences.Pending.Length==0);
        });
        yield return("M6.5-D queued real stdio sequence rechecks revoked authority before execution",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var s=f.Writer!.Sequences;s.PrepareTrusted();f.ApproveUi();Guid id=Guid.NewGuid();
            Check(!f.Call(AnimationSequenceSchemas.Propose,new{graphId=f.Graph,caseId=id,test=AnimationSequenceCodec.Encode(Case())},Guid.NewGuid()).GetProperty("isError").GetBoolean());var review=s.CaptureReview(id);s.Approve(review,review.Fingerprint,true);
            var request=new{jsonrpc="2.0",id=987654,method="tools/call",@params=new{name=AnimationSequenceSchemas.Run,arguments=new{contractVersion=2,requestId=Guid.NewGuid(),sessionId=f.Owner.Edit!.SessionId,expectedRevision=f.Owner.Edit.Revision,input=new{graphId=f.Graph,caseId=id,section="timeline",offset=0,limit=8}}}};
            f.Client.StandardInput.WriteLine(JsonSerializer.Serialize(request,Ncma.Editor.Protocol.Wire.Json));f.Client.StandardInput.Flush();var pending=f.Client.StandardOutput.ReadLineAsync();var watch=System.Diagnostics.Stopwatch.StartNew();
            while(f.Owner.Endpoint!.View.QueueCount==0){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Sequence request was not queued.");Thread.Sleep(1);}
            Check(!pending.IsCompleted,"No analysis on IO thread");byte[] before=f.Owner.Document.CaptureBytes();s.Revoke();f.Owner.Endpoint.Pump();Check(pending.Wait(10000));
            using var response=JsonDocument.Parse(pending.Result!);Check(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Document.World.Tick==0);
            Reject(()=>s.RunLocal(id));
        });
        yield return("M6.5-D sequence root opt-out/assertion outcomes and retired-ID bound",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var s=f.Writer!.Sequences;
            Reject(()=>s.PrepareTrusted(true,int.MaxValue));s.PrepareTrusted(false);Reject(()=>s.ProposeLocal(new(.1,1,[],[new(1,AnimationSequenceAssertionKind.RootX,Guid.Empty,0)])));
            Guid id=s.ProposeLocal(Case());var noRoot=s.RunLocal(id);Check(!noRoot.RootMotionSupported&&noRoot.Timeline.All(t=>t.Root is null));s.Cancel(id);s.PrepareTrusted();
            id=s.ProposeLocal(new(.1,1,[],[new(1,AnimationSequenceAssertionKind.RootX,Guid.Empty,999)]));Check(!s.RunLocal(id).Passed&&s.RunLocal(id).Checks.Single().Code=="assertion_failed");s.Cancel(id);
            for(int i=2;i<256;i++){id=s.ProposeLocal(Case());s.Cancel(id);}Reject(()=>s.ProposeLocal(Case()));Check(s.Pending.Length==0&&f.Owner.Document.World.Tick==0);
        });
        yield return("M6.5-C stamped event-track draft drag/cancel/save and independent sequence UI",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var w=f.Writer!;
            f.ApproveUi();f.Click(40,8);f.Build();f.Click(24,12);f.Build();f.Click(24,48);f.Build();Check(f.View.GraphWorkspaceActive);
            void Click(ulong high,ulong low,double value=0){f.Click(high,low,value);f.Build();}
            void Text(ulong high,ulong low,string text,uint phase=3){byte[] bytes=System.Text.Encoding.UTF8.GetBytes(text);var e=f.Event(high,low);e.Phase=phase;e.TextLength=(uint)bytes.Length;f.View.Apply([e],bytes);f.Build();}
            byte[] before=File.ReadAllBytes(f.File),scene=f.Owner.Document.CaptureBytes();
            Click(50,6,1);Click(50,7);Click(50,8);Click(50,34);Click(50,44);
            var marker=w.Capture()!.Events.Single();Text(54,2,"0.1 0.5 0",1);Text(54,2,"0.2 0.5 0",2);Text(54,2,"0.3 0.5 0",3);
            Check(w.Capture()!.Events.Single().Time==.3&&before.SequenceEqual(File.ReadAllBytes(f.File))&&f.Owner.Edit!.State.UndoCount==0);
            Click(50,9);Check(w.Capture()!.Events.Length==0&&before.SequenceEqual(File.ReadAllBytes(f.File)));
            Click(50,8);Click(50,44);Click(50,602,.1);Click(50,10);Click(50,52,1);Click(50,53);Click(50,54);
            Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Events.Single().Time==.1&&f.Owner.Edit!.State.UndoCount==1&&scene.SequenceEqual(f.Owner.Document.CaptureBytes()));
            Click(50,60);Click(55,2);Text(55,5,AnimationSequenceCodec.Encode(new(.1,3,[],[new(1,AnimationSequenceAssertionKind.EventCount,Guid.Empty,0)])).GetRawText());Click(55,6);Click(55,7);Check(System.Text.Encoding.UTF8.GetString(f.View.Text).Contains("passed=False")&&f.Owner.Document.World.Tick==0);Click(55,19);Check(System.Text.Encoding.UTF8.GetString(f.View.Text).Contains("marker="));Click(55,20);Check(System.Text.Encoding.UTF8.GetString(f.View.Text).Contains("assertion_failed"));
            Click(55,16);Click(55,17);
            Check(!f.View.Items.ToArray().Any(i=>i.WidgetHigh==55));
        });
        yield return("M6.5-C complete paged exact-case GUI approval cannot skip unseen review rows",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var s=f.Writer!.Sequences;f.ApproveUi();
            Guid id=Guid.NewGuid();var test=new AnimationSequenceCase(.1,64,[],Enumerable.Range(1,64).Select(i=>new AnimationSequenceAssertion(i,AnimationSequenceAssertionKind.EventCount,Guid.Empty,0)).ToArray());
            f.Click(40,8);f.Build();f.Click(24,12);f.Build();f.Click(24,48);f.Build();f.Click(50,60);f.Build();
            s.PrepareTrusted();Check(!f.Call(AnimationSequenceSchemas.Propose,new{graphId=f.Graph,caseId=id,test=AnimationSequenceCodec.Encode(test)},Guid.NewGuid()).GetProperty("isError").GetBoolean());f.Build();
            var choice=f.View.Items.ToArray().Single(i=>i.WidgetHigh==55&&i.WidgetLow>=1000&&i.Kind==(uint)GuiItemKind.Button);f.Click(55,choice.WidgetLow);f.Build();f.Click(55,8);f.Build();
            object run=new{graphId=f.Graph,caseId=id,section="summary",offset=0,limit=1};
            Check(f.View.Items.ToArray().Single(i=>i.WidgetHigh==55&&i.WidgetLow==12).Enabled==0,"Initial full review must be disabled.");
            f.Click(55,12,1);f.Build();f.Click(55,13);f.Build();Check(f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("isError").GetBoolean());
            int pages=1;while(f.View.Items.ToArray().Single(i=>i.WidgetHigh==55&&i.WidgetLow==11).Enabled==1){f.Click(55,11);f.Build();Check(++pages<10);}
            Check(pages>1);f.Click(55,12,1);f.Build();f.Click(55,13);f.Build();Check(!f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("isError").GetBoolean());
            f.Click(55,16);f.Build();Check(f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("isError").GetBoolean());
        });
        yield return("M6.5-C exact NCA independent local sequence, copied cases and no World/history/native steps",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var s=f.Writer!.Sequences;Reject(()=>s.ProposeLocal(Case()));s.PrepareTrusted();byte[] scene=f.Owner.Document.CaptureBytes();var state=f.Owner.Edit!.State;
            Guid id=s.ProposeLocal(Case());var copy=s.CaseCopy(id);copy.Assertions[0]=copy.Assertions[0] with{Value=1};var result=s.RunLocal(id);Check(result.Passed&&result.Timeline.Count==3&&result.RootMotionSupported&&!result.ResourcesPrepared);
            Check(result.Timeline.All(t=>t.Root is not null)&&result.Timeline[0].Frame.Context.WorldId!=f.Owner.Document.World.Identity&&result.Timeline[^1].Frame.Context.Tick==3);
            Check(scene.SequenceEqual(f.Owner.Document.CaptureBytes())&&state==f.Owner.Edit.State);s.Cancel(id);Reject(()=>s.RunLocal(id));
            for(int i=0;i<4;i++)s.ProposeLocal(Case());Reject(()=>s.ProposeLocal(Case()));s.Revoke();Reject(()=>s.CaptureReview(s.Pending[0]));
        });
        yield return("M6.5-C real stdio sequence default-denied/exact human approval/paged repeat/no file IO",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var s=f.Writer!.Sequences;s.PrepareTrusted();Guid id=Guid.NewGuid();object propose=new{graphId=f.Graph,caseId=id,test=AnimationSequenceCodec.Encode(Case())};
            Check(f.Call(AnimationSequenceSchemas.Propose,propose,Guid.NewGuid()).GetProperty("isError").GetBoolean());f.ApproveUi();
            var p=f.Call(AnimationSequenceSchemas.Propose,propose,Guid.NewGuid());Check(!p.GetProperty("isError").GetBoolean(),p.GetRawText());
            object run=new{graphId=f.Graph,caseId=id,section="timeline",offset=0,limit=8};Check(f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("isError").GetBoolean());
            var review=s.CaptureReview(id);Reject(()=>s.Approve(review,review.Fingerprint,false));Reject(()=>s.Approve(review with{Resources=[]},review.Fingerprint,true));s.Approve(review,review.Fingerprint,true);
            byte[] before=f.Owner.Document.CaptureBytes();var history=f.Owner.Edit!.State;
            using(var locked=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.None)) {
                var r=f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid());Check(!r.GetProperty("isError").GetBoolean(),r.GetRawText());var data=r.GetProperty("structuredContent").GetProperty("data");
                Check(data.GetProperty("resourcesPrepared").GetBoolean()&&data.GetProperty("rootMotionSupported").GetBoolean()&&!data.GetProperty("livePlay").GetBoolean()&&!data.GetProperty("collisionExecuted").GetBoolean()&&data.GetProperty("items").GetArrayLength()==3);
                Check(data.GetProperty("items")[0].GetProperty("root").GetProperty("translation").TryGetProperty("x",out _),data.GetRawText());
                var repeated=f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("structuredContent").GetProperty("data");Check(data.GetRawText()==repeated.GetRawText());
            }
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())&&history==f.Owner.Edit.State);
            s.Revoke();Check(f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("isError").GetBoolean());
        });
        yield return("M6.5-C sequence TTL/re-pair/edit/source/cancel/over-budget and closed schema negatives",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var s=f.Writer!.Sequences;s.PrepareTrusted();f.ApproveUi();Guid id=Guid.NewGuid();
            object propose=new{graphId=f.Graph,caseId=id,test=AnimationSequenceCodec.Encode(Case())};f.Call(AnimationSequenceSchemas.Propose,propose,Guid.NewGuid());var review=s.CaptureReview(id);s.Approve(review,review.Fingerprint,true);
            object run=new{graphId=f.Graph,caseId=id,section="summary",offset=0,limit=1};f.Time.Now=60000;Check(f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("isError").GetBoolean());f.Time.Now=0;
            var endpoint=f.Owner.Endpoint!;var connection=endpoint.View.Connections.Single().ConnectionId;endpoint.Revoke(connection);endpoint.Pair(connection,true);Check(f.Call(AnimationSequenceSchemas.Run,run,Guid.NewGuid()).GetProperty("isError").GetBoolean());Reject(()=>s.Approve(review,review.Fingerprint,true));
            f.ApproveUi();s.Approve(s.CaptureReview(id),s.CaptureReview(id).Fingerprint,true);s.Cancel(id);Check(f.Call(AnimationSequenceSchemas.Propose,propose,Guid.NewGuid()).GetProperty("isError").GetBoolean());
            foreach(var bad in new object[]{new{graphId=f.Graph,caseId=Guid.NewGuid(),test=AnimationSequenceCodec.Encode(Case()),path="assets/escape"},new{graphId=f.Graph,caseId=Guid.NewGuid(),test=AnimationSequenceCodec.Encode(Case() with{Steps=257})},new{graphId=f.Graph,caseId=Guid.NewGuid(),test=AnimationSequenceCodec.Encode(Case() with{Writes=[new(1,Guid.NewGuid(),AnimationParameterKind.Bool,1)]})}})Check(f.Call(AnimationSequenceSchemas.Propose,bad,Guid.NewGuid()).GetProperty("isError").GetBoolean());
            id=s.ProposeLocal(Case());f.Workspace.CreateObject(f.Workspace.Stamp);Reject(()=>s.RunLocal(id));Reject(()=>s.CaptureReview(id));
        });
        yield return("M6.5-C persistent event edits use exact reviewed resource duration and single history",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var w=f.Writer!;byte[] original=File.ReadAllBytes(f.File);var marker=new AnimationEventMarker(Guid.NewGuid(),f.Clip,.1,"footstep");
            w.ApproveFileWrite(w.Stamp,w.Path!,f.Graph,true);w.Begin(w.Stamp);w.ApplyDraft(AnimationGraphEdits.Operations(new{op="event.upsert",marker}));Guid id=w.PrepareLocal();var review=w.CaptureReview(id);w.Approve(review,review.Fingerprint,review.Graph,true);w.CommitLocal(id);
            Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Events.Single()==marker&&f.Owner.Edit!.State.UndoCount==1);
            w.Sequences.PrepareTrusted();Guid sequence=w.Sequences.ProposeLocal(new(.1,1,[],[new(1,AnimationSequenceAssertionKind.EventCount,marker.Id,1)]));Check(w.Sequences.RunLocal(sequence).Passed);
            f.Workspace.History(f.Workspace.Stamp,false);w.Synchronize();Check(original.SequenceEqual(File.ReadAllBytes(f.File)));Reject(()=>w.Sequences.RunLocal(sequence));f.Workspace.History(f.Workspace.Stamp,true);w.Synchronize();Check(w.Capture()!.Events.Single()==marker);
            w.Begin(w.Stamp);w.ApplyDraft(AnimationGraphEdits.Operations(new{op="event.upsert",marker=marker with{Time=600}}));id=w.PrepareLocal();Reject(()=>w.CaptureReview(id));w.Cancel();
            string old=System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(f.File)).Replace("\"version\":3","\"version\":2");File.WriteAllText(f.File,old);byte[] rejected=File.ReadAllBytes(f.File);Reject(()=>w.Open("assets/test.ncmaanim"));Check(rejected.SequenceEqual(File.ReadAllBytes(f.File)));
        });
    }
}
