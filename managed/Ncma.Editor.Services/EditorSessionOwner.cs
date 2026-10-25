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
    private readonly RuntimeSessionOwner _runtime;
    private bool _disposed, _busy;
    private EditSession? _edit;
    private EditorWorkflows? _workflows;
    private readonly TimeProvider? _workflowTime;
    private EditorEndpoint? _endpoint;
    private AssetProjectAuthoring? _assets;
    private AssetInspectionService? _assetInspections;
    private EditorEnvironmentWorkspace? _environments;
    private EditorPostProcessWorkspace? _postProcessing;
    private readonly Ncma.Rendering.Scene.PostProcessModules? _postModules;
    private IDisposable? _playAssets;
    private ImportCoordinator? _imports;
    private PreparedSceneAssetLease? _renderAssets, _playRenderAssets;
    private string? _assetRoot;
    private Guid _assetProject;
    private string? _renderAssetPackage;
    private readonly Action<SceneDocumentSnapshot>? _compositionPolicy;
    private readonly Func<PlaySession, IDisposable?>? _composePlay;
    private readonly Func<PlaySession,PreparedSceneAssetLease?,IDisposable?>? _composePreparedPlay;
    private readonly Action? _beforePlayStop;
    public SceneAssetPolicy AssetPolicy { get; }
    private SceneBakeStamp? _playAuthoringSource;
    private EditorPlayDependencies? _playDependencies;
    public EditorPlayDependencies? PlayDependencies { get { Verify(); return _playDependencies; } }
    private EditorRuntimeChanges? _runtimeChanges;
    private PlaySession? _play => _runtime.Play;
    public ScriptCatalogService Catalog => _runtime.Catalog;
    public EditorSessionOwner(string name, ScriptCatalogService? catalog = null, bool activateEditor = true, Ncma.Runtime.ComponentRegistry? components = null,
        Action<SceneDocumentSnapshot>? validateComposition = null, Func<PlaySession, IDisposable?>? composePlay = null, Action? beforePlayStop = null,
        Func<PlaySession,PreparedSceneAssetLease?,IDisposable?>? composePreparedPlay = null, TimeProvider? workflowTime = null,
        Ncma.Rendering.Scene.PostProcessModules? postProcessModules = null,
        SceneAssetPolicy assetPolicy = SceneAssetPolicy.PresentationAndSimulation)
    {
        _ = SceneAssetPolicies.RequiresPresentation(assetPolicy); AssetPolicy = assetPolicy;
        _postModules=components?.Describe().Any(c=>c.TypeId==PostProcessData.TypeId)==true?postProcessModules??Ncma.Rendering.Scene.PostProcessModules.Official():null;
        if(postProcessModules is not null&&_postModules is null)throw new ArgumentException("Post modules require the registered scene value component.");
        _compositionPolicy = snapshot=>{validateComposition?.Invoke(snapshot);if(_postModules is not null)Ncma.Rendering.Scene.ScenePostProcessPreflight.ValidateValues(snapshot,_postModules);};
        _workflowTime = workflowTime;
        _composePlay = composePlay;
        if(composePlay is not null&&composePreparedPlay is not null)throw new ArgumentException("Choose one Play composition contract.");
        _composePreparedPlay=composePreparedPlay;
        _beforePlayStop = beforePlayStop;
        _document = new(name, components, snapshot => {
            _compositionPolicy?.Invoke(snapshot);
            if (_assetRoot is not null) _ = SceneRenderValidation.Inspect(snapshot, _renderAssets?.Metadata, catalog: _assets?.Snapshot.Catalog, policy: AssetPolicy);
        });
        _runtime = new(catalog);
        if (activateEditor) ActivateEditor();
    }
    public SceneDocument Document { get { Verify(); return _document; } }
    public EditSession? Edit { get { Verify(); return _edit; } }
    public EditorWorkflows? Workflows { get { Verify(); return _workflows; } }
    public EditorEndpoint? Endpoint { get { Verify(); return _endpoint; } }
    public PlaySession? Play { get { Verify(); return _play; } }
    // Origin of the currently isolated Play, never a write grant or a live document mirror.
    public SceneBakeStamp? PlayAuthoringSource { get { Verify(); return _playAuthoringSource; } }
    public EditorRuntimeChanges? RuntimeChanges { get { Verify(); return _runtimeChanges; } }
    public AssetProjectAuthoring? Assets { get { Verify(); return _assets; } }
    public AssetInspectionService? AssetInspections { get { Verify(); return _assetInspections; } }
    public EditorEnvironmentWorkspace? Environments { get { Verify(); return _environments; } }
    public EditorPostProcessWorkspace? PostProcessing { get { Verify(); return _postProcessing; } }
    public void ConfigureEnvironments(string root,Guid project,Func<Ncma.Assets.EnvironmentPresetLibrary> loadPreset,TimeProvider? time=null,Action<string>? fault=null)
    {
        Verify();if(_assets is null||_environments is not null||_play is not null||_renderAssetPackage is not null)throw new InvalidOperationException("Environment authoring requires fresh source-project startup.");
        if(Path.GetFullPath(root)!=_assetRoot||project!=_assetProject)throw new ArgumentException("Foreign environment authoring project.");
        _environments=new(new EditorWorkspace(this),root,project,loadPreset,time,fault);
    }
    public ImportCoordinator? Imports { get { Verify(); return _imports; } }
    public PreparedSceneAssetLease? RenderAssets { get { Verify(); return _renderAssets; } }
    public PreparedSceneAssetLease? PlayRenderAssets { get { Verify(); return _playRenderAssets; } }
    public void ConfigureImportTools(ImportWorkerLaunch trustedLaunch, Func<string, bool> approvedSource)
    {
        Verify(); if (_assets is null || _imports is not null || _play is not null) throw new InvalidOperationException("Import tools require idle configured Editor assets.");
        _imports = _assets.CreateImportCoordinator(trustedLaunch, approvedSource);
    }
    public void ConfigureAssets(string projectRoot, Guid projectId, ulong generation, AssetWriteScope? scope = null,UiWriteScope? uiScope=null,AnimationGraphWriteScope? graphScope=null)
    {
        Verify();
        if (_edit is null || _assets is not null || _play is not null) throw new InvalidOperationException("Asset startup requires a fresh Editor.");
        _assets = new(projectRoot, projectId, generation, _edit, scope ?? new([], [], [], () => true),uiScope:uiScope,graphScope:graphScope);
        _assetRoot = Path.GetFullPath(projectRoot); _assetProject = projectId;
        var assets = _assets;
        _assetInspections = new(_edit, projectId, generation, () => !_disposed && ReferenceEquals(_assets, assets),
            () => (assets.Clock.Revision, assets.SnapshotRevision));
        var snapshot = assets.Snapshot;
        _assetInspections.Publish(snapshot.Catalog, snapshot.Diagnostics, assets.SnapshotRevision);
        _assetInspections.Register(); // Defaults to zero visible UUIDs; pairing is not a file grant.
    }
    // Explicit startup/refresh; never called from a simulation/render tick or an Agent callback.
    public void PrepareRenderAssets(string projectRoot, Guid projectId,string? package=null)
    {
        Verify(); if (_play is not null) throw new InvalidOperationException("Initial render asset preparation requires idle Edit.");
        if (_assetRoot is not null && (_assetRoot != Path.GetFullPath(projectRoot) || _assetProject != projectId)) throw new ArgumentException("Foreign asset project.");
        var candidate = SceneAssetPreparation.Prepare(projectRoot, projectId, _document.CaptureSnapshot(), false,package,AssetPolicy);
        _renderAssets?.Dispose(); _renderAssets = candidate; _assetRoot = Path.GetFullPath(projectRoot); _assetProject = projectId;_renderAssetPackage=package;
    }
    public void RefreshRenderAssets()
    {
        Verify(); if (_assetRoot is null) throw new InvalidOperationException("No configured render asset project.");
        var candidate = SceneAssetPreparation.Prepare(_assetRoot, _assetProject, _document.CaptureSnapshot(), false,_renderAssetPackage,AssetPolicy);
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
        _runtimeChanges = new(this);
        _workflows = new(_edit, _workflowTime);
        if(_postModules is not null)_postProcessing=new(new EditorWorkspace(this),_postModules,_workflowTime);
    }
    public void RefreshCatalog() { Verify(); _edit?.SetBehaviourCatalog(Describe(Catalog.Snapshot)); }
    public void ConfigureEndpoint(bool enabled, string projectRoot)
    {
        Verify();
        if (_edit is null) throw new InvalidOperationException("Editor required.");
        // Prepare the replacement first; failure preserves the previous endpoint.
        var candidate = enabled ? new EditorEndpoint(_edit, projectRoot) : null;
        if (candidate is not null) candidate.AttachRequestMonitor(_workflows!);
        try { _endpoint?.Dispose(); } catch { candidate?.Dispose(); throw; }
        _endpoint = candidate;
        _workflows!.Bind(candidate);
        _assetInspections?.Revoke(); // A replacement endpoint never inherits old asset-read approval.
        _environments?.RevokeRead();
        _postProcessing?.RevokeRead();
    }
    public int LoadGameplay(string path)
    {
        Verify();
        if (_play is not null) throw new InvalidOperationException("Stop Play before loading gameplay.");
        int count;
        using (_document.ReadOnly()) count = _runtime.LoadGameplay(path, _document.CaptureSnapshot());
        RefreshCatalog(); return count;
    }
    public PlaySession StartPlay()
    {
        Verify();
        if (_play is not null) throw new InvalidOperationException("Stop current Play first.");
        PreparedSceneAssetLease? renderPins = null;
        IDisposable? pins = null;
        try
        {
            SceneAssetDependencyClosure? assetClosure = null;
            var baked = _document.BakeRuntime(_document.Revision, snapshot => {
                if (SceneAssetPreparation.References(snapshot, AssetPolicy).Length != 0) renderPins = _renderAssets?.AcquireLease();
                else if (_renderAssets is not null && _renderAssets.Assets.List().Count == 0) renderPins = _renderAssets.AcquireLease();
                else if (_assetRoot is not null) renderPins = SceneAssetPreparation.Prepare(_assetRoot, _assetProject, snapshot, true, _renderAssetPackage, AssetPolicy);
                assetClosure = SceneAssetPreparation.ResolveDependencies(snapshot, renderPins, AssetPolicy);
            });
            var scriptClosure = Catalog.ResolveBindings(baked.Image);
            var dependencies = new EditorPlayDependencies(baked.Source, baked.Image.ContentHash, assetClosure!, scriptClosure);
            pins = _assets?.PinForPlay();
            _busy = true;
            PlaySession play;
            using (_document.ReadOnly()) {
                var scene = baked.Image.Instantiate(_document.Components);
                Catalog.RequireCurrent(scriptClosure); assetClosure!.RequireCurrent(renderPins);
                play = _runtime.StartPlay(scene,
                    compose:p=>_composePreparedPlay is null?_composePlay?.Invoke(p):_composePreparedPlay(p,renderPins));
            }
            _playAuthoringSource = baked.Source;
            _playDependencies = dependencies;
            _playAssets = pins; pins = null; _playRenderAssets = renderPins; renderPins = null; _edit?.SetFrozen(true); return play;
        }
        catch
        {
            // A failed close during startup must retain the associated asset pins and Edit freeze.
            if (_runtime.Play is not null) {
                _playAssets = pins; pins = null; _playRenderAssets = renderPins; renderPins = null; _edit?.SetFrozen(true);
            }
            throw;
        }
        finally { try { pins?.Dispose(); } finally { try { renderPins?.Dispose(); } finally { _busy = false; } } }
    }
    public int ReloadGameplay(string path)
    {
        Verify();
        _busy = true;
        try { if(_play is not null)_beforePlayStop?.Invoke();
            using var read = _document.ReadOnly();
            return _runtime.ReloadGameplay(path, _play is null ? _document.CaptureSnapshot() : null); }
        finally { try { _playDependencies = null; _edit?.SetBehaviourCatalog(Describe(Catalog.Snapshot)); } finally { _busy = false; } }
    }
    public void StopPlay()
    {
        Verify();
        _busy = true;
        try { if(_runtime.Play is not null)_beforePlayStop?.Invoke(); _runtime.StopPlay(); }
        finally
        {
            try { if (_runtime.Play is null) { _playAuthoringSource = null; _playDependencies = null; _runtimeChanges?.Invalidate(); _playAssets?.Dispose(); _playAssets = null; _playRenderAssets?.Dispose(); _playRenderAssets = null; _edit?.SetFrozen(false); } }
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
        _workflows?.Close();
        try { _renderAssets?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _renderAssets = null;
        try { _environments?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _environments = null;
        try { _postProcessing?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _postProcessing=null;
        try { _assets?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _assets = null;
        _assetInspections?.Revoke(); _assetInspections = null;
        try { _endpoint?.Dispose(); } catch (Exception e) { errors.Add(e); }
        _endpoint = null;
        _runtime.Dispose();
        _disposed = true;
        if (errors.Count != 0) throw new AggregateException(errors);
    }
}
