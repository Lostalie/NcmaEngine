using System.Text;
using System.Text.Json;
using Ncma.Editor.Services;

internal static class WorkflowObservationTests
{
    private static readonly string H = new('A', 64);
    private static void Check(bool value) { if (!value) throw new Exception("M8.6-C3 observed evidence assertion"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or JsonException) { return; }
        throw new Exception("Expected evidence rejection");
    }
    private static WorkflowVerificationTask Task(string recipe = "workflow-contracts-v1")
    {
        string prefix = "compiled/" + Guid.NewGuid().ToString("N") + "/runtime/";
        return new(new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H, new(Guid.NewGuid(), 1, 1), Guid.NewGuid(), H, H, H, null,
            recipe, "Debug", "default", "9.0.315", H),
            new[] { "Ncma.Editor.Services.Tests.dll", "Ncma.Editor.Services.Tests.deps.json", "Ncma.Editor.Services.Tests.runtimeconfig.json" }.Select(p => new WorkflowEvidenceFile(prefix + p, 1, H)),
            recipe == "editor-services-build-v1" ? [] : WorkflowTestInventory.Copy(recipe));
    }
    private static byte[] Encode(IEnumerable<object> rows) => Encoding.UTF8.GetBytes(string.Concat(rows.Select(r => JsonSerializer.Serialize(r) + "\n")));
    private static object Header(WorkflowVerificationTask task) => new { kind = "header", version = 1, taskFingerprint = task.Fingerprint, recipeId = task.Recipe.Id, inventorySha256 = task.InventorySha256 };
    private static object[] Journal(WorkflowVerificationTask task, bool failure = false)
    {
        var rows = new List<object> { Header(task) }; int index = 0;
        foreach (string id in task.CopyTestInventory()) {
            rows.Add(new { kind = "begin", id }); bool passed = !failure || index++ != 0;
            rows.Add(new { kind = "case", id, passed, exceptionType = passed ? "" : "InvalidOperationException" });
        }
        int count = task.CopyTestInventory().Length;
        rows.Add(new { kind = "complete", total = count, passed = failure ? count - 1 : count, failed = failure ? 1 : 0 }); return rows.ToArray();
    }
    private static WorkflowVerificationReceipt Receipt(WorkflowVerificationTask task, WorkflowTestJournalResult report, int exit = 0)
        => new(task, task.Fingerprint, WorkflowProcessOutcome.Exited, exit, 0, report.Cases.Count(c => c.Outcome == WorkflowCaseOutcome.Failed),
            task.CopyInputs(), [new("test-results.jsonl", 1, H)], [new("stdout.log", 0, H)], report.Cases, report.Complete, report.Diagnostic);
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.6-C3 fixed catalog is shared with all actual registrations and returned detached", () => {
            Check(WorkflowTestInventory.Copy("workflow-contracts-v1").SequenceEqual(WorkflowVerificationTests.Cases().Select(c => c.Item1)));
            Check(WorkflowTestInventory.Copy("workflow-host-v1").SequenceEqual(WorkflowTests.Cases("unused", "unused", "unused").Select(c => c.Item1)));
            var detached = WorkflowTestInventory.Copy("workflow-contracts-v1"); detached[0] = "foreign";
            Check(WorkflowTestInventory.Copy("workflow-contracts-v1")[0] != "foreign");
            var task = Task(); var request = WorkflowTestRequestCodec.Read(WorkflowTestRequestCodec.Encode(task));
            Check(request.TaskFingerprint == task.Fingerprint && request.Inventory.SequenceEqual(task.CopyTestInventory()));
            Reject(() => WorkflowTestRequestCodec.Encode(new(task.Binding, task.CopyInputs(), ["unapproved-case"])));
        });
        yield return ("M8.6-C3 complete journal proves exact coverage, failed case cannot be overwritten", () => {
            var task = Task(); var good = WorkflowTestJournal.Read(task, Encode(Journal(task)));
            Check(good.Complete && good.Cases.Length == 8 && Receipt(task, good).Conclusion == WorkflowVerificationConclusion.TestsPassed);
            var failure = WorkflowTestJournal.Read(task, Encode(Journal(task, true)));
            Check(failure.Complete && failure.Cases.Count(c => c.Outcome == WorkflowCaseOutcome.Failed) == 1 && failure.Cases.Count(c => c.Outcome == WorkflowCaseOutcome.Passed) == 7);
            Check(Receipt(task, failure, 1).Conclusion == WorkflowVerificationConclusion.Failed);
        });
        yield return ("M8.6-C3 missing footer or truncated case preserves prefix but never reports a pass", () => {
            var task = Task(); var rows = Journal(task);
            var noFooter = WorkflowTestJournal.Read(task, Encode(rows[..^1]));
            Check(!noFooter.Complete && noFooter.Cases.All(c => c.Outcome == WorkflowCaseOutcome.Passed));
            Check(Receipt(task, noFooter).Conclusion == WorkflowVerificationConclusion.Incomplete);
            var prefix = WorkflowTestJournal.Read(task, Encode(rows[..4]));
            Check(!prefix.Complete && prefix.Cases.Count(c => c.Outcome == WorkflowCaseOutcome.Passed) == 1 && prefix.Cases.Any(c => c.DiagnosticCode == "case_incomplete"));
            byte[] bytes = Encode(rows); var truncated = WorkflowTestJournal.Read(task, bytes.AsSpan(0, bytes.Length - 1));
            Check(!truncated.Complete && truncated.Diagnostic == "test_report_truncated");
        });
        yield return ("M8.6-C3 wrong task duplicate unknown trailing and malformed records fail closed", () => {
            var task = Task(); var rows = Journal(task);
            foreach (byte[] data in new[] {
                Encode(rows.Prepend(Header(task))), Encode(rows.Append(new { kind = "complete", total = 8, passed = 8, failed = 0 })),
                Encode(rows.Take(1).Append(new { kind = "begin", id = "unknown" })), Encode(Journal(Task())),
                Encoding.UTF8.GetBytes("{\"kind\":\"header\",\"kind\":\"header\"}\n"), new byte[] { 0xFF, 0x0A }
            }) {
                var report = WorkflowTestJournal.Read(task, data); Check(!report.Complete && report.Diagnostic.Length != 0);
                Check(Receipt(task, report).Conclusion != WorkflowVerificationConclusion.TestsPassed);
            }
        });
        yield return ("M8.6-C3 observed harness runs every case after first failure and retains exact case rows", () => {
            var task = Task(); var request = WorkflowTestRequestCodec.Read(WorkflowTestRequestCodec.Encode(task)); int reached = 0;
            var rows = new List<object>(); var cases = request.Inventory.Select((id, index) => (id, (Action)(() => { reached++; if (index == 0) throw new InvalidOperationException("injected assertion"); }))).ToArray();
            Check(WorkflowEvidenceHarness.Execute(request, cases, rows.Add) == 1 && reached == 8);
            var result = WorkflowTestJournal.Read(task, Encode(rows)); Check(result.Complete && result.Cases.Count(c => c.Outcome == WorkflowCaseOutcome.Failed) == 1);
            reached = 0; rows.Clear(); Reject(() => WorkflowEvidenceHarness.Execute(request, cases[..^1], rows.Add));
            Check(reached == 0 && WorkflowTestJournal.Read(task, Encode(rows)).Cases.All(c => c.Outcome == WorkflowCaseOutcome.NotRun));
        });
        yield return ("M8.6-C3 missing or ambiguous compiler summary is unknown, not zero warnings", () => {
            var zero = WorkflowObservedEvidence.CompilerCounts(Encoding.UTF8.GetBytes("    0 Warning(s)\r\n    0 Error(s)\r\n"), []);
            Check(zero.Warnings == 0 && zero.Errors == 0);
            var absent = WorkflowObservedEvidence.CompilerCounts(Encoding.UTF8.GetBytes("Build succeeded.\n"), []); Check(absent.Warnings is null && absent.Errors is null);
            var duplicate = WorkflowObservedEvidence.CompilerCounts(Encoding.UTF8.GetBytes("0 Warning(s)\n0 Warning(s)\n0 Error(s)\n"), []); Check(duplicate.Warnings is null);
            var task = Task("editor-services-build-v1");
            var unknown = new WorkflowVerificationReceipt(task, task.Fingerprint, WorkflowProcessOutcome.Exited, 0, null, null, task.CopyInputs(),
                task.Recipe.RequiredOutputs.Select(p => new WorkflowEvidenceFile(p, 1, H)), [new("stdout.log", 1, H)], [], false, "compiler_summary_unobserved");
            Check(unknown.Version == 2 && unknown.Conclusion == WorkflowVerificationConclusion.Incomplete && unknown.WarningCount is null);
            var unavailable = new WorkflowVerificationReceipt(task, task.Fingerprint, WorkflowProcessOutcome.Exited, 1, null, null, [], [], [], [], false, "inputs_unobserved", false);
            Check(unavailable.Conclusion == WorkflowVerificationConclusion.Failed && !unavailable.InputsObserved);
        });
        yield return ("M8.6-C3 test vector selects only retained producer runtime and never invokes build or restore", () => {
            var task = Task(); var environment = new WorkflowBuildEnvironment(@"F:\NcmaEngineRelease\source", @"C:\sdk\dotnet.exe", @"C:\runner\powershell.exe", H,
                [new("scripts/WorkflowBuildInputs.ps1", 1, H)], TimeSpan.FromMinutes(1));
            var command = new WorkflowBuildCommand(task, environment); var args = command.CopyArguments();
            Check(args.Length == 3 && args[0] == Path.Combine(WorkflowTestLayout.RuntimeRoot(task, environment.WorkspaceRoot), "Ncma.Editor.Services.Tests.dll") &&
                args[1] == "--workflow-evidence" && args[2] == Path.Combine(command.OutputRoot, "test-request.json"));
            Check(!args.Contains("build") && !args.Contains("restore") && command.OutputRoot.EndsWith(task.Binding.JobId.ToString("N"), StringComparison.Ordinal));
            Reject(() => new WorkflowBuildCommand(new(task.Binding, [new("old/runtime.dll", 1, H)], task.CopyTestInventory()), environment));
        });
        yield return ("M8.6-C3 lost exit observation is incomplete even with all passing case rows", () => {
            var task = Task(); var report = WorkflowTestJournal.Read(task, Encode(Journal(task)));
            var status = new WorkflowBuildStatus(task.Binding.JobId, WorkflowBuildPhase.AwaitingClose, WorkflowBuildOutcome.ObservationFailed,
                true, true, null, true, false, "transport_failed", "", "");
            var actual = new WorkflowBuildObservation(true, true, null, "transport_failed");
            var outcome = WorkflowObservedEvidence.Outcome(task, status, actual); Check(outcome == WorkflowProcessOutcome.ObservationLost);
            var receipt = new WorkflowVerificationReceipt(task, task.Fingerprint, outcome, null, 0, 0, task.CopyInputs(), [], [new("stdout.log", 1, H)],
                report.Cases, false, "execution_evidence_incomplete");
            Check(receipt.Conclusion == WorkflowVerificationConclusion.Incomplete);
            foreach (var stopped in new[] { WorkflowBuildOutcome.Stale, WorkflowBuildOutcome.ObservationFailed, WorkflowBuildOutcome.LaunchFailed, WorkflowBuildOutcome.Rejected, WorkflowBuildOutcome.Expired }) {
                var zero = WorkflowObservedEvidence.Outcome(task, status with { Outcome = stopped, ExitCode = 0 }, actual with { ExitCode = 0 });
                Check(zero == WorkflowProcessOutcome.ObservationLost);
                var notPassed = new WorkflowVerificationReceipt(task, task.Fingerprint, zero, 0, 0, 0, task.CopyInputs(),
                    task.Recipe.RequiredOutputs.Select(p => new WorkflowEvidenceFile(p, 1, H)), [new("stdout.log", 1, H)], report.Cases, true, "source_or_execution_invalid");
                Check(notPassed.Conclusion == WorkflowVerificationConclusion.Incomplete);
            }
            Reject(() => WorkflowObservedEvidence.Outcome(task, status with { ExecutionDrained = false }, actual));
            Reject(() => WorkflowObservedEvidence.Outcome(task, status with { JobId = Guid.NewGuid() }, actual));
            Reject(() => WorkflowObservedEvidence.Outcome(task, status with { Phase = WorkflowBuildPhase.Running }, actual));
            var failed = WorkflowTestJournal.Read(task, Encode(Journal(task, true)));
            var failure = new WorkflowVerificationReceipt(task, task.Fingerprint, outcome, null, 0, 1, task.CopyInputs(), [], [new("stdout.log", 1, H)], failed.Cases, false, "execution_evidence_incomplete");
            Check(failure.Conclusion == WorkflowVerificationConclusion.Failed);
        });
    }
}
