using Ncma.Runtime;
namespace Ncma.Scene;

// Authoring data only. No World, Entity slots, component pools, simulation clock or runtime facade.
// The private normalized document is the sole authoring value store; the UUID index stores no values.
public sealed partial class SceneDocument
{
    private SceneDocumentSnapshot _content;
    private Dictionary<Guid, int> _index = [];
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private ulong _revision;
    private int _readOnlyDepth;
    private bool _preparing, _reading;
    private long _capturedDocumentCount;
    private readonly Action<SceneDocumentSnapshot>? _validateComposition;

    public SceneDocument(string name = "Untitled", ComponentRegistry? components = null,
        Action<SceneDocumentSnapshot>? validateComposition = null)
    {
        _content = SceneDocumentCodec.Copy(new(SceneDocumentCodec.Version, name, []));
        Components = components ?? ComponentRegistry.CreateDefault(); Components.Freeze();
        _validateComposition = validateComposition;
    }
    public ComponentRegistry Components { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
    public ulong Revision { get { VerifyAccess(); return _revision; } }
    public string Name { get { VerifyReadBoundary(); return _content.Name; } }
    public int ObjectCount { get { VerifyReadBoundary(); return _content.Objects.Length; } }
    public void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Authoring document access requires its owner thread.");
    }
    private void VerifyMutation()
    {
        VerifyReadBoundary();
        if (_readOnlyDepth != 0) throw new InvalidOperationException("Authoring document is read-only in this scope.");
    }
    public IDisposable ReadOnly()
    {
        VerifyAccess(); _readOnlyDepth = checked(_readOnlyDepth + 1); return new ReadScope(this);
    }
    private sealed class ReadScope(SceneDocument document) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            document.VerifyAccess(); if (_disposed) return;
            document._readOnlyDepth--; _disposed = true;
        }
    }
    public SceneDocumentSnapshot CaptureSnapshot()
    {
        VerifyReadBoundary(); _capturedDocumentCount++;
        return SceneDocumentCodec.Copy(_content);
    }
    private void ValidateComposition(SceneDocumentSnapshot snapshot)
    {
        // No callback receives the publishable arrays or JSON ownership.
        _validateComposition?.Invoke(SceneDocumentCodec.Copy(snapshot));
    }
    public SceneDocument CreateIsolatedCopy(Action<SceneDocumentSnapshot>? validateCompositionOverride = null)
    {
        byte[] bytes = CaptureBytes();
        using var read = ReadOnly();
        var copy = new SceneDocument(_content.Name, Components, validateCompositionOverride ?? _validateComposition);
        copy.RestoreBytes(bytes); return copy;
    }
    public void ValidateAuthoring() => _ = PrepareRestore(CaptureSnapshot());
    public byte[] CaptureBytes() => SceneDocumentCodec.Encode(CaptureSnapshot());
    public void RestoreBytes(ReadOnlySpan<byte> bytes, ulong? expectedRevision = null)
    { VerifyMutation(); RestoreSnapshot(SceneDocumentCodec.Decode(bytes), expectedRevision); }
    public void RestoreSnapshot(SceneDocumentSnapshot snapshot, ulong? expectedRevision = null)
        => PrepareRestore(snapshot, expectedRevision)();

    internal Action PrepareRestore(SceneDocumentSnapshot snapshot, ulong? expectedRevision = null,
        Action<SceneDocumentSnapshot>? prepared = null)
    {
        VerifyMutation(); ulong baseline = _revision;
        if (expectedRevision is not null && expectedRevision != baseline) throw new InvalidOperationException("Document revision conflict.");
        _preparing = true;
        try
        {
            var detached = SceneDocumentCodec.Copy(snapshot);
            var normalized = detached with { Objects = detached.Objects.Select(o => o with
            { Components = NormalizeComponents(o.Components) }).ToArray() };
            // Recheck total normalized bytes BEFORE any callbacks or publication.
            var owned = SceneDocumentCodec.Copy(normalized);
            var index = Index(owned); ulong revision = checked(baseline + 1);
            ValidateComposition(owned); prepared?.Invoke(SceneDocumentCodec.Copy(owned));
            return () =>
            {
                VerifyMutation();
                if (_revision != baseline) throw new InvalidOperationException("Document changed after preparation.");
                _content = owned; _index = index; _revision = revision;
            };
        }
        finally { _preparing = false; }
    }
    private static Dictionary<Guid, int> Index(SceneDocumentSnapshot snapshot) => snapshot.Objects
        .Select((o, i) => (o.Id, Index: i)).ToDictionary(p => p.Id, p => p.Index);

    // Trusted authoring builders use these operations. Editor/AI changes still enter the unique
    // EditSession transaction/Undo pipeline; no alternative history is introduced here.
    private void Change(Func<SceneDocumentSnapshot, SceneDocumentSnapshot> prepare)
    {
        VerifyMutation(); _preparing = true;
        try
        {
            ulong revision = checked(_revision + 1);
            var owned = SceneDocumentCodec.Copy(prepare(_content));
            var index = Index(owned); // All allocations/validation complete before publication.
            _content = owned; _index = index; _revision = revision;
        }
        finally { _preparing = false; }
    }
    private SceneObjectData RequireObject(Guid objectId) => _index.TryGetValue(objectId, out int index)
        ? _content.Objects[index] : throw new ArgumentException("Unknown authoring object UUID.", nameof(objectId));
    private SceneDocumentSnapshot Replace(SceneObjectData item) => _content with
    { Objects = _content.Objects.Select(o => o.Id == item.Id ? item : o).ToArray() };
    public Guid[] GetObjectIds() { VerifyReadBoundary(); return _content.Objects.Select(o => o.Id).ToArray(); }
    public bool ContainsObject(Guid id) { VerifyReadBoundary(); return _index.ContainsKey(id); }
    public string GetObjectName(Guid id) { VerifyReadBoundary(); return RequireObject(id).Name; }
    public Guid CreateObject(string name, Guid? id = null)
    {
        Guid created = id ?? Guid.NewGuid();
        Change(s => s with { Objects = [.. s.Objects, new(created, name, [], [])] }); return created;
    }
    public void DeleteObject(Guid id) => Change(s => { _ = RequireObject(id); return s with { Objects = s.Objects.Where(o => o.Id != id).ToArray() }; });
    public void RenameObject(Guid id, string name) => Change(_ => Replace(RequireObject(id) with { Name = name }));
    public void Rename(string name) => Change(s => s with { Name = name });
    public BehaviourBindingData[] GetBindings(Guid objectId)
    {
        VerifyReadBoundary(); var bindings = RequireObject(objectId).Behaviours;
        return bindings.Select(b => b with { Exports = (ExportData[])b.Exports.Clone() }).ToArray();
    }
    public void SetBindings(Guid objectId, BehaviourBindingData[] values) => Change(_ =>
    {
        var item = RequireObject(objectId);
        BehaviourBindingRules.Validate(values, _content.Objects.Where(o => o.Id != objectId)
            .SelectMany(o => o.Behaviours).Select(b => b.Id).ToHashSet(), "bindings");
        var bindings = values.Select(b => b with { Exports = b.Exports.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray() }).ToArray();
        return Replace(item with { Behaviours = bindings });
    });
}
