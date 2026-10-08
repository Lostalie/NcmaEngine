using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Assets.Authoring;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Samples;

internal static unsafe class GraphAuthoringTests
{
    private static void Check(bool value,string reason="") { if(!value)throw new Exception("Graph authoring assertion: "+reason); }
    private static void Reject(Action action) { try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException){return;}throw new Exception("Graph authoring invalid input accepted."); }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root,File;internal readonly EditorSessionOwner Owner=new("Graph authoring");internal readonly EditorWorkspace Workspace;
        internal readonly EditorAnimationGraphWorkspace Graphs;internal readonly AnimationGraphDefinition Definition;
        internal Fixture(string output,string repository){var sample=ActionSample.Create(Path.Combine(output,"graph-authoring"),Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),0);Root=sample.Root;File=Path.Combine(Root,"assets/animator.ncmaanim");var record=AssetRecordCodec.Decode(System.IO.File.ReadAllBytes(Path.Combine(Root,"assets/procedural.fbx.ncmeta")));Guid rig=record.Subassets.Single(s=>s.Kind==AssetKind.Skeleton).AssetId,clip=record.Subassets.First(s=>s.Kind==AssetKind.Clip).AssetId;
            var a=AnimationGraphNode.Create(Guid.NewGuid(),"Idle",AnimationNodeKind.Clip) with{ClipId=clip,Loop=true,Speed=1,X=16,Y=16};var b=AnimationGraphNode.Create(Guid.NewGuid(),"Output",AnimationNodeKind.Output) with{X=300,Y=16};
            Definition=new(AnimationGraphCodec.CurrentVersion,Guid.NewGuid(),"Animator",rig,Guid.Empty,[],[a,b],[new(Guid.NewGuid(),a.Id,"pose",b.Id,"pose")],[],[]);System.IO.File.WriteAllBytes(File,AnimationGraphCodec.Encode(Definition));Workspace=new(Owner);Graphs=new(Workspace,Root,sample.ProjectId);Owner.ConfigureAssets(Root,sample.ProjectId,1,graphScope:Graphs.Scope);Graphs.Open("assets/animator.ncmaanim");
        }
        internal void Begin(){Graphs.ApproveFileWrite(Graphs.Stamp,Graphs.Path!,Definition.AssetId,true);Graphs.Begin(Graphs.Stamp);}
        internal Guid Ready(){Guid id=Graphs.PrepareLocal();var review=Graphs.CaptureReview(id);Graphs.Approve(review,review.Fingerprint,review.Graph,true);return id;}
        public void Dispose(){Graphs.Dispose();Owner.Dispose();}
    }
    private static JsonElement Rename(string name)=>AnimationGraphEdits.Operations(new{op="graph.rename",name});
    private static GuiEvent Event(EditorPresenter view,ulong high,ulong low,uint phase=3,double value=0,int bytes=0){var item=view.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low);var f=view.Frame;return new(){Kind=item.Kind,WidgetHigh=high,WidgetLow=low,Phase=phase,Value=value,TextLength=(uint)bytes,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision};}
    internal static IEnumerable<(string,Action)> Cases(string output,string repository,string plugins)
    {
        yield return("M6.4 shared closed semantic edits / incomplete draft / typed canvas",()=>{
            using var f=new Fixture(output,repository);var d=f.Definition;var canvas=new AnimationGraphCanvas();canvas.Load(d);canvas.Select(d.Nodes[0].Id);canvas.Select(d.Nodes[1].Id,true);var moved=AnimationGraphEdits.Apply(d,canvas.Move(new(20,30)));Check(moved.Nodes.Single(n=>n.Id==d.Nodes[0].Id).X==36&&moved.Nodes.Single(n=>n.Id==d.Nodes[1].Id).X==320);
            canvas.Load(moved);var pins=canvas.Pins;var from=pins.Single(p=>p.Node==d.Nodes[0].Id&&p.Output);var to=pins.Single(p=>p.Node==d.Nodes[1].Id&&!p.Output);var connected=AnimationGraphEdits.Apply(moved,canvas.Connect(from,to,Guid.NewGuid()));Check(connected.Links.Length==1);Reject(()=>canvas.Connect(from,pins.Single(p=>p.Node==d.Nodes[0].Id&&!p.Output),Guid.NewGuid()));
            Vector2 origin=new(100,200),anchor=new(350,260);var local=canvas.GraphPoint(anchor,origin);canvas.ZoomAt(anchor,origin,1.7f);Check(Vector2.Distance(canvas.GraphPoint(anchor,origin),local)<.001);canvas.TranslatePan(new(20,30));canvas.Marquee(new(-1000),new(1000),false);Check(canvas.Selection.Length==2);Check(canvas.Search("idle").SequenceEqual(new[]{d.Nodes[0].Id}));
            var incomplete=AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="link.delete",id=d.Links[0].Id}),false);Reject(()=>AnimationGraphCodec.Encode(incomplete));Reject(()=>AnimationGraphEdits.Apply(d,AnimationGraphEdits.Operations(new{op="graph.rename",name="X",path="assets/escape"})));
            using var duplicate=JsonDocument.Parse("[{\"op\":\"graph.rename\",\"name\":\"A\",\"name\":\"B\"}]");Reject(()=>AnimationGraphEdits.Apply(d,duplicate.RootElement));
            string numeric=AnimationGraphEdits.Operations(new{op="node.upsert",node=d.Nodes[0]}).GetRawText().Replace("\"clip\"","0",StringComparison.Ordinal);using var bad=JsonDocument.Parse(numeric);Reject(()=>AnimationGraphEdits.Apply(d,bad.RootElement));
        });
        yield return("M6.4 exact file review / draft cancel / shared Undo Redo / reopen",()=>{
            using var f=new Fixture(output,repository);byte[] scene=f.Owner.Document.CaptureBytes(),original=File.ReadAllBytes(f.File);Reject(()=>f.Graphs.Begin(f.Graphs.Stamp));Reject(()=>f.Graphs.ApproveFileWrite(f.Graphs.Stamp,"assets/other.ncmaanim",f.Definition.AssetId,true));
            f.Begin();for(int i=0;i<32;i++)f.Graphs.Update(Rename("Draft "+i));Check(original.SequenceEqual(File.ReadAllBytes(f.File))&&f.Owner.Edit!.State.UndoCount==0);f.Graphs.Cancel();Check(f.Graphs.Capture()!.Name=="Animator"&&!f.Owner.Edit!.State.EditBusy);
            f.Graphs.Begin(f.Graphs.Stamp);f.Graphs.ApplyDraft(Rename("Saved"));Guid id=f.Ready();Check(f.Graphs.CommitLocal(id).Status=="ok"&&f.Owner.Edit!.State.UndoCount==1);Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Name=="Saved"&&scene.SequenceEqual(f.Owner.Document.CaptureBytes()));
            f.Workspace.History(f.Workspace.Stamp,false);f.Graphs.Synchronize();Check(original.SequenceEqual(File.ReadAllBytes(f.File)));f.Workspace.History(f.Workspace.Stamp,true);f.Graphs.Synchronize();Check(f.Graphs.Capture()!.Name=="Saved");f.Graphs.Open("assets/animator.ncmaanim");Check(f.Graphs.Capture()!.Name=="Saved"&&!f.Graphs.Writable);f.Graphs.Revoke();Reject(()=>f.Workspace.History(f.Workspace.Stamp,false));
        });
        yield return("M6.4 new graph checked create / Undo removes only created file / Redo",()=>{
            using var f=new Fixture(output,repository);var d=f.Definition with{AssetId=Guid.NewGuid(),Name="New"};f.Graphs.New("assets/new.ncmaanim",d);Check(!File.Exists(Path.Combine(f.Root,"assets/new.ncmaanim")));f.Graphs.ApproveFileWrite(f.Graphs.Stamp,f.Graphs.Path!,d.AssetId,true);f.Graphs.Begin(f.Graphs.Stamp);Guid id=f.Ready();f.Graphs.CommitLocal(id);Check(File.Exists(Path.Combine(f.Root,"assets/new.ncmaanim")));
            f.Workspace.History(f.Workspace.Stamp,false);f.Graphs.Synchronize();Check(f.Graphs.Capture() is null&&!File.Exists(Path.Combine(f.Root,"assets/new.ncmaanim")));f.Workspace.History(f.Workspace.Stamp,true);f.Graphs.Synchronize();Check(f.Graphs.Capture()!.AssetId==d.AssetId&&File.Exists(f.File));Reject(()=>f.Graphs.New("assets/new.ncmaanim",d));foreach(string path in new[]{"../escape.ncmaanim","out/test.ncmaanim",f.File,"assets/old.ncscene"})Reject(()=>f.Graphs.New(path,d));
        });
        yield return("M6.4 raw file conflict / missing resource / forged review / stale World",()=>{
            using var f=new Fixture(output,repository);f.Begin();f.Graphs.ApplyDraft(Rename("Candidate"));Guid id=f.Graphs.PrepareLocal();var review=f.Graphs.CaptureReview(id);Reject(()=>f.Graphs.Approve(review,review.Fingerprint,review.Graph,false));Reject(()=>f.Graphs.Approve(review with{ResourceHashes=[]},review.Fingerprint,review.Graph,true));review.Dependencies[0]=Guid.NewGuid();Reject(()=>f.Graphs.Approve(review,review.Fingerprint,review.Graph,true));Check(f.Graphs.Review!.Dependencies[0]!=review.Dependencies[0]);
            id=f.Ready();File.AppendAllText(f.File,"\n");byte[] conflict=File.ReadAllBytes(f.File);Reject(()=>f.Graphs.CommitLocal(id));Check(conflict.SequenceEqual(File.ReadAllBytes(f.File))&&f.Owner.Edit!.State.UndoCount==0);f.Graphs.Cancel();f.Graphs.Open("assets/animator.ncmaanim");f.Graphs.ApproveFileWrite(f.Graphs.Stamp,f.Graphs.Path!,f.Definition.AssetId,true);f.Graphs.Begin(f.Graphs.Stamp);
            var node=f.Definition.Nodes[0] with{ClipId=Guid.NewGuid()};f.Graphs.ApplyDraft(AnimationGraphEdits.Operations(new{op="node.upsert",node}));id=f.Graphs.PrepareLocal();Reject(()=>f.Graphs.CaptureReview(id));f.Graphs.Cancel();f.Begin();id=f.Ready();f.Graphs.Cancel();f.Workspace.CreateObject(f.Workspace.Stamp);Reject(()=>f.Graphs.CaptureReview(id));
        });
        yield return("M6.4 graph participant compensates checked journal at publication failure",()=>{
            using var f=new Fixture(output,repository);var paths=new AssetProjectPaths(f.Root);var clock=new AssetRevisionClock();bool fail=true;using var command=new AnimationGraphCommands(paths,new((_,_,_,_)=>true),clock,stage=>{if(fail&&stage=="published:0")throw new IOException("injected");});var edit=new EditSession(new Ncma.Scene.SceneDocument("Fault"));edit.RegisterCommandParticipant(AnimationGraphCommands.Descriptor,command);byte[] before=File.ReadAllBytes(f.File);Guid proposal=Guid.NewGuid();command.PrepareProposal(proposal,"assets/animator.ncmaanim",before,f.Definition with{Name="After"},clock.Revision);
            var request=new CapabilityRequest(2,Guid.NewGuid(),edit.SessionId,edit.Revision,AnimationGraphCommands.CapabilityName,JsonSerializer.SerializeToElement(new{proposalId=proposal,expectedAssetRevision=clock.Revision}));var result=edit.Invoke(request,new([AnimationGraphCommands.CapabilityName]));Check(result.Status!="ok"&&before.SequenceEqual(File.ReadAllBytes(f.File))&&!File.Exists(f.File+".journal")&&!edit.State.Frozen&&edit.State.UndoCount==0,JsonSerializer.Serialize(new{result,state=edit.State,journal=File.Exists(f.File+".journal")}));fail=false;
        });
        yield return("M6.4 actual stdio propose / two human approvals / transaction replay / Undo",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);var writer=f.Writer!;Guid proposal=Guid.NewGuid();var input=new{graphId=f.Graph,proposalId=proposal,operations=Rename("MCP saved")};Check(f.Call(AnimationGraphAuthoringSchemas.Propose,input,Guid.NewGuid()).GetProperty("isError").GetBoolean());f.ApproveUi();byte[] before=File.ReadAllBytes(f.File),scene=f.Owner.Document.CaptureBytes();
            using(var locked=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.None)){var result=f.Call(AnimationGraphAuthoringSchemas.Propose,input,Guid.NewGuid());Check(!result.GetProperty("isError").GetBoolean(),result.GetRawText());var repeat=f.Call(AnimationGraphAuthoringSchemas.Propose,input,Guid.NewGuid());Check(!repeat.GetProperty("isError").GetBoolean());}
            Check(before.SequenceEqual(File.ReadAllBytes(f.File))&&f.Owner.Edit!.State.UndoCount==0&&scene.SequenceEqual(f.Owner.Document.CaptureBytes()));Guid request=Guid.NewGuid();ulong revision=f.Owner.Edit!.Revision;var tx=new{proposalId=proposal,expectedAssetRevision=f.Owner.Assets!.Clock.Revision};var denied=f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision);Check(denied.GetProperty("isError").GetBoolean());var controller=new EditorAuthorizationController(f.Workspace);var page=controller.Capture()!;var displayed=page.Proposals.Single();controller.Approve(page,displayed.Scope.Id,displayed.Fingerprint,true,null);
            Check(f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision).GetProperty("isError").GetBoolean()); // endpoint alone is not file authority
            var review=writer.CaptureReview(proposal);writer.Approve(review,review.Fingerprint,review.Graph,true);var ok=f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision);Check(!ok.GetProperty("isError").GetBoolean(),ok.GetRawText());Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Name=="MCP saved"&&f.Owner.Edit.State.UndoCount==1);
            var replay=f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision);Check(!replay.GetProperty("isError").GetBoolean()&&replay.GetProperty("structuredContent").GetProperty("replayed").GetBoolean());writer.Synchronize();f.Workspace.History(f.Workspace.Stamp,false);writer.Synchronize();Check(before.SequenceEqual(File.ReadAllBytes(f.File)));f.Workspace.History(f.Workspace.Stamp,true);writer.Synchronize();writer.Revoke();Reject(()=>f.Workspace.History(f.Workspace.Stamp,false));
        });
        yield return("M6.4 Agent expiry/revoke/re-pair/schema/stale resource negatives",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,authoring:true);f.ApproveUi();Guid id=Guid.NewGuid();object input=new{graphId=f.Graph,proposalId=id,operations=Rename("Denied")};Check(!f.Call(AnimationGraphAuthoringSchemas.Propose,input,Guid.NewGuid()).GetProperty("isError").GetBoolean());var writer=f.Writer!;var review=writer.CaptureReview(id);writer.Approve(review,review.Fingerprint,review.Graph,true);f.Time.Now=60000;Reject(()=>writer.CaptureReview(id));f.Time.Now=0;writer.Reads.Revoke();Reject(()=>writer.CaptureReview(id));
            f.ApproveUi();writer.CancelProposal(id);id=Guid.NewGuid();input=new{graphId=f.Graph,proposalId=id,operations=Rename("New")};f.Call(AnimationGraphAuthoringSchemas.Propose,input,Guid.NewGuid());review=writer.CaptureReview(id);var endpoint=f.Owner.Endpoint!;var connection=endpoint.View.Connections.Single().ConnectionId;endpoint.Revoke(connection);endpoint.Pair(connection,true);Reject(()=>writer.Approve(review,review.Fingerprint,review.Graph,true));Check(f.Call(AnimationGraphAuthoringSchemas.Propose,new{graphId=f.Graph,proposalId=Guid.NewGuid(),operations=Rename("X"),path="assets/escape"},Guid.NewGuid()).GetProperty("isError").GetBoolean());
        });
        yield return("M6.4 committed resource generation replacement / Play freeze / preview cook denial",()=>{
            using var f=new Fixture(output,repository);f.Begin();f.Graphs.ApplyDraft(Rename("Resource checked"));Guid proposal=f.Ready();using(var snapshot=f.Graphs.PreparePreview(proposal)){using var view=snapshot.AcquireLease();Check(view.IsAuthoringPreview);Reject(()=>RuntimeAssetPackage.Encode(view));Reject(()=>view.CreateGraphPreview(f.Definition));}
            string metadata=Path.Combine(f.Root,"assets/procedural.fbx.ncmeta");var record=AssetRecordCodec.Decode(File.ReadAllBytes(metadata));var generation=record.Generation!;string next=generation.RelativePath.Replace("/1-","/2-",StringComparison.Ordinal);File.Copy(Path.Combine(f.Root,generation.RelativePath),Path.Combine(f.Root,next));File.WriteAllBytes(metadata,AssetRecordCodec.Encode(record with{Generation=generation with{Number=2,RelativePath=next}}));byte[] before=File.ReadAllBytes(f.File);Reject(()=>f.Graphs.CommitLocal(proposal));Check(before.SequenceEqual(File.ReadAllBytes(f.File))&&f.Owner.Edit!.State.UndoCount==0);f.Graphs.Cancel();
            f.Graphs.Open("assets/animator.ncmaanim");f.Graphs.ApproveFileWrite(f.Graphs.Stamp,f.Graphs.Path!,f.Definition.AssetId,true);var play=f.Owner.StartPlay();play.Pause();ulong tick=play.Tick;Guid session=play.SessionId;Reject(()=>f.Graphs.Begin(f.Graphs.Stamp));Reject(()=>f.Graphs.New("assets/frozen.ncmaanim",f.Definition with{AssetId=Guid.NewGuid()}));Check(play.Tick==tick&&play.SessionId==session);f.Owner.StopPlay();
        });
        yield return("M6.4 actual editor stamped canvas gestures + independent GPU preview + resource release",()=>GpuUi(output,repository,plugins));
    }
    private static void GpuUi(string output,string repository,string plugins)
    {
        using var f=new Fixture(output,repository);byte[] scene=f.Owner.Document.CaptureBytes();Guid world=f.Owner.Document.World.Identity;
        using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,7,["platform","renderer"])]);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"M6.4 animation workspace",1280,720,false);using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,1280,720);
        string path=Path.Combine(plugins,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));using var preview=new EditorAnimationGraphPreview(renderer,()=>kernel);using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"));gui.AttachRenderer(renderer);
        try {
        var view=new EditorPresenter(f.Workspace,null,f.Root,workspaceStyle:true);view.AttachGraphAuthoring(f.Graphs,preview);ulong frame=1;
        void Build(){view.SynchronizeGraph();view.Build(frame++,1280,720);}void Click(ulong high,ulong low){view.Apply([Event(view,high,low)],[]);Build();}
        Build();Click(24,12);Click(24,48);Check(view.GraphWorkspaceActive);view.Apply([Event(view,50,6,value:1)],[]);Build();Click(50,7);
        // UI starts one copied draft for the complete drag; file/history do not change until explicit reviewed save.
        var canvasItem=view.Items.ToArray().Single(i=>i.Kind==(uint)GuiItemKind.CanvasInput);float x=canvasItem.Rect[0],y=canvasItem.Rect[1],w=canvasItem.Rect[2],h=canvasItem.Rect[3];
        void Pointer(uint phase,float px,float py){string text=FormattableString.Invariant($"{px/w} {py/h} 0");byte[] bytes=Encoding.UTF8.GetBytes(text);view.Apply([Event(view,51,2,phase,bytes:bytes.Length)],bytes);Build();}
        Pointer(1,100,60);Check(f.Graphs.HasDraft);Pointer(2,120,70);Pointer(3,140,80);Check(f.Owner.Edit!.State.UndoCount==0&&f.Graphs.Capture()!.Nodes.Single(n=>n.Id==f.Definition.Nodes[0].Id).X==56);Click(50,34);Click(50,44);Click(50,10);view.Apply([Event(view,50,52,value:1)],[]);Build();Click(50,53);Click(50,55);Check(preview.Prepared&&preview.WorldId!=world&&preview.Tick==0);
        byte[]? start=null,last=null;
        for(int i=0;i<16;i++) {gui.Begin(window.Poll(),1.0/60);Build();var description=view.AttachGraphPreview(view.Frame.Frame);Check(view.GraphPreviewSubmitted);gui.Draw(description,view.Items,view.Text);gui.RenderGpu();if(i==0)start=preview.Capture();if(i==15){last=preview.Capture();byte[] screenshot=new byte[1280*720*4];renderer.Capture(screenshot);var image=view.Items.ToArray().Single(item=>item.Kind==(uint)GuiItemKind.Image&&item.WidgetHigh==51);int iw=(int)image.Rect[2],ih=(int)image.Rect[3],matches=0;for(int sy=1;sy<5;sy++)for(int sx=1;sx<5;sx++){int px=iw*sx/5,py=ih*sy/5,src=(py*iw+px)*4,dst=(((int)image.Rect[1]+py)*1280+(int)image.Rect[0]+px)*4;if(last.AsSpan(src,3).SequenceEqual(screenshot.AsSpan(dst,3)))matches++;}Check(matches>=12,"Real preview must be visibly composited, not hidden behind another panel.");SaveBmp(Path.Combine(output,"m6-4-animation-workspace.bmp"),screenshot,1280,720);}renderer.Present();preview.Advance(1.0/60);}
        Check(start is not null&&last is not null&&!start.SequenceEqual(last)&&preview.Tick==0&&preview.GraphFrame!.Value.Context.Tick==16);Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0);Check(scene.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Document.World.Identity==world&&f.Owner.Document.World.Tick==0);
        Click(50,54);Check(f.Owner.Edit.State.UndoCount==1&&AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Nodes.Single(n=>n.Id==f.Definition.Nodes[0].Id).X==56);Build();var old=Event(view,50,8);old.ViewGeneration--;view.Apply([old],[]);Check(!f.Graphs.HasDraft);
        // The sole complete-document history intentionally reinstalls the identical scene and
        // invalidates runtime references at an approved file commit. Preview itself does not.
        Check(scene.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Document.World.Identity!=world&&f.Owner.Document.World.Tick==0);preview.Close();Check(renderer.SkinStats.Meshes==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0);
        File.WriteAllBytes(Path.Combine(output,"m6-4-preview-start.rgba"),start!);File.WriteAllText(Path.Combine(output,"m6-4-graph-authoring-results.json"),JsonSerializer.Serialize(new{passed=true,frames=16,independentWorld=true,worldTick=0,manualAccepted=false,rootCollisionIntegrated=false}));
        }catch(Exception error){Console.Error.WriteLine("M6.4 GPU original failure: "+error);throw;}
    }
    private static void SaveBmp(string path,byte[] pixels,int width,int height){using var bmp=new BinaryWriter(File.Create(path));bmp.Write((ushort)0x4d42);bmp.Write(54+pixels.Length);bmp.Write(0);bmp.Write(54);bmp.Write(40);bmp.Write(width);bmp.Write(-height);bmp.Write((ushort)1);bmp.Write((ushort)32);bmp.Write(0);bmp.Write(pixels.Length);bmp.Write(2835);bmp.Write(2835);bmp.Write(0);bmp.Write(0);for(int i=0;i<pixels.Length;i+=4){bmp.Write(pixels[i+2]);bmp.Write(pixels[i+1]);bmp.Write(pixels[i]);bmp.Write(pixels[i+3]);}Console.WriteLine("M6.4 actual workspace image: "+path);}
}
