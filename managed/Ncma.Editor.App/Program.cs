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
namespace Ncma.Editor.App;
internal static class Program
{
    // Synchronous Main is the sole owner; no hostfxr, unmanaged Run or async continuation.
    [STAThread]
    public static int Main(string[] args)
    {
        try {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, DeploymentManifest.FileName))) DeploymentManifest.Validate(AppContext.BaseDirectory);
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
            Console.WriteLine($"candidate_{(cpuOnly ? "cpu_gui" : "dx11_reference")} frames={rendered}; resources released");
            return 0;
        } catch (ArgumentException e) { Console.Error.WriteLine("invalid_configuration: " + e.Message); return 2; }
        catch (Exception e) { Console.Error.WriteLine("candidate_failed: " + e.Message); return 4; }
    }
}
internal sealed unsafe class CandidatePresentation(string plugins, ProjectContext? project, bool smoke, bool cpuOnly, bool preview) : IApplicationService, IFramePipeline
{
    private readonly PluginLoader _loader = new();
    private PhysicsService? _physics;
    private PlatformWindow? _window;
    private GuiSession? _gui;
    private RendererSession? _renderer;
    private RenderPipelineService? _renderService;
    private CompiledRenderGraph? _graph;
    private uint _viewWidth, _viewHeight;
    private ulong _configurationRevision = ulong.MaxValue;
    private RenderConfiguration _configuration;
    private EditorSessionOwner? _editor;
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
            new("ncma.renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 1, ["ncma.platform"]),
            new("ncma.gui", ModuleKind.Gui, "NcmaGui.dll", "NcmaGui.dll", 1, 2, ["ncma.platform", "ncma.renderer"]) ]);
        // Independent optional module only; no scene rigid bodies or Play Step dispatch.
        _physics = new(plugins, project?.Configuration.PhysicsEnabled == true);
        var physics = _physics.Inspect();
        if (physics.Enabled)
            _log.Write("info", "physics.module_ready", $"Physics service ABI {physics.AbiMajor}.{physics.AbiMinor}; capabilities={physics.Capabilities}; scene integration disabled.", _correlation);
        _window = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "NcmaEngine — managed candidate", 1280, 720, !smoke);
        _window.SetIcon(Path.Combine(AppContext.BaseDirectory,"NcmaEngine.ico"));
        string font=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc");
        if(!smoke && !preview)_preferences=new(Path.Combine(project?.Root??AppContext.BaseDirectory,"out/user/editor/preferences.json"),font);
        _gui = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Gui), _window, _preferences?.Current.FontPath??font,fontSize:_preferences?.Current.FontSize??18);
        if (!cpuOnly) {
            _renderer = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), _window, 1280, 720);
            _renderService = new(_renderer); _gui.AttachRenderer(_renderer);
        }
        _log.Write("info", cpuOnly ? "presentation.cpu_only" : "presentation.dx11_reference", "Managed candidate presentation initialized.", _correlation);
        _editor = new(project?.Configuration.Name ?? "Presentation smoke", components: RenderConfiguration.CreateRegistry());
        if (project is not null) {
            var workspace = new EditorWorkspace(_editor);
            workspace.Open(workspace.Stamp, project.StartupScenePath, true);
            _editor.Edit!.Resynchronize(); // Startup is the saved baseline, not a user Open command.
            _editor.LoadGameplay(project.GameplayAssemblyPath);
        }
        if (!preview) {
            _fbxPreview = new(Path.Combine(plugins, "NcmaNative.dll")); // lazy immutable kernel loading on trusted local import only
            _animationPreview = new(Path.Combine(plugins,"NcmaNative.dll"));
            _filePicker=new();
            _presenter = new(new EditorWorkspace(_editor), project?.GameplayAssemblyPath, project?.Root, _fbxPreview, _animationPreview,_preferences,_filePicker.Choose,_log);
        }
        if (_renderer is not null) {
            _configuration=RenderConfiguration.Default(_editor.Edit!.SessionId);
            _renderService!.Configure(_configuration,960,680);
            RenderingEditorAdapter.RegisterInspections(_editor.Edit,_renderService,_renderer);
        }
        string[] labels = ["NcmaEngine candidate", "C# frame loop / native GPU execution", cpuOnly ? "CPU GUI 验证模式" : "C# 渲染图 / DX11 PBR 参考预览", "中文输入诊断（非场景编辑）"];
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
    public void PumpAgent(FrameContext frame) { _editor!.Endpoint?.Pump(); }
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
            if (_configurationRevision != _editor.Edit!.Revision) {
                var configurations=_editor.Document.World.GetObjects().Where(item=>item.Has<RenderConfiguration>()).Take(2).ToArray();
                if(configurations.Length>1) _log!.Write("warning","render.ambiguous_configuration","Multiple render configurations; keeping the last valid configuration.",_correlation);
                else _configuration=configurations.Length==1?configurations[0].Get<RenderConfiguration>():RenderConfiguration.Default(_editor.Edit.SessionId);
                _configurationRevision=_editor.Edit.Revision;
            }
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
            _renderService.Submit(frame.FrameId,x,y,hasModel ? model : default); _gui.RenderGpu(); _renderer.Present();
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
        var failures = new List<Exception>();
        try { _presenter?.CancelInteraction(); } catch (Exception e) { failures.Add(e); }
        try { _nativeDiagnostics?.Dispose(); _nativeDiagnostics = null; } catch (Exception e) { failures.Add(e); }
        try { _animationPreview?.Dispose(); _animationPreview = null; } catch (Exception e) { failures.Add(e); }
        try { _fbxPreview?.Dispose(); _fbxPreview = null; } catch (Exception e) { failures.Add(e); }
        try { _editor?.Dispose(); _editor = null; } catch (Exception e) { failures.Add(e); }
        try { _gui?.Dispose(); _gui = null; } catch (Exception e) { failures.Add(e); }
        try { _renderService?.Dispose(); _renderService = null; } catch (Exception e) { failures.Add(e); }
        try { _renderer?.Dispose(); _renderer = null; } catch (Exception e) { failures.Add(e); }
        try { _window?.Dispose(); _window = null; } catch (Exception e) { failures.Add(e); }
        try { _physics?.Dispose(); _physics = null; } catch (Exception e) { failures.Add(e); }
        try { _loader.Dispose(); } catch (Exception e) { failures.Add(e); }
        try { _log?.Dispose(); _log = null; } catch (Exception e) { failures.Add(e); }
        if (failures.Count != 0) throw new AggregateException(failures);
        _closed = true;
    }
}
