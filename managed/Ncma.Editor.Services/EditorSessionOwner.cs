using Ncma.Editor.Core;
using Ncma.Editor.Transport;
using Ncma.Gameplay;
using Ncma.Scene;
using Ncma.Scripting;
using Ncma.Application.Runtime;
using Ncma.Assets.Authoring;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.Services;

// Owns one edit document, endpoint and optional isolated Play; catalog can be explicitly borrowed
// by the transitional bridge, whose application must outlive all session owners.
public sealed class EditorSessionOwner : IDisposable
{
    private readonly SceneDocument _document;
    private readonly Ncma.SceneWorld _facade;
    private readonly RuntimeSessionOwner _runtime;
    private bool _disposed, _busy;
    private EditSession? _edit;
    private EditorEndpoint? _endpoint;
    private AssetProjectAuthoring? _assets;
    private AssetInspectionService? _assetInspections;
    private IDisposable? _playAssets;
    private ImportCoordinator? _imports;
    private PreparedSceneAssetLease? _renderAssets, _playRenderAssets;
    private string? _assetRoot;
    private Guid _assetProject;
    private readonly Action<SceneDocumentSnapshot>? _compositionPolicy;
    private PlaySession? _play => _runtime.Play;
    public ScriptCatalogService Catalog => _runtime.Catalog;
    public EditorSessionOwner(string name, ScriptCatalogService? catalog = null, bool activateEditor = true, Ncma.Runtime.ComponentRegistry? components = null,
        Action<SceneDocumentSnapshot>? validateComposition = null)
    {
        _compositionPolicy = validateComposition;
        _document = new(name, components, snapshot => {
            _compositionPolicy?.Invoke(snapshot);
            if (_assetRoot is not null) _ = SceneRenderValidation.Inspect(snapshot, _renderAssets?.Metadata, catalog: _assets?.Snapshot.Catalog);
        }); _facade = new(_document.World);
        _runtime = new(_document, catalog);
        if (activateEditor) ActivateEditor();
    }
    public SceneDocument Document { get { Verify(); return _document; } }
    public EditSession? Edit { get { Verify(); return _edit; } }
    public EditorEndpoint? Endpoint { get { Verify(); return _endpoint; } }
    public PlaySession? Play { get { Verify(); return _play; } }
    public AssetProjectAuthoring? Assets { get { Verify(); return _assets; } }
    public AssetInspectionService? AssetInspections { get { Verify(); return _assetInspections; } }
    public ImportCoordinator? Imports { get { Verify(); return _imports; } }
    public PreparedSceneAssetLease? RenderAssets { get { Verify(); return _renderAssets; } }
    public PreparedSceneAssetLease? PlayRenderAssets { get { Verify(); return _playRenderAssets; } }
    public void ConfigureImportTools(ImportWorkerLaunch trustedLaunch, Func<string, bool> approvedSource)
    {
        Verify(); if (_assets is null || _imports is not null || _play is not null) throw new InvalidOperationException("Import tools require idle configured Editor assets.");
        _imports = _assets.CreateImportCoordinator(trustedLaunch, approvedSource);
    }
    public void ConfigureAssets(string projectRoot, Guid projectId, ulong generation, AssetWriteScope? scope = null)
    {
        Verify();
        if (_edit is null || _assets is not null || _play is not null) throw new InvalidOperationException("Asset startup requires a fresh Editor.");
        _assets = new(projectRoot, projectId, generation, _edit, scope ?? new([], [], [], () => true));
        _assetRoot = Path.GetFullPath(projectRoot); _assetProject = projectId;
        var assets = _assets;
        _assetInspections = new(_edit, projectId, generation, () => !_disposed && ReferenceEquals(_assets, assets),
            () => (assets.Clock.Revision, assets.SnapshotRevision));
        var snapshot = assets.Snapshot;
        _assetInspections.Publish(snapshot.Catalog, snapshot.Diagnostics, assets.SnapshotRevision);
        _assetInspections.Register(); // Defaults to zero visible UUIDs; pairing is not a file grant.
    }
    // Explicit startup/refresh; never called from a simulation/render tick or an Agent callback.
    public void PrepareRenderAssets(string projectRoot, Guid projectId)
    {
        Verify(); if (_play is not null) throw new InvalidOperationException("Initial render asset preparation requires idle Edit.");
        if (_assetRoot is not null && (_assetRoot != Path.GetFullPath(projectRoot) || _assetProject != projectId)) throw new ArgumentException("Foreign asset project.");
        var candidate = SceneAssetPreparation.Prepare(projectRoot, projectId, _document.CaptureSnapshot(), false);
        _renderAssets?.Dispose(); _renderAssets = candidate; _assetRoot = Path.GetFullPath(projectRoot); _assetProject = projectId;
    }
    public void RefreshRenderAssets()
    {
        Verify(); if (_assetRoot is null) throw new InvalidOperationException("No configured render asset project.");
        var candidate = SceneAssetPreparation.Prepare(_assetRoot, _assetProject, _document.CaptureSnapshot(), false);
        _renderAssets?.Dispose(); _renderAssets = candidate;
    }
    public bool RefreshAssets(bool force = false)
    {
        Verify(); if (_assets is null) return false;
        bool changed = _assets.Refresh(_assets.Generation, force);
        if (_assetInspections is not null && _assetInspections.PublishedRevision != _assets.SnapshotRevision && _assets.SnapshotRevision == _assets.Clock.Revision) {
            var snapshot = _assets.Snapshot;
            _assetInspections.Publish(snapshot.Catalog, snapshot.Diagnostics, _assets.SnapshotRevision);
        }
        return changed;
    }
    internal Ncma.SceneWorld Facade { get { Verify(); return _facade; } }
    private void Verify()
    {
        _document.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Session callback reentry is forbidden.");
    }
    public static BehaviourCatalog Describe(ScriptCatalogSnapshot catalog) => new(catalog.Generation,
        catalog.Types.Select(t => new BehaviourTypeDescriptor(t.TypeName,
            t.Exports.Select(e => new BehaviourExportDescriptor(e.Name, e.Kind, e.DisplayName, e.Category, e.DefaultValue)).ToArray())).ToArray());
    public void ActivateEditor()
    {
        Verify();
        if (_play is not null) throw new InvalidOperationException("Stop Play before activating Editor.");
        if (_edit is not null) throw new InvalidOperationException("Editor is already activated.");
        _edit = new(_document); RefreshCatalog();
    }
    public void RefreshCatalog() { Verify(); _edit?.SetBehaviourCatalog(Describe(Catalog.Snapshot)); }
    public void ConfigureEndpoint(bool enabled, string projectRoot)
    {
        Verify();
        if (_edit is null) throw new InvalidOperationException("Editor required.");
        // Prepare the replacement first; failure preserves the previous endpoint.
        var candidate = enabled ? new EditorEndpoint(_edit, projectRoot) : null;
        try { _endpoint?.Dispose(); } catch { candidate?.Dispose(); throw; }
        _endpoint = candidate;
        _assetInspections?.Revoke(); // A replacement endpoint never inherits old asset-read approval.
    }
    public int LoadGameplay(string path)
    {
        Verify();
        if (_play is not null) throw new InvalidOperationException("Stop Play before loading gameplay.");
        int count = _runtime.LoadGameplay(path); RefreshCatalog(); return count;
    }
    public PlaySession StartPlay()
    {
        Verify();
        if (_play is not null) throw new InvalidOperationException("Stop current Play first.");
        var document = _document;
        PreparedSceneAssetLease? renderPins = _renderAssets?.AcquireLease();
        IDisposable? pins = null;
        try
        {
            if (_edit is not null)
            {
                var playMetadata = renderPins?.Metadata;
                document = _document.CreateIsolatedCopy(snapshot => {
                    _compositionPolicy?.Invoke(snapshot);
                    if (playMetadata is not null) _ = SceneRenderValidation.Inspect(snapshot, playMetadata);
                });
            }
            pins = _assets?.PinForPlay();
            _busy = true;
            var play = _runtime.StartPlay(document, d => _edit is null ? new PlaySession(d, _facade) : new PlaySession(d));
            _playAssets = pins; pins = null; _playRenderAssets = renderPins; renderPins = null; _edit?.SetFrozen(true); return play;
        }
        finally { try { pins?.Dispose(); } finally { try { renderPins?.Dispose(); } finally { _busy = false; } } }
    }
    public int ReloadGameplay(string path)
    {
        Verify();
        _busy = true;
        try { return _runtime.ReloadGameplay(path); }
        finally { try { _edit?.SetBehaviourCatalog(Describe(Catalog.Snapshot)); } finally { _busy = false; } }
    }
    public void StopPlay()
    {
        Verify();
        _busy = true;
        try { _runtime.StopPlay(); }
        finally
        {
            try { if (_runtime.Play is null) { _playAssets?.Dispose(); _playAssets = null; _playRenderAssets?.Dispose(); _playRenderAssets = null; _edit?.SetFrozen(false); } }
            finally { _busy = false; }
        }
    }
    public void Dispose()
    {
        _document.VerifyAccess();
        if (_disposed) return;
        Verify();
        var errors = new List<Exception>();
        try { StopPlay(); } catch (Exception e) { errors.Add(e); }
        if (_play is not null) throw new AggregateException(errors);
        _edit?.SetFrozen(true);
        try { _renderAssets?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _renderAssets = null;
        try { _assets?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _assets = null;
        _assetInspections?.Revoke(); _assetInspections = null;
        try { _endpoint?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _endpoint = null; _facade.Dispose();
        _runtime.Dispose();
        _disposed = true;
        if (errors.Count != 0) throw new AggregateException(errors);
    }
}
