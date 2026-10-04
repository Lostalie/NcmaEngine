using System.Text.Json;
namespace Ncma.Editor.Services;

public sealed record FbxPreviewStatus(ulong Revision, Guid? AssetId, string? Source, bool Paused, int Clip,
    string? ClipName, double Time, double Duration, int Bones, int Meshes, int UndoCount, int RedoCount);
public enum FbxPreviewCommand { Pause, Resume, Step, SelectClip, Undo, Redo }

// Explicit isolated preview document/history. Never a SceneDocument, gameplay object or Agent file-loading tool.
public sealed class FbxPreviewSession(string libraryPath) : IDisposable
{
    private sealed record State(ImportedCharacterResource? Resource = null, string? Source = null,
        bool Paused = true, int Clip = 0, double Time = 0);
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private State _state = new();
    private readonly List<State> _undo = [], _redo = [];
    private readonly HashSet<ImportedCharacterResource> _resources = [];
    private bool _disposed;
    public ulong Revision { get; private set; }
    public const int HistoryLimit = 8;
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Preview owner thread required.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    private void Check(ulong revision) { Verify(); if (revision != Revision) throw new InvalidOperationException("stale_preview"); }
    public FbxPreviewStatus Capture()
    {
        Verify(); var s = _state; var r = s.Resource;
        return new(Revision, r?.AssetId, s.Source, s.Paused, s.Clip, r?.ClipName(s.Clip), s.Time,
            r?.ClipDuration(s.Clip) ?? 0, r?.BoneCount ?? 0, r?.MeshCount ?? 0, _undo.Count, _redo.Count);
    }
    public Guid? ResourceIdentity { get { Verify(); return _state.Resource?.ResourceIdentity; } }
    public JsonElement? Report { get { Verify(); return _state.Resource?.Report; } }
    private void Prune()
    {
        var retained = _undo.Concat(_redo).Append(_state).Select(s => s.Resource).Where(r => r is not null).ToHashSet();
        foreach (var resource in _resources.Where(r => !retained.Contains(r)).ToArray())
        { resource.Dispose(); _resources.Remove(resource); }
    }
    private void Commit(State candidate)
    {
        ulong nextRevision = checked(Revision + 1);
        _undo.Add(_state); if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _state = candidate; _redo.Clear(); Revision = nextRevision; Prune();
    }
    public void Import(ulong revision, string source, double sampleRate = 30)
    {
        Check(revision);
        if (!Path.IsPathFullyQualified(source)) throw new ArgumentException("Trusted local FBX import requires an absolute path.");
        source = Path.GetFullPath(source);
        // Reimport retains persistent identity. Numerical resources are immutable and individually leased.
        Guid? id = string.Equals(_state.Source, source, StringComparison.OrdinalIgnoreCase) ? _state.Resource?.AssetId : null;
        var resource = new ImportedCharacterResource(libraryPath, source, sampleRate, id);
        _resources.Add(resource);
        Commit(new(resource, source, true, resource.ClipCount > 1 ? 1 : 0));
    }
    public void Execute(ulong revision, FbxPreviewCommand command, double seconds = 1.0 / 60, int clip = 0)
    {
        Check(revision);
        if (!Enum.IsDefined(command)) throw new ArgumentException("Unknown preview command.");
        if (command is FbxPreviewCommand.Undo or FbxPreviewCommand.Redo)
        {
            var source = command == FbxPreviewCommand.Undo ? _undo : _redo;
            var target = command == FbxPreviewCommand.Undo ? _redo : _undo;
            if (source.Count == 0) throw new InvalidOperationException("Preview history empty.");
            ulong nextRevision = checked(Revision + 1);
            target.Add(_state); _state = source[^1]; source.RemoveAt(source.Count - 1);
            Revision = nextRevision; Prune(); return;
        }
        var resource = _state.Resource ?? throw new InvalidOperationException("No imported character.");
        State candidate = command switch
        {
            FbxPreviewCommand.Pause => _state with { Paused = true },
            FbxPreviewCommand.Resume => _state with { Paused = false },
            FbxPreviewCommand.Step => Advance(_state, seconds),
            FbxPreviewCommand.SelectClip when (uint)clip < resource.ClipCount => _state with { Clip = clip, Time = 0 },
            _ => throw new ArgumentException("Invalid preview clip.")
        };
        Commit(candidate);
    }
    private static State Advance(State state, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds is < 0 or > 1) throw new ArgumentException("Preview delta must be finite within 0..1.");
        var resource = state.Resource ?? throw new InvalidOperationException("No imported character.");
        return state with { Time = (state.Time + seconds) % resource.ClipDuration(state.Clip) };
    }
    public void Tick(double seconds)
    {
        Verify();
        if (!double.IsFinite(seconds) || seconds is < 0 or > 1) throw new ArgumentException("Invalid preview delta.");
        // Automatic preview time does not allocate history entries or affect scene revision.
        if (_state.Resource is not null && !_state.Paused) _state = Advance(_state, seconds);
    }
    public string ClipName(int clip) { Verify(); return (_state.Resource ?? throw new InvalidOperationException("No imported character.")).ClipName(clip); }
    public int ClipCount { get { Verify(); return _state.Resource?.ClipCount ?? 0; } }
    public int SampleFloatCount { get { Verify(); return _state.Resource?.SampleFloatCount ?? 0; } }
    public void Sample(Span<float> destination)
    {
        Verify(); var resource = _state.Resource ?? throw new InvalidOperationException("No imported character.");
        resource.Sample(_state.Clip, _state.Time, destination);
    }
    public void ReadIndices(int mesh, Span<uint> destination)
    { Verify(); (_state.Resource ?? throw new InvalidOperationException("No imported character.")).ReadIndices(mesh, destination); }
    public int IndexCount(int mesh)
    { Verify(); return (_state.Resource ?? throw new InvalidOperationException("No imported character.")).IndexCount(mesh); }
    public int VertexCount(int mesh)
    { Verify(); return (_state.Resource ?? throw new InvalidOperationException("No imported character.")).VertexCount(mesh); }
    public int BoneParent(int bone)
    { Verify(); return (_state.Resource ?? throw new InvalidOperationException("No imported character.")).BoneParent(bone); }
    public string BoneName(int bone)
    { Verify(); return (_state.Resource ?? throw new InvalidOperationException("No imported character.")).BoneName(bone); }
    public void Dispose()
    {
        if (_disposed) return;
        Verify();
        // Only release resources whose native disposal succeeds; retries retain the remaining leases.
        foreach (var resource in _resources.ToArray()) { resource.Dispose(); _resources.Remove(resource); }
        _state = new(); _undo.Clear(); _redo.Clear(); _disposed = true;
    }
}
