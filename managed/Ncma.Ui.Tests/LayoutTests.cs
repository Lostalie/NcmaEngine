using System.Numerics;
using Ncma.Ui;

internal static class LayoutTests
{
    private static void Check(bool value) { if (!value) throw new Exception("UI layout assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("UI layout accepted invalid constraint."); }
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("UI shared flow, padding, gap, Fill, Hug and static layout cache", () => {
            var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var root = d.Elements[0] with { Layout = d.Elements[0].Layout with { Flow = UiFlow.Horizontal, Gap = 10, Padding = new(5, 6, 5, 6) } };
            var a = UiElement.Create(Guid.NewGuid(), "A", UiKind.Rectangle, root.Id) with { Layout = UiLayout.Fixed(0, 0, 50, 20) };
            var b = UiElement.Create(Guid.NewGuid(), "B", UiKind.Rectangle, root.Id) with { Order = 1, Layout = UiLayout.Fixed(0, 0, 0, 20) with { WidthMode = UiSizing.Fill } };
            var engine = new UiLayoutEngine(new(d with { Elements = [root, a, b] }));
            var v = new UiViewport(200, 100, 1, default); var boxes = engine.Layout(v);
            Check(boxes.Length == 3 && boxes[1].Transform.M31 == 5 && boxes[1].Transform.M32 == 6 && boxes[2].Rect.Width == 130 && boxes[2].Transform.M31 == 65);
            ulong measured = engine.MeasurePasses; for (int i = 0; i < 1000; i++) engine.Layout(v);
            Check(engine.LayoutPasses == 1 && engine.MeasurePasses == measured);
            Check(engine.Layout(new(400, 200, 2, default))[2].Rect.Width == 130 && engine.MeasurePasses == measured);
            root = root with { Layout = root.Layout with { WidthMode = UiSizing.Hug, HeightMode = UiSizing.Hug } };
            b = b with { Layout = b.Layout with { WidthMode = UiSizing.Fixed, Width = 30 } };
            var hug = new UiLayoutEngine(new(d with { Elements = [root, a, b] }));
            Check(hug.Layout(v)[0].Rect.Width == 100 && hug.Layout(v)[0].Rect.Height == 32);
        });
        yield return ("UI safe area, anchors, clipping, disabled/hidden ancestry and finite viewport", () => {
            var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var root = d.Elements[0] with { Enabled = false, Style = d.Elements[0].Style with { Clip = true, Opacity = .5f } };
            var a = UiElement.Create(Guid.NewGuid(), "A", UiKind.Rectangle, root.Id) with { Layout = UiLayout.Fixed(0, 0, 20, 30) with { AnchorX = 1, AnchorY = 1 }, Style = UiStyle.Default with { Opacity = .5f } };
            var engine = new UiLayoutEngine(new(d with { Elements = [root, a] })); var boxes = engine.Layout(new(200, 100, 1, new(10, 5, 20, 15)));
            Check(boxes[1].Transform.M31 == 160 && boxes[1].Transform.M32 == 55 && !boxes[1].Enabled && boxes[1].Opacity == .25f && boxes[1].Clip == new UiRect(10, 5, 170, 80));
            Check(new UiLayoutEngine(new(d with { Elements = [root with { Visible = false }, a] })).Layout(new(200, 100, 1, default)).Length == 0);
            Reject(() => engine.Layout(new(float.NaN, 100, 1, default))); Reject(() => engine.Layout(new(100, 100, 1, new(100, 0, 0, 0))));
            Reject(() => new UiLayoutEngine(new(d with { Elements = [root with { Layout = root.Layout with { Rotation = 45 } }, a] })));
        });
        yield return ("UI Hug/Fill cycles fail without document mutation and text metrics are explicit", () => {
            var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var root = d.Elements[0] with { Layout = d.Elements[0].Layout with { WidthMode = UiSizing.Hug } };
            var a = UiElement.Create(Guid.NewGuid(), "A", UiKind.Text, root.Id) with { Text = "中文", Layout = UiLayout.Fixed(0, 0, 0, 20) with { WidthMode = UiSizing.Fill } };
            Reject(() => new UiLayoutEngine(new(d with { Elements = [root, a] })));
            a = a with { Layout = a.Layout with { WidthMode = UiSizing.Hug } };
            var engine = new UiLayoutEngine(new(d with { Elements = [root, a] })); Reject(() => engine.Layout(new(200, 100, 1, default)));
            var metrics = new TestMetrics(); var measured = new UiLayoutEngine(new(d with { Elements = [root, a] }), metrics);
            Check(measured.Layout(new(200, 100, 1, default))[0].Rect.Width == 42); // test metrics, NOT a font/shaping implementation
        });
        yield return ("UI large topology remains bounded and static frames allocate no layout data", () => {
            var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var elements = new List<UiElement> { d.Elements[0] };
            for (int i = 1; i < 4096; i++) elements.Add(UiElement.Create(Guid.NewGuid(), "Rectangle", UiKind.Rectangle, d.Root) with { Order = i });
            var engine = new UiLayoutEngine(new(d with { Elements = elements.ToArray() })); var v = new UiViewport(1280, 720, 1, default);
            Check(engine.Layout(v).Length == 4096); long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++) engine.Layout(v); Check(GC.GetAllocatedBytesForCurrentThread() - allocated == 0);
        });
    }
    private sealed class TestMetrics : IUiTextMetrics { public Vector2 Measure(UiElement element, float availableWidth) => new(42, 20); }
}
