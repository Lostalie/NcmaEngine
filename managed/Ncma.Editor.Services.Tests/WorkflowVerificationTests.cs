using Ncma.Editor.Core;
using Ncma.Editor.Services;

internal static class WorkflowVerificationTests
{
    private static string Hash(char c = 'A') => new(c, 64);
    private static WorkflowEvidenceFile File(string path = "input.dll", char hash = 'A', long bytes = 12) => new(path, bytes, Hash(hash));
    private static WorkflowVerificationBinding Binding(string recipe = "workflow-contracts-v1") => new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Hash(),
        new(Guid.NewGuid(), 1, 2), Guid.NewGuid(), Hash('B'), Hash('C'), Hash('D'), null, recipe, "Debug", "default", "dotnet-sdk-9.0.315", Hash('E'));
    private static WorkflowVerificationTask Task(WorkflowVerificationBinding? binding = null, WorkflowEvidenceFile[]? inputs = null, string[]? inventory = null)
    {
        binding ??= Binding();
        return new(binding, inputs ?? [File()], inventory ?? (binding.RecipeId == "editor-services-build-v1" ? [] : ["case-a", "case-b"]));
    }
    private static WorkflowCaseEvidence[] CasesFor(WorkflowVerificationTask task, WorkflowCaseOutcome outcome = WorkflowCaseOutcome.Passed)
        => task.CopyTestInventory().Select(id => new WorkflowCaseEvidence(id, outcome, outcome == WorkflowCaseOutcome.Passed ? "" : "fixture_reason")).ToArray();
    private static WorkflowVerificationReceipt Receipt(WorkflowVerificationTask task, WorkflowProcessOutcome outcome = WorkflowProcessOutcome.Exited,
        int? exit = 0, WorkflowCaseEvidence[]? cases = null, WorkflowEvidenceFile[]? inputs = null, WorkflowEvidenceFile[]? outputs = null,
        WorkflowEvidenceFile[]? logs = null, int warnings = 0, int errors = 0)
        => new(task, task.Fingerprint, outcome, exit, warnings, errors, inputs ?? task.CopyInputs(),
            outputs ?? task.Recipe.RequiredOutputs.Select(p => File(p)).ToArray(), logs ?? [File("stdout.log")], cases ?? CasesFor(task));
    private static void Check(bool value) { if (!value) throw new Exception("M8.6-B evidence assertion"); }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (EditRejectedException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 0), () => {
            var task = Task(Binding("editor-services-restore-offline-v1"), inventory: []);
            Check(task.Recipe.Kind == WorkflowVerificationKind.OfflineRestore && task.CopyTestInventory().Length == 0);
            Check(Receipt(task).Conclusion == WorkflowVerificationConclusion.RestorePrepared);
            Check(Receipt(task, outputs: []).Conclusion == WorkflowVerificationConclusion.Incomplete);
            var build = Task(task.Binding with { RecipeId = "editor-services-build-v1" }, inventory: []);
            Check(task.Fingerprint != build.Fingerprint);
            Reject(() => Receipt(task).RequireTask(build), "workflow_evidence_task_mismatch");
            Reject(() => Task(task.Binding, inventory: ["case"]), "workflow_evidence_scope");
        });
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 1), () => {
            var compile = Task(Binding("editor-services-build-v1")); var compiled = Receipt(compile);
            Check(compiled.Conclusion == WorkflowVerificationConclusion.CompileSucceeded && !compiled.InstallationAuthorized);
            var test = Task(); var passed = Receipt(test);
            Check(passed.Conclusion == WorkflowVerificationConclusion.TestsPassed && !passed.InstallationAuthorized);
            Reject(() => Task(Binding("powershell-custom")), "workflow_recipe_unknown");
            Reject(() => Task(compile.Binding, inventory: ["fake-test"]), "workflow_evidence_scope");
            Reject(() => Task(test.Binding, inventory: []), "workflow_evidence_scope");
            Reject(() => compiled.RequireTask(test), "workflow_evidence_task_mismatch");
            Check(Task(Binding("workflow-host-v1")).Recipe.TestSwitch == "--workflow-evidence");
            Check(Receipt(test, outputs: []).Conclusion == WorkflowVerificationConclusion.Incomplete);
        });
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 2), () => {
            var task = Task(); var b = task.Binding; var receipt = Receipt(task);
            foreach (var changed in new[] { b with { JobId = Guid.NewGuid() }, b with { WorkspaceSession = Guid.NewGuid() },
                b with { WorkflowId = Guid.NewGuid() }, b with { PlanHash = Hash('F') }, b with { Source = b.Source with { Revision = 3 } },
                b with { CaptureId = Guid.NewGuid() }, b with { SourceInputsSha256 = Hash('F') }, b with { CaptureManifestSha256 = Hash('F') },
                b with { CaptureRecipeSha256 = Hash('F') }, b with { ReviewBundleSha256 = Hash('F') }, b with { RecipeId = "workflow-host-v1" },
                b with { Configuration = "Release" }, b with { Variant = "ai" }, b with { ToolVersion = "different-version" }, b with { ToolSha256 = Hash('F') },
                b with { Assets = new(Guid.NewGuid(), 1, 0) }, b with { Assets = new(Guid.Empty, 0, 0) } })
                Reject(() => receipt.RequireTask(Task(changed)), "workflow_evidence_task_mismatch");
            Reject(() => Task(b with { Version = 2 }), "workflow_evidence_identity");
            Reject(() => Task(b with { Configuration = "debug" }), "workflow_evidence_configuration");
            Reject(() => Task(b with { Variant = "unknown" }), "workflow_evidence_configuration");
            Reject(() => Task(b with { SourceInputsSha256 = "" }), "workflow_evidence_hash");
            Reject(() => Task(b with { ToolVersion = "bad\uD800" }), "workflow_evidence_text");
            Reject(() => Task(b with { Assets = new(Guid.Empty, 1, 0) }), "workflow_evidence_asset_source");
            Reject(() => Task(b with { Assets = new(Guid.NewGuid(), 0, 1) }), "workflow_evidence_asset_source");
            var domain = Task(b with { Assets = new(Guid.NewGuid(), 1, 4) });
            Reject(() => Receipt(domain).RequireTask(Task(domain.Binding with { Assets = domain.Binding.Assets!.Value with { Revision = 5 } })), "workflow_evidence_task_mismatch");
        });
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 3), () => {
            var task = Task(); var rows = CasesFor(task);
            Reject(() => Receipt(task, cases: []), "workflow_evidence_inventory");
            Reject(() => Receipt(task, cases: [rows[0]]), "workflow_evidence_inventory");
            Reject(() => Receipt(task, cases: [rows[0], rows[0]]), "workflow_evidence_case_duplicate");
            Reject(() => Receipt(task, cases: [..rows, new("extra", WorkflowCaseOutcome.Passed, "")]), "workflow_evidence_inventory");
            Reject(() => Receipt(task, cases: [rows[0] with { Outcome = (WorkflowCaseOutcome)99 }, rows[1]]), "workflow_evidence_case");
            Check(Receipt(task, cases: rows.Reverse().ToArray()).Conclusion == WorkflowVerificationConclusion.TestsPassed);
        });
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 4), () => {
            var task = Task();
            foreach (var state in new[] { WorkflowCaseOutcome.NotRun, WorkflowCaseOutcome.Skipped, WorkflowCaseOutcome.EnvironmentBlocked, WorkflowCaseOutcome.Cancelled }) {
                var rows = CasesFor(task); rows[1] = rows[1] with { Outcome = state, DiagnosticCode = "not_completed" };
                Check(Receipt(task, cases: rows).Conclusion == WorkflowVerificationConclusion.Incomplete);
            }
            Check(Receipt(task, cases: CasesFor(task, WorkflowCaseOutcome.Failed)).Conclusion == WorkflowVerificationConclusion.Failed);
            Check(Receipt(task, exit: 1).Conclusion == WorkflowVerificationConclusion.Failed);
            Check(Receipt(task, warnings: 1).Conclusion == WorkflowVerificationConclusion.Incomplete);
            Check(Receipt(task, errors: 1).Conclusion == WorkflowVerificationConclusion.Failed);
            Check(Receipt(task, WorkflowProcessOutcome.NotStarted, null, CasesFor(task, WorkflowCaseOutcome.NotRun), inputs: [], outputs: [], logs: []).Conclusion == WorkflowVerificationConclusion.NotRun);
            Check(Receipt(task, WorkflowProcessOutcome.EnvironmentBlocked, null, CasesFor(task, WorkflowCaseOutcome.EnvironmentBlocked), inputs: [], outputs: [], logs: []).Conclusion == WorkflowVerificationConclusion.EnvironmentBlocked);
            Check(Receipt(task, WorkflowProcessOutcome.Cancelled, null).Conclusion == WorkflowVerificationConclusion.Cancelled);
            Check(Receipt(task, WorkflowProcessOutcome.TimedOut, null).Conclusion == WorkflowVerificationConclusion.TimedOut);
            Reject(() => Receipt(task, WorkflowProcessOutcome.NotStarted, null), "workflow_evidence_unexecuted_results");
            Reject(() => Receipt(task, WorkflowProcessOutcome.EnvironmentBlocked, 0), "workflow_evidence_process");
            Reject(() => Receipt(task, exit: null), "workflow_evidence_process");
            Reject(() => Receipt(task, (WorkflowProcessOutcome)99), "workflow_evidence_process");
        });
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 5), () => {
            var task = Task();
            foreach (var inputs in new[] { Array.Empty<WorkflowEvidenceFile>(), new[] { File(hash: 'F') }, new[] { File(bytes: 13) }, new[] { File(), File("extra.dll") } })
                Check(Receipt(task, inputs: inputs).Conclusion == WorkflowVerificationConclusion.InputsChanged);
            var compile = Task(Binding("editor-services-build-v1"));
            Check(Receipt(compile, outputs: []).Conclusion == WorkflowVerificationConclusion.Incomplete);
            Check(Receipt(compile, outputs: compile.Recipe.RequiredOutputs.Select(p => File(p, bytes: 0)).ToArray()).Conclusion == WorkflowVerificationConclusion.Incomplete);
            Reject(() => Receipt(task, logs: []), "workflow_evidence_logs_missing");
            Reject(() => Receipt(task, warnings: -1), "workflow_evidence_process");
        });
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 6), () => {
            foreach (string path in new[] { "../x.dll", "C:/x.dll", "/x.dll", "x\\y.dll", "x:stream", "x//y", "x/../y", "CON.txt", "a/LPT1", "x.", "x/y " })
                Reject(() => Task(inputs: [File(path)]), "workflow_evidence_path");
            Reject(() => Task(inputs: [File("a.dll"), File("A.dll")]), "workflow_evidence_file");
            Reject(() => Task(inputs: [File("dir/child"), File("dir")]), "workflow_evidence_path_collision");
            Reject(() => Task(inputs: [File(bytes: -1)]), "workflow_evidence_file");
            Reject(() => Task(inputs: [File() with { Sha256 = "invalid" }]), "workflow_evidence_hash");
            Reject(() => Task(inventory: ["same", "SAME"]), "workflow_evidence_case_duplicate");
        });
        yield return (WorkflowTestInventory.Name("workflow-contracts-v1", 7), () => {
            var b = Binding(); var input = new[] { File() }; var inventory = new[] { "case-a", "case-b" }; var task = Task(b, input, inventory);
            input[0] = File(hash: 'F'); inventory[0] = "changed";
            Check(task.CopyInputs()[0].Sha256 == Hash() && task.CopyTestInventory()[0] == "case-a");
            var rows = CasesFor(task); var receipt = Receipt(task, cases: rows); string fingerprint = receipt.Fingerprint;
            rows[0] = rows[0] with { Outcome = WorkflowCaseOutcome.Failed };
            var copy = receipt.CopyCases(); copy[0] = copy[0] with { DiagnosticCode = "forged" };
            var files = receipt.CopyObservedInputs(); files[0] = File(hash: 'F');
            Check(receipt.Fingerprint == fingerprint && receipt.CopyCases()[0].Outcome == WorkflowCaseOutcome.Passed && receipt.CopyObservedInputs()[0].Sha256 == Hash());
            Reject(() => Task(inputs: Enumerable.Range(0, 2049).Select(i => File($"f{i}.dll")).ToArray()), "workflow_evidence_file_budget");
            Reject(() => Task(inventory: Enumerable.Range(0, 4097).Select(i => $"case-{i}").ToArray()), "workflow_evidence_case_budget");
            Check(Task(b, [File("b.dll"), File("a.dll")], ["b", "a"]).Fingerprint == Task(b, [File("a.dll"), File("b.dll")], ["a", "b"]).Fingerprint);
        });
    }
}
