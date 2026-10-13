using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class EnvironmentAuthoringTests
{
    private static void Check(bool ok,string detail="") { if(!ok)throw new Exception("Environment authoring assertion: "+detail); }
    private static void Reject(Action action) { try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or OperationCanceledException){return;}throw new Exception("Invalid environment operation accepted."); }
    private static void Schema(JsonElement value,JsonElement schema)
    {
        if(schema.TryGetProperty("oneOf",out var branches)){int matches=0;foreach(var branch in branches.EnumerateArray()){try{Schema(value,branch);matches++;}catch(ArgumentException){}}if(matches!=1)throw new ArgumentException("schema oneOf");return;}
        if(schema.TryGetProperty("anyOf",out branches)){foreach(var branch in branches.EnumerateArray()){try{Schema(value,branch);return;}catch(ArgumentException){}}throw new ArgumentException("schema anyOf");}
        void Require(bool ok){if(!ok)throw new ArgumentException("schema mismatch");}
        if(schema.TryGetProperty("const",out var constant))Require(value.GetRawText()==constant.GetRawText());
        if(schema.TryGetProperty("enum",out var values))Require(values.EnumerateArray().Any(v=>v.GetRawText()==value.GetRawText()));
        if(!schema.TryGetProperty("type",out var type))return;
        switch(type.GetString()){
            case "object":Require(value.ValueKind==JsonValueKind.Object);var fields=schema.GetProperty("properties");foreach(var field in value.EnumerateObject()){Require(fields.TryGetProperty(field.Name,out var contract));Schema(field.Value,contract);}if(schema.TryGetProperty("required",out var required))foreach(var key in required.EnumerateArray())Require(value.TryGetProperty(key.GetString()!,out _));break;
            case "array":Require(value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()<=schema.GetProperty("maxItems").GetInt32());foreach(var item in value.EnumerateArray())Schema(item,schema.GetProperty("items"));break;
            case "string":Require(value.ValueKind==JsonValueKind.String);if(schema.TryGetProperty("maxLength",out var length))Require(value.GetString()!.Length<=length.GetInt32());if(schema.TryGetProperty("format",out _))Require(Guid.TryParse(value.GetString(),out _));break;
            case "integer":Require(value.ValueKind==JsonValueKind.Number&&value.TryGetUInt64(out _));if(schema.TryGetProperty("minimum",out var min))Require(value.GetDecimal()>=min.GetDecimal());if(schema.TryGetProperty("maximum",out var max))Require(value.GetDecimal()<=max.GetDecimal());break;
            case "boolean":Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False);break;
            case "null":Require(value.ValueKind==JsonValueKind.Null);break;
            default:throw new ArgumentException("unknown schema type");
        }
    }
    private sealed class Clock:TimeProvider { internal long Now;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>Now; }
    private sealed class Participant(Func<JsonElement> operations):IEditCommandSceneParticipant
    {
        internal int Prepares,Publishes,Compensations;internal Action? OnPublish,OnPrepare;
        public JsonElement SceneOperations(JsonElement input)=>operations();
        public ParticipantMemento Prepare(JsonElement input){Prepares++;OnPrepare?.Invoke();return new([0],[1]);}
        public void Authorize(ParticipantMemento m){}
        public void Validate(ParticipantMemento m,bool forward){}
        public void Publish(ParticipantMemento m,bool forward){Publishes++;OnPublish?.Invoke();}
        public void Compensate(ParticipantMemento m,bool forward){Compensations++;}
    }
    private sealed class Fixture:IDisposable
    {
        internal readonly string Root;internal readonly Guid Project=Guid.NewGuid(),Id;
        internal readonly EditorSessionOwner Owner;internal readonly EditorWorkspace Workspace;internal readonly EditorPresenter View;internal readonly Clock Time=new();
        internal EditorEnvironmentWorkspace Service=>Owner.Environments!;internal int Loads;private ulong _frame;
        internal Fixture(string output,string repository,Action<string>? fault=null) {
            Root=Path.Combine(output,"environment-author-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(Root,"assets"));
            Owner=new("Environment authoring",components:RenderComponentRegistry.CreateRegistry(),validateComposition:CandidatePresentation.RequireComposition);
            Workspace=new(Owner);Owner.ConfigureAssets(Root,Project,1);Owner.ConfigureEnvironments(Root,Project,()=>{Loads++;return EnvironmentPresetLibrary.LoadDeployment(Path.Combine(repository,"out/bin"));},Time,fault);
            Id=Workspace.CreateObject(Workspace.Stamp);View=new(Workspace,null,Root,workspaceStyle:true);Build();
        }
        internal void Build()=>View.Build(++_frame,1280,720);
        internal void Click(string label,double value=0){var i=View.Items.ToArray().Single(i=>Encoding.UTF8.GetString(View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))==label);var f=View.Frame;
            View.Apply([new GuiEvent{Kind=i.Kind,WidgetHigh=i.WidgetHigh,WidgetLow=i.WidgetLow,Phase=3,Value=value,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);Build();}
        internal EnvironmentPresetReview Review()=>Service.ReviewNeutral(Workspace.Stamp,Id);
        internal Guid Approve(EnvironmentPresetReview r)=>Service.Approve(r,r.Fingerprint,true);
        internal string FilePath(EnvironmentPresetReview r)=>Path.Combine(Root,r.Path);
        internal EnvironmentLightingData Value=>Owner.Document.World.FindObject(Id).Get<EnvironmentLightingData>();
        internal CapabilityResult Read(object? input=null)=>Owner.Edit!.Invoke(new(2,Guid.NewGuid(),Owner.Edit.SessionId,Owner.Edit.Revision,EditorEnvironmentWorkspace.ListCapability,JsonSerializer.SerializeToElement(input??new{})));
        public void Dispose()=>Owner.Dispose();
    }
    private sealed class Client:IDisposable
    {
        private readonly Fixture _f;private readonly Process _process;private readonly Task<string> _errors;private int _id;
        internal Client(Fixture f,string repository){_f=f;f.Owner.ConfigureEndpoint(true,f.Root);var p=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            p.ArgumentList.Add(Path.Combine(repository,"out/managed/editor-mcp/Ncma.Editor.Mcp.dll"));p.ArgumentList.Add("--descriptor");p.ArgumentList.Add(f.Owner.Endpoint!.DescriptorPath);_process=Process.Start(p)!;_errors=_process.StandardError.ReadToEndAsync();
            Send(new{jsonrpc="2.0",id=++_id,method="initialize",@params=new{protocolVersion="2025-11-25"}});_process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            var list=Send(new{jsonrpc="2.0",id=++_id,method="tools/list"}).GetProperty("result").GetProperty("tools");Check(list.EnumerateArray().Any(t=>t.GetProperty("name").GetString()==EditorEnvironmentWorkspace.ListCapability));}
        private JsonElement Send(object data){_process.StandardInput.WriteLine(JsonSerializer.Serialize(data,Wire.Json));_process.StandardInput.Flush();var line=_process.StandardOutput.ReadLineAsync();var watch=Stopwatch.StartNew();while(!line.IsCompleted){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException();var ep=_f.Owner.Endpoint!;foreach(var c in ep.View.Connections.Where(c=>c.Connected&&!c.Paired))ep.Pair(c.ConnectionId,true);ep.Pump();Thread.Sleep(1);}using var parsed=JsonDocument.Parse(line.GetAwaiter().GetResult()??throw new IOException("MCP closed"));return parsed.RootElement.Clone();}
        internal JsonElement Call(string capability,object input,Guid? request=null)=>Send(new{jsonrpc="2.0",id=++_id,method="tools/call",@params=new{name=capability,arguments=new{contractVersion=2,requestId=request??Guid.NewGuid(),sessionId=_f.Owner.Edit!.SessionId,expectedRevision=_f.Owner.Edit.Revision,input}}}).GetProperty("result");
        public void Dispose(){_process.StandardInput.Close();Check(_process.WaitForExit(5000));Check(_process.ExitCode==0&&_errors.GetAwaiter().GetResult()=="");_process.Dispose();}
    }
    internal static IEnumerable<(string,Action)> Cases(string output,string repository)
    {
        yield return("M7.3-C3-B2 Core scene participant validates before durable preparation and compensates late revoke",()=>{
            var doc=new SceneDocument();Guid id=doc.World.CreateObject("Before").PersistentId;var edit=new EditSession(doc);bool allowed=true;
            var p=new Participant(()=>JsonSerializer.SerializeToElement(new[]{new{op="rename",objectId=id,name="After"}}));edit.RegisterCommandParticipant(EditorEnvironmentWorkspace.ApplyDescriptor,p);
            CapabilityRequest Request()=>new(2,Guid.NewGuid(),edit.SessionId,edit.Revision,EditorEnvironmentWorkspace.ApplyCapability,JsonSerializer.SerializeToElement(new{}));
            Check(!edit.Invoke(Request(),new([EditorEnvironmentWorkspace.ApplyCapability],objectScope:[])).Changed&&p.Prepares==0);
            var impact=edit.DescribeRequestImpact(Request());Check(impact.Impact.Objects.SequenceEqual(new[]{id})&&p.Prepares==0);
            p.OnPrepare=()=>{doc.World.FindObject(id).Name="illegal";};Check(!edit.Invoke(Request(),new([EditorEnvironmentWorkspace.ApplyCapability])).Changed&&doc.World.FindObject(id).Name=="Before"&&p.Publishes==0);
            p.OnPrepare=null;p.OnPublish=()=>allowed=false;var grant=new CapabilityPermissions([EditorEnvironmentWorkspace.ApplyCapability],objectScope:[id],isCurrent:()=>allowed);
            Check(!edit.Invoke(Request(),grant).Changed&&p.Compensations==1&&doc.World.FindObject(id).Name=="Before"&&!edit.State.Frozen);
            p.OnPublish=null;allowed=true;Check(edit.Invoke(Request(),grant).Changed&&doc.World.FindObject(id).Name=="After");
        });
        yield return("M7.3-C3-B2 Core no-op requested scope retained and malformed scene operations rejected",()=>{
            foreach(bool malformed in new[]{false,true}){var doc=new SceneDocument();Guid id=doc.World.CreateObject("Before").PersistentId;var edit=new EditSession(doc);
                var p=new Participant(()=>malformed?JsonSerializer.SerializeToElement(Array.Empty<object>()):JsonSerializer.SerializeToElement(new[]{new{op="rename",objectId=id,name="Before"}}));edit.RegisterCommandParticipant(EditorEnvironmentWorkspace.ApplyDescriptor,p);
                var request=new CapabilityRequest(2,Guid.NewGuid(),edit.SessionId,edit.Revision,EditorEnvironmentWorkspace.ApplyCapability,JsonSerializer.SerializeToElement(new{}));
                if(malformed){Check(!edit.Invoke(request,new([EditorEnvironmentWorkspace.ApplyCapability])).Changed&&p.Prepares==0);continue;}
                Check(edit.DescribeRequestImpact(request).Impact.Objects.SequenceEqual(new[]{id}));Check(edit.Invoke(request,new([EditorEnvironmentWorkspace.ApplyCapability],objectScope:[id])).Changed);
                var undo=new CapabilityRequest(2,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.history.undo",JsonSerializer.SerializeToElement(new{}));Check(!edit.Invoke(undo,new(["ncma.history.undo",EditorEnvironmentWorkspace.ApplyCapability],objectScope:[])).Changed);
            }
        });
        yield return("M7.3-C3-B2 bounded 17 inspections and failed paired registration leaves no partial capability",()=>{
            var edit=new EditSession(new SceneDocument());var schema=JsonSerializer.SerializeToElement(new{type="object"});for(int i=0;i<EditSession.MaxRegisteredInspections;i++)edit.RegisterInspection(new("test."+i,"test",MutationRisk.ReadOnly,schema,schema),_=>new{});
            var participant=new Participant(()=>JsonSerializer.SerializeToElement(Array.Empty<object>()));Reject(()=>edit.RegisterCommandParticipant(EditorEnvironmentWorkspace.ApplyDescriptor,participant,(EditorEnvironmentWorkspace.ListDescriptor,_=>new{})));
            Check(!edit.Describe().Any(d=>d.Name==EditorEnvironmentWorkspace.ApplyCapability));Reject(()=>edit.RegisterInspection(EditorEnvironmentWorkspace.ListDescriptor,_=>new{}));
        });
        yield return("M7.3-C3-B2 clone owned numerical payload and lazy canceled review",()=>{
            using var f=new Fixture(output,repository);Check(f.Loads==0&&!Directory.Exists(Path.Combine(f.Root,"out/assets")));
            using var library=EnvironmentPresetLibrary.LoadDeployment(Path.Combine(repository,"out/bin"));byte[] before=library.Package.CopyBytes();var clone=library.Package.CloneForAsset(Guid.NewGuid(),2);
            Check(clone.Generation==2&&clone.SourceId==library.Package.SourceId&&clone.ContentHash!=library.Package.ContentHash&&!clone.GpuValidated);
            Check(clone.CopyBytes().AsSpan(160).SequenceEqual(before.AsSpan(160))&&library.Package.CopyBytes().SequenceEqual(before));Reject(()=>library.Package.CloneForAsset(Guid.Empty,1));Reject(()=>library.Package.CloneForAsset(library.Package.SourceId,1));Reject(()=>library.Package.CloneForAsset(Guid.NewGuid(),0));
            var r=f.Review();Check(r.Synthetic&&r.AssetId!=library.Package.AssetId);f.Service.CancelReview();Reject(()=>f.Approve(r));Check(!File.Exists(f.FilePath(r))&&!Directory.Exists(Path.Combine(f.Root,"out/assets")));
            for(int i=0;i<40;i++){f.Review();f.Service.CancelReview();}Check(!Directory.Exists(Path.Combine(f.Root,"out/assets")),"cancel does not exhaust retained generation budget");
        });
        yield return("M7.3-C3-B2 one history publishes scene and descriptor; pinned NCE survives Undo Redo",()=>{
            using var f=new Fixture(output,repository);byte[] before=f.Owner.Document.CaptureBytes();int history=f.Owner.Edit!.State.UndoCount;var r=f.Review();Guid token=f.Approve(r);Check(!File.Exists(f.FilePath(r)));
            Check(f.Service.ApplyLocal(token).Changed&&f.Owner.Edit.State.UndoCount==history+1&&f.Value.AssetId==r.AssetId);var d=EnvironmentAssetDescriptor.Decode(File.ReadAllBytes(f.FilePath(r)));string nce=Path.Combine(f.Root,d.Path);
            byte[] after=f.Owner.Document.CaptureBytes();using(var prepared=SceneAssetPreparation.Prepare(f.Root,f.Project,f.Owner.Document.CaptureSnapshot(),false)){
                Reject(()=>{using var writer=new FileStream(nce,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);});
                f.Workspace.History(f.Workspace.Stamp,false);Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())&&!File.Exists(f.FilePath(r))&&File.Exists(nce));
                f.Workspace.History(f.Workspace.Stamp,true);Check(after.SequenceEqual(f.Owner.Document.CaptureBytes())&&File.Exists(f.FilePath(r)));}
            f.Service.Synchronize();Check(f.Service.Rows().Single().State=="registered");string scene=Path.Combine(f.Root,"saved.ncmascene");f.Workspace.Save(f.Workspace.Stamp,scene);
            using var reopened=new EditorSessionOwner("reopened",components:RenderComponentRegistry.CreateRegistry(),validateComposition:CandidatePresentation.RequireComposition);new EditorWorkspace(reopened).Open(new EditorWorkspace(reopened).Stamp,scene,true);Check(reopened.Document.World.FindObject(f.Id).Get<EnvironmentLightingData>()==f.Value);
        });
        yield return("M7.3-C3-B2 exact scope default deny unknown input stale proposal and original history",()=>{
            using var f=new Fixture(output,repository);var r=f.Review();Guid token=f.Approve(r);var edit=f.Owner.Edit!;var request=f.Service.Request(token);byte[] before=f.Owner.Document.CaptureBytes();
            Check(edit.Invoke(request).Status=="denied");Check(!edit.Invoke(request,new([EditorEnvironmentWorkspace.ApplyCapability],objectScope:[Guid.NewGuid()],componentScope:[EnvironmentLightingData.TypeId])).Changed);
            Check(!edit.Invoke(request,new([EditorEnvironmentWorkspace.ApplyCapability],objectScope:[f.Id],componentScope:[])).Changed);
            Check(!File.Exists(f.FilePath(r))&&before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            var grant=new CapabilityPermissions([EditorEnvironmentWorkspace.ApplyCapability,"ncma.history.undo","ncma.history.redo"],objectScope:[f.Id],componentScope:[EnvironmentLightingData.TypeId]);
            Check(!edit.Invoke(request with{Input=JsonSerializer.SerializeToElement(new{proposalId=token,expectedAssetRevision=0,path="assets/injected.ncenv"})},grant).Changed);
            Check(edit.Invoke(request,grant).Changed);Check(!edit.Invoke(request with{RequestId=Guid.NewGuid()},grant).Changed);
            var undo=new CapabilityRequest(2,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.history.undo",JsonSerializer.SerializeToElement(new{}));Check(!edit.Invoke(undo,new(["ncma.history.undo"])).Changed);Check(edit.Invoke(undo,grant).Changed);
        });
        yield return("M7.3-C3-B2 review tamper expiry asset revision scene change Play and owner rejection",()=>{
            using var f=new Fixture(output,repository);var r=f.Review();Reject(()=>f.Service.Approve(r,r.Fingerprint,false));Reject(()=>f.Service.Approve(r with{ObjectId=Guid.NewGuid()},r.Fingerprint,true));f.Time.Now=60000;Reject(()=>f.Approve(r));
            r=f.Review();f.Owner.Assets!.Clock.Advance();Reject(()=>f.Approve(r));r=f.Review();f.Workspace.CreateObject(f.Workspace.Stamp);Reject(()=>f.Approve(r));
            r=f.Review();Guid token=f.Approve(r);f.Time.Now+=60000;Reject(()=>f.Service.ApplyLocal(token));
            f.Owner.RefreshAssets(true);f.Owner.StartPlay();Reject(()=>f.Review());f.Owner.StopPlay();Task.Run(()=>Reject(()=>f.Service.Rows())).GetAwaiter().GetResult();Check(!File.Exists(f.FilePath(r)));
        });
        yield return("M7.3-C3-B2 publication faults compensate files and scene without history",()=>{
            foreach(string point in new[]{"journal_created","backup:0","published:0","metadata_published"}){
                bool fire=true;using var f=new Fixture(output,repository,p=>{if(fire&&p==point){fire=false;throw new IOException("injected "+point);}});var r=f.Review();var token=f.Approve(r);byte[] before=f.Owner.Document.CaptureBytes();int history=f.Owner.Edit!.State.UndoCount;
                Reject(()=>f.Service.ApplyLocal(token));Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())&&f.Owner.Edit.State.UndoCount==history&&!f.Owner.Edit.State.Frozen&&!File.Exists(f.FilePath(r))&&!File.Exists(f.FilePath(r)+".journal"),point);
                r=f.Review();Check(f.Service.ApplyLocal(f.Approve(r)).Changed,"retry "+point);
            }
        });
        yield return("M7.3-C3-B2 final decision fault freezes and exact durable recovery preserves commit",()=>{
            string root;Guid project;EnvironmentPresetReview review;
            using(var f=new Fixture(output,repository,p=>{if(p=="commit_decided")throw new IOException("commit decision crash");})){
                review=f.Review();root=f.Root;project=f.Project;Reject(()=>f.Service.ApplyLocal(f.Approve(review)));Check(f.Owner.Edit!.State.Frozen&&f.Value.AssetId==review.AssetId&&File.Exists(f.FilePath(review)+".journal"));}
            using var recovery=new EnvironmentPublication(new(root),project,new(),(path,d)=>path==review.Path&&d.AssetId==review.AssetId&&d.ContentHash==review.ContentHash);
            recovery.Recover(review.Path);Check(File.Exists(Path.Combine(root,review.Path))&&!File.Exists(Path.Combine(root,review.Path+".journal")));
        });
        yield return("M7.3-C3-B2 cancellation generation fault scope foreign descriptor and duplicate identity",()=>{
            using var f=new Fixture(output,repository);using var library=EnvironmentPresetLibrary.LoadDeployment(Path.Combine(repository,"out/bin"));var package=library.Package.CloneForAsset(Guid.NewGuid(),1);var paths=new AssetProjectPaths(f.Root);
            using var store=new EnvironmentDerivedStore(paths,f.Project);using var canceled=new CancellationTokenSource();canceled.Cancel();Reject(()=>store.Prepare(package,canceled.Token));
            Reject(()=>store.Prepare(package,fault:p=>{if(p=="environment_generation_written")throw new IOException("write fixture");}));var descriptor=store.Prepare(package);Check(EnvironmentAuthoringFiles.ReadPackage(paths,descriptor).ContentHash==package.ContentHash);
            string file="assets/existing.ncenv";File.WriteAllBytes(Path.Combine(f.Root,file),descriptor.Encode());File.WriteAllBytes(Path.Combine(f.Root,"assets/duplicate.ncenv"),descriptor.Encode());Reject(()=>EnvironmentAuthoringCatalog.Scan(paths,f.Project));
            using var publication=new EnvironmentPublication(paths,f.Project,new(),(_,_)=>false);Reject(()=>publication.Prepare(file,descriptor,0));
            using var foreign=new EnvironmentPublication(paths,Guid.NewGuid(),new(),(_,_)=>true);Reject(()=>foreign.Prepare(file,descriptor,0));
        });
        yield return("M7.3-C3-B2 UI cancel explicit confirmation apply single Undo and frozen controls",()=>{
            using var f=new Fixture(output,repository);int history=f.Owner.Edit!.State.UndoCount;f.Click("选择合成中性预设…");Check(f.Service.Review is not null);f.Click("取消预设选择");Check(f.Service.Review is null&&f.Owner.Edit.State.UndoCount==history);
            f.Click("选择合成中性预设…");f.Click("已核对目标、资源和文件发布范围",1);f.Click("批准并应用预设（一次撤销）");Check(f.Owner.Edit.State.UndoCount==history+1&&f.Value.Enabled);
            f.Workspace.History(f.Workspace.Stamp,false);Check(!f.Owner.Document.World.FindObject(f.Id).Has<EnvironmentLightingData>());f.Workspace.History(f.Workspace.Stamp,true);
            f.Owner.RefreshAssets(true);f.Owner.StartPlay();f.Build();var before=f.Owner.Document.CaptureBytes();f.Click("选择合成中性预设…");Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));f.Owner.StopPlay();
        });
        yield return("M7.3-C3-B2 real stdio bounded metadata default deny no file IO TTL audience epoch",()=>{
            using var f=new Fixture(output,repository);using var client=new Client(f,repository);var r=f.Review();f.Service.ApplyLocal(f.Approve(r));f.Service.Synchronize();
            JsonElement Read(){var result=client.Call(EditorEnvironmentWorkspace.ListCapability,new{offset=0,limit=1}).GetProperty("structuredContent");Schema(result,EditorEnvironmentWorkspace.ListDescriptor.OutputSchema);return result.GetProperty("data");}
            Check(Read().GetProperty("total").GetInt32()==0);void Grant(){var review=f.Service.ReviewRead([r.AssetId]);f.Service.ApproveRead(review,review.Fingerprint,true);}Grant();
            using(var locked=new FileStream(f.FilePath(r),FileMode.Open,FileAccess.Read,FileShare.None)){var read=Read();Check(read.GetProperty("items").GetArrayLength()==1&&!read.GetRawText().Contains("assets/")&&!read.GetRawText().Contains("source"));}
            Check(f.Read(new{limit=33}).Status=="error");f.Time.Now=60000;Check(Read().GetProperty("total").GetInt32()==0);Grant();var ep=f.Owner.Endpoint!;Guid connection=ep.View.Connections.Single().ConnectionId;ep.Revoke(connection);ep.Pair(connection,true);Check(Read().GetProperty("total").GetInt32()==0);
            Grant();f.Workspace.CreateObject(f.Workspace.Stamp);Check(Read().GetProperty("total").GetInt32()==0);Grant();f.Service.RevokeRead();Check(Read().GetProperty("total").GetInt32()==0);
        });
        yield return("M7.3-C3-B2 real stdio prepared UUID proposal exact review apply and history",()=>{
            using var f=new Fixture(output,repository);using var client=new Client(f,repository);var r=f.Review();Guid token=f.Approve(r);object input=new{proposalId=token,expectedAssetRevision=f.Owner.Assets!.Clock.Revision};
            Guid request=Guid.NewGuid();var denied=client.Call(EditorEnvironmentWorkspace.ApplyCapability,input,request);Check(denied.GetProperty("isError").GetBoolean()&&!File.Exists(f.FilePath(r)));
            var controller=new EditorAuthorizationController(f.Workspace);var page=controller.Capture()!;var proposal=page.Proposals.Single();Check(proposal.Scope.Objects.SequenceEqual(new[]{f.Id})&&proposal.Scope.ComponentTypes.SequenceEqual(new[]{EnvironmentLightingData.TypeId}));
            controller.Approve(page,proposal.Scope.Id,proposal.Fingerprint,true,null);var applied=client.Call(EditorEnvironmentWorkspace.ApplyCapability,input,request);Check(!applied.GetProperty("isError").GetBoolean()&&f.Value.AssetId==r.AssetId);
            Check(!client.Call("ncma.history.undo",new{}).GetProperty("isError").GetBoolean()&&!File.Exists(f.FilePath(r)));
        });
    }
}
