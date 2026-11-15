using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Editor.App;
#if NCMA_EXTENSIONS
using Ncma.Extensions.Hosting;
#endif

internal static partial class WorkflowTests
{
    private static void HandoffReject(Action action, string code)
    {
        try { action(); }
        catch (EditRejectedException e) when (e.Code == code) { return; }
        catch (IOException e) when (e.Message == code) { return; }
        throw new Exception("Expected handoff rejection: " + code);
    }
    private static byte[] HandoffBundle() => JsonSerializer.SerializeToUtf8Bytes(new {
        schemaVersion = 1, kind = "ncma.extension.captured-sources.v1", patchFingerprint = new string('A', 64),
        originFingerprint = new string('B', 64), candidateOriginFingerprint = new string('C', 64),
        files = new[] { new { path = "managed/Example/A.cs", before = (byte[]?)null, after = Encoding.UTF8.GetBytes("// retained fixture") } }
    });
    internal static IEnumerable<(string, Action)> SourceHandoffCases(string output, string repository)
    {
        yield return ("M8.6-D review handoff binds bytes owner plan and revision without executable authority", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")]);
            var host = f.Owner.WorkflowJobs!; var bytes = HandoffBundle(); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var review = host.BindReviewedSource(id, repository, bytes); bytes[0] = 0;
            Check(review.BundleSha256 == hash && review.WorkflowId == id && host.CanPrepareSourceReview(review));
            Check(review.Source == f.Workspace.Stamp && host.Capture().Length == 0);
            var foreign = new WorkflowSourceReview(id, repository, review.PlanHash, review.Source, HandoffBundle());
            Check(foreign.Fingerprint == review.Fingerprint && !host.IsSourceReviewCurrent(foreign));
            HandoffReject(() => host.RevokeSourceReview(foreign), "workflow_source_foreign_handoff");
            HandoffReject(() => host.PrepareReviewedOffline(foreign, Guid.NewGuid(), "invalid", "Debug", "extensions"), "workflow_source_handoff_stale");
            HandoffReject(() => host.PrepareReviewedOffline(review, Guid.NewGuid(), "invalid", "Debug", "default"), "workflow_source_handoff_stale");
            Check(host.CanPrepareSourceReview(review)); host.RevokeSourceReview(review); Check(!host.IsSourceReviewCurrent(review));
            var next = host.BindReviewedSource(id, repository, HandoffBundle());
            f.Workspace.CreateObject(f.Workspace.Stamp); Check(!host.IsSourceReviewCurrent(next));
            HandoffReject(() => host.PrepareReviewedOffline(next, Guid.NewGuid(), "invalid", "Debug", "extensions"), "workflow_source_handoff_stale");
            Check(host.Capture().Length == 0);
        });
        yield return ("M8.6-D patched capture cannot enter normal preparation or mismatched reviewed handoff", () => {
            string hash = Convert.ToHexString(SHA256.HashData(HandoffBundle()));
            JsonDocument Manifest(string? review) => JsonDocument.Parse(JsonSerializer.Serialize(new { kind = "ncma.captured-build.v1", schemaVersion = 1, reviewBundleSha256 = review }));
            using var patched = Manifest(hash); using var normal = Manifest(null);
            WorkflowLocalInputs.RequireReviewedCapture(normal.RootElement, null);
            WorkflowLocalInputs.RequireReviewedCapture(patched.RootElement, hash);
            HandoffReject(() => WorkflowLocalInputs.RequireReviewedCapture(patched.RootElement, null), "workflow_capture_review_mismatch");
            HandoffReject(() => WorkflowLocalInputs.RequireReviewedCapture(normal.RootElement, hash), "workflow_capture_review_mismatch");
            HandoffReject(() => WorkflowLocalInputs.RequireReviewedCapture(patched.RootElement, new string('F', 64)), "workflow_capture_review_mismatch");
            using var duplicate = JsonDocument.Parse("{\"kind\":\"ncma.captured-build.v1\",\"schemaVersion\":1,\"reviewBundleSha256\":null,\"reviewBundleSha256\":null}");
            HandoffReject(() => WorkflowLocalInputs.RequireReviewedCapture(duplicate.RootElement, null), "workflow_capture_duplicate_field");
        });
        yield return ("M8.6-D failed preparation consumes handoff and cannot borrow old capture authority", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")]); var host = f.Owner.WorkflowJobs!;
            var review = host.BindReviewedSource(id, repository, HandoffBundle());
            byte[] scene = f.Owner.Document.CaptureBytes(); var state = f.Owner.Edit!.State;
            // Deliberately invalid cache path fails before any process can start.
            try { host.PrepareReviewedOffline(review, Guid.NewGuid(), "invalid", "Debug", "extensions"); throw new Exception("Expected invalid local path"); }
            catch (IOException) { }
            Check(review.Consumed && !host.CanPrepareSourceReview(review));
            HandoffReject(() => host.PrepareReviewedOffline(review, Guid.NewGuid(), "invalid", "Debug", "extensions"), "workflow_source_handoff_stale");
            Check(host.Capture().Length == 0 && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && state == f.Owner.Edit.State);
            // Synthetic lineage proves revocation cannot be revived by an equal
            // but unconsumed handoff. NOT a real preparer or SDK run.
            review.CaptureId = Guid.NewGuid(); var binding = ContinuationBinding(f, id) with { CaptureId = review.CaptureId, ReviewBundleSha256 = review.BundleSha256 };
            Check(host.IsSourceCurrent(binding)); host.RevokeSourceReview(review); Check(!host.IsSourceCurrent(binding));
            var equal = host.BindReviewedSource(id, repository, HandoffBundle()); Check(equal.Fingerprint == review.Fingerprint);
            HandoffReject(() => host.PrepareReviewedOffline(equal, review.CaptureId, "invalid", "Debug", "extensions"), "workflow_source_handoff_stale");
            Check(!host.IsSourceCurrent(binding));
        });
