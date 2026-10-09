using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Pipes;
using Ncma.Editor.Protocol;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Physics;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Assets.Authoring;

static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException) { return; } throw new Exception("Expected rejection."); }
static GuiEvent Event(EditorPresenter view, ulong id, uint phase = 3, double number = 0, int textBytes = 0, ulong domain = 2) {
    var item = view.Items.ToArray().Single(i => i.WidgetHigh == domain && i.WidgetLow == id);
    var frame = view.Frame;
    return new() { Kind = item.Kind, WidgetHigh = domain, WidgetLow = id, Phase = phase, Value = number,
        Frame = frame.Frame, ViewGeneration = frame.ViewGeneration, DocumentGeneration = frame.DocumentGeneration,
        Revision = frame.Revision, TextLength = (uint)textBytes };
}
string root = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(Path.Combine(root,"out/verification/m2/editor-services",Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(output);
static void Compare(JsonElement a,JsonElement b) {
    Check(a.ValueKind==b.ValueKind);
    switch(a.ValueKind) {
        case JsonValueKind.Object:
            foreach(var p in a.EnumerateObject()) Compare(p.Value,b.GetProperty(p.Name));
            Check(a.EnumerateObject().Count()==b.EnumerateObject().Count());break;
        case JsonValueKind.Array:
            Check(a.GetArrayLength()==b.GetArrayLength());
            for(int i=0;i<a.GetArrayLength();i++)Compare(a[i],b[i]);break;
        case JsonValueKind.Number:Check(Math.Abs(a.GetDouble()-b.GetDouble())<.00002);break;
        default:Check(a.GetRawText()==b.GetRawText());break;
    }
}
void AssertSceneSequence(JsonElement expected,string baseline,string saved) {
        Guid id=Guid.Parse("11111111-1111-1111-1111-111111111111");
        using var owner=new EditorSessionOwner("Parity");var ui=new EditorWorkspace(owner);
        ui.Open(ui.Stamp,baseline,true);ui.Select(ui.Stamp,id);
        int index=0;
        void Assert() {
            var state=owner.Edit!.State;
            using var document=JsonDocument.Parse(owner.Document.CaptureBytes());
            var actual=JsonSerializer.SerializeToElement(new {document=document.RootElement,revision=state.Revision,undo=state.UndoCount,redo=state.RedoCount,
                dirty=state.Dirty,busy=state.EditBusy,invalidated=state.HistoryInvalidated,frozen=state.Frozen,selection=state.Selection});
            Compare(expected[index++],actual);
        }
        Assert();ui.Transaction(ui.Stamp,"Rename",[new {op="rename",objectId=id,name="Renamed"}],id);Assert();
        ui.BeginDraft(ui.Stamp,id,"Discard draft");ui.UpdateDraft(ui.Stamp,id,new {op="rename",objectId=id,name="Discard me"});Assert();
        ui.CancelDraft();Assert();
        ui.Transaction(ui.Stamp,"Transform",[new {op="set_component",objectId=id,typeId="ncma.transform",version=1,
            data=owner.Document.World.Components.Encode(new TransformData(new(2,3,4),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One))}],id);Assert();
        ui.History(ui.Stamp,false);Assert();ui.History(ui.Stamp,true);Assert();
        ui.Delete(ui.Stamp,id,id);Assert();ui.History(ui.Stamp,false);Assert();
        ui.Save(ui.Stamp,saved);Assert();
        ui.New(ui.Stamp,false);Assert();ui.History(ui.Stamp,false);Assert();
        Check(index==expected.GetArrayLength());
}
void AssertActionSequence(JsonElement expected,Ncma.ActionAnimationSession animation) {
        var commands=new (uint Command,double Value,string Text)[] {
            (1,1,""),(4,.1,""),(2,0,"Attack"),(4,.2,""),(4,.25,""),(2,0,"Attack"),
            (6,0,""),(7,0,""),(4,1,""),(5,0,""),(2,0,"Dodge"),(4,.1,""),(4,.8,""),(6,0,""),(7,0,"")
        };
        void Assert(int i) {using var actual=JsonDocument.Parse(animation.InspectJson());Compare(expected[i],actual.RootElement);}
        Assert(0);int index=1;
        foreach(var command in commands) {animation.Execute(command.Command,command.Value,command.Text);Assert(index++);}
        Check(index==expected.GetArrayLength());
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference ProbeManagedCatalog(string path) {
    using var catalog=new Ncma.Scripting.ScriptCatalogService();
    using var candidate=catalog.LoadCandidate(path);Check(candidate.Count==1);
    var weak=candidate.ContextReference;catalog.CommitCandidate(candidate);
    var type=catalog.Snapshot.Types.Single();
    var binding=new BehaviourBindingData(Guid.NewGuid(),type.TypeName,true,
        type.Exports.Select(e=>new ExportData(e.Name,(ExportKind)e.Kind,e.DefaultValue)).ToArray());
    var instance=catalog.Instantiate(binding);
    Check(instance.GetType().BaseType==typeof(Ncma.Behaviour) && type.Exports.Length==3);
    using(var lease=catalog.AcquireLease())Reject(catalog.Clear);
    catalog.Clear();return weak;
}
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference CurrentCatalogContext(Ncma.Scripting.ScriptCatalogService catalog,BehaviourBindingData binding) {
    var instance=catalog.Instantiate(binding);
    return new(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(instance.GetType().Assembly)!);
}
var cases = AssetInspectionTests.Cases(output, root).Concat(AssetAuthorizationTests.Cases(output, root, args[1])).Concat(EditorPrefabTests.Cases()).Concat(GraphInspectionTests.Cases(output,root)).Concat(GraphAuthoringTests.Cases(output,root,args[1])).Concat(GraphSequenceTests.Cases(output,root)).Concat(BlendSpaceAuthoringTests.Cases(output,root,args[1])).Concat(LayerAuthoringTests.Cases(output,root,args[1])).Concat(MontageAuthoringTests.Cases(output,root,args[1])).Concat(MontageSequenceTests.Cases(output,root,args[1])).Concat(WorkflowTests.Cases(output,root,args[1])).Concat(new (string, Action)[] {
    ("Direct catalog exported values, candidate ownership and failed load preserve metadata", () => {
        string path=Path.Combine(root,"out/managed/Ncma.Gameplay.Sample.dll");
        using var catalog=new Ncma.Scripting.ScriptCatalogService();using var other=new Ncma.Scripting.ScriptCatalogService();
        using(var prepared=catalog.LoadCandidate(path)) {
            Check(catalog.Snapshot.Types.Length==0 && prepared.Count==1);
            Reject(()=>other.CommitCandidate(prepared));Check(catalog.Snapshot.Types.Length==0);
            catalog.CommitCandidate(prepared);Reject(()=>_ = prepared.Count);
        }
        var snapshot=catalog.Snapshot;var type=snapshot.Types.Single();
        var binding=new BehaviourBindingData(Guid.NewGuid(),type.TypeName,true,[
            new("DegreesPerSecond",ExportKind.Float,90),new("Multiplier",ExportKind.Integer,2),new("Clockwise",ExportKind.Boolean,0)]);
        var instance=catalog.Instantiate(binding);var reflected=instance.GetType();
        Check(ReferenceEquals(reflected.BaseType,typeof(Ncma.Behaviour)));
        Check((float)reflected.GetProperty("DegreesPerSecond")!.GetValue(instance)! ==90 &&
              (int)reflected.GetField("Multiplier")!.GetValue(instance)! ==2 &&
              !(bool)reflected.GetProperty("Clockwise")!.GetValue(instance)!);
        foreach(var bad in new[]{new ExportData("Clockwise",ExportKind.Boolean,2),new("Multiplier",ExportKind.Integer,1.5),
                                  new("DegreesPerSecond",ExportKind.Float,double.MaxValue),new("Missing",ExportKind.Double,1),
                                  new("DegreesPerSecond",ExportKind.Double,90)})
            Reject(()=>catalog.Instantiate(binding with{Exports=[bad]}));
        Reject(()=>catalog.Instantiate(binding with{TypeName="Missing.Type"}));
        Reject(()=>catalog.LoadCandidate(path+".missing"));
        Check(catalog.Snapshot.Generation==snapshot.Generation && catalog.Snapshot.Types.Single().Exports.Length==3);
        type.Exports[0]=type.Exports[0] with{DefaultValue=12345};
        Check(catalog.Snapshot.Types.Single().Exports[0].DefaultValue!=12345);
        using(var lease=catalog.AcquireLease()) {
            Reject(catalog.Clear);Check(catalog.Snapshot.Generation==snapshot.Generation);
            Check(Task.Run(()=>{try{_ = catalog.Snapshot;return false;}catch(InvalidOperationException){return true;}}).Result);
        }
        catalog.Clear();Check(catalog.Snapshot.Types.Length==0);
    }),
    ("Direct sample attachment/Exports and 12 active reload unloads retain World and rotate input epoch", () => {
        string path=Path.Combine(root,"out/managed/Ncma.Gameplay.Sample.dll");
        using var owner=new EditorSessionOwner("Direct host semantics",activateEditor:false);
        var document=owner.Document;owner.LoadGameplay(path);var type=owner.Catalog.Snapshot.Types.Single();
        Guid fast=document.World.CreateObject("Fast").PersistentId,slow=document.World.CreateObject("Slow").PersistentId,
             disabled=document.World.CreateObject("Disabled").PersistentId,unattached=document.World.CreateObject("No script").PersistentId,
             logic=document.World.CreateObject("Non-spatial disabled").PersistentId;
        foreach(var id in new[]{fast,slow,disabled,unattached})document.World.FindObject(id).Set(TransformData.Identity);
        BehaviourBindingData Binding(float degrees,int multiplier=1,bool clockwise=true,bool enabled=true)=>new(Guid.NewGuid(),type.TypeName,enabled,[
            new("DegreesPerSecond",ExportKind.Float,degrees),new("Multiplier",ExportKind.Integer,multiplier),new("Clockwise",ExportKind.Boolean,clockwise?1:0)]);
        document.SetBindings(fast,[Binding(90,2,false)]);document.SetBindings(slow,[Binding(30)]);
        document.SetBindings(disabled,[Binding(45,enabled:false)]);document.SetBindings(logic,[Binding(45,enabled:false)]);
        owner.ActivateEditor();byte[] editBefore=document.CaptureBytes();var play=owner.StartPlay();
        for(int i=0;i<30;i++)play.AdvanceFrame(1.0/60);
        var runtime=play.Document.World;
        Check(Math.Abs(runtime.FindObject(fast).Get<TransformData>().Rotation.Y+Math.Sqrt(.5))<.0001);
        Check(Math.Abs(runtime.FindObject(slow).Get<TransformData>().Rotation.Y-Math.Sin(Math.PI/24))<.0001);
        Check(runtime.FindObject(disabled).Get<TransformData>()==TransformData.Identity &&
              runtime.FindObject(unattached).Get<TransformData>()==TransformData.Identity &&
              !runtime.FindObject(logic).Has<TransformData>());
        Check(editBefore.SequenceEqual(document.CaptureBytes()) && play.Tick==30);
        byte[] committed=play.Document.CaptureBytes();var reference=runtime.FindObject(fast).Reference;
        for(int cycle=0;cycle<12;cycle++) {
            var epoch=play.SessionId;var generation=owner.Catalog.Snapshot.Generation;ulong tick=play.Tick;
            var weak=CurrentCatalogContext(owner.Catalog,document.GetBindings(fast).Single());
            Reject(()=>owner.ReloadGameplay(path+".missing"));
            Check(play.State==Ncma.Gameplay.PlayState.Paused && play.SessionId==epoch &&
                  owner.Catalog.Snapshot.Generation==generation && committed.SequenceEqual(play.Document.CaptureBytes()));
            Check(owner.ReloadGameplay(path)==1);
            Check(play.State==Ncma.Gameplay.PlayState.Paused && play.SessionId!=epoch && play.Tick==tick &&
                  runtime.FindObject(fast).Reference==reference && committed.SequenceEqual(play.Document.CaptureBytes()));
            Reject(()=>play.SubmitInput(new(epoch,1,true,new ulong[8],new ulong[8],new ulong[8],0,0)));
            using(var lease=owner.Catalog.AcquireLease())Reject(owner.Catalog.Clear);
            for(int pass=0;pass<10 && weak.IsAlive;pass++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
            Check(!weak.IsAlive);play.Resume();play.AdvanceFrame(0);Check(play.Tick==tick);
        }
        owner.StopPlay();Check(!owner.Edit!.State.Frozen && editBefore.SequenceEqual(document.CaptureBytes()));
    }),
    ("Direct catalog SDK identity/12 unloads and 64 isolated Play lifecycle cycles", () => {
        string assembly=Path.Combine(root,"out/managed/Ncma.Gameplay.Sample.dll");
        for(int cycle=0;cycle<12;cycle++) {
            var weak=ProbeManagedCatalog(assembly);
            for(int pass=0;pass<10 && weak.IsAlive;pass++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
            Check(!weak.IsAlive);
        }
        using var owner=new EditorSessionOwner("Lifecycle");var ui=new EditorWorkspace(owner);owner.LoadGameplay(assembly);
        Guid id=ui.CreateObject(ui.Stamp);var type=owner.Catalog.Snapshot.Types.Single();
        ui.Transaction(ui.Stamp,"Fixture",[
            new {op="set_component",objectId=id,typeId="ncma.transform",version=1,data=owner.Document.World.Components.Encode(TransformData.Identity)},
            new {op="add_binding",objectId=id,binding=new {id=Guid.NewGuid(),typeName=type.TypeName,enabled=true,exports=type.Exports.Select(e=>new {name=e.Name,kind=e.Kind,value=e.DefaultValue}).ToArray()}}],id);
        byte[] before=owner.Document.CaptureBytes();
        for(int cycle=0;cycle<64;cycle++) {
            ui.PlayControl(ui.Stamp,"start");owner.Play!.AdvanceFrame(1.0/60);
            ui.PlayControl(ui.Stamp,"pause");ui.PlayControl(ui.Stamp,"step");
            Check(owner.Play.Tick==2);ui.PlayControl(ui.Stamp,"stop");
            Check(owner.Play is null && !owner.Edit!.State.Frozen && before.SequenceEqual(owner.Document.CaptureBytes()));
        }
    }),
    ("M2.8 bounded current snapshot/cached-list measurement evidence", () => {
        var rows=new List<object>();
        foreach(int count in new[]{0,256,4096}) {
            using var owner=new EditorSessionOwner("Measurement",activateEditor:false);
            for(int i=0;i<count;i++)owner.Document.World.CreateObject("Object"+i).Set(TransformData.Identity);
            owner.ActivateEditor();var ui=new EditorWorkspace(owner);
            foreach(string workload in new[]{"snapshot_copy","cached_list_32"}) {
                void Run() {if(workload=="snapshot_copy")_ = owner.Document.CaptureBytes();else _ = ui.Capture();}
                for(int i=0;i<8;i++)Run();
                var times=new double[32];var allocations=new long[32];
                int[] collections=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
                for(int i=0;i<32;i++) {
                    long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();Run();
                    times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations[i]=GC.GetAllocatedBytesForCurrentThread()-allocated;
                }
                Array.Sort(times);Array.Sort(allocations);
                rows.Add(new {workload,objects=count,warmup=8,samples=32,medianMs=(times[15]+times[16])/2,p95Ms=times[30],maxMs=times[31],
                    medianThreadBytes=(allocations[15]+allocations[16])/2,maxThreadBytes=allocations[31],
                    gcCollections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-collections[i]).ToArray()});
            }
        }
        string path=Path.Combine(output,"performance.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new {schemaVersion=1,scope="current_managed_document_and_cached_list_only",oldBaselineComparison=false,frameRateAcceptance=false,rows}));
        Console.WriteLine("M2.8 measurements: "+path);
    }),
    ("Frozen scene reference uses direct managed commands without old bridge process", () => {
        string baseline=Path.Combine(output,"frozen.ncmascene");
        var document=new SceneDocument("Baseline");
        document.World.CreateObject("Original",Guid.Parse("11111111-1111-1111-1111-111111111111")).Set(TransformData.Identity);
        SceneDocumentFiles.Save(document,baseline);
        using var reference=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"tests/assets/m2/scene-command-reference.json")));
        AssertSceneSequence(reference.RootElement,baseline,Path.Combine(output,"frozen.saved.ncmascene"));
    }),
    ("Frozen action reference uses managed policy and numerical ABI 2 only", () => {
        using var reference=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"tests/assets/m2/action-command-reference.json")));
        using var animation=new Ncma.ActionAnimationSession(Path.Combine(root,"out/managed/NcmaNative.dll"));
        AssertActionSequence(reference.RootElement,animation);
    }),
    ("Local preferences strict atomic persistence, independent Undo and failed-write preservation", () => {
        string path=Path.Combine(output,"preferences.json");var store=new EditorPreferencesStore(path);var initial=store.Current;
        store.Save(0,initial with {Theme="Light",SideWidth=350});Check(store.Revision==1 && new EditorPreferencesStore(path).Current.Theme=="Light");
        store.History(1,false);Check(store.Current==initial);store.History(2,true);Check(store.Current.SideWidth==350);
        var before=File.ReadAllBytes(path);ulong revision=store.Revision;Reject(()=>store.Save(revision,initial with {Theme="Unknown"}));Reject(()=>store.Save(0,initial));
        using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))Reject(()=>store.Save(revision,initial with {Theme="Classic"}));
        Check(store.Revision==revision && before.SequenceEqual(File.ReadAllBytes(path)));
        Check(Task.Run(()=>{try{store.Save(store.Revision,initial);return false;}catch(InvalidOperationException){return true;}}).Result);
        string invalid=Path.Combine(output,"invalid-preferences.json");
        File.WriteAllText(invalid,"{\"version\":1,\"version\":1}");Reject(()=>new EditorPreferencesStore(invalid));
        File.WriteAllText(invalid,"{\"version\":1,\"unknown\":true}");Reject(()=>new EditorPreferencesStore(invalid));
    }),
    ("Preferences GUI applies theme/layout and independent history without scene writes", () => {
        using var owner=new EditorSessionOwner("Preferences");var ui=new EditorWorkspace(owner);
        var store=new EditorPreferencesStore(Path.Combine(output,"gui-preferences.json"));
        var view=new EditorPresenter(ui,null,preferences:store);view.Build(1,1280,720);view.Apply([Event(view,42)],[]);
        view.Build(2,1280,720);view.Apply([Event(view,2,number:350,domain:11)],[]);
        view.Build(3,1280,720);view.Apply([Event(view,8,domain:11)],[]);Check(store.Current.SideWidth==350 && owner.Edit!.Revision==0);
        view.Build(4,1280,720);view.Apply([Event(view,9,domain:11)],[]);Check(store.Current.SideWidth==300 && owner.Edit!.State.UndoCount==0);
        view.Build(5,1280,720);view.Apply([Event(view,10,domain:11)],[]);Check(store.Current.SideWidth==350);
    }),
    ("Local file picker intents and shortcut capture/focus/draft rules reuse scene commands", () => {
        using var owner=new EditorSessionOwner("Picker");var ui=new EditorWorkspace(owner);Guid id=ui.CreateObject(ui.Stamp);
        string scene=Path.Combine(output,"picker.ncmascene");string? selection=scene;
        var view=new EditorPresenter(ui,null,filePicker:kind=>selection);view.Build(1,1280,720);view.Apply([Event(view,41)],[]);
        Check(File.Exists(scene) && !owner.Edit!.State.Dirty);
        Check(!view.Shortcut(90,true,false,true,true));Check(!view.Shortcut(90,true,false,false,false));Check(owner.Document.World.GetObjects().Count==1);
        ui.BeginDraft(ui.Stamp,id,"Draft");Check(!view.Shortcut(90,true,false,false,true));ui.CancelDraft();
        Check(view.Shortcut(90,true,false,false,true) && owner.Document.World.GetObjects().Count==0);
        Check(view.Shortcut(89,true,false,false,true) && owner.Document.World.GetObjects().Count==1);
        selection=null;byte[] before=owner.Document.CaptureBytes();view.Build(2,1280,720);view.Apply([Event(view,40)],[]);Check(before.SequenceEqual(owner.Document.CaptureBytes()));
        selection=Path.Combine(output,"missing.ncmascene");view.Build(3,1280,720);view.Apply([Event(view,40)],[]);Check(before.SequenceEqual(owner.Document.CaptureBytes()));
    }),
    ("Copied native spdlog pages, bounded overflow and managed Console cache", () => {
        string native=Path.Combine(root,"out/managed/NcmaNative.dll");
        using var reader=new NativeDiagnosticsReader(native);using var log=new ApplicationLog(Path.Combine(output,"native-console.jsonl"));
        while(reader.Pump(log,Guid.Empty)>0){}
        for(int i=0;i<3;i++)using(new Ncma.ActionAnimationSession(native)){}
        Check(reader.Pump(log,Guid.Empty)==3 && reader.Pump(log,Guid.Empty)==0);
        for(int i=0;i<520;i++)using(new Ncma.ActionAnimationSession(native)){}
        Check(reader.Pump(log,Guid.Empty)==32 && log.Snapshot.Any(e=>e.Code=="native.logs_dropped"));
        while(reader.Pump(log,Guid.Empty)>0){}
        Check(log.Snapshot.Length==512);
        using var owner=new EditorSessionOwner("Console");var view=new EditorPresenter(new EditorWorkspace(owner),null,log:log);
        view.Build(1,1280,720);view.Apply([Event(view,3,domain:13)],[]);view.Build(2,1280,720);
        Check(Encoding.UTF8.GetString(view.Text).Contains("native.animation.library"));
        string lockedPath=Path.Combine(output,"locked-console.jsonl");
        using var blockedLog=new ApplicationLog(lockedPath);
        using var locked=new FileStream(lockedPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        var ui=new EditorWorkspace(owner);ui.CreateObject(ui.Stamp);byte[] before=owner.Document.CaptureBytes();
        var blockedView=new EditorPresenter(ui,null,log:blockedLog);blockedView.Build(1,1280,720);
        blockedView.Apply([Event(blockedView,3)],[]);
        Check(blockedView.LastMessage.Contains("unsaved_confirmation_required") && before.SequenceEqual(owner.Document.CaptureBytes()) && blockedLog.Sequence==0);
    }),
    ("Reference lighting/shadow Core drafts, strict values, shared Undo and actual GPU changes", () => {
        using var owner=new EditorSessionOwner("Shadow controls",components:RenderConfiguration.CreateRegistry());var ui=new EditorWorkspace(owner);
        Guid id=ui.CreateObject(ui.Stamp);var initial=RenderConfiguration.Default(Guid.NewGuid());
        ui.Transaction(ui.Stamp,"Config",[new {op="set_component",objectId=id,typeId=RenderConfiguration.ComponentType,version=1,data=owner.Document.World.Components.Encode(initial)}],id);
        byte[] before=owner.Document.CaptureBytes();var view=new EditorPresenter(ui,null);view.Build(1,1280,720);
        var item=view.Items.ToArray().Single(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="Directional shadows");
        view.Apply([Event(view,item.WidgetLow,1),Event(view,item.WidgetLow,2,0),Event(view,item.WidgetLow,3,0)],[]);
        Check(!owner.Document.World.FindObject(id).Get<RenderConfiguration>().ShadowEnabled);ui.History(ui.Stamp,false);Check(before.SequenceEqual(owner.Document.CaptureBytes()));
        Reject(()=>RenderConfiguration.Validate(initial with {ShadowFilter=3}));Reject(()=>RenderConfiguration.Validate(initial with {LightIntensity=float.NaN}));
        Reject(()=>RenderConfiguration.Validate(initial with {ContactSteps=33}));
        using var loader=new PluginLoader();loader.Load(args[1],[
            new("ncma.platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
            new("ncma.renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,1,["ncma.platform"])]);
        using var window=new PlatformWindow(loader.Modules[0],"Reference controls",256,256,false);
        using var renderer=new RendererSession(loader.Modules[1],window,256,256);using var service=new RenderPipelineService(renderer);
        service.Configure(initial,256,256);service.Submit(1);byte[] first=new byte[256*256*4];renderer.Capture(first);renderer.Present();
        service.Configure(initial with {BaseRed=.9f,BaseGreen=.1f,BaseBlue=.12f,ShadowEnabled=false,ContactEnabled=false},256,256);
        service.Submit(2);byte[] second=new byte[first.Length];renderer.Capture(second);renderer.Present();
        Check(!first.SequenceEqual(second) && renderer.Stats.ValidationErrors==0 && renderer.Stats.ValidationWarnings==0);
    }),
    ("Action policy managed/native old-laboratory sequence parity, failures and bounded history", () => {
        string nativeBuild=Path.GetFullPath(Path.Combine(args[1],"../.."));
        var start=new ProcessStartInfo(Path.Combine(nativeBuild,"NcmaAnimationTests.exe")) {RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add("--action-reference");
        using var process=Process.Start(start)!; string reference=process.StandardOutput.ReadToEnd();
        Check(process.WaitForExit(15000) && process.ExitCode==0);
        using var expected=JsonDocument.Parse(reference);
        using var animation=new Ncma.ActionAnimationSession(Path.Combine(root,"out/managed/NcmaNative.dll"));
        var commands=new (uint Command,double Value,string Text)[] {
            (1,1,""),(4,.1,""),(2,0,"Attack"),(4,.2,""),(4,.25,""),(2,0,"Attack"),
            (6,0,""),(7,0,""),(4,1,""),(5,0,""),(2,0,"Dodge"),(4,.1,""),(4,.8,""),(6,0,""),(7,0,"")
        };
        void Assert(int i) {using var actual=JsonDocument.Parse(animation.InspectJson());Compare(expected.RootElement[i],actual.RootElement);}
        Assert(0);int index=1;
        foreach(var command in commands) {animation.Execute(command.Command,command.Value,command.Text);Assert(index++);}
        string before=animation.InspectJson();Reject(()=>animation.Execute(4,double.NaN));Reject(()=>animation.Execute(1,2));
        Reject(()=>animation.Execute(1,0,expectedRevision:0));Check(before==animation.InspectJson());
        animation.Reset();animation.SetPaused(false);ulong revision=animation.Revision;animation.Tick(.2);Check(animation.Revision==revision && animation.Time>.1);
        for(int i=0;i<140;i++)animation.SetSpeed(0);
        for(int i=0;i<128;i++)animation.Undo();Reject(animation.Undo);
        Check(Task.Run(()=>{try{animation.Step(.1);return false;}catch(InvalidOperationException){return true;}}).Result);
    }),
    ("Action semantic GUI commands route isolated C# history and reject stale preview revisions", () => {
        using var owner=new EditorSessionOwner("Action UI");var ui=new EditorWorkspace(owner);
        using var preview=new ActionPreviewSession(Path.Combine(root,"out/managed/NcmaNative.dll"));
        var view=new EditorPresenter(ui,null,animationPreview:preview);view.Build(1,1280,720);view.Apply([Event(view,1,domain:9)],[]);
        Check(preview.Current is not null);view.Build(2,1280,720);view.Apply([Event(view,5,domain:9)],[]);
        view.Build(3,1280,720);view.Apply([Event(view,4,domain:9)],[]);Check(preview.Current!.Time>0 && owner.Edit!.Revision==0);
        view.Build(4,1280,720);var stale=Event(view,4,domain:9);preview.Current.Step(.1);view.Apply([stale],[]);Check(view.LastMessage=="stale_preview");
        view.Build(5,1280,720);Check(view.Items.ToArray().Any(i=>i.Kind==(uint)GuiItemKind.CanvasLines));view.Apply([Event(view,8,domain:9)],[]);
        Check(owner.Edit!.State.UndoCount==0);
    }),
    ("FBX complete wireframe, orbit, copied skeleton and report metadata", () => {
        using var session=new FbxPreviewSession(Path.Combine(root,"out/managed/NcmaNative.dll"));
        session.Import(0,Path.Combine(root,"tests/assets/fbx/blender_279_sausage_7400_binary.fbx"));
        var wire=new FbxWireframe();var front=wire.Build(session,0,600,340).ToArray();
        Check(wire.TotalTriangles==576 && wire.DisplayedTriangles==576 && front.Length>=576*3);
        var orbit=wire.Build(session,1,600,340).ToArray();Check(!front.SequenceEqual(orbit));
        Check(orbit.All(l=>float.IsFinite(l.A.X)&&float.IsFinite(l.A.Y)&&float.IsFinite(l.B.X)&&float.IsFinite(l.B.Y)));
        Check(session.BoneParent(0)==-1 && session.BoneName(0).Length>0);
        string report=session.Report!.Value.GetRawText();Check(string.Concat(InspectionText.Split(report))==report);
        Reject(()=>wire.Build(session,float.NaN,600,340));
    }),
    ("Paged JSON stays active when preview becomes smaller than a single control", () => {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<EditorJsonLabel>("test.long_json", 1, """{"type":"object","required":["value"],"properties":{"value":{"type":"string"}}}""", value => value);
        using var owner = new EditorSessionOwner("Page shrink", components: registry); var ui = new EditorWorkspace(owner); Guid id = ui.CreateObject(ui.Stamp);
        ui.Transaction(ui.Stamp, "Fixture", [new {op="set_component", objectId=id, typeId="test.long_json", version=1, data=new {value=new string('A', 1200)}}], id);
        var view = new EditorPresenter(ui, null); view.Build(1, 1280, 720);
        var item = view.Items.ToArray().Single(i => i.Kind == (uint)GuiItemKind.Text && Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset, (int)i.LabelLength)) == "Component JSON page");
        string page = Encoding.UTF8.GetString(view.Text.Slice((int)item.TextOffset, (int)item.TextLength)), replacement = page[..page.IndexOf('A')];
        var bytes = Encoding.UTF8.GetBytes(replacement); byte[] before = owner.Document.CaptureBytes();
        view.Apply([Event(view, item.WidgetLow, 1), Event(view, item.WidgetLow, 2, textBytes: bytes.Length)], bytes);
        Check(ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(2, 1280, 720); view.Apply([Event(view, item.WidgetLow, 3, textBytes: bytes.Length)], bytes);
        Check(!ui.HasDraft && owner.Document.World.FindObject(id).Get<EditorJsonLabel>().Value.Length < 1023);
        ui.History(ui.Stamp, false); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
    }),
    ("Dedicated reference-render controls validate, commit once and preserve identity", () => {
        using var owner = new EditorSessionOwner("Render fields", components: RenderConfiguration.CreateRegistry()); var ui = new EditorWorkspace(owner);
        Guid id = ui.CreateObject(ui.Stamp); var initial = RenderConfiguration.Default(Guid.NewGuid());
        ui.Transaction(ui.Stamp, "Config", [new {op="set_component", objectId=id, typeId=RenderConfiguration.ComponentType, version=1, data=owner.Document.World.Components.Encode(initial)}], id);
        var view = new EditorPresenter(ui, null); byte[] before = owner.Document.CaptureBytes(); int history = owner.Edit!.State.UndoCount;
        ulong Widget(string label) => view.Items.ToArray().Single(i => Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset, (int)i.LabelLength)) == label).WidgetLow;
        view.Build(1, 1280, 720); ulong roughness = Widget("Reference roughness");
        view.Apply([Event(view, roughness, 1), Event(view, roughness, 2, .5)], []);
        Check(before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(2, 1280, 720); view.Apply([Event(view, roughness, 3, .5)], []);
        var result = owner.Document.World.FindObject(id).Get<RenderConfiguration>();
        Check(result.Roughness == .5f && result.ConfigurationId == initial.ConfigurationId && result.PipelineType == initial.PipelineType && owner.Edit.State.UndoCount == history + 1);
        ui.History(ui.Stamp, false); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(3, 1280, 720); roughness = Widget("Reference roughness"); view.Apply([Event(view, roughness, 1), Event(view, roughness, 3, 0)], []);
        Check(!ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(4, 1280, 720); ulong toggle = Widget("Replace tone stage");
        view.Apply([Event(view, toggle, 1), Event(view, toggle, 2, 1), Event(view, toggle, 3, 1)], []);
        Check(owner.Document.World.FindObject(id).Get<RenderConfiguration>().ReplaceToneStage);
        ui.History(ui.Stamp, false); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
    }),
    ("Unicode-safe JSON paging preserves whole text and enforces replacement budgets", () => {
        string json = JsonSerializer.Serialize(new { value = string.Concat(Enumerable.Repeat("中文😀", 700)) }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var pages = new JsonTextPages(json);
        Check(pages.Count > 1 && string.Concat(Enumerable.Range(0, pages.Count).Select(pages.Text)) == json);
        foreach (int i in Enumerable.Range(0, pages.Count)) {
            string page = pages.Text(i); Check(Encoding.UTF8.GetByteCount(page) <= JsonTextPages.PageBytes);
            Check(pages.Replace(i, page) == json);
        }
        Reject(() => pages.Replace(0, new string('x', 1024)));
        Reject(() => pages.Text(pages.Count));
        Reject(() => new JsonTextPages(new string('x', 4 * 1024 * 1024 + 1)));
        Reject(() => new JsonTextPages(new string((char)0xd800, 1)));
    }),
    ("Large component JSON page preview, stable boundaries, single commit and shared Undo", () => {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<EditorJsonLabel>("test.long_json", 1, """{"type":"object","required":["value"],"properties":{"value":{"type":"string"}}}""", value => value);
        using var owner = new EditorSessionOwner("Large JSON", components: registry);
        var ui = new EditorWorkspace(owner); Guid id = ui.CreateObject(ui.Stamp);
        ui.Transaction(ui.Stamp, "Long fixture", [new {op="set_component", objectId=id, typeId="test.long_json", version=1, data=new {value=new string('A', 2500)}}], id);
        var view = new EditorPresenter(ui, null); view.Build(1, 1280, 720);
        var textItem = view.Items.ToArray().Single(i => i.Kind == (uint)GuiItemKind.Text && Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset, (int)i.LabelLength)) == "Component JSON page");
        string original = Encoding.UTF8.GetString(view.Text.Slice((int)textItem.TextOffset, (int)textItem.TextLength));
        string changed = original.Replace(new string('A', 10), new string('B', 11));
        byte[] before = owner.Document.CaptureBytes(), bytes = Encoding.UTF8.GetBytes(changed);
        int history = owner.Edit!.State.UndoCount;
        view.Apply([Event(view, textItem.WidgetLow, 1, textBytes: Encoding.UTF8.GetByteCount(original))], Encoding.UTF8.GetBytes(original));
        view.Apply([Event(view, textItem.WidgetLow, 2, textBytes: bytes.Length)], bytes);
        Check(ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(2, 1280, 720); view.Apply([Event(view, textItem.WidgetLow, 3, textBytes: bytes.Length)], bytes);
        Check(!ui.HasDraft && owner.Edit.State.UndoCount == history + 1);
        string value = owner.Document.World.FindObject(id).Get<EditorJsonLabel>().Value;
        Check(value.Contains('B') && value.Length > 2500);
        ui.History(ui.Stamp, false); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
        ui.History(ui.Stamp, true); Check(owner.Document.World.FindObject(id).Get<EditorJsonLabel>().Value == value);
    }),
    ("Paged JSON invalid commit and navigation cancel drafts without damaging document", () => {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<EditorJsonLabel>("test.long_json", 1, """{"type":"object","required":["value"],"properties":{"value":{"type":"string"}}}""", value => value);
        using var owner = new EditorSessionOwner("Paged cancel", components: registry); var ui = new EditorWorkspace(owner); Guid id = ui.CreateObject(ui.Stamp);
        ui.Transaction(ui.Stamp, "Long fixture", [new {op="set_component", objectId=id, typeId="test.long_json", version=1, data=new {value=new string('A', 2500)}}], id);
        var view = new EditorPresenter(ui, null); byte[] before = owner.Document.CaptureBytes();
        ulong Widget(string label) => view.Items.ToArray().Single(i => Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset, (int)i.LabelLength)) == label).WidgetLow;
        view.Build(1, 1280, 720); ulong field = Widget("Component JSON page");
        view.Apply([Event(view, field, 1)], []); view.Apply([Event(view, field, 2, textBytes: 1)], Encoding.UTF8.GetBytes("{"));
        Check(ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Apply([Event(view, field, 3, textBytes: 1)], Encoding.UTF8.GetBytes("{"));
        Check(!ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(2, 1280, 720); view.Apply([Event(view, Widget("Next JSON page"))], []);
        view.Build(3, 1280, 720); field = Widget("Component JSON page");
        string page = Encoding.UTF8.GetString(view.Text.Slice((int)view.Items.ToArray().Single(i => i.WidgetLow == field && i.WidgetHigh == 2).TextOffset,
            (int)view.Items.ToArray().Single(i => i.WidgetLow == field && i.WidgetHigh == 2).TextLength));
        view.Apply([Event(view, field, 1)], []); view.Apply([Event(view, field, 2, textBytes: page.Length)], Encoding.UTF8.GetBytes(page.Replace('A','B')));
        Check(ui.HasDraft); view.Apply([Event(view, Widget("Previous JSON page"))], []);
        Check(!ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(4, 1280, 720); Check(Widget("Component JSON page") > 0);
    }),
    ("FBX ABI 2 immutable caller-owned pose, indices and report", () => {
        string kernel = Path.Combine(root, "out/managed/NcmaNative.dll");
        string source = Path.Combine(root, "tests/assets/fbx/blender_279_sausage_7400_binary.fbx");
        using var resource = new Ncma.ImportedCharacterResource(kernel, source);
        var report = resource.Report;
        Check(report.GetProperty("format").GetString() == "FBX" && resource.BoneCount >= 3 && resource.ClipCount >= 2);
        float[] bind = new float[resource.SampleFloatCount], animated = new float[resource.SampleFloatCount];
        resource.Sample(0, 0, bind); resource.Sample(1, 0.25, animated);
        Check(bind.All(float.IsFinite) && animated.All(float.IsFinite) && !bind.SequenceEqual(animated));
        uint[] indices = new uint[resource.IndexCount(0)]; resource.ReadIndices(0, indices);
        Check(indices.Length > 0 && indices.All(i => i < resource.VertexCount(0)));
        Reject(() => resource.Sample(999, 0, animated)); Reject(() => resource.Sample(0, double.NaN, animated));
        Reject(() => resource.Sample(0, 0, new float[1])); Reject(() => resource.ReadIndices(0, new uint[1]));
        bool affinityRejected = Task.Run(() => { try { resource.Sample(0, 0, animated); return false; } catch (InvalidOperationException) { return true; } }).Result;
        Check(affinityRejected);
        resource.Dispose(); Check(report.GetProperty("bones").GetInt32() >= 3 && animated.All(float.IsFinite));
        bool disposedRejected = false; try { resource.Sample(0, 0, animated); } catch (ObjectDisposedException) { disposedRejected = true; } Check(disposedRejected);
    }),
    ("C# FBX clocks, preview Undo and failure preservation independent of scene/history/Play", () => {
        string source = Path.Combine(root, "tests/assets/fbx/blender_279_sausage_7400_binary.fbx");
        using var owner = new EditorSessionOwner("FBX isolation"); var ui = new EditorWorkspace(owner); ui.CreateObject(ui.Stamp);
        byte[] scene = owner.Document.CaptureBytes(); var before = ui.Capture();
        using var preview = new FbxPreviewSession(Path.Combine(root, "out/managed/NcmaNative.dll"));
        preview.Import(preview.Revision, source); var imported = preview.Capture();
        Check(imported.Paused && imported.Clip == 1 && imported.AssetId is not null);
        Reject(() => preview.Execute(0, FbxPreviewCommand.Step)); Check(preview.Capture() == imported);
        Reject(() => preview.Import(preview.Revision, Path.Combine(output, "missing.fbx"))); Check(preview.Capture() == imported);
        preview.Execute(preview.Revision, FbxPreviewCommand.Resume); preview.Tick(.25); var advanced = preview.Capture();
        Check(advanced.Time > 0 && advanced.UndoCount == 2);
        preview.Execute(preview.Revision, FbxPreviewCommand.Pause); preview.Tick(.25); Check(preview.Capture().Time == advanced.Time);
        Reject(() => preview.Execute(preview.Revision, FbxPreviewCommand.Step, double.NaN)); Reject(() => preview.Tick(-1));
        preview.Execute(preview.Revision, FbxPreviewCommand.Step, .1); var stepped = preview.Capture();
        preview.Execute(preview.Revision, FbxPreviewCommand.Undo); Check(preview.Capture().Time == advanced.Time);
        preview.Execute(preview.Revision, FbxPreviewCommand.Redo); Check(preview.Capture().Time == stepped.Time);
        preview.Import(preview.Revision, source); Check(preview.Capture().AssetId == imported.AssetId);
        preview.Execute(preview.Revision, FbxPreviewCommand.Undo); Check(preview.Capture().Time == stepped.Time);
        preview.Execute(preview.Revision, FbxPreviewCommand.SelectClip, clip: 0); Check(preview.Capture().Time == 0 && preview.Capture().RedoCount == 0);
        Check(scene.SequenceEqual(owner.Document.CaptureBytes()) && ui.Capture().State.UndoCount == before.State.UndoCount && ui.Stamp == before.Stamp);
        ui.PlayControl(ui.Stamp, "start"); preview.Execute(preview.Revision, FbxPreviewCommand.Step);
        Check(owner.Play!.Tick == 0 && ui.Capture().State.Frozen); ui.PlayControl(ui.Stamp, "stop");
    }),
    ("FBX bounded history and lease pruning over repeated imports, undo and disposal", () => {
        string source = Path.Combine(root, "tests/assets/fbx/blender_279_sausage_6100_ascii.fbx");
        for (int cycle = 0; cycle < 3; cycle++) {
            using var preview = new FbxPreviewSession(Path.Combine(root, "out/managed/NcmaNative.dll"));
            for (int import = 0; import < 24; import++) preview.Import(preview.Revision, source);
            Check(preview.Capture().UndoCount == FbxPreviewSession.HistoryLimit);
            for (int i = 0; i < FbxPreviewSession.HistoryLimit; i++) preview.Execute(preview.Revision, FbxPreviewCommand.Undo);
            Check(preview.Capture().UndoCount == 0 && preview.Capture().RedoCount == FbxPreviewSession.HistoryLimit);
            Reject(() => preview.Execute(preview.Revision, FbxPreviewCommand.Undo));
            preview.Execute(preview.Revision, FbxPreviewCommand.Redo);
            float[] sampled = new float[preview.SampleFloatCount]; preview.Sample(sampled); Check(sampled.All(float.IsFinite));
            preview.Execute(preview.Revision, FbxPreviewCommand.Pause); Check(preview.Capture().RedoCount == 0);
        }
    }),
    ("FBX semantic GUI events use preview command scope, stale guards and not scene Undo", () => {
        using var owner = new EditorSessionOwner("FBX presenter"); var ui = new EditorWorkspace(owner);
        using var preview = new FbxPreviewSession(Path.Combine(root, "out/managed/NcmaNative.dll"));
        var view = new EditorPresenter(ui, null, null, preview);
        string source = Path.Combine(root, "tests/assets/fbx/blender_279_sausage_7400_binary.fbx");
        view.Build(1, 1280, 720);
        view.Apply([Event(view, 1, textBytes: Encoding.UTF8.GetByteCount(source), domain: 7)], Encoding.UTF8.GetBytes(source));
        view.Build(2, 1280, 720); view.Apply([Event(view, 2, domain: 7)], []);
        Check(preview.Capture().AssetId is not null && owner.Edit!.State.UndoCount == 0);
        view.Build(3, 1280, 720); var stale = Event(view, 6, domain: 7);
        preview.Execute(preview.Revision, FbxPreviewCommand.Pause); view.Apply([stale], []);
        Check(view.LastMessage == "stale_preview" && preview.Capture().Time == 0);
        view.Build(4, 1280, 720); view.Apply([Event(view, 6, domain: 7)], []);
        Check(preview.Capture().Time > 0 && owner.Edit!.State.UndoCount == 0);
        view.Build(5, 1280, 720); view.Apply([Event(view, 7, domain: 7)], []);
        Check(view.Text.Length > 0); view.Build(6, 1280, 720);
        view.Apply([Event(view, 3, domain: 7)], []); Check(preview.Capture().Time == 0);
    }),
    ("Empty GameObject, UUID selection, shared history and explicit delete confirmation", () => {
        using var owner = new EditorSessionOwner("UI"); var ui = new EditorWorkspace(owner);
        Guid id = ui.CreateObject(ui.Stamp); Check(owner.Document.CaptureSnapshot().Objects.Single().Components.Length == 0);
        ui.Select(ui.Stamp,id); ui.History(ui.Stamp,false); Check(ui.Capture().Total == 0);
        ui.History(ui.Stamp,true); Check(ui.Capture().Selected!.Id == id);
        Reject(() => ui.Delete(ui.Stamp,id,Guid.NewGuid())); Check(ui.Capture().Total == 1);
        ui.Delete(ui.Stamp,id,id); Check(ui.Capture().Total == 0); ui.History(ui.Stamp,false); Check(ui.Capture().Total == 1);
    }),
    ("Cached paged rows and copied Inspector data", () => {
        using var owner = new EditorSessionOwner("Cache"); var ui = new EditorWorkspace(owner);
        var operations = Enumerable.Range(0,65).Select(i => (object)new { op="create", objectId=Guid.NewGuid(), name="Object"+i }).ToArray();
        ui.Transaction(ui.Stamp,"Batch",operations); var first = ui.Capture(); int reads = ui.SnapshotReads;
        Check(first.Total == 65 && first.Rows.Length == 32 && ui.Capture(32).Rows.Length == 32 && ui.Capture(64).Rows.Length == 1);
        Check(ui.SnapshotReads == reads); first.Rows[0] = new(Guid.Empty,"corrupt",0,0); Check(ui.Capture().Rows[0].Id != Guid.Empty);
        Reject(() => ui.Capture(0,65));
    }),
    ("Name drafts never leak to committed World and cancel on selection/Play", () => {
        using var owner = new EditorSessionOwner("Draft"); var ui = new EditorWorkspace(owner); Guid id = ui.CreateObject(ui.Stamp);
        ui.BeginDraft(ui.Stamp,id,"Rename"); ui.UpdateDraft(ui.Stamp,id,new { op="rename", objectId=id,name="Draft name" });
        Check(ui.Capture().Selected!.Name == "Draft name" && owner.Document.CaptureSnapshot().Objects.Single().Name == "GameObject");
        ui.Select(ui.Stamp,id); Check(!ui.HasDraft && ui.Capture().Selected!.Name == "GameObject");
        ui.BeginDraft(ui.Stamp,id,"Rename"); ui.UpdateDraft(ui.Stamp,id,new { op="rename", objectId=id,name="Committed" });
        ui.CommitDraft(ui.Stamp,id); Check(ui.Capture().Selected!.Name == "Committed"); ui.History(ui.Stamp,false);
        Check(ui.Capture().Selected!.Name == "GameObject");
        ui.BeginDraft(ui.Stamp,id,"Rename"); ui.PlayControl(ui.Stamp,"start"); Check(!ui.HasDraft && owner.Edit!.State.Frozen);
        Reject(() => ui.CreateObject(ui.Stamp)); ui.PlayControl(ui.Stamp,"pause"); ui.PlayControl(ui.Stamp,"step");
        Check(owner.Play!.Tick == 1); ui.PlayControl(ui.Stamp,"stop"); Check(!owner.Edit!.State.Frozen && owner.Document.World.Tick == 0);
    }),
    ("File association, Dirty, replacement confirmation and failure preservation", () => {
        using var owner = new EditorSessionOwner("Files"); var ui = new EditorWorkspace(owner); ui.CreateObject(ui.Stamp);
        Reject(() => ui.New(ui.Stamp,false)); string path = Path.Combine(output,"场景 test.ncmascene"); ui.Save(ui.Stamp,path);
        Check(!ui.Capture().State.Dirty && ui.Capture().State.FilePath == path);
        ui.New(ui.Stamp,false); Check(ui.Capture().Total == 0 && ui.Capture().State.FilePath is null);
        ui.History(ui.Stamp,false); Check(ui.Capture().Total == 1 && !ui.Capture().State.Dirty);
        byte[] before = owner.Document.CaptureBytes(); Reject(() => ui.Open(ui.Stamp,path+".missing",true));
        Check(before.SequenceEqual(owner.Document.CaptureBytes())); Reject(() => ui.Open(ui.Stamp,Path.Combine(output,"old.ncscene"),true));
        ui.New(ui.Stamp,true); ui.Open(ui.Stamp,path,true); Check(!ui.Capture().State.Dirty);
    }),
    ("Wrong thread and stale stamp reject without mutation", () => {
        using var owner = new EditorSessionOwner("Safety"); var ui = new EditorWorkspace(owner); var stamp = ui.Stamp; ui.CreateObject(stamp);
        Reject(() => ui.CreateObject(stamp)); Check(ui.Capture().Total == 1);
        Check(Task.Run(() => { try { _=ui.Capture(); return false; } catch (InvalidOperationException) { return true; } }).Result);
        Check(Task.Run(() => { try { ui.CancelDraft(); return false; } catch (InvalidOperationException) { return true; } }).Result);
    }),
    ("GUI create, stale replay, name activation/change/commit, unchanged completion", () => {
        using var owner = new EditorSessionOwner("Presenter"); var ui = new EditorWorkspace(owner); var view = new EditorPresenter(ui,null);
        view.Build(1,1280,720); var create = Event(view,8); view.Apply([create],[]); view.Apply([create],[]); Check(ui.Capture().Total == 1);
        view.Build(2,1280,720); byte[] text = Encoding.UTF8.GetBytes("中文对象");
        view.Apply([Event(view,11,1,textBytes:text.Length)],text); view.Apply([Event(view,11,2,textBytes:text.Length)],text);
        Check(ui.Capture().Selected!.Name == "中文对象" && owner.Document.CaptureSnapshot().Objects.Single().Name == "GameObject");
        view.Build(3,1280,720); view.Apply([Event(view,11,3,textBytes:text.Length)],text); Check(ui.Capture().Selected!.Name == "中文对象");
        int history = owner.Edit!.State.UndoCount;
        view.Build(4,1280,720); view.Apply([Event(view,11,1,textBytes:text.Length)],text); view.Apply([Event(view,11,3,textBytes:text.Length)],text);
        Check(!ui.HasDraft && owner.Edit.State.UndoCount == history);
    }),
    ("GUI canceled interaction and invalid component preserve committed content", () => {
        using var owner = new EditorSessionOwner("Cancel"); var ui = new EditorWorkspace(owner); ui.CreateObject(ui.Stamp); var view = new EditorPresenter(ui,null);
        view.Build(1,1280,720); byte[] text = Encoding.UTF8.GetBytes("draft"); var commit = Event(view,11,3,textBytes:text.Length);
        view.Apply([Event(view,11,1,textBytes:text.Length),Event(view,11,2,textBytes:text.Length)],text); view.CancelInteraction(); view.Apply([commit],text);
        Check(!ui.HasDraft && ui.Capture().Selected!.Name == "GameObject");
        Guid id = ui.Capture().Selected!.Id; Reject(() => ui.Transaction(ui.Stamp,"Invalid",[new {op="set_component",objectId=id,typeId="unknown",version=1,data=new {}}],id));
        Check(ui.Capture().Selected!.Components.Length == 0);
    }),
    ("GUI disabled and forged event kind cannot execute", () => {
        using var owner = new EditorSessionOwner("Events"); var ui = new EditorWorkspace(owner); var view = new EditorPresenter(ui,null);
        view.Build(1,1280,720); var create = Event(view,8); create.Kind = (uint)GuiItemKind.Text; view.Apply([create],[]); Check(ui.Capture().Total == 0);
        view.Apply([Event(view,14)],[]); view.Build(2,1280,720); view.Apply([Event(view,8)],[]); Check(ui.Capture().Total == 0 && owner.Play is not null);
    }),
    ("Generic component JSON allows incomplete typing but rejects invalid commit", () => {
        using var owner=new EditorSessionOwner("JSON",components:RenderConfiguration.CreateRegistry()); var ui=new EditorWorkspace(owner); Guid id=ui.CreateObject(ui.Stamp);
        var config=RenderConfiguration.Default(Guid.NewGuid()); var data=owner.Document.World.Components.Encode(config);
        ui.Transaction(ui.Stamp,"Configuration",[new {op="set_component",objectId=id,typeId=RenderConfiguration.ComponentType,version=1,data}],id);
        var view=new EditorPresenter(ui,null); view.Build(1,1280,720); byte[] before=owner.Document.CaptureBytes(), original=Encoding.UTF8.GetBytes(data.GetRawText()), invalid=Encoding.UTF8.GetBytes("{");
        view.Apply([Event(view,100,1,textBytes:original.Length)],original); view.Apply([Event(view,100,2,textBytes:invalid.Length)],invalid);
        Check(ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Apply([Event(view,100,3,textBytes:invalid.Length)],invalid); Check(!ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(2,1280,720); byte[] valid=Encoding.UTF8.GetBytes(owner.Document.World.Components.Encode(config with {Exposure=2}).GetRawText());
        view.Apply([Event(view,100,1,textBytes:original.Length)],original); view.Apply([Event(view,100,2,textBytes:valid.Length)],valid);
        Check(before.SequenceEqual(owner.Document.CaptureBytes())); view.Apply([Event(view,100,3,textBytes:valid.Length)],valid);
        Check(!before.SequenceEqual(owner.Document.CaptureBytes())); ui.History(ui.Stamp,false); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
    }),
    ("Transform preview commits once, invalid quaternion and nonfinite events cancel", () => {
        using var owner=new EditorSessionOwner("Transform"); var ui=new EditorWorkspace(owner); Guid id=ui.CreateObject(ui.Stamp);
        ui.Transaction(ui.Stamp,"Transform",[new {op="set_component",objectId=id,typeId="ncma.transform",version=1,data=owner.Document.World.Components.Encode(TransformData.Identity)}],id);
        var view=new EditorPresenter(ui,null); view.Build(1,1280,720); byte[] before=owner.Document.CaptureBytes();
        view.Apply([Event(view,100,1),Event(view,100,2,3)],[]); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(2,1280,720); view.Apply([Event(view,100,3,3)],[]); Check(owner.Document.World.FindObject(id).Get<TransformData>().Position.X==3);
        before=owner.Document.CaptureBytes(); view.Build(3,1280,720); view.Apply([Event(view,106,1,1),Event(view,106,2,0)],[]);
        Check(ui.HasDraft); view.Apply([Event(view,106,3,0)],[]); Check(!ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(4,1280,720); view.Apply([Event(view,100,1),Event(view,100,2,double.NaN)],[]); Check(!ui.HasDraft && before.SequenceEqual(owner.Document.CaptureBytes()));
    }),
    ("Binding and Export pagination preserve complete unknown metadata", () => {
        using var owner=new EditorSessionOwner("Pages"); var ui=new EditorWorkspace(owner); Guid id=ui.CreateObject(ui.Stamp);
        var bindings=Enumerable.Range(0,9).Select(i=>new {id=Guid.Parse("00000000-0000-0000-0000-"+(i+1).ToString("D12")),typeName="Unknown.Type"+i,enabled=false,
            exports=Enumerable.Range(0,17).Select(e=>new {name="Export"+e.ToString("D2"),kind=2,value=(double)e}).ToArray()}).ToArray();
        ui.Transaction(ui.Stamp,"Fixture bindings",[new {op="set_bindings",objectId=id,bindings}],id);
        byte[] before=owner.Document.CaptureBytes(); var view=new EditorPresenter(ui,null); view.Build(1,1280,720);
        bool Label(string value) => view.Items.ToArray().Any(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))==value);
        Check(Label("Unknown.Type0") && !Label("Unknown.Type8")); view.Apply([Event(view,33)],[]); view.Build(2,1280,720);
        Check(!Label("Unknown.Type0") && Label("Unknown.Type8") && !Label("Export16"));
        var next=view.Items.ToArray().Single(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="Next Exports");
        view.Apply([Event(view,next.WidgetLow)],[]); view.Build(3,1280,720); Check(Label("Export16") && !Label("Export00"));
        Check(before.SequenceEqual(owner.Document.CaptureBytes()));
    }),
    ("Configured script binding/Export, isolated Play and reload failure preservation", () => {
        using var owner = new EditorSessionOwner("Scripts"); var ui = new EditorWorkspace(owner); Guid id = ui.CreateObject(ui.Stamp);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        string assembly = Path.Combine(root,"managed/Ncma.Gameplay.Sample/bin",configuration,"net8.0/Ncma.Gameplay.Sample.dll");
        owner.LoadGameplay(assembly); var type = owner.Catalog.Snapshot.Types.Single(); Guid binding = Guid.NewGuid();
        ui.Transaction(ui.Stamp,"Attach",[new { op="add_binding",objectId=id,binding=new { id=binding,typeName=type.TypeName,enabled=true,
            exports=type.Exports.Select(e=>new {name=e.Name,kind=e.Kind,value=e.DefaultValue}).ToArray() } }],id);
        ui.Transaction(ui.Stamp,"Transform",[new { op="set_component",objectId=id,typeId="ncma.transform",version=1,data=owner.Document.World.Components.Encode(TransformData.Identity) }],id);
        byte[] before = owner.Document.CaptureBytes(); ui.PlayControl(ui.Stamp,"start"); owner.Play!.AdvanceFrame(1.0/60);
        Check(owner.Play.Tick == 1 && owner.Document.CaptureBytes().SequenceEqual(before));
        var generation = owner.Catalog.Snapshot.Generation; Reject(() => ui.Reload(ui.Stamp,assembly+".missing"));
        Check(owner.Play.State == Ncma.Gameplay.PlayState.Paused && owner.Catalog.Snapshot.Generation == generation);
        ui.PlayControl(ui.Stamp,"stop"); ui.History(ui.Stamp,false); Check(ui.Capture().Selected!.Behaviours.Length == 1 && ui.Capture().Selected!.Components.Length == 0);
    }),
    ("Arbitrary gameplay startup exception remains a bounded UI error", () => {
        using var owner=new EditorSessionOwner("User error"); var ui=new EditorWorkspace(owner); Guid id=ui.CreateObject(ui.Stamp);
        owner.LoadGameplay(typeof(ThrowingUiBehaviour).Assembly.Location);
        ui.Transaction(ui.Stamp,"Throwing fixture",[new {op="add_binding",objectId=id,binding=new {id=Guid.NewGuid(),typeName=typeof(ThrowingUiBehaviour).FullName!,enabled=true,exports=Array.Empty<object>()}}],id);
        byte[] before=owner.Document.CaptureBytes(); var view=new EditorPresenter(ui,null); view.Build(1,1280,720); view.Apply([Event(view,14)],[]);
        Check(view.LastMessage.Contains("custom gameplay failure") && owner.Play is null && !owner.Edit!.State.Frozen && before.SequenceEqual(owner.Document.CaptureBytes()));
        view.Build(2,1280,720); view.Apply([Event(view,8)],[]); Check(ui.Capture().Total==2);
    }),
    ("Managed owner IPC: default deny, exact displayed approval, shared UI Undo/remote Redo", () => {
        using var owner = new EditorSessionOwner("IPC owner"); var ui = new EditorWorkspace(owner); Guid id = ui.CreateObject(ui.Stamp);
        var authorization = new EditorAuthorizationController(ui); Check(authorization.Capture() is null);
        authorization.Configure(ui.Stamp,true,output); var endpoint = owner.Endpoint!;
        var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(endpoint.DescriptorPath));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".",descriptor.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        pipe.Connect(3000); Wire.WriteAsync(pipe,Wire.Encode(new Hello(1,descriptor.InstanceId,descriptor.ProjectRoot,"M2.5 UI test")),stop.Token).GetAwaiter().GetResult();
        var handshake = Wire.ReadAsync(pipe,stop.Token);
        void Until(Func<bool> done) { var clock = Stopwatch.StartNew(); while(!done()) { if(clock.ElapsedMilliseconds>5000) throw new TimeoutException(); endpoint.Pump(); Thread.Sleep(1); } }
        Until(()=>endpoint.View.Connections.Length==1); var page = authorization.Capture()!; Check(!handshake.IsCompleted);
        authorization.Pair(page,page.Endpoint.Connections.Single().ConnectionId,true); Until(()=>handshake.IsCompleted);
        var hello = Wire.Decode<HelloResult>(handshake.Result!); Check(hello.SessionId == owner.Edit!.SessionId);
        IpcResponse Remote(CapabilityRequest request) {
            Wire.WriteAsync(pipe,Wire.Encode(new IpcRequest("invoke",owner.Edit.DocumentGeneration,JsonSerializer.SerializeToElement(request,Wire.Json),CallId:Guid.NewGuid())),stop.Token).GetAwaiter().GetResult();
            var response=Wire.ReadAsync(pipe,stop.Token); Until(()=>response.IsCompleted); return Wire.Decode<IpcResponse>(response.Result!);
        }
        var rename = new CapabilityRequest(2,Guid.NewGuid(),owner.Edit.SessionId,owner.Edit.Revision,"ncma.scene.transaction",
            JsonSerializer.SerializeToElement(new {operations=new[]{new {op="rename",objectId=id,name="Agent renamed"}}}));
        Check(Remote(rename).Result!.Value.GetProperty("code").GetString()=="permission_denied");
        page=authorization.Capture()!; var proposal=page.Proposals.Single();
        Reject(()=>authorization.Approve(page,proposal.Scope.Id,"wrong fingerprint",true,null));
        var presenter=new EditorPresenter(ui,null,output); presenter.Build(1,1280,720);
        presenter.Apply([Event(presenter,4,number:1,domain:5),Event(presenter,5,number:1,domain:5)],[]);
        presenter.Build(2,1280,720); presenter.Apply([Event(presenter,7,domain:5)],[]);
        Check(endpoint.Grants.Length==1);
        Check(Remote(rename).Result!.Value.GetProperty("changed").GetBoolean() && ui.Capture().Selected!.Name=="Agent renamed");
        ui.History(ui.Stamp,false); Check(ui.Capture().Selected!.Name=="GameObject");
        var redo=new CapabilityRequest(2,Guid.NewGuid(),owner.Edit.SessionId,owner.Edit.Revision,"ncma.history.redo",JsonSerializer.SerializeToElement(new {}));
        Check(Remote(redo).Result!.Value.GetProperty("changed").GetBoolean());
        page=authorization.Capture()!; authorization.Revoke(page,hello.ConnectionId); Check(endpoint.Grants.Length==0);
        authorization.Configure(ui.Stamp,false,output); Check(authorization.Capture() is null && !File.Exists(descriptor.ProjectRoot+"/out/sessions/"+descriptor.InstanceId.ToString("N")+".json"));
    }),
    ("M5.7 toolbar-only geometry, command guards, scoped theme, actual icon and GPU review image", () => ToolbarTests.Run(root,args[1],output)),
    ("M5.7 unified workspace layout, project brand, stale menu guards, Play isolation and actual GPU", () => WorkspaceTests.Run(root,args[1],output)),
    ("M5.7 color-only UI targets, exact cached leases, one Present and 32 lifecycle baselines", () => UiTargetTests.Run(args[1])),
    ("Real GUI/Renderer business view composition, validation and resource release", () => {
        if(args.Length<2) throw new ArgumentException("Plugin directory required for graphics integration.");
        using var loader=new PluginLoader(); loader.Load(args[1],[
            new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
            new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,1,["platform"]),
            new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,2,["platform","renderer"])]);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"M2.5 business composition",1280,720,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,1280,720);
        using var pipeline=new RenderPipelineService(renderer); pipeline.Configure(RenderConfiguration.Default(Guid.NewGuid()),680,315);
        using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc")); gui.AttachRenderer(renderer);
        using var owner=new EditorSessionOwner("业务场景"); var ui=new EditorWorkspace(owner); Guid id=ui.CreateObject(ui.Stamp);
        ui.Transaction(ui.Stamp,"Add Transform",[new {op="set_component",objectId=id,typeId="ncma.transform",version=1,data=owner.Document.World.Components.Encode(TransformData.Identity)}],id);
        using var fbxPreview=new FbxPreviewSession(Path.Combine(root,"out/managed/NcmaNative.dll"));
        fbxPreview.Import(0,Path.Combine(root,"tests/assets/fbx/blender_279_sausage_7400_binary.fbx"));
        using var animationPreview=new ActionPreviewSession(Path.Combine(root,"out/managed/NcmaNative.dll"));animationPreview.Open();
        var presenter=new EditorPresenter(ui,null,fbxPreview:fbxPreview,animationPreview:animationPreview); byte[] pixels=new byte[1280*720*4];
        for(ulong frame=1;frame<=4;frame++) {
            var state=window.Poll(); gui.Begin(state,1.0/60); var view=presenter.Build(frame,1280,720);
            var stats=gui.Draw(view,presenter.Items,presenter.Text); Check(stats.Vertices>0 && stats.EventOverflow==0);
            pipeline.Submit(frame,300,405); gui.RenderGpu(); if(frame==4) renderer.Capture(pixels); renderer.Present();
        }
        Check(renderer.Stats.ValidationErrors==0 && renderer.Stats.ValidationWarnings==0 && renderer.Stats.Presents==4);
        using var bmp=new BinaryWriter(File.Create(Path.Combine(output,"business-composited.bmp")));
        bmp.Write((ushort)0x4d42);bmp.Write(54+pixels.Length);bmp.Write(0);bmp.Write(54);bmp.Write(40);bmp.Write(1280);bmp.Write(-720);
        bmp.Write((ushort)1);bmp.Write((ushort)32);bmp.Write(0);bmp.Write(pixels.Length);bmp.Write(2835);bmp.Write(2835);bmp.Write(0);bmp.Write(0);
        for(int i=0;i<pixels.Length;i+=4){bmp.Write(pixels[i+2]);bmp.Write(pixels[i+1]);bmp.Write(pixels[i]);bmp.Write(pixels[i+3]);}
        Console.WriteLine("Business image: "+Path.Combine(output,"business-composited.bmp"));
    }),
    ("Asset project service uses the same Editor history, defaults read-only and closes cleanly", () => {
        string directory = Path.Combine(output, "asset-project"); Directory.CreateDirectory(Path.Combine(directory, "assets"));
        byte[] source = Encoding.UTF8.GetBytes("asset source fixture"); File.WriteAllBytes(Path.Combine(directory, "assets/Hero.fbx"), source);
        var record = new AssetRecord(1, Guid.NewGuid(), AssetKind.Character, "assets/Hero.fbx",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)), "ufbx", 1, new(1, 60, true), [], [], null);
        Guid project = Guid.NewGuid();
        using (var owner = new EditorSessionOwner("Read-only assets"))
        {
            owner.ConfigureAssets(directory, project, 1); Check(owner.Assets!.Snapshot.Diagnostics.Single().Code == "source_orphan");
            var input = JsonSerializer.SerializeToElement(new { operation = "create", path = "assets/Hero.ncmeta", expectedAssetRevision = owner.Assets.Clock.Revision,
                record = JsonDocument.Parse(AssetRecordCodec.Encode(record)).RootElement.Clone() });
            var result = owner.Edit!.Invoke(new(2, Guid.NewGuid(), owner.Edit.SessionId, owner.Edit.Revision, AssetMetadataCommands.CapabilityName, input),
                new([AssetMetadataCommands.CapabilityName]));
            Check(result.Status == "denied" && !File.Exists(Path.Combine(directory, "assets/Hero.ncmeta")));
        }
        using (var owner = new EditorSessionOwner("Granted assets"))
        {
            owner.ConfigureAssets(directory, project, 2, new(["assets/Hero.ncmeta"], [record.SourcePath], [record.AssetId], () => true));
            var input = JsonSerializer.SerializeToElement(new { operation = "create", path = "assets/Hero.ncmeta", expectedAssetRevision = owner.Assets!.Clock.Revision,
                record = JsonDocument.Parse(AssetRecordCodec.Encode(record)).RootElement.Clone() });
            Check(owner.Edit!.Invoke(new(2, Guid.NewGuid(), owner.Edit.SessionId, owner.Edit.Revision, AssetMetadataCommands.CapabilityName, input),
                new([AssetMetadataCommands.CapabilityName])).Changed);
            var ui = new EditorWorkspace(owner); ui.History(ui.Stamp, false); Check(!File.Exists(Path.Combine(directory, "assets/Hero.ncmeta")));
            ui.History(ui.Stamp, true); Check(owner.RefreshAssets(true) && owner.Assets.Snapshot.Catalog.Count == 1);
            Check(owner.Document.World.GetObjects().Count() == 0 && owner.Edit.State.UndoCount == 1);
        }
        using var reopened = new EditorSessionOwner("Reopened assets"); reopened.ConfigureAssets(directory, project, 3);
        Check(reopened.Assets!.Snapshot.Catalog.List()[0].AssetId == record.AssetId);
    }),
    ("Scene camera selection and resource refresh are explicit local presentation intents", () => {
        using var owner=new EditorSessionOwner("Camera UI",components:Ncma.Scene.Rendering.RenderComponentRegistry.CreateRegistry(),validateComposition:Ncma.Scene.Rendering.SceneRenderValidation.RequireComposition);
        var camera=owner.Document.World.CreateObject("Camera");camera.Set(TransformData.Identity);camera.Set(Ncma.Scene.Rendering.CameraData.Default);
        owner.Edit!.Resynchronize();var workspace=new EditorWorkspace(owner);workspace.Select(workspace.Stamp,camera.PersistentId);
        var presenter=new EditorPresenter(workspace,null,root);presenter.Build(1,1280,720);byte[] before=owner.Document.CaptureBytes();ulong revision=owner.Document.Revision;
        presenter.Apply([Event(presenter,2,domain:14)],[]);Check(presenter.SceneCamera==camera.PersistentId);
        presenter.Build(2,1280,720);presenter.Apply([Event(presenter,1,domain:14)],[]);Check(presenter.SceneCamera==Guid.Empty);
        presenter.Build(3,1280,720);presenter.Apply([Event(presenter,3,domain:14)],[]);Check(presenter.ConsumeRenderAssetRefresh()&&!presenter.ConsumeRenderAssetRefresh());
        Check(revision==owner.Document.Revision&&before.SequenceEqual(owner.Document.CaptureBytes()));
    }),
    ("Scene render inspections are scoped read-only copied v4 results", () => {
        using var loader=new PluginLoader();loader.Load(args[1],[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"])]);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Inspection test",256,256,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,256,256);
        using var owner=new EditorSessionOwner("Scene inspection");SceneRenderInspections.Register(owner.Edit!,renderer,()=>null);
        foreach(string name in new[]{"ncma.render.inspect_pipeline","ncma.render.inspect_graph","ncma.render.get_profile"}) {
            var request=new CapabilityRequest(EditSession.ContractVersion,Guid.NewGuid(),owner.Edit!.SessionId,owner.Edit.Revision,name,JsonSerializer.SerializeToElement(new{}));
            var result=owner.Edit.Invoke(request);Check(result.Status=="ok"&&!result.Changed&&result.Data.GetProperty("version").GetInt32()==4);
            Check(owner.Edit.Invoke(request with{RequestId=Guid.NewGuid(),Input=JsonSerializer.SerializeToElement(new{write=true})}).Status!="ok");
        }
        Check(renderer.PipelineStats.Pipelines==0&&renderer.PipelineStats.ResidentBytes==0&&renderer.ResourceStats.Creates==0);
    }),
    ("M5.7 UI authoring approval/draft/shared Undo/cache/GPU/isolated preview",()=>UiWorkspaceTests.Run(root,args[1],output)),
    ("M5.7 strict workspace settings/splitter cancellation/eight-handle geometry/locks/auto-layout",()=>UiGeometryTests.Run(output)),
    ("M3.6 asset UI plan/grant/import/history/flat placement/frozen/cancel/stale/restart",()=>M36WorkflowTests.Run(root,args[1],output)),
    ("M3.6 independent Orbit/Pan/Zoom and right-handed bounded CPU picking",M36WorkflowTests.CameraAndPicking),
    ("M4.4 actual Editor root motion/scene GPU/interpolation/Edit isolation/close failure retry",()=>CharacterEditorChecks.Run(output,args[1])),
    ("M4.6 character/combat closed schemas, live stdio, precise UI approval/expiry/revoke/redaction and reload",()=>CharacterInspectionTests.Run(root,args[1],output)),
    ("M6.3-D graph runtime exact resource/Play/UI approval + real stdio read/expiry/revoke/reload/fault",()=>AnimationRuntimeInspectionTests.Run(root,args[1],output)),
    ("Optional Physics project bootstrap without scene coupling", () => {
        string dir=Path.Combine(output,"physics-project");Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir,"Main.ncmascene"),new SceneDocument().CaptureBytes());
        File.Copy(Path.Combine(root,"out/managed/Ncma.Gameplay.Sample.dll"),Path.Combine(dir,"Gameplay.dll"),true);
        var config=new ProjectConfiguration(1,Guid.NewGuid(),"Physics bootstrap","Main.ncmascene","Gameplay.dll","Direct3D11",[]);
        string path=Path.Combine(dir,"Project.ncmaproject");
        ProjectContext Context(bool enabled) {
            File.WriteAllText(path,JsonSerializer.Serialize(config with{PhysicsEnabled=enabled},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            return ProjectContext.Load(path);
        }
        string withoutPhysics=Path.Combine(output,"plugins-no-physics");Directory.CreateDirectory(withoutPhysics);
        foreach(string name in new[]{"NcmaPlatform.dll","NcmaGui.dll","NcmaRenderer.dll","glfw3.dll"})
            File.Copy(Path.Combine(args[1],name),Path.Combine(withoutPhysics,name),true);
        // Normal candidate startup works with no Physics DLL. Strict enabled path fails
        // and can still shut down every previously created platform/GUI resource.
        using(var disabled=new CandidatePresentation(withoutPhysics,Context(false),true,true,false))disabled.Start();
        using(var absent=new CandidatePresentation(withoutPhysics,Context(true),true,true,false))Reject(absent.Start);
        using(var enabled=new CandidatePresentation(args[1],Context(true),true,true,false)) {
            enabled.Start();Reject(()=>new PhysicsService(args[1]));
        }
        using var verification=new PhysicsModuleHost(args[1]);
        Check(verification.Module!.Status.LiveResources==0 && verification.Module.Status.LiveJobs==0);
    }),
}).ToArray();
foreach (var (name, run) in cases) { try { run(); Console.WriteLine("PASS: " + name); } catch (Exception e) { Console.Error.WriteLine("FAIL: " + name + "\n" + e); return 1; } }
Console.WriteLine($"Editor services: {cases.Length}/{cases.Length} passed.");
return 0;

public readonly record struct EditorJsonLabel(string Value) : IComponent;

public sealed class ThrowingUiBehaviour : Ncma.Behaviour
{
    protected override void OnCreate() => throw new CustomUiGameplayException();
}
public sealed class CustomUiGameplayException : Exception
{
    public CustomUiGameplayException() : base("custom gameplay failure") { }
}
