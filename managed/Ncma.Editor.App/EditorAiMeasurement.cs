#if NCMA_AI
using System.Diagnostics;
using Ncma.Editor.Modules;
using Ncma.Modules;

namespace Ncma.Editor.App;

internal sealed record EditorAiMeasurementStatus(bool Configured, bool Attached, bool SealAttempted, bool HasEvidence,
    bool OuterRetained, string? FailureType, AiMeasurementBudgetStatus? Budget);
internal sealed unsafe partial class CandidatePresentation
{
    private ObservationIdentity? _aiMeasurementIdentity;
    private AiWorkspaceMeasurementLimits? _aiMeasurementLimits;
    private bool _aiMeasurementStartup, _aiMeasurementAttached, _aiMeasurementSealed;
    private byte[]? _aiMeasurementBytes;
    private string? _aiMeasurementFailure;
    private AiMeasurementBudgetStatus? _aiMeasurementBudget;

    // Trusted composition before Start only. No CLI/UI/MCP exposure, project load or approval here.
    internal void ConfigureAiMeasurement(ObservationIdentity identity, AiWorkspaceMeasurementLimits limits)
    {
        VerifyGraphicsOwner(); ArgumentNullException.ThrowIfNull(limits);
        if (_aiMeasurementStartup || _closed || _graphicsShutdown || _aiMeasurementIdentity is not null || preview)
            throw new InvalidOperationException("editor_ai_measurement_boundary");
        _ = new ModuleObservationCapture(identity, 0, 1);
#if DEBUG
        const ObservationConfiguration configuration = ObservationConfiguration.Debug;
#else
        const ObservationConfiguration configuration = ObservationConfiguration.Release;
#endif
#if NCMA_EXTENSIONS
        const ObservationVariant variant = ObservationVariant.AiExtensions;
#else
        const ObservationVariant variant = ObservationVariant.Ai;
#endif
        if (identity.Configuration != configuration || identity.Variant != variant || identity.TimestampFrequency != Stopwatch.Frequency)
            throw new ArgumentException("editor_ai_measurement_build_identity");
        _aiMeasurementIdentity = identity; _aiMeasurementLimits = limits;
    }
    private void AttachAiMeasurement()
    {
        if (_aiMeasurementIdentity is null) return;
        _aiAuthoring!.BeginMeasuredObservation(_aiMeasurementIdentity, _aiMeasurementLimits!);
        _aiMeasurementAttached = true;
    }
    private void CompleteAiMeasurement()
    {
        if (_aiMeasurementIdentity is null || _aiMeasurementSealed) return;
        _aiMeasurementSealed = true; // Non-retryable evidence attempt, distinct from explicit application close retries.
        try {
            CaptureAiSceneMeasurementFinal();
            if (!_aiMeasurementAttached || _aiAuthoring is null) throw new InvalidOperationException("editor_ai_measurement_not_attached");
            var report = _aiAuthoring.SealMeasuredObservation();
            _aiMeasurementBytes = AiWorkspaceObservationEvidence.Encode(report);
        } catch (Exception error) when (error is not OutOfMemoryException) { _aiMeasurementFailure = error.GetType().Name; }
        finally { if (_aiAuthoring is not null) _aiMeasurementBudget = _aiAuthoring.MeasurementBudget; }
    }
    internal void SealAiMeasurement()
    {
        VerifyGraphicsOwner();
        if (_aiMeasurementIdentity is null || !_aiMeasurementStartup || _aiMeasurementSealed || _graphicsFrameOpen)
            throw new InvalidOperationException("editor_ai_measurement_seal_boundary");
        CompleteAiMeasurement();
    }
    // Diagnostics have no close/retry/IO side effect. Detached bytes survive the original workspace release.
    internal EditorAiMeasurementStatus AiMeasurementStatus { get { VerifyGraphicsOwner(); return new(_aiMeasurementIdentity is not null,
        _aiMeasurementAttached, _aiMeasurementSealed, _aiMeasurementBytes is not null, _graphicsShutdown && !_closed,
        _aiMeasurementFailure, _aiMeasurementBudget ?? _aiAuthoring?.MeasurementBudget); } }
    internal byte[]? CopyAiMeasurementEvidence() { VerifyGraphicsOwner(); return _aiMeasurementBytes?.ToArray(); }
}
#endif
