using System.Runtime.CompilerServices;
using Ncma.Application;

namespace Ncma.Extensions.Hosting;

public interface IManagedExtension : IDisposable
{
    // Store access in resource facades and check it on every public resource operation.
    // It becomes active only after the ENTIRE group starts; close permanently revokes it.
    void Start(ExtensionAccess access);
}

public sealed class ExtensionRegistration
{
    public ExtensionDescriptor Descriptor { get; }
    internal Action<ExtensionConstruction> Factory { get; }
    public ExtensionRegistration(ExtensionDescriptor descriptor, Action<ExtensionConstruction> factory)
    {
        ArgumentNullException.ThrowIfNull(descriptor); ArgumentNullException.ThrowIfNull(factory);
        Descriptor = descriptor; Factory = factory;
    }
}

public sealed class ExtensionConstruction
{
    private readonly Action<IManagedExtension> attach;
    internal ExtensionConstruction(Action<IManagedExtension> attach) => this.attach = attach;
    // Ownership transfers only on success. Attach BEFORE further fallible initialization.
    // A throwing constructor or an instance rejected here remains the factory's responsibility.
    public void Attach(IManagedExtension instance) => attach(instance);
}

public sealed class ExtensionAccess
{
    private readonly ExtensionApplicationService owner;
    public Guid SessionId { get; }
    public string ExtensionId { get; }
    internal ExtensionAccess(ExtensionApplicationService owner, string id, Guid sessionId)
    { this.owner = owner; ExtensionId = id; SessionId = sessionId; }
    public void RequireActive() => owner.RequireAccess(this);
}

public enum ExtensionGroupState { Created, Starting, Running, Closing, Faulted, Closed }
public sealed record ExtensionFailure(string Code, string ExtensionId, string ExceptionType);
public sealed record ExtensionGroupStatus(ExtensionGroupState State, ApplicationState LifetimeState,
    int RetainedServices, string? RetainedExtensionId, bool CloseRetryRequired,
    ExtensionFailure? StartupFailure, ExtensionFailure? CloseFailure)
{
    // Host-enriched diagnostic: an outer close has begun but its ownership is not
    // released. RetainedServices and CloseRetryRequired describe ONLY this group.
    public bool OuterRetained { get; init; }
}

// One optional IApplicationService owned by the ordinary ApplicationLifetime.
// Its private original ApplicationLifetime orders/retains individual extension services.
// This is a trusted local lifecycle contract, NOT an authority boundary for arbitrary C#.
public sealed class ExtensionApplicationService : IApplicationService
{
    private static readonly ConditionalWeakTable<IManagedExtension, ExtensionApplicationService> Claims = new();
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ApplicationLifetime lifetime = new();
    private readonly Entry[] entries;
    private readonly IApplicationService[] services;
    private readonly Dictionary<string, Entry> byId;
    private readonly Guid session = Guid.NewGuid();
    private ExtensionGroupState state;
    private bool busy, callback, retryRequired;
    private int callbackViolation;
    private ExtensionFailure? startupFailure, closeFailure;
    private string? retainedId;