#if NCMA_EXTENSIONS
        yield return ("M8.6-D optional actual source presenter transfers reviewed bytes and exact plan without launching", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")]);
            using var extensions = new EditorTrustedExtensions(f.Workspace); extensions.Start(); f.View.AttachTrustedExtensions(extensions);
            Click(f, 40, 8); Click(f, 24, 12); Click(f, 24, 70); Click(f, 61, 1000); Click(f, 61, 9);
            Click(f, 24, 12); Click(f, 24, 76);
            string root = Path.Combine(output, "handoff-ui", Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "source"), candidate = Path.Combine(root, "candidate");
            Directory.CreateDirectory(source); Directory.CreateDirectory(candidate);
            WorkflowSourceCaptureTests.WriteHelpers(source);
            File.WriteAllText(Path.Combine(source, "A.cs"), "// before"); File.WriteAllText(Path.Combine(candidate, "A.cs"), "// after");
            void Text(ulong low, string text) { var bytes = Encoding.UTF8.GetBytes(text); var e = f.Event(84, low); e.TextLength = (uint)bytes.Length; f.View.Apply([e], bytes); f.Build(); }
            bool Enabled(ulong low) => f.View.Items.ToArray().Single(i => i.WidgetHigh == 84 && i.WidgetLow == low).Enabled != 0;
            Text(20, source); Text(21, candidate); Text(22, "A.cs"); Click(f, 84, 23); Check(!Enabled(32));
            int pages = 0; do { Check(++pages < 128); Click(f, 84, 26, 1); if (!Enabled(25)) break; Click(f, 84, 25); } while (true);
            Click(f, 84, 27, 1); Click(f, 84, 28); Check(!Enabled(32));
            string bundle = Path.Combine(root, "review.nccapture"); Text(30, bundle); Click(f, 84, 31); Check(Enabled(32));
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bundle)));
            File.WriteAllText(Path.Combine(candidate, "A.cs"), "// later author edit must not replace review");
            var old = f.Event(84, 32); f.View.Apply([old], []); f.Build();
            string labels = Encoding.UTF8.GetString(f.View.Text); Check(labels.Contains(hash, StringComparison.Ordinal) && labels.Contains(id.ToString("D"), StringComparison.Ordinal));
            Check(f.Owner.WorkflowJobs!.Capture().Length == 0 && f.Owner.Edit!.State.UndoCount == 0);
            f.View.Apply([old], []); f.Build(); Click(f, 62, 21);
            Check(!Encoding.UTF8.GetString(f.View.Text).Contains("已审阅源码交接（不是执行批准）", StringComparison.Ordinal));
        });
#endif
    }
}
