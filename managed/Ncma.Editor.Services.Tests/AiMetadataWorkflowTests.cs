using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

// Registered only; real file transactions and substitute HTTP run at whole-stage validation.
internal static class AiMetadataWorkflowTests
{
    private static void Check(bool value) { if (!value) throw new Exception("AI metadata assertion"); }
    private static void Reject(Action action) {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException or AiProtocolException or JsonException or IOException) { return; }
        throw new Exception("Expected AI metadata rejection");
    }
    private static void Until(Func<bool> condition) {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Metadata fixture drain"); Thread.Sleep(1); }
    }
    private static string Draft(Guid id, int rate = 30) => JsonSerializer.Serialize(new { version = 1, capability = AiMetadataDraft.Capability,
        input = new { assetId = id, settings = new { version = 1, sampleRate = rate, generateTangents = false } } });
    private sealed class Handler : HttpMessageHandler
    {
        internal string Proposal = "{}";
        internal int Calls;
        internal byte[]? Sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Sent = await request.Content!.ReadAsByteArrayAsync(cancellationToken); Interlocked.Increment(ref Calls);
            var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new {
                id = "metadata-fixture", @object = "chat.completion", created = 1, model = "metadata-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = Proposal }, finish_reason = "stop" } }
            })) };
            reply.Content.Headers.ContentType = new("application/json"); return reply;
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner = new("AI metadata");
        internal readonly Guid Id = Guid.NewGuid(), Project = Guid.NewGuid();
        internal readonly string Root, FilePath;
        internal readonly EditorMetadataContent Content;
        internal readonly AiAuthoringWorkspace Ui;
        internal readonly Handler Transport = new();
        internal readonly byte[] Before, SceneBefore;
        internal bool Granted;
        internal EditorDomainProposals Domains => Owner.DomainProposals!;
        internal Fixture(string output, bool writable = true) {
            Root = Path.Combine(output, "ai-metadata-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(Root, "assets"));
            byte[] source = [1, 2, 3]; File.WriteAllBytes(Path.Combine(Root, "assets/private.bin"), source);
            FilePath = Path.Combine(Root, "assets/descriptor.ncmeta");
            var record = new AssetRecord(1, Id, AssetKind.Texture, "assets/private.bin", Convert.ToHexString(SHA256.HashData(source)),
                "fixture", 1, new(1, 60, true), [], [], null);
            Before = AssetRecordCodec.Encode(record); File.WriteAllBytes(FilePath, Before); Granted = writable;
            Owner.ConfigureAssets(Root, Project, 1, new AssetWriteScope(["assets/descriptor.ncmeta"], ["assets/private.bin"], [Id], () => Granted));
            Owner.ConfigureDomainProposals(Project); Content = Owner.CreateMetadataContentReview();
            Ui = new(Domains, (snapshot, operation, credential) => new(snapshot, operation, credential, Transport), metadata: Content);
            Check(Ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "metadata-model"));
            SceneBefore = Owner.Document.CaptureBytes(); Transport.Proposal = Draft(Id);
        }
        internal AiDomainRequestReview Prepare() => Ui.PrepareMetadata(Id, AssetKind.Texture, "Change sample rate to 30 and disable tangents");
        internal void Start() { var review = Prepare(); Ui.Send(review, review.Observation.Fingerprint, true); }
        internal void Complete() => Until(() => { Domains.PumpModules(); return Ui.Status != "pending"; });
        internal DomainProposalReview Result() { Start(); Complete(); return Ui.RefreshReview() ?? throw new Exception("Metadata review missing"); }
        internal void Unchanged() => Check(Before.SequenceEqual(File.ReadAllBytes(FilePath)) && SceneBefore.SequenceEqual(Owner.Document.CaptureBytes()) && Owner.Edit!.State.UndoCount == 0);
        public void Dispose() { Until(Ui.TryClose); Owner.Dispose(); }
    }
    public static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.4-D metadata closed settings draft rejects paths records handles and malformed values", () => {
            Guid id = Guid.NewGuid(); string valid = Draft(id); Check(AiMetadataDraft.Decode(Encoding.UTF8.GetBytes(valid)).SampleRate == 30);
            foreach (string bad in new[] { Draft(Guid.Empty), Draft(id, 0), Draft(id, 121),
                valid.Replace("\"settings\":", "\"path\":\"assets/other.ncmeta\",\"settings\":"),
                valid.Replace("\"settings\":", "\"record\":{},\"settings\":"),
                valid.Replace("\"settings\":", "\"proposalId\":\"00000000-0000-0000-0000-000000000001\",\"settings\":"),
                valid.Replace("\"sampleRate\":30", "\"sampleRate\":30,\"sampleRate\":30"),
                valid.Replace("\"sampleRate\":30", "\"sampleRate\":30.5"),
                valid.Replace("\"generateTangents\":false", "\"generateTangents\":\"false\"") })
                Reject(() => AiMetadataDraft.Decode(Encoding.UTF8.GetBytes(bad)));
        });
        yield return ("M8.4-D metadata exact outbound bytes second confirmation original Commit Undo Redo", () => {
            using var f = new Fixture(output); var send = f.Prepare(); byte[] wire = send.WireRequest.CopyBytes();
            Check(f.Owner.Endpoint is null && f.Transport.Calls == 0);
            string logical = Encoding.UTF8.GetString(send.Observation.CopyBytes());
            Check(logical.Contains("local.asset.metadata", StringComparison.Ordinal) && logical.Contains("assets/private.bin", StringComparison.Ordinal) && logical.Contains("assets/descriptor.ncmeta", StringComparison.Ordinal));
            byte[] copy = send.Observation.CopyBytes(); copy[0] = 0; Check(send.Observation.CopyBytes()[0] != 0);
            Reject(() => f.Ui.Send(send, send.Observation.Fingerprint, false)); Reject(() => f.Ui.Send(send, "wrong", true));
            f.Unchanged(); f.Ui.Send(send, send.Observation.Fingerprint, true); f.Complete();
            Check(f.Transport.Calls == 1 && wire.SequenceEqual(f.Transport.Sent!));
            var result = f.Ui.RefreshReview()!; f.Unchanged();
            using var command = JsonDocument.Parse(result.CopyPayload()); var input = command.RootElement.GetProperty("input");
            Check(input.GetProperty("operation").GetString() == "settings" && input.GetProperty("path").GetString() == "assets/descriptor.ncmeta");
            Reject(() => f.Ui.Confirm(result, result.Fingerprint, false)); f.Unchanged();
            Check(f.Ui.Confirm(result, result.Fingerprint, true).Status == "ok");
            byte[] after = File.ReadAllBytes(f.FilePath); var record = AssetRecordCodec.Decode(after);
            Check(record.AssetId == f.Id && record.Settings == new ImportSettings(1, 30, false) && f.Owner.Edit!.State.UndoCount == 1);
            var workspace = new EditorWorkspace(f.Owner); workspace.History(workspace.Stamp, false); Check(f.Before.SequenceEqual(File.ReadAllBytes(f.FilePath)));
            workspace.History(workspace.Stamp, true); Check(after.SequenceEqual(File.ReadAllBytes(f.FilePath)) && f.SceneBefore.SequenceEqual(f.Owner.Document.CaptureBytes()));
        });
        yield return ("M8.4-D metadata read selection does not grant original write authority", () => {
            using (var denied = new Fixture(output, writable: false)) {
                denied.Start(); denied.Complete(); Check(denied.Ui.RefreshReview() is null); denied.Unchanged();
            }
            using var f = new Fixture(output); var result = f.Result(); f.Granted = false;
            var receipt = f.Ui.Confirm(result, result.Fingerprint, true); Check(receipt.Status != "ok"); f.Unchanged();
        });
        yield return ("M8.4-D metadata external file change before Send or final Commit rejects", () => {
            using (var f = new Fixture(output)) {
                var send = f.Prepare(); File.AppendAllText(f.FilePath, " ");
                Reject(() => f.Ui.Send(send, send.Observation.Fingerprint, true)); Check(f.Transport.Calls == 0 && f.Owner.Edit!.State.UndoCount == 0);
            }
            using (var f = new Fixture(output)) {
                var result = f.Result(); File.AppendAllText(f.FilePath, " ");
                Reject(() => f.Domains.Confirm(result, result.Fingerprint, true)); Check(f.Owner.Edit!.State.UndoCount == 0);
            }
        });
        yield return ("M8.4-D metadata cancellation reconfiguration and domain revoke invalidate direct Commit", () => {
            foreach (int mode in new[] { 0, 1, 2 }) {
                using var f = new Fixture(output); var result = f.Result();
                if (mode == 0) f.Ui.Cancel(); else if (mode == 1) Check(f.Ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "other-model")); else f.Domains.Invalidate();
                Reject(() => f.Domains.Confirm(result, result.Fingerprint, true)); f.Unchanged();
            }
        });
        yield return ("M8.4-D metadata wrong asset and raw command cannot bypass settings-only stage", () => {
            foreach (int mode in new[] { 0, 1 }) {
                using var f = new Fixture(output);
                f.Transport.Proposal = mode == 0 ? Draft(Guid.NewGuid()) : JsonSerializer.Serialize(new { version = 1, capability = "ncma.assets.metadata.edit", input = new { operation = "create" } });
                f.Start(); f.Complete(); Check(f.Ui.RefreshReview() is null); f.Unchanged();
            }
        });
        yield return ("M8.4-D metadata passive polling has no file IO and full claim/delivery rechecks", () => {
            using var f = new Fixture(output); var send = f.Prepare(); var approved = send.Approve(send.Observation.Fingerprint, true);
            _ = f.Domains.ClaimObservation(approved, send.Observation.Target);
            File.WriteAllText(f.FilePath, "{malformed-external");
            Check(f.Domains.RequestIdentityCurrent(approved)); Check(!f.Domains.RequestCurrent(approved));
            Check(f.Transport.Calls == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-D metadata foreign project thread stale review and 12 cancel cycles", () => {
            using var f = new Fixture(output); using var other = new Fixture(output);
            Reject(() => f.Content.SelectDomainObservation(other.Domains.CaptureSource(), f.Id, AssetKind.Texture));
            Task.Run(() => Reject(() => f.Prepare())).GetAwaiter().GetResult();
            var first = f.Prepare(); var second = f.Prepare(); Reject(() => f.Ui.Send(first, first.Observation.Fingerprint, true));
            f.Ui.Cancel(); Reject(() => f.Ui.Send(second, second.Observation.Fingerprint, true));
            for (int i = 0; i < 12; i++) { _ = f.Prepare(); f.Ui.Cancel(); Check(f.Domains.RegisteredModules.Length == 1); }
            Check(f.Ui.TryClose()); Check(f.Domains.RegisteredModules.Length == 0 && f.Transport.Calls == 0); f.Unchanged();
        });
    }
}
