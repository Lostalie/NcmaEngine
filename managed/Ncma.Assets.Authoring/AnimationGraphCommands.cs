using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets.Authoring.Storage;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

// Exact host-approved bytes/file pairs, not directory or graph-UUID-only permission.
public sealed class AnimationGraphWriteScope(Func<string, Guid, string?, string, bool> approved, Func<Guid,bool>? proposalCurrent = null, Action<string,Guid,string?,string>? validateResources = null)
{
    internal void Require(string path, byte[]? before, byte[] after) {
        var candidate = AnimationGraphCodec.Decode(after); string hash = Convert.ToHexString(SHA256.HashData(after));
        if (before is not null && AnimationGraphCodec.Decode(before).AssetId != candidate.AssetId) throw new ArgumentException("Graph identity cannot change.");
        if (!approved(path, candidate.AssetId, before is null ? null : Convert.ToHexString(SHA256.HashData(before)), hash))
            throw new EditCommandRejectedException("graph_scope_denied", denied: true);
    }
    internal void RequireProposal(Guid id) { if(proposalCurrent is not null&&!proposalCurrent(id))throw new EditCommandRejectedException("graph_proposal_stale"); }
    internal void ValidateResources(string path,byte[]? before,byte[] after){var d=AnimationGraphCodec.Decode(after);validateResources?.Invoke(path,d.AssetId,before is null?null:Convert.ToHexString(SHA256.HashData(before)),Convert.ToHexString(SHA256.HashData(after)));}
    internal AnimationGraphWriteScope Bind(Func<bool> current) => new((path, id, before, after) => current() && approved(path, id, before, after), id => current() && (proposalCurrent?.Invoke(id) ?? true),validateResources);
}

