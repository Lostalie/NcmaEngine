using Ncma.Gameplay;
using Ncma.Scene;
using Ncma.Scripting;

namespace Ncma.Application.Runtime;

// No editor, native or transport dependencies. Caller decides whether the Play document is a clone.
public sealed class RuntimeSessionOwner : IDisposable
{
    private readonly bool _ownsCatalog;
    private PlaySession? _play;
    private IDisposable? _lease;
    private PlayStatus? _lastStartFailure;
    private bool _busy, _disposed;
    public SceneDocument Document { get; }
    public ScriptCatalogService Catalog { get; }
    public PlaySession? Play { get { Verify(); return _play; } }
    public PlayStatus? LastStartFailure { get { Verify(); return _lastStartFailure; } }
    public RuntimeSessionOwner(SceneDocument document, ScriptCatalogService? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(document); document.VerifyAccess();
        Document = document; Catalog = catalog ?? new(); _ownsCatalog = catalog is null;
    }
    private void Verify()
    {
        Document.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Runtime callback reentry is forbidden.");
    }
    private void Preflight(ScriptCatalogService.Candidate candidate)
    {
        using (Document.World.ReadOnly())
            foreach (var item in Document.CaptureSnapshot().Objects)
                foreach (var binding in item.Behaviours) _ = candidate.Instantiate(binding);
    }
    public int LoadGameplay(string path)
    {
        Verify(); if (_play is not null) throw new InvalidOperationException("Stop Play before loading gameplay.");
        _busy = true;
        try { using var read = Document.World.ReadOnly(); using var candidate = Catalog.LoadCandidate(path); int count = candidate.Count;
            Preflight(candidate); Catalog.CommitCandidate(candidate); return count; }
        finally { _busy = false; }
    }
    public PlaySession StartPlay(SceneDocument? runtimeDocument = null, Func<SceneDocument, PlaySession>? factory = null)
    {
        Verify(); if (_play is not null) throw new InvalidOperationException("Stop current Play first.");
        var document = runtimeDocument ?? Document; document.VerifyAccess();
        PlaySession? candidate = null; IDisposable? lease = null; _busy = true; _lastStartFailure = null;
        try
        {
            using (Document.World.ReadOnly()) using (document.World.ReadOnly())
                candidate = factory is null ? new PlaySession(document) : factory(document);
            if (candidate is null || !ReferenceEquals(candidate.Document, document)) throw new ArgumentException("Foreign Play document.");
            lease = Catalog.AcquireLease(); candidate.Start(Catalog.Instantiate); _lease = lease; _play = candidate; return candidate;
        }
        catch { if (candidate is not null) _lastStartFailure = candidate.Status;
            try { candidate?.Dispose(); } finally { lease?.Dispose(); } throw; }
        finally { _busy = false; }
    }
    public int ReloadGameplay(string path)
    {
        Verify(); _play?.Pause(); _busy = true;
        try
        {
            ScriptCatalogService.Candidate prepared;
            using (Document.World.ReadOnly()) using (_play?.Document.World.ReadOnly()) prepared = Catalog.LoadCandidate(path);
            using var candidate = prepared; int count = candidate.Count; bool committed = false;
            void Commit() { if (!committed) { Catalog.CommitCandidate(candidate); committed = true; } }
            try { if (_play is not null) _play.Reload(candidate.Instantiate); else Preflight(candidate); Commit(); return count; }
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
            try { if (previous.State == PlayState.Stopped) { _play = null;
                try { previous.Dispose(); } finally { _lease?.Dispose(); _lease = null; } } }
            finally { _busy = false; }
        }
    }
    public void Dispose()
    {
        Document.VerifyAccess(); if (_disposed) return; Verify(); Exception? error = null;
        try { StopPlay(); } catch (Exception e) { error = e; }
        if (_play is not null) throw new InvalidOperationException("Runtime still owns active Play resources.", error);
        if (_ownsCatalog) Catalog.Dispose(); _disposed = true;
        if (error is not null) throw new AggregateException("Runtime shutdown failed.", error);
    }
}
