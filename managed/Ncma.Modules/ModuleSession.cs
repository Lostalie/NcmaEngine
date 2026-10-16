namespace Ncma.Modules;

/// <summary>
/// Owner-thread bounded authoring proposal inbox. Pump/TryTake never wait for adapter or cancellation
/// callbacks. Host must keep pumping until TryClose succeeds before disposing the adapter/transport.
/// This is lifecycle isolation for trusted code, NOT a security or CPU/memory sandbox.
/// </summary>
public sealed class ModuleSession
{
    public const int MaximumOutstanding = 16;
    public const int MaximumLifetimeRequests = 4096;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly ModuleCatalog catalog;
    private readonly IModuleAdapter adapter;
    private readonly TimeProvider time;
    private readonly int concurrency;
    private readonly Dictionary<Guid, Entry> entries = new();
    private readonly HashSet<Guid> issued = new();
    private readonly Guid sessionId = Guid.NewGuid();
    private ModuleContext context;
    private long epoch = 1;
    private bool stopped;
    private bool pumping;

    private sealed class Entry(ModuleRequest request, long started)
    {
        internal readonly ModuleRequest Request = request;
        internal readonly long Started = started;
        internal CancellationTokenSource? Cancellation;
        internal Task<ModuleReply>? Work;
        internal Task? CancellationWork;
        internal ModuleOutcome? Outcome;
        internal bool Delivered;
    }