public sealed class AnimationGraphCommands : IEditCommandParticipant, IEditCommandProposal, IDisposable
{
    public const string CapabilityName = "ncma.animgraph.transaction";
    private sealed record Proposal(string Path, byte[]? Before, byte[] After, ulong Revision);
    private readonly Dictionary<Guid, Proposal> _proposals = [];
    private readonly AssetProjectPaths _paths; private readonly AnimationGraphWriteScope _scope;
    private readonly AssetRevisionClock _clock; private readonly AssetDiskTransaction _disk;
    private readonly int _thread = Environment.CurrentManagedThreadId; private bool _disposed;
    public AnimationGraphCommands(AssetProjectPaths paths, AnimationGraphWriteScope scope, AssetRevisionClock clock, Action<string>? faultInjection = null)
    { _paths = paths; _scope = scope; _clock = clock; _disk = new(paths, faultInjection); }
    private static JsonElement Schema(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    public static CapabilityDescriptor Descriptor => new(CapabilityName, "Commit an exact host-reviewed animation graph proposal with checked file conflict detection and shared Undo.", MutationRisk.Reversible,
        Schema("""{"type":"object","additionalProperties":false,"required":["proposalId","expectedAssetRevision"],"properties":{"proposalId":{"type":"string","format":"uuid"},"expectedAssetRevision":{"type":"integer","minimum":0}}}"""), AssetMetadataCommands.Descriptor.OutputSchema);
    public void PrepareProposal(Guid id, string path, byte[]? before, AnimationGraphDefinition candidate, ulong revision)
    {
        Verify(); path = ValidatePath(path); if (id == Guid.Empty || _proposals.ContainsKey(id) || _proposals.Count >= 4 || revision != _clock.Revision) throw new EditCommandRejectedException("graph_proposal_stale_or_capacity");
        byte[] after = AnimationGraphCodec.Encode(candidate); if (before is not null) _ = AnimationGraphCodec.Decode(before);
        _proposals.Add(id, new(path, before?.ToArray(), after, revision)); // preparation does NOT grant writes.
    }
    public void CancelProposal(Guid id) { Verify(); _proposals.Remove(id); }
    public void ValidateProposal(JsonElement input) { _ = ProposalFor(input); } // pure: no filesystem/approval/World mutation.
    private Proposal ProposalFor(JsonElement input)
    {
        Verify(); AnimationGraphEdits.Closed(input, "proposalId", "expectedAssetRevision"); Guid id = AnimationGraphEdits.Uuid(input, "proposalId"); _scope.RequireProposal(id);
        if (!_proposals.TryGetValue(id, out var proposal) || !input.GetProperty("expectedAssetRevision").TryGetUInt64(out ulong revision) || revision != _clock.Revision || proposal.Revision != revision)
            throw new EditCommandRejectedException("graph_proposal_stale"); return proposal;
    }
    public ParticipantMemento Prepare(JsonElement input)
    {
        var p = ProposalFor(input); _scope.Require(p.Path, p.Before, p.After); NoJournal(p.Path);
        using var parents = new AssetDirectoryLease(_paths, [p.Path]); string full = _paths.Resolve(p.Path);
        byte[]? actual = null; if (Directory.Exists(full)) throw new ArgumentException("Graph file required.");
        if (File.Exists(full)) { using var file = WindowsAssetFile.Open(full); actual = file.Read(AnimationGraphCodec.MaxBytes); }
        if (actual is null ? p.Before is not null : p.Before is null || !actual.AsSpan().SequenceEqual(p.Before)) throw new EditCommandRejectedException("graph_file_conflict");
        return new(AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(p.Path, p.Before)]), AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(p.Path, p.After)]));
    }
    private static string PathOf(ParticipantMemento memento)
    {
        var (a, b) = AssetDiskTransaction.States(memento, true);
        if (a.Length != 1 || b.Length != 1 || a[0].Path != b[0].Path || a[0].Blob is not null || b[0].Blob is not null || b[0].Data is null) throw new ArgumentException("Graph-only memento required.");
        return ValidatePath(a[0].Path);
    }
    public void Authorize(ParticipantMemento memento) { Verify(); string path = PathOf(memento); var (a, b) = AssetDiskTransaction.States(memento, true); _scope.Require(path, a[0].Data, b[0].Data!); }
    public void Validate(ParticipantMemento memento, bool forward) { Authorize(memento);string path=PathOf(memento); NoJournal(path);var(a,b)=AssetDiskTransaction.States(memento,true);_scope.ValidateResources(path,a[0].Data,b[0].Data!); _disk.Validate(memento, forward); _ = checked(_clock.Revision + 1); }
    public void Publish(ParticipantMemento memento, bool forward) { Validate(memento, forward); _disk.Publish(memento, forward, PathOf(memento) + ".journal"); _clock.Advance(); }
    public void Complete(ParticipantMemento memento, bool forward) { Verify(); _disk.Complete(); _proposals.Clear(); }
    public void Compensate(ParticipantMemento memento, bool forward) { Verify(); _disk.Compensate(memento, forward, PathOf(memento) + ".journal"); }
    public void Recover(string path) { Verify(); path = ValidatePath(path); _disk.Recover(path + ".journal", Authorize); _clock.Advance(); _proposals.Clear(); }
    public byte[] Read(string path) { Verify(); path = ValidatePath(path); NoJournal(path); return UiAuthoringSource.Read(_paths.Root, path, AnimationGraphCodec.MaxBytes); }
    public void RequireMissing(string path) { Verify();path=ValidatePath(path);NoJournal(path);using var parents=new AssetDirectoryLease(_paths,[path]);string full=_paths.Resolve(path);if(File.Exists(full)||Directory.Exists(full))throw new EditCommandRejectedException("graph_file_conflict"); }
    public static string ValidatePath(string path) { path = AssetPaths.Validate(path); AnimationGraphCodec.RequireExtension(path);if(!path.EndsWith(".ncmaanim",StringComparison.Ordinal))throw new ArgumentException("Canonical lowercase graph extension required."); return path; }
    private void NoJournal(string path) { string full = _paths.Resolve(path + ".journal"); if (File.Exists(full) || Directory.Exists(full)) throw new EditCommandRejectedException("asset_recovery_required"); }
    private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Graph command owner thread required."); }
    public void Dispose() { if (_disposed) return; Verify(); _disk.Dispose(); _proposals.Clear(); _disposed = true; }
}
