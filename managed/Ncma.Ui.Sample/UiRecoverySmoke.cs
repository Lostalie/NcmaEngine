using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

// Explicit automated demo mode, never an automatic recovery or permission decision
// in the ordinary interactive sample. Requires no World, font or 3D asset.
internal static class UiRecoverySmoke
{
    internal static void Run(string plugins, RuntimeShaderFileSet? shaders)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks++; }
        using var loader = new PluginLoader();
        loader.Load(plugins, [
            new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
            new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["platform"])]);
        var native = loader.Modules.Single(m => m.Kind == ModuleKind.Renderer);
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Pure UI recovery verification", 256, 128, false);
        var definition = UiDefinition.Create(Guid.NewGuid(), "Independent recovery");
        var slider = UiElement.Create(Guid.NewGuid(), "Value", UiKind.Slider, definition.Root) with {
            Action = "value", Layout = UiLayout.Fixed(8, 16, 160, 24)
        };
        definition = definition with { Elements = [definition.Elements[0], slider] };
        Guid project = Guid.NewGuid();
        byte[] bytes = RuntimeUiPackage.Encode(project, 1, definition, [], []);
        using var package = RuntimeUiPackage.Decode(bytes, project, definition.AssetId, 1, Convert.ToHexString(SHA256.HashData(bytes)));
        using var ui = new SceneUiPresentation();
        UiRuntime? retained = null;
        byte[]? reference = null;
        ulong previousGeneration = 0, frame = 0;
        NativeFailureDiagnostic? first = null;
        for (int cycle = 0; cycle < 3; cycle++) {
            using var renderer = new RendererSession(native, window, 256, 128, pureUi: true);
            shaders?.InstallSelection(renderer, () => true);
            var surface = new RendererWindowSurface(renderer);
            var visible = window.State;
            visible.FramebufferWidth = 256; visible.FramebufferHeight = 128; visible.Minimized = 0;
            ui.Prepare(package, renderer, plugins, new(256, 128, 1, default), interactive: true);
            Check(renderer.ReadFrameState().Generation != previousGeneration, "Fresh pure UI renderer generation required");
            previousGeneration = renderer.ReadFrameState().Generation;
            if (retained is null) {
                retained = ui.Runtime;
                retained.Input(retained.Stamp, [new(UiInputKind.PointerDown, 1, new(108, 28)), new(UiInputKind.PointerUp, 2, new(108, 28))]);
                Check(retained.Widget(slider.Id).Value == .625f && retained.QueuedActions != 0, "Copied slider proposal required");
                retained.CancelInput();
            }
            Check(ReferenceEquals(retained, ui.Runtime) && retained.Widget(slider.Id).Value == .625f && retained.QueuedActions == 0,
                "Independent widget state survives without replaying proposals");
            byte[] Draw(WindowState state, bool present = true) {
                if (!surface.Prepare(state)) return [];
                ui.PrepareView(new(state.FramebufferWidth, state.FramebufferHeight, 1, default));
                ui.Submit(++frame, overlay: false);
                var pixels = new byte[checked((int)(state.FramebufferWidth * state.FramebufferHeight * 4))];
                renderer.Capture(pixels);
                if (present) renderer.Present();
                return pixels;
            }
            var image = Draw(visible);
            reference ??= image;
            Check(image.SequenceEqual(reference), "Exact pure UI pixels after new generation");
            var stats = renderer.Stats;
            var hidden = visible; hidden.Minimized = 1; hidden.FramebufferWidth = hidden.FramebufferHeight = 0;
            Check(Draw(hidden).Length == 0 && renderer.Stats.SubmittedFrames == stats.SubmittedFrames, "Hidden pure UI does not submit");
            hidden.Minimized = 0; hidden.FramebufferHeight = 128;
            Check(Draw(hidden).Length == 0 && renderer.Stats.Presents == stats.Presents, "Zero-size pure UI does not present");
            var resized = visible; resized.FramebufferWidth = 311; resized.FramebufferHeight = 193;
            Check(Draw(resized).Length == 311 * 193 * 4 && Draw(visible).SequenceEqual(reference), "Pure UI resize and restore pixel parity");
            var before = renderer.UiStats; ulong preparations = ui.Preparations;
            for (int i = 0; i < 16; i++) { ui.PrepareView(new(256, 128, 1, default)); ui.Submit(++frame, false); renderer.Present(); }
            Check(renderer.UiStats.UploadedBytes == before.UploadedBytes && ui.Preparations == preparations, "Pure UI warm frames reuse display lists and uploads");
            var environment = renderer.EnvironmentStats; var post = renderer.PostStats;
            Check(renderer.Stats.LiveGroups == 0 && renderer.ResourceStats.Targets == 0 && renderer.PipelineStats.Pipelines == 0 && renderer.SkinStats.Meshes == 0 &&
                environment.Live == 0 && environment.ResidentBytes == 0 && environment.UploadedBytes == 0 &&
                post.Bindings == 0 && post.Targets == 0 && post.Programs == 0 && post.ResidentBytes == 0 &&
                renderer.UiStats.PureUi == 1 && !ui.TextLoaded, "No World/3D/physics/font resources in independent UI recovery");
            using var held = renderer.CreateUiImage(1, 1, new byte[] { 255, 255, 255, 255 });
            Draw(visible, present: false);
            Check(renderer.ReadFrameState().Active, "Retain actually submitted incomplete pure UI frame");
            renderer.AbandonFrameForShutdown(frame);
            first ??= renderer.ReadFirstFailure();
            Check(renderer.ReadFirstFailure() is { Operation: (uint)GpuFailureOperation.ExplicitAbandon, NativeCode: null }, "Explicit abandon does not invent physical device-loss HRESULT");
            ui.RetireGraphics(); surface.BeginShutdown();
            bool rejected = false;
            try { renderer.Dispose(); } catch (PluginException error) when (error.Result == PluginResult.Busy) { rejected = true; }
            Check(rejected && native.Status.LiveResources > 0, "Live pure UI image prevents premature device retirement");
            held.Dispose();
            Check(renderer.UiStats.ResidentBytes == 0 && renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0, "Pure UI resource drain and API0/0");
            renderer.Dispose(); surface.CompleteShutdown();
            Check(native.Status.LiveResources == 0, "Pure UI native resources zero after each generation");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { sample = "M7.6 independent UI recovery", checks, cycles = 3,
            runtimeShaderPackage = shaders is not null, exactPixels = true, no3D = true, physicalDeviceLoss = false,
            syntheticInput = true, nativeDiagnostic = first, fullPerformanceAccepted = false }));
    }
}