    public ModuleSession(ModuleCatalog catalog, IModuleAdapter adapter, ModuleContext context,
        int maximumConcurrency = 2, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        if (maximumConcurrency is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        this.catalog = catalog;
        this.adapter = adapter;
        this.context = context;
        concurrency = maximumConcurrency;
        time = timeProvider ?? TimeProvider.System;
    }

    public int RetainedCount { get { CheckOwner(); return entries.Count; } }
    public int ActiveCount { get { CheckOwner(); return entries.Values.Count(e => e.Work is not null); } }

    public ModuleAdmission Submit(Guid requestId, string operationName, ReadOnlySpan<byte> observation)
    {
        CheckOwner();
        if (stopped) return ModuleAdmission.Stopped;
        if (requestId == Guid.Empty) throw new ArgumentException("Empty request identity.", nameof(requestId));
        if (issued.Contains(requestId)) return ModuleAdmission.Duplicate;
        if (issued.Count == MaximumLifetimeRequests) return ModuleAdmission.SessionBudget;
        if (entries.Count == MaximumOutstanding) return ModuleAdmission.Backpressure;
        var operation = catalog.Get(operationName);
        if (observation.Length > operation.MaximumInputBytes) throw new ArgumentOutOfRangeException(nameof(observation));
        var payload = new ModulePayload(observation);
        var request = new ModuleRequest(requestId, sessionId, epoch, context, operation, payload);
        entries.Add(requestId, new Entry(request, time.GetTimestamp()));
        issued.Add(requestId);
        return ModuleAdmission.Accepted;
    }

    public bool Cancel(Guid requestId)
    {
        CheckOwner();
        if (!entries.TryGetValue(requestId, out var entry) || entry.Delivered) return false;
        SetTerminal(entry, ModuleOutcomeStatus.Cancelled);
        return true;
    }

    /// <summary>Call on document/World replacement, revision change or permission revocation.</summary>
    public void Invalidate(ModuleContext current)
    {
        CheckOwner();
        ArgumentNullException.ThrowIfNull(current);
        current.Validate();
        epoch = checked(epoch + 1);
        context = current;
        foreach (var entry in entries.Values) SetTerminal(entry, ModuleOutcomeStatus.Stale);
    }

    public void Stop()
    {
        CheckOwner();
        stopped = true;
        foreach (var entry in entries.Values) SetTerminal(entry, ModuleOutcomeStatus.Cancelled);
    }

    public void Pump(ModuleContext current)
    {
        CheckOwner();
        if (current != context) Invalidate(current);
        pumping = true;
        try
        {
            foreach (var entry in entries.Values)
            {
                if (!entry.Delivered && (entry.Outcome is null ||
                    entry.Outcome.Status == ModuleOutcomeStatus.NeedsDomainValidation) &&
                    time.GetElapsedTime(entry.Started).TotalMilliseconds >=
                    entry.Request.Operation.TimeoutMilliseconds)
                    SetTerminal(entry, ModuleOutcomeStatus.TimedOut);
                if (entry.Work is { IsCompleted: true })
                {
                    // Observe failures even when the result was cancelled, expired or superseded.
                    try
                    {
                        var reply = entry.Work.GetAwaiter().GetResult();
                        entry.Outcome ??= ValidateReply(entry.Request, reply);
                    }
                    catch (Exception) { entry.Outcome ??= Outcome(entry, ModuleOutcomeStatus.AdapterFault); }
                    if (entry.CancellationWork is null || entry.CancellationWork.IsCompleted)
                    {
                        // Cancellation callbacks can throw. They are fault-isolated, never retried.
                        _ = entry.CancellationWork?.Exception;
                        entry.Cancellation!.Dispose();
                        entry.Cancellation = null;
                        entry.Work = null;
                        entry.CancellationWork = null;
                    }
                }
            }
            foreach (var id in entries.Where(p => p.Value.Delivered && p.Value.Work is null)
                .Select(p => p.Key).ToArray()) entries.Remove(id);
            var active = entries.Values.Count(e => e.Work is not null);
            foreach (var entry in entries.Values)
            {
                if (stopped || active == concurrency) break;
                if (entry.Work is not null || entry.Outcome is not null) continue;
                entry.Cancellation = new CancellationTokenSource();
                var token = entry.Cancellation.Token;
                // Even synchronous code before an adapter's first await is off the owner thread.
                entry.Work = Task.Run(async () =>
                {
                    // Compute on dispatch, not when queued onto the ThreadPool. A supplied
                    // TimeProvider must support concurrent monotonic timestamp reads.
                    var remaining = TimeSpan.FromMilliseconds(entry.Request.Operation.TimeoutMilliseconds) -
                        time.GetElapsedTime(entry.Started);
                    if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();
                    token.ThrowIfCancellationRequested();
                    return await adapter.InvokeAsync(entry.Request, remaining, token).ConfigureAwait(false);
                });
                active++;
            }
        }
        finally { pumping = false; }
    }

    public bool TryTake(Guid requestId, ModuleContext current, out ModuleOutcome? outcome)
    {
        Pump(current);
        outcome = null;
        if (!entries.TryGetValue(requestId, out var entry) || entry.Delivered || entry.Outcome is null) return false;
        entry.Delivered = true;
        outcome = entry.Outcome;
        if (entry.Work is null) entries.Remove(requestId);
        return true;
    }

    /// <summary>Explicit discard after Stop, not an implicit wait or force-abort.</summary>
    public bool TryClose()
    {
        CheckOwner();
        if (!stopped) throw new InvalidOperationException("Stop the module session before closing.");
        Pump(context);
        foreach (var entry in entries.Values) entry.Delivered = true;
        Pump(context);
        return entries.Count == 0;
    }

    private void SetTerminal(Entry entry, ModuleOutcomeStatus status)
    {
        if (!entry.Delivered) entry.Outcome = Outcome(entry, status);
        if (entry.Work is not null && entry.CancellationWork is null)
        {
            var cancellation = entry.Cancellation!;
            // Cancel invokes adapter callbacks: never execute them synchronously on the owner.
            entry.CancellationWork = Task.Run(() => cancellation.Cancel());
        }
    }

    private static ModuleOutcome Outcome(Entry entry, ModuleOutcomeStatus status) =>
        new(entry.Request.RequestId, entry.Request.Fingerprint, status, null);

    private static ModuleOutcome ValidateReply(ModuleRequest request, ModuleReply? reply)
    {
        var valid = reply is not null && reply.Version == ModuleRequest.ContractVersion &&
            reply.RequestId == request.RequestId && reply.SessionId == request.SessionId &&
            reply.Epoch == request.Epoch && reply.Fingerprint == request.Fingerprint &&
            reply.OutputSchema == request.Operation.OutputSchema && Enum.IsDefined(reply.Status) &&
            (reply.Status == ModuleReplyStatus.Proposal ? reply.Proposal is not null &&
                reply.Proposal.Length <= request.Operation.MaximumOutputBytes : reply.Proposal is null);
        var status = !valid ? ModuleOutcomeStatus.InvalidReply : reply!.Status switch
        {
            ModuleReplyStatus.Proposal => ModuleOutcomeStatus.NeedsDomainValidation,
            ModuleReplyStatus.Unavailable => ModuleOutcomeStatus.Unavailable,
            _ => ModuleOutcomeStatus.Rejected
        };
        return new(request.RequestId, request.Fingerprint, status,
            status == ModuleOutcomeStatus.NeedsDomainValidation ? reply!.Proposal : null);
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != owner || pumping)
            throw new InvalidOperationException("Module session requires its non-reentrant owner thread.");
    }
}
