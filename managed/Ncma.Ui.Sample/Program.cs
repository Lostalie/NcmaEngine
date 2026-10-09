using System.Numerics;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Text;
using Ncma.Ui;
using Ncma.Ui.Rendering;

try {
    string Value(string name, string fallback) { int i = Array.IndexOf(args, name); return i < 0 ? fallback : i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + name); }
    bool smoke = args.Contains("--smoke"); string plugins = Path.GetFullPath(Value("--plugins", "out/build/windows-ninja-debug/m2/plugins"));
    string fontPath = Path.GetFullPath(Value("--font", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc")));
    // Local demo only. The Windows font is read explicitly and never copied or published.
    Guid fontId = Guid.Parse("af503c62-86f4-4af2-8d99-2eb3d797d284");
    var asset = new FontAsset(fontId, File.ReadAllBytes(fontPath), "Explicit local demo font, redistribution not authorized", false);
    var d = UiDefinition.Create(Guid.NewGuid(), "Ncma UI sample");
    UiElement Label(string name, string text, int order, float y) => UiElement.Create(Guid.NewGuid(), name, UiKind.Text, d.Root) with { Order = order, Font = fontId, FontSize = 24, Text = text, Layout = UiLayout.Fixed(32, y, 500, 48) };
    var title = Label("Title", "NcmaEngine 独立 UI", 0, 20); var health = Label("Health", "生命值 100", 1, 80);
    var button = UiElement.Create(Guid.NewGuid(), "Attack", UiKind.Button, d.Root) with { Order = 2, Font = fontId, Text = "攻击 / Attack", Action = "attack", FontSize = 22, Layout = UiLayout.Fixed(32, 146, 240, 48), Style = UiStyle.Default with { Fill = new(.04f, .35f, .8f, 1), CornerRadius = 7 } };
    var field = UiElement.Create(Guid.NewGuid(), "Name", UiKind.TextInput, d.Root) with { Order = 3, Font = fontId, Text = "角色名称", Action = "name.changed", FontSize = 22, Layout = UiLayout.Fixed(32, 216, 400, 48), Style = UiStyle.Default with { Fill = new(.1f, .16f, .24f, 1), Clip = true } };
    var slider = UiElement.Create(Guid.NewGuid(), "Slider", UiKind.Slider, d.Root) with { Order = 4, Action = "volume", Layout = UiLayout.Fixed(32, 292, 400, 24), Style = UiStyle.Default with { Fill = new(.08f, .15f, .23f, 1), Foreground = new(.08f, .6f, .98f, 1), CornerRadius = 5 } };
    d = d with { Elements = [d.Elements[0] with { Style = UiStyle.Default with { Fill = new(.025f, .045f, .075f, 1), Clip = true } }, title, health, button, field, slider] };
    using var loader = new PluginLoader(); loader.Load(plugins, [
        new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
        new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["platform"]),
        new("text", ModuleKind.Text, "NcmaText.dll", "NcmaText.dll", 1, 0, [])]);
    var platform = loader.Modules.Single(m => m.Kind == ModuleKind.Platform); var native = loader.Modules.Single(m => m.Kind == ModuleKind.Renderer); var text = loader.Modules.Single(m => m.Kind == ModuleKind.Text);
    using var window = new PlatformWindow(platform, "NcmaEngine UI sample", 640, 400, !smoke);
    using var renderer = new RendererSession(native, window, 640, 400, pureUi: true); using var textService = new TextService(text);
    // Trusted sample startup, before any canvas/image creation or event/render loop. C4 owns
    // formal Editor/Player defaults and shader package loading; this is the independent UI demo.
    bool preparing = true;
    var shaderCatalog = DefaultUiShaders.CopyCatalog(renderer);
    renderer.CreateUiShaders(RegisteredUiShaders.Prepare(renderer, shaderCatalog, DefaultUiShaders.Select(shaderCatalog), () => preparing));
    preparing = false;
    using var canvas = UiCanvas.Create(new(UiCodec.Decode(UiCodec.Encode(d))), renderer, textService, new Dictionary<Guid, FontAsset> { [fontId] = asset });
    var input = new UiWindowInput(); ulong frame = 1, renderedRevision = ulong.MaxValue; uint width = 640, height = 400; int value = 100;
    for (;;) {
        var state = window.Poll(); if (state.CloseRequested != 0) break;
        if (state.Minimized != 0 || state.FramebufferWidth == 0 || state.FramebufferHeight == 0) { window.Wait(.05); continue; }
        if (width != state.FramebufferWidth || height != state.FramebufferHeight) { width = state.FramebufferWidth; height = state.FramebufferHeight; renderer.Resize(width, height); renderedRevision = ulong.MaxValue; }
        var viewport = new UiViewport(width, height, Math.Clamp(state.ScaleX, .25f, 8), default); canvas.Prepare(viewport);
        input.Pump(canvas, state, window.Events);
        foreach (var action in input.Drain(canvas)) if (action.Action == "attack") { value = Math.Max(0, value - 10); canvas.Runtime.SetText(health.Id, "生命值 " + value); }
        canvas.Prepare(viewport);
        // Static application redraws on state/window changes, not a continuous simulation clock.
        if (smoke || renderedRevision != canvas.Runtime.PresentationRevision) { canvas.Submit(frame++, Vector4.Zero); renderer.Present(); renderedRevision = canvas.Runtime.PresentationRevision; }
        if (smoke && frame == 5) break; if (!smoke) window.Wait(.05);
    }
    canvas.Dispose(); textService.Dispose(); var stats = renderer.Stats;
    if (stats.ValidationErrors != 0 || stats.ValidationWarnings != 0) throw new InvalidOperationException("UI sample graphics validation failed.");
    if(renderer.PipelineStats.Pipelines != 0 || renderer.SkinStats.Meshes != 0 || renderer.UiStats.PureUi != 1 || renderer.UiShaderGeneration != 1) throw new InvalidOperationException("Independent registered UI closure failed.");
    Console.WriteLine(JsonSerializer.Serialize(new { sample = "M7.1-C3 registered UI", frames = frame - 1, registeredUi = true, shaderGeneration = renderer.UiShaderGeneration, worldInitialized = false, physicsInitialized = false, imguiInitialized = false, validationErrors = stats.ValidationErrors, validationWarnings = stats.ValidationWarnings }));
} catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
