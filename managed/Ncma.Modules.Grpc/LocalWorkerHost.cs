using System.Security.Cryptography;
using System.Text.Json;

namespace Ncma.Modules.Grpc;

public enum LocalWorkerState { Stopped, Starting, Ready, Stopping, FailedRetained, Closed }
public sealed record WorkerLaunchReview(Guid Id, ModuleContext Context, string InstallationRoot, int Files, long Bytes,
    string InstallationFingerprint, int Attempt, string Fingerprint);
public sealed record WorkerRecoveryReview(Guid Id, Guid Instance, int ProcessId, bool Terminate, string Fingerprint);
public sealed record LocalWorkerSnapshot(LocalWorkerState State, int Attempts, Guid Instance, int ProcessId,
    string Code, string Fault, int? ExitCode, long StdoutBytes, long StderrBytes, bool ResourcesRetained);

/// <summary>
/// Explicit trusted local human launch/recovery boundary; do not register Approve methods as
/// Agent capabilities. One owner thread, one process/session, three lifetime launch attempts.
/// Installation inspection and approval are off-frame operations. Pump never waits for IO/RPC.
/// No model connection, World reference, process-name kill, automatic restart or request replay.
/// </summary>
public sealed class LocalWorkerHost : IDisposable
{
    public const int MaximumAttempts = 3;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly WorkerInstallation installation;
    private readonly Func<bool> authorized;
    private readonly TimeProvider time;
    private ModuleContext context;
    private ModuleSession? session;
    private WorkerAttempt? attempt;
    private Task? start, cancellationWork;
    private Task<bool>? close;
    private CancellationTokenSource? cancellation;
    private WorkerLaunchReview? launchReview;
    private WorkerRecoveryReview? recoveryReview;
    private long reviewTime;
    private int attempts;
    private bool callback;
    private string code = "stopped";
    private string fault = "";
    private readonly List<LocalWorkerSnapshot> history = [];
    private LocalWorkerState state;

    public LocalWorkerHost(WorkerInstallation installation, ModuleContext context, Func<bool> authorized, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(installation); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(authorized);
        Validate(context);
        this.installation = installation; this.context = context; this.authorized = authorized; time = timeProvider ?? TimeProvider.System;
    }

