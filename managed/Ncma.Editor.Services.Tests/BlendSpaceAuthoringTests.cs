using System.Text;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets.Authoring;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Animation.Native;
using System.Security.Cryptography;

internal static unsafe class BlendSpaceAuthoringTests
{
    private static void Check(bool v,string reason=""){if(!v)throw new Exception("M6.6-C authoring: "+reason);}
    private static void Reject(Action f){try{f();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or JsonException or IOException){return;}throw new Exception("Invalid space authoring accepted.");}
    private static AnimationGraphDefinition Draft(GraphInspectionTests.Fixture f,bool two=true)
    {
        var d=f.Writer!.Capture()!;var x=new AnimationParameter(Guid.NewGuid(),"Speed",AnimationParameterKind.Float,.2,0,false);var y=x with{Id=Guid.NewGuid(),Name="Turn",FloatDefault=.3};
        var s=new BlendSpaceDefinition(Guid.NewGuid(),two?2:1,new(x.Id,"Speed","m/s",0,1),two?new(y.Id,"Turn","degree/s",0,1):null,1,Guid.Empty,two?[new(Guid.NewGuid(),f.Clip,0,0),new(Guid.NewGuid(),f.Clip,1,0),new(Guid.NewGuid(),f.Clip,0,1)]:[new(Guid.NewGuid(),f.Clip,0,0),new(Guid.NewGuid(),f.Clip,1,0)]);
        return d with{Parameters=two?[x,y]:[x],Nodes=d.Nodes.Select(n=>n.Kind==AnimationNodeKind.Clip?n with{Kind=AnimationNodeKind.BlendSpace,ClipId=Guid.Empty,BlendSpace=s,X=16,Y=32}:n with{X=320,Y=32}).ToArray()};
    }
    private static void Save(GraphInspectionTests.Fixture f,AnimationGraphDefinition d)
    {
        var w=f.Writer!;w.ApproveFileWrite(w.Stamp,w.Path!,f.Graph,true);w.Begin(w.Stamp);w.ApplyDraft(AnimationGraphEdits.Operations(d.Parameters.Select(p=>(object)new{op="parameter.upsert",parameter=p}).Concat(d.Nodes.Select(n=>(object)new{op="node.upsert",node=n})).ToArray()));
        Guid proposal=w.PrepareLocal();var review=w.CaptureReview(proposal);w.Approve(review,review.Fingerprint,review.Graph,true);w.CommitLocal(proposal);f.Build();
    }
    internal static IEnumerable<(string,Action)> Cases(string output,string repository,string plugins)
    {
        yield return("M6.6-C shared sample operations/strict schema/axis metadata/complete NCA review/history",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);var d=Draft(f);var n=d.Nodes.Single(v=>v.BlendSpace is not null);var sample=n.BlendSpace!.Samples[0];var moved=AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="blendspace.sample.upsert",nodeId=n.Id,sample=sample with{X=.1,Y=.1}}));Check(moved.Nodes.Single(v=>v.Id==n.Id).BlendSpace!.Samples.Single(p=>p.Id==sample.Id).X==.1);
            var deleted=AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="blendspace.sample.delete",nodeId=n.Id,sampleId=sample.Id}),false);Reject(()=>AnimationGraphCodec.Encode(deleted));Check(d.Nodes.Single(v=>v.Id==n.Id).BlendSpace!.Samples.Length==3);
            Reject(()=>AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="blendspace.sample.upsert",nodeId=n.Id,sample,path="escape"})));Reject(()=>AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="blendspace.sample.delete",nodeId=Guid.NewGuid(),sampleId=sample.Id})));
            var schema=AnimationGraphAuthoringSchemas.Descriptor.InputSchema.GetRawText();Check(schema.Contains("blendspace.sample.upsert")&&schema.Contains("additionalProperties"));
            byte[] scene=f.Owner.Document.CaptureBytes();Save(f,d);Check(f.Owner.Edit!.State.UndoCount==1&&scene.SequenceEqual(f.Owner.Document.CaptureBytes()));Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Nodes.Single(v=>v.Id==n.Id).BlendSpace!.AxisX.Unit=="m/s");
            f.Workspace.History(f.Workspace.Stamp,false);f.Writer!.Synchronize();Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Nodes.All(v=>v.BlendSpace is null));f.Workspace.History(f.Workspace.Stamp,true);f.Writer.Synchronize();Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Nodes.Any(v=>v.BlendSpace is not null));
            var w=f.Writer;w.ApproveFileWrite(w.Stamp,w.Path!,f.Graph,true);w.Begin(w.Stamp);w.ApplyDraft(AnimationGraphEdits.Operations(new{op="blendspace.sample.upsert",nodeId=n.Id,sample=sample with{ClipId=Guid.NewGuid()}}));Guid missing=w.PrepareLocal();Reject(()=>w.CaptureReview(missing));w.Cancel();
        });
        yield return("M6.6-C actual NCA real stdio coordinate weights/default denied/exact approval/revoke/TTL",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);var d=Draft(f);Save(f,d);f.ApproveUi();var s=f.Writer!.Sequences;s.PrepareTrusted(false);
            var test=new AnimationSequenceCase(.1,3,[new(1,d.Parameters[0].Id,AnimationParameterKind.Float,.2),new(1,d.Parameters[1].Id,AnimationParameterKind.Float,.3),new(2,d.Parameters[0].Id,AnimationParameterKind.Float,.8),new(2,d.Parameters[1].Id,AnimationParameterKind.Float,.8)],[]);Guid id=Guid.NewGuid(),request=Guid.NewGuid();object run=new{graphId=f.Graph,caseId=id,section="weights",offset=0,limit=8};
            Check(f.Call(AnimationSequenceSchemas.Run,run,request).GetProperty("isError").GetBoolean());Check(!f.Call(AnimationSequenceSchemas.Propose,new{graphId=f.Graph,caseId=id,test=AnimationSequenceCodec.Encode(test)},Guid.NewGuid()).GetProperty("isError").GetBoolean());
            Check(f.Call(AnimationSequenceSchemas.Run,run,request).GetProperty("isError").GetBoolean());var review=s.CaptureReview(id);s.Approve(review,review.Fingerprint,true);byte[] scene=f.Owner.Document.CaptureBytes();
            var data=f.Call(AnimationSequenceSchemas.Run,run,request).GetProperty("structuredContent").GetProperty("data");Check(data.GetProperty("resourcesPrepared").GetBoolean()&&!data.GetProperty("livePlay").GetBoolean()&&!data.GetProperty("collisionExecuted").GetBoolean()&&data.GetProperty("total").GetInt32()==3);var first=data.GetProperty("items")[0].GetProperty("weights");Check(first.GetProperty("count").GetInt32()==3&&!first.GetProperty("projected").GetBoolean());Check(data.GetProperty("items")[1].GetProperty("weights").GetProperty("projected").GetBoolean());
            Check(scene.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Document.World.Tick==0&&f.Owner.Edit!.State.UndoCount==1);s.Revoke();Check(f.Call(AnimationSequenceSchemas.Run,run,request).GetProperty("isError").GetBoolean());review=s.CaptureReview(id);s.Approve(review,review.Fingerprint,true);f.Time.Now=60000;Check(f.Call(AnimationSequenceSchemas.Run,run,request).GetProperty("isError").GetBoolean());
        });
        yield return("M6.6-C real stdio point proposal is pure, exact dual approval, sole transaction/Undo",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);var d=Draft(f);Save(f,d);f.ApproveUi();var n=d.Nodes.Single(v=>v.BlendSpace is not null);var sample=n.BlendSpace!.Samples[0] with{X=.1,Y=.1};Guid id=Guid.NewGuid();byte[] before=File.ReadAllBytes(f.File);
            using(var locked=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.None))Check(!f.Call(AnimationGraphAuthoringSchemas.Propose,new{graphId=f.Graph,proposalId=id,operations=AnimationGraphEdits.Operations(new{op="blendspace.sample.upsert",nodeId=n.Id,sample})},Guid.NewGuid()).GetProperty("isError").GetBoolean());
            Guid request=Guid.NewGuid();ulong revision=f.Owner.Edit!.Revision;object tx=new{proposalId=id,expectedAssetRevision=f.Owner.Assets!.Clock.Revision};Check(f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision).GetProperty("isError").GetBoolean());
            var controller=new EditorAuthorizationController(f.Workspace);var page=controller.Capture()!;var displayed=page.Proposals.Single();controller.Approve(page,displayed.Scope.Id,displayed.Fingerprint,true,null);Check(f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision).GetProperty("isError").GetBoolean());
            var review=f.Writer!.CaptureReview(id);f.Writer.Approve(review,review.Fingerprint,review.Graph,true);Check(!f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision).GetProperty("isError").GetBoolean());Check(f.Owner.Edit.State.UndoCount==2&&AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Nodes.Single(v=>v.Id==n.Id).BlendSpace!.Samples.Single(p=>p.Id==sample.Id).X==.1);
            f.Writer.Synchronize();f.Workspace.History(f.Workspace.Stamp,false);f.Writer.Synchronize();Check(before.SequenceEqual(File.ReadAllBytes(f.File)));
        });
        yield return("M6.6-C stamped point drag/cancel and typed axis/sample properties",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);var d=Draft(f);Save(f,d);f.ApproveUi();f.Click(40,8);f.Build();f.Click(24,12);f.Build();f.Click(24,48);f.Build();
            void Click(ulong high,ulong low,double value=0){f.Click(high,low,value);f.Build();}
            void Text(ulong high,ulong low,string text,uint phase=3){var bytes=Encoding.UTF8.GetBytes(text);var e=f.Event(high,low);e.Phase=phase;e.TextLength=(uint)bytes.Length;f.View.Apply([e],bytes);f.Build();}
            var w=f.Writer!;Click(50,6,1);Click(50,7);Click(50,8);var choice=f.View.Items.ToArray().First(i=>i.WidgetHigh==51&&i.Kind==(uint)GuiItemKind.SelectionButton&&Encoding.UTF8.GetString(f.View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength)).Contains("approved clip"));Click(51,choice.WidgetLow);
            byte[] disk=File.ReadAllBytes(f.File),scene=f.Owner.Document.CaptureBytes();Text(56,2,"0 1 0",1);Text(56,2,"0.1 0.9 0",2);Text(56,2,"0.2 0.8 0",3);Check(w.Capture()!.Nodes.Single(n=>n.BlendSpace is not null).BlendSpace!.Samples.Any(p=>Math.Abs(p.X-.2)<1e-8&&Math.Abs(p.Y-.2)<1e-8));Check(disk.SequenceEqual(File.ReadAllBytes(f.File))&&scene.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Edit!.State.UndoCount==1);
            Click(56,60,.25);Check(disk.SequenceEqual(File.ReadAllBytes(f.File)));Click(50,9);Check(disk.SequenceEqual(File.ReadAllBytes(f.File)));Click(50,8);Click(56,101,2);Check(w.Capture()!.Nodes.Single(n=>n.BlendSpace is not null).BlendSpace!.CycleSeconds==2);Click(50,9);
        });
        yield return("M6.6-C space-only graph creates state and event without a Clip node",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);var d=Draft(f);Save(f,d);f.ApproveUi();f.Click(40,8);f.Build();f.Click(24,12);f.Build();f.Click(24,48);f.Build();
            void Click(ulong high,ulong low,double value=0){f.Click(high,low,value);f.Build();}
            Click(50,6,1);Click(50,7);Click(50,8);Click(50,31);Click(50,44);
            var edited=f.Writer!.Capture()!;Check(edited.States.Length==1&&edited.Nodes.Single(n=>n.Id==edited.States[0].PoseNode).Kind==AnimationNodeKind.BlendSpace,"Space-only state pose");
            Click(50,34);Click(50,44);Check(f.Writer.Capture()!.Events.Single().ClipId==f.Clip,"Space dependency event");Click(50,9);Check(f.Owner.Edit!.State.UndoCount==1,"Uncommitted elements cancel");
        });
        yield return("M6.6-C weight request queued before revocation and repeated after re-pair never revives",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);Save(f,Draft(f));f.ApproveUi();var s=f.Writer!.Sequences;s.PrepareTrusted(false);Guid id=Guid.NewGuid(),requestId=Guid.NewGuid();
            Check(!f.Call(AnimationSequenceSchemas.Propose,new{graphId=f.Graph,caseId=id,test=AnimationSequenceCodec.Encode(new(.1,1,[],[]))},Guid.NewGuid()).GetProperty("isError").GetBoolean());var review=s.CaptureReview(id);s.Approve(review,review.Fingerprint,true);
            var request=new{jsonrpc="2.0",id=987654,method="tools/call",@params=new{name=AnimationSequenceSchemas.Run,arguments=new{contractVersion=2,requestId,sessionId=f.Owner.Edit!.SessionId,expectedRevision=f.Owner.Edit.Revision,input=new{graphId=f.Graph,caseId=id,section="weights",offset=0,limit=8}}}};
            f.Client.StandardInput.WriteLine(JsonSerializer.Serialize(request,Ncma.Editor.Protocol.Wire.Json));f.Client.StandardInput.Flush();var pending=f.Client.StandardOutput.ReadLineAsync();var watch=System.Diagnostics.Stopwatch.StartNew();while(f.Owner.Endpoint!.View.QueueCount==0){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Weight request queue.");Thread.Sleep(1);}Check(!pending.IsCompleted);s.Revoke();f.Owner.Endpoint.Pump();Check(pending.Wait(10000));using var response=JsonDocument.Parse(pending.Result!);Check(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            review=s.CaptureReview(id);s.Approve(review,review.Fingerprint,true);Guid client=f.Owner.Endpoint.View.Connections.Single().ConnectionId;f.Owner.Endpoint.Revoke(client);f.Owner.Endpoint.Pair(client,true);Check(f.Call(AnimationSequenceSchemas.Run,new{graphId=f.Graph,caseId=id,section="weights",offset=0,limit=8},requestId).GetProperty("isError").GetBoolean());Check(f.Owner.Document.World.Tick==0);
        });
        yield return("M6.6-C native ImGui topology/primary/query presentation and actual independent skin preview",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);Save(f,Draft(f));f.ApproveUi();byte[] scene=f.Owner.Document.CaptureBytes();
            using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,7,["platform","renderer"])]);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"M6.6 space authoring",1280,720,false);using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,1280,720);
            string path=Path.Combine(plugins,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true);using var preview=new EditorAnimationGraphPreview(renderer,()=>kernel);using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"));gui.AttachRenderer(renderer);f.View.AttachGraphAuthoring(f.Writer!,preview);
            void Click(ulong high,ulong low,double value=0){f.Click(high,low,value);f.Build();}Click(40,8);Click(24,12);Click(24,48);Click(50,6,1);Click(50,7);Click(50,8);var choice=f.View.Items.ToArray().First(i=>i.WidgetHigh==51&&i.Kind==(uint)GuiItemKind.SelectionButton&&Encoding.UTF8.GetString(f.View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength)).Contains("approved clip"));Click(51,choice.WidgetLow);Click(50,10);Click(50,52,1);Click(50,53);Click(50,55);Check(preview.Prepared);
            try{for(int i=0;i<4;i++){gui.Begin(window.Poll(),1d/60);f.Build();var frame=f.View.AttachGraphPreview(f.View.Frame.Frame);Check(f.View.GraphPreviewSubmitted&&f.View.Items.ToArray().Any(v=>v.WidgetHigh==56&&v.Kind==(uint)GuiItemKind.CanvasRect));gui.Draw(frame,f.View.Items,f.View.Text);gui.RenderGpu();if(i==3){var pixels=new byte[1280*720*4];renderer.Capture(pixels);GraphAuthoringTests.SaveBmp(Path.Combine(output,"m6-6-c-space-authoring.bmp"),pixels,1280,720);}renderer.Present();preview.Advance(1d/60);}
                Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0&&f.Owner.Document.World.Tick==0&&scene.SequenceEqual(f.Owner.Document.CaptureBytes()),"Shared graph preview owns no authoring World tick");
            }finally{preview.Close();}
            Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0,"Space authoring preview drains");
        });
    }
}
