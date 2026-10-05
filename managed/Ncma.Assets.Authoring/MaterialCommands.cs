using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Assets.Authoring.Storage;
namespace Ncma.Assets.Authoring;
// Trusted host grants, never reconstructed from MCP payload. No derived MAT1 modification.
public sealed class MaterialWriteScope
{
    private readonly Dictionary<string, Guid> _targets;
    private readonly Func<Guid, AssetKind, bool> _dependency;
    private readonly Func<bool> _current;
    public MaterialWriteScope(IEnumerable<KeyValuePair<string, Guid>> targets, Func<Guid, AssetKind, bool> dependency, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(targets); _dependency = dependency ?? throw new ArgumentNullException(nameof(dependency)); _current = current ?? throw new ArgumentNullException(nameof(current));
        _targets = targets.ToDictionary(p => AssetPaths.Validate(p.Key), p => p.Value, StringComparer.Ordinal);
        if (_targets.Count > 128 || _targets.Any(p => p.Value == Guid.Empty || !MaterialCommands.IsPath(p.Key))) throw new ArgumentException("Material grant.");
    }
    internal void Require(string path, Guid id, IEnumerable<Guid> dependencies, AssetKind kind)
    { if (!_current() || !_targets.TryGetValue(path, out var allowed) || allowed != id || dependencies.Any(d => !_dependency(d, kind))) throw new EditCommandRejectedException("material_scope_denied", denied: true); }
    internal MaterialWriteScope Bind(Func<bool> current) => new(_targets, _dependency, () => _current() && current());
}
public sealed class MaterialCommands : IEditCommandParticipant, IDisposable
{
    public const string CapabilityName = "ncma.assets.material.edit";
    private readonly AssetProjectPaths _paths; private readonly MaterialWriteScope _scope; private readonly AssetDiskTransaction _disk; private readonly AssetRevisionClock _clock;
    private readonly int _thread = Environment.CurrentManagedThreadId; private bool _disposed;
    public MaterialCommands(AssetProjectPaths paths, MaterialWriteScope scope, AssetRevisionClock clock, Action<string>? faultInjection = null)
    { _paths = paths ?? throw new ArgumentNullException(nameof(paths)); _scope = scope ?? throw new ArgumentNullException(nameof(scope)); _clock = clock ?? throw new ArgumentNullException(nameof(clock)); _disk = new(paths, faultInjection); }
    private static JsonElement Schema(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    public static CapabilityDescriptor Descriptor => new(CapabilityName, "Create or replace an explicitly approved UUID material/material-set authoring file; durable shared Undo, no imported data overwrite.", MutationRisk.Reversible,
        Schema("""{"type":"object","additionalProperties":false,"required":["path","expectedAssetRevision","document"],"properties":{"path":{"type":"string","maxLength":1024},"expectedAssetRevision":{"type":"integer","minimum":0},"document":{"type":"string","maxLength":4194304}}}"""), AssetMetadataCommands.Descriptor.OutputSchema);
    internal static bool IsPath(string path) => path.EndsWith(".ncmaterial", StringComparison.Ordinal) || path.EndsWith(".ncmatset", StringComparison.Ordinal);
    private Guid Check(string path, byte[] bytes)
    {
        if (path.EndsWith(".ncmaterial", StringComparison.Ordinal)) { var d = MaterialCodec.Decode(bytes); _scope.Require(path, d.AssetId, d.TextureIds.Where(id => id != Guid.Empty), AssetKind.Texture); return d.AssetId; }
        if (path.EndsWith(".ncmatset", StringComparison.Ordinal)) { var d = MaterialCodec.DecodeSet(bytes); _scope.Require(path, d.AssetId, d.Materials, AssetKind.Material); return d.AssetId; }
        throw new ArgumentException("Authoring material extension required.");
    }
    public ParticipantMemento Prepare(JsonElement input)
    {
        Verify(); if (input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Material input.");
        var fields = input.EnumerateObject().ToArray(); string[] names = ["path", "expectedAssetRevision", "document"];
        if (fields.Length != 3 || fields.Select(p => p.Name).Distinct().Count() != 3 || fields.Any(p => !names.Contains(p.Name))) throw new ArgumentException("Closed material command.");
        if (input.GetProperty("expectedAssetRevision").GetUInt64() != _clock.Revision) throw new EditCommandRejectedException("asset_revision_conflict");
        string path = AssetPaths.Validate(input.GetProperty("path").GetString() ?? ""); byte[] after = Encoding.UTF8.GetBytes(input.GetProperty("document").GetString() ?? "");
        Guid id = Check(path, after); RequireNoJournal(path); using var parents = new AssetDirectoryLease(_paths, [path]);
        byte[]? before = null; string full = _paths.Resolve(path); if (Directory.Exists(full)) throw new ArgumentException("File target required.");
        if (File.Exists(full)) { using var file = WindowsAssetFile.Open(full); before = file.Read(4 * 1024 * 1024); if (Check(path, before) != id) throw new ArgumentException("Material identity cannot change."); }
        return new(AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(path, before)]), AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(path, after)]));
    }
    private static string PathOf(ParticipantMemento memento)
    {
        var (a, b) = AssetDiskTransaction.States(memento, true);
        if (a.Length != 1 || b.Length != 1 || a[0].Blob is not null || b[0].Blob is not null || a[0].Path != b[0].Path || !IsPath(a[0].Path) || b[0].Data is null) throw new ArgumentException("Material-only memento.");
        return a[0].Path;
    }
    public void Authorize(ParticipantMemento memento)
    {
        Verify(); string path = PathOf(memento); var (a, b) = AssetDiskTransaction.States(memento, true); Guid id = Check(path, b[0].Data!);
        if (a[0].Data is { } bytes && Check(path, bytes) != id) throw new ArgumentException("Material history identity mismatch.");
    }
    public void Validate(ParticipantMemento memento, bool forward) { Authorize(memento); RequireNoJournal(PathOf(memento)); _disk.Validate(memento, forward); _ = checked(_clock.Revision + 1); }
    public void Publish(ParticipantMemento memento, bool forward) { Validate(memento, forward); _disk.Publish(memento, forward, PathOf(memento) + ".journal"); _clock.Advance(); }
    public void Complete(ParticipantMemento memento, bool forward) { Verify(); _disk.Complete(); }
    public void Compensate(ParticipantMemento memento, bool forward) { Verify(); _disk.Compensate(memento, forward, PathOf(memento) + ".journal"); }
    public void Recover(string path) { Verify(); if (!IsPath(path)) throw new ArgumentException("Material journal path."); _disk.Recover(AssetPaths.Validate(path) + ".journal", Authorize); _clock.Advance(); }
    private void RequireNoJournal(string path) { string full = _paths.Resolve(path + ".journal"); if (File.Exists(full) || Directory.Exists(full)) throw new EditCommandRejectedException("asset_recovery_required"); }
    private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Material commands require owner thread."); }
    public void Dispose() { if (_disposed) return; Verify(); _disk.Dispose(); _disposed = true; }
}