    private static void Validate(ModuleContext value)
    {
        if (value.ProjectId == Guid.Empty || value.HostSessionId == Guid.Empty || value.DocumentId == Guid.Empty || value.Generation <= 0 ||
            value.Revision < 0 || value.ObservationTick < 0 || (value.WorldId == Guid.Empty && value.ObservationTick != 0))
            throw new ArgumentException("Invalid worker host context.");
    }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != owner || callback) throw new InvalidOperationException("Worker owner/nonreentry boundary.");
        ObjectDisposedException.ThrowIf(state == LocalWorkerState.Closed, this);
    }
    private bool Allowed()
    {
        callback = true;
        try
        {
            if (authorized()) return true;
            launchReview = null; recoveryReview = null; return false;
        }
        catch { launchReview = null; recoveryReview = null; return false; }
        finally { callback = false; }
    }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private bool Fresh() => time.GetElapsedTime(reviewTime) < TimeSpan.FromSeconds(60);

    public LocalWorkerSnapshot Capture()
    {
        Verify();
        // Never inspect a partially prepared Process object from the owner while StartAsync runs.
        var prepared = start?.IsCompleted == true;
        return new(state, attempts, attempt?.Instance ?? Guid.Empty, prepared ? attempt!.ProcessId : 0, code, fault,
            prepared ? attempt!.ExitCode : null, prepared ? attempt!.StdoutBytes : 0, prepared ? attempt!.StderrBytes : 0,
            attempt is not null && (close is { IsCompleted: false } || !attempt.Released));
    }
    public LocalWorkerSnapshot[] CopyHistory() { Verify(); return history.ToArray(); }

    public WorkerLaunchReview ReviewStart(ModuleContext current)
    {
        Verify(); Validate(current);
        if (current != context || !Allowed() || state != LocalWorkerState.Stopped || attempt is { Released: false } || attempts >= MaximumAttempts)
            throw new InvalidOperationException("Worker launch is not currently authorized or available.");
        var review = new WorkerLaunchReview(Guid.NewGuid(), current, installation.Root, installation.FileCount,
            installation.Bytes, installation.Fingerprint, attempts + 1, "");
        launchReview = review with { Fingerprint = Hash(review) }; recoveryReview = null;
        reviewTime = time.GetTimestamp();
        return launchReview;
    }

    public void ApproveStart(WorkerLaunchReview displayed, string fingerprint, bool humanReviewed, ModuleContext current)
    {
        Verify();
        if (!humanReviewed || displayed != launchReview || displayed.Fingerprint != fingerprint || !Fresh() ||
            current != context || displayed.Context != context || !Allowed() || state != LocalWorkerState.Stopped ||
            attempts >= MaximumAttempts || displayed.Attempt != attempts + 1 || attempt is { Released: false })
        { launchReview = null; throw new InvalidOperationException("Exact local worker launch review required."); }
        launchReview = null; recoveryReview = null;
        if (attempt is not null) history.Add(Capture());
        attempt?.DisposeReleased(); cancellation?.Dispose();
        cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        attempt = new WorkerAttempt(installation); attempts++;
        var owned = attempt; var token = cancellation.Token;
        start = Task.Run(() => owned.StartAsync(token)); close = null; cancellationWork = null; session = null;
        state = LocalWorkerState.Starting; code = "starting"; fault = "";
    }

    /// <summary>Call on permission revocation even if the document revision did not change.</summary>
    public void Revoke() { Verify(); BeginStop("revoked"); }
    public void Stop() { Verify(); BeginStop("stopping"); }

    private void BeginStop(string reason)
    {
        launchReview = null; recoveryReview = null;
        if (state is LocalWorkerState.Stopped or LocalWorkerState.FailedRetained) return;
        code = reason;
        if (reason is not ("stopping" or "revoked" or "context_changed")) fault = reason;
        session?.Stop();
        if (cancellation is not null && cancellationWork is null) cancellationWork = cancellation.CancelAsync();
        if (start?.IsCompleted == true) attempt?.Adapter?.Stop();
        state = LocalWorkerState.Stopping;
    }

    public void Pump(ModuleContext current)
    {
        Verify(); Validate(current);
        var permitted = Allowed();
        if (current != context || !permitted)
        {
            BeginStop(permitted ? "context_changed" : "revoked");
            context = current;
        }
        if (state == LocalWorkerState.Starting && start!.IsCompleted)
        {
            _ = start.Exception;
            if (start.IsFaulted || attempt!.Failure.Length != 0 || attempt.Exited || attempt.OutputFault)
                BeginStop(attempt!.Failure.Length == 0 ? "startup_failed" : attempt.Failure);
            else
            {
                session = new ModuleSession(new ModuleCatalog([TextStatisticsModule.Operation]), attempt.Adapter!, context, maximumConcurrency: 4);
                state = LocalWorkerState.Ready; code = "ready";
            }
        }
        if (state == LocalWorkerState.Ready && (attempt!.Exited || attempt.OutputFault || attempt.Adapter!.ConnectionLost))
            BeginStop(attempt.Exited ? "worker_exited" : attempt.OutputFault ? "worker_output_fault" : "worker_connection_lost");
        if (state == LocalWorkerState.Ready) session!.Pump(context);
        if (state == LocalWorkerState.Stopping)
        {
            if (start is { IsCompleted: false } || cancellationWork is { IsCompleted: false }) return;
            _ = cancellationWork?.Exception;
            _ = start?.Exception;
            if (session is not null && !session.TryClose()) return;
            session = null;
            close ??= Task.Run(() => attempt!.CloseAsync(false));
            if (!close.IsCompleted) return;
            _ = close.Exception;
            if (close.IsCompletedSuccessfully && close.Result)
            {
                state = LocalWorkerState.Stopped;
                if (code == "stopping") code = "stopped";
            }
            else { state = LocalWorkerState.FailedRetained; code = attempt!.Failure.Length == 0 ? "close_failed_retained" : attempt.Failure; if (fault.Length == 0) fault = code; }
        }
    }

    public ModuleAdmission Submit(Guid id, ReadOnlySpan<byte> observation, ModuleContext current)
    {
        Pump(current);
        return state == LocalWorkerState.Ready ? session!.Submit(id, TextStatisticsModule.Operation.Name, observation) : ModuleAdmission.Stopped;
    }
    public bool TryTake(Guid id, ModuleContext current, out ModuleOutcome? result)
    {
        Pump(current); result = null;
        if (state != LocalWorkerState.Ready) return false;
        var found = session!.TryTake(id, current, out result);
        // Process/output observation is checked again at delivery, not only at submission.
        if (attempt!.Exited || attempt.OutputFault || attempt.Adapter!.ConnectionLost)
        { result = null; BeginStop("worker_lost_before_delivery"); return false; }
        return found;
    }
    public bool Cancel(Guid id) { Verify(); return state == LocalWorkerState.Ready && session!.Cancel(id); }

    public WorkerRecoveryReview ReviewRecovery(bool terminate)
    {
        Verify();
        if (!Allowed() || state != LocalWorkerState.FailedRetained || attempt is null)
            throw new InvalidOperationException("No retained worker eligible for local recovery.");
        var review = new WorkerRecoveryReview(Guid.NewGuid(), attempt.Instance, attempt.ProcessId, terminate, "");
        recoveryReview = review with { Fingerprint = Hash(new { review, installation.Fingerprint, context }) };
        launchReview = null; reviewTime = time.GetTimestamp(); return recoveryReview;
    }

    public void ApproveRecovery(WorkerRecoveryReview displayed, string fingerprint, bool humanReviewed)
    {
        Verify();
        if (!humanReviewed || displayed != recoveryReview || fingerprint != displayed.Fingerprint || !Fresh() || !Allowed() ||
            state != LocalWorkerState.FailedRetained || attempt is null || displayed.Instance != attempt.Instance || displayed.ProcessId != attempt.ProcessId)
        { recoveryReview = null; throw new InvalidOperationException("Exact retained-process recovery review required."); }
        recoveryReview = null;
        var owned = attempt;
        close = Task.Run(() => owned.CloseAsync(displayed.Terminate));
        state = LocalWorkerState.Stopping; code = displayed.Terminate ? "approved_termination" : "approved_close_retry";
    }

    public void Dispose()
    {
        if (state == LocalWorkerState.Closed) return;
        Verify();
        if (state != LocalWorkerState.Stopped || attempt is { Released: false })
            throw new InvalidOperationException("Stop and pump, or explicitly recover the retained worker before disposing.");
        attempt?.DisposeReleased(); cancellation?.Dispose(); launchReview = null; recoveryReview = null;
        state = LocalWorkerState.Closed;
    }
}
