using System.Text.Json;
using Ncma.Assets.Authoring.Storage;
using Ncma.Editor.Core;
using Ncma.Ui;

namespace Ncma.Assets.Authoring;

public enum UiResourceKind { Font, Image }

// Trusted exact file/document grants. UI selection or scene grants never imply file permission.
public sealed class UiWriteScope
{
    private readonly Dictionary<string, Guid> _targets;
    private readonly Func<Guid, UiResourceKind, bool> _dependency;
    private readonly Func<bool> _current;
    private Func<string,Guid,bool>? _hostTarget;
    public static UiWriteScope ForHost(Func<string,Guid,bool> target,Func<Guid,UiResourceKind,bool> dependency,Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(target);var scope=new UiWriteScope([],dependency,current);scope._hostTarget=target;return scope;
    }
    public UiWriteScope(IEnumerable<KeyValuePair<string, Guid>> targets, Func<Guid, UiResourceKind, bool> dependency, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(targets); _dependency = dependency ?? throw new ArgumentNullException(nameof(dependency));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _targets = targets.ToDictionary(p => AssetPaths.Validate(p.Key), p => p.Value, StringComparer.Ordinal);
        if (_targets.Count > 128 || _targets.Any(p => p.Value == Guid.Empty || !p.Key.EndsWith(".ncmaui", StringComparison.Ordinal))) throw new ArgumentException("UI exact grant.");
    }
    internal void Require(string path, UiDefinition d)
    {
        if (!_current() || !(_hostTarget is { } host?host(path,d.AssetId):_targets.TryGetValue(path,out var id)&&id==d.AssetId) || d.Elements.Any(e =>
            e.Font != Guid.Empty && !_dependency(e.Font, UiResourceKind.Font) || e.Image != Guid.Empty && !_dependency(e.Image, UiResourceKind.Image)))
            throw new EditCommandRejectedException("ui_scope_denied", denied: true);
    }
    internal UiWriteScope Bind(Func<bool> current) => _hostTarget is { } host?ForHost(host,_dependency,()=>_current()&&current()):new(_targets,_dependency,()=>_current()&&current());
}