    public ExtensionApplicationService(ExtensionPlan plan, params ExtensionRegistration[] registrations)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(registrations);
        if (registrations.Length != plan.StartupOrder.Count)
            throw new ExtensionRejectedException("extension_registration_set");
        var copy = (ExtensionRegistration[])registrations.Clone();
        var selected = new Dictionary<string, ExtensionRegistration>(StringComparer.Ordinal);
        foreach (var registration in copy) {
            if (registration is null) throw new ExtensionRejectedException("extension_registration_null");
            if (!selected.TryAdd(registration.Descriptor.Id, registration))
                throw new ExtensionRejectedException("extension_registration_duplicate", registration.Descriptor.Id);
        }
        // Exact immutable descriptor objects, not just matching ID/version claims.
        foreach (var descriptor in plan.StartupOrder)
            if (!selected.TryGetValue(descriptor.Id, out var registration) || !ReferenceEquals(descriptor, registration.Descriptor))
                throw new ExtensionRejectedException("extension_registration_foreign", descriptor.Id);
        entries = plan.StartupOrder.Select(d => new Entry(this, selected[d.Id])).ToArray();
        services = entries.Cast<IApplicationService>().ToArray();
        byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != thread)
            throw new InvalidOperationException("extension_owner_thread_required");
    }
    private void Verify()
    {
        VerifyThread();
        if (busy) {
            if (callback) callbackViolation = 1;
            throw new InvalidOperationException("extension_callback_reentry");
        }
    }
    public ExtensionGroupStatus Status
    {
        get { Verify(); return new(state, lifetime.State, lifetime.RetainedServiceCount, retainedId,
            retryRequired, startupFailure, closeFailure); }
    }
    public ExtensionAccess GetAccess(string extensionId)
    {
        Verify(); ArgumentNullException.ThrowIfNull(extensionId);
        if (state != ExtensionGroupState.Running || !byId.TryGetValue(extensionId, out var entry))
            throw new InvalidOperationException("extension_access_unavailable");
        return entry.Access;
    }
    internal void RequireAccess(ExtensionAccess access)
    {
        VerifyThread();
        if (state != ExtensionGroupState.Running || busy || !byId.TryGetValue(access.ExtensionId, out var entry) ||
            !ReferenceEquals(entry.Access, access)) throw new InvalidOperationException("extension_access_revoked");
    }
    private void Invoke(Action action)
    {
        callbackViolation = 0; callback = true;
        try {
            action();
            if (callbackViolation != 0) throw new InvalidOperationException("extension_callback_reentry");
        }
        finally { callback = false; }
    }
    private void Attach(Entry entry, IManagedExtension instance)
    {
        VerifyThread();
        if (!callback || !entry.FactoryOpen || entry.Instance is not null || instance is null) {
            if (callback) callbackViolation = 1;
            throw new InvalidOperationException("extension_attach_rejected");
        }
        lock (Claims) {
            if (Claims.TryGetValue(instance, out _)) {
                callbackViolation = 1;
                throw new InvalidOperationException("extension_instance_owned");
            }
            Claims.Add(instance, this);
            entry.Instance = instance;
        }
    }
    private static ExtensionFailure Failure(string code, string id, Exception error)
    {
        string type = error.GetType().Name;
        return new(code, id, type.Length <= 256 ? type : type[..256]);
    }
    public void Start()
    {
        Verify();
        if (state != ExtensionGroupState.Created) throw new InvalidOperationException("extension_already_started");
        state = ExtensionGroupState.Starting; busy = true;
        try { lifetime.Start(services); state = ExtensionGroupState.Running; }
        catch {
            state = ExtensionGroupState.Faulted;
            // Original Start already attempted cleanup. An enclosing lifetime's cleanup
            // must NOT automatically retry a failed extension's Dispose.
            retryRequired = lifetime.RetainedServiceCount != 0;
            throw;
        }
        finally { foreach (var entry in entries) entry.DiscardFactory(); busy = false; }
    }
    public void Dispose()
    {
        Verify();
        if (state == ExtensionGroupState.Closed) return;
        if (retryRequired) throw new InvalidOperationException("extension_close_retry_required");
        Close();
    }
    // Explicit trusted host recovery only, not an automatic retry or remote command.
    public void RetryClose()
    {
        Verify();
        if (!retryRequired) throw new InvalidOperationException("extension_close_retry_not_required");
        Close();
    }
    private void Close()
    {
        state = ExtensionGroupState.Closing; busy = true; // Revoke ALL access before first callback.
        foreach (var entry in entries) entry.DiscardFactory();
        try {
            lifetime.Dispose(); retryRequired = false; retainedId = null;
            state = ExtensionGroupState.Closed; // StartupFailure remains available; not a successful start.
        }
        catch { retryRequired = true; state = ExtensionGroupState.Faulted; throw; }
        finally { busy = false; }
    }

    private sealed class Entry : IApplicationService
    {
        private readonly ExtensionApplicationService owner;
        private Action<ExtensionConstruction>? factory;
        private readonly ExtensionConstruction construction;
        internal string Id { get; }
        internal ExtensionAccess Access { get; }
        internal IManagedExtension? Instance;
        internal bool FactoryOpen;
        internal Entry(ExtensionApplicationService owner, ExtensionRegistration registration)
        {
            this.owner = owner; factory = registration.Factory; Id = registration.Descriptor.Id;
            Access = new(owner, Id, owner.session);
            construction = new(instance => owner.Attach(this, instance));
        }
        internal void DiscardFactory() => factory = null;
        public void Start()
        {
            try {
                var create = factory ?? throw new InvalidOperationException("extension_factory_consumed");
                factory = null;
                FactoryOpen = true;
                try { owner.Invoke(() => create(construction)); }
                finally { FactoryOpen = false; }
                if (Instance is null) throw new InvalidOperationException("extension_factory_unattached");
                owner.Invoke(() => Instance.Start(Access));
            }
            catch (Exception error) {
                owner.state = ExtensionGroupState.Faulted; // Before original lifetime compensates.
                owner.startupFailure ??= Failure("extension_start_failed", Id, error);
                throw;
            }
        }
        public void Dispose()
        {
            var instance = Instance;
            if (instance is null) return;
            try {
                owner.Invoke(instance.Dispose);
                lock (Claims) { Claims.Remove(instance); }
                Instance = null;
            }
            catch (Exception error) {
                owner.retainedId = Id;
                owner.closeFailure = Failure("extension_close_failed", Id, error);
                throw; // Instance, claim and dependency services remain owned.
            }
        }
    }
}
