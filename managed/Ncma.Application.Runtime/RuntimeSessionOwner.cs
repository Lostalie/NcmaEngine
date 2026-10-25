using Ncma.Gameplay;
using Ncma.Scene;
using Ncma.Scripting;

namespace Ncma.Application.Runtime;

// Owns runtime lifecycle and catalog leases. No authoring document or editable World is retained.
public sealed class RuntimeSessionOwner : IDisposable
{
    private readonly bool _ownsCatalog;
    private PlaySession? _play;
    private IDisposable? _lease;
    private IDisposable? _composition;
    private PlayStatus? _lastStartFailure;
    private bool _busy, _disposed;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    public ScriptCatalogService Catalog { get; }
    public PlaySession? Play { get { Verify(); return _play; } }
    public PlayStatus? LastStartFailure { get { Verify(); return _lastStartFailure; } }
    public RuntimeSessionOwner(ScriptCatalogService? catalog = null)
    {
        Catalog = catalog ?? new(); _ownsCatalog = catalog is null;
    }
    private void Verify()
    {
        VerifyThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Runtime callback reentry is forbidden.");
    }
    private void VerifyThread()
    { if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Runtime owner thread required."); }
    private static BehaviourBindingData[] CopyBindings(ISceneContent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Objects.SelectMany(o => o.Behaviours).Select(b =>
            b with { Exports = (ExportData[])b.Exports.Clone() }).ToArray();
    }
    public int LoadGameplay(string path, ISceneContent source)
    {
        Verify(); if (_play is not null) throw new InvalidOperationException("Stop Play before loading gameplay.");
        var bindings = CopyBindings(source); _busy = true;
        try { using var candidate = Catalog.LoadCandidate(path); int count = candidate.Count;
            foreach (var binding in bindings) _ = candidate.Instantiate(binding);
            Catalog.CommitCandidate(candidate); return count; }
        finally { _busy = false; }
    }
    public int LoadGameplay(string path, RuntimeSceneInstance scene)
    {
        Verify(); ArgumentNullException.ThrowIfNull(scene); scene.VerifyAccess();
        using var read = scene.World.ReadOnly();
        return LoadGameplay(path, scene.CaptureSnapshot());
    }
    public PlaySession StartPlay(RuntimeSceneInstance scene, Func<RuntimeSceneInstance, PlaySession>? factory = null,
        Func<PlaySession, IDisposable?>? compose = null)
    {
        Verify(); if (_play is not null) throw new InvalidOperationException("Stop current Play first.");
        ArgumentNullException.ThrowIfNull(scene); scene.VerifyAccess();
        PlaySession? candidate = null; IDisposable? lease = null; IDisposable? composition = null; _busy = true; _lastStartFailure = null;
        try
        {
            // Resolve all bindings before factories/composition or any Start callback. No
            // script constructors/setters run during this metadata-only closure check.
            var scripts = Catalog.ResolveBindings(scene);
            using (scene.World.ReadOnly())
                candidate = factory is null ? new PlaySession(scene) : factory(scene);
            if (candidate is null || !ReferenceEquals(candidate.Scene, scene)) throw new ArgumentException("Foreign Play scene.");
            using (scene.World.ReadOnly()) composition = compose?.Invoke(candidate);
            Catalog.RequireCurrent(scripts);
            lease = Catalog.AcquireLease(); candidate.Start(Catalog.Instantiate);
            Catalog.RequireCurrent(scripts); // Trusted lifecycle callbacks must not swap the resolved catalog.
            _composition = composition; _lease = lease; _play = candidate; return candidate;
        }
        catch (Exception original) {
            if (candidate is not null) _lastStartFailure = candidate.Status;
            try { candidate?.Stop(); composition?.Dispose(); candidate?.Dispose(); lease?.Dispose(); }
            catch (Exception close) { _play = candidate; _lease = lease; _composition = composition;
                throw new AggregateException("Failed startup resources remain owned; retry StopPlay before unload.", original, close); }
            throw;
        }
        finally { _busy = false; }
    }
    public int ReloadGameplay(string path, ISceneContent? stoppedSource = null)
    {
        Verify();
        var bindings = _play is null ? CopyBindings(stoppedSource ?? throw new ArgumentNullException(nameof(stoppedSource))) : [];
        _play?.Pause(); _busy = true;
        try
        {
            ScriptCatalogService.Candidate prepared;
            using (_play?.Scene.World.ReadOnly()) prepared = Catalog.LoadCandidate(path);
            using var candidate = prepared; int count = candidate.Count; bool committed = false;
            void Commit() { if (!committed) { Catalog.CommitCandidate(candidate); committed = true; } }
            try { if (_play is not null) _play.Reload(candidate.Instantiate); else foreach (var binding in bindings) _ = candidate.Instantiate(binding); Commit(); return count; }
            catch { if (_play?.State == PlayState.Faulted) Commit(); throw; }
        }
        finally { _busy = false; }
    }
    public void StopPlay()
    {
        Verify(); var previous = _play; if (previous is null) return; _busy = true;
        try { previous.Stop(); }
        finally
        {
            try { if (previous.State == PlayState.Stopped) {
                _composition?.Dispose(); _composition = null; previous.Dispose(); _lease?.Dispose(); _lease = null; _play = null;
            } }
            finally { _busy = false; }
        }
    }
    public void Dispose()
    {
        VerifyThread(); if (_disposed) return; Verify(); Exception? error = null;
        try { StopPlay(); } catch (Exception e) { error = e; }
        if (_play is not null) throw new InvalidOperationException("Runtime still owns active Play resources.", error);
        if (_ownsCatalog) Catalog.Dispose(); _disposed = true;
        if (error is not null) throw new AggregateException("Runtime shutdown failed.", error);
    }
}
