using System.Text.Json;
using Ncma.Editor.Services;

// Separate opt-in fixed entry. Original CaseHarness still executes every
// selected case and reports all failures; no first-failure short circuit.
internal static class WorkflowEvidenceHarness
{
    internal static int Run(string requestPath)
    {
        requestPath = Path.GetFullPath(requestPath);
        if (Path.GetFileName(requestPath) != "test-request.json") throw new IOException("workflow_test_request_path");
        using var pins = new WorkflowPhysicalPins();
        var hash = pins.Observe(requestPath, WorkflowTestRequestCodec.MaximumBytes);
        var request = WorkflowTestRequestCodec.Read(pins.ReadFile(requestPath, hash.Sha256, hash.Bytes, WorkflowTestRequestCodec.MaximumBytes));
        string root = Path.GetDirectoryName(requestPath)!;
        using var journal = new FileStream(Path.Combine(root, "test-results.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        WorkflowPhysicalPins.ValidateHandle(journal.SafeFileHandle, false); long written = 0;
        void Write(object row) {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(row);
            if (bytes.Length + 1 > WorkflowTestJournal.MaximumBytes - written) throw new IOException("workflow_test_journal_budget");
            journal.Write(bytes); journal.WriteByte((byte)'\n'); journal.Flush(true); written += bytes.Length + 1;
        }
        var source = request.RecipeId switch {
            "workflow-contracts-v1" => WorkflowVerificationTests.Cases(),
            "workflow-host-v1" => WorkflowTests.Cases(Path.Combine(root, "cases"), Path.Combine(root, "inputs"), Path.Combine(root, "inputs", "out", "managed")),
            _ => throw new IOException("workflow_test_recipe_unknown") };
        return Execute(request, source, Write);
    }
    internal static int Execute(WorkflowTestRequest request, IEnumerable<(string, Action)> source, Action<object> write)
    {
        write(new { kind = "header", version = 1, taskFingerprint = request.TaskFingerprint, recipeId = request.RecipeId, inventorySha256 = request.InventorySha256 });
        var cases = new List<(string, Action)>();
        foreach (var item in source) {
            if (cases.Count == request.Inventory.Length) throw new IOException("workflow_test_actual_inventory_mismatch"); cases.Add(item);
        }
        // Unknown/missing/duplicate registrations do not execute unapproved cases.
        // Header remains and host reports ALL approved cases as unexecuted.
        if (cases.Count != request.Inventory.Length || !WorkflowEvidenceRules.Inventory(cases.Select(c => c.Item1)).SequenceEqual(request.Inventory, StringComparer.Ordinal))
            throw new IOException("workflow_test_actual_inventory_mismatch");
        var harness = new CaseHarness("M8.6 fixed " + request.RecipeId);
        foreach (var (name, action) in cases) {
            write(new { kind = "begin", id = name }); int before = harness.Passed;
            harness.Case(name, action);
            bool passed = harness.Passed == before + 1;
            write(new { kind = "case", id = name, passed, exceptionType = passed ? "" : harness.LastFailureType });
        }
        int exit = harness.Finish();
        write(new { kind = "complete", total = harness.Total, passed = harness.Passed, failed = harness.Failed });
        return exit;
    }
}
