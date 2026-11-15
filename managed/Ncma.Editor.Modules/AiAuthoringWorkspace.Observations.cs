using System.Diagnostics;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

public sealed partial class AiAuthoringWorkspace
{
    private AiWorkspaceObservations? observations;
    private bool observationStarted, authoringStarted;

    // Trusted host opt-in only. No presenter/MCP command, transport probe, send or approval.
    public void BeginObservation(ObservationIdentity identity, int workflowCapacity = 32,
        int workflowByteLimit = AiWorkspaceObservationEvidence.MaximumWorkflowBytes,
        int totalByteLimit = AiWorkspaceObservationEvidence.MaximumPayloadBytes)
    {
        Active();
        if (observationStarted || authoringStarted) throw new InvalidOperationException("ai_workspace_observation_boundary");
        observations = new(identity, workflowCapacity, workflowByteLimit, totalByteLimit);
        observationStarted = true;
    }

    // May seal after TryClose, or seal a pending prefix without cancelling/draining business.
    // Single use even when export/validation later fails; no automatic retry/reset.
    public AiWorkspaceObservationRecord SealObservation()
    {
        Verify();
        var current = observations ?? throw new InvalidOperationException("ai_workspace_observation_absent");
        observations = null;
        return current.Seal();
    }
}

internal sealed class AiWorkspaceObservations
{
    private readonly ObservationIdentity identity;
    private readonly long started;
    private readonly int byteLimit, totalLimit;
    private readonly AiWorkspaceWorkflowEvidence[] entries;
    private int count, bytes;
    private long workflows, dropped, failed;
    private AiDomainWorkflow? active;
    private Guid configuration;

    internal AiWorkspaceObservations(ObservationIdentity identity, int capacity, int byteLimit, int totalLimit)
    {
        AiWorkspaceObservationEvidence.ValidateLimits(capacity, byteLimit, totalLimit);
        _ = new ModuleObservationCapture(identity, 0, 1);
        if (identity.TimestampFrequency != Stopwatch.Frequency || identity.Variant is not (ObservationVariant.Ai or ObservationVariant.AiExtensions))
            throw new ArgumentException("ai_workspace_observation_identity");
        this.identity = identity; this.byteLimit = byteLimit; this.totalLimit = totalLimit;
        entries = new AiWorkspaceWorkflowEvidence[capacity]; started = Stopwatch.GetTimestamp();
    }

    internal void Attach(AiDomainWorkflow workflow)
    {
        workflows++;
        // Bounded prefix of attempted workflows, not a reservoir that hides earlier failures.
        if (workflows > entries.Length || totalLimit - bytes < byteLimit) { dropped++; return; }
        try {
            configuration = workflow.Target.ConfigurationId;
            workflow.BeginObservation(identity);
            active = workflow;
        } catch (Exception error) when (error is not OutOfMemoryException) { failed++; }
    }

    internal void Retire(AiDomainWorkflow workflow)
    {
        if (ReferenceEquals(active, workflow)) Capture(released: true);
    }

    private void Capture(bool released)
    {
        var workflow = active!; active = null; // Never retain business through detached evidence.
        try {
            var report = workflow.SealObservation();
            var data = AiWorkflowObservationEvidence.Encode(report, byteLimit);
            entries[count++] = new(workflows, configuration, report.Intent, released, data);
            bytes += data.Length;
        } catch (Exception error) when (error is not OutOfMemoryException) { failed++; }
    }

    internal AiWorkspaceObservationRecord Seal()
    {
        if (active is not null) Capture(released: false);
        return new(1, identity, started, Stopwatch.GetTimestamp(), entries.Length, byteLimit, totalLimit,
            workflows, dropped, failed, entries.AsSpan(0, count).ToArray());
    }
}
