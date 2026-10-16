using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ncma.Modules;

static class Program
{
    private static readonly ModuleContext Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, Guid.Empty, 0);
    private static readonly ModuleOperation Operation = new(Guid.NewGuid(), "1.0", Guid.NewGuid(),
        "ncma.scene.propose", Guid.NewGuid(), Guid.NewGuid());
    private static int checks;

    public static int Main()
    {
        try { RunAll(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void RunAll()
    {
        Run("owned bounded payload and catalog", Contracts);
        Run("background invocation and proposal-only result", Success);
        Run("reply identity and schema rejection", InvalidReplies);
        Run("fault isolation", Faults);
        Run("bounded queue and duplicate identity", Queue);
        Run("timeout and retained hung worker", Timeout);
        Run("cancellation callbacks stay off owner", Cancellation);
        Run("revision/replacement/revocation and late results", Stale);
        Run("out of order and one delivery", OutOfOrder);
        Run("queue deadline prevents dispatch", QueuedDeadline);
        Run("stop and drain lifecycle", Stop);
        Run("owner thread and dependency boundary", Owner);
        Run("bounded lifetime replay protection", Lifetime);
        Run("all exact context fields and generation identity", ContextFields);
        Run("completed replies remain within outstanding budget", CompletedBudget);
        Run("unclaimed proposal expires before delivery", UnclaimedDeadline);
        Run("transport dispatch receives only remaining budget", DispatchBudget);
        Console.WriteLine($"Ncma.Modules: 17 groups, {checks} checks passed (CPU only; no transport/inference/World mutation).");
    }

    private static void Run(string name, Action test) { test(); Console.WriteLine($"PASS {name}"); }
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); checks++; }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { checks++; return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private static ModuleSession Session(IModuleAdapter adapter, Clock? clock = null, int concurrency = 2,
        ModuleOperation? operation = null) => new(new ModuleCatalog([operation ?? Operation]), adapter,
            Context, concurrency, clock);
    private static Guid Submit(ModuleSession session)
    {
        var id = Guid.NewGuid();
        Check(session.Submit(id, Operation.Name, "{}"u8) == ModuleAdmission.Accepted);
        return id;
    }
    private static ModuleReply Reply(ModuleRequest r) => new(1, r.RequestId, r.SessionId, r.Epoch,
        r.Fingerprint, r.Operation.OutputSchema, ModuleReplyStatus.Proposal, new("{\"proposal\":1}"u8));
    private static void Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Test barrier timeout.");
            Thread.Sleep(1);
        }
    }
    private static ModuleOutcome Take(ModuleSession session, Guid id, ModuleContext? current = null)
    {
        ModuleOutcome? result = null;
        Wait(() => session.TryTake(id, current ?? Context, out result));
        return result!;
    }
    private static void Close(ModuleSession session) { session.Stop(); Wait(session.TryClose); }

    private static void Contracts()
    {
        var source = Encoding.UTF8.GetBytes("{\"a\":1}");
        var payload = new ModulePayload(source);
        var hash = payload.Sha256;
        source[5] = (byte)'9';
        var copy = payload.CopyBytes(); copy[5] = (byte)'8';
        Check(Encoding.UTF8.GetString(payload.CopyBytes()) == "{\"a\":1}" && payload.Sha256 == hash);
        Reject<ArgumentOutOfRangeException>(() => new ModulePayload(new byte[65_537]));
        Reject<ArgumentException>(() => new ModulePayload("[]"u8));
        Reject<ArgumentException>(() => new ModulePayload("{\"a\":1,\"a\":2}"u8));
        Reject<ArgumentException>(() => new ModulePayload("{\"x\":{\"a\":1,\"\\u0061\":2}}"u8));
        Reject<JsonException>(() => new ModulePayload("{\"a\":1,}"u8));
        Reject<DecoderFallbackException>(() => new ModulePayload(new byte[] { 123, 34, 97, 34, 58, 34, 255, 34, 125 }));
        Reject<JsonException>(() => new ModulePayload(Encoding.UTF8.GetBytes("{\"a\":" + new string('[', 17) + "0" + new string(']', 17) + "}")));
        Reject<ArgumentException>(() => new ModuleCatalog([Operation, Operation]));
        Reject<ArgumentException>(() => new ModuleCatalog([]));
        Reject<ArgumentException>(() => new ModuleCatalog([Operation with { Name = "../exec" }]));
        Reject<ArgumentException>(() => new ModuleCatalog([Operation with { TimeoutMilliseconds = 0 }]));
        Reject<ArgumentException>(() => new ModuleCatalog([Operation with { InputSchema = Guid.Empty }]));
        Reject<ArgumentException>(() => new ModuleCatalog(Enumerable.Range(0, 65).Select(i => Operation with { Name = $"op{i}" })));
        var adapter = new Adapter(r => Task.FromResult(Reply(r)));
        Reject<ArgumentException>(() => new ModuleSession(new([Operation]), adapter, Context with { Generation = 0 }));
        Reject<ArgumentException>(() => new ModuleSession(new([Operation]), adapter, Context with { ObservationTick = 1 }));
        var session = Session(adapter, operation: Operation with { MaximumInputBytes = 2 });
        Reject<ArgumentException>(() => session.Submit(Guid.NewGuid(), "ncma.shell", "{}"u8));
        Reject<ArgumentOutOfRangeException>(() => session.Submit(Guid.NewGuid(), Operation.Name, "{\"a\":1}"u8));
        Reject<ArgumentException>(() => session.Submit(Guid.Empty, Operation.Name, "{}"u8));
        Check(session.RetainedCount == 0);
        Close(session);
    }

    private static void Success()
    {
        var owner = Environment.CurrentManagedThreadId;
        ModuleRequest? observed = null;
        var session = Session(new Adapter(r =>
        {
            if (Environment.CurrentManagedThreadId == owner) throw new Exception("Adapter ran on owner.");
            observed = r;
            return Task.FromResult(Reply(r));
        }));
        var id = Submit(session);
        var result = Take(session, id);
        Check(observed is not null && observed.Context == Context && observed.Version == 1);
        Check(result.Status == ModuleOutcomeStatus.NeedsDomainValidation && result.Proposal is not null);
        Check(result.Fingerprint == observed!.Fingerprint);
        Check(!session.TryTake(id, Context, out _));
        Check(session.Submit(id, Operation.Name, "{}"u8) == ModuleAdmission.Duplicate);
        Close(session);
    }

    private static void InvalidReplies()
    {
        Func<ModuleReply, ModuleReply>[] changes =
        [r => r with { Version = 2 }, r => r with { RequestId = Guid.NewGuid() },
         r => r with { SessionId = Guid.NewGuid() }, r => r with { Epoch = 99 },
         r => r with { Fingerprint = "forged" }, r => r with { OutputSchema = Guid.NewGuid() },
         r => r with { Status = (ModuleReplyStatus)99 }, r => r with { Proposal = null },
         r => r with { Status = ModuleReplyStatus.Unavailable }];
        foreach (var change in changes)
        {
            var session = Session(new Adapter(r => Task.FromResult(change(Reply(r)))));
            var result = Take(session, Submit(session));
            Check(result.Status == ModuleOutcomeStatus.InvalidReply && result.Proposal is null);
            Close(session);
        }
        var small = Session(new Adapter(r => Task.FromResult(Reply(r))), operation: Operation with { MaximumOutputBytes = 2 });
        Check(Take(small, Submit(small)).Status == ModuleOutcomeStatus.InvalidReply); Close(small);
        foreach (var status in new[] { ModuleReplyStatus.Rejected, ModuleReplyStatus.Unavailable })
        {
            var session = Session(new Adapter(r => Task.FromResult(Reply(r) with { Status = status, Proposal = null })));
            Check(Take(session, Submit(session)).Status == (status == ModuleReplyStatus.Rejected ?
                ModuleOutcomeStatus.Rejected : ModuleOutcomeStatus.Unavailable)); Close(session);
        }
    }

    private static void Faults()
    {
        foreach (var adapter in new IModuleAdapter[] {
            new Adapter(_ => throw new Exception("secret must not enter diagnostics")),
            new Adapter(_ => Task.FromException<ModuleReply>(new Exception("secret"))),
            new Adapter(_ => Task.FromResult<ModuleReply>(null!)) })
        {
            var session = Session(adapter);
            var result = Take(session, Submit(session));
            Check(result.Status is ModuleOutcomeStatus.AdapterFault or ModuleOutcomeStatus.InvalidReply);
            Check(result.Proposal is null && !result.ToString().Contains("secret")); Close(session);
        }
    }

    private static void Queue()
    {
        using var adapter = new Controlled();
        var session = Session(adapter, concurrency: 2);
        try
        {
            var ids = Enumerable.Range(0, 16).Select(_ => Submit(session)).ToArray();
            Check(session.Submit(Guid.NewGuid(), Operation.Name, "{}"u8) == ModuleAdmission.Backpressure);
            session.Pump(Context); Wait(() => adapter.Requests.Count == 2);
            Check(session.ActiveCount == 2 && session.RetainedCount == 16);
            Check(session.Cancel(ids[15]));
            Check(Take(session, ids[15]).Status == ModuleOutcomeStatus.Cancelled);
            Check(session.Submit(ids[15], Operation.Name, "{}"u8) == ModuleAdmission.Duplicate);
        }
        finally { session.Stop(); adapter.ReleaseAll(); Close(session); }
    }

    private static void Timeout()
    {
        var clock = new Clock(); using var adapter = new Controlled();
        var session = Session(adapter, clock, 1);
        try
        {
            var first = Submit(session); session.Pump(Context); Wait(() => adapter.Requests.Count == 1);
            clock.Advance(10_001);
            Check(Take(session, first).Status == ModuleOutcomeStatus.TimedOut);
            var second = Submit(session); session.Pump(Context);
            Check(session.ActiveCount == 1 && adapter.Requests.Count == 1);
            Check(session.RetainedCount == 2); // Cancelled uncooperative worker still owns its slot.
            adapter.Complete(first);
            Wait(() => { session.Pump(Context); return adapter.Requests.Count == 2; });
            adapter.Complete(second);
            Check(Take(session, second).Status == ModuleOutcomeStatus.NeedsDomainValidation);
            Check(!session.TryTake(first, Context, out _));
        }
        finally { session.Stop(); adapter.ReleaseAll(); Close(session); }
    }

    private static void Cancellation()
    {
        using var started = new ManualResetEventSlim(); using var callback = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var session = Session(new TokenAdapter((r, token) =>
        {
            token.Register(() => { callback.Set(); release.Wait(); throw new Exception("callback error"); });
            started.Set(); release.Wait(); return Task.FromResult(Reply(r));
        }), concurrency: 1);
        try
        {
            var id = Submit(session); session.Pump(Context); Check(started.Wait(5000));
            session.Cancel(id); Check(callback.Wait(5000));
            Check(Take(session, id).Status == ModuleOutcomeStatus.Cancelled);
            session.Stop(); Check(!session.TryClose());
            Check(session.ActiveCount == 1);
        }
        finally { release.Set(); Close(session); }
    }

    private static void Stale()
    {
        using var adapter = new Controlled(); var session = Session(adapter);
        try
        {
            var id = Submit(session); session.Pump(Context); Wait(() => adapter.Requests.Count == 1);
            adapter.Complete(id);
            Wait(() => { session.Pump(Context); return session.ActiveCount == 0; });
            Check(Take(session, id, Context with { Revision = 1 }).Status == ModuleOutcomeStatus.Stale);
            var next = Submit(session); session.Pump(Context with { Revision = 1 });
            Wait(() => adapter.Requests.Count == 2);
            session.Invalidate(Context with { Revision = 1 }); // Same-revision permission revocation increments epoch.
            adapter.Complete(next);
            Check(Take(session, next, Context with { Revision = 1 }).Status == ModuleOutcomeStatus.Stale);
            var third = Submit(session); session.Pump(Context with { Revision = 1 });
            Check(Take(session, third, Context with { Generation = 2 }).Status == ModuleOutcomeStatus.Stale);
        }
        finally { session.Stop(); adapter.ReleaseAll(); Close(session); }
    }

    private static void OutOfOrder()
    {
        using var adapter = new Controlled(); var session = Session(adapter);
        try
        {
            var first = Submit(session); var second = Submit(session);
            session.Pump(Context); Wait(() => adapter.Requests.Count == 2);
            adapter.Complete(second); Check(Take(session, second).Status == ModuleOutcomeStatus.NeedsDomainValidation);
            Check(!session.TryTake(first, Context, out _));
            adapter.Complete(first); Check(Take(session, first).Status == ModuleOutcomeStatus.NeedsDomainValidation);
        }
        finally { session.Stop(); adapter.ReleaseAll(); Close(session); }
    }

    private static void QueuedDeadline()
    {
        var clock = new Clock(); var calls = 0;
        var session = Session(new Adapter(r => { Interlocked.Increment(ref calls); return Task.FromResult(Reply(r)); }), clock);
        var id = Submit(session); clock.Advance(10_000);
        Check(Take(session, id).Status == ModuleOutcomeStatus.TimedOut && calls == 0); Close(session);
    }

    private static void Stop()
    {
        using var adapter = new Controlled(); var session = Session(adapter);
        try
        {
            Reject<InvalidOperationException>(() => session.TryClose());
            var id = Submit(session); session.Pump(Context); Wait(() => adapter.Requests.Count == 1);
            session.Stop(); Check(!session.TryClose());
            Check(session.Submit(Guid.NewGuid(), Operation.Name, "{}"u8) == ModuleAdmission.Stopped);
            adapter.Complete(id); Wait(session.TryClose); Check(session.TryClose());
            Check(!session.TryTake(id, Context, out _));
        }
        finally { adapter.ReleaseAll(); Close(session); }
    }

    private static void Owner()
    {
        var session = Session(new Adapter(r => Task.FromResult(Reply(r))));
        var rejected = Task.Run(() =>
        {
            try { session.Pump(Context); return false; } catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        Check(rejected);
        Check(typeof(ModuleSession).Assembly.GetReferencedAssemblies().All(a => !a.Name!.StartsWith("Ncma.") &&
            !a.Name.Contains("Python") && !a.Name.Contains("Grpc")));
        Close(session);
    }

    private static void Lifetime()
    {
        var session = Session(new Adapter(r => Task.FromResult(Reply(r))));
        for (var i = 0; i < ModuleSession.MaximumLifetimeRequests; i++)
        {
            var id = Submit(session); session.Cancel(id);
            Check(Take(session, id).Status == ModuleOutcomeStatus.Cancelled);
        }
        Check(session.Submit(Guid.NewGuid(), Operation.Name, "{}"u8) == ModuleAdmission.SessionBudget);
        Check(session.RetainedCount == 0); Close(session);
    }

    private static void ContextFields()
    {
        ModuleContext[] changes = [Context with { ProjectId = Guid.NewGuid() },
            Context with { HostSessionId = Guid.NewGuid() }, Context with { DocumentId = Guid.NewGuid() },
            Context with { Generation = 2 }, Context with { Revision = 1 },
            Context with { WorldId = Guid.NewGuid() }, Context with { WorldId = Guid.NewGuid(), ObservationTick = 1 }];
        foreach (var current in changes)
        {
            var session = Session(new Adapter(r => Task.FromResult(Reply(r))));
            var id = Submit(session);
            Check(Take(session, id, current).Status == ModuleOutcomeStatus.Stale);
            Close(session);
        }
        var requests = new ConcurrentQueue<ModuleRequest>();
        var bound = Session(new Adapter(r => { requests.Enqueue(r); return Task.FromResult(Reply(r)); }));
        var original = Take(bound, Submit(bound));
        bound.Invalidate(Context);
        var updated = Take(bound, Submit(bound));
        Check(original.Fingerprint != updated.Fingerprint);
        Check(requests.ToArray()[0].Epoch + 1 == requests.ToArray()[1].Epoch);
        Check(requests.ToArray()[0].SessionId == requests.ToArray()[1].SessionId);
        Close(bound);
    }

    private static void CompletedBudget()
    {
        var session = Session(new Adapter(r => Task.FromResult(Reply(r))), concurrency: 4);
        var ids = Enumerable.Range(0, 16).Select(_ => Submit(session)).ToArray();
        for (var i = 0; i < 16; i++)
        {
            session.Pump(Context);
            Thread.Sleep(1);
        }
        Wait(() => { session.Pump(Context); return session.ActiveCount == 0; });
        Check(session.RetainedCount == 16);
        Check(session.Submit(Guid.NewGuid(), Operation.Name, "{}"u8) == ModuleAdmission.Backpressure);
        foreach (var id in ids) Check(Take(session, id).Status == ModuleOutcomeStatus.NeedsDomainValidation);
        Check(session.RetainedCount == 0); Close(session);
    }

    private static void UnclaimedDeadline()
    {
        var clock = new Clock();
        var session = Session(new Adapter(r => Task.FromResult(Reply(r))), clock);
        var id = Submit(session); session.Pump(Context);
        Wait(() => { session.Pump(Context); return session.ActiveCount == 0; });
        clock.Advance(10_000);
        var result = Take(session, id);
        Check(result.Status == ModuleOutcomeStatus.TimedOut && result.Proposal is null);
        Close(session);
    }

    private static void DispatchBudget()
    {
        var clock = new Clock(); using var adapter = new Controlled();
        var session = Session(adapter, clock, 1);
        try
        {
            var first = Submit(session); var second = Submit(session);
            session.Pump(Context); Wait(() => adapter.Requests.Count == 1);
            Check(adapter.Requests[first].Budget == TimeSpan.FromMilliseconds(10_000));
            clock.Advance(6000); adapter.Complete(first);
            Check(Take(session, first).Status == ModuleOutcomeStatus.NeedsDomainValidation);
            Wait(() => { session.Pump(Context); return adapter.Requests.Count == 2; });
            Check(adapter.Requests[second].Budget == TimeSpan.FromMilliseconds(4000));
            clock.Advance(4000);
            Check(Take(session, second).Status == ModuleOutcomeStatus.TimedOut);
            adapter.Complete(second);
        }
        finally { session.Stop(); adapter.ReleaseAll(); Close(session); }
    }

    private sealed class Adapter(Func<ModuleRequest, Task<ModuleReply>> invoke) : IModuleAdapter
    { public Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan remainingBudget, CancellationToken cancellation) => invoke(request); }
    private sealed class TokenAdapter(Func<ModuleRequest, CancellationToken, Task<ModuleReply>> invoke) : IModuleAdapter
    { public Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan remainingBudget, CancellationToken cancellation) => invoke(request, cancellation); }
    private sealed class Clock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref milliseconds);
        internal void Advance(long value) => Interlocked.Add(ref milliseconds, value);
    }
    private sealed class Controlled : IModuleAdapter, IDisposable
    {
        internal ConcurrentDictionary<Guid, (ModuleRequest Request, TaskCompletionSource<ModuleReply> Completion, TimeSpan Budget)> Requests { get; } = new();
        private volatile bool released;
        public Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan remainingBudget, CancellationToken cancellation)
        {
            var source = new TaskCompletionSource<ModuleReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests[request.RequestId] = (request, source, remainingBudget);
            if (released) source.TrySetResult(Reply(request));
            return source.Task;
        }
        internal void Complete(Guid id) { var item = Requests[id]; item.Completion.TrySetResult(Reply(item.Request)); }
        internal void ReleaseAll() { released = true; foreach (var id in Requests.Keys) Complete(id); }
        public void Dispose() => ReleaseAll();
    }
}
