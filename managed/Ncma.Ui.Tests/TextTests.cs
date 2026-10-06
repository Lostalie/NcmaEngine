using System.Numerics;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Text;
using Ncma.Ui;

internal static class TextTests
{
    private static void Check(bool value) { if (!value) throw new Exception("UI text assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or PluginException) { return; } throw new Exception("Invalid text operation accepted."); }
    public static IEnumerable<(string Name, Action Run)> Cases(string[] args)
    {
        if (args.Length == 0 || !OperatingSystem.IsWindows()) yield break;
        yield return ("Supplied font bytes shape CJK/combining/wrap with CPU-only lifecycle and cached metrics", () => {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
            // Same explicit Windows test prerequisite as current Editor. Never copied to published assets.
            byte[] bytes = File.ReadAllBytes(path); Guid id = Guid.NewGuid(); var asset = new FontAsset(id, bytes, "Installed Windows font; local regression only, do not redistribute", false);
            using var loader = new PluginLoader(); loader.Load(args[0], [new("text", ModuleKind.Text, "NcmaText.dll", "NcmaText.dll", 1, 0, [])]);
            var module = loader.Modules.Single();
            for (int cycle = 0; cycle < 16; cycle++) {
                using var service = new TextService(module); using var font = service.CreateFont(asset);
                Reject(() => service.CreateFont(new(Guid.NewGuid(), new byte[20], "invalid fixture", false)));
                using var text = service.Prepare(font, "中文动作 HUD e\u0301\n生命值 100", 20, 160, 90);
                byte[] rgba = text.CopyRgba(); Check(text.Metrics.Lines >= 2 && text.Metrics.ContentWidth > 20 && rgba.Where((_, i) => i % 4 == 3).Any(a => a > 0));
                var element = UiElement.Create(Guid.NewGuid(), "Text", UiKind.Text) with { Font = id, Text = text.Text };
                var metrics = new PreparedTextMetrics(new Dictionary<Guid, PreparedText> { [element.Id] = text }); ulong prepared = service.PrepareCalls;
                for (int i = 0; i < 100; i++) Check(metrics.Measure(element, 200).X == text.Metrics.ContentWidth);
                Check(service.PrepareCalls == prepared); Reject(font.Dispose); Reject(service.Dispose);
                Reject(() => service.Prepare(font, "\U0010FFFF", 20, 100, 40)); Reject(() => service.Prepare(font, "Bad", float.NaN, 100, 40));
                text.Dispose(); font.Dispose(); service.Dispose(); Check(module.Status.LiveResources == 0);
            }
        });
    }
}
