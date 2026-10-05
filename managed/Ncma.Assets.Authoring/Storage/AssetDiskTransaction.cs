using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring.Storage;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DiskImage([property: JsonRequired] string Path, [property: JsonRequired] byte[]? Data,
    [property: JsonRequired] string? Blob, [property: JsonRequired] string? Hash);

// A bounded, recoverable multi-file transaction. All removals operate on verified owned handles.
// The journal is retained until Core confirms publication; restart rolls an incomplete publication back.
internal sealed class AssetDiskTransaction(AssetProjectPaths paths, Action<string>? fault = null) : IDisposable
{
    public const int MaxSourceBytes = 256 * 1024 * 1024, MaxJournalBytes = 16 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 24 };
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Step([property: JsonRequired] string Path, [property: JsonRequired] string Backup,
        [property: JsonRequired] string Stage, [property: JsonRequired] string? BeforeId, [property: JsonRequired] string? AfterId,
        [property: JsonRequired] string? BeforeHash, [property: JsonRequired] string? AfterHash);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Journal([property: JsonRequired] int Version, [property: JsonRequired] Guid Transaction,
        [property: JsonRequired] bool Committed, [property: JsonRequired] ParticipantMemento Memento,
        [property: JsonRequired] bool Forward, [property: JsonRequired] Step[] Steps);
    private readonly List<WindowsAssetFile> _files = [];
    private readonly List<WindowsAssetFile> _created = [];
    private AssetDirectoryLease? _directories;
    private WindowsAssetFile? _journalFile;
    private Journal? _journal;

    public static byte[] Encode(DiskImage[] images) => JsonSerializer.SerializeToUtf8Bytes(images, Json);
    public static DiskImage[] Decode(byte[] bytes)
    {
        if (bytes.Length > MaxJournalBytes) throw new ArgumentException("Asset memento exceeds budget.");
        RejectDuplicates(bytes);
        var images = JsonSerializer.Deserialize<DiskImage[]>(bytes, Json) ?? throw new ArgumentException("Invalid asset memento.");
        if (images.Length is < 1 or > 4 || images.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != images.Length)
            throw new ArgumentException("Invalid asset file set.");
        foreach (var image in images)
        {
            AssetPaths.Validate(image.Path);
            if (image.Hash is null) { if (image.Data is not null || image.Blob is not null) throw new ArgumentException("Invalid absent image."); }
            else
            {
                AssetRecordCodec.ValidateHash(image.Hash);
                if ((image.Data is null) == (image.Blob is null)) throw new ArgumentException("Expected exactly one immutable image.");
                if (image.Data is not null && (image.Data.Length > AssetRecordCodec.MaxBytes || Hash(image.Data) != image.Hash))
                    throw new ArgumentException("Invalid inline image.");
                if (image.Blob is not null) AssetPaths.Validate(image.Blob, "out/asset-authoring");
            }
        }
        return images;
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static DiskImage Inline(string path, byte[]? bytes) => new(path, bytes, null, bytes is null ? null : Hash(bytes));
    public static (DiskImage[] Before, DiskImage[] After) States(ParticipantMemento memento, bool forward)
    {
        var a = Decode(memento.Before); var b = Decode(memento.After);
        if (!a.Select(i => i.Path).SequenceEqual(b.Select(i => i.Path))) throw new ArgumentException("Mismatched asset file paths.");
        return forward ? (a, b) : (b, a);
    }
    public void Validate(ParticipantMemento memento, bool forward)
    {
        var (before, _) = States(memento, forward);
        using var directories = new AssetDirectoryLease(paths, before.Select(i => i.Path));
        foreach (var image in before)
        {
            string full = paths.Resolve(image.Path);
            if (File.Exists(full)) { using var file = WindowsAssetFile.Open(full); if (file.Hash(MaxSourceBytes) != image.Hash) Conflict(); }
            else if (image.Hash is not null || Directory.Exists(full)) Conflict();
        }
    }
    public void Publish(ParticipantMemento memento, bool forward, string journalPath)
    {
        if (_journal is not null) throw new InvalidOperationException("A publication is already pending.");
        var (before, after) = States(memento, forward); Guid id = Guid.NewGuid();
        _directories = new(paths, before.Select(i => i.Path).Append(journalPath));
        var steps = new List<Step>();
        bool durable = false;
        try
        {
            for (int i = 0; i < before.Length; i++)
            {
                var a = before[i]; var b = after[i];
                string full = paths.Resolve(a.Path);
                WindowsAssetFile? old = File.Exists(full) ? Track(WindowsAssetFile.Open(full)) : null;
                if (old?.Hash(MaxSourceBytes) != a.Hash || Directory.Exists(full)) Conflict();
                string backup = a.Path + "." + id.ToString("N") + ".before";
                string stage = a.Path + "." + id.ToString("N") + ".after";
                WindowsAssetFile? candidate = null;
                if (b.Hash is not null && b.Hash != a.Hash)
                {
                    candidate = Track(WindowsAssetFile.Open(paths.Resolve(stage), create: true));
                    _created.Add(candidate);
                    if (b.Data is not null) candidate.Write(b.Data);
                    else
                    {
                        string blob = paths.ResolveCache(b.Blob!);
                        using var cacheLease = new AssetDirectoryLease(paths, [b.Blob!]);
                        using var source = WindowsAssetFile.Open(blob); source.CopyTo(candidate, MaxSourceBytes);
                    }
                    if (candidate.Hash(MaxSourceBytes) != b.Hash) Conflict();
                }
                steps.Add(new(a.Path, backup, stage, old?.Identity, a.Hash == b.Hash ? old?.Identity : candidate?.Identity, a.Hash, b.Hash));
            }
            _journal = new(1, id, false, memento, forward, steps.ToArray());
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(_journal, Json);
            if (bytes.Length > MaxJournalBytes) throw new ArgumentException("Asset journal exceeds budget.");
            _journalFile = WindowsAssetFile.Open(paths.Resolve(journalPath), create: true); _journalFile.Write(bytes);
            durable = true;
            fault?.Invoke("journal_created");
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step.BeforeId == step.AfterId) continue;
                if (step.BeforeId is not null) _files.Single(f => f.Identity == step.BeforeId).Rename(paths.Resolve(step.Backup));
                fault?.Invoke("backup:" + i);
                if (step.AfterId is not null) _files.Single(f => f.Identity == step.AfterId).Rename(paths.Resolve(step.Path));
                fault?.Invoke("published:" + i);
            }
            fault?.Invoke("metadata_published");
        }
        catch
        {
            // Once the journal exists, compensation/restart owns all generated files.
            if (!durable)
            {
                try { foreach (var created in _created) created.Delete(); _journalFile?.Delete(); }
                finally { Dispose(); }
            }
            throw;
        }
    }
    public void Complete()
    {
        if (_journal is null || _journalFile is null) return;
        fault?.Invoke("completion_ready");
        // Durable decision before garbage collection; recovery of a committed journal never rolls it back.
        _journal = _journal with { Committed = true };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(_journal, Json);
        // Committed:true is one byte shorter: pad to the original length with JSON whitespace.
        byte[] padded = new byte[checked((int)_journalFile.Length)]; Array.Fill(padded, (byte)' '); bytes.CopyTo(padded, 0);
        _journalFile.Write(padded);
        fault?.Invoke("commit_decided");
        Cleanup(_journal); _journalFile.Delete(); Dispose();
    }
    public void Compensate(ParticipantMemento memento, bool forward, string journalPath)
    {
        if (_journal is null)
        {
            if (!File.Exists(paths.Resolve(journalPath))) return;
            Recover(journalPath, value => {
                if (!value.Before.SequenceEqual(memento.Before) || !value.After.SequenceEqual(memento.After)) Conflict();
            }); return;
        }
        var journal = _journal; Dispose(); // Close leases before restart-style recovery; identity checks prevent overwriting strangers.
        if (journal.Committed) throw new InvalidOperationException("Publication is already committed.");
        Recover(journalPath, _ => { });
    }
    public void Recover(string journalPath, Action<ParticipantMemento> authorize)
    {
        AssetPaths.Validate(journalPath);
        using var directory = new AssetDirectoryLease(paths, [journalPath]);
        using var file = WindowsAssetFile.Open(paths.Resolve(journalPath, requireFile: true));
        byte[] bytes = file.Read(MaxJournalBytes); RejectDuplicates(bytes);
        var journal = JsonSerializer.Deserialize<Journal>(bytes, Json) ?? throw new ArgumentException("Invalid asset journal.");
        ValidateJournal(journalPath, journal); authorize(journal.Memento);
        using var parents = new AssetDirectoryLease(paths, journal.Steps.SelectMany(s => new[] { s.Path, s.Backup, s.Stage }));
        if (!journal.Committed)
        {
            foreach (var step in journal.Steps.Reverse())
            {
                if (step.BeforeId == step.AfterId) continue;
                string full = paths.Resolve(step.Path);
                if (File.Exists(full))
                {
                    using var current = WindowsAssetFile.Open(full);
                    if (current.Identity == step.AfterId && current.Hash(MaxSourceBytes) == step.AfterHash)
                        current.Rename(paths.Resolve(step.Stage));
                    else if (current.Identity != step.BeforeId || current.Hash(MaxSourceBytes) != step.BeforeHash) Conflict();
                }
                if (File.Exists(paths.Resolve(step.Backup)))
                {
                    using var backup = WindowsAssetFile.Open(paths.Resolve(step.Backup));
                    if (backup.Identity != step.BeforeId || backup.Hash(MaxSourceBytes) != step.BeforeHash) Conflict();
                    backup.Rename(paths.Resolve(step.Path));
                }
                else if (step.BeforeId is not null && !File.Exists(full)) Conflict();
            }
        }
        Cleanup(journal); file.Delete();
    }
    private void Cleanup(Journal journal)
    {
        foreach (var step in journal.Steps)
        {
            if (step.BeforeId == step.AfterId) continue;
            foreach (var (path, identity, hash) in new[] { (step.Backup, step.BeforeId, step.BeforeHash), (step.Stage, step.AfterId, step.AfterHash) })
            {
                string full = paths.Resolve(path);
                if (!File.Exists(full)) continue;
                var live = _files.FirstOrDefault(f => f.Identity == identity);
                if (live is not null) { if (live.Hash(MaxSourceBytes) != hash) Conflict(); live.Delete(); }
                else { using var owned = WindowsAssetFile.Open(full); if (owned.Identity != identity || owned.Hash(MaxSourceBytes) != hash) Conflict(); owned.Delete(); }
            }
        }
    }
    private static void ValidateJournal(string path, Journal journal)
    {
        var (a, b) = States(journal.Memento, journal.Forward);
        bool supported = path.EndsWith(".ncmeta.journal", StringComparison.Ordinal) || path.EndsWith(".ncmaterial.journal", StringComparison.Ordinal) || path.EndsWith(".ncmatset.journal", StringComparison.Ordinal);
        if (journal.Version != 1 || journal.Transaction == Guid.Empty || journal.Steps.Length != a.Length || !supported ||
            !a.Any(i => i.Path + ".journal" == path)) throw new ArgumentException("Journal target/version mismatch.");
        for (int i = 0; i < a.Length; i++)
        {
            var s = journal.Steps[i]; string prefix = a[i].Path + "." + journal.Transaction.ToString("N");
            if (s.Path != a[i].Path || s.Backup != prefix + ".before" || s.Stage != prefix + ".after" ||
                s.BeforeHash != a[i].Hash || s.AfterHash != b[i].Hash || (s.BeforeId is null) != (s.BeforeHash is null) ||
                (s.AfterId is null) != (s.AfterHash is null)) throw new ArgumentException("Invalid journal file set.");
            AssetPaths.Validate(s.Backup); AssetPaths.Validate(s.Stage);
        }
    }
    internal static void RejectDuplicates(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        void Visit(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new ArgumentException("Duplicate journal field."); Visit(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Visit(item);
        }
        Visit(document.RootElement);
    }
    private WindowsAssetFile Track(WindowsAssetFile file) { _files.Add(file); return file; }
    private static void Conflict() => throw new EditCommandRejectedException("asset_file_conflict");
    public void Dispose()
    {
        _journalFile?.Dispose(); _journalFile = null;
        foreach (var file in _files) file.Dispose(); _files.Clear();
        _created.Clear(); _directories?.Dispose(); _directories = null; _journal = null;
    }
}
