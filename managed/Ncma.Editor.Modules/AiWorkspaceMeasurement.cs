using System.Security.Cryptography;
using System.Text.Json;
using Ncma.AI;
using Ncma.Editor.Core;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

// Restriction only, NOT a grant, price cap, arbitrary-plugin sandbox or global network quota.
public sealed class AiWorkspaceMeasurementLimits
{
    private readonly AiConnectionProfile recipient;
    public int MaximumAttempts { get; }
    public string Fingerprint { get; }
    public AiWorkspaceMeasurementLimits(AiConnectionProfile expectedRecipient, int maximumAttempts)
    {
        ArgumentNullException.ThrowIfNull(expectedRecipient);
        if (maximumAttempts is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        recipient = expectedRecipient; MaximumAttempts = maximumAttempts;
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            version = 1, expectedRecipient.ProviderId, expectedRecipient.Protocol, expectedRecipient.Deployment,
            expectedRecipient.Endpoint, expectedRecipient.Model, expectedRecipient.MaximumOutputTokens,
            expectedRecipient.TimeoutMilliseconds, maximumAttempts
        })));
    }
    internal bool Matches(AiConnectionProfile actual) => actual.ProviderId == recipient.ProviderId && actual.Protocol == recipient.Protocol &&
        actual.Deployment == recipient.Deployment && actual.Endpoint == recipient.Endpoint && actual.Model == recipient.Model &&
        actual.MaximumOutputTokens <= recipient.MaximumOutputTokens && actual.TimeoutMilliseconds <= recipient.TimeoutMilliseconds;
}
public sealed record AiMeasurementBudgetStatus(string PolicySha256, int MaximumAttempts, int ChargedAttempts, bool Closed)
{
    public int RemainingAttempts => MaximumAttempts - ChargedAttempts;
    public bool GrantsExecutionAuthority => false;
    public bool CoversDirectTransportOrPlugins => false;
}
public sealed partial class AiAuthoringWorkspace
{
    private AiWorkspaceMeasurementLimits? measurementLimits;
    private int measurementAttempts;
    private bool measurementClosed;
    public void BeginMeasuredObservation(ObservationIdentity identity, AiWorkspaceMeasurementLimits limits)
    {
        Active(); ArgumentNullException.ThrowIfNull(limits);
        if (measurementLimits is not null || connectionDiagnostic is not null || draining)
            throw new InvalidOperationException("ai_measurement_boundary");
        BeginObservation(identity); measurementLimits = limits;
    }
    public AiMeasurementBudgetStatus? MeasurementBudget { get { Verify(); return measurementLimits is null ? null :
        new(measurementLimits.Fingerprint, measurementLimits.MaximumAttempts, measurementAttempts, measurementClosed); } }
    private void ChargeMeasurementAttempt()
    {
        if (measurementLimits is null) return;
        if (measurementClosed) throw new EditRejectedException("ai_measurement_closed");
        if (profile is null || !measurementLimits.Matches(profile)) throw new EditRejectedException("ai_measurement_recipient_mismatch");
        if (measurementAttempts == measurementLimits.MaximumAttempts) throw new EditRejectedException("ai_measurement_attempt_budget");
        // Conservative: charge before original approval/dispatch; invalid/rejected/failed attempts are NOT refunded.
        // Reconfiguration/diagnostic replacement never resets this workspace-wide counter.
        measurementAttempts++;
    }
    public AiWorkspaceObservationRecord SealMeasuredObservation()
    {
        Verify(); if (measurementLimits is null) throw new InvalidOperationException("ai_measurement_absent");
        measurementClosed = true; // Stop NEW workspace sends, never cancel/drain existing transport or close the application.
        return SealObservation();
    }
}
