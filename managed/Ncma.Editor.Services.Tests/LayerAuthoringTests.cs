using System.Text;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Animation.Native;
using System.Security.Cryptography;

internal static unsafe class LayerAuthoringTests
{
    private static void Check(bool v,string reason=""){if(!v)throw new Exception("M6.7-C author: "+reason);}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException){return;}throw new Exception("Invalid layer author accepted.");}
    private static AnimationGraphDefinition Definition(GraphInspectionTests.Fixture f,bool additive=true)
    {
        var w=f.Writer!;w.Skeletons.PrepareTrusted();var rig=w.Skeletons.LocalCopy();var d=w.Capture()!;var basis=d.Nodes.Single(n=>n.Kind==AnimationNodeKind.Clip);var output=d.Nodes.Single(n=>n.Kind==AnimationNodeKind.Output);
        var cache=AnimationGraphNode.Create(Guid.NewGuid(),"Cache",AnimationNodeKind.CachePose);var layer=AnimationGraphNode.Create(Guid.NewGuid(),"Upper",additive?AnimationNodeKind.LayerAdditive:AnimationNodeKind.LayerOverride) with{Weight=.5,Layer=new(new(Guid.NewGuid(),rig.Id,rig.ContentHash,[new(rig.Bones[1].Path,1)]),additive?f.Clip:Guid.Empty,0)};
        var blend=AnimationGraphNode.Create(Guid.NewGuid(),"Mixed",AnimationNodeKind.Blend) with{Weight=.5};
        return d with{Nodes=[basis with{X=0,Y=0},cache with{X=220,Y=0},layer with{X=440,Y=120},blend with{X=660,Y=0},output with{X=880,Y=0}],Links=[new(Guid.NewGuid(),basis.Id,"pose",cache.Id,"pose"),new(Guid.NewGuid(),cache.Id,"pose",layer.Id,"a"),new(Guid.NewGuid(),cache.Id,"pose",layer.Id,"b"),new(Guid.NewGuid(),cache.Id,"pose",blend.Id,"a"),new(Guid.NewGuid(),layer.Id,"pose",blend.Id,"b"),new(Guid.NewGuid(),blend.Id,"pose",output.Id,"pose")]};
    }
    private static void Save(GraphInspectionTests.Fixture f,AnimationGraphDefinition d)
    {
        var w=f.Writer!;w.ApproveFileWrite(w.Stamp,w.Path!,f.Graph,true);w.Begin(w.Stamp);
        w.ApplyDraft(AnimationGraphEdits.Operations(d.Nodes.Select(n=>(object)new{op="node.upsert",node=n}).Concat(w.Capture()!.Links.Select(l=>(object)new{op="link.delete",id=l.Id})).Concat(d.Links.Select(l=>(object)new{op="link.upsert",link=l})).ToArray()));
        Guid p=w.PrepareLocal();var r=w.CaptureReview(p);w.Approve(r,r.Fingerprint,r.Graph,true);w.CommitLocal(p);f.Build();
    }
    internal static IEnumerable<(string,Action)> Cases(string output,string repository,string plugins)
    {
        yield return("M6.7-C real NCA bone metadata default denied/separate exact review/schema/copied paths/TTL/revoke",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);f.ApproveUi();var s=f.Writer!.Skeletons;s.PrepareTrusted();Guid request=Guid.NewGuid();object input=new{graphId=f.Graph,offset=0,limit=8};
            Check(f.Call(AnimationSkeletonSchemas.Name,input,request).GetProperty("isError").GetBoolean());var r=s.CaptureReview();Reject(()=>s.Approve(r,r.Fingerprint,false));Reject(()=>s.Approve(r with{SkeletonHash=new('B',64)},r.Fingerprint,true));s.Approve(r,r.Fingerprint,true);
            var read=f.Call(AnimationSkeletonSchemas.Name,input,request);var content=read.GetProperty("structuredContent");CharacterInspectionTests.Schema(content,AnimationSkeletonSchemas.Descriptor.OutputSchema);var data=content.GetProperty("data");Check(data.GetProperty("resourcesPrepared").GetBoolean()&&!data.GetProperty("poseMemory").GetBoolean()&&!data.GetProperty("livePlay").GetBoolean());Check(data.GetProperty("page").GetProperty("bones")[1].GetProperty("path").GetString()=="root/hand");
            var owned=s.LocalCopy();owned.Bones[0]=new("changed",-1);Check(s.LocalCopy().Bones[0].Path=="root");Check(!read.GetRawText().Contains(f.Root,StringComparison.OrdinalIgnoreCase));s.Revoke();Check(f.Call(AnimationSkeletonSchemas.Name,input,request).GetProperty("isError").GetBoolean());s.Approve(s.CaptureReview(),s.CaptureReview().Fingerprint,true);f.Time.Now=60000;Check(f.Call(AnimationSkeletonSchemas.Name,input,request).GetProperty("isError").GetBoolean());Check(f.Owner.Document.World.Tick==0&&f.Owner.Edit!.State.UndoCount==0);
        });
        yield return("M6.7-C bone schema rejects extra paths/limit and queued revocation/re-pair without execution",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);f.ApproveUi();var s=f.Writer!.Skeletons;s.PrepareTrusted();var r=s.CaptureReview();s.Approve(r,r.Fingerprint,true);Guid requestId=Guid.NewGuid();
            Check(f.Call(AnimationSkeletonSchemas.Name,new{graphId=f.Graph,offset=0,limit=9},requestId).GetProperty("isError").GetBoolean());Check(f.Call(AnimationSkeletonSchemas.Name,new{graphId=f.Graph,offset=0,limit=1,path="secret"},Guid.NewGuid()).GetProperty("isError").GetBoolean());
            var request=new{jsonrpc="2.0",id=987654,method="tools/call",@params=new{name=AnimationSkeletonSchemas.Name,arguments=new{contractVersion=2,requestId,sessionId=f.Owner.Edit!.SessionId,expectedRevision=f.Owner.Edit.Revision,input=new{graphId=f.Graph,offset=0,limit=1}}}};
            f.Client.StandardInput.WriteLine(JsonSerializer.Serialize(request,Ncma.Editor.Protocol.Wire.Json));f.Client.StandardInput.Flush();var pending=f.Client.StandardOutput.ReadLineAsync();var watch=System.Diagnostics.Stopwatch.StartNew();while(f.Owner.Endpoint!.View.QueueCount==0){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Bone queue.");Thread.Sleep(1);}Check(!pending.IsCompleted);s.Revoke();f.Owner.Endpoint.Pump();Check(pending.Wait(10000));using var response=JsonDocument.Parse(pending.Result!);Check(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            r=s.CaptureReview();s.Approve(r,r.Fingerprint,true);Guid client=f.Owner.Endpoint.View.Connections.Single().ConnectionId;f.Owner.Endpoint.Revoke(client);f.Owner.Endpoint.Pair(client,true);Check(f.Call(AnimationSkeletonSchemas.Name,new{graphId=f.Graph,offset=0,limit=1},requestId).GetProperty("isError").GetBoolean());Check(f.Owner.Document.World.Tick==0);
        });
        yield return("M6.7-C shared mask history/actual skeleton rejection/real stdio pure proposal and dual-approved transaction",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);var d=Definition(f);Save(f,d);f.ApproveUi();var n=d.Nodes.Single(n=>n.Layer is not null);byte[] before=File.ReadAllBytes(f.File);Guid proposal=Guid.NewGuid();
            using(var locked=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.None))Check(!f.Call(AnimationGraphAuthoringSchemas.Propose,new{graphId=f.Graph,proposalId=proposal,operations=AnimationGraphEdits.Operations(new{op="layer.bone.upsert",nodeId=n.Id,bone=new AnimationBoneWeight("root/hand",.25f)})},Guid.NewGuid()).GetProperty("isError").GetBoolean());
            Guid request=Guid.NewGuid();ulong revision=f.Owner.Edit!.Revision;object tx=new{proposalId=proposal,expectedAssetRevision=f.Owner.Assets!.Clock.Revision};Check(f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision).GetProperty("isError").GetBoolean());var auth=new EditorAuthorizationController(f.Workspace);var page=auth.Capture()!;var pending=page.Proposals.Single();auth.Approve(page,pending.Scope.Id,pending.Fingerprint,true,null);
            Check(f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision).GetProperty("isError").GetBoolean());var review=f.Writer!.CaptureReview(proposal);f.Writer.Approve(review,review.Fingerprint,review.Graph,true);Check(!f.Call(AnimationGraphCommands.CapabilityName,tx,request,revision).GetProperty("isError").GetBoolean());Check(f.Owner.Edit.State.UndoCount==2);f.Writer.Synchronize();f.Workspace.History(f.Workspace.Stamp,false);f.Writer.Synchronize();Check(before.SequenceEqual(File.ReadAllBytes(f.File)));
            var w=f.Writer;w.ApproveFileWrite(w.Stamp,w.Path!,f.Graph,true);w.Begin(w.Stamp);w.ApplyDraft(AnimationGraphEdits.Operations(new{op="layer.bone.upsert",nodeId=n.Id,bone=new AnimationBoneWeight("root",1)}));Guid bad=w.PrepareLocal();Reject(()=>w.CaptureReview(bad));w.Cancel();
        });
        yield return("M6.7-C actual independent cache diagnostics default denied/exact case/closed schema/revoke",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);Save(f,Definition(f));f.ApproveUi();var s=f.Writer!.Sequences;s.PrepareTrusted(false);Guid id=Guid.NewGuid(),request=Guid.NewGuid();var test=new AnimationSequenceCase(.1,3,[],[new(1,AnimationSequenceAssertionKind.CacheHits,Guid.Empty,2),new(1,AnimationSequenceAssertionKind.CacheRequests,Guid.Empty,3)]);
            Check(!f.Call(AnimationSequenceSchemas.Propose,new{graphId=f.Graph,caseId=id,test=AnimationSequenceCodec.Encode(test)},Guid.NewGuid()).GetProperty("isError").GetBoolean());object input=new{graphId=f.Graph,caseId=id,section="cache",offset=0,limit=8};Check(f.Call(AnimationSequenceSchemas.Run,input,request).GetProperty("isError").GetBoolean());var review=s.CaptureReview(id);s.Approve(review,review.Fingerprint,true);
            var read=f.Call(AnimationSequenceSchemas.Run,input,request).GetProperty("structuredContent");CharacterInspectionTests.Schema(read,AnimationSequenceSchemas.Descriptors()[1].OutputSchema);var rows=read.GetProperty("data").GetProperty("items");Check(rows.GetArrayLength()==3&&rows[0].GetProperty("hits").GetInt32()==2&&rows[0].GetProperty("requests").GetInt32()==3);Check(s.RunLocal(id).Passed&&f.Owner.Document.World.Tick==0&&f.Owner.Edit!.State.UndoCount==1);s.Revoke();Check(f.Call(AnimationSequenceSchemas.Run,input,request).GetProperty("isError").GetBoolean());
        });
        yield return("M6.7-C stamped typed layer creation/weights/draft cancellation and exact UI bone approval",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);f.ApproveUi();f.Click(40,8);f.Build();f.Click(24,12);f.Build();f.Click(24,48);f.Build();
            void Click(ulong high,ulong low,double value=0){f.Click(high,low,value);f.Build();}
            Click(50,90);Click(58,1);Click(58,4);int pages=0;while(f.View.Items.ToArray().Any(i=>i.WidgetHigh==58&&i.WidgetLow==6&&i.Enabled!=0)){if(++pages>512)throw new Exception("Bounded review pagination.");Click(58,6);}Click(58,7,1);Click(58,8);Check(!f.Call(AnimationSkeletonSchemas.Name,new{graphId=f.Graph,offset=0,limit=1},Guid.NewGuid()).GetProperty("isError").GetBoolean(),"Complete UI bone review");Click(58,10);
            Click(50,6,1);Click(50,7);Click(50,8);byte[] disk=File.ReadAllBytes(f.File);Click(50,86);var n=f.Writer!.Capture()!.Nodes.Single(n=>n.Layer is not null);Check(n.Layer!.Mask.SkeletonHash==f.Writer.Skeletons.LocalCopy().ContentHash&&n.Layer.Mask.Bones.Single().Weight==0);
            var item=f.View.Items.ToArray().First(i=>i.WidgetHigh==57&&i.Kind==(uint)GuiItemKind.Button&&Encoding.UTF8.GetString(f.View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength)).Contains("root/hand"));Click(57,item.WidgetLow);
            var weight=f.View.Items.ToArray().Single(i=>i.WidgetHigh==57&&i.Kind==(uint)GuiItemKind.Number&&Encoding.UTF8.GetString(f.View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength)).Contains("root/hand"));Click(57,weight.WidgetLow,.5);Check(f.Writer.Capture()!.Nodes.Single(n=>n.Layer is not null).Layer!.Mask.Bones.Single(b=>b.BonePath=="root/hand").Weight==.5f);
            Click(50,9);Check(disk.SequenceEqual(File.ReadAllBytes(f.File))&&f.Owner.Edit!.State.UndoCount==0&&f.Owner.Document.World.Tick==0);
        });
        yield return("M6.7-C actual ImGui typed mask properties and independent layered skin preview",()=>{
            using var f=new GraphInspectionTests.Fixture(output,repository,true);Save(f,Definition(f));f.ApproveUi();byte[] before=f.Owner.Document.CaptureBytes();
            using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,7,["platform","renderer"])]);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"M6.7 layers",1280,720,false);using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,1280,720);
            string path=Path.Combine(plugins,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true,layerSupport:true);using var preview=new EditorAnimationGraphPreview(renderer,()=>kernel);using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"));gui.AttachRenderer(renderer);f.View.AttachGraphAuthoring(f.Writer!,preview);
            void Click(ulong high,ulong low,double value=0){f.Click(high,low,value);f.Build();}
            Click(40,8);Click(24,12);Click(24,48);Click(50,6,1);Click(50,7);Click(50,8);var button=f.View.Items.ToArray().Single(i=>i.WidgetHigh==51&&i.Kind==(uint)GuiItemKind.SelectionButton&&Encoding.UTF8.GetString(f.View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength)).EndsWith("Upper",StringComparison.Ordinal));Click(51,button.WidgetLow);
            Click(50,10);Click(50,52,1);Click(50,53);Click(50,55);Check(preview.Prepared);
            try{for(int i=0;i<4;i++){gui.Begin(window.Poll(),1d/60);f.Build();var frame=f.View.AttachGraphPreview(f.View.Frame.Frame);Check(f.View.GraphPreviewSubmitted&&f.View.Items.ToArray().Any(v=>v.WidgetHigh==57&&v.Kind==(uint)GuiItemKind.Number));gui.Draw(frame,f.View.Items,f.View.Text);gui.RenderGpu();if(i==3){var bytes=new byte[1280*720*4];renderer.Capture(bytes);GraphAuthoringTests.SaveBmp(Path.Combine(output,"m6-7-c-layer-authoring.bmp"),bytes,1280,720);}renderer.Present();preview.Advance(1d/60);}
                Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0&&before.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Document.World.Tick==0,"No authoring World advancement");
            }finally{preview.Close();}
            Check(kernel.LayerStatistics.LayerCalls>0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0,"Actual layer path and complete drain");
        });
    }
}
