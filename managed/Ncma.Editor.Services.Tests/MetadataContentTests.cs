using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

// Whole M8.4 end only. Construction performs real asset IO only when the harness runs.
internal static class MetadataContentTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Metadata content assertion."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException or IOException) { return; }
        throw new Exception("Expected metadata content rejection.");
    }
    private sealed class Clock : TimeProvider
    {
        internal long Now;
        internal Action? Callback;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { Callback?.Invoke(); return Now; }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly Clock Time = new();
        internal readonly Guid Id = Guid.NewGuid(), Project = Guid.NewGuid(), Sub = Guid.NewGuid();
        internal readonly string Root, File;
        internal readonly EditorSessionOwner Owner;
        internal readonly EditorMetadataContent Content;
        internal readonly AiAuthoringWorkspace Controller;
        internal readonly AssetRecord Record;
        internal Fixture(string output, bool imported = false)
        {
            Root = Path.Combine(output, "metadata-local-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "assets"));
            // Deliberately NOT SourcePath + .ncmeta. No source blob exists: review must not open it.
            File = Path.Combine(Root, "assets/descriptor.ncmeta");
            Record = new(1, Id, AssetKind.Texture, "assets/private-image.png", new string('A', 64), "image", 1, new(1, 60, true),
                imported ? [new(Sub, AssetKind.Clip, "clip", "私人😀", false)] : [], [], null);
            System.IO.File.WriteAllBytes(File, AssetRecordCodec.Encode(Record));
            Owner = new("Metadata content", workflowTime: Time);
            Owner.ConfigureAssets(Root, Project, 1); // Default deny writer, no MCP endpoint.
            Owner.ConfigureDomainProposals(Project);
            Content = Owner.CreateMetadataContentReview();
            Controller = new(Owner.DomainProposals!, metadata: Content);
        }
        internal MetadataContentReview Capture() => Controller.ReviewMetadataContent(Id, AssetKind.Texture);
        public void Dispose() { Check(Controller.TryClose()); Owner.Dispose(); }
    }
    public static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.4-D metadata local exact file mapping complete copy no export or write", () => {
            using var f = new Fixture(output); byte[] before = System.IO.File.ReadAllBytes(f.File);
            var review = f.Capture(); byte[] bytes = review.CopyBytes();
            using var json = JsonDocument.Parse(bytes);
            Check(json.RootElement.GetProperty("metadataPath").GetString() == "assets/descriptor.ncmeta");
            Check(json.RootElement.GetProperty("record").GetProperty("sourcePath").GetString() == "assets/private-image.png");
            Check(review.SourceSha256 == Convert.ToHexString(SHA256.HashData(before)));
            bytes[0] = 0; Check(review.CopyBytes()[0] != 0); f.Controller.ValidateMetadataContent(review);
            Check(f.Owner.Endpoint is null && f.Controller.Profile is null && f.Owner.Edit!.State.UndoCount == 0 && before.SequenceEqual(System.IO.File.ReadAllBytes(f.File)));
            Reject(() => f.Content.Capture(Guid.NewGuid(), AssetKind.Texture)); Check(!f.Content.IsCurrent(review));
            Reject(() => f.Content.Capture(f.Id, AssetKind.Clip));
        });
        yield return ("M8.4-D metadata external byte or semantic changes fail explicit revalidation", () => {
            using var f = new Fixture(output); var review = f.Capture();
            System.IO.File.AppendAllText(f.File, " "); // Even equivalent JSON requires a new exact-byte review.
            Reject(() => f.Content.ValidateCurrent(review)); Check(!f.Content.IsCurrent(review));
            review = f.Capture(); f.Content.ValidateCurrent(review);
            System.IO.File.WriteAllBytes(f.File, AssetRecordCodec.Encode(f.Record with { Settings = new(1, 30, false) }));
            Reject(() => f.Content.ValidateCurrent(review)); Reject(() => f.Capture());
            Check(f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-D metadata replacement cancellation expiry and asset revision invalidate", () => {
            using var f = new Fixture(output); var old = f.Capture(); var current = f.Capture();
            Check(!f.Content.IsCurrent(old)); f.Content.ValidateCurrent(current);
            f.Controller.Cancel(); Check(!f.Content.IsCurrent(current));
            current = f.Capture(); f.Time.Now += 60_000; Check(!f.Content.IsCurrent(current));
            current = f.Capture(); f.Owner.Assets!.Clock.Advance(); Check(!f.Content.IsCurrent(current)); Reject(() => f.Capture());
        });
        yield return ("M8.4-D metadata foreign ticket thread reentry and document change reject", () => {
            using var f = new Fixture(output); using var other = new Fixture(output); var review = f.Capture();
            Check(!other.Content.IsCurrent(review)); Reject(() => other.Content.ValidateCurrent(review));
            Task.Run(() => Reject(() => f.Content.Capture(f.Id, AssetKind.Texture))).GetAwaiter().GetResult();
            f.Time.Callback = () => f.Content.Invalidate(); Reject(() => f.Capture()); f.Time.Callback = null;
            review = f.Capture(); var workspace = new EditorWorkspace(f.Owner); workspace.CreateObject(workspace.Stamp);
            Check(!f.Content.IsCurrent(review));
        });
        yield return ("M8.4-D metadata imported root subasset budgets malformed and recovery reject", () => {
            using (var imported = new Fixture(output, imported: true)) {
                Reject(() => imported.Capture()); Reject(() => imported.Content.Capture(imported.Sub, AssetKind.Clip));
            }
            using var f = new Fixture(output); byte[] original = System.IO.File.ReadAllBytes(f.File);
            System.IO.File.AppendAllText(f.File, new string(' ', EditorMetadataContent.MaximumBytes)); Reject(() => f.Capture());
            System.IO.File.WriteAllText(f.File, "{\"version\":1,\"version\":1}"); Reject(() => f.Capture());
            System.IO.File.WriteAllBytes(f.File, original); var review = f.Capture();
            System.IO.File.WriteAllText(f.File + ".journal", "{}"); Reject(() => f.Content.ValidateCurrent(review));
        });
        yield return ("M8.4-D metadata controller no outbound path reconfigure disconnect close revoke", () => {
            using var f = new Fixture(output); var review = f.Capture();
            Check(f.Controller.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
            Check(!f.Content.IsCurrent(review));
            Reject(() => f.Controller.Prepare(AiAuthoringIntent.AssetMetadata, f.Id, "change settings"));
            for (int i = 0; i < 12; i++) { review = f.Capture(); f.Controller.Cancel(); Check(!f.Content.IsCurrent(review)); }
            review = f.Capture(); Check(f.Controller.Disconnect()); Check(!f.Content.IsCurrent(review));
            review = f.Capture(); Check(f.Controller.TryClose()); Check(!f.Content.IsCurrent(review));
            Reject(() => f.Controller.ReviewMetadataContent(f.Id, AssetKind.Texture));
            Check(f.Owner.Edit!.State.UndoCount == 0 && f.Owner.Endpoint is null);
        });
    }
}
