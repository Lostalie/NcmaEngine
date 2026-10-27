using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Physics;
using Ncma.Rendering;
using Ncma.Assets.Authoring;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.App;
internal static class Program
{
    internal static bool StartupCostsRequested(string[] args)
    {
        // Only option positions opt in. A path/value may legitimately equal a flag.
        for(int i=0;i<args.Length;i++) {
            if(args[i]=="--startup-costs")return true;
            if(args[i] is "--project" or "--plugins" or "--frames" or "--max-fps" or "--loop-stream")i++;
        }
        return false;
    }
    // One process-owned failed lifetime, not an orphan registry or forced unload.
    // Embedded hosts keep their ApplicationLifetime and explicitly retry close.
    private static ApplicationLifetime? _execution;
    internal static string ErrorText(Exception error,ApplicationLifetime? execution)=>
        EditorUserDataStartupException.Format(error) ?? (
        error is Ncma.Ui.Rendering.SceneUiRoutesRequiredException?Ncma.Ui.Rendering.SceneUiRoutesRequiredException.Diagnostic:
        error is ArgumentException&&error.Message==PluginLoader.InvalidRootDiagnostic?PluginLoader.InvalidRootDiagnostic:
        execution?.Failure?.Code??error.Message);
    // Synchronous Main is the sole owner; no hostfxr, unmanaged Run or async continuation.
    [STAThread]
    public static int Main(string[] args)
    {
        ApplicationLifetime? execution=null;
        LoopCostCapture? loopCosts=null;
        LoopCostStream? loopStream=null;
        LoopStreamReport? streamReport=null;
        PresentationStageCapture? stages=null;
        ProcessMemoryCapture? memory=null;
        RendererMemoryCapture? resourceMemory=null;
        CandidatePresentation? presentation=null;
        bool memoryBeforeShutdown=false,memoryAfterShutdown=false;
        StartupCostCapture? startup=StartupCostsRequested(args)?new():null;
        try {
            startup?.Begin(StartupCall.MainPreparation);
            startup?.Phase(StartupStage.PackageValidation);
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, DeploymentManifest.FileName))) DeploymentManifest.Validate(AppContext.BaseDirectory);
            if (args.SequenceEqual(new[] { "--validate-package" })) return 0;
            startup?.Phase(StartupStage.Arguments);
            if (args.Length == 0) args = ["--editor"];
            string? projectPath = null, plugins = null; bool smoke = false, cpuOnly = false, preview = false, editor = false, costs=false, simulationCosts=false, observeLoop=false, observeStages=false, observeMemory=false; ulong frames = ulong.MaxValue;
            int? frameRateLimit=null;string? loopStreamPath=null;
            var flags = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++) {
                if (!flags.Add(args[i])) { Console.Error.WriteLine("duplicate_arguments"); return 2; }
                switch (args[i]) {
                    case "--presentation-costs": costs=true; break;
                    case "--simulation-costs": simulationCosts=true; break;
                    case "--loop-costs": observeLoop=true; break;
                    case "--loop-stream" when i+1<args.Length:
                        loopStreamPath=Path.GetFullPath(args[++i]);observeLoop=true;break;
                    case "--presentation-stages": observeStages=true; break;
                    case "--startup-costs": break;
                    case "--memory-checkpoints": observeMemory=true;break;
                    case "--max-fps" when i+1<args.Length && int.TryParse(args[++i],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int rate) && rate is >=1 and <=240: frameRateLimit=rate;break;
                    case "--preview" when !preview: preview = true; break;
                    case "--editor" when !editor: editor = true; break;
                    case "--cpu-only" when !cpuOnly: cpuOnly = true; break;
                    case "--project" when i + 1 < args.Length: projectPath = args[++i]; break;
                    case "--plugins" when i + 1 < args.Length: plugins = args[++i]; break;
                    case "--smoke-test" when !smoke: smoke = true; if (!flags.Contains("--frames")) frames = 8; break;
                    case "--frames" when i + 1 < args.Length && ulong.TryParse(args[++i], out ulong limit) && limit is > 0 and <= 1000000: frames = limit; break;
                    default: Console.Error.WriteLine("invalid_arguments"); return 2;
                }
            }
            if (preview && (smoke || editor || projectPath is not null) || editor && smoke) { Console.Error.WriteLine("invalid_preview_arguments"); return 2; }
            if (projectPath is null && !smoke && !preview && !editor) { Console.Error.WriteLine("project_required"); return 2; }
            if(observeMemory){memory=new();memory.Observe(MemoryCheckpoint.BeforeStartup);resourceMemory=new();resourceMemory.Observe(RendererMemoryCheckpoint.BeforeStartup,null);}
            startup?.Phase(StartupStage.Project);
            ProjectContext? project = projectPath is null ? null : ProjectContext.Load(projectPath);
            if (project is not null && project.Configuration.Renderer != "Direct3D11") {
                Console.Error.WriteLine("renderer_unimplemented: " + project.Configuration.Renderer); return 3;
            }
            if (project?.Configuration.Plugins.Length > 0) {
                Console.Error.WriteLine("project_plugin_overrides_unimplemented"); return 3;
            }
            // Release candidate explicitly chooses its sibling plugin directory; never cwd/PATH.
            string pluginRoot = Path.GetFullPath(plugins ?? Path.Combine(AppContext.BaseDirectory, "plugins"));
            if(_execution is not null){Console.Error.WriteLine("editor_shutdown_retained");return 4;}
            startup?.Phase(StartupStage.HostConstruction);
            var lifetime = execution = new ApplicationLifetime();_execution=lifetime;
            if(loopStreamPath is not null)loopStream=LoopCostStream.Open(loopStreamPath,frameRateLimit);
            if(observeLoop)loopCosts=new(frameRateLimit:frameRateLimit,stream:loopStream);
            if(observeStages)stages=new();
            presentation = new CandidatePresentation(pluginRoot, project, smoke, cpuOnly, preview,presentationCosts:costs,simulationCosts:simulationCosts,loopCosts:loopCosts,frameRateLimit:frameRateLimit,stages:stages,startup:startup,resourceMemory:resourceMemory);
            startup?.End(true);
            lifetime.Start([presentation]);
            memory?.Observe(MemoryCheckpoint.RuntimeReady);
            presentation.ObserveResourceMemory(RendererMemoryCheckpoint.RuntimeReady);
            var confirmation=new WindowsConfirmation();
            ulong rendered = EditorRunLoop.Run(lifetime,presentation,new MonotonicClock(),frames,
                smoke||preview?null:request=>confirmation.Confirm(request.Message,"NcmaEngine 图形恢复"),loopCosts,presentation.Pacer);
            if(memory is not null){memory.Observe(MemoryCheckpoint.BeforeShutdown);memoryBeforeShutdown=true;}
            presentation.ObserveResourceMemory(RendererMemoryCheckpoint.BeforeShutdown);
            lifetime.Dispose();
            if(memory is not null){memory.Observe(MemoryCheckpoint.AfterShutdown);memoryAfterShutdown=true;}
            resourceMemory?.Observe(RendererMemoryCheckpoint.AfterShutdown,null);
            streamReport=loopStream?.Complete();
            if(costs)Console.WriteLine(JsonSerializer.Serialize(new { presentationCosts=presentation.LastPresentationCosts }));
            if(simulationCosts)Console.WriteLine(JsonSerializer.Serialize(new { simulationCosts=presentation.LastSimulationCosts }));
            Console.WriteLine($"candidate_{(cpuOnly ? "cpu_gui" : (preview||smoke)&&project is null ? "dx11_reference" : "dx11_scene")} frames={rendered}; resources released");
            return streamReport is {CompleteRawCoverage:false}?9:0;
        } catch (ArgumentException e) { Console.Error.WriteLine("invalid_configuration: " + ErrorText(e,execution)); return 2; }
        catch (Exception e) { Console.Error.WriteLine("candidate_failed: " + ErrorText(e,execution)); return 4; }
        finally {
            if(startup?.ActiveCall==StartupCall.MainPreparation)startup.End(false);
            if(execution is not null) {
                // Start already attempted cleanup on failure; a failed close is
                // NEVER automatically retried by a second using/finally Dispose.
                if(execution.RetainedServiceCount!=0&&execution.ShutdownAttempts==0) {
                    if(memory is not null&&!memoryBeforeShutdown){memory.Observe(MemoryCheckpoint.BeforeShutdown);memoryBeforeShutdown=true;}
                    presentation?.ObserveResourceMemory(RendererMemoryCheckpoint.BeforeShutdown);
                    try{execution.Dispose();}catch{Console.Error.WriteLine("editor_shutdown_retained");}
                }
                // Start may have already cleaned up a failed initialization. Do not invent a
                // pre-shutdown sample after the fact or retry a retained failed close.
                if(execution.RetainedServiceCount==0&&memory is not null&&!memoryAfterShutdown){
                    memory.Observe(MemoryCheckpoint.AfterShutdown);memoryAfterShutdown=true;
                    resourceMemory?.Observe(RendererMemoryCheckpoint.AfterShutdown,null);
                }
                if(execution.RetainedServiceCount==0&&ReferenceEquals(_execution,execution))_execution=null;
            }
            if(loopStream is not null&&streamReport is null)streamReport=loopStream.Complete();
            if(loopCosts is not null)Console.WriteLine(JsonSerializer.Serialize(new { loopCosts=loopCosts.Capture() }));
            if(stages is not null)Console.WriteLine(JsonSerializer.Serialize(new { presentationStages=stages.Capture() }));
            if(startup is not null)Console.WriteLine(JsonSerializer.Serialize(new { startupCosts=startup.Capture() }));
            if(memory is not null)Console.WriteLine(MemoryDiagnosticJson.Process(memory.Capture()));
            if(resourceMemory is not null)Console.WriteLine(MemoryDiagnosticJson.Resource(resourceMemory.Capture()));
        }
    }
}
internal sealed unsafe partial class CandidatePresentation(string plugins, ProjectContext? project, bool smoke, bool cpuOnly, bool preview,
    Func<Ncma.Assets.EnvironmentPresetLibrary>? environmentPresets = null, Ncma.Rendering.Scene.PostProcessModules? postProcessModules = null,
    Func<float,float,bool,RenderPipeline>? pipelineFactory = null, Ncma.Application.Runtime.SceneUiActions? uiActions = null, bool presentationCosts=false, bool simulationCosts=false, LoopCostCapture? loopCosts=null,int? frameRateLimit=null,PresentationStageCapture? stages=null,StartupCostCapture? startup=null,RendererMemoryCapture? resourceMemory=null) : IApplicationService, IFramePipeline
{
    internal static void RequireComposition(Ncma.Scene.ISceneContent snapshot)
    {
        Ncma.Characters.CharacterComponents.RequireComposition(snapshot);
    }
    private bool ReferencePreview => (preview||smoke) && project is null;
    private RendererSession? _costRenderer;
    private PresentationCostCapture? _costCapture;
    internal PresentationCostReport? LastPresentationCosts => _costCapture?.Last;
    private Ncma.Characters.SceneSimulationCostReport? _lastSimulationCosts;
    internal Ncma.Characters.SceneSimulationCostReport? LastSimulationCosts => _lastSimulationCosts;
    private readonly PluginLoader _loader = new();
    private PhysicsService? _physics;
    private PlatformWindow? _window;
    private PlatformPrecisionWait? _precisionWait;
    internal FramePacer? Pacer { get; private set; }
    private GuiSession? _gui;
    private RendererSession? _renderer;
    internal void ObserveResourceMemory(RendererMemoryCheckpoint checkpoint)=>resourceMemory?.Observe(checkpoint,_renderer);
    private RendererWindowSurface? _windowSurface;
    private RuntimeShaderFileSet? _shaderFiles;
    private GpuViewTarget? _viewportTarget, _pendingViewportTarget;
    private RenderPipelineService? _renderService;
    private Ncma.Rendering.RenderResourceCache? _sceneCache;
    private Ncma.Rendering.Scene.SceneRenderSession? _editScene, _playScene, _pendingScene;
    private Ncma.Animation.Native.PoseKernel? _poseKernel;
    private Ncma.Characters.CharacterPlayRuntime? _characterRuntime;
    private Ncma.Characters.ScenePlayRuntime? _sceneRuntime;
    private Guid _editAssets, _playAssets, _playSession;
    private CompiledRenderGraph? _graph;
    private uint _viewWidth, _viewHeight;
    private ulong _configurationRevision = ulong.MaxValue;
    private Guid _configurationWorld;
    private Ncma.Runtime.World? _configurationQueryWorld;
    private Ncma.Scene.Rendering.SceneComponentReader<RenderConfiguration>? _configurationReader;
    private RenderConfiguration _configuration;
    private EditorSessionOwner? _editor;
    private EditorAssetWorkflow? _assetWorkflow;
    private EditorPresenter? _presenter;
#if NCMA_AI
    private Ncma.Editor.Modules.AiAuthoringWorkspace? _aiAuthoring;
#endif
    private EditorUiWorkspace? _uiWorkspace;
    private EditorUiPreview? _uiPreview;
    private Ncma.Ui.Rendering.RuntimeUiPackage? _sceneHudPackage;
    private Ncma.Ui.Rendering.SceneUiPresentation? _sceneHud;
    private EditorAnimationGraphWorkspace? _animationGraphs;
    private EditorAnimationGraphPreview? _graphPreview;
    private FbxPreviewSession? _fbxPreview;
    private ActionPreviewSession? _animationPreview;
    private EditorPreferencesStore? _preferences;
    private WindowsFilePicker? _filePicker;
    private NativeDiagnosticsReader? _nativeDiagnostics;
    private GuiCapture _capture;
    private readonly ulong[] _held = new ulong[8], _pressed = new ulong[8], _released = new ulong[8];
    private double _previousPointerX, _previousPointerY;
    private bool _pointerWasFocused;
    private readonly WindowInputAccumulator _input = new();
    private WindowState _state;
    private readonly GuiItem[] _items = new GuiItem[5];
    private string _presentationText = "测试 Ncma";
    private byte[] _labels = [];
    private uint _labelsLength;
    private byte[] _text = [];
    private bool _closed;
    private ApplicationLog? _log;
    private string? _userDataRoot;
    internal string VerificationUserDataRoot => _userDataRoot ?? throw new InvalidOperationException("Editor not started.");
    private readonly Guid _correlation = Guid.NewGuid();
    private string _lastPlatformDiagnostic = "";
    public void Start()
    {
        VerifyGraphicsOwner();
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_graphicsShutdown) throw new InvalidOperationException("Editor shutdown cannot be restarted.");
        startup?.Begin(StartupCall.PresentationStart);
        bool startupCompleted=false;
        try {
        startup?.Phase(StartupStage.ScenePreflight);
        _sceneHudSelection=project is null?null:Ncma.Scene.Rendering.SceneUiState.Resolve(Ncma.Scene.SceneDocumentCodec.Decode(File.ReadAllBytes(project.StartupScenePath)),project.Configuration.UiPackage);
        if (project is not null&&_sceneHudSelection is {} uiSelection) {
            if (cpuOnly) throw new NotSupportedException("Scene HUD requires graphical presentation.");
            _sceneHudPackage = Ncma.Ui.Rendering.RuntimeUiPackage.Open(project.Root,project.Configuration.ProjectId,uiSelection);
            Ncma.Ui.Rendering.SceneUiPresentation.Validate(_sceneHudPackage,uiActions is not null);
            uiActions?.Validate(_sceneHudPackage.Capture());
        }
        startup?.Phase(StartupStage.ShaderPreflight);
        if (!cpuOnly) _shaderFiles = RuntimeShaderFileSet.ForHost(AppContext.BaseDirectory,project?.Root,project?.Configuration.ShaderPackages);
        if (_sceneHudPackage is not null && _shaderFiles is not null) _ = _shaderFiles.Get(ShaderProfile.Flat2D,false,false);
        if(project is not null&&!cpuOnly)Ncma.Rendering.Scene.SceneEnvironmentPreflight.RequireShaderClosure(_shaderFiles,
            Ncma.Scene.SceneDocumentCodec.Decode(File.ReadAllBytes(project.StartupScenePath)));
        startup?.Phase(StartupStage.LogAndPlugins);
        _userDataRoot = EditorUserDataPaths.ForHost(project?.Root, smoke || preview, _correlation);
        _log = EditorUserDataStartupException.OpenLog(_userDataRoot);
        _loader.Load(plugins, [
            new("ncma.platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, frameRateLimit is null?0u:1u, []),
            new("ncma.renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["ncma.platform"]),
            new("ncma.gui", ModuleKind.Gui, "NcmaGui.dll", "NcmaGui.dll", 1, 8, ["ncma.platform", "ncma.renderer"]) ]);
        startup?.Phase(StartupStage.PacingAndPhysics);
        if(frameRateLimit is int fps) {
            FramePacer.ValidateRate(fps);
            _precisionWait=new(_loader.Modules.Single(m=>m.Kind==ModuleKind.Platform));
            Pacer=new(fps,_precisionWait.WaitMicroseconds);
        }
        // Explicit optional shared solver module; only bound Play creates numerical resources.
        _physics = new(plugins, project?.Configuration.PhysicsEnabled == true,characterSupport:project?.Configuration.PhysicsEnabled == true);
        var physics = _physics.Inspect();
        if (physics.Enabled)
            _log.Write("info", "physics.module_ready", $"Physics service ABI {physics.AbiMajor}.{physics.AbiMinor}; capabilities={physics.Capabilities}; explicit Play bindings only.", _correlation);
        startup?.Phase(StartupStage.Window);
        _window = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Platform), EditorPresenter.WindowTitle(project?.Configuration.Name), 1280, 720, !smoke);
        _window.SetIcon(Path.Combine(AppContext.BaseDirectory,"NcmaEngine.ico"));
        startup?.Phase(StartupStage.Preferences);
        string font=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc");
        if(!smoke && !preview)_preferences=new(Path.Combine(_userDataRoot,"editor/preferences.json"),font);
        startup?.Phase(StartupStage.GuiCreate);
        _gui = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Gui), _window, _preferences?.Current.FontPath??font,fontSize:_preferences?.Current.FontSize??18);
        startup?.Phase(StartupStage.Renderer);
        if (!cpuOnly) {
            _renderer = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), _window, 1280, 720);
            _windowSurface=new(_renderer);
            _shaderFiles?.InstallSelection(_renderer,()=>!_closed);
            _renderService = new(_renderer); _sceneCache=new(_renderer); _gui.AttachRenderer(_renderer);
        }
        startup?.Phase(StartupStage.ToolbarIcon);
        _gui.SetToolbarIcon(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NcmaEngine.ico")));
        _log.Write("info", cpuOnly ? "presentation.cpu_only" : ReferencePreview ? "presentation.dx11_reference" : "presentation.dx11_scene", "Managed candidate presentation initialized.", _correlation);
        startup?.Phase(StartupStage.EditorOwner);
        _editor = new(project?.Configuration.Name ?? "Presentation smoke",
            components: global::Ncma.Runtime.CompositionRegistry.Build(global::NcmaEngine.Generated.ComponentModule.Module),
            validateComposition: RequireComposition,
            composePreparedPlay:ComposePreparedPlay,postProcessModules:_postModules,
            assetPolicy:cpuOnly ? SceneAssetPolicy.Simulation : SceneAssetPolicy.PresentationAndSimulation,
            beforePlayStop:()=>{VerifyGraphicsOwner();SuspendSceneHudInput();_pendingScene?.Dispose();_pendingScene=null;_pendingEditProjection=null;_playScene?.Dispose();_playScene=null;_retainedPlayAnimation?.Dispose();_retainedPlayAnimation=null;_playSession=_playAssets=Guid.Empty;});
        var characters=new CharacterInspectionService(_editor,()=>_characterRuntime);characters.Register();
        var graphRuntime=new AnimationRuntimeInspectionService(_editor,()=>_sceneRuntime,()=>_playScene);graphRuntime.Register();
        var runtimeInspection=new RuntimeInspectionService(_editor);runtimeInspection.Register();
        if (project is not null) {
            startup?.Phase(StartupStage.Document);
            var workspace = new EditorWorkspace(_editor);
            workspace.Open(workspace.Stamp, project.StartupScenePath, true);
            _editor.Edit!.Resynchronize(); // Startup is the saved baseline, not a user Open command.
            _editor.ConfigureDomainProposals(project.Configuration.ProjectId); // Passive preflight only; no adapter, inference or permission grant.
            if (Directory.Exists(Path.Combine(project.Root, "assets")))
            {
                startup?.Phase(StartupStage.Authoring);
                _assetWorkflow=new(workspace,project.Root,project.Configuration.ProjectId,1);
                _uiWorkspace=new(workspace,project.Root,uiActions?.RegisteredActions);
                _animationGraphs=new(workspace,project.Root,project.Configuration.ProjectId);
                _editor.ConfigureAssets(project.Root, project.Configuration.ProjectId, 1,_assetWorkflow.Scope,_uiWorkspace.Scope,_animationGraphs.Scope); // Exact human approvals only; Agent cannot manufacture grants.
                _uiPackagePreparation=new(_uiWorkspace,project.Root,project.Configuration.ProjectId,actions:uiActions);
                _uiPublication=new(workspace,_uiWorkspace,_uiPackagePreparation,project.Root,project.Configuration.ProjectId,uiActions);
                if(project.Configuration.AssetPackage is null)
                    _editor.ConfigureEnvironments(project.Root,project.Configuration.ProjectId,environmentPresets??(()=>Ncma.Assets.EnvironmentPresetLibrary.LoadDeployment(AppContext.BaseDirectory)));
                if(_renderer is not null)_uiPreview=new(_renderer,plugins,_uiTextResources);
                if(_renderer is not null)_graphPreview=new(_renderer,()=>_poseKernel??=new(Path.Combine(plugins,"NcmaAnimationKernel.dll"),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(plugins,"NcmaAnimationKernel.dll"))))));
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, DeploymentManifest.FileName)))
                    _editor.ConfigureImportTools(ImportToolDeployment.FromValidatedEditorPackage(AppContext.BaseDirectory), _assetWorkflow.IsSourceApproved);
            }
            startup?.Phase(StartupStage.RenderAssets);
            _editor.PrepareRenderAssets(project.Root, project.Configuration.ProjectId,project.Configuration.AssetPackage);
            _editProjection=CreateOrReuseEditProjection();
            var startupSnapshot=_editProjection.Scene.CaptureSnapshot();
            PreflightPost(_editProjection.Scene.World,startupSnapshot);
            if(_renderer is not null){
                startup?.Phase(StartupStage.Environment);
                _environmentStartup=new(_renderer,_sceneCache!);
                _environmentStartup.Prepare(_editProjection.Scene.World,_editor.RenderAssets!,startupSnapshot,()=>!_closed&&_editProjection.IsCurrent(_editor));
            }
            if (_sceneHudPackage is not null) {
                startup?.Phase(StartupStage.SceneHud);
                _sceneHud = new(_uiTextResources); _sceneHud.Prepare(_sceneHudPackage,_renderer!,plugins,new(1280,720,1,default),interactive:uiActions is not null);
            }
            startup?.Phase(StartupStage.Gameplay);
            _editor.LoadGameplay(project.GameplayAssemblyPath);
            foreach (var diagnostic in _editor.RenderAssets!.Diagnostics.Take(64))
                _log.Write("warning", "render.asset." + diagnostic.Code, diagnostic.AssetId.ToString("D"), _correlation);
        }
        startup?.Phase(StartupStage.Presenter);
        if (!preview) {
            _fbxPreview = new(Path.Combine(plugins, "NcmaNative.dll")); // lazy immutable kernel loading on trusted local import only
            _animationPreview = new(Path.Combine(plugins,"NcmaNative.dll"));
            _filePicker=new();
            _presenter = new(new EditorWorkspace(_editor), project?.GameplayAssemblyPath, project?.Root, _fbxPreview, _animationPreview,_preferences,_filePicker.Choose,_log,_assetWorkflow,workspaceStyle:true,projectName:project?.Configuration.Name);
            _presenter.AttachCharacters(characters);
            _presenter.AttachAnimationRuntime(graphRuntime);
            _presenter.AttachRuntimeInspection(runtimeInspection);
            if(_animationGraphs is not null)_presenter.AttachGraphAuthoring(_animationGraphs,_graphPreview);
            else if(project is not null)_presenter.AttachGraphReads(new AnimationGraphInspections(new EditorWorkspace(_editor),project.Root));
            if(!smoke)_presenter.AttachLayout(new(Path.Combine(_userDataRoot,"editor/workspace.json")));
            if(_uiWorkspace is not null)_presenter.AttachUi(_uiWorkspace,_uiPreview);
#if NCMA_AI
            // Always offer editor-session model configuration, even without a project.
            // Only project-dependent authoring receives the optional domain services.
            _aiAuthoring = CreateAiAuthoring(_editor.DomainProposals); // unconfigured, no transport or probe
            _presenter.AttachAiAuthoring(_aiAuthoring);
            _presenter.AttachAiSecretInput(frame => _gui.TakeSecret(frame,81,30), _gui.ClearSecret);
#endif
            if(_uiPackagePreparation is not null&&_uiPublication is not null)_presenter.AttachUiPublication(_uiPackagePreparation,_uiPublication);
            _presenter.SelectStartupCamera(project?.Configuration.SceneCamera);
        }
        startup?.Phase(StartupStage.SceneSynchronization);
        if (_renderer is not null) {
            _configuration=RenderConfiguration.Default(_editor.Edit!.SessionId);
            _renderService!.Configure(ReferencePreview ? _configuration : ClearConfiguration(_configuration),960,680);
            if(ReferencePreview) RenderingEditorAdapter.RegisterInspections(_editor.Edit,_renderService,_renderer);
            else SceneRenderInspections.Register(_editor.Edit,CurrentGraphicsRenderer,CurrentSceneForPresentation);
            SynchronizeSceneResources();
            _sceneSynchronizationOk=true;_environmentStartup?.Dispose();_environmentStartup=null;
        }
        startup?.Phase(StartupStage.Diagnostics);
        string[] labels = ["NcmaEngine candidate", "C# frame loop / native GPU execution", cpuOnly ? "CPU GUI 验证模式" : ReferencePreview ? "C# 渲染图 / DX11 PBR 参考预览" : "C# 渲染图 / DX11 静态场景", "中文输入诊断（非场景编辑）"];
        _labels = Encoding.UTF8.GetBytes(string.Concat(labels)); _labelsLength=(uint)_labels.Length; _text=new byte[_labels.Length+4096];
        _labels.CopyTo(_text,0);
        uint offset = 0;
        for (int i = 0; i < 4; i++) {
            _items[i] = new() { Kind = (uint)(i == 0 ? GuiItemKind.PanelBegin : i == 3 ? GuiItemKind.Text : GuiItemKind.Label), Enabled = 1,
                WidgetHigh = 1, WidgetLow = (ulong)(i + 1), LabelOffset = offset, LabelLength = (uint)Encoding.UTF8.GetByteCount(labels[i]) };
            offset += _items[i].LabelLength;
        }
        _items[4].Kind = (uint)GuiItemKind.PanelEnd;
        startupCompleted=true;
        }
        finally { startup?.End(startupCompleted); }
    }
    public bool PumpPlatform()
    {
        RequireGraphicsReady();
        _verificationWindowEvents=null;
        _state = _window!.Poll(); _input.Update(_state, _window.Events);
        if (_state.Minimized != 0 || _state.FramebufferWidth == 0 || _state.FramebufferHeight == 0) _window.Wait(0.02);
        if (_state.CloseRequested != 0) return false;
        return true;
    }
    public void ApplyEditorIntents(FrameContext frame) {
        RequireGraphicsReady();
        if (_presenter is not null) {
            bool escape = false;
            foreach (var input in WindowEvents) if (input.Kind == 1 && input.Key == 256 && input.Action == 1) escape = true;
            if (_input.CancelInteraction || escape) _presenter.CancelInteraction();
            else _presenter.Apply(_gui!.Events, _gui.OutputText);
            return;
        }
        // Presentation diagnostics only; no scene/configuration mutation or authority in native GUI.
        foreach(var input in _gui!.Events) if(input.WidgetHigh==1 && input.WidgetLow==4 && input.Phase==3 &&
            input.DocumentGeneration==_editor!.Edit!.DocumentGeneration && input.Revision==_editor.Document.Revision)
            _presentationText=Encoding.UTF8.GetString(_gui.OutputText.Slice((int)input.TextOffset,(int)input.TextLength));
    }
    public void PumpAgent(FrameContext frame) {
        RequireGraphicsReady();
        _editor!.RefreshAssets();_assetWorkflow?.Pump(); _editor.Endpoint?.Pump();
        _editor.DomainProposals?.PumpModules();
        SynchronizeEnvironmentCatalog();
        if(_presenter?.ConsumeScrub() is { } scrub&&CurrentEditProjection is not null)_editScene?.Animation?.SetPreviewTime(scrub);
        if (_presenter?.ConsumeRenderAssetRefresh()==true) {
            try { _editor.RefreshRenderAssets(); }
            catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException) { _log!.Write("error","render.asset_refresh",e.Message,_correlation); }
        }
        // Trusted preparation boundary BEFORE simulation/render; snapshots only on explicit resource/session changes.
        SynchronizeSceneResourcesChecked();
        _presenter?.SynchronizeUi();
        _presenter?.SynchronizeGraph();
        SynchronizeSceneHud();
    }
    private static RenderConfiguration ClearConfiguration(RenderConfiguration c) => c with {PipelineType="ncma.clear.v1",ToneExposureOverride=0,FeatureExposure=0,ReplaceToneStage=false};
    private void SynchronizeSceneResources() {
        if(_renderer is null || _sceneCache is null || _editor is null)return;
        if(_pendingScene is not null)throw new InvalidOperationException("A failed scene candidate is retained; explicit close is required before another exchange.");
        var projection=CreateOrReuseEditProjection();
        if(_editor.RenderAssets is { } edit && (!ReferenceEquals(_editProjection,projection) ||
            _editAssets!=edit.Assets.Identity || _editScene?.WorldId!=projection.Scene.World.Identity ||
            _editScene.PreparedRevision!=projection.Scene.World.Revision)) {
            _pendingEditProjection=projection;
            using var sourceRead=_editor.Document.ReadOnly();
            var snapshot=projection.Scene.CaptureSnapshot();var world=projection.Scene.World;
            edit=PrepareCurrentEnvironmentAssets(edit,snapshot);
            bool Current()=>!_closed&&projection.IsCurrent(_editor);
            _pendingScene=new Ncma.Rendering.Scene.SceneRenderSession(_renderer,_sceneCache,world);_pendingScene.Prepare(edit,snapshot,
                pipelineFactory:Ncma.Rendering.Scene.ScenePipelineStartup.Bind(_renderer,world,pipelineFactory,Current),poseKernel:PoseFor(snapshot),environmentPreparationAllowed:Current,
                postProcessing:PreparePostRuntime(world,snapshot),highShadows:PrepareShadowRuntime(world,snapshot),
                retainedAnimation:_graphicsRecoveryBusy?_retainedEditAnimation:null);
            projection.RequireCurrent(_editor);
            _editScene?.Dispose();
            if(!_graphicsRecoveryBusy){_retainedEditAnimation?.Dispose();_retainedEditAnimation=null;}
            projection.RequireCurrent(_editor);
            _editScene=_pendingScene;_pendingScene=null;_editProjection=projection;_pendingEditProjection=null;_editAssets=edit.Assets.Identity;
        } else if(_editor.RenderAssets is null) {
            _editScene?.Dispose();_editScene=null;_editProjection=projection;_editAssets=Guid.Empty;
        }
        if(_editor.Play is { } play && _editor.PlayRenderAssets is { } pins) {
            if(_playScene is not null && _playSession!=play.SessionId && _playAssets==pins.Assets.Identity && _playScene.WorldId==play.Scene.World.Identity) {
                _playScene.Animation?.ResynchronizeAfterReload(play);_playSession=play.SessionId;
            }
            if(_playSession!=play.SessionId || _playAssets!=pins.Assets.Identity || _playScene?.WorldId!=play.Scene.World.Identity) {
                var snapshot = play.Scene.CaptureSnapshot();
                _pendingScene=new Ncma.Rendering.Scene.SceneRenderSession(_renderer,_sceneCache,play.Scene.World);_pendingScene.Prepare(pins,snapshot,
                    pipelineFactory:Ncma.Rendering.Scene.ScenePipelineStartup.Bind(_renderer,play.Scene.World,pipelineFactory,()=>!_closed),poseKernel:PoseFor(snapshot),play:play,
                    interpolateTransforms:Ncma.Characters.CharacterComponents.HasPhysics(snapshot),rootMotion:_characterRuntime,animators:_sceneRuntime?.Animators,environmentPreparationAllowed:()=>!_closed,
                    postProcessing:PreparePostRuntime(play.Scene.World,snapshot),highShadows:PrepareShadowRuntime(play.Scene.World,snapshot),
                    retainedAnimation:_graphicsRecoveryBusy?_retainedPlayAnimation:null,deferPlayObserver:_graphicsRecoveryBusy);
                _playScene?.Dispose();
                if(!_graphicsRecoveryBusy){_retainedPlayAnimation?.Dispose();_retainedPlayAnimation=null;}
                _playScene=_pendingScene;_pendingScene=null; _playSession=play.SessionId; _playAssets=pins.Assets.Identity;
            }
        } else { _playScene?.Dispose(); _playScene=null;_retainedPlayAnimation?.Dispose();_retainedPlayAnimation=null; _playAssets=_playSession=Guid.Empty; }
    }
    private Ncma.Animation.Native.PoseKernel? PoseFor(Ncma.Scene.ISceneContent snapshot) {
        if(!snapshot.Objects.Any(o=>o.Components.Any(c=>c.TypeId==Ncma.Scene.Rendering.SkinnedMeshData.TypeId)))return null;
        if(_poseKernel is null) {
            string path=Path.Combine(plugins,"NcmaAnimationKernel.dll");
            _poseKernel=new(path,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true,layerSupport:true);
        }
        return _poseKernel;
    }
    public void BeginPresentation(FrameContext frame) {
        RequireGraphicsReady();
        stages?.Begin(frame.FrameId,PresentationCall.BeginPresentation);
        bool completed=false;
        try{
            stages?.Phase(PresentationStage.GuiBegin);
            _capture = _gui!.Begin(_state, frame.DeltaSeconds);
            stages?.Phase(PresentationStage.Control);
            _graphicsFrameOpen=true;
            if (_capture.CancelInteraction != 0) _presenter?.CancelInteraction();
            completed=true;
        }catch(Exception error){_graphicsFrameOpen=false;_windowSurface?.RecordFailure(error);RememberGraphicsNativeFailure(error);throw;}
        finally{stages?.End(completed);}
    }
    public void SubmitInput(FrameContext frame) {
        RequireGraphicsReady();
        bool suspended=!RendererWindowSurface.CanPresent(_state);
        if(suspended)_presenter?.CancelInteraction();
        var hudCapture=ProcessSceneHudInput(_state,WindowEvents);
        if(_presenter is not null && !suspended && _input.Focused && !_input.CancelInteraction && !hudCapture.CaptureKeyboard) {
            bool Held(int key) => (_state.Held[key/64]&(1UL<<(key%64)))!=0;
            foreach(var input in WindowEvents){
                if(input.Kind==5)_presenter.UiKeyboard(0,char.ConvertFromUtf32((int)input.Codepoint),false,_capture.Keyboard!=0);
                if(input.Kind==1 && input.Action==1&&!_presenter.UiKeyboard(input.Key,"",Held(340)||Held(344),_capture.Keyboard!=0))_presenter.Shortcut(input.Key,Held(341)||Held(345),Held(340)||Held(344),_capture.Keyboard!=0,_input.Focused);
            }
        }
        bool focused = !suspended && _input.Focused && _capture.Mouse == 0 && !hudCapture.CapturePointer;
        double x = focused && _pointerWasFocused ? _input.PointerX - _previousPointerX : 0;
        double y = focused && _pointerWasFocused ? _input.PointerY - _previousPointerY : 0;
        _previousPointerX = _input.PointerX; _previousPointerY = _input.PointerY; _pointerWasFocused = focused;
        if (_editor!.Play is not { State: Ncma.Gameplay.PlayState.Running or Ncma.Gameplay.PlayState.Paused } play) return;
        _input.CopyGameplayBits(suspended || _capture.Keyboard != 0 || hudCapture.CaptureKeyboard, suspended || _capture.Mouse != 0 || hudCapture.CapturePointer, _held, _pressed, _released);
        play.SubmitInput(new(play.SessionId,_input.Sequence,_input.Focused&&!suspended,_held,_pressed,_released,x,y));
    }
    public void AdvancePlay(FrameContext frame) {
        RequireGraphicsReady();
        if (_editor!.Play is { State: Ncma.Gameplay.PlayState.Running } play) play.AdvanceFrame(frame.DeltaSeconds);
        if (simulationCosts) _lastSimulationCosts = _editor.Play is null ? null : _sceneRuntime?.ReadSimulationCosts();
        if(_sceneSynchronizationOk&&CurrentEditProjection is not null&&_editor.Play is null&&_presenter?.EditPreviewPaused!=true)_editScene?.Animation?.AdvancePreview(Math.Clamp(frame.DeltaSeconds,0,.25));
        _presenter?.AdvanceGraphPreview(Math.Clamp(frame.DeltaSeconds,0,.25));
        _fbxPreview?.Tick(Math.Min(frame.DeltaSeconds, 1));
        _animationPreview?.Tick(Math.Min(frame.DeltaSeconds,1));
    }
    public void Present(FrameContext frame)
    {
        RequireGraphicsReady();
        stages?.Begin(frame.FrameId,PresentationCall.Present,_verificationCapture is not null||_verificationComposite is not null);
        bool completed=false;
        PresentationCostCapture? costs=null;
        try{
            stages?.Phase(PresentationStage.Observer);
            if(presentationCosts&&_renderer is not null&&RendererWindowSurface.CanPresent(_state)){
                if(!ReferenceEquals(_costRenderer,_renderer)){_costCapture=new(_renderer);_costRenderer=_renderer;}
                costs=_costCapture;costs!.Begin(frame.FrameId,_verificationCapture is not null||_verificationComposite is not null);
            }
            PresentCore(frame);stages?.Phase(PresentationStage.Observer);costs?.End();completed=true;
        }
        catch(Exception error){costs?.Abort();_windowSurface?.RecordFailure(error);RememberGraphicsNativeFailure(error);throw;}
        finally{_graphicsFrameOpen=false;stages?.End(completed);}
    }
    private void PresentCore(FrameContext frame)
    {
        stages?.Phase(PresentationStage.Surface);
        bool drawable=_windowSurface?.Prepare(_state)??RendererWindowSurface.CanPresent(_state);
        stages?.Phase(PresentationStage.GuiBuild);
        _items[0].Rect[0] = 0; _items[0].Rect[1] = 0; _items[0].Rect[2] = Math.Min(Math.Max(_state.Width, 1), 320); _items[0].Rect[3] = Math.Max(_state.Height, 1);
        int inputBytes=Encoding.UTF8.GetBytes(_presentationText.AsSpan(),_text.AsSpan(_labels.Length));
        _items[3].TextOffset=_labelsLength; _items[3].TextLength=(uint)inputBytes;
        var description = new GuiFrame { StructSize = (uint)Marshal.SizeOf<GuiFrame>(), Frame = frame.FrameId, ViewGeneration = 1,
            DocumentGeneration = _editor!.Edit!.DocumentGeneration, Revision = _editor.Document.Revision,
            ItemCount = (uint)_items.Length, TextBytes = _labelsLength+(uint)inputBytes };
        GuiStats stats;
        if (_presenter is not null) {
            description = _presenter.Build(frame.FrameId,_state.Width,_state.Height);
            stages?.Phase(PresentationStage.ViewPreparation);
            bool uiActive=_presenter.UiWorkspaceActive;
            bool graphActive=_presenter.GraphWorkspaceActive;
            if(graphActive&&_renderer is not null&&drawable)description=_presenter.AttachGraphPreview(frame.FrameId);
            if(uiActive&&_renderer is not null&&drawable){
                if(_uiPreview!.Prepared&&!_uiPreview.RecoveryRequired&&_uiPreview.Acquire(frame.FrameId) is {} token)description=_presenter.AttachUiImage(token);
            }
            if(!uiActive&&!graphActive&&!ReferencePreview&&_renderer is not null) {
                if(drawable) {
                    var area=_presenter.Viewport;
                    uint targetWidth=Math.Clamp((uint)Math.Ceiling(area.Width*_state.FramebufferWidth/Math.Max(_state.Width,1)),1,4096);
                    uint targetHeight=Math.Clamp((uint)Math.Ceiling(area.Height*_state.FramebufferHeight/Math.Max(_state.Height,1)),1,4096);
                    if(_viewportTarget is null||_viewportTarget.Width!=targetWidth||_viewportTarget.Height!=targetHeight) {
                        if(_pendingViewportTarget is not null)throw new InvalidOperationException("Retained viewport candidate must close before another replacement.");
                        _pendingViewportTarget=_renderer.CreateViewTarget(targetWidth,targetHeight);
                        _viewportTarget?.Dispose(); // Both owners remain reachable on failure.
                        _viewportTarget=_pendingViewportTarget;_pendingViewportTarget=null;
                    }
                    GuiCachedImageToken? hud = null;
                    if (ActiveSceneHud is { } activeHud) {
                        activeHud.PrepareSurface(new(targetWidth,targetHeight,(float)_state.FramebufferWidth/Math.Max(_state.Width,1),default));
                        hud = activeHud.AcquireSurface(frame.FrameId);
                    }
                    description=_presenter.AttachViewport(_viewportTarget.ImageToken,hud);
                } // Suspension keeps the last valid target; no acquire/submit while hidden.
            }
            stages?.Phase(PresentationStage.GuiDraw);
            stats = _gui!.Draw(description,_presenter.Items,_presenter.Text);
            if (stats.EventOverflow != 0) _presenter.CancelInteraction();
        } else {
            stages?.Phase(PresentationStage.GuiDraw);
            stats = _gui!.Draw(description, _items, _text.AsSpan(0,_labels.Length+inputBytes));
        }
        stages?.Phase(PresentationStage.SceneSubmission);
        if (_renderer is not null && drawable) {
            var logical = _presenter?.Viewport ?? (320f,40f,Math.Max(1,_state.Width-320f),Math.Max(1,_state.Height-40f));
            uint x=Math.Min(_state.FramebufferWidth-1,(uint)Math.Ceiling(logical.Item1*_state.FramebufferWidth/Math.Max(_state.Width,1)));
            uint y=Math.Min(_state.FramebufferHeight-1,(uint)Math.Ceiling(logical.Item2*_state.FramebufferHeight/Math.Max(_state.Height,1)));
            if(x>=_state.FramebufferWidth || y>=_state.FramebufferHeight)return;
            uint width=Math.Clamp((uint)Math.Ceiling(logical.Item3*_state.FramebufferWidth/Math.Max(_state.Width,1)),1,_state.FramebufferWidth-x);
            uint height=Math.Clamp((uint)Math.Ceiling(logical.Item4*_state.FramebufferHeight/Math.Max(_state.Height,1)),1,_state.FramebufferHeight-y);
            if(_viewportTarget is not null){x=0;y=0;width=_viewportTarget.Width;height=_viewportTarget.Height;}
            var configurationWorld=_editor.Play?.Scene.World??CurrentEditProjection?.Scene.World;
            if (configurationWorld is not null && (_configurationRevision != configurationWorld.Revision || _configurationWorld != configurationWorld.Identity)) {
                if (!ReferenceEquals(_configurationQueryWorld, configurationWorld)) {
                    _configurationReader = new(configurationWorld); _configurationQueryWorld = configurationWorld;
                }
                int configurations = _configurationReader!.ReadSingle(configurationWorld.Identity, out var selectedConfig);
                if(configurations>1) _log!.Write("warning","render.ambiguous_configuration","Multiple render configurations; keeping the last valid configuration.",_correlation);
                else _configuration=configurations==1?selectedConfig:RenderConfiguration.Default(_editor.Edit.SessionId);
                _configurationRevision=configurationWorld.Revision; _configurationWorld=configurationWorld.Identity;
            }
            if(_presenter?.UiWorkspaceActive==true||_presenter?.GraphWorkspaceActive==true){
                // A 3D graph-preview target submission already began/cleared the same renderer frame.
                // GUI composes its target into that frame; never submit a second frame or wait for it.
                if(_presenter?.GraphPreviewSubmitted!=true){_renderService!.Configure(ClearConfiguration(_configuration),_state.FramebufferWidth,_state.FramebufferHeight);_renderService.Submit(frame.FrameId,0,0);}
            } else if (!ReferencePreview) {
                // Runtime World guards do not protect the separate authoring document.
                // Trusted graph/environment callbacks must not mutate either authority.
                using var sourceRead=_editor.Document.ReadOnly();
                var scene=CurrentSceneForPresentation(); // Includes edits after PumpAgent (e.g. keyboard shortcuts).
                Guid camera=_presenter?.SceneCamera??project?.Configuration.SceneCamera??Guid.Empty;
                Ncma.Scene.Rendering.SceneCameraView? browser=null;
                if(_editor.Play is { } followPlay)browser=Ncma.Characters.CharacterPlayRuntime.FollowView(followPlay,camera,width,height);
                if(camera==Guid.Empty) {
                    browser=_presenter?.BrowserCamera(width,height)??new EditorOrbitCamera().View(width,height);
                }
                if(scene is not null&&!PrepareSceneEnvironmentView(scene,width,height,camera,browser,_configuration))scene=null;
                if(scene?.Submit(frame.FrameId,width,height,camera,browser,_configuration.Exposure,_configuration.Ambient,target:_viewportTarget,origin:new(x,y))!=true) {
                    if(_viewportTarget is not null)_renderer.SubmitResources(frame.FrameId,[],new(0,0,width,height),new(.015f,.02f,.03f,1),new(System.Numerics.Vector3.Zero,System.Numerics.Vector3.UnitY,System.Numerics.Vector4.One),_viewportTarget);
                    else {_renderService!.Configure(ClearConfiguration(_configuration),width,height);_renderService.Submit(frame.FrameId,x,y);}
                }
                _presenter?.ShowRenderDiagnostics(scene?.Diagnostics);
                var environmentWorld = _editor.Play?.Scene.World ?? _editProjection?.Scene.World;
                var authoringSource = _editor.Play is null ? _editProjection?.Source : null;
                // A rejected request is current failure evidence, not an installed old World.
                if(_editor.Play is null&&!_sceneSynchronizationOk&&_failedSceneSync==SceneSyncKey()) {
                    authoringSource=EditorSceneSourceStamp.Read(_editor);environmentWorld=null;
                }
                _presenter?.ShowEnvironmentPreparation(environmentWorld?.Identity??Guid.Empty, environmentWorld?.Revision??0, _editor.Play?.SessionId ?? Guid.Empty,
                    scene is not null, _scenePreparationError is not null, scene?.PreparedEnvironment,authoringSource);
                _presenter?.ShowPostPreparation(environmentWorld?.Identity??Guid.Empty,environmentWorld?.Revision??0,_editor.Play?.SessionId??Guid.Empty,
                    scene?.IsPostProcessReady??false,_scenePreparationError is not null,scene?.PreparedPostProcess,frame.FrameId,authoringSource);
                _presenter?.SetViewportFrame(_editor.Play is null?scene:null,frame.FrameId,authoringSource);
            } else {
            if (_graph is null || width != _viewWidth || height != _viewHeight) {
                _renderService!.Configure(_configuration,width,height);
                _graph = _renderService.Plan;
                _viewWidth=width; _viewHeight=height;
            }
            _renderService!.Configure(_configuration,width,height);
            Span<float> model = stackalloc float[16]; bool hasModel = false;
            if (_presenter?.Selected is { } selected) {
                Ncma.Runtime.TransformData? transform = null;
                if (_editor.Play is { } play) transform = play.RenderView.Objects.FirstOrDefault(o => o.ObjectId == selected.Id)?.Transform;
                else if (selected.Components.FirstOrDefault(c => c.TypeId == "ncma.transform") is { } component)
                    transform = JsonSerializer.Deserialize<Ncma.Runtime.TransformData>(component.Data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true });
                if (transform is not null) {
                    new Ncma.Rendering.RenderFrameView(configurationWorld?.Identity??Guid.Empty,configurationWorld?.Revision??0,selected.Id,transform).CopyColumnMajorModel(model); hasModel = true;
                }
            }
            _renderService.Submit(frame.FrameId,x,y,hasModel ? model : default);
            }
            stages?.Phase(PresentationStage.DiagnosticCapture);CaptureVerificationViewport();
            stages?.Phase(PresentationStage.GuiGpu);_gui.RenderGpu();
            stages?.Phase(PresentationStage.DiagnosticCapture);CaptureVerificationComposite();
            stages?.Phase(PresentationStage.NativePresent);_renderer.Present();loopCosts?.PresentReturned();
            stages?.Phase(PresentationStage.Cleanup);
            ActiveSceneHud?.ReleaseSurface();
            _uiPreview?.ReleasePresentation();
            var gpuStats=_renderer.Stats;
            if(gpuStats.ValidationErrors!=0 || gpuStats.ValidationWarnings!=0) throw new InvalidOperationException("DX11 validation failed.");
        }
        stages?.Phase(PresentationStage.Diagnostics);
        if (frame.FrameId == 1) _log!.Write("info", "presentation.first_frame", $"CPU vertices={stats.Vertices} indices={stats.Indices}", _correlation);
        if (frame.FrameId % 60 == 0) {
            if(_nativeDiagnostics is null && (_animationPreview?.Current is not null || _fbxPreview?.Capture().AssetId is not null))
                _nativeDiagnostics=new(Path.Combine(plugins,"NcmaNative.dll"));
            _nativeDiagnostics?.Pump(_log!,_correlation);
            string diagnostic = _loader.Modules.Single(m => m.Kind == ModuleKind.Platform).ReadDiagnostics();
            if (diagnostic != _lastPlatformDiagnostic) {
                foreach (var message in diagnostic.Split('\n', StringSplitOptions.RemoveEmptyEntries)) _log!.Write("warning", "native.platform", message, _correlation);
                _lastPlatformDiagnostic = diagnostic;
            }
        }
    }
    // Trusted owner close-only operation, not an Editor command or Agent capability.
    // Only after a failed close; never Present an incomplete image or resume this renderer.
    internal void AbandonFrameForShutdown()
    {
        VerifyGraphicsOwner();
        if(_windowSurface?.Status.Phase!=RenderWindowPhase.Closing||_renderer is null)
            throw new InvalidOperationException("Editor must retain a failed renderer close before frame abandonment.");
        var frame=_renderer.ReadFrameState();
        if(!frame.Active||frame.Failed)throw new InvalidOperationException("No healthy active Editor frame to abandon; retry ordinary close for a stopped renderer.");
        _renderer.AbandonFrameForShutdown(frame.Frame);
    }
    public void Dispose()
    {
        VerifyGraphicsOwner();
        if (_closed) return;
        // Terminal close intent precedes AI drain. Retain resources, but never resume frames
        // or graphics recovery while an uncooperative request is still owned by this host.
        _graphicsShutdown=true;_graphicsReview=null;
#if NCMA_AI
        // Preserve every downstream owner until ignored-cancellation work actually drains.
        _gui?.ClearSecret();
        if (_aiAuthoring?.TryClose() == false) throw new InvalidOperationException("ai_shutdown_draining");
        _aiAuthoring = null;
#endif
        _windowSurface?.BeginShutdown();
        // Stop at the first failed close. Keep that owner and every downstream dependency
        // for an explicit retry; never unload plugins under a live GPU/solver resource.
        _presenter?.CancelInteraction();
        _nativeDiagnostics?.Dispose(); _nativeDiagnostics = null;
        _animationPreview?.Dispose(); _animationPreview = null;
        _fbxPreview?.Dispose(); _fbxPreview = null;
        _pendingScene?.Dispose(); _pendingScene = null;_pendingEditProjection=null;
        _playScene?.Dispose(); _playScene = null;
        _editScene?.Dispose(); _editScene = null;
        _retainedPlayAnimation?.Dispose();_retainedPlayAnimation=null;
        _retainedEditAnimation?.Dispose();_retainedEditAnimation=null;
        _editProjection=null;_configurationQueryWorld=null;_configurationReader=null;_environmentQueryWorld=null;_environmentReader=null;
        _environmentStartup?.Dispose();_environmentStartup=null;
        _playEnvironmentStartup?.Dispose();_playEnvironmentStartup=null;
        _pipelineStartup?.Dispose();_pipelineStartup=null;
        _postRuntime?.Dispose();_postRuntime=null;_postLibrary?.Dispose();_postLibrary=null;
        _shadowLibrary?.Dispose();_shadowLibrary=null;_highShadows=null;
        _sceneCache?.Dispose(); _sceneCache = null;
        _graphPreview?.Dispose();_graphPreview=null;
        _poseKernel?.Dispose(); _poseKernel = null;
        _assetWorkflow?.Dispose(); _assetWorkflow = null;
        _uiPublication?.Dispose();_uiPublication=null;
        _uiPackagePreparation?.Dispose();_uiPackagePreparation=null;
        _uiWorkspace?.Dispose();_uiWorkspace=null;
        _animationGraphs?.Dispose();_animationGraphs=null;
        _editor?.Dispose(); _editor = null;
        _gui?.Dispose(); _gui = null;
        ClosePlayHud();
        _sceneHud?.Dispose(); _sceneHud = null;
        _sceneHudPackage?.Dispose(); _sceneHudPackage = null;
        while(_hudRetained.Count!=0){_hudRetained[0].Dispose();_hudRetained.RemoveAt(0);}
        _uiPreview?.Dispose();_uiPreview=null;
        _uiTextResources.Dispose();
        _pendingViewportTarget?.Dispose(); _pendingViewportTarget = null;
        _viewportTarget?.Dispose(); _viewportTarget = null;
        _renderService?.Dispose(); _renderService = null;
        ObserveResourceMemory(RendererMemoryCheckpoint.BeforeRendererClose);
        _renderer?.Dispose(); _windowSurface?.CompleteShutdown(); _renderer = null;
        _shaderFiles?.Dispose(); _shaderFiles = null;
        _precisionWait?.Dispose();_precisionWait=null;Pacer=null;
        _window?.Dispose(); _window = null;
        _physics?.Dispose(); _physics = null;
        _loader.Dispose();
        _log?.Dispose(); _log = null;
        _closed = true;
    }
}
