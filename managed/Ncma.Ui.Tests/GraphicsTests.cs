using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;

internal static class GraphicsTests
{
    private static void Check(bool value) { if (!value) throw new Exception("UI GPU assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or PluginException) { return; } throw new Exception("Invalid UI GPU operation accepted."); }
    public static IEnumerable<(string Name, Action Run)> Cases(string[] args)
    {
        if (args.Length == 0 || !OperatingSystem.IsWindows()) yield break;
        yield return ("UI actual DX11 reference, clipping, alpha/order, resident batching and lifecycle", () => {
            Check(Marshal.SizeOf<UiVertex>() == 52 && Marshal.SizeOf<UiBatch>() == 40 && Marshal.SizeOf<UiRenderStats>() == 64);
            using var loader = new PluginLoader(); loader.Load(args[0], [
                new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
                new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["platform"])]);
            var platform = loader.Modules.Single(m => m.Kind == ModuleKind.Platform); var native = loader.Modules.Single(m => m.Kind == ModuleKind.Renderer);
            for (int cycle = 0; cycle < 16; cycle++) {
                using var window = new PlatformWindow(platform, "UI pipeline test", 128, 128, false);
                using var renderer = new RendererSession(native, window, 128, 128, pureUi: true);
                Check(renderer.UiStats.PureUi == 1 && renderer.Stats.LiveGroups == 0);
                using var white = renderer.CreateUiImage(1, 1, new byte[] { 255, 255, 255, 255 });
                var builder = new UiDisplayListBuilder(renderer);
                var box = new UiLayoutBox(Guid.NewGuid(), new(0, 0, 80, 80), Matrix3x2.CreateTranslation(10, 10), new(0, 0, 128, 128), 1, true, -1);
                builder.Quad(box, white, new(1, 0, 0, 1));
                builder.Quad(box with { Transform = Matrix3x2.CreateTranslation(30, 30), Clip = new(0, 0, 80, 80) }, white, new(0, 0, 1, .5f));
                Check(builder.VertexCount == 12 && builder.BatchCount == 2);
                using var list = builder.Build(); Reject(white.Dispose); Reject(renderer.Dispose);
                byte[] pixels = new byte[128 * 128 * 4]; renderer.SubmitUi(list, 1, new(0, 0, 0, 1)); renderer.Capture(pixels); renderer.Present();
                int At(int x, int y) => (y * 128 + x) * 4;
                Check(pixels[At(20, 20)] == 255 && pixels[At(20, 20) + 2] == 0);
                Check(Math.Abs(pixels[At(50, 50)] - 128) <= 2 && Math.Abs(pixels[At(50, 50) + 2] - 128) <= 2);
                Check(pixels[At(85, 50)] == 255 && pixels[At(85, 50) + 2] == 0 && pixels[At(110, 110)] == 0);
                Reject(() => renderer.SubmitUi(list, 1, Vector4.Zero));
                var initial = renderer.UiStats; for (ulong frame = 2; frame <= 65; frame++) { renderer.SubmitUi(list, frame, new(0, 0, 0, 1)); renderer.Present(); }
                Check(renderer.UiStats.UploadedBytes == initial.UploadedBytes && renderer.UiStats.Draws == initial.Draws + 128);
                Check(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0);
                list.Dispose(); white.Dispose(); Check(renderer.UiStats.Images == 0 && renderer.UiStats.Lists == 0 && renderer.UiStats.ResidentBytes == 0);
                renderer.Dispose(); Check(native.Status.LiveResources == 0);
            }
        });
    }
}
