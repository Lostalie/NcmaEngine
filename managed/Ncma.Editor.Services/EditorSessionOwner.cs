using Ncma.Editor.Core;
using Ncma.Editor.Transport;
using Ncma.Gameplay;
using Ncma.Scene;
using Ncma.Scripting;
using Ncma.Application.Runtime;
using Ncma.Assets.Authoring;
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
    private IDisposable? _playAssets;
    private ImportCoordinator? _imports;
    private PlaySession? _play => _runtime.Play;
    public ScriptCatalogService Catalog => _runtime.Catalog;
    public EditorSessionOwner(string name, ScriptCatalogService? catalog = null, bool activateEditor = true, Ncma.Runtime.ComponentRegistry? components = null)
    {
        _document = new(name, components); _facade = new(_document.World);
        _runtime = new(_document, catalog);
        if (activateEditor) ActivateEditor();
    }
    public SceneDocument Document { get { Verify(); return _document; } }
    public EditSession? Edit { get { Verify(); return _edit; } }
    public EditorEndpoint? Endpoint { get { Verify(); return _endpoint; } }
    public PlaySession? Play { get { Verify(); return _play; } }
    public AssetProjectAuthoring? Assets { get { Verify(); return _assets; } }
    public ImportCoordinator? Imports { get { Verify(); return _imports; } }
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
    }
    public bool RefreshAssets(bool force = false)
    {
        Verify(); return _assets?.Refresh(_assets.Generation, force) ?? false;
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
        if (_edit is not null)
        {
            document = new(_document.World.Name, _document.World.Components);
            document.RestoreBytes(_document.CaptureBytes());
        }
        var pins = _assets?.PinForPlay();
        _busy = true;
        try { var play = _runtime.StartPlay(document, d => _edit is null ? new PlaySession(d, _facade) : new PlaySession(d));
            _playAssets = pins; pins = null; _edit?.SetFrozen(true); return play; }
        catch { pins?.Dispose(); throw; }
        finally { _busy = false; }
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
            try { if (_runtime.Play is null) { _playAssets?.Dispose(); _playAssets = null; _edit?.SetFrozen(false); } }
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
        try { _assets?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _assets = null;
        try { _endpoint?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _endpoint = null; _facade.Dispose();
        _runtime.Dispose();
        _disposed = true;
        if (errors.Count != 0) throw new AggregateException(errors);
    }
}
