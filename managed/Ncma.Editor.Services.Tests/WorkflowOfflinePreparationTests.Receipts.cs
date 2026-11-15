using System.Security.Cryptography;
using System.Text;
using Ncma.Editor.Services;

internal static partial class WorkflowOfflinePreparationTests
{
    // Real output/log/tree owners and receipt producer, synthetic SDK observation.
    // The separate SDK chain exercises the actual EditorWorkflowJobs entry points.
    private sealed class RestoreFixture : IWorkflowRestoreProducer, IDisposable
    {
        public Guid LeaseId { get; } = Guid.NewGuid();
        public WorkflowVerificationTask Task { get; } = WorkflowOfflinePreparationTests.Task();
        public WorkflowBuildEnvironment Environment { get; }
        public WorkflowBuildOutput Output { get; }
        public bool SdkStarted { get; set; } = true;
        internal bool Current = true;
        internal WorkflowBuildObservation Observation = new(false, true, null);
        internal WorkflowBuildJobs Queue { get; }
        internal WorkflowRestoreProduct Product { get; }
        internal RestoreFixture(string parent, string summary = "0 Warning(s)\n0 Error(s)\n", string? missing = null, bool start = true)
        {
            Environment = new(Root(parent), @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe", H,
                [new("scripts/WorkflowBuildInputs.ps1", 1, H)], TimeSpan.FromSeconds(90));
            Output = new(this); Product = new(this); Queue = new(_ => true);
            var review = Queue.Review(this); Queue.Approve(review, review.Fingerprint, true);
            if (!start) { Queue.Cancel(Task.Binding.JobId); return; }
            Check(Queue.StartNext()); Output.Append(false, Encoding.UTF8.GetBytes(summary));
            string root = Path.Combine(Output.Command.ArtifactsRoot, "obj", "Ncma.Editor.Services.Tests"); Directory.CreateDirectory(root);
            foreach (string name in Task.Recipe.RequiredOutputs)
                if (name != missing) File.WriteAllText(Path.Combine(root, name), "observed fixture " + name);
            // Identically named outputs from a different project cannot satisfy this recipe.
            string foreign = Path.Combine(Output.Command.ArtifactsRoot, "obj", "Other"); Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "project.assets.json"), "foreign");
            Observation = new(true, true, 0);
        }
        public void RequireCurrent() { if (!Current) throw new IOException("workflow_fixture_inputs_changed"); }
        public void Start(WorkflowBuildStartPermit permit) { Output.Reserve(permit); Observation = new(true, false, null); }
        public void RequestStop() { }
        public WorkflowBuildObservation Observe() => Observation;
        public void Close() { Product.Dispose(); Output.Close(); }
        internal WorkflowBuildStatus Status() => Queue.Poll(Task.Binding.JobId);
        public void Dispose() { Observation = Observation with { ExecutionDrained = true }; Check(Queue.Close(Task.Binding.JobId)); }
    }
    private static IEnumerable<(string, Action)> ReceiptCases(string parent)
    {
        yield return ("Offline restore producer seals exact required files and logs into RestorePrepared", () => {
            using var f = new RestoreFixture(parent); var receipt = f.Product.Prepare(f.Status());
            Check(ReferenceEquals(receipt, f.Product.Receipt) && receipt.Conclusion == WorkflowVerificationConclusion.RestorePrepared);
            Check(receipt.WarningCount == 0 && receipt.ErrorCount == 0 && receipt.EvidenceComplete && receipt.InputFilesMatch);
            Check(receipt.CopyObservedInputs().SequenceEqual(f.Task.CopyInputs()) && receipt.CopyCases().Length == 0 && !receipt.InstallationAuthorized);
            Check(receipt.CopyOutputs().Select(x => x.Path).ToHashSet().SetEquals(f.Task.Recipe.RequiredOutputs));
            foreach (var file in receipt.CopyOutputs()) {
                byte[] bytes = File.ReadAllBytes(Path.Combine(f.Output.Command.ArtifactsRoot, "obj", "Ncma.Editor.Services.Tests", file.Path));
                Check(file.Bytes == bytes.Length && file.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)));
            }
            Check(receipt.CopyLogs().Length == 2 && receipt.CopyLogs().Single(x => x.Path == "stdout.log").Sha256 ==
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("0 Warning(s)\n0 Error(s)\n"))));
            Check(f.Product.Fingerprint.Length == 64); f.Product.RequireCurrent(); Reject(() => f.Product.Prepare(f.Status()));
            string fingerprint = receipt.Fingerprint; f.Dispose(); Check(receipt.Fingerprint == fingerprint);
        });
        yield return ("Offline restore missing empty or foreign outputs never prepare a build product", () => {
            foreach (string name in Task().Recipe.RequiredOutputs) foreach (bool empty in new[] { false, true }) {
                using var f = new RestoreFixture(parent, missing: name);
                if (empty) File.WriteAllBytes(Path.Combine(f.Output.Command.ArtifactsRoot, "obj", "Ncma.Editor.Services.Tests", name), []);
                var receipt = f.Product.Prepare(f.Status());
                Check(receipt.Conclusion == WorkflowVerificationConclusion.Incomplete && receipt.DiagnosticCode == "workflow_restore_product_missing");
                Check(f.Product.Fingerprint.Length == 0); Reject(f.Product.RequireCurrent);
            }
        });
        yield return ("Offline restore requires observed unambiguous diagnostic counts", () => {
            foreach (string summary in new[] { "Restore succeeded.\n", "0 Warning(s)\n0 Warning(s)\n0 Error(s)\n", "1 Warning(s)\n0 Error(s)\n", "0 Warning(s)\n1 Error(s)\n" }) {
                using var f = new RestoreFixture(parent, summary); var receipt = f.Product.Prepare(f.Status());
                Check(receipt.Conclusion == (summary.Contains("1 Error(s)", StringComparison.Ordinal) ? WorkflowVerificationConclusion.Failed : WorkflowVerificationConclusion.Incomplete));
                Check(f.Product.Fingerprint.Length == 0); Reject(f.Product.RequireCurrent);
            }
        });
        yield return ("Offline restore failed cancelled timed-out stale and abandoned jobs retain non-success receipts", () => {
            foreach (var outcome in new[] { WorkflowBuildOutcome.ProcessFailed, WorkflowBuildOutcome.Cancelled, WorkflowBuildOutcome.TimedOut, WorkflowBuildOutcome.Stale, WorkflowBuildOutcome.ProcessExited }) {
                using var f = new RestoreFixture(parent);
                if (outcome == WorkflowBuildOutcome.ProcessFailed) f.Observation = f.Observation with { ExitCode = 1 };
                var receipt = f.Product.Prepare(f.Status() with { Outcome = outcome, Abandoned = outcome == WorkflowBuildOutcome.ProcessExited });
                var expected = outcome switch { WorkflowBuildOutcome.ProcessFailed => WorkflowVerificationConclusion.Failed,
                    WorkflowBuildOutcome.Cancelled => WorkflowVerificationConclusion.Cancelled, WorkflowBuildOutcome.TimedOut => WorkflowVerificationConclusion.TimedOut,
                    _ => WorkflowVerificationConclusion.Incomplete };
                Check(receipt.Conclusion == expected && receipt.CopyLogs().Length == 2 && f.Product.Fingerprint.Length == 0); Reject(f.Product.RequireCurrent);
            }
        });
        yield return ("Offline restore unavailable inputs or SDK observation cannot authorize compilation", () => {
            for (int mode = 0; mode < 3; mode++) {
                using var f = new RestoreFixture(parent);
                if (mode == 0) f.Current = false;
                if (mode == 1) f.SdkStarted = false;
                if (mode == 2) f.Observation = f.Observation with { FailureCode = "lost" };
                var receipt = f.Product.Prepare(f.Status());
                Check(receipt.Conclusion == WorkflowVerificationConclusion.Incomplete && !receipt.EvidenceComplete && receipt.InputsObserved == (mode != 0));
                Check(f.Product.Fingerprint.Length == 0); Reject(f.Product.RequireCurrent);
            }
        });
        yield return ("Offline restore rejects premature sealing without consuming its single attempt", () => {
            using var f = new RestoreFixture(parent); var status = f.Status();
            Reject(() => f.Product.Prepare(status with { ExecutionDrained = false }));
            Reject(() => f.Product.Prepare(status with { JobId = Guid.NewGuid() }));
            Check(f.Product.Receipt is null && f.Product.Prepare(status).Conclusion == WorkflowVerificationConclusion.RestorePrepared);
            using var cancelled = new RestoreFixture(parent, start: false);
            var receipt = cancelled.Product.Prepare(cancelled.Status());
            Check(receipt.Conclusion == WorkflowVerificationConclusion.Cancelled && receipt.CopyOutputs().Length == 0 && !receipt.InputsObserved);
        });
    }
}
