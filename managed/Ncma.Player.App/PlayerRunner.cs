using System.Diagnostics;
using System.Security.Cryptography;
using Ncma.Application;
using Ncma.Application.Runtime;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Physics;
using Ncma.Rendering;
using Ncma.Scene;
namespace Ncma.Player.App;

public sealed partial class PlayerRunner
{
    // Trusted local host only. No EditorEndpoint, hidden EditSession or Agent execution channel.
    public PlayerReport Run(PlayerOptions options, Func<bool>? cancelled = null, string? pluginRoot = null,
        bool visible = true, Action<PlaySession, ulong>? trustedInput = null, Ncma.Rendering.Scene.PostProcessModules? postModules = null,
        Func<float,float,bool,RenderPipeline>? pipelineFactory = null, SceneUiActions? uiActions = null,
        Func<PlayerGraphicsRecoveryRequest,bool>? confirmGraphicsRecovery=null)
        =>RunCore(options,cancelled,pluginRoot,visible,trustedInput,null,postModules,pipelineFactory,uiActions,confirmGraphicsRecovery);
    // Internal bounded diagnostic hook, not a production/Agent screenshot or execution capability.
    internal PlayerReport RunForVerification(PlayerOptions options,string pluginRoot,Action<byte[],uint,uint> capture,
        Action<PlaySession,ulong>? trustedInput=null,Ncma.Rendering.Scene.PostProcessModules? postModules=null,
        Func<float,float,bool,RenderPipeline>? pipelineFactory=null, SceneUiActions? uiActions=null,
        Func<PlayerGraphicsRecoveryRequest,bool>? confirmGraphicsRecovery=null)=>RunCore(options,null,pluginRoot,false,trustedInput,capture,postModules,pipelineFactory,uiActions,confirmGraphicsRecovery);
    private PlayerReport RunCore(PlayerOptions options,Func<bool>? cancelled,string? pluginRoot,bool visible,
        Action<PlaySession,ulong>? trustedInput,Action<byte[],uint,uint>? capture,Ncma.Rendering.Scene.PostProcessModules? postModules=null,
        Func<float,float,bool,RenderPipeline>? pipelineFactory=null, SceneUiActions? uiActions=null,Func<PlayerGraphicsRecoveryRequest,bool>? confirmGraphicsRecovery=null)
    {
        ArgumentNullException.ThrowIfNull(options);BeginRun();
        try { return ExecuteCore(options,cancelled,pluginRoot,visible,trustedInput,capture,postModules,pipelineFactory,uiActions,confirmGraphicsRecovery); }
        finally { try{if(_phase==PlayerRunnerPhase.Running)TryShutdown();}finally{_runActive=false;} }
    }
    private PlayerReport ExecuteCore(PlayerOptions options,Func<bool>? cancelled,string? pluginRoot,bool visible,
        Action<PlaySession,ulong>? trustedInput,Action<byte[],uint,uint>? capture,Ncma.Rendering.Scene.PostProcessModules? postModules,
        Func<float,float,bool,RenderPipeline>? pipelineFactory,SceneUiActions? uiActions,Func<PlayerGraphicsRecoveryRequest,bool>? confirmGraphicsRecovery)
    {
        var clock=Stopwatch.StartNew();ProjectContext? project=null;PlayStatus? status=null;
        postModules??=Ncma.Rendering.Scene.PostProcessModules.Official();
        int exit = 0, count = 0; string reason = "completed", renderer = options.Headless ? "null" : "unknown";
        string? digest = null; string phase = "configuration"; var errors = new List<string>(); PlayerModule[] modules = [];
        PresentationCostReport? costs=null;
        Ncma.Characters.SceneSimulationCostReport? simulationCosts=null;
        LoopCostCapture? loopCosts=null;
        LoopCostStream? loopStream=null;
        LoopStreamReport? streamReport=null;
        PresentationStageCapture? stages=null;
        PlayerOutputReport? output=null;
        ProcessMemoryCapture? memory=null;
        RendererMemoryCapture? resourceMemory=null;
        ulong rendered = 0, validationErrors = 0, validationWarnings = 0; bool running = false;
        try
        {
            options.ValidateRun(); PlayerReports.ValidateTarget(options.Report);
            if(options.MemoryCheckpoints) {memory=new();memory.Observe(MemoryCheckpoint.BeforeStartup);resourceMemory=new();resourceMemory.Observe(RendererMemoryCheckpoint.BeforeStartup,null);}
            if(options.LoopStream is not null)loopStream=LoopCostStream.Open(options.LoopStream,options.FrameRateLimit);
            if(options.LoopCosts)loopCosts=new(frameRateLimit:options.FrameRateLimit,stream:loopStream);
            if(options.PresentationStages)stages=new();
            FramePacer? pacer=null;
            project = ProjectContext.Load(options.Project ?? throw new ArgumentException("project_required"));
            renderer = options.EffectiveRenderer(project);
            if (renderer == "vulkan" || !options.Headless && renderer == "null") { exit = 7; reason = "renderer_unimplemented"; }
            else
            {
                if (project.Configuration.Plugins.Length != 0) throw new NotSupportedException("project_plugin_overrides_unimplemented");
                var runtimeScene = LoadRuntimeScene(project);
                if (Ncma.Scene.Rendering.SceneUiState.Resolve(runtimeScene.CaptureSnapshot(),project.Configuration.UiPackage) is { } uiSelection) {
                    phase = "dependencies";
                    uiPackage = Ncma.Ui.Rendering.RuntimeUiPackage.Open(project.Root, project.Configuration.ProjectId, uiSelection);
                    Ncma.Ui.Rendering.SceneUiPresentation.Validate(uiPackage, uiActions is not null);
                    uiActions?.Validate(uiPackage.Capture());
                }
                Ncma.Rendering.Scene.ScenePostProcessPreflight.ValidateValues(runtimeScene.CaptureSnapshot(),postModules);
                if(options.Headless&&project.Configuration.PostProcessLibrary is not null&&Ncma.Rendering.Scene.ScenePostProcessPreflight.HasSettings(runtimeScene.CaptureSnapshot())&&Ncma.Rendering.Scene.ScenePostProcessPreflight.HasGeometry(runtimeScene.CaptureSnapshot())) {
                    phase="dependencies";postLibrary=RuntimePostProcessLibrary.ForHost(AppContext.BaseDirectory,project.Root,project.Configuration.PostProcessLibrary);
                    Ncma.Rendering.Scene.ScenePostProcessPreflight.ValidateFiles(runtimeScene.CaptureSnapshot(),postLibrary);
                }
                bool hasPhysics = Ncma.Characters.CharacterComponents.HasPhysics(runtimeScene.CaptureSnapshot());
                if(hasPhysics && !project.Configuration.PhysicsEnabled)throw new ArgumentException("physics_bindings_require_enabled_project");
                if (options.Headless && (project.Configuration.AssetPackage is not null || runtimeScene.World.Query<Ncma.Animation.RootMotionData>().Count != 0 || runtimeScene.World.Query<Ncma.Animation.AnimatorData>().Count != 0)) {
                    phase = "dependencies";
                    renderAssets = Ncma.Scene.Rendering.SceneAssetPreparation.Prepare(project.Root, project.Configuration.ProjectId, runtimeScene.CaptureSnapshot(), true, project.Configuration.AssetPackage);
                }
                if (!options.Headless)
                {
                    phase = "dependencies";
                    var startup = runtimeScene.CaptureSnapshot();
                    shaderFiles = RuntimeShaderFileSet.ForHost(AppContext.BaseDirectory,project.Root,project.Configuration.ShaderPackages);
                    if (uiPackage is not null && shaderFiles is not null) _ = shaderFiles.Get(ShaderProfile.Flat2D,false,false);
                    Ncma.Rendering.Scene.SceneEnvironmentPreflight.RequireShaderClosure(shaderFiles,startup);
                    if(Ncma.Scene.Rendering.SceneShadowState.Read(startup)?.Quality==Ncma.Assets.SceneShadowQuality.High&&Ncma.Rendering.Scene.ScenePostProcessPreflight.HasGeometry(startup))
                        shadowLibrary=RuntimeShadowLibrary.ForHost(AppContext.BaseDirectory,project.Root,project.Configuration.ShadowLibrary);
                    if(Ncma.Rendering.Scene.ScenePostProcessPreflight.HasSettings(startup)&&Ncma.Rendering.Scene.ScenePostProcessPreflight.HasGeometry(startup)) {
                        postLibrary=RuntimePostProcessLibrary.ForHost(AppContext.BaseDirectory,project.Root,project.Configuration.PostProcessLibrary);
                        Ncma.Rendering.Scene.ScenePostProcessPreflight.ValidateFiles(startup,postLibrary);
                    }
                    renderAssets = Ncma.Scene.Rendering.SceneAssetPreparation.Prepare(project.Root, project.Configuration.ProjectId, startup, true, project.Configuration.AssetPackage);
                    if (Ncma.Scene.Rendering.SceneAssetPreparation.References(startup).Length != 0)
                    {
                        phase = "configuration";
                        if (project.Configuration.SceneCamera is not { } camera ||
                            !runtimeScene.World.TryFindEntity(camera, out var cameraEntity) || !runtimeScene.World.Has<Ncma.Scene.Rendering.CameraData>(cameraEntity))
                            throw new ArgumentException("scene_camera_selection_required");
                    }
                }
                string plugins = Path.GetFullPath(pluginRoot ?? Path.Combine(AppContext.BaseDirectory, "plugins"));
                phase = "dependencies"; physics = new(plugins, project.Configuration.PhysicsEnabled,characterSupport:hasPhysics);
                if (physics.Inspect() is { Enabled: true } inspected) modules = [new("ncma.physics", inspected.AbiMajor, inspected.AbiMinor, (ulong)inspected.Capabilities)];
                if (!options.Headless)
                {
                    presentation = new(plugins, visible, pipelineFactory,options.PresentationCosts,loopCosts,options.FrameRateLimit,stages,options.WindowSize,options.WindowMode??Ncma.Platform.WindowMode.Windowed,resourceMemory); presentation.Start(shaderFiles,postLibrary,postModules,shadowLibrary);
                    pacer=presentation.Pacer;
                    presentation.PrepareBeforeGameplay(runtimeScene,renderAssets!,uiPackage,uiActions);
                    CheckRun();modules = modules.Concat(presentation.Modules).ToArray();
                }
                Ncma.Characters.ScenePlayRuntime? sceneRuntime=null;
                owner = new();
                owner.LoadGameplay(project.GameplayAssemblyPath, runtimeScene);
                phase = "initialize"; var play = owner.StartPlay(runtimeScene, factory: d => new PlaySession(d,
                    options.Headless ? FrameTimePolicy.Strict : FrameTimePolicy.Interactive, options.FixedDelta,
                    advanceMode: options.Headless ? PlayAdvanceMode.FixedSteps : PlayAdvanceMode.Frames),
                    compose:p=>{ if (uiPackage is not null) uiActions?.Install(p); return sceneRuntime=Ncma.Characters.ScenePlayRuntime.Compose(p,physics,renderAssets,profile:options.SimulationCosts); });
                if (presentation is not null) presentation.BindScene(play, renderAssets!, project.Configuration.SceneCamera,sceneRuntime?.Characters,sceneRuntime?.Animators);
                CheckRun();memory?.Observe(MemoryCheckpoint.RuntimeReady);
                if(presentation is not null)presentation.ObserveResourceMemory(RendererMemoryCheckpoint.RuntimeReady);else resourceMemory?.Observe(RendererMemoryCheckpoint.RuntimeReady,null);
                running = true; double previous = clock.Elapsed.TotalSeconds;
                while (options.Ticks is null || play.Tick < options.Ticks)
                {
                    loopCosts?.Begin();
                    var loopOutcome=LoopCostOutcome.Faulted;
                    try {
                    bool stop=cancelled?.Invoke()==true;CheckRun();
                    if (stop) { exit = 130; reason = "cancelled"; loopOutcome=LoopCostOutcome.Stopped; break; }
                    if (clock.Elapsed.TotalSeconds >= options.MaxRuntimeSeconds) { exit = 8; reason = "time_budget"; loopOutcome=LoopCostOutcome.Stopped; break; }
                    if(pacer is not null) {
                        loopCosts?.Phase(LoopCostPhase.Pacing);
                        bool admitted=pacer.Wait(cancelled);CheckRun();
                        if(!admitted){exit=130;reason="cancelled";loopOutcome=LoopCostOutcome.Stopped;break;}
                        loopCosts?.PacingAdmitted(pacer.LastAdmissionTimestamp!.Value);
                        if(clock.Elapsed.TotalSeconds>=options.MaxRuntimeSeconds){exit=8;reason="time_budget";loopOutcome=LoopCostOutcome.Stopped;break;}
                    }
                    loopCosts?.Phase(LoopCostPhase.Platform);
                    if (presentation is not null && !presentation.Pump(play)) { reason = "window_closed"; loopOutcome=LoopCostOutcome.Stopped; break; }
                    loopCosts?.Phase(LoopCostPhase.Input);
                    trustedInput?.Invoke(play, play.Tick + 1);CheckRun();
                    loopCosts?.Phase(LoopCostPhase.Simulation);
                    if (options.Headless) {
                        status = play.AdvanceFixedStep();
                        if (options.SimulationCosts) simulationCosts=sceneRuntime?.ReadSimulationCosts();
                    }
                    else
                    {
                        double now = clock.Elapsed.TotalSeconds;
                        status = play.AdvanceFrame(now - previous); previous = now;
                        if (options.SimulationCosts) simulationCosts=sceneRuntime?.ReadSimulationCosts();
                        loopCosts?.Phase(LoopCostPhase.Presentation);
                        try{presentation!.Present(play,capture);}
                        catch when(confirmGraphicsRecovery is not null&&play.State is PlayState.Running or PlayState.Paused){
                            loopCosts?.Phase(LoopCostPhase.Recovery);
                            // Only presentation failures enter this path. Never replay input or the
                            // already committed gameplay frame, and never recover a faulted script.
                            for(int attempt=0;;attempt++){
                                if(attempt==16||clock.Elapsed.TotalSeconds>=options.MaxRuntimeSeconds)throw;
                                var review=presentation!.ReviewGraphicsRecovery(true);
                                bool approved=confirmGraphicsRecovery(new(review,attempt!=0,presentation.FirstGraphicsFailure,presentation.FirstNativeFailure));CheckRun();
                                if(!approved)throw;
                                try{presentation.RebuildGraphics(review);}
                                catch{CheckRun();continue;}
                                CheckRun();previous=clock.Elapsed.TotalSeconds;break;
                            }
                            loopOutcome=LoopCostOutcome.Recovered;
                        }
                        CheckRun();
                    }
                    if (status.State == PlayState.Faulted) { exit = 5; reason = "gameplay_fault"; break; }
                    if(loopOutcome!=LoopCostOutcome.Recovered)loopOutcome=LoopCostOutcome.Completed;
                    } finally { loopCosts?.End(status?.FrameCount??0,loopOutcome); }
                }
                status = play.Status; count = play.Scene.World.Count;
                digest = Convert.ToHexString(SHA256.HashData(play.Scene.CaptureCheckpoint().CopyBytes()));
                if (presentation is not null)
                {
                    rendered = presentation.TotalSubmittedFrames;
                    validationErrors = presentation.TotalValidationErrors; validationWarnings = presentation.TotalValidationWarnings;
                    if (validationErrors != 0 || validationWarnings != 0) { exit = 5; reason = "graphics_validation_failed"; }
                }
            }
        }
        catch (Exception e)
        {
            Exception? diagnosticError=e;
            for(int diagnosticDepth=0;diagnosticError is not null&&diagnosticDepth<8;diagnosticDepth++,diagnosticError=diagnosticError.InnerException){
                if(diagnosticError is PluginException{NativeDiagnostic:{} diagnostic}){LastNativeFailureDiagnostic??=diagnostic;break;}
            }
            status = owner?.Play?.Status ?? owner?.LastStartFailure ?? status;
            exit = status?.State == PlayState.Faulted || running ? 5 : e switch
            { FileNotFoundException or BadImageFormatException or DllNotFoundException or PluginException => 3,
                NotSupportedException => 7, ArgumentException or IOException or System.Text.Json.JsonException when phase == "dependencies" => 3,
                ArgumentException or System.Text.Json.JsonException => 2, _ => 4 };
            reason = exit switch { 2 => "invalid_configuration", 3 => "dependency_failed", 5 => "gameplay_fault", 7 => "feature_unimplemented", _ => "initialize_failed" };
        }
        finally
        {
            try {
            costs=presentation?.LastPresentationCosts;
            output=presentation?.CaptureOutput();
            LastPresentationFailure=presentation?.FirstGraphicsFailure??presentation?.SurfaceStatus?.Failure;
            LastNativeFailureDiagnostic??=presentation?.FirstNativeFailure;
            memory?.Observe(MemoryCheckpoint.BeforeShutdown);
            if(presentation is not null)presentation.ObserveResourceMemory(RendererMemoryCheckpoint.BeforeShutdown);else resourceMemory?.Observe(RendererMemoryCheckpoint.BeforeShutdown,null);
            if(!TryShutdown())errors.Add(_failedService!);
            else {memory?.Observe(MemoryCheckpoint.AfterShutdown);resourceMemory?.Observe(RendererMemoryCheckpoint.AfterShutdown,null);}
            } finally { streamReport=loopStream?.Complete(); }
        }
        if(_runRejected){if(exit==0)exit=running?5:4;if(errors.Count==0)reason="host_callback_reentry";}
        if (exit == 0 && errors.Count != 0) { exit = 6; reason = "shutdown_failed"; }
        if(exit==0 && streamReport is {CompleteRawCoverage:false}) {exit=9;reason="loop_stream_incomplete";}
        var fault = status?.Fault is { } f ? f with { Message = "Gameplay callback failed." } : null;
        // Report v2 hashes the explicit ncma.runtime-scene v1 state, not an authoring document.
        var report = new PlayerReport(2, options.Headless ? "headless" : "graphical", renderer, project?.Configuration.ProjectId ?? Guid.Empty,
            status?.SessionId ?? Guid.Empty, status?.WorldId ?? Guid.Empty, options.Ticks, status?.Tick ?? 0, options.FixedDelta,
            status?.State ?? PlayState.Stopped, exit, reason, fault, errors.ToArray(), null, digest, count,
            clock.Elapsed.TotalSeconds, modules, rendered, validationErrors, validationWarnings) { PresentationCosts=costs, SimulationCosts=simulationCosts, LoopCosts=loopCosts?.Capture(), FrameRateLimit=options.FrameRateLimit, PresentationStages=stages?.Capture(), Output=output, MemoryCheckpoints=memory?.Capture(), ResourceMemory=resourceMemory?.Capture() };
        try { PlayerReports.Write(options.Report, report); }
        catch { report = report with { ReportError = "report_write_failed", ExitCode = exit == 0 ? 9 : exit,
            Reason = exit == 0 ? "report_write_failed" : reason }; }
        LastReport=report;return report;
    }
}
