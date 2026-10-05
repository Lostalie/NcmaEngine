using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Editor.Core;
using Ncma.Assets.Authoring.Storage;

namespace Ncma.Assets.Authoring;

// Metadata create/settings participant; source relocation uses the separate exact file capability.
public sealed class AssetMetadataCommands : IEditCommandParticipant, IDisposable
{
    public const string CapabilityName = "ncma.assets.metadata.edit";
    private readonly AssetProjectPaths _paths;
    private readonly AssetWriteScope _scope;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly AssetDiskTransaction _disk;
    private readonly AssetRevisionClock _clock;
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FileState([property: JsonRequired] string Path, [property: JsonRequired] byte[]? Content);

    public AssetMetadataCommands(AssetProjectPaths paths, AssetWriteScope scope, Action<string>? faultInjection = null, AssetRevisionClock? clock = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope)); _disk = new(paths, faultInjection); _clock = clock ?? new();
    }
    public ulong AssetRevision => _clock.Revision;

    public static CapabilityDescriptor Descriptor => new(CapabilityName, "Create asset metadata or replace only import settings with durable Undo.",
        MutationRisk.Reversible, Element("""
        {"type":"object","additionalProperties":false,"required":["operation","path","expectedAssetRevision","record"],
         "properties":{"operation":{"enum":["create","settings"]},"path":{"type":"string","maxLength":1024},
         "expectedAssetRevision":{"type":"integer","minimum":0},"record":{"$ref":"#/$defs/record"}},
         "$defs":{"record":{"type":"object","additionalProperties":false,
         "required":["version","assetId","kind","sourcePath","sourceHash","importer","importerVersion","settings","subassets","dependencies","generation"],
         "properties":{"version":{"const":1},"assetId":{"type":"string","format":"uuid"},
         "kind":{"enum":["character","staticMesh","skinnedMesh","skeleton","clip","texture","material","materialSet","prefab","overrideSet"]},
         "sourcePath":{"type":"string","maxLength":1024},"sourceHash":{"type":"string","pattern":"^[0-9A-F]{64}$"},
         "importer":{"type":"string","minLength":1,"maxLength":128},"importerVersion":{"type":"integer","minimum":1},
         "settings":{"type":"object","additionalProperties":false,"required":["version","sampleRate","generateTangents"],
           "properties":{"version":{"const":1},"sampleRate":{"type":"integer","minimum":1,"maximum":120},"generateTangents":{"type":"boolean"}}},
         "subassets":{"type":"array","maxItems":0},"dependencies":{"type":"array","maxItems":0},"generation":{"type":"null"}}}}
        }
        """), Element("""
        {"type":"object","additionalProperties":false,
         "required":["contractVersion","requestId","sessionId","revision","status","code","changed","data","executionRevision","replayed"],
         "properties":{"contractVersion":{"const":2},"requestId":{"type":"string","format":"uuid"},"sessionId":{"type":"string","format":"uuid"},
         "revision":{"type":"integer","minimum":0},"status":{"enum":["ok","error","conflict","denied"]},"code":{"type":"string"},
         "changed":{"type":"boolean"},"data":{"type":"object","additionalProperties":false,
           "properties":{"history":{"$ref":"#/$defs/history"},"message":{"type":"string"},
           "execution":{"type":"object","additionalProperties":false,"required":["history"],"properties":{"history":{"$ref":"#/$defs/history"}}}}},
         "executionRevision":{"type":"integer","minimum":0},"replayed":{"type":"boolean"}},
         "$defs":{"history":{"type":"object","additionalProperties":false,
           "required":["sessionId","revision","undoCount","redoCount","undoLabel","redoLabel","dirty","historyInvalidated","editBusy","frozen","selection","filePath"],
           "properties":{"sessionId":{"type":"string","format":"uuid"},"revision":{"type":"integer","minimum":0},
             "undoCount":{"type":"integer","minimum":0,"maximum":64},"redoCount":{"type":"integer","minimum":0,"maximum":64},
             "undoLabel":{"type":"string"},"redoLabel":{"type":"string"},"dirty":{"type":"boolean"},"historyInvalidated":{"type":"boolean"},
             "editBusy":{"type":"boolean"},"frozen":{"type":"boolean"},"selection":{"type":["string","null"],"format":"uuid"},"filePath":{"type":["string","null"]}}}}}
        """));

    public ParticipantMemento Prepare(JsonElement input)
    {
        Verify();
        if (input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected metadata command object.");
        string[] names = ["operation", "path", "expectedAssetRevision", "record"];
        var properties = input.EnumerateObject().ToArray();
        if (properties.Length != 4 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 4 ||
            properties.Any(p => !names.Contains(p.Name, StringComparer.Ordinal))) throw new ArgumentException("Invalid metadata command fields.");
        if (input.GetProperty("expectedAssetRevision").GetUInt64() != AssetRevision) throw new EditCommandRejectedException("asset_revision_conflict");
        string path = input.GetProperty("path").GetString() ?? throw new ArgumentException("Missing metadata path.");
        if (!path.EndsWith(".ncmeta", StringComparison.Ordinal)) throw new ArgumentException("Expected .ncmeta.");
        var record = AssetRecordCodec.Decode(System.Text.Encoding.UTF8.GetBytes(input.GetProperty("record").GetRawText()));
        // Initial authoring slice has no imported generations. G2 will add a separately validated import commit.
        if (record.Subassets.Length != 0 || record.Dependencies.Length != 0 || record.Generation is not null)
            throw new ArgumentException("Imported metadata requires a future import-commit capability.");
        _scope.Require(path, record); _paths.Resolve(record.SourcePath, requireFile: true);
        using var parents = new AssetDirectoryLease(_paths, [path, record.SourcePath]);
        using (var source = WindowsAssetFile.Open(_paths.Resolve(record.SourcePath, requireFile: true)))
            if (source.Hash(AssetDiskTransaction.MaxSourceBytes) != record.SourceHash) throw new EditCommandRejectedException("asset_source_changed");
        string full = _paths.Resolve(path); RequireNoJournal(path);
        byte[]? before = Read(full);
        string? op = input.GetProperty("operation").GetString();
        if (op == "create")
        {
            if (before is not null) throw new InvalidOperationException("Metadata already exists.");
            var catalog = new AssetCatalogScanner(_paths).Scan().Catalog;
            _ = new AssetCatalog(catalog.List(0, 128).Concat(AllRemaining(catalog)).Append(record));
        }
        else if (op == "settings")
        {
            if (before is null) throw new InvalidOperationException("Metadata does not exist.");
            var original = AssetRecordCodec.Decode(before);
            if (!AssetRecordCodec.Encode(original with { Settings = record.Settings }).AsSpan().SequenceEqual(AssetRecordCodec.Encode(record)))
                throw new ArgumentException("Settings command cannot change identity, source, dependency or generation.");
        }
        else throw new ArgumentException("Unknown metadata command.");
        return new(AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(path, before)]),
            AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(path, AssetRecordCodec.Encode(record))]));
    }

    private static IEnumerable<AssetRecord> AllRemaining(AssetCatalog catalog)
    {
        for (int offset = 128; offset < catalog.Count; offset += 128) foreach (var record in catalog.List(offset, 128)) yield return record;
    }

    public void Validate(ParticipantMemento memento, bool forward)
    {
        Verify(); var (before, after) = States(memento, forward);
        Authorize(memento);
        RequireNoJournal(before.Path);
        _disk.Validate(memento, forward);
        _ = checked(AssetRevision + 1);
    }

    public void Authorize(ParticipantMemento memento)
    {
        Verify(); var (before, after) = States(memento, true);
        if (before.Content is not null) _scope.Require(before.Path, AssetRecordCodec.Decode(before.Content));
        if (after.Content is not null) _scope.Require(after.Path, AssetRecordCodec.Decode(after.Content));
    }

    public void Publish(ParticipantMemento memento, bool forward)
    {
        Validate(memento, forward); var (before, after) = States(memento, forward);
        _disk.Publish(memento, forward, JournalRelative(before.Path));
        _clock.Advance();
    }
    public void Complete(ParticipantMemento memento, bool forward) { Verify(); _disk.Complete(); }
    public void Dispose() => _disk.Dispose();

    public void Compensate(ParticipantMemento memento, bool forward)
    {
        Verify(); var (before, after) = States(memento, forward);
        _disk.Compensate(memento, forward, JournalRelative(before.Path));
        // A completed publish consumed a monotonic revision even if World installation was compensated.
    }

    public void Recover(string metadataPath)
    {
        Verify();
        _disk.Recover(JournalRelative(metadataPath), Authorize); _clock.Advance();
    }

    private static (FileState Before, FileState After) States(ParticipantMemento memento, bool forward)
    {
        var (aImages, bImages) = AssetDiskTransaction.States(memento, true);
        if (aImages.Length != 1 || aImages[0].Blob is not null || bImages[0].Blob is not null) throw new ArgumentException("Expected metadata-only history.");
        var before = new FileState(aImages[0].Path, aImages[0].Data); var after = new FileState(bImages[0].Path, bImages[0].Data);
        if (!before.Path.EndsWith(".ncmeta", StringComparison.Ordinal)) throw new ArgumentException("Expected metadata history.");
        if (before.Path != after.Path || (before.Content is null && after.Content is null)) throw new ArgumentException("Mismatched metadata history.");
        if (before.Content is not null && after.Content is not null)
        {
            var a = AssetRecordCodec.Decode(before.Content); var b = AssetRecordCodec.Decode(after.Content);
            if (!AssetRecordCodec.Encode(a with { Settings = b.Settings }).AsSpan().SequenceEqual(AssetRecordCodec.Encode(b)))
                throw new ArgumentException("History changes more than import settings.");
        }
        return forward ? (before, after) : (after, before);
    }
    private static byte[]? Read(string full)
    {
        if (!File.Exists(full)) { if (Directory.Exists(full)) throw new ArgumentException("Metadata target is a directory."); return null; }
        using var stream = WindowsAssetFile.Open(full);
        if (stream.Length > 3 * AssetRecordCodec.MaxBytes) throw new ArgumentException("Metadata/journal exceeds budget.");
        return stream.Read(3 * AssetRecordCodec.MaxBytes);
    }
    private string JournalRelative(string path) => AssetPaths.Validate(path) + ".journal";
    private string JournalPath(string path) => _paths.Resolve(JournalRelative(path));
    private void RequireNoJournal(string path)
    {
        if (File.Exists(JournalPath(path)) || Directory.Exists(JournalPath(path))) throw new EditCommandRejectedException("asset_recovery_required");
    }
    private void Verify()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Asset commands require owner thread.");
    }
    private static JsonElement Element(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
}
