using System.Security.Cryptography;
using Ncma.Application;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

public sealed record WorkflowProcessAssociationReport(Guid RunId, string WorkflowSha256, string ProcessSha256,
    WorkflowObservationSummary Workflow, bool ProcessBoundToLoop, bool ProcessCadenceComplete, bool ProcessCountersAvailable)
{
    public bool CommonClockVerified => false;
    public bool ProvenanceVerified => false;
    public bool PerformanceAccepted => false;
    public string Scope => "same supplied run/source/workload/configuration/variant labels and expected bytes only; no cross-report timestamp subtraction or process provenance proof; not all domain commits";
}
public static class WorkflowProcessAssociation
{
    // Expected hashes/identities must come from separately pinned caller records, not input self-claims.
    // No path, model, approval, process control or code execution; no default product dependency change.
    public static WorkflowProcessAssociationReport Inspect(ReadOnlySpan<byte> workflowBytes, ReadOnlySpan<byte> processBytes,
        ObservationIdentity expectedWorkflow, AiAuthoringIntent expectedIntent, ProcessMeasurementIdentity expectedProcess,
        string expectedWorkflowSha256, string expectedProcessSha256)
    {
        if (workflowBytes.Length is < 1 or > AiWorkflowObservationEvidence.MaximumBytes || processBytes.Length is < 1 or > ProcessSeriesEvidence.MaximumBytes)
            throw new ArgumentException("workflow_process_byte_budget");
        byte[] workflow = workflowBytes.ToArray(), process = processBytes.ToArray();
        string workflowHash = Convert.ToHexString(SHA256.HashData(workflow)), processHash = Convert.ToHexString(SHA256.HashData(process));
        if (workflowHash != expectedWorkflowSha256 || processHash != expectedProcessSha256) throw new ArgumentException("workflow_process_expected_bytes");
        var domain = AiWorkflowObservationEvidence.DecodeExpected(workflow, expectedWorkflow, expectedIntent);
        var measurement = ProcessMeasurementEvidence.DecodeExpected(process, expectedProcess);
        ValidateLabels(domain.Identity, measurement.Identity);
        // Matching frequency alone never proves common process/clock. Do not join or subtract timestamps.
        return new(domain.Identity.RunId, workflowHash, processHash, domain.Summary, measurement.BoundToLoop,
            measurement.Series.CompleteCadenceCoverage, measurement.Series.AllCountersAvailable);
    }
    // Shared label contract for single-workflow and workspace consumers, including empty workspaces.
    internal static void ValidateLabels(ObservationIdentity left, ProcessMeasurementIdentity right)
    {
        bool configuration = (left.Configuration, right.Configuration) is
            (ObservationConfiguration.Debug, MeasurementConfiguration.Debug) or (ObservationConfiguration.Release, MeasurementConfiguration.Release);
        bool variant = (left.Variant, right.Variant) is
            (ObservationVariant.Ai, MeasurementVariant.Ai) or (ObservationVariant.AiExtensions, MeasurementVariant.AiExtensions);
        if (left.RunId != right.RunId || left.SourceSha256 != right.SourceSha256 || left.WorkloadSha256 != right.WorkloadSha256 ||
            !configuration || !variant || right.Host != MeasurementHost.Editor)
            throw new ArgumentException("workflow_process_label_mismatch");
    }
}
