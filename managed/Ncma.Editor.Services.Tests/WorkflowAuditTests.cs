using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;

internal static partial class WorkflowTests
{
    private static string AuditHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void CheckAudit(WorkflowAuditView audit)
    {
        Check(audit.Entries.Length is > 0 and <= EditorWorkflows.MaxAuditEvents);
        string previous = "";
        for (int i = 0; i < audit.Entries.Length; i++) {
            var row = audit.Entries[i];
            Check(row.SchemaVersion == (row.ExpectedAssets is not null ? 3 : row.Verification is null ? 1 : 2) && row.Sequence == i + 1 && row.WorkflowId == audit.WorkflowId);
            Check((row.ExpectedAssets is null) == (row.ObservedAssets is null));
            Check(row.PreviousHash == previous && row.Hash == AuditHash(JsonSerializer.Serialize(row with { Hash = "" }, Wire.Json)));
            var plan = audit.Plans.Single(p => p.Revision == row.PlanRevision);
            Check(plan.Hash == row.PlanHash && plan.Hash == AuditHash(plan.Plan));
            previous = row.Hash;
        }
    }
    private static IEnumerable<(string, Action)> AuditCases(string output, string repository)
    {
        yield return (WorkflowTestInventory.Name("workflow-host-v1", 0), () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")]);
            var service = f.Owner.Workflows!; var review = service.CaptureReview(id);
            service.Approve(review, review.Fingerprint, true);
            var ticket = Next(f, id); Data(Execute(f, ticket));
            var audit = service.CaptureAudit(id); CheckAudit(audit);
            Check(audit.Entries.Select(e => e.Kind).SequenceEqual(new[] { WorkflowAuditKind.Proposed, WorkflowAuditKind.Approved, WorkflowAuditKind.TicketIssued, WorkflowAuditKind.Receipt }));
            Check(audit.Entries[1].ApprovalFingerprint == review.Fingerprint && audit.Entries[1].ScopeFingerprint.Length == 64);
            Check(audit.Entries[2].RequestHash == AuditHash(JsonSerializer.Serialize(ticket, Wire.Json)));
            Check(audit.Entries[^1].Receipt == service.Capture().Single().Receipts.Single());
            string before = JsonSerializer.Serialize(audit, Wire.Json);
            audit.Plans[0] = audit.Plans[0] with { Plan = "forged" };
            audit.Entries[0] = audit.Entries[0] with { Hash = "forged" };
            Check(JsonSerializer.Serialize(service.CaptureAudit(id), Wire.Json) == before);
            Reject(() => Task.Run(() => service.CaptureAudit(id)).GetAwaiter().GetResult());
            f.Time.Now = 121000; f.Workspace.CreateObject(f.Workspace.Stamp); service.Revoke(); service.Cancel(id);
            for (int i = 0; i < 100; i++) Check(service.Capture().Single().Status == "completed");
            Check(JsonSerializer.Serialize(service.CaptureAudit(id), Wire.Json) == before);
            // Synthetic receipt checks ONLY the internal ledger-link contract;
            // this does not pretend a production build/test process ran.
            string h = new('A', 64); var edit = f.Owner.Edit!;
            var task = new WorkflowVerificationTask(new(1, Guid.NewGuid(), edit.SessionId, id, review.Hash, review.Stamp,
                Guid.NewGuid(), h, h, h, null, "editor-services-build-v1", "Debug", "default", "test-sdk", h), [new("input.cs", 1, h)], []);
            var evidence = new WorkflowVerificationReceipt(task, task.Fingerprint, WorkflowProcessOutcome.Exited, 1, 0, 1,
                task.CopyInputs(), [], [new("stdout.log", 1, h)], [], false, "injected_failure");
            byte[] scene = f.Owner.Document.CaptureBytes(); var history = edit.State.UndoCount;
            var state = JsonSerializer.Serialize(service.Capture(), Wire.Json);
            service.RecordVerification(task, evidence); var linked = service.CaptureAudit(id); CheckAudit(linked);
            Check(linked.Entries.Length == 5 && linked.Entries[^1].Verification!.ReceiptFingerprint == evidence.Fingerprint);
            Check(linked.Entries[^1].ExpectedSource == review.Stamp && linked.Entries[^1].Status == "completed");
            service.RecordVerification(task, evidence); Check(service.CaptureAudit(id).Entries.Length == 5);
            Check(JsonSerializer.Serialize(service.Capture(), Wire.Json) == state && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == edit.State.UndoCount);
            var conflict = new WorkflowVerificationReceipt(task, task.Fingerprint, WorkflowProcessOutcome.Exited, 2, 0, 1,
                task.CopyInputs(), [], [new("stdout.log", 1, h)], [], false, "injected_other_failure");
            Reject(() => service.RecordVerification(task, conflict));
            var foreign = new WorkflowVerificationTask(task.Binding with { JobId = Guid.NewGuid(), PlanHash = new string('B', 64) }, task.CopyInputs(), []);
            var foreignReceipt = new WorkflowVerificationReceipt(foreign, foreign.Fingerprint, WorkflowProcessOutcome.NotStarted, null, 0, 0, foreign.CopyInputs(), [], [], []);
            Reject(() => service.RecordVerification(foreign, foreignReceipt));
            Check(service.CaptureAudit(id).Entries.Length == 5);
        });
        yield return (WorkflowTestInventory.Name("workflow-host-v1", 1), () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid objectId = Guid.NewGuid();
            Guid id = Propose(f, [Step("ncma.scene.transaction", new { operations = new[] { new { op = "create", objectId, name = "Audited" } } }), Step("ncma.scene.validate", new { }, "valid")]);
            Approve(f, id); var ticket = Next(f, id); var service = f.Owner.Workflows!;
            string pending = JsonSerializer.Serialize(service.CaptureAudit(id), Wire.Json);
            for (int i = 0; i < 4; i++) {
                Check(Next(f, id).RequestId == ticket.RequestId);
                Check(Execute(f, ticket).GetProperty("isError").GetBoolean());
            }
            Check(JsonSerializer.Serialize(service.CaptureAudit(id), Wire.Json) == pending);
            ApproveTool(f); Data(Execute(f, ticket));
            var committed = f.Owner.Document.CaptureBytes(); var history = f.Owner.Edit!.State.UndoCount;
            service.Cancel(id); var audit = service.CaptureAudit(id); CheckAudit(audit);
            Check(audit.Entries[^1].Status == "cancelled" && audit.Entries[^2].Receipt!.Changed);
            Check(audit.Entries[^2].ExpectedSource.Revision == ticket.ExpectedRevision && audit.Entries[^2].ObservedSource.Revision == f.Owner.Edit.Revision);
            Check(Op(f, "next", id).GetProperty("isError").GetBoolean());
            for (int i = 0; i < 100; i++) service.Cancel(id);
            Check(service.CaptureAudit(id).Entries.Length == audit.Entries.Length);
            Check(committed.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == 1 && f.Owner.Edit.State.UndoCount == history);
        });
        yield return (WorkflowTestInventory.Name("workflow-host-v1", 2), () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            var steps = Enumerable.Range(0, 11).Select(_ => Step("ncma.scene.validate", new { }, "valid"))
                .Append(Step("ncma.scene.object.inspect", new { objectId = Guid.NewGuid(), section = "summary" })).ToArray();
            Guid id = Propose(f, steps, budget: 2); Approve(f, id);
            for (int i = 0; i < 11; i++) Data(Execute(f, Next(f, id)));
            Check(Execute(f, Next(f, id)).GetProperty("isError").GetBoolean());
            var service = f.Owner.Workflows!; var first = service.CaptureAudit(id);
            for (int i = 0; i < 2; i++) {
                Data(f.Call(EditorWorkflows.Capability, new { op = "repair", workflowId = id,
                    input = new { objectId = Guid.NewGuid(), section = "summary" } }, Guid.NewGuid()));
                Check(Op(f, "next", id).GetProperty("isError").GetBoolean());
                Approve(f, id); Check(Execute(f, Next(f, id)).GetProperty("isError").GetBoolean());
            }
            var audit = service.CaptureAudit(id); CheckAudit(audit);
            Check(audit.Plans.Length == 3 && audit.Plans[0] == first.Plans[0] && audit.Plans.Select(p => p.Hash).Distinct().Count() == 3);
            Check(audit.Entries.Length == 34 && audit.Entries.Take(first.Entries.Length).SequenceEqual(first.Entries));
            Check(audit.Entries.Count(e => e.Kind == WorkflowAuditKind.Receipt) == 14 && service.Capture().Single().Receipts.Length == 14);
            Check(f.Call(EditorWorkflows.Capability, new { op = "repair", workflowId = id, input = new { } }, Guid.NewGuid()).GetProperty("isError").GetBoolean());
            Check(service.CaptureAudit(id).Entries.SequenceEqual(audit.Entries) && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return (WorkflowTestInventory.Name("workflow-host-v1", 3), () => {
            foreach (string reason in new[] { "expired", "revoked", "conflict" }) {
                using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
                Guid id = Propose(f, [Step("ncma.scene.validate", new { })], deadline: 1); Approve(f, id); var ticket = Next(f, id);
                if (reason == "expired") f.Time.Now = 1000;
                else if (reason == "revoked") {
                    var endpoint = f.Owner.Endpoint!; var connection = endpoint.View.Connections.Single().ConnectionId;
                    endpoint.Revoke(connection); endpoint.Pair(connection, true);
                } else f.Workspace.CreateObject(f.Workspace.Stamp);
                var service = f.Owner.Workflows!; var audit = service.CaptureAudit(id); CheckAudit(audit);
                Check(audit.Entries[^1].Kind == WorkflowAuditKind.Stopped && audit.Entries[^1].Status == reason);
                Check(Execute(f, ticket).GetProperty("isError").GetBoolean());
                for (int i = 0; i < 100; i++) Check(service.CaptureAudit(id).Entries.SequenceEqual(audit.Entries));
                Check(service.Capture().Single().Receipts.Length == 0);
            }
        });
    }
}
