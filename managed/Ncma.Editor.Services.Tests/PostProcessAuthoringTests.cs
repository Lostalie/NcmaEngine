using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene.Rendering;

internal static class PostProcessAuthoringTests
{
    internal static void CpuProbe(string output)
    {
        using var f=new Fixture(output);_=f.Service.Modules.Describe();_=EditorPostProcessWorkspace.ListDescriptor;
        Check(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName is "NcmaRenderer.dll" or "NcmaPlatform.dll" or "NcmaGui.dll" or "d3dcompiler_47.dll"));
        Console.WriteLine("POST_AUTHOR_CPU_NATIVE_COMPILER_ZERO");
    }
    private static void Check(bool value,string label="Post assertion"){if(!value)throw new Exception(label);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or JsonException){return;}throw new Exception("Expected rejection");}
    private sealed class Clock:TimeProvider { internal long Now;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>Now; }
    private sealed class Fixture:IDisposable
    {
        internal readonly EditorSessionOwner Owner;internal readonly EditorWorkspace Workspace;internal readonly EditorPresenter View;
        internal readonly Clock Time=new();internal readonly string Root;internal readonly Guid Id;private ulong _frame;
        internal EditorPostProcessWorkspace Service=>Owner.PostProcessing!;
        internal Fixture(string output,PostProcessModules? modules=null,bool geometry=true){
            Root=Path.Combine(output,"post-author-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
            Owner=new("Post authoring",components:RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry()),validateComposition:CandidatePresentation.RequireComposition,workflowTime:Time,postProcessModules:modules);
            Workspace=new(Owner);if(geometry){var mesh=Workspace.CreateObject(Workspace.Stamp);Workspace.Transaction(Workspace.Stamp,"Geometry",[
                new{op="set_component",objectId=mesh,typeId="ncma.transform",version=1,data=Owner.Document.Components.Encode(TransformData.Identity)},
                new{op="set_component",objectId=mesh,typeId=StaticMeshData.TypeId,version=1,data=Owner.Document.Components.Encode(new StaticMeshData(Guid.NewGuid(),Guid.NewGuid(),true,true,1))}]);}
            Id=Workspace.CreateObject(Workspace.Stamp);View=new(Workspace,null,Root,workspaceStyle:true);Build();Click("添加后处理配置（关闭）");
        }
        internal PostProcessData Value=>Owner.Document.GetComponent<PostProcessData>(Id);
        internal void Build()=>View.Build(++_frame,1280,720);
        internal GuiItem Item(string name){var found=View.Items.ToArray().Where(i=>Encoding.UTF8.GetString(View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))==name).ToArray();if(found.Length!=1)throw new Exception("Missing/duplicate GUI item: "+name+"; "+Encoding.UTF8.GetString(View.Text));return found[0];}
        internal GuiEvent Event(string name,uint phase=3,double value=0){var i=Item(name);var f=View.Frame;return new(){Kind=i.Kind,WidgetHigh=i.WidgetHigh,WidgetLow=i.WidgetLow,Phase=phase,Value=value,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision};}
        internal void Click(string name,double value=0){View.Apply([Event(name,3,value)],[]);Build();}
        internal void Change(string name,double value){View.Apply([Event(name,1),Event(name,2,value),Event(name,3,value)],[]);Build();}
        internal object Input(PostProcessData value)=>new{operations=new[]{RenderingEditorAdapter.PostOperation(Service.Modules,Id,value)}};
        internal CapabilityRequest Raw(PostProcessData value)=>new(2,Guid.NewGuid(),Owner.Edit!.SessionId,Owner.Edit.Revision,"ncma.scene.transaction",
            JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=Id,typeId=PostProcessData.TypeId,version=1,data=Owner.Document.Components.Encode(value)}}}));
        public void Dispose()=>Owner.Dispose();
    }
    private sealed class Client:IDisposable
    {
        private readonly Fixture _f;private readonly Process _process;private readonly Task<string> _errors;private int _id;
        internal Client(Fixture f,string repo){_f=f;f.Owner.ConfigureEndpoint(true,f.Root);var p=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            p.ArgumentList.Add(Path.Combine(repo,"out/managed/editor-mcp/Ncma.Editor.Mcp.dll"));p.ArgumentList.Add("--descriptor");p.ArgumentList.Add(f.Owner.Endpoint!.DescriptorPath);_process=Process.Start(p)!;_errors=_process.StandardError.ReadToEndAsync();
            Send(new{jsonrpc="2.0",id=++_id,method="initialize",@params=new{protocolVersion="2025-11-25"}});_process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            var list=Send(new{jsonrpc="2.0",id=++_id,method="tools/list"}).GetProperty("result").GetProperty("tools");Check(list.EnumerateArray().Any(t=>t.GetProperty("name").GetString()==EditorPostProcessWorkspace.ListCapability));}
        private JsonElement Send(object value){_process.StandardInput.WriteLine(JsonSerializer.Serialize(value,Wire.Json));_process.StandardInput.Flush();var line=_process.StandardOutput.ReadLineAsync();var watch=Stopwatch.StartNew();while(!line.IsCompleted){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException();var ep=_f.Owner.Endpoint!;foreach(var c in ep.View.Connections.Where(c=>c.Connected&&!c.Paired))ep.Pair(c.ConnectionId,true);ep.Pump();Thread.Sleep(1);}using var parsed=JsonDocument.Parse(line.GetAwaiter().GetResult()??throw new IOException("MCP closed"));return parsed.RootElement.Clone();}
        internal JsonElement Call(string capability,object input,Guid? request=null,ulong? revision=null)=>Send(new{jsonrpc="2.0",id=++_id,method="tools/call",@params=new{name=capability,arguments=new{contractVersion=2,requestId=request??Guid.NewGuid(),sessionId=_f.Owner.Edit!.SessionId,expectedRevision=revision??_f.Owner.Edit.Revision,input}}}).GetProperty("result");
        public void Dispose(){_process.StandardInput.Close();Check(_process.WaitForExit(5000));Check(_process.ExitCode==0&&_errors.GetAwaiter().GetResult()=="");_process.Dispose();}
    }
    private static void Schema(JsonElement value,JsonElement schema)
    {
        if(schema.TryGetProperty("oneOf",out var one)||schema.TryGetProperty("anyOf",out one)){Check(one.EnumerateArray().Any(s=>{try{Schema(value,s);return true;}catch{return false;}}),"Schema union");return;}
        if(schema.TryGetProperty("const",out var c))Check(value.GetRawText()==c.GetRawText());if(schema.TryGetProperty("enum",out var choices))Check(choices.EnumerateArray().Any(c=>c.GetRawText()==value.GetRawText()));
        if(!schema.TryGetProperty("type",out var type))return;
        switch(type.GetString()){
            case "object":Check(value.ValueKind==JsonValueKind.Object);var props=schema.GetProperty("properties");foreach(var p in value.EnumerateObject()){Check(props.TryGetProperty(p.Name,out var s));Schema(p.Value,s);}if(schema.TryGetProperty("required",out var required))foreach(var key in required.EnumerateArray())Check(value.TryGetProperty(key.GetString()!,out _));break;
            case "array":Check(value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()<=schema.GetProperty("maxItems").GetInt32());foreach(var x in value.EnumerateArray())Schema(x,schema.GetProperty("items"));break;
            case "string":Check(value.ValueKind==JsonValueKind.String);if(schema.TryGetProperty("maxLength",out var length))Check(value.GetString()!.Length<=length.GetInt32());if(schema.TryGetProperty("format",out _))Check(Guid.TryParse(value.GetString(),out _));break;
            case "integer":case "number":Check(value.ValueKind==JsonValueKind.Number&&double.IsFinite(value.GetDouble()));if(type.GetString()=="integer")Check(value.TryGetInt64(out _));if(schema.TryGetProperty("minimum",out var min))Check(value.GetDouble()>=min.GetDouble());if(schema.TryGetProperty("maximum",out var max))Check(value.GetDouble()<=max.GetDouble());break;
            case "boolean":Check(value.ValueKind is JsonValueKind.True or JsonValueKind.False);break;case "null":Check(value.ValueKind==JsonValueKind.Null);break;default:throw new Exception("Unknown schema type");
        }
    }
    internal static IEnumerable<(string,Action)> Cases(string output,string repo)
    {
        yield return("M7.4-C3 Low button uses original transaction and exact single history",()=>{
            using var f=new Fixture(output);f.Click("添加 Feature：Bloom");f.Change("启用后处理 / 关闭对照",1);f.Change("曝光 EV",-1);
            byte[] before=f.Owner.Document.CaptureBytes();var previous=f.Value;int history=f.Owner.Edit!.State.UndoCount;
            f.Click("应用场景画质：Low v1");
            Check(f.Owner.Edit.State.UndoCount==history+1&&f.Value==previous with{StackJson="[]"});
            Check(SceneShadowState.Read(f.Owner.Document.CaptureSnapshot())?.Quality==SceneShadowQuality.Low);
            byte[] low=f.Owner.Document.CaptureBytes();f.Workspace.History(f.Workspace.Stamp,false);f.Build();
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));f.Workspace.History(f.Workspace.Stamp,true);f.Build();
            Check(low.SequenceEqual(f.Owner.Document.CaptureBytes()));
        });
        yield return("M7.4-C3 Low stale forged draft and Play button guards preserve document",()=>{
            using var f=new Fixture(output);f.Click("添加 Feature：Bloom");byte[] before=f.Owner.Document.CaptureBytes();int history=f.Owner.Edit!.State.UndoCount;
            var forged=f.Event("应用场景画质：Low v1");forged.Kind=(uint)GuiItemKind.Label;f.View.Apply([forged],[]);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Edit.State.UndoCount==history);
            var stale=f.Event("应用场景画质：Low v1");f.Workspace.Transaction(f.Workspace.Stamp,"New revision",[new{op="rename",objectId=f.Id,name="New name"}],f.Id);
            before=f.Owner.Document.CaptureBytes();f.View.Apply([stale],[]);Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));f.Build();
            var beforeDraft=f.Event("应用场景画质：Low v1");
            f.View.Apply([f.Event("曝光 EV",1),f.Event("曝光 EV",2,1)],[]);
            Check(f.Workspace.HasDraft,"draft starts before queued quality click");f.View.Apply([beforeDraft],[]);
            Check(f.Workspace.HasDraft&&before.SequenceEqual(f.Owner.Document.CaptureBytes()),"queued quality click preserves both draft and committed document");
            f.Build();Check(f.Item("应用场景画质：Low v1").Enabled==0);f.View.CancelInteraction();f.Build();
            f.Owner.StartPlay();f.Build();Check(f.Item("应用场景画质：Low v1").Enabled==0);
            f.Click("应用场景画质：Low v1");Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));f.Owner.StopPlay();
        });
        yield return("M7.4-C3 catalog owned CPU data and defaults",()=>{
            var modules=PostProcessModules.Official();var descriptions=modules.Describe();Check(descriptions.Length==4);var bloom=descriptions.Single(m=>m.Kind=="feature");bloom.Parameters[0]=new("changed",0,1);Check(modules.Describe().Single(m=>m.Kind=="feature").Parameters[0].Name=="levels");
            var value=RenderingEditorAdapter.PostFeature(modules,PostProcessData.Off,PostProcessLibrarySelection.BloomModule,2);Check(value.StackJson==PostProcessData.EncodeStack([PostProcessModules.Bloom(new())]));
            Reject(()=>new PostProcessModules([new(Guid.NewGuid(),1,new string('x',65))],[]));Reject(()=>new PostProcessModules([], [new(Guid.NewGuid(),1,0,[new("gain",0,1,Default:2)],[],(_,_,_,_)=>null)]));
            var p=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            p.ArgumentList.Add(typeof(PostProcessAuthoringTests).Assembly.Location);p.ArgumentList.Add(repo);p.ArgumentList.Add("unused");p.ArgumentList.Add("--post-author-cpu-probe");
            using var child=Process.Start(p)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();Check(child.WaitForExit(10000)&&child.ExitCode==0);Check(stdout.GetAwaiter().GetResult().Contains("POST_AUTHOR_CPU_NATIVE_COMPILER_ZERO")&&stderr.GetAwaiter().GetResult()=="");
        });
        yield return("M7.4-C3 panel draft one Undo Tone EV Bloom Off compare",()=>{
            using var f=new Fixture(output);f.Click("添加 Feature：Bloom");f.Change("启用后处理 / 关闭对照",1);var before=f.Value;int history=f.Owner.Edit!.State.UndoCount;
            f.View.Apply([f.Event("Bloom / intensity",1),f.Event("Bloom / intensity",2,.8)],[]);Check(f.Value==before&&f.Workspace.HasDraft);f.Build();f.View.Apply([f.Event("Bloom / intensity",3,.8)],[]);f.Build();
            Check(f.Value!=before&&f.Owner.Edit.State.UndoCount==history+1);f.Workspace.History(f.Workspace.Stamp,false);Check(f.Value==before);f.Workspace.History(f.Workspace.Stamp,true);f.Build();
            f.Change("曝光 EV",-1);f.Click("选择 Tone：Reinhard");Check(f.Value.ExposureEv==-1&&f.Value.ToneModule==PostProcessLibrarySelection.ReinhardModule);
            string stack=f.Value.StackJson;f.Change("启用后处理 / 关闭对照",0);Check(!f.Value.Enabled&&f.Value.StackJson==stack);f.Workspace.History(f.Workspace.Stamp,false);Check(f.Value.Enabled);
        });
        yield return("M7.4-C3 panel invalid cancel forged stale and integer preserves committed state",()=>{
            using var f=new Fixture(output);f.Click("添加 Feature：Bloom");var before=f.Value;int history=f.Owner.Edit!.State.UndoCount;
            foreach(double v in new[]{1.5,1.00000001,7,double.NaN})f.Change("Bloom / levels（整数）",v);
            f.Change("曝光 EV",4.00000001);f.Change("Bloom / intensity",4.00000001);Check(f.Value==before&&f.Owner.Edit.State.UndoCount==history);
            var stale=f.Event("曝光 EV",1);f.Workspace.Transaction(f.Workspace.Stamp,"revision",[new{op="rename",objectId=f.Id,name="renamed"}],f.Id);f.View.Apply([stale],[]);Check(!f.Workspace.HasDraft);f.Build();
            var forged=f.Event("曝光 EV",1);forged.Kind=(uint)GuiItemKind.Label;f.View.Apply([forged],[]);Check(!f.Workspace.HasDraft);
            f.View.Apply([f.Event("曝光 EV",1),f.Event("曝光 EV",2,2)],[]);f.View.CancelInteraction();Check(f.Value==before&&!f.Workspace.HasDraft);
        });
        yield return("M7.4-C3 original raw transaction refuses unknown version range duplicate and pureUI",()=>{
            using var f=new Fixture(output);var before=f.Owner.Document.CaptureBytes();var edit=f.Owner.Edit!;var grant=new CapabilityPermissions(["ncma.scene.transaction"],objectScope:[f.Id],componentScope:[PostProcessData.TypeId]);
            foreach(var invalid in new[]{f.Value with{ToneModule=Guid.NewGuid()},f.Value with{ToneVersion=2},f.Value with{StackJson=PostProcessData.EncodeStack([new(Guid.NewGuid(),1,[])])},f.Value with{StackJson=PostProcessData.EncodeStack([PostProcessModules.Bloom(new()) with{Version=2}])}}){Check(!edit.Invoke(f.Raw(invalid),grant).Changed);Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));}
            using var ui=new Fixture(output,geometry:false);Check(!ui.Owner.Edit!.Invoke(ui.Raw(ui.Value with{Enabled=true}),new(["ncma.scene.transaction"])).Changed);Check(!ui.Value.Enabled);
            Guid configuration=f.Workspace.CreateObject(f.Workspace.Stamp);f.Workspace.Transaction(f.Workspace.Stamp,"Exposure",[new{op="set_component",objectId=configuration,typeId=RenderConfiguration.ComponentType,version=1,data=f.Owner.Document.Components.Encode(RenderConfiguration.Default(Guid.NewGuid()) with{Exposure=16})}]);
            Check(!edit.Invoke(f.Raw(f.Value with{Enabled=true,ExposureEv=1}),grant).Changed&&!f.Value.Enabled);
        });
        yield return("M7.4-C3 user parameters dependencies legal reordering same UI and contract",()=>{
            Guid a=Guid.NewGuid(),b=Guid.NewGuid();var modules=new PostProcessModules([new(PostProcessLibrarySelection.AcesModule,1,"Custom Tone")],[
                new(a,2,10,[new("gain",0,2,Default:.4f)],[],(_,_,_,_)=>throw new Exception("UI executed graph"),"User A"),
                new(b,1,10,[new("count",1,4,true,2)],[a],(_,_,_,_)=>throw new Exception("UI executed graph"),"User B")]);
            using var f=new Fixture(output,modules);Check(f.Item("添加 Feature：User B").Enabled==0);f.Click("添加 Feature：User A");f.Change("User A / gain",1.2);f.Click("添加 Feature：User B");Check(f.Item("上移 User B").Enabled==0&&f.Item("移除 User A").Enabled==0);f.Change("User B / count（整数）",3);
            Check(PostProcessData.DecodeStack(f.Value.StackJson)[1].Parameters[0].Value==3);f.Click("移除 User B");f.Click("移除 User A");Check(f.Value.StackJson=="[]");
            var independent=new PostProcessModules([new(PostProcessLibrarySelection.AcesModule,1)],[new(a,1,10,[],[],(_,_,_,_)=>null),new(b,1,10,[],[],(_,_,_,_)=>null)]);
            var v=PostProcessData.Off with{StackJson=PostProcessData.EncodeStack([new(a,1,[]),new(b,1,[])])};Check(PostProcessData.DecodeStack(RenderingEditorAdapter.PostFeature(independent,v,b,-1).StackJson)[0].ModuleId==b);
        });
        yield return("M7.4-C3 persistence Play freeze and current prepared stamps",()=>{
            using var f=new Fixture(output);f.Click("添加 Feature：Bloom");f.Change("曝光 EV",-2);string file=Path.Combine(f.Root,"scene.ncmascene");f.Workspace.Save(f.Workspace.Stamp,file);f.Workspace.Open(f.Workspace.Stamp,file,true);f.Workspace.Select(f.Workspace.Stamp,f.Id);f.Build();Check(f.Value.ExposureEv==-2);
            var preview=EditorSceneProjection.Bake(f.Owner);var world=preview.Scene.World;f.View.ShowPostPreparation(world.Identity,world.Revision,Guid.Empty,true,false,f.Value,authoring:preview.Source);Check(f.View.PostPreparationStatus.Contains("关闭"));
            f.View.ShowPostPreparation(world.Identity,world.Revision,Guid.Empty,false,true,null,authoring:preview.Source);Check(f.View.PostPreparationStatus.Contains("失败"));f.Change("曝光 EV",-1);Check(f.View.PostPreparationStatus.Contains("等待"));
            var play=f.Owner.StartPlay();f.Build();Check(f.Item("曝光 EV").Enabled==0);f.Change("曝光 EV",3);Check(f.Value.ExposureEv==-1);f.View.ShowPostPreparation(play.Scene.World.Identity,play.Scene.World.Revision,play.SessionId,true,false,f.Value);Check(f.View.PostPreparationStatus.Contains("Play"));f.Owner.StopPlay();Check(f.View.PostPreparationStatus.Contains("等待"));
        });
        yield return("M7.4-C3 stdio catalog default deny exact human scope TTL revoke repair endpoint",()=>{
            using var f=new Fixture(output);using(var client=new Client(f,repo)){
                JsonElement Read(){var r=client.Call(EditorPostProcessWorkspace.ListCapability,new{offset=0,limit=1}).GetProperty("structuredContent");Schema(r,EditorPostProcessWorkspace.ListDescriptor.OutputSchema);return r.GetProperty("data");}
                Check(Read().GetProperty("total").GetInt32()==0);void Grant(){var r=f.Service.ReviewRead([PostProcessLibrarySelection.BloomModule]);f.Service.ApproveRead(r,r.Fingerprint,true);}var review=f.Service.ReviewRead([PostProcessLibrarySelection.BloomModule]);Reject(()=>f.Service.ApproveRead(review,review.Fingerprint,false));Grant();
                var data=Read();Check(data.GetProperty("items").GetArrayLength()==1&&data.GetProperty("items")[0].GetProperty("parameters").GetArrayLength()==5&&!data.GetRawText().Contains("assets/"));
                f.Service.RevokeRead();f.Build();f.Click("MCP 只读：Bloom",1);f.Click("审阅后处理模块读取范围");Check(f.Item("批准后处理目录读取 60 秒").Enabled==0);
                f.Click("已核对后处理模块与客户端",1);f.Click("批准后处理目录读取 60 秒");Check(Read().GetProperty("total").GetInt32()==1);
                Check(client.Call(EditorPostProcessWorkspace.ListCapability,new{limit=9}).GetProperty("isError").GetBoolean());Check(client.Call(EditorPostProcessWorkspace.ListCapability,new{source="ignored"}).GetProperty("isError").GetBoolean());
                f.Time.Now=60000;Check(Read().GetProperty("total").GetInt32()==0);Grant();var ep=f.Owner.Endpoint!;Guid id=ep.View.Connections.Single().ConnectionId;ep.Revoke(id);ep.Pair(id,true);Check(Read().GetProperty("total").GetInt32()==0);
                Grant();f.Workspace.CreateObject(f.Workspace.Stamp);Check(Read().GetProperty("total").GetInt32()==0);Grant();f.Service.RevokeRead();Check(Read().GetProperty("total").GetInt32()==0);
                Grant();f.Owner.StartPlay();Check(Read().GetProperty("total").GetInt32()==0);f.Owner.StopPlay();Grant();
            }
            f.Owner.ConfigureEndpoint(true,f.Root);Check(f.Owner.Edit!.Invoke(new(2,Guid.NewGuid(),f.Owner.Edit.SessionId,f.Owner.Edit.Revision,EditorPostProcessWorkspace.ListCapability,JsonSerializer.SerializeToElement(new{}))).Data.GetProperty("total").GetInt32()==0);
        });
        yield return("M7.4-C3 stdio original transaction exact review history stale and revoked",()=>{
            using var f=new Fixture(output);using var client=new Client(f,repo);var value=f.Value with{Enabled=true,ExposureEv=-1};var input=f.Input(value);Guid request=Guid.NewGuid();ulong oldRevision=f.Owner.Edit!.Revision;
            Check(client.Call("ncma.scene.transaction",input,request).GetProperty("isError").GetBoolean()&&!f.Value.Enabled);
            var controller=new EditorAuthorizationController(f.Workspace);var page=controller.Capture()!;var proposal=page.Proposals.Single();Check(proposal.Scope.Objects.SequenceEqual(new[]{f.Id})&&proposal.Scope.ComponentTypes.SequenceEqual(new[]{PostProcessData.TypeId}));
            controller.Approve(page,proposal.Scope.Id,proposal.Fingerprint,true,null);Check(!client.Call("ncma.scene.transaction",input,request).GetProperty("isError").GetBoolean()&&f.Value==value);
            Check(client.Call("ncma.scene.transaction",input,revision:oldRevision).GetProperty("isError").GetBoolean());Check(!client.Call("ncma.history.undo",new{}).GetProperty("isError").GetBoolean()&&!f.Value.Enabled);Check(!client.Call("ncma.history.redo",new{}).GetProperty("isError").GetBoolean()&&f.Value.Enabled);
            var ep=f.Owner.Endpoint!;var connection=ep.View.Connections.Single().ConnectionId;ep.Revoke(connection);ep.Pair(connection,true);Check(client.Call("ncma.history.undo",new{}).GetProperty("isError").GetBoolean()&&f.Value.Enabled);
        });
        yield return("M7.4-C3 owner and registry nonreentry same exact command boundary",()=>{
            using var f=new Fixture(output);Exception? failure=null;var thread=new Thread(()=>{try{f.Service.Modules.Describe();}catch(Exception e){failure=e;}});thread.Start();thread.Join();Check(failure is InvalidOperationException);
            Guid id=Guid.NewGuid();PostProcessModules? modules=null;modules=new([new(PostProcessLibrarySelection.AcesModule,1)],[new(id,1,0,[],[],(_,_,_,_)=>{modules!.Describe();return null;})]);
            using var library=RuntimePostProcessLibrary.LoadDeployment(Path.Combine(repo,"out/bin"));var settings=PostProcessData.Off with{Enabled=true,StackJson=PostProcessData.EncodeStack([new(id,1,[])])};
            Reject(()=>modules.Build(settings,16,16,_=>library.Program(PostProcessLibrarySelection.BloomModule)));Check(modules.Describe().Length==2);
            var request=f.Raw(f.Value with{Enabled=true});var wrong=request with{SessionId=Guid.NewGuid()};Check(!f.Owner.Edit!.Invoke(wrong,new(["ncma.scene.transaction"])).Changed);
            Check(!f.Owner.Edit.Invoke(request,new(["ncma.scene.transaction"],objectScope:[Guid.NewGuid()],componentScope:[PostProcessData.TypeId])).Changed);
        });
    }
}
