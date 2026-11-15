using System.Text;
using System.Text.Json;

namespace Ncma.Editor.Services;

internal sealed record WorkflowTestJournalResult(bool Complete, string Diagnostic, WorkflowCaseEvidence[] Cases);

// Strict post-drain parsing of the child's append-only, bounded UTF-8 JSONL.
// A valid prefix is retained after truncation, but cannot produce TestsPassed.
// This is observation data, not a signature or authority from untrusted code.
internal static class WorkflowTestJournal
{
    internal const int MaximumBytes = 1024 * 1024;
    internal static WorkflowTestJournalResult Read(WorkflowVerificationTask task, ReadOnlySpan<byte> bytes)
    {
        if (task.Recipe.Kind != WorkflowVerificationKind.Tests || bytes.Length > MaximumBytes) throw new IOException("workflow_test_journal_scope");
        var inventory = task.CopyTestInventory();
        var rows = inventory.ToDictionary(id => id, id => new WorkflowCaseEvidence(id, WorkflowCaseOutcome.NotRun, "case_not_started"), StringComparer.Ordinal);
        string diagnostic = "test_report_incomplete"; string? active = null;
        var started = new HashSet<string>(StringComparer.Ordinal); bool header = false, complete = false;
        int offset = 0, records = 0;
        while (offset < bytes.Length) {
            int newline = bytes[offset..].IndexOf((byte)'\n');
            if (newline < 0) { diagnostic = "test_report_truncated"; break; }
            var line = bytes.Slice(offset, newline); offset += newline + 1;
            if (line.Length is < 2 or > 16384 || ++records > 2 * inventory.Length + 2 || complete) { diagnostic = "test_report_shape"; complete = false; break; }
            try {
                // Throw on malformed UTF-8, not replacement characters in case IDs.
                string text = new UTF8Encoding(false, true).GetString(line);
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 }); var value = document.RootElement;
                string kind = value.GetProperty("kind").GetString() ?? "";
                if (!header) {
                    WorkflowCaptureBridge.Shape(value, "kind", "version", "taskFingerprint", "recipeId", "inventorySha256");
                    if (kind != "header" || value.GetProperty("version").GetInt32() != 1 ||
                        value.GetProperty("taskFingerprint").GetString() != task.Fingerprint || value.GetProperty("recipeId").GetString() != task.Recipe.Id ||
                        value.GetProperty("inventorySha256").GetString() != task.InventorySha256) throw new IOException("workflow_test_journal_binding");
                    header = true;
                } else if (kind == "begin") {
                    WorkflowCaptureBridge.Shape(value, "kind", "id"); string id = value.GetProperty("id").GetString()!;
                    if (active is not null || !rows.ContainsKey(id) || !started.Add(id)) throw new IOException("workflow_test_journal_case");
                    active = id; rows[id] = new(id, WorkflowCaseOutcome.NotRun, "case_incomplete");
                } else if (kind == "case") {
                    WorkflowCaptureBridge.Shape(value, "kind", "id", "passed", "exceptionType"); string id = value.GetProperty("id").GetString()!;
                    string type = value.GetProperty("exceptionType").GetString()!; bool passed = value.GetProperty("passed").GetBoolean();
                    if (active != id || type is null || type.Length > 128 || type.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') ||
                        (passed ? type.Length != 0 : type.Length == 0)) throw new IOException("workflow_test_journal_result");
                    rows[id] = new(id, passed ? WorkflowCaseOutcome.Passed : WorkflowCaseOutcome.Failed, passed ? "" : "assertion_failed"); active = null;
                } else if (kind == "complete") {
                    WorkflowCaptureBridge.Shape(value, "kind", "total", "passed", "failed");
                    int passed = rows.Values.Count(r => r.Outcome == WorkflowCaseOutcome.Passed), failed = rows.Values.Count(r => r.Outcome == WorkflowCaseOutcome.Failed);
                    if (active is not null || started.Count != inventory.Length || passed + failed != inventory.Length ||
                        value.GetProperty("total").GetInt32() != inventory.Length || value.GetProperty("passed").GetInt32() != passed || value.GetProperty("failed").GetInt32() != failed)
                        throw new IOException("workflow_test_journal_summary");
                    complete = true; diagnostic = "";
                } else throw new IOException("workflow_test_journal_event");
            } catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or DecoderFallbackException or ArgumentException) {
                diagnostic = "test_report_invalid"; complete = false; break;
            }
        }
        if (!header) diagnostic = "test_report_header_missing";
        return new(complete && offset == bytes.Length, diagnostic, inventory.Select(id => rows[id]).ToArray());
    }
}
