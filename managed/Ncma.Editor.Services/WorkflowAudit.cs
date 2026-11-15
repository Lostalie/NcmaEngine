using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public enum WorkflowAuditKind { Proposed, Approved, TicketIssued, Receipt, Repaired, Stopped, Verification }
public sealed record WorkflowVerificationAudit(Guid JobId, Guid CaptureId, string Recipe, string Configuration, string Variant,
    string TaskFingerprint, string ReceiptFingerprint, WorkflowVerificationConclusion Conclusion, bool Complete, bool InputsObserved);
public sealed record WorkflowPlanRevision(int Revision, string Hash, string Plan);
public sealed record WorkflowAuditEntry(int SchemaVersion, int Sequence, Guid WorkflowId, Guid Connection,
    WorkflowAuditKind Kind, long Timestamp, EditorViewStamp ExpectedSource, EditorViewStamp ObservedSource,
    int PlanRevision, string PlanHash, string Status, Guid StepId, Guid RequestId, string RequestHash,
    string ApprovalFingerprint, string ScopeFingerprint, WorkflowReceipt? Receipt, string PreviousHash, string Hash)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowVerificationAudit? Verification { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowAssetSource? ExpectedAssets { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowAssetSource? ObservedAssets { get; init; }
}
public sealed record WorkflowAuditView(Guid WorkflowId, WorkflowPlanRevision[] Plans, WorkflowAuditEntry[] Entries);

public sealed partial class EditorWorkflows
{
    // Cold local audit, not a wire capability or execution/approval token. No eviction.
    // <= 3 plans + 3 approvals + 14 tickets + 14 receipts + 2 stop transitions = 36 events.
    public const int MaxAuditEvents = 64;
    public WorkflowAuditView CaptureAudit(Guid id)
    {
        Verify(); var p = Find(id); Refresh(p);
        return new(p.Id, p.PlanRevisions.ToArray(), p.Audit.ToArray());
    }
    // Called by the local job owner ONLY after its own real lease has sealed the
    // receipt. No wire capability, deserialization approval or workflow advance.
    // Retain a bounded hash link here; full immutable receipt stays with its job.
    internal void RecordVerification(WorkflowVerificationTask task, WorkflowVerificationReceipt receipt)
    {
        Verify(); receipt.RequireTask(task); var binding = task.Binding; var p = Find(binding.WorkflowId);
        if (binding.WorkspaceSession != _edit.SessionId || binding.Source.SessionId != _edit.SessionId)
            throw new EditRejectedException("workflow_verification_session");
        var revision = p.PlanRevisions.SingleOrDefault(r => r.Hash == binding.PlanHash)
            ?? throw new EditRejectedException("workflow_verification_plan");
        var existing = p.Audit.FirstOrDefault(e => e.Verification?.JobId == binding.JobId);
        if (existing is not null) {
            if (existing.Verification!.TaskFingerprint != task.Fingerprint || existing.Verification.ReceiptFingerprint != receipt.Fingerprint)
                throw new EditRejectedException("workflow_verification_conflict");
            return; // repeated local viewing cannot inflate the log
        }
        if (p.Audit.Count(e => e.Verification is not null) == WorkflowBuildJobs.MaxJobs)
            throw new EditRejectedException("workflow_verification_budget");
        AuditCapacity(p);
        var entry = new WorkflowAuditEntry(binding.Assets is null ? 2 : 3, p.Audit.Count + 1, p.Id, p.Connection, WorkflowAuditKind.Verification,
            _time.GetTimestamp(), binding.Source, Stamp, revision.Revision, revision.Hash, p.Status, Guid.Empty, Guid.Empty,
            "", "", "", null, p.Audit.Count == 0 ? "" : p.Audit[^1].Hash, "") {
            ExpectedAssets = binding.Assets, ObservedAssets = binding.Assets is null ? null : ReadAssets(),
            Verification = new(binding.JobId, binding.CaptureId, binding.RecipeId, binding.Configuration, binding.Variant,
                task.Fingerprint, receipt.Fingerprint, receipt.Conclusion, receipt.EvidenceComplete, receipt.InputsObserved)
        };
        p.Audit.Add(entry with { Hash = Fingerprint(entry) });
    }
    private static void AuditCapacity(Plan p)
    {
        if (p.Audit.Count >= MaxAuditEvents) throw new EditRejectedException("workflow_audit_budget");
    }
    private void RecordPlan(Plan p, WorkflowAuditKind kind)
    {
        AuditCapacity(p);
        if (p.PlanRevisions.Count >= 3) throw new EditRejectedException("workflow_revision_budget");
        p.PlanRevisions.Add(new(p.Repairs, p.Hash, p.Text));
        Record(p, kind);
    }
    private void Record(Plan p, WorkflowAuditKind kind, Guid stepId = default, Guid requestId = default,
        string requestHash = "", string approval = "", WorkflowReceipt? receipt = null, EditorViewStamp? expected = null,
        WorkflowAssetSource? expectedAssets = null)
    {
        AuditCapacity(p);
        var entry = new WorkflowAuditEntry(p.Assets is null ? 1 : 3, p.Audit.Count + 1, p.Id, p.Connection, kind, _time.GetTimestamp(),
            expected ?? p.Stamp, Stamp, p.Repairs, p.Hash, p.Status, stepId, requestId, requestHash,
            approval, p.Scope?.Fingerprint ?? "", receipt, p.Audit.Count == 0 ? "" : p.Audit[^1].Hash, "") {
            ExpectedAssets = expectedAssets ?? p.Assets, ObservedAssets = p.Assets is null ? null : ReadAssets()
        };
        // Versioned deterministic JSON chaining is diagnostic integrity, NOT a signature/authority.
        p.Audit.Add(entry with { Hash = Fingerprint(entry) });
    }
    private void Stop(Plan p, string status)
    {
        if (p.Status == status) return;
        AuditCapacity(p); p.Status = status;
        Record(p, WorkflowAuditKind.Stopped);
    }
}
