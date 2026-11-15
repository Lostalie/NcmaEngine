using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
namespace Ncma.Editor.Services;

public sealed record WorkflowRegistryReview(Guid Session, ulong Generation, Guid Catalog, Guid Endpoint,
    ulong AudienceEpoch, Guid[] Audience, string Registry, string Fingerprint);
// Authoring clock, not a file hash/scan or a resource/file permission grant.
public readonly record struct WorkflowAssetSource(Guid ProjectId, ulong Generation, ulong Revision);
public sealed record WorkflowReview(Guid WorkflowId, EditorViewStamp Stamp, Guid Connection, string Plan,
    string Hash, string Fingerprint)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowAssetSource? Assets { get; init; }
}
public sealed record WorkflowReceipt(Guid StepId, Guid RequestId, string Capability, string Status, string Code,
    bool Changed, ulong ExecutionRevision, string ResultHash, bool AssertionPassed);
public sealed record WorkflowView(Guid WorkflowId, string Status, int Index, int StepCount, int RepairCount,
    string Hash, string? Ticket, WorkflowReceipt[] Receipts);

// A ledger and exact request fence, NOT a nested tool executor or an approval authority.
// Only the original endpoint/capability may execute a ticket, with its original independent grants.
public sealed partial class EditorWorkflows : IEditorRequestMonitor
{
    public const string Capability = "ncma.workflow";
    public const int MaxSteps = 12, MaxPlans = 16, MaxActive = 2, MaxPlanBytes = 48 * 1024, MaxStepBytes = 16 * 1024;
    private sealed record Step(Guid Id, string Capability, JsonElement Input, string Expectation);
    private sealed class Plan(Guid id, Guid connection, Step[] steps, int deadline, int budget, EditorViewStamp stamp, string text)
    {
        internal readonly Guid Id = id, Connection = connection;
        internal Step[] Steps = steps;
        internal readonly int Deadline = deadline, Budget = budget;
        internal EditorViewStamp Stamp = stamp;
        internal WorkflowAssetSource? Assets;
        internal string Text = text, Hash = Digest(text), Status = "proposed";
        internal int Index, Repairs;
        internal long Started, Approved;
        internal WorkflowRegistryReview? Scope;
        internal CapabilityRequest? Ticket;
        internal readonly List<WorkflowReceipt> Receipts = [];
        internal readonly List<WorkflowPlanRevision> PlanRevisions = new(3);
        internal readonly List<WorkflowAuditEntry> Audit = new(MaxAuditEvents);
    }
    // Ticket tombstones are retained for the bounded lifetime of this editor, not evicted.
    private sealed record TicketOwner(Plan Plan, string Fingerprint);
    private readonly EditSession _edit;
    private readonly TimeProvider _time;
    private readonly Func<WorkflowAssetSource>? _assetSource;
    private EditorEndpoint? _endpoint;
    private Guid _caller;
    private readonly Dictionary<Guid, Plan> _plans = [];
    private readonly Dictionary<Guid, TicketOwner> _tickets = [];
    private WorkflowRegistryReview? _visibility;
    private long _visibleAt;
    private bool _closed;
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal) {
        "ncma.scene.inspect", "ncma.scene.object.inspect", "ncma.scene.validate", "ncma.scene.transaction",
        "ncma.assets.list", "ncma.assets.inspect", "ncma.assets.validate", "ncma.ui.document.commit",
        "ncma.animgraph.inspect", "ncma.animgraph.validate", "ncma.animgraph.bones", "ncma.animgraph.propose",
        "ncma.animgraph.transaction", "ncma.animgraph.sequence.propose", "ncma.animgraph.sequence.run" };
    public EditorWorkflows(EditSession edit, TimeProvider? time = null) : this(edit, time, null) { }
    internal EditorWorkflows(EditSession edit, TimeProvider? time, Func<WorkflowAssetSource>? assetSource)
    {
        _edit = edit; _time = time ?? TimeProvider.System; _assetSource = assetSource;
        edit.RegisterInspection(WorkflowSchemas.Descriptor(), Invoke);
    }
    private static bool UsesAssets(Step[] steps) => steps.Any(s => s.Capability.StartsWith("ncma.assets.", StringComparison.Ordinal) ||
        s.Capability.StartsWith("ncma.animgraph.", StringComparison.Ordinal) || s.Capability == "ncma.ui.document.commit");
    private WorkflowAssetSource ReadAssets() => _assetSource?.Invoke() ?? default;
    private bool AssetsCurrent(Plan plan) => plan.Assets is not { } expected || expected == ReadAssets();
    public void Bind(EditorEndpoint? endpoint)
    {
        Verify(); Revoke(); _caller = Guid.Empty; _endpoint = endpoint;
    }
    public void Close() { Verify(); Revoke(); _closed = true; _caller = Guid.Empty; }
    private void Verify() { _edit.Document.VerifyAccess(); if (_closed) throw new EditRejectedException("workflow_closed"); }
    private EditorViewStamp Stamp => new(_edit.SessionId, _edit.DocumentGeneration, _edit.Revision);
    private void Idle()
    {
        Verify(); var state = _edit.State;
        if (state.Frozen || state.HistoryInvalidated || state.EditBusy) throw new EditRejectedException("workflow_edit_unavailable");
    }
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Fingerprint(object value) => Digest(JsonSerializer.Serialize(value, Wire.Json));
    private bool Fresh(long start, int seconds) => _time.GetElapsedTime(start, _time.GetTimestamp()) is var elapsed && elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(seconds);
    private WorkflowRegistryReview Registry()
    {
        Verify(); var endpoint = _endpoint ?? throw new EditRejectedException("workflow_endpoint_missing");
        var view = endpoint.View; var audience = view.Connections.Where(c => c.Paired && c.Connected).Select(c => c.ConnectionId).Order().ToArray();
        var descriptors = _edit.Describe().Where(d => Supported.Contains(d.Name)).OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
        string registry = JsonSerializer.Serialize(descriptors.Select(d => new { d.Name, d.Description, risk = d.Risk.ToString(),
            inputHash = Digest(d.InputSchema.GetRawText()), outputHash = Digest(d.OutputSchema.GetRawText()) }), Wire.Json);
        var r = new WorkflowRegistryReview(_edit.SessionId, _edit.DocumentGeneration, _edit.BehaviourCatalogGeneration,
            view.InstanceId, endpoint.AudienceRevision, audience, registry, "");
        return r with { Fingerprint = Fingerprint(r) };
    }
    public WorkflowRegistryReview CaptureRegistry() { Idle(); return Registry(); }
    public void ApproveRegistry(WorkflowRegistryReview review, string fingerprint, bool reviewed)
    {
        Idle(); var current = Registry();
        if (!reviewed || current.Audience.Length == 0 || fingerprint != current.Fingerprint || Fingerprint(review) != Fingerprint(current))
            throw new EditRejectedException("workflow_registry_review_stale");
        _visibility = current; _visibleAt = _time.GetTimestamp();
    }
    public void Revoke()
    {
        Verify(); _visibility = null;
        foreach (var p in _plans.Values) if (p.Status is "approved" or "proposed" or "failed") Stop(p, "revoked");
        foreach (var p in _plans.Values) p.Scope = null;
    }
    private bool ScopeCurrent(WorkflowRegistryReview? scope) => scope is not null && scope.Fingerprint == Registry().Fingerprint;
    private void Visible(Guid connection)
    {
        Idle(); if (!Fresh(_visibleAt, 60) || !ScopeCurrent(_visibility) || !_visibility!.Audience.Contains(connection))
            throw new EditRejectedException("workflow_not_visible");
    }
    public WorkflowView[] Capture()
    {
        Verify(); foreach (var p in _plans.Values) Refresh(p);
        return _plans.Values.Select(View).ToArray();
    }
    private static WorkflowView View(Plan p) => new(p.Id, p.Status, p.Index, p.Steps.Length, p.Repairs, p.Hash,
        p.Ticket is null ? null : JsonSerializer.Serialize(p.Ticket, Wire.Json), p.Receipts.ToArray());
    public WorkflowReview CaptureReview(Guid id)
    {
        Idle(); var p = Find(id); Refresh(p);
        if (p.Status != "proposed" || p.Stamp != Stamp) throw new EditRejectedException("workflow_review_unavailable");
        var scope = Registry(); if (!scope.Audience.Contains(p.Connection)) throw new EditRejectedException("workflow_connection_revoked");
        var r = new WorkflowReview(id, p.Stamp, p.Connection, p.Text, p.Hash, "") { Assets = p.Assets };
        return r with { Fingerprint = Fingerprint(new { review = r, scope = scope.Fingerprint }) };
    }
    public void Approve(WorkflowReview review, string fingerprint, bool reviewed)
    {
        Idle(); var current = CaptureReview(review.WorkflowId);
        if (!reviewed || fingerprint != current.Fingerprint || Fingerprint(review) != Fingerprint(current)) throw new EditRejectedException("workflow_review_stale");
        var p = Find(review.WorkflowId); Visible(p.Connection);
        foreach (var other in _plans.Values) Refresh(other);
        if (_plans.Values.Count(x => x.Status == "approved") >= MaxActive) throw new EditRejectedException("workflow_active_budget");
        AuditCapacity(p);
        p.Scope = Registry(); p.Approved = _time.GetTimestamp(); if (p.Repairs == 0) p.Started = p.Approved;
        p.Status = "approved";
        Record(p, WorkflowAuditKind.Approved, approval: current.Fingerprint);
    }
    public void Cancel(Guid id)
    {
        Verify(); var p = Find(id); if (p.Status != "completed") Stop(p, "cancelled");
        // Retain exact tickets and receipts. Cancellation never performs Undo or file/native rollback.
    }
    private Plan Find(Guid id) => _plans.GetValueOrDefault(id) ?? throw new EditRejectedException("workflow_not_found");
    // Local continuation guard, NOT a capability/approval. Completion preserves
    // receipts, but never makes an unrelated later edit a source for this plan.
    internal bool IsVerificationSourceCurrent(WorkflowVerificationBinding binding)
    {
        Verify(); return binding.WorkspaceSession == _edit.SessionId && IsVerificationSourceCurrent(binding.WorkflowId, binding.PlanHash, binding.Source) &&
            binding.Assets == Find(binding.WorkflowId).Assets;
    }
    internal WorkflowAssetSource? VerificationAssetSource(Guid workflowId) { Verify(); return Find(workflowId).Assets; }
    internal bool IsVerificationSourceCurrent(Guid workflowId, string planHash, EditorViewStamp source)
    {
        Verify(); var state = _edit.State;
        if (state.Frozen || state.HistoryInvalidated || state.EditBusy || source != Stamp || !_plans.TryGetValue(workflowId, out var plan)) return false;
        Refresh(plan);
        return plan.Hash == planHash && plan.Stamp == Stamp && AssetsCurrent(plan) && plan.Status is "proposed" or "approved" or "completed";
    }
    private void Refresh(Plan p)
    {
        if (p.Status is "completed" or "cancelled" or "revoked" or "expired" or "conflict") return;
        // A project refresh can advance this clock WITHOUT changing Edit.Revision.
        // Latch conflict even before approval; never adopt an unrelated new source.
        if (!AssetsCurrent(p)) { Stop(p, "conflict"); return; }
        if (p.Scope is null) return;
        if (!ScopeCurrent(p.Scope)) Stop(p, "revoked");
        else if (!Fresh(p.Started, p.Deadline) || !Fresh(p.Approved, 60)) Stop(p, "expired");
        else if (p.Stamp != Stamp) Stop(p, "conflict");
    }
    private void Executable(Plan p, Guid connection)
    {
        Idle(); Refresh(p);
        if (p.Connection != connection) throw new EditRejectedException("workflow_owner_mismatch");
        Visible(connection);
        if (p.Status != "approved") throw new EditRejectedException("workflow_" + p.Status);
    }
    private WorkflowView Next(Plan p, Guid connection)
    {
        Executable(p, connection);
        if (p.Ticket is null) {
            AuditCapacity(p);
            var step = p.Steps[p.Index];
            var request = new CapabilityRequest(2, Guid.NewGuid(), _edit.SessionId, p.Stamp.Revision, step.Capability, step.Input.Clone());
            _tickets.Add(request.RequestId, new(p, Fingerprint(request))); p.Ticket = request;
            Record(p, WorkflowAuditKind.TicketIssued, step.Id, request.RequestId, Fingerprint(request));
        }
        return View(p);
    }
    public string? Before(Guid connection, CapabilityRequest request)
    {
        Verify(); _caller = Guid.Empty;
        if (!_tickets.TryGetValue(request.RequestId, out var ticket)) {
            if (request.Capability == Capability) _caller = connection;
            return null;
        }
        try {
            var p = ticket.Plan; Executable(p, connection);
            if (ticket.Fingerprint != Fingerprint(request)) return "workflow_ticket_mismatch";
            if (p.Ticket?.RequestId != request.RequestId) return "workflow_step_recorded";
            AuditCapacity(p); // Reserve the bounded receipt slot before original execution.
            return null;
        }
        catch (EditRejectedException e) { return e.Code; }
    }
    public void After(Guid connection, CapabilityRequest request, CapabilityResult result)
    {
        Verify(); _caller = Guid.Empty;
        if (!_tickets.TryGetValue(request.RequestId, out var ticket)) return;
        var p = ticket.Plan;
        if (p.Ticket?.RequestId != request.RequestId || p.Connection != connection) throw new InvalidOperationException("Workflow receipt identity changed.");
        // Missing independent human approvals are wait states, never repair attempts or implicit grants.
        if (result.Code is "permission_denied" or "graph_scope_denied" or "ui_scope_denied" ||
            result.Code.EndsWith("not_visible", StringComparison.Ordinal) || result.Code.EndsWith("not_approved", StringComparison.Ordinal)) return;
        AuditCapacity(p);
        var expectedSource = p.Stamp;
        var expectedAssets = p.Assets;
        var step = p.Steps[p.Index]; bool assertion = result.Status == "ok" && Meets(step.Expectation, result.Data);
        p.Receipts.Add(new(step.Id, request.RequestId, step.Capability, result.Status, result.Code, result.Changed,
            result.ExecutionRevision, Digest(result.Data.GetRawText()), assertion));
        p.Ticket = null;
        if (result.Status == "ok") {
            // Only this exact successfully observed operation can advance the expected revision.
            p.Stamp = Stamp;
            if (p.Assets is not null) p.Assets = ReadAssets();
            if (assertion) { p.Index++; p.Status = p.Index == p.Steps.Length ? "completed" : "approved"; }
            else p.Status = "failed";
        } else p.Status = "failed";
        Record(p, WorkflowAuditKind.Receipt, step.Id, request.RequestId, ticket.Fingerprint,
            receipt: p.Receipts[^1], expected: expectedSource, expectedAssets: expectedAssets);
    }
    private static bool Meets(string expectation, JsonElement data) => expectation switch {
        "success" => true,
        "valid" => data.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.True,
        "passed" => data.TryGetProperty("section", out var section) && section.GetString() == "summary" &&
            data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == 1 &&
            items[0].TryGetProperty("passed", out var passed) && passed.ValueKind == JsonValueKind.True,
        _ => false };
    private object Invoke(JsonElement input)
    {
        Verify(); if (_caller == Guid.Empty) throw new EditRejectedException("workflow_endpoint_context_required");
        Guid caller = _caller; string op = Text(input, "op", 16);
        if (op == "propose") {
            Closed(input, "op", "plan"); Visible(caller);
            if (_plans.Count >= MaxPlans) throw new EditRejectedException("workflow_plan_budget");
            var plan = input.GetProperty("plan"); var parsed = Parse(plan); if (_plans.ContainsKey(parsed.Id)) throw new EditRejectedException("workflow_id_reused");
            var p = new Plan(parsed.Id, caller, parsed.Steps, parsed.Deadline, parsed.Budget, Stamp, plan.GetRawText()) {
                Assets = UsesAssets(parsed.Steps) ? ReadAssets() : null };
            RecordPlan(p, WorkflowAuditKind.Proposed);
            _plans.Add(p.Id, p); return View(p);
        }
        if (op == "repair") Closed(input, "op", "workflowId", "input"); else Closed(input, "op", "workflowId");
        var existing = Find(Id(input, "workflowId"));
        if (existing.Connection != caller) throw new EditRejectedException("workflow_owner_mismatch");
        if (op == "cancel") { Cancel(existing.Id); return View(existing); }
        Visible(caller); Refresh(existing);
        switch (op) {
            case "inspect": return View(existing);
            case "next": return Next(existing, caller);
            case "repair":
                if (existing.Status != "failed" || existing.Repairs >= existing.Budget || existing.Stamp != Stamp)
                    throw new EditRejectedException("workflow_repair_unavailable");
                var repaired = input.GetProperty("input"); ValidateInput(repaired);
                AuditCapacity(existing);
                var steps = existing.Steps.ToArray(); steps[existing.Index] = steps[existing.Index] with { Input = repaired.Clone() };
                var previous = existing.Steps; existing.Steps = steps;
                string candidate = Encode(existing);
                try { _ = Parse(JsonSerializer.Deserialize<JsonElement>(candidate)); }
                catch { existing.Steps = previous; throw; }
                existing.Text = candidate; existing.Hash = Digest(existing.Text);
                existing.Repairs++; existing.Status = "proposed";
                RecordPlan(existing, WorkflowAuditKind.Repaired); return View(existing);
            default: throw new EditRejectedException("workflow_operation_invalid");
        }
    }
    private static string Encode(Plan p) => JsonSerializer.Serialize(new { version = 1, workflowId = p.Id, deadlineSeconds = p.Deadline,
        repairBudget = p.Budget, steps = p.Steps.Select(s => new { id = s.Id, capability = s.Capability, input = s.Input, expectation = s.Expectation }) }, Wire.Json);
    private (Guid Id, Step[] Steps, int Deadline, int Budget) Parse(JsonElement plan)
    {
        Closed(plan, "version", "workflowId", "deadlineSeconds", "repairBudget", "steps");
        if (Encoding.UTF8.GetByteCount(plan.GetRawText()) > MaxPlanBytes || Integer(plan, "version", 1, 1) != 1) throw new EditRejectedException("workflow_plan_invalid");
        var rows = plan.GetProperty("steps"); if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is < 1 or > MaxSteps) throw new EditRejectedException("workflow_step_budget");
        var descriptors = _edit.Describe().ToDictionary(d => d.Name, StringComparer.Ordinal); var ids = new HashSet<Guid>(); var steps = new List<Step>();
        foreach (var row in rows.EnumerateArray()) {
            Closed(row, "id", "capability", "input", "expectation"); var id = Id(row, "id"); string capability = Text(row, "capability", 128), expectation = Text(row, "expectation", 16);
            if (!ids.Add(id)) throw new EditRejectedException("workflow_step_id_reused");
            if (!Supported.Contains(capability) || !descriptors.TryGetValue(capability, out var descriptor) || descriptor.Risk == MutationRisk.Destructive)
                throw new EditRejectedException("workflow_unsupported_capability");
            if (expectation != "success" && !(expectation == "valid" && capability is "ncma.scene.validate" or "ncma.assets.validate" or "ncma.animgraph.validate") &&
                !(expectation == "passed" && capability == "ncma.animgraph.sequence.run")) throw new EditRejectedException("workflow_expectation_invalid");
            var value = row.GetProperty("input"); ValidateInput(value);
            if (expectation == "passed" && (!value.TryGetProperty("section", out var section) || section.GetString() != "summary")) throw new EditRejectedException("workflow_summary_required");
            steps.Add(new(id, capability, value.Clone(), expectation));
        }
        return (Id(plan, "workflowId"), steps.ToArray(), Integer(plan, "deadlineSeconds", 1, 120), Integer(plan, "repairBudget", 0, 2));
    }
    // Envelope/budget validation only. Original capabilities still own their complete schemas and semantics.
    private static void ValidateInput(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(input.GetRawText()) > MaxStepBytes) throw new EditRejectedException("workflow_input_invalid");
        Duplicates(input, 0);
    }
    private static void Duplicates(JsonElement input, int depth)
    {
        if (depth > 32) throw new EditRejectedException("workflow_input_depth");
        if (input.ValueKind == JsonValueKind.Object) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var field in input.EnumerateObject()) { if (!seen.Add(field.Name)) throw new EditRejectedException("workflow_duplicate_field"); Duplicates(field.Value, depth + 1); } }
        else if (input.ValueKind == JsonValueKind.Array) foreach (var item in input.EnumerateArray()) Duplicates(item, depth + 1);
    }
    private static void Closed(JsonElement input, params string[] names)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Count() != names.Length || input.EnumerateObject().Any(p => !names.Contains(p.Name, StringComparer.Ordinal)) || names.Any(n => !input.TryGetProperty(n, out _)))
            throw new EditRejectedException("workflow_closed_fields");
    }
    private static Guid Id(JsonElement input, string name) => input.GetProperty(name).ValueKind == JsonValueKind.String && input.GetProperty(name).TryGetGuid(out var id) && id != Guid.Empty && input.GetProperty(name).GetString() == id.ToString("D") ? id : throw new EditRejectedException("workflow_uuid_invalid");
    private static string Text(JsonElement input, string name, int max) => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text && text.Length <= max ? text : throw new EditRejectedException("workflow_text_invalid");
    private static int Integer(JsonElement input, string name, int min, int max) => input.GetProperty(name).TryGetInt32(out int value) && value >= min && value <= max ? value : throw new EditRejectedException("workflow_integer_invalid");
}
