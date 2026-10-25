using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Rendering.Scene.Examples;
using Ncma.Scene.Rendering;

// Executed both from the source harness and, separately, inside the strict product ALC.
internal static class PipelineHostProbe
{
    internal static void Run(string requestPath)
    {
        using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath)); var r = request.RootElement;
        string project = r.GetProperty("project").GetString()!, output = r.GetProperty("output").GetString()!,
            plugins = r.GetProperty("plugins").GetString()!, kind = r.GetProperty("kind").GetString()!;
        bool relocated = r.GetProperty("relocated").GetBoolean(); string root = r.GetProperty("productRoot").GetString()!;
        int checks = 0, builds = 0; var images = new List<object>(); var costs = new List<object>();
        void Pass(bool ok, string label) { if (!ok) throw new Exception("D3-A host: " + label); checks++; }
        void Image(byte[] pixels, string label) { Pass(pixels.Length > 0 && pixels.Distinct().Count() > 4, label); images.Add(new { label, bytes = pixels.Length, sha256 = Convert.ToHexString(SHA256.HashData(pixels)) }); }
        if (relocated) DeploymentManifest.Validate(root);
        var modules = SceneCustomizationExamples.Modules();
        Func<float,float,bool,RenderPipeline> Factory(bool full) => (e,a,s) => { builds++; return full ? SceneCustomizationExamples.ReplacePipeline(e*.5f,a,s) : SceneCustomizationExamples.ReplaceGeometryStage(e*.5f,a,s); };
        if (kind is "player" or "null") RunPlayer(); else RunEditor();
        // Each product intentionally excludes the other app assembly. Keep JIT resolution
        // behind separate non-inlined methods, not just a branch in one combined method.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void RunPlayer()
        {
            bool headless = kind == "null";
            PlayerOptions Options() => new(project, headless, null, 4, 1d/60, 30, Path.Combine(output, Guid.NewGuid()+"-report.json"), false, false);
            byte[]? baseline = null;
            PlayerReport Run(Func<float,float,bool,RenderPipeline>? factory, Action<byte[],uint,uint>? capture = null)
            {
                using var playerRunOwner = new PlayerRunner();
                EcsCommittedProbe? probe = null;
                void Observe(Ncma.Gameplay.PlaySession play, ulong nextTick)
                {
                    if (probe is null) { probe = new(play.Scene.World); play.AddCommittedObserver(probe); }
                    // Caller boundary, not an IPC or snapshot operation inside a simulation callback.
                    var snapshot = play.Scene.CaptureSnapshot(); var ids = new List<Guid>();
                    foreach (var entity in probe.Query) ids.Add(play.Scene.World.GetUuid(entity));
                    var expected = snapshot.Objects.Where(o => o.Components.Any(c => c.TypeId == "ncma.transform")).Select(o => o.Id).ToArray();
                    if (!ids.SequenceEqual(expected) || nextTick != play.Tick + 1 || probe.LastTick != play.Tick)
                        throw new Exception("E4-E Player committed query/cold snapshot disagree.");
                }
                var report = headless
                    ? playerRunOwner.Run(Options(), pluginRoot: plugins, visible: false, trustedInput: Observe, postModules: modules, pipelineFactory: factory)
                    : playerRunOwner.RunForVerification(Options(), plugins, capture ?? ((_,_,_)=>{}), trustedInput: Observe, postModules: modules, pipelineFactory: factory);
                if(report.ExitCode==0)Pass(probe is not null && probe.Steps >= 4 && probe.LastTick == report.Tick, "E4-E actual Player/Null committed observer/query lifetime");
                return report;
            }
            void Healthy(PlayerReport report) => Pass(report.ExitCode == 0 && report.Tick >= 4 && report.ShutdownErrors.Length == 0 && report.ValidationErrors == 0 && report.ValidationWarnings == 0, "Player ticks/close/API0/0: " + JsonSerializer.Serialize(report));
            Healthy(Run(null, (p,_,_)=>baseline??=p));
            byte[]? stage = null;
            foreach (bool full in new[] { false, true })
            {
                byte[]? pixels = null; int before = builds;
                var report = Run(Factory(full), (p,_,_)=>pixels??=p); Healthy(report);
                if (headless) Pass(builds == before && report.RenderedFrames == 0 && report.Modules.Length == 0, "Null never invokes rendering extension");
                else
                {
                    Pass(builds == before + 3, "two startup variants + one viewport, no per-frame callback");
                    Image(pixels!, full ? "full replacement" : "stage replacement");
                    Pass(!pixels!.SequenceEqual(baseline!), "custom exposure changes formal Player pixels");
                    if (stage is null) stage = pixels; else Pass(stage.SequenceEqual(pixels!), "own graph/stage identical unshadowed image");
                }
            }
            if (!headless)
            {
                foreach (var factory in new Func<float,float,bool,RenderPipeline>[] {
                    (_,_,_)=>throw new ArgumentException("trusted startup failure"), (_,_,_)=>null!,
                    (e,a,s)=>new Scene3DPipeline(e,a,shadows:false),
                    (e,a,s)=>new CallbackPipeline((w,h)=>new Scene3DPipeline(e,a,shadows:s,features:[new SceneExposureFeature(1)]).Build(w,h)),
                    (e,a,s)=>new CallbackPipeline((w,h)=>new Scene3DPipeline(e,a,shadows:s).Build(w+1,h)) })
                {
                    int captures=0; var rejected=Run(factory,(_,_,_)=>captures++);
                    Pass(rejected.ExitCode!=0 && rejected.SessionId==Guid.Empty && rejected.Tick==0 && captures==0 && rejected.RenderedFrames==0 && rejected.ShutdownErrors.Length==0, "invalid factory/Build rejected before gameplay");
                }
                // A late failure is not hidden by the startup check: host clears and faults.
                int calls=0, captured=0; byte[]? failed=null;
                var late=Run((e,a,s)=>++calls>2?throw new ArgumentException("late callback failure"):new Scene3DPipeline(e,a,shadows:s),(p,_,_)=>{captured++;failed=p;});
                Pass(late.ExitCode==5 && captured==1 && failed is not null && failed.Where((b,i)=>b!=failed[i%4]).Any()==false && late.ShutdownErrors.Length==0, "late factory failure clears without stale pixels and closes");
                Healthy(Run(Factory(true))); Pass(!GraphicsLoaded(), "Player resources unload after all runs");
                foreach (bool environmentConflict in new[] { false, true }) {
                    using var faultOwner = new PlayerRunner(); bool invoked = false; int frames = 0; string? rejected = null;
                    string expected = environmentConflict ? "ambiguous_environment_configuration" : "ambiguous_render_configuration";
                    var report = faultOwner.RunForVerification(Options(), plugins, (_,_,_) => frames++, postModules: modules,
                        trustedInput: (play, _) => {
                            Pass(!invoked, "E4-C fault probe enters once"); invoked = true;
                            var presentation = EnvironmentProductEvidence.Field<PlayerPresentation>(faultOwner, "presentation");
                            presentation.Present(play, (_,_,_) => frames++); // Warm the real host and retained query plans.
                            // Trusted test fault injection at a committed boundary, not a gameplay/Agent capability.
                            for (int i = 0; i < 2; i++) {
                                var obj = play.Scene.World.CreateEntity("E4-C duplicate setting");
                                if (environmentConflict) play.Scene.World.Set(obj, EnvironmentLightingData.Off);
                                else play.Scene.World.Set(obj, RenderConfiguration.Default(Guid.NewGuid()));
                            }
                            try { presentation.Present(play, (_,_,_) => frames++); }
                            catch (ArgumentException error) { rejected = error.Message; throw; }
                        });
                    Pass(invoked && rejected == expected && report.ExitCode == 5 && frames == 1 &&
                        report.ShutdownErrors.Length == 0 && faultOwner.LastPresentationFailure is not null,
                        "E4-C Player duplicate setting faults before another presentation and closes: " + expected);
                    Pass(!GraphicsLoaded(), "E4-C failed Player releases graphics");
                }
            }
            else Pass(!GraphicsLoaded(), "Null no graphics native modules");
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void RunEditor()
        {
            var context=ProjectContext.Load(project); byte[]? defaultImage=null, stageImage=null;
            foreach(int mode in new[]{0,1,2})
            {
                bool broken=false; Action? onBuild=null;
                RenderPipeline FactoryWithFault(float e,float a,bool s)
                {
                    builds++;
                    return new CallbackPipeline((w,h)=> { onBuild?.Invoke(); if(broken) throw new RenderGraphException("test", "invalid late graph"); return (mode==2?SceneCustomizationExamples.ReplacePipeline(e*.5f,a,s):SceneCustomizationExamples.ReplaceGeometryStage(e*.5f,a,s)).Build(w,h); });
                }
                using(var editor=new CandidatePresentation(plugins,context,true,false,false,postProcessModules:modules,pipelineFactory:mode==0?null:FactoryWithFault))
                {
                    editor.Start(); ulong frame=0;
                    byte[] Draw(bool capture=true)
                    {
                        byte[]? pixels=null; if(capture)editor.CaptureNextViewportForVerification((p,_,_)=>pixels=p);
                        if(!editor.PumpPlatform())throw new Exception("Window closed"); var f=new FrameContext(++frame,0);
                        editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??[];
                    }
                    var image=Draw(); Image(image,"Editor mode "+mode); var size=editor.VerificationViewport;
                    if(mode==0){
                        defaultImage=image;
                        var editorOwner=editor.VerificationEditor;var authoring=editorOwner.Document;
                        var original=editorOwner.Document.CaptureSnapshot();var oldProjection=EnvironmentProductEvidence.Field<EditorSceneProjection>(editor,"_editProjection");Guid oldWorld=oldProjection.Scene.World.Identity;
                        var reader=EnvironmentProductEvidence.Field<SceneComponentReader<RenderConfiguration>>(editor,"_configurationReader");
                        var originalConfig=EnvironmentProductEvidence.Field<RenderConfiguration>(editor,"_configuration");
                        // Deliberately malformed trusted extension state, not an authoring permission bypass.
                        var first=authoring.CreateObject("E4-C setting");var selected=RenderConfiguration.Default(Guid.NewGuid()) with{Exposure=.5f};authoring.SetComponent(first,selected);
                        Draw();Pass(EnvironmentProductEvidence.Field<RenderConfiguration>(editor,"_configuration")==selected,"E4-C Editor observes one setting");
                        var second=authoring.CreateObject("E4-C duplicate");authoring.SetComponent(second,selected with{Exposure=2});Draw();
                        Pass(EnvironmentProductEvidence.Field<RenderConfiguration>(editor,"_configuration")==selected,"E4-C Editor duplicate preserves last valid setting");
                        var log=EnvironmentProductEvidence.Field<ApplicationLog>(editor,"_log");
                        Pass(log.Snapshot.Any(e=>e.Code=="render.ambiguous_configuration"&&e.Level=="warning"),"E4-C duplicate warning remains visible");
                        authoring.SetComponent(first,selected with{Exposure=.25f});Draw();
                        Pass(EnvironmentProductEvidence.Field<RenderConfiguration>(editor,"_configuration")==selected,"E4-C value mutation does not pick an ambiguous setting");
                        authoring.DeleteObject(second);Draw();
                        Pass(EnvironmentProductEvidence.Field<RenderConfiguration>(editor,"_configuration")== (selected with{Exposure=.25f}),"E4-C unique setting recovers after member removal");
                        editorOwner.Document.RestoreSnapshot(original);
                        Pass(Draw().SequenceEqual(image)&&EnvironmentProductEvidence.Field<EditorSceneProjection>(editor,"_editProjection").Scene.World.Identity!=oldWorld,"E4-C Restore rebuilds resources and restores exact viewport");
                        Pass(!ReferenceEquals(reader,EnvironmentProductEvidence.Field<SceneComponentReader<RenderConfiguration>>(editor,"_configurationReader"))&&
                            EnvironmentProductEvidence.Field<RenderConfiguration>(editor,"_configuration")==originalConfig,"E4-C new projection gets its own reader, absent setting uses original default");
                        continue;
                    }
                    Pass(!image.SequenceEqual(defaultImage!),"Editor uses custom stage/final Tone");
                    if(mode==1)stageImage=image;else Pass(image.SequenceEqual(stageImage!),"Editor full graph/stage parity");
                    var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);var edit=owner.Edit!;
                    void InspectCurrent()
                    {
                        var expected=owner.Document.CaptureSnapshot();
                        long Captures()=>(long)typeof(Ncma.Scene.SceneDocument).GetField("_capturedDocumentCount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner.Document)!;
                        var captureBefore=Captures();
                        CapabilityRequest Read(string capability,object input)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,capability,JsonSerializer.SerializeToElement(input));
                        for(int repeat=0;repeat<2;repeat++){
                            var inspected=edit.Invoke(Read("ncma.scene.inspect",new{offset=0,limit=128}));
                            Pass(inspected.Status=="ok"&&!inspected.Changed&&inspected.Data.GetProperty("model").GetString()=="ncma.authoring-scene"&&inspected.Data.GetProperty("modelVersion").GetInt32()==1&&inspected.Data.GetProperty("documentSessionId").GetGuid()==owner.Document.SessionId&&inspected.Data.GetProperty("documentRevision").GetUInt64()==owner.Document.Revision,"E4-E actual Editor committed page");
                            var objects=inspected.Data.GetProperty("scene").GetProperty("objects").EnumerateArray().ToArray();
                            Pass(objects.Select(o=>o.GetProperty("id").GetGuid()).SequenceEqual(expected.Objects.Take(128).Select(o=>o.Id)),"E4-E rendered Editor page creation order");
                            foreach(var obj in expected.Objects.Take(128)){
                                var detail=edit.Invoke(Read("ncma.scene.object.inspect",new{objectId=obj.Id,section="components",limit=64}));
                                Pass(detail.Status=="ok","E4-E actual Editor component details");
                                var items=detail.Data.GetProperty("items").EnumerateArray().ToArray();
                                Pass(items.Length==obj.Components.Length&&obj.Components.All(c=>items.Single(i=>i.GetProperty("typeId").GetString()==c.TypeId).GetProperty("data").GetRawText()==c.Data.GetRawText()),"E4-E rendered Editor component/cold codec equality");
                            }
                        }
                        // Pure authoring pages use stored normalized values; neither cold nor warm reads capture the whole document.
                        Pass(Captures()==captureBefore,"E4-E no repeated complete document capture on stable Editor reads");
                    }
                    InspectCurrent();
                    Guid setting=owner.Document.GetObjectIds().Single(o=>owner.Document.HasComponent<PostProcessData>(o));Guid id=setting;var active=owner.Document.GetComponent<PostProcessData>(setting);
                    void Set(PostProcessData value)
                    {
                        var command=new CapabilityRequest(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=id,typeId=PostProcessData.TypeId,version=1,data=owner.Document.Components.Encode(value)}}}));
                        Pass(edit.Invoke(command).Status=="denied","default Agent remains read-only");
                        var result=edit.Invoke(command,new(["ncma.scene.transaction"],objectScope:[id],componentScope:[PostProcessData.TypeId]));
                        if(!result.Changed)Console.WriteLine($"PIPELINE COMMAND mode={mode} status={result.Status} code={result.Code} data={result.Data}");
                        Pass(result.Changed,"original scoped post command");
                    }
                    Set(active with{ExposureEv=-1});var changed=Draw();Pass(!changed.SequenceEqual(image),"command changes custom graph pixels");InspectCurrent();
                    workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(image),"sole Undo exact custom pixels");InspectCurrent();
                    workspace.History(workspace.Stamp,true);Pass(Draw().SequenceEqual(changed),"sole Redo exact custom pixels");InspectCurrent();
                    Set(active);Pass(Draw().SequenceEqual(image),"restore custom baseline");
                    owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
                    for(int cycle=0;cycle<2;cycle++){
                        int before=builds;var play=owner.StartPlay();Pass(builds==before+2,"Play preflight before callbacks");
                        Pass(Draw().SequenceEqual(image),"isolated Play custom graph");play.Pause();owner.ReloadGameplay(context.GameplayAssemblyPath);
                        Pass(Draw().SequenceEqual(image),"Reload custom graph");InspectCurrent();owner.StopPlay();Pass(Draw().SequenceEqual(image),"Stop restores Edit");InspectCurrent();
                    }
                    var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
                    foreach(Action forbidden in new Action[]{()=>owner.Document.CreateObject("forbidden Build write"),()=>renderer.Dispose()})
                    {
                        onBuild=forbidden;Set(active with{ExposureEv=-1});
                        byte[] authorBeforeCallback=owner.Document.CaptureBytes();var clear=Draw();
                        Pass(owner.Document.CaptureBytes().SequenceEqual(authorBeforeCallback),"rejected render callback preserves exact authoring bytes");
                        Pass(clear.Where((b,i)=>b!=clear[i%4]).Any()==false,"Build callback rejects World write/renderer unload and clears");
                        onBuild=null;Set(active);Pass(Draw().SequenceEqual(image),"explicit recovery after callback fault");
                    }
                    Pass(owner.Document.GetObjectIds().All(o=>owner.Document.GetObjectName(o)!="forbidden Build write"),"no callback World mutation");
                    broken=true;Set(active with{ExposureEv=-1});var fault=Draw();Pass(fault.Where((b,i)=>b!=fault[i%4]).Any()==false,"late invalid graph themed clear");
                    broken=false;Set(active);Pass(Draw().SequenceEqual(image),"invalid graph recovery");
                    var view=editor.VerificationPresenter;view.AttachLayout(new(Path.Combine(output,$"layout-{mode}.json")));Draw();var vf=view.Frame;
                    GuiEvent Split(uint phase)=>new(){Kind=(uint)GuiItemKind.Splitter,WidgetHigh=32,WidgetLow=1,Phase=phase,Value=320,Frame=vf.Frame,ViewGeneration=vf.ViewGeneration,DocumentGeneration=vf.DocumentGeneration,Revision=vf.Revision};
                    view.Apply([Split(1),Split(2),Split(3)],[]);Image(Draw(),"resized custom graph");Pass(editor.VerificationViewport!=size,"formal splitter resize");
                    for(int i=0;i<8;i++)Draw(false);
                    int built=builds;var beforePost=renderer.PostStats;var beforeEnvironment=renderer.EnvironmentStats;var beforeResource=renderer.ResourceStats;var beforeMesh=renderer.SceneStats;var beforeScene=renderer.PipelineStats;
                    long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();for(int i=0;i<16;i++)Draw(false);double elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                    Pass(builds==built && renderer.PipelineStats.Creates==beforeScene.Creates && renderer.PostStats.Publications==beforePost.Publications && renderer.EnvironmentStats.UploadedBytes==beforeEnvironment.UploadedBytes && renderer.EnvironmentStats.Captures==beforeEnvironment.Captures && renderer.ResourceStats.UploadedBytes==beforeResource.UploadedBytes && renderer.SceneStats.UploadedBytes==beforeMesh.UploadedBytes,"warm no callback/rebuild/immutable upload/environment readback");
                    var stats=renderer.Stats;Pass(stats.ValidationErrors==0&&stats.ValidationWarnings==0,"Editor API0/0");
                    var scene=EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_editScene");var pipeline=EnvironmentProductEvidence.Field<ScenePipelineSession>(scene,"_pipeline");
                    Pass(renderer.PostStats.Bindings==1&&renderer.PostStats.ResidentBytes==pipeline.PostProcess!.ResidentBytes&&renderer.EnvironmentStats.ResidentBytes<=renderer.EnvironmentStats.BudgetBytes,"actual current graph post/environment resource budget");
                    costs.Add(new{mode,frames=16,allocatedBytes=bytes,cpuMilliseconds=elapsed,stats.GpuMilliseconds,stats.GpuSampleValid,gpuOriginFrameKnown=false,fullPerformanceAccepted=false,pipeline=renderer.PipelineStats,post=renderer.PostStats,environment=renderer.EnvironmentStats,meshes=renderer.SceneStats,resources=renderer.ResourceStats});
                    if(relocated)Pass(EnvironmentProductEvidence.NativePaths(root).Length>0,"exact relocated native modules");
                }
                Pass(!GraphicsLoaded(),"Editor renderer/platform/gui unload");
            }
        }
        if(relocated)DeploymentManifest.Validate(root);
        File.WriteAllText(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(new{checks,builds,kind,relocated,images,costs,manualAccepted=false},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
    }
    private static bool GraphicsLoaded()=>Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName is "NcmaRenderer.dll" or "NcmaPlatform.dll" or "NcmaGui.dll");
    private sealed class EcsCommittedProbe : Ncma.Runtime.ICommittedStepObserver
    {
        private readonly Ncma.Runtime.World _world;
        internal readonly Ncma.Runtime.EntityQuery Query;
        internal ulong LastTick;
        internal int Steps;
        internal EcsCommittedProbe(Ncma.Runtime.World world) { _world=world;Query=world.Query<Ncma.Runtime.TransformData>();LastTick=world.Tick; }
        public void StepCommitted(Ncma.Runtime.World world,double delta)
        {
            if(!ReferenceEquals(world,_world)||world.Tick!=LastTick+1||Query.Count==0)throw new Exception("E4-E committed Player query identity/tick.");
            foreach(var entity in Query){_ = world.Get<Ncma.Runtime.TransformData>(entity);if(world.FindEntity(world.GetUuid(entity))!=entity)throw new Exception("E4-E Player UUID resolution.");}
            bool refused=false;try{world.CreateEntity("forbidden observer write");}catch(InvalidOperationException){refused=true;}
            if(!refused)throw new Exception("E4-E observer gained World write authority.");
            LastTick=world.Tick;Steps++;
        }
    }
    private sealed class CallbackPipeline(Func<uint,uint,RenderGraph> build):RenderPipeline {public override RenderGraph Build(uint w,uint h)=>build(w,h);}
}
