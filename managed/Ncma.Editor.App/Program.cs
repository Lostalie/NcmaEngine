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
namespace Ncma.Editor.App;
internal static class Program
{
    // Synchronous Main is the sole owner; no hostfxr, unmanaged Run or async continuation.
    [STAThread]
    public static int Main(string[] args)
    {
        try {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, DeploymentManifest.FileName))) DeploymentManifest.Validate(AppContext.BaseDirectory);
            if (args.SequenceEqual(new[] { "--validate-package" })) return 0;
            if (args.Length == 0) args = ["--editor"];
            string? projectPath = null, plugins = null; bool smoke = false, cpuOnly = false, preview = false, editor = false; ulong frames = ulong.MaxValue;
            var flags = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++) {
                if (!flags.Add(args[i])) { Console.Error.WriteLine("duplicate_arguments"); return 2; }
                switch (args[i]) {
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
            ProjectContext? project = projectPath is null ? null : ProjectContext.Load(projectPath);
            if (project is not null && project.Configuration.Renderer != "Direct3D11") {
                Console.Error.WriteLine("renderer_unimplemented: " + project.Configuration.Renderer); return 3;
            }
            if (project?.Configuration.Plugins.Length > 0) {
                Console.Error.WriteLine("project_plugin_overrides_unimplemented"); return 3;
            }
            // Release candidate explicitly chooses its sibling plugin directory; never cwd/PATH.
            string pluginRoot = Path.GetFullPath(plugins ?? Path.Combine(AppContext.BaseDirectory, "plugins"));
            using var lifetime = new ApplicationLifetime();
            var presentation = new CandidatePresentation(pluginRoot, project, smoke, cpuOnly, preview);
            lifetime.Start([presentation]);
            ulong rendered = lifetime.Run(new MonotonicClock(), presentation, frames);
            lifetime.Dispose();
            Console.WriteLine($"candidate_{(cpuOnly ? "cpu_gui" : (preview||smoke)&&project is null ? "dx11_reference" : "dx11_scene")} frames={rendered}; resources released");
            return 0;
        } catch (ArgumentException e) { Console.Error.WriteLine("invalid_configuration: " + e.Message); return 2; }
        catch (Exception e) { Console.Error.WriteLine("candidate_failed: " + e.Message); return 4; }
    }
}
internal sealed unsafe class CandidatePresentation(string plugins, ProjectContext? project, bool smoke, bool cpuOnly, bool preview) : IApplicationService, IFramePipeline
{
    private bool ReferencePreview => (preview||smoke) && project is null;
    private readonly PluginLoader _loader = new();
    private PhysicsService? _physics;
    private PlatformWindow? _window;
    private GuiSession? _gui;
    private RendererSession? _renderer;
    private GpuViewTarget? _viewportTarget;
    private RenderPipelineService? _renderService;
    private Ncma.Rendering.RenderResourceCache? _sceneCache;
    private Ncma.Rendering.Scene.SceneRenderSession? _editScene, _playScene;
    private Ncma.Animation.Native.PoseKernel? _poseKernel;
    private Guid _editAssets, _playAssets, _playSession;
    private CompiledRenderGraph? _graph;
    private uint _viewWidth, _viewHeight;
    private ulong _configurationRevision = ulong.MaxValue;
    private Guid _configurationWorld;
    private RenderConfiguration _configuration;
    private EditorSessionOwner? _editor;
    private EditorAssetWorkflow? _assetWorkflow;
    private EditorPresenter? _presenter;
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
    private readonly Guid _correlation = Guid.NewGuid();
    private string _lastPlatformDiagnostic = "";
    public void Start()
    {
        _log = new(Path.Combine(project?.Root ?? AppContext.BaseDirectory, "out/user/logs/editor-candidate.jsonl"));
        _loader.Load(plugins, [
            new("ncma.platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
            new("ncma.renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["ncma.platform"]),
            new("ncma.gui", ModuleKind.Gui, "NcmaGui.dll", "NcmaGui.dll", 1, 3, ["ncma.platform", "ncma.renderer"]) ]);
        // Explicit optional shared solver module; only bound Play creates numerical resources.
        _physics = new(plugins, project?.Configuration.PhysicsEnabled == true,characterSupport:project?.Configuration.PhysicsEnabled == true);
        var physics = _physics.Inspect();
        if (physics.Enabled)
            _log.Write("info", "physics.module_ready", $"Physics service ABI {physics.AbiMajor}.{physics.AbiMinor}; capabilities={physics.Capabilities}; explicit Play bindings only.", _correlation);
        _window = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "NcmaEngine", 1280, 720, !smoke);
        _window.SetIcon(Path.Combine(AppContext.BaseDirectory,"NcmaEngine.ico"));
        string font=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc");
        if(!smoke && !preview)_preferences=new(Path.Combine(project?.Root??AppContext.BaseDirectory,"out/user/editor/preferences.json"),font);
        _gui = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Gui), _window, _preferences?.Current.FontPath??font,fontSize:_preferences?.Current.FontSize??18);
        if (!cpuOnly) {
            _renderer = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), _window, 1280, 720);
            _renderService = new(_renderer); _sceneCache=new(_renderer); _gui.AttachRenderer(_renderer);
        }
        _log.Write("info", cpuOnly ? "presentation.cpu_only" : ReferencePreview ? "presentation.dx11_reference" : "presentation.dx11_scene", "Managed candidate presentation initialized.", _correlation);
        _editor = new(project?.Configuration.Name ?? "Presentation smoke",
            components: Ncma.Characters.CharacterComponents.Register(Ncma.Scene.Rendering.RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry())),
            validateComposition: Ncma.Characters.CharacterComponents.RequireComposition,
            composePlay:play=>Ncma.Characters.CharacterPlayRuntime.Compose(play,_physics),
            beforePlayStop:()=>{_playScene?.Dispose();_playScene=null;_playSession=_playAssets=Guid.Empty;});
        if (project is not null) {
            var workspace = new EditorWorkspace(_editor);
            workspace.Open(workspace.Stamp, project.StartupScenePath, true);
            _editor.Edit!.Resynchronize(); // Startup is the saved baseline, not a user Open command.
            if (Directory.Exists(Path.Combine(project.Root, "assets")))
            {
                _assetWorkflow=new(workspace,project.Root,project.Configuration.ProjectId,1);
                _editor.ConfigureAssets(project.Root, project.Configuration.ProjectId, 1,_assetWorkflow.Scope); // Exact human approvals only; Agent cannot manufacture grants.
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, DeploymentManifest.FileName)))
                    _editor.ConfigureImportTools(ImportToolDeployment.FromValidatedEditorPackage(AppContext.BaseDirectory), _assetWorkflow.IsSourceApproved);
            }
            _editor.LoadGameplay(project.GameplayAssemblyPath);
            _editor.PrepareRenderAssets(project.Root, project.Configuration.ProjectId);
            foreach (var diagnostic in _editor.RenderAssets!.Diagnostics.Take(64))
                _log.Write("warning", "render.asset." + diagnostic.Code, diagnostic.AssetId.ToString("D"), _correlation);
        }
        if (!preview) {
            _fbxPreview = new(Path.Combine(plugins, "NcmaNative.dll")); // lazy immutable kernel loading on trusted local import only
            _animationPreview = new(Path.Combine(plugins,"NcmaNative.dll"));
            _filePicker=new();
            _presenter = new(new EditorWorkspace(_editor), project?.GameplayAssemblyPath, project?.Root, _fbxPreview, _animationPreview,_preferences,_filePicker.Choose,_log,_assetWorkflow);
            _presenter.SelectStartupCamera(project?.Configuration.SceneCamera);
        }
        if (_renderer is not null) {
            _configuration=RenderConfiguration.Default(_editor.Edit!.SessionId);
            _renderService!.Configure(ReferencePreview ? _configuration : ClearConfiguration(_configuration),960,680);
            if(ReferencePreview) RenderingEditorAdapter.RegisterInspections(_editor.Edit,_renderService,_renderer);
            else SceneRenderInspections.Register(_editor.Edit,_renderer,()=>_editor.Play is null?_editScene:_playScene);
            SynchronizeSceneResources();
        }
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
    }
    public bool PumpPlatform()
    {
        _state = _window!.Poll(); _input.Update(_state, _window.Events);
        if (_state.Minimized != 0 || _state.FramebufferWidth == 0 || _state.FramebufferHeight == 0) _window.Wait(0.02);
        if (_state.CloseRequested != 0) return false;
        return true;
    }
    public void ApplyEditorIntents(FrameContext frame) {
        if (_presenter is not null) {
            bool escape = false;
            foreach (var input in _window!.Events) if (input.Kind == 1 && input.Key == 256 && input.Action == 1) escape = true;
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
        _editor!.RefreshAssets();_assetWorkflow?.Pump(); _editor.Endpoint?.Pump();
        if(_presenter?.ConsumeScrub() is { } scrub)_editScene?.Animation?.SetPreviewTime(scrub);
        if (_presenter?.ConsumeRenderAssetRefresh()==true) {
            try { _editor.RefreshRenderAssets(); }
            catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException) { _log!.Write("error","render.asset_refresh",e.Message,_correlation); }
        }
        // Trusted preparation boundary BEFORE simulation/render; snapshots only on explicit resource/session changes.
        SynchronizeSceneResources();
    }
    private static RenderConfiguration ClearConfiguration(RenderConfiguration c) => c with {PipelineType="ncma.clear.v1",ToneExposureOverride=0,FeatureExposure=0,ReplaceToneStage=false};
    private void SynchronizeSceneResources() {
        if(_renderer is null || _sceneCache is null || _editor is null)return;
        if(_editor.RenderAssets is { } edit && (_editAssets!=edit.Assets.Identity || _editScene?.WorldId!=_editor.Document.World.Identity || _editScene.PreparedRevision!=_editor.Document.World.Revision)) {
            var snapshot = _editor.Document.CaptureSnapshot();
            var candidate=new Ncma.Rendering.Scene.SceneRenderSession(_renderer,_sceneCache,_editor.Document.World,edit,snapshot,poseKernel:PoseFor(snapshot));
            try { _editScene?.Dispose(); } catch { candidate.Dispose(); throw; }
            _editScene=candidate; _editAssets=edit.Assets.Identity;
        }
        if(_editor.Play is { } play && _editor.PlayRenderAssets is { } pins) {
            if(_playScene is not null && _playSession!=play.SessionId && _playAssets==pins.Assets.Identity && _playScene.WorldId==play.Document.World.Identity) {
                _playScene.Animation?.ResynchronizeAfterReload(play);_playSession=play.SessionId;
            }
            if(_playSession!=play.SessionId || _playAssets!=pins.Assets.Identity || _playScene?.WorldId!=play.Document.World.Identity) {
                var snapshot = play.Document.CaptureSnapshot();
                var candidate=new Ncma.Rendering.Scene.SceneRenderSession(_renderer,_sceneCache,play.Document.World,pins,snapshot,poseKernel:PoseFor(snapshot),play:play,
                    interpolateTransforms:Ncma.Characters.CharacterComponents.HasPhysics(snapshot));
                try { _playScene?.Dispose(); } catch { candidate.Dispose(); throw; }
                _playScene=candidate; _playSession=play.SessionId; _playAssets=pins.Assets.Identity;
            }
        } else { _playScene?.Dispose(); _playScene=null; _playAssets=_playSession=Guid.Empty; }
    }
    private Ncma.Animation.Native.PoseKernel? PoseFor(Ncma.Scene.SceneDocumentSnapshot snapshot) {
        if(!snapshot.Objects.Any(o=>o.Components.Any(c=>c.TypeId==Ncma.Scene.Rendering.SkinnedMeshData.TypeId)))return null;
        if(_poseKernel is null) {
            string path=Path.Combine(plugins,"NcmaAnimationKernel.dll");
            _poseKernel=new(path,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        }
        return _poseKernel;
    }
    public void BeginPresentation(FrameContext frame) {
        _capture = _gui!.Begin(_state, frame.DeltaSeconds);
        if (_capture.CancelInteraction != 0) _presenter?.CancelInteraction();
    }
    public void SubmitInput(FrameContext frame) {
        if(_presenter is not null && _input.Focused && !_input.CancelInteraction) {
            bool Held(int key) => (_state.Held[key/64]&(1UL<<(key%64)))!=0;
            foreach(var input in _window!.Events)if(input.Kind==1 && input.Action==1)
                _presenter.Shortcut(input.Key,Held(341)||Held(345),Held(340)||Held(344),_capture.Keyboard!=0,_input.Focused);
        }
        bool focused = _input.Focused && _capture.Mouse == 0;
        double x = focused && _pointerWasFocused ? _input.PointerX - _previousPointerX : 0;
        double y = focused && _pointerWasFocused ? _input.PointerY - _previousPointerY : 0;
        _previousPointerX = _input.PointerX; _previousPointerY = _input.PointerY; _pointerWasFocused = focused;
        if (_editor!.Play is not { State: Ncma.Gameplay.PlayState.Running or Ncma.Gameplay.PlayState.Paused } play) return;
        _input.CopyGameplayBits(_capture.Keyboard != 0, _capture.Mouse != 0, _held, _pressed, _released);
        play.SubmitInput(new(play.SessionId,_input.Sequence,_input.Focused,_held,_pressed,_released,x,y));
    }
    public void AdvancePlay(FrameContext frame) {
        if (_editor!.Play is { State: Ncma.Gameplay.PlayState.Running } play) play.AdvanceFrame(frame.DeltaSeconds);
        if(_editor.Play is null&&_presenter?.EditPreviewPaused!=true)_editScene?.Animation?.AdvancePreview(Math.Clamp(frame.DeltaSeconds,0,.25));
        _fbxPreview?.Tick(Math.Min(frame.DeltaSeconds, 1));
        _animationPreview?.Tick(Math.Min(frame.DeltaSeconds,1));
    }
    public void Present(FrameContext frame)
    {
        _items[0].Rect[0] = 0; _items[0].Rect[1] = 0; _items[0].Rect[2] = Math.Min(Math.Max(_state.Width, 1), 320); _items[0].Rect[3] = Math.Max(_state.Height, 1);
        int inputBytes=Encoding.UTF8.GetBytes(_presentationText.AsSpan(),_text.AsSpan(_labels.Length));
        _items[3].TextOffset=_labelsLength; _items[3].TextLength=(uint)inputBytes;
        var description = new GuiFrame { StructSize = (uint)Marshal.SizeOf<GuiFrame>(), Frame = frame.FrameId, ViewGeneration = 1,
            DocumentGeneration = _editor!.Edit!.DocumentGeneration, Revision = _editor.Document.Revision,
            ItemCount = (uint)_items.Length, TextBytes = _labelsLength+(uint)inputBytes };
        GuiStats stats;
        if (_presenter is not null) {
            description = _presenter.Build(frame.FrameId,_state.Width,_state.Height);
            if(!ReferencePreview&&_renderer is not null) {
                if(_state.Minimized==0&&_state.FramebufferWidth>0&&_state.FramebufferHeight>0) {
                    var area=_presenter.Viewport;
                    uint targetWidth=Math.Clamp((uint)Math.Ceiling(area.Width*_state.FramebufferWidth/Math.Max(_state.Width,1)),1,4096);
                    uint targetHeight=Math.Clamp((uint)Math.Ceiling(area.Height*_state.FramebufferHeight/Math.Max(_state.Height,1)),1,4096);
                    if(_viewportTarget is null||_viewportTarget.Width!=targetWidth||_viewportTarget.Height!=targetHeight) {
                        var candidate=_renderer.CreateViewTarget(targetWidth,targetHeight);
                        try{_viewportTarget?.Dispose();}catch{candidate.Dispose();throw;}
                        _viewportTarget=candidate;
                    }
                    description=_presenter.AttachViewport(_viewportTarget.ImageToken);
                } else {_viewportTarget?.Dispose();_viewportTarget=null;}
            }
            stats = _gui!.Draw(description,_presenter.Items,_presenter.Text);
            if (stats.EventOverflow != 0) _presenter.CancelInteraction();
        } else stats = _gui!.Draw(description, _items, _text.AsSpan(0,_labels.Length+inputBytes));
        if (_renderer is not null && _state.Minimized == 0 && _state.FramebufferWidth > 0 && _state.FramebufferHeight > 0) {
            _renderer.Resize(_state.FramebufferWidth,_state.FramebufferHeight);
            var logical = _presenter?.Viewport ?? (320f,40f,Math.Max(1,_state.Width-320f),Math.Max(1,_state.Height-40f));
            uint x=Math.Min(_state.FramebufferWidth-1,(uint)Math.Ceiling(logical.Item1*_state.FramebufferWidth/Math.Max(_state.Width,1)));
            uint y=Math.Min(_state.FramebufferHeight-1,(uint)Math.Ceiling(logical.Item2*_state.FramebufferHeight/Math.Max(_state.Height,1)));
            if(x>=_state.FramebufferWidth || y>=_state.FramebufferHeight)return;
            uint width=Math.Clamp((uint)Math.Ceiling(logical.Item3*_state.FramebufferWidth/Math.Max(_state.Width,1)),1,_state.FramebufferWidth-x);
            uint height=Math.Clamp((uint)Math.Ceiling(logical.Item4*_state.FramebufferHeight/Math.Max(_state.Height,1)),1,_state.FramebufferHeight-y);
            if(_viewportTarget is not null){x=0;y=0;width=_viewportTarget.Width;height=_viewportTarget.Height;}
            var configurationWorld=_editor.Play?.Document.World??_editor.Document.World;
            if (_configurationRevision != configurationWorld.Revision || _configurationWorld != configurationWorld.Identity) {
                var configurations=configurationWorld.GetObjects().Where(item=>item.Has<RenderConfiguration>()).Take(2).ToArray();
                if(configurations.Length>1) _log!.Write("warning","render.ambiguous_configuration","Multiple render configurations; keeping the last valid configuration.",_correlation);
                else _configuration=configurations.Length==1?configurations[0].Get<RenderConfiguration>():RenderConfiguration.Default(_editor.Edit.SessionId);
                _configurationRevision=configurationWorld.Revision; _configurationWorld=configurationWorld.Identity;
            }
            if (!ReferencePreview) {
                var scene=_editor.Play is null?_editScene:_playScene;
                if(_editor.Play?.State==Ncma.Gameplay.PlayState.Faulted)scene=null; // Invalid coupled snapshots are never rendered as synchronized.
                Guid camera=_presenter?.SceneCamera??project?.Configuration.SceneCamera??Guid.Empty;
                Ncma.Scene.Rendering.SceneCameraView? browser=null;
                if(_editor.Play is { } followPlay)browser=Ncma.Characters.CharacterPlayRuntime.FollowView(followPlay,camera,width,height);
                if(camera==Guid.Empty) {
                    browser=_presenter?.BrowserCamera(width,height)??new EditorOrbitCamera().View(width,height);
                }
                if(scene?.Submit(frame.FrameId,width,height,camera,browser,_configuration.Exposure,_configuration.Ambient,target:_viewportTarget,origin:new(x,y))!=true) {
                    if(_viewportTarget is not null)_renderer.SubmitResources(frame.FrameId,[],new(0,0,width,height),new(.015f,.02f,.03f,1),new(System.Numerics.Vector3.Zero,System.Numerics.Vector3.UnitY,System.Numerics.Vector4.One),_viewportTarget);
                    else {_renderService!.Configure(ClearConfiguration(_configuration),width,height);_renderService.Submit(frame.FrameId,x,y);}
                }
                _presenter?.ShowRenderDiagnostics(scene?.Diagnostics);
                _presenter?.SetViewportFrame(_editor.Play is null?scene:null,frame.FrameId);
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
                    new Ncma.Rendering.RenderFrameView(_editor.Document.World.Identity,_editor.Document.Revision,selected.Id,transform).CopyColumnMajorModel(model); hasModel = true;
                }
            }
            _renderService.Submit(frame.FrameId,x,y,hasModel ? model : default);
            }
            _gui.RenderGpu(); _renderer.Present();
            var gpuStats=_renderer.Stats;
            if(gpuStats.ValidationErrors!=0 || gpuStats.ValidationWarnings!=0) throw new InvalidOperationException("DX11 validation failed.");
        }
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
    public void Dispose()
    {
        if (_closed) return;
        // Stop at the first failed close. Keep that owner and every downstream dependency
        // for an explicit retry; never unload plugins under a live GPU/solver resource.
        _presenter?.CancelInteraction();
        _nativeDiagnostics?.Dispose(); _nativeDiagnostics = null;
        _animationPreview?.Dispose(); _animationPreview = null;
        _fbxPreview?.Dispose(); _fbxPreview = null;
        _playScene?.Dispose(); _playScene = null;
        _editScene?.Dispose(); _editScene = null;
        _sceneCache?.Dispose(); _sceneCache = null;
        _poseKernel?.Dispose(); _poseKernel = null;
        _assetWorkflow?.Dispose(); _assetWorkflow = null;
        _editor?.Dispose(); _editor = null;
        _gui?.Dispose(); _gui = null;
        _viewportTarget?.Dispose(); _viewportTarget = null;
        _renderService?.Dispose(); _renderService = null;
        _renderer?.Dispose(); _renderer = null;
        _window?.Dispose(); _window = null;
        _physics?.Dispose(); _physics = null;
        _loader.Dispose();
        _log?.Dispose(); _log = null;
        _closed = true;
    }
}
