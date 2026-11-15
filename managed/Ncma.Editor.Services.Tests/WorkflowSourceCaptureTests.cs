using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Services;

internal static class WorkflowSourceCaptureTests
{
    private static readonly List<WorkflowSourceCaptureJob> retained = []; // failed owners never silently disposed
    private static string H => new('A', 64);
    private static void Check(bool value) { if (!value) throw new Exception("M8.6 source capture assertion"); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException) { return; } throw new Exception("Expected rejection"); }
    private static void RejectCode(Action action, string code)
    {
        try { action(); } catch (IOException e) when (e.Message == code) { return; }
        throw new Exception("Expected source capture rejection: " + code);
    }
    internal static void WriteHelpers(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        foreach (string relative in WorkflowSourceCaptureJob.RequiredHelpers())
            File.WriteAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), "# fixture\n");
    }
    private static string Fixture(string output)
    {
        string root = Path.Combine(output, "source-helpers", Guid.NewGuid().ToString("N"));
        WriteHelpers(root); return root;
    }
    private static byte[] Bundle(byte[] before, string path) => JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, kind = "ncma.extension.captured-sources.v1",
        patchFingerprint = H, originFingerprint = H, candidateOriginFingerprint = H, files = new[] { new { path, before, after = before } } });
    private static WorkflowSourceReview Review(string root, byte[] bytes) => new(Guid.NewGuid(), root, H, new(Guid.NewGuid(), 1, 0), bytes);
    private static Dictionary<string, object> Facts(WorkflowSourceReview review) => new() {
        ["kind"] = "ncma.workflow.source-created.v1", ["captureId"] = Guid.NewGuid().ToString("N"), ["workspace"] = review.Workspace,
        ["sourceInputsSha256"] = H, ["manifestSha256"] = H, ["recipeSha256"] = H, ["reviewBundleSha256"] = review.BundleSha256,
        ["sourceFileCount"] = 2, ["buildAuthorized"] = false, ["installationAuthorized"] = false };
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.6-D source creation facts bind exact reviewed copy and no execution authority", () => {
            var review = Review(Fixture(output), Bundle([1], "managed/A/A.cs")); var facts = Facts(review);
            var result = WorkflowSourceCaptureJob.ValidateFacts(JsonSerializer.Serialize(facts), review);
            Check(result.Files == 2 && result.ManifestSha256 == H);
            foreach (string key in new[] { "buildAuthorized", "installationAuthorized" }) { var bad = new Dictionary<string, object>(facts) { [key] = true }; Reject(() => WorkflowSourceCaptureJob.ValidateFacts(JsonSerializer.Serialize(bad), review)); }
            foreach (string key in new[] { "reviewBundleSha256", "captureId", "workspace", "kind" }) { var bad = new Dictionary<string, object>(facts) { [key] = "foreign" }; Reject(() => WorkflowSourceCaptureJob.ValidateFacts(JsonSerializer.Serialize(bad), review)); }
        });
        yield return ("M8.6-D source creation bounds duplicates and missing facts reject", () => {
            var review = Review(Fixture(output), Bundle([1], "managed/A/A.cs")); var facts = Facts(review);
            foreach (int count in new[] { 0, 1, 65537 }) { facts["sourceFileCount"] = count; Reject(() => WorkflowSourceCaptureJob.ValidateFacts(JsonSerializer.Serialize(facts), review)); }
            facts = Facts(review); string valid = JsonSerializer.Serialize(facts);
            Reject(() => WorkflowSourceCaptureJob.ValidateFacts(valid.Insert(1, "\"kind\":\"duplicate\","), review));
            facts.Remove("recipeSha256"); Reject(() => WorkflowSourceCaptureJob.ValidateFacts(JsonSerializer.Serialize(facts), review));
            Reject(() => WorkflowSourceCaptureJob.ValidateFacts(new string(' ', 16385), review));
        });
        yield return ("M8.6-D source owner passive construction has no process or implicit operation", () => {
            var review = Review(Fixture(output), Bundle([1], "managed/A/A.cs"));
            var capture = new WorkflowSourceCaptureJob(review, Path.Combine(output, "absent.nccapture"), Path.Combine(output, "git.exe")); retained.Add(capture);
            var before = capture.Snapshot(); Check(before.RootExited && before.ActiveProcesses == 0 && !before.Ready && before.RetainedHandles == 0 && before.WorkerId == 0);
            Reject(() => capture.RequireReady()); Check(capture.TryClose() && capture.TryClose()); Reject(capture.Begin); retained.Remove(capture);
        });
        yield return ("M8.6-D failed source begin retains partially acquired helper until explicit close", () => {
            string root = Fixture(output);
            string helper = Path.Combine(root, WorkflowSourceCaptureJob.RequiredHelpers()[0].Replace('/', Path.DirectorySeparatorChar));
            var review = Review(root, Bundle([1], "managed/A/A.cs"));
            File.WriteAllText(Path.Combine(root, WorkflowSourceCaptureJob.RequiredHelpers()[1].Replace('/', Path.DirectorySeparatorChar)), "# replaced helper\n");
            var capture = new WorkflowSourceCaptureJob(review, Path.Combine(root, "absent.nccapture"), Path.Combine(root, "git.exe")); retained.Add(capture);
            RejectCode(capture.Begin, "workflow_tool_size"); Check(capture.Snapshot().WorkerId == 0 && capture.Snapshot().Failure.Length != 0);
            Reject(() => { using var denied = new FileStream(helper, FileMode.Open, FileAccess.Write, FileShare.None); });
            Check(capture.TryClose()); retained.Remove(capture);
            using var writable = new FileStream(helper, FileMode.Open, FileAccess.Write, FileShare.None); // no byte mutation
        });
        foreach (string relative in WorkflowSourceCaptureJob.RequiredHelpers())
        foreach (bool sameSize in new[] { true, false }) {
            string helperRelative = relative; bool preserveSize = sameSize;
            yield return ($"M8.6-D source capture rejects reviewed helper {helperRelative} {(preserveSize ? "hash" : "size")} change before startup", () => {
                string root = Fixture(output), helper = Path.Combine(root, helperRelative.Replace('/', Path.DirectorySeparatorChar));
                byte[] bundle = Bundle([1], "managed/A/A.cs"); var review = Review(root, bundle);
                byte[] changed = File.ReadAllBytes(helper);
                if (preserveSize) changed[^2] ^= 1; else changed = [.. changed, (byte)' '];
                File.WriteAllBytes(helper, changed);
                var replacement = new WorkflowSourceReview(review.WorkflowId, root, review.PlanHash, review.Source, bundle);
                Check(replacement.BundleSha256 == review.BundleSha256 && replacement.Fingerprint != review.Fingerprint);
                var capture = new WorkflowSourceCaptureJob(review, Path.Combine(root, "absent.nccapture"), Path.Combine(root, "git.exe")); retained.Add(capture);
                string code = preserveSize ? "workflow_tool_bytes_changed" : "workflow_tool_size";
                RejectCode(capture.Begin, code);
                var status = capture.Snapshot();
                Check(!status.Ready && status.WorkerId == 0 && status.RootExited && status.ActiveProcesses == 0 && status.Failure == code);
                Check(!Directory.Exists(Path.Combine(root, "out"))); // rejected before scratch, pipe or process creation
                RejectCode(capture.Begin, "workflow_source_single_use");
                Check(capture.TryClose()); retained.Remove(capture);
                foreach (var file in review.CopyHelpers()) {
                    using var writable = new FileStream(Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Write, FileShare.None);
                }
            });
        }
        yield return ("M8.6-D unchanged reviewed helpers pass validation and remain pinned until explicit close", () => {
            string root = Fixture(output); var review = Review(root, Bundle([1], "managed/A/A.cs"));
            var helpers = review.CopyHelpers();
            Check(helpers.Select(h => h.Path).SequenceEqual(WorkflowSourceCaptureJob.RequiredHelpers()));
            helpers[0] = helpers[0] with { Sha256 = H }; // a returned array cannot alter the review
            var capture = new WorkflowSourceCaptureJob(review, Path.Combine(root, "absent.nccapture"), Path.Combine(root, "git.exe")); retained.Add(capture);
            RejectCode(capture.Begin, "workflow_file_identity"); // reached the absent bundle AFTER all helper comparisons
            Check(capture.Snapshot().WorkerId == 0);
            foreach (var helper in review.CopyHelpers())
                Reject(() => { using var denied = new FileStream(Path.Combine(root, helper.Path.Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Write, FileShare.None); });
            Check(capture.TryClose()); retained.Remove(capture);
            foreach (var helper in review.CopyHelpers()) {
                using var writable = new FileStream(Path.Combine(root, helper.Path.Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Write, FileShare.None);
            }
        });
    }
    internal static IEnumerable<(string, Action)> HostCases(string workspace, string output, string git)
    {
        yield return ("M8.6-D actual original capture creation holds copied manifest through worker drain", () => {
            if (string.IsNullOrWhiteSpace(git)) throw new ArgumentException("Explicit installed Git executable required; no skip or PATH fallback");
            const string relative = "managed/Ncma.Editor.Services/WorkflowSourceReview.cs";
            byte[] original = File.ReadAllBytes(Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar)));
            byte[] bundle = Bundle(original, relative); var review = Review(workspace, bundle);
            string root = Path.Combine(output, "source-create-host", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string path = Path.Combine(root, "review.nccapture"); File.WriteAllBytes(path, bundle);
            var capture = new WorkflowSourceCaptureJob(review, path, git); retained.Add(capture); capture.Begin();
            static void Wait(Func<bool> predicate, int seconds) { var clock = Stopwatch.StartNew(); while (!predicate()) { if (clock.Elapsed.TotalSeconds > seconds) throw new TimeoutException("source_capture_owner_retained"); Thread.Sleep(10); } }
            Wait(() => capture.Snapshot().Ready || capture.Snapshot().Failure.Length != 0, 125);
            var created = capture.RequireReady(); var status = capture.Snapshot(); Check(status.Ready && status.ActiveProcesses == 1 && status.RetainedHandles >= created.Files + 3);
            string capturedRoot = Path.Combine(workspace, "out", "captured-build", created.CaptureId.ToString("N"));
            string manifest = Path.Combine(capturedRoot, "capture.json"); Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifest))) == created.ManifestSha256);
            Check(File.ReadAllBytes(Path.Combine(capturedRoot, "source", relative.Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(original));
            Reject(() => { using var denied = new FileStream(manifest, FileMode.Open, FileAccess.Write, FileShare.None); });
            capture.RequestClose(); Wait(() => capture.Snapshot().RootExited && capture.Snapshot().ActiveProcesses == 0, 30);
            Check(capture.Snapshot().RetainedHandles > 0); Reject(() => { using var denied = new FileStream(manifest, FileMode.Open, FileAccess.Write, FileShare.None); });
            Wait(capture.TryClose, 5); Check(capture.Snapshot().ExitCode == 0); retained.Remove(capture);
            Check(File.ReadAllBytes(Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(original));
            using var writable = new FileStream(manifest, FileMode.Open, FileAccess.Write, FileShare.None); // no write/deletion of evidence
        });
    }
}