// Uses the existing checked Windows storage, journal and sole Editor.Core history.
// Large candidates stay out of the capability request's 64 KiB envelope. Only trusted host code
// can prepare a bounded proposal; no arbitrary document, file path or approval arrives via Agent JSON.
public sealed class UiCommands : IEditCommandParticipant, IDisposable
{
    public const string CapabilityName = "ncma.ui.document.commit";
    private sealed record Proposal(string Path, byte[] Bytes, ulong Revision);
    private readonly Dictionary<Guid, Proposal> _proposals = [];
    private readonly AssetProjectPaths _paths; private readonly UiWriteScope _scope;
    private readonly AssetRevisionClock _clock; private readonly AssetDiskTransaction _disk;
    private readonly int _thread = Environment.CurrentManagedThreadId; private bool _disposed;
    public UiCommands(AssetProjectPaths paths, UiWriteScope scope, AssetRevisionClock clock, Action<string>? faultInjection = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths)); _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock)); _disk = new(paths, faultInjection);
    }
    public static CapabilityDescriptor Descriptor => new(CapabilityName, "Commit a host-prepared UI document proposal with exact document/file/resource grants and shared durable Undo.", MutationRisk.Reversible,
        Schema("""{"type":"object","additionalProperties":false,"required":["proposalId","expectedAssetRevision"],"properties":{"proposalId":{"type":"string","format":"uuid"},"expectedAssetRevision":{"type":"integer","minimum":0}}}"""), AssetMetadataCommands.Descriptor.OutputSchema);
    private static JsonElement Schema(string json) { using var parsed = JsonDocument.Parse(json); return parsed.RootElement.Clone(); }
    public Guid PrepareProposal(string path, UiDefinition candidate, ulong expectedAssetRevision)
    {
        Verify(); path = AssetPaths.Validate(path);
        if (expectedAssetRevision != _clock.Revision) throw new EditCommandRejectedException("asset_revision_conflict");
        byte[] bytes = UiCodec.Encode(candidate); _scope.Require(path, UiCodec.Decode(bytes));
        if (_proposals.Count >= 4) throw new InvalidOperationException("UI proposal budget; cancel old proposals first.");
        Guid id = Guid.NewGuid(); _proposals.Add(id, new(path, bytes, expectedAssetRevision)); return id;
    }
    public void CancelProposal(Guid id) { Verify(); _proposals.Remove(id); }
    public ParticipantMemento Prepare(JsonElement input)
    {
        Verify(); if (input.ValueKind != JsonValueKind.Object) throw new ArgumentException("UI commit object required.");
        var fields = input.EnumerateObject().ToArray();
        if (fields.Length != 2 || fields.Select(p => p.Name).Distinct().Count() != 2 || fields.Any(p => p.Name is not ("proposalId" or "expectedAssetRevision"))) throw new ArgumentException("Closed UI commit input.");
        Guid id = input.GetProperty("proposalId").GetGuid();
        if (!_proposals.TryGetValue(id, out var proposal)) throw new ArgumentException("Unknown host UI proposal.");
        if (input.GetProperty("expectedAssetRevision").GetUInt64() != _clock.Revision || proposal.Revision != _clock.Revision) throw new EditCommandRejectedException("asset_revision_conflict");
        var after = UiCodec.Decode(proposal.Bytes); _scope.Require(proposal.Path, after); NoJournal(proposal.Path);
        using var parents = new AssetDirectoryLease(_paths, [proposal.Path]);
        byte[]? before = null; string full = _paths.Resolve(proposal.Path);
        if (Directory.Exists(full)) throw new ArgumentException("UI file required.");
        if (File.Exists(full)) {
            using var file = WindowsAssetFile.Open(full); before = file.Read(UiCodec.MaxBytes); var previous = UiCodec.Decode(before);
            _scope.Require(proposal.Path, previous); if (previous.AssetId != after.AssetId) throw new ArgumentException("UI identity cannot change.");
        }
        return new(AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(proposal.Path, before)]), AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(proposal.Path, proposal.Bytes)]));
    }
    private static string PathOf(ParticipantMemento memento)
    {
        var (a, b) = AssetDiskTransaction.States(memento, true);
        if (a.Length != 1 || b.Length != 1 || a[0].Blob is not null || b[0].Blob is not null || a[0].Path != b[0].Path ||
            !a[0].Path.EndsWith(".ncmaui", StringComparison.Ordinal) || b[0].Data is null) throw new ArgumentException("UI-only memento required.");
        return a[0].Path;
    }
    public void Authorize(ParticipantMemento memento)
    {
        Verify(); string path = PathOf(memento); var (a, b) = AssetDiskTransaction.States(memento, true);
        var after = UiCodec.Decode(b[0].Data!); _scope.Require(path, after);
        if (a[0].Data is { } bytes) { var before = UiCodec.Decode(bytes); _scope.Require(path, before); if (before.AssetId != after.AssetId) throw new ArgumentException("UI history identity mismatch."); }
    }
    public void Validate(ParticipantMemento memento, bool forward) { Authorize(memento); NoJournal(PathOf(memento)); _disk.Validate(memento, forward); _ = checked(_clock.Revision + 1); }
    public void Publish(ParticipantMemento memento, bool forward) { Validate(memento, forward); _disk.Publish(memento, forward, PathOf(memento) + ".journal"); _clock.Advance(); }
    public void Complete(ParticipantMemento memento, bool forward) { Verify(); _disk.Complete(); _proposals.Clear(); }
    public void Compensate(ParticipantMemento memento, bool forward) { Verify(); _disk.Compensate(memento, forward, PathOf(memento) + ".journal"); }
    public void Recover(string path) { Verify(); path = AssetPaths.Validate(path); if (!path.EndsWith(".ncmaui", StringComparison.Ordinal)) throw new ArgumentException("UI recovery extension."); _disk.Recover(path + ".journal", Authorize); _clock.Advance(); _proposals.Clear(); }
    public UiDefinition Read(string path)
    {
        Verify(); path = AssetPaths.Validate(path); using var parents = new AssetDirectoryLease(_paths, [path]);
        NoJournal(path); using var file = WindowsAssetFile.Open(_paths.Resolve(path, requireFile: true));
        var result = UiCodec.Decode(file.Read(UiCodec.MaxBytes)); _scope.Require(path, result); return result;
    }
    private void NoJournal(string path) { string full = _paths.Resolve(path + ".journal"); if (File.Exists(full) || Directory.Exists(full)) throw new EditCommandRejectedException("asset_recovery_required"); }
    private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI commands require owner thread."); }
    public void Dispose() { if (_disposed) return; Verify(); _disk.Dispose(); _proposals.Clear(); _disposed = true; }
}
