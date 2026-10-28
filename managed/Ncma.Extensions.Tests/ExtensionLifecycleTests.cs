using Ncma.Application;
using Ncma.Extensions;
using Ncma.Extensions.Hosting;

internal static class ExtensionLifecycleTests
{
    private static readonly ExtensionVersion Version = new(1, 0, 0);
    private static ExtensionDescriptor E(string id, params string[] deps) =>
        new(id, Version, Version, deps.Select(d => new ExtensionDependency(d, Version, Version)).ToArray());
    private static void Check(bool value) { if (!value) throw new Exception("Extension lifecycle assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidOperationException or ArgumentException or AggregateException) { return; }
        throw new Exception("Expected lifecycle rejection.");
    }
    private static ExtensionRegistration R(ExtensionDescriptor d, Fake value) => new(d, c => c.Attach(value));
    private static ExtensionApplicationService Host(params ExtensionRegistration[] registrations) =>
        new(ExtensionPlan.Compile(Version, registrations.Select(r => r.Descriptor).ToArray()), registrations);
    private sealed class Fake : IManagedExtension
    {
        internal int Starts, Closes;
        internal ExtensionAccess? Access;
        internal Action? OnStart, OnClose;
        public void Start(ExtensionAccess access) { Access = access; Starts++; OnStart?.Invoke(); }
        public void Dispose() { Closes++; OnClose?.Invoke(); }
    }
    private sealed class Service(Action start, Action close) : IApplicationService
    { public void Start() => start(); public void Dispose() => close(); }

    internal static int Run() => CaseHarness.Run("Extension lifecycle", [
        ("dependency order and group-wide access revocation", () => {
            var calls = new List<string>(); var a = new Fake(); var b = new Fake();
            a.OnStart = () => { calls.Add("start-a"); Reject(() => a.Access!.RequireActive()); };
            b.OnStart = () => { calls.Add("start-b"); Reject(() => a.Access!.RequireActive()); };
            b.OnClose = () => { calls.Add("close-b"); Reject(() => a.Access!.RequireActive()); Reject(() => b.Access!.RequireActive()); };
            a.OnClose = () => calls.Add("close-a");
            using var host = Host(R(E("b", "a"), b), R(E("a"), a));
            host.Start(); var access = host.GetAccess("a"); access.RequireActive();
            Check(ReferenceEquals(access, a.Access)); host.Dispose(); host.Dispose();
            Check(calls.SequenceEqual(new[] { "start-a", "start-b", "close-b", "close-a" }));
            Check(host.Status.State == ExtensionGroupState.Closed && host.Status.RetainedServices == 0);
            Reject(access.RequireActive);
        }),
        ("exact plan and copied registration set before factories", () => {
            var d = E("a"); var plan = ExtensionPlan.Compile(Version, d); int calls = 0;
            Reject(() => new ExtensionApplicationService(plan, new ExtensionRegistration(E("a"), _ => calls++)));
            Reject(() => new ExtensionApplicationService(plan));
            var fake = new Fake(); var registration = new ExtensionRegistration(d, c => { calls++; c.Attach(fake); });
            var input = new[] { registration }; using var host = new ExtensionApplicationService(plan, input);
            input[0] = new(E("other"), _ => throw new Exception("unexpected factory"));
            Check(calls == 0); host.Start(); Check(calls == 1 && fake.Starts == 1);
        }),
        ("attached factory failure owns partial instance", () => {
            var a = new Fake(); var b = new Fake(); int later = 0;
            using var host = Host(R(E("a"), a), new(E("b", "a"), c => { c.Attach(b); throw new InvalidOperationException("private factory detail"); }),
                new(E("c", "b"), _ => later++));
            Reject(host.Start);
            Check(a.Starts == 1 && a.Closes == 1 && b.Starts == 0 && b.Closes == 1 && later == 0);
            Check(host.Status.StartupFailure == new ExtensionFailure("extension_start_failed", "b", nameof(InvalidOperationException)));
            Check(host.Status.RetainedServices == 0 && !host.Status.CloseRetryRequired);
            Reject(() => a.Access!.RequireActive()); Reject(host.Start);
        }),
        ("unattached factory has no fabricated ownership", () => {
            using var missing = Host(new ExtensionRegistration(E("a"), _ => { }));
            Reject(missing.Start); Check(missing.Status.RetainedServices == 0);
            var a = new Fake(); using var throwing = Host(R(E("a"), a), new(E("b", "a"), _ => throw new InvalidOperationException()));
            Reject(throwing.Start); Check(a.Closes == 1 && throwing.Status.RetainedServices == 0);
        }),
        ("startup cleanup failure is not automatically retried by outer lifetime", () => {
            int externalClosed = 0; bool failClose = true; var a = new Fake();
            var b = new Fake { OnStart = () => throw new InvalidOperationException("start"),
                OnClose = () => { if (failClose) throw new InvalidOperationException("close"); } };
            using var group = Host(R(E("a"), a), R(E("b", "a"), b));
            using var outer = new ApplicationLifetime();
            Reject(() => outer.Start([new Service(() => { }, () => externalClosed++), group]));
            Check(b.Closes == 1 && a.Closes == 0 && externalClosed == 0 && outer.RetainedServiceCount == 2);
            Check(group.Status.RetainedServices == 2 && group.Status.RetainedExtensionId == "b" && group.Status.CloseRetryRequired);
            Reject(outer.Dispose); Reject(group.Dispose); Check(b.Closes == 1);
            failClose = false; group.RetryClose(); outer.Dispose();
            Check(b.Closes == 2 && a.Closes == 1 && externalClosed == 1);
            Check(group.Status.StartupFailure is not null && group.Status.CloseFailure is not null);
            Check(group.Status.State == ExtensionGroupState.Closed && outer.RetainedServiceCount == 0);
        }),
        ("normal close failure retains dependencies until explicit retry", () => {
            var a = new Fake(); bool fail = true; var b = new Fake { OnClose = () => { if (fail) throw new InvalidOperationException(); } };
            using var host = Host(R(E("a"), a), R(E("b", "a"), b)); host.Start();
            var token = host.GetAccess("a"); Reject(host.Dispose); Reject(token.RequireActive);
            Check(a.Closes == 0 && b.Closes == 1 && host.Status.CloseRetryRequired);
            Reject(host.Dispose); Check(b.Closes == 1);
            fail = false; host.RetryClose(); Check(a.Closes == 1 && b.Closes == 2);
            Reject(host.RetryClose); Reject(token.RequireActive);
        }),
        ("swallowed factory reentry still rejects startup", () => {
            ExtensionApplicationService? host = null; var fake = new Fake();
            host = Host(new ExtensionRegistration(E("a"), c => { c.Attach(fake); Reject(() => host!.Dispose()); }));
            using (host) { Reject(host.Start); Check(fake.Starts == 0 && fake.Closes == 1); }
        }),
        ("swallowed Start reentry still rejects startup", () => {
            ExtensionApplicationService? host = null; var fake = new Fake { OnStart = () => Reject(() => _ = host!.Status) };
            host = Host(R(E("a"), fake));
            using (host) { Reject(host.Start); Check(fake.Starts == 1 && fake.Closes == 1); }
        }),
        ("swallowed Dispose reentry retains ownership", () => {
            ExtensionApplicationService? host = null; bool reenter = true;
            var fake = new Fake { OnClose = () => { if (reenter) Reject(() => host!.Start()); } };
            host = Host(R(E("a"), fake));
            using (host) {
                host.Start(); Reject(host.Dispose); Check(host.Status.RetainedServices == 1);
                reenter = false; host.RetryClose(); Check(fake.Closes == 2);
            }
        }),
        ("owner thread and construction lifetime guards", () => {
            ExtensionConstruction? saved = null; var fake = new Fake();
            using var host = Host(new ExtensionRegistration(E("a"), c => { saved = c; c.Attach(fake); }));
            Check(Task.Run(() => { try { host.Start(); return false; } catch (InvalidOperationException) { return true; } }).Result);
            Check(fake.Starts == 0); host.Start();
            Check(Task.Run(() => { try { host.GetAccess("a").RequireActive(); return false; } catch (InvalidOperationException) { return true; } }).Result);
            var access = host.GetAccess("a");
            Check(Task.Run(() => { try { access.RequireActive(); return false; } catch (InvalidOperationException) { return true; } }).Result);
            Check(Task.Run(() => { try { host.Dispose(); return false; } catch (InvalidOperationException) { return true; } }).Result);
            Reject(() => saved!.Attach(new Fake())); access.RequireActive(); Check(fake.Closes == 0);
        }),
        ("duplicate attachment cannot hide partially owned instance", () => {
            var first = new Fake(); var rejected = new Fake();
            using var host = Host(new ExtensionRegistration(E("a"), c => { c.Attach(first); Reject(() => c.Attach(rejected)); }));
            Reject(host.Start); Check(first.Starts == 0 && first.Closes == 1 && rejected.Closes == 0);
            // Rejected attachment ownership did not transfer; factory/caller still owns it.
            rejected.Dispose();
        }),
        ("one live instance cannot belong to two groups or entries", () => {
            var shared = new Fake(); using var first = Host(R(E("a"), shared)); first.Start();
            using (var second = Host(R(E("a"), shared))) {
                Reject(second.Start); Check(shared.Closes == 0); first.GetAccess("a").RequireActive();
            }
            first.Dispose(); Check(shared.Closes == 1);
            var duplicate = new Fake(); using var same = Host(R(E("a"), duplicate), R(E("b", "a"), duplicate));
            Reject(same.Start); Check(duplicate.Starts == 1 && duplicate.Closes == 1);
        }),
        ("close before Start consumes group without constructing", () => {
            int built = 0; using var host = Host(new ExtensionRegistration(E("a"), _ => built++));
            host.Dispose(); host.Dispose(); Check(built == 0 && host.Status.State == ExtensionGroupState.Closed);
            Reject(host.Start); Reject(host.RetryClose);
        }),
        ("new group does not reactivate old access", () => {
            var descriptor = E("a"); using var old = Host(R(descriptor, new Fake())); old.Start(); var token = old.GetAccess("a"); old.Dispose();
            using var next = Host(R(descriptor, new Fake())); next.Start(); var current = next.GetAccess("a");
            Check(current.SessionId != token.SessionId); current.RequireActive(); Reject(token.RequireActive);
        }),
        ("original outer startup compensation revokes a started group", () => {
            var fake = new Fake(); using var group = Host(R(E("a"), fake)); using var app = new ApplicationLifetime();
            Reject(() => app.Start([group, new Service(() => throw new InvalidOperationException(), () => { })]));
            Check(fake.Starts == 1 && fake.Closes == 1 && app.RetainedServiceCount == 0);
            Reject(() => fake.Access!.RequireActive());
        }),
        ("original lifetime accepts bounded 64-service group", () => {
            var fakes = Enumerable.Range(0, 64).Select(_ => new Fake()).ToArray();
            using var host = Host(fakes.Select((f, i) => R(E("e" + i.ToString("D2")), f)).ToArray());
            host.Start(); Check(host.Status.RetainedServices == 64); host.Dispose();
            Check(fakes.All(f => f.Starts == 1 && f.Closes == 1));
        })
    ]);
}
