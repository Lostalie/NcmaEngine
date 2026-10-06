using System.Text;
using System.Text.Json;
using Ncma.Ui;

static void Check(bool condition) { if (!condition) throw new Exception("UI assertion failed."); }
static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or JsonException or InvalidOperationException or DecoderFallbackException) { return; } throw new Exception("Invalid UI accepted."); }
var cases = new List<(string Name, Action Run)> {
    ("Strict UI v1 codec and owner copies", () => {
        var d = UiDefinition.Create(Guid.NewGuid(), "动作 HUD"); byte[] bytes = UiCodec.Encode(d);
        Check(UiCodec.Decode(bytes).Root == d.Root); var runtime = new UiDocument(d);
        d.Elements[0] = d.Elements[0] with { Name = "Changed" }; Check(runtime.Capture().Elements[0].Name == "Root");
        var copy = runtime.Capture(); copy.Elements[0] = copy.Elements[0] with { Name = "Tampered" }; Check(runtime.Capture().Elements[0].Name == "Root");
        Task.Run(() => Reject(() => runtime.Capture())).GetAwaiter().GetResult();
    }),
    ("Duplicate, missing, unknown, integer enum, old and malformed formats reject", () => {
        string json = Encoding.UTF8.GetString(UiCodec.Encode(UiDefinition.Create(Guid.NewGuid(), "Test")));
        foreach (string bad in new[] { json.Replace("\"version\":1", "\"version\":1,\"version\":1"), json.Replace("\"version\":1,", ""),
            json.Replace("\"version\":1", "\"version\":2"), json.Replace("\"version\":1", "\"version\":1,\"nativeHandle\":3"),
            json.Replace("\"kind\":\"frame\"", "\"kind\":0"), "{}", "{\"nodes\":[]}", "NCUI1", "{\"language\":\"python\"}" }) Reject(() => UiCodec.Decode(Encoding.UTF8.GetBytes(bad)));
        Reject(() => UiCodec.Decode(new byte[] { 123, 255, 125 })); Reject(() => UiCodec.Decode(new byte[UiCodec.MaxBytes + 1]));
    }),
    ("Hierarchy identity, cycle, depth, leaf and order rejection", () => {
        var d = UiDefinition.Create(Guid.NewGuid(), "Test"); var child = UiElement.Create(Guid.NewGuid(), "Child", UiKind.Rectangle, d.Root);
        UiCodec.Validate(d with { Elements = [d.Elements[0], child] });
        Reject(() => UiCodec.Validate(d with { Elements = [d.Elements[0], child with { Parent = Guid.NewGuid() }] }));
        Reject(() => UiCodec.Validate(d with { Elements = [d.Elements[0], child, child with { Id = Guid.NewGuid() }] }));
        Reject(() => UiCodec.Validate(d with { Elements = [d.Elements[0], child with { Id = d.Root }] }));
        var grandchild = UiElement.Create(Guid.NewGuid(), "Grandchild", UiKind.Text, child.Id); Reject(() => UiCodec.Validate(d with { Elements = [d.Elements[0], child, grandchild] }));
        var list = new List<UiElement> { d.Elements[0] }; for (int i = 1; i <= 64; i++) list.Add(UiElement.Create(Guid.NewGuid(), "Frame", UiKind.Frame, list[^1].Id));
        UiCodec.Validate(d with { Elements = list.Take(64).ToArray() }); Reject(() => UiCodec.Validate(d with { Elements = list.ToArray() }));
        var a = child with { Kind = UiKind.Frame, Parent = grandchild.Id }; Reject(() => UiCodec.Validate(d with { Elements = [d.Elements[0], a, grandchild with { Kind = UiKind.Frame }] }));
    }),
    ("Finite values, Unicode, typed tokens and UUID dependencies", () => {
        var d = UiDefinition.Create(Guid.NewGuid(), "Test"); var root = d.Elements[0];
        Reject(() => UiCodec.Encode(d with { Elements = [root with { Layout = root.Layout with { Width = float.NaN } }] }));
        Reject(() => UiCodec.Encode(d with { Elements = [root with { Name = "\uD800" }] }));
        Reject(() => UiCodec.Encode(d with { Elements = [root with { Style = root.Style with { FillToken = Guid.NewGuid() } }] }));
        Guid token = Guid.NewGuid(); var t = new UiToken(token, "accent", UiTokenKind.Color, UiColor.White, 0);
        UiCodec.Validate(d with { Tokens = [t], Elements = [root with { Style = root.Style with { FillToken = token } }] });
        Reject(() => UiCodec.Validate(d with { Tokens = [t], Elements = [root with { Style = root.Style with { OpacityToken = token } }] }));
        Guid font = Guid.NewGuid(); var text = UiElement.Create(Guid.NewGuid(), "Text", UiKind.Text, root.Id) with { Text = "中文 😀 e\u0301", Font = font };
        Check(UiCodec.Dependencies(d with { Elements = [root, text] }).SequenceEqual(new[] { font }));
    }),
};
cases.AddRange(AuthoringTests.Cases());
cases.AddRange(LayoutTests.Cases());
cases.AddRange(GraphicsTests.Cases(args));
cases.AddRange(TextTests.Cases(args));
cases.AddRange(RuntimeTests.Cases());
cases.AddRange(CanvasTests.Cases(args));
foreach (var test in cases) {
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { Console.Error.WriteLine($"FAIL {test.Name}: {error}"); Environment.ExitCode = 1; return; }
}
Console.WriteLine($"Ncma UI: {cases.Count} cases passed.");
