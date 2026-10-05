using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Assets.Authoring.Storage;

namespace Ncma.Assets.Authoring;

public sealed record PreparedImportInfo(Guid Ticket, Guid JobId, bool RequiresIdentityConfirmation,
    AssetRecord Record, ImportIdentityChange[] Changes, AssetDiagnostic[] Diagnostics);

// Only this participant installs imported descriptors into the sole EditSession history.
// Large data is prepared/pinned asynchronously; memento contains bounded metadata, never mesh bytes.
public sealed class AssetImportCommands : IEditCommandParticipant, IDisposable
{
    public const string CapabilityName = "ncma.assets.import.commit";
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly AssetProjectPaths _paths;
    private readonly AssetWriteScope _scope;
    private readonly Guid _project;
    private readonly ulong _generation;
    private readonly AssetRevisionClock _clock;
    private readonly Func<bool> _current;
    private readonly AssetDiskTransaction _disk;
    private readonly Action<string>? _fault;
    private readonly Dictionary<Guid, Ticket> _tickets = [];
    private bool _closed;
    private readonly CancellationTokenSource _preloadCancellation = new();
    private Task _preload = Task.CompletedTask;
    private sealed record Prepared(PreparedImportInfo Info, ParticipantMemento Memento);
    private sealed record Ticket(Guid JobId, ImportCoordinator Coordinator, ulong Revision, Task<Prepared> Task);
    public DerivedGenerationStore Generations { get; }
    public Task PrepareStoredGenerationsAsync(IEnumerable<AssetRecord> records)
    {
        Verify(); if (!_preload.IsCompleted) throw new EditCommandRejectedException("asset_generation_preparing");
        var owned = records.Where(r => r.Generation is not null).Select(r => AssetRecordCodec.Decode(AssetRecordCodec.Encode(r))).Take(DerivedGenerationStore.MaxGenerations + 1).ToArray();
        if (owned.Length > DerivedGenerationStore.MaxGenerations) throw new EditCommandRejectedException("asset_generation_budget");
        var token = _preloadCancellation.Token;
        return _preload = owned.Length == 0 ? Task.CompletedTask : Task.Run(() => {
            foreach (var record in owned) { token.ThrowIfCancellationRequested(); Generations.Prepare(record, null, token); }
        });
    }
    public AssetImportCommands(AssetProjectPaths paths, AssetWriteScope scope, Guid project, ulong generation,
        AssetRevisionClock clock, Func<bool> current, Action<string>? fault = null)
    { _paths = paths; _scope = scope; _project = project; _generation = generation; _clock = clock; _current = current; _fault = fault;
        _disk = new(paths, fault); Generations = new(paths, project); }
    public static CapabilityDescriptor Descriptor => new(CapabilityName, "Publish a prepared FBX generation with exact grants, conflict confirmation and shared Undo.", MutationRisk.Reversible,
        JsonSerializer.SerializeToElement(new {
            type = "object", additionalProperties = false, required = new[] { "ticket", "expectedAssetRevision", "projectGeneration", "confirmNewIdentities" },
            properties = new { ticket = new { type = "string", format = "uuid" }, expectedAssetRevision = new { type = "integer", minimum = 0 },
                projectGeneration = new { type = "integer", minimum = 1 }, confirmNewIdentities = new { type = "boolean" } } }), AssetMetadataCommands.Descriptor.OutputSchema);
    public Guid PrepareAsync(ImportCoordinator coordinator, Guid jobId, string metadataPath, AssetRecord requested)
    {
        Verify(); if (!_current()) throw new EditCommandRejectedException("asset_project_stale");
        AssetPaths.Validate(metadataPath); if (!metadataPath.EndsWith(".ncmeta", StringComparison.Ordinal)) throw new ArgumentException("Expected metadata.");
        _scope.Require(metadataPath, requested); byte[] requestedBytes = AssetRecordCodec.Encode(requested); var owned = AssetRecordCodec.Decode(requestedBytes);
        var candidate = coordinator.RequireReady(jobId, _clock.Revision, _generation);
        if (candidate.ProjectId != _project || candidate.SourcePath != requested.SourcePath || candidate.SourceHash != requested.SourceHash ||
            candidate.SampleRate != requested.Settings.SampleRate || candidate.StaticOnly != (requested.Kind == AssetKind.StaticMesh)) throw new EditCommandRejectedException("asset_import_candidate_mismatch");
        if (_tickets.Count >= 4) throw new EditCommandRejectedException("asset_import_prepare_budget");
        Guid ticketId = Guid.NewGuid(); ulong revision = _clock.Revision;
        Task<Prepared> task = coordinator.PrepareAsync<Prepared>(jobId, (model, info, cancellation) => {
            using var parents = new AssetDirectoryLease(_paths, [metadataPath]); byte[]? before = null;
            if (File.Exists(_paths.Resolve(metadataPath))) { using var file = WindowsAssetFile.OpenReadLease(_paths.Resolve(metadataPath)); before = file.Read(AssetRecordCodec.MaxBytes); }
            AssetRecord? previous = before is null ? null : AssetRecordCodec.Decode(before);
            if (previous?.Generation is not null) Generations.Prepare(previous, null, cancellation);
            var plan = ModelImportPlanner.Build(model, owned, _project, info.StaticOnly, previous, cancellation);
            // Global identity/source collision preflight is off owner. Owner still rechecks exact scope/revision.
            var catalog = new AssetCatalogScanner(_paths).Scan().Catalog;
            var all = new List<AssetRecord>(); for (int offset = 0; offset < catalog.Count; offset += 128) all.AddRange(catalog.List(offset, 128));
            if (previous is null && all.Any(r => r.AssetId == plan.Record.AssetId)) throw new EditCommandRejectedException("asset_duplicate_uuid");
            _ = new AssetCatalog(all.Where(r => r.AssetId != plan.Record.AssetId).Append(plan.Record));
            Generations.Prepare(plan.Record, plan.DerivedBytes, cancellation, _fault); cancellation.ThrowIfCancellationRequested();
            var memento = new ParticipantMemento(AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(metadataPath, before)]),
                AssetDiskTransaction.Encode([AssetDiskTransaction.Inline(metadataPath, AssetRecordCodec.Encode(plan.Record))]));
            return new(new(ticketId, jobId, plan.RequiresConfirmation, plan.Record, plan.Changes, plan.Diagnostics), memento);
        });
        _tickets.Add(ticketId, new(jobId, coordinator, revision, task)); return ticketId;
    }
    public bool IsPrepared(Guid ticket) { Verify(); return Find(ticket).Task.IsCompleted; }
    public PreparedImportInfo InspectPrepared(Guid ticket)
    {
        Verify(); var t = Find(ticket); if (!t.Task.IsCompleted) throw new EditCommandRejectedException("asset_import_preparing");
        var info = t.Task.GetAwaiter().GetResult().Info;
        // No mutable arrays escape the trusted prepared state.
        return info with { Record = AssetRecordCodec.Decode(AssetRecordCodec.Encode(info.Record)), Changes = (ImportIdentityChange[])info.Changes.Clone(), Diagnostics = (AssetDiagnostic[])info.Diagnostics.Clone() };
    }
    public ParticipantMemento Prepare(JsonElement input)
    {
        Verify(); string[] names = ["ticket", "expectedAssetRevision", "projectGeneration", "confirmNewIdentities"];
        if (input.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected import command.");
        var fields = input.EnumerateObject().ToArray(); if (fields.Length != 4 || fields.Select(p => p.Name).Distinct().Count() != 4 || fields.Any(p => !names.Contains(p.Name))) throw new ArgumentException("Invalid import fields.");
        if (!_current() || input.GetProperty("projectGeneration").GetUInt64() != _generation || input.GetProperty("expectedAssetRevision").GetUInt64() != _clock.Revision)
            throw new EditCommandRejectedException("asset_project_stale");
        var ticket = Find(input.GetProperty("ticket").GetGuid());
        if (ticket.Revision != _clock.Revision) throw new EditCommandRejectedException("asset_revision_conflict");
        ticket.Coordinator.RequireReady(ticket.JobId, _clock.Revision, _generation);
        if (!ticket.Task.IsCompleted) throw new EditCommandRejectedException("asset_import_preparing"); var prepared = ticket.Task.GetAwaiter().GetResult();
        if (prepared.Info.RequiresIdentityConfirmation && !input.GetProperty("confirmNewIdentities").GetBoolean()) throw new EditCommandRejectedException("asset_identity_confirmation_required");
        Authorize(prepared.Memento); return prepared.Memento;
    }
    private Ticket Find(Guid id) => _tickets.TryGetValue(id, out var ticket) ? ticket : throw new EditCommandRejectedException("asset_import_ticket_unknown");
    public void Authorize(ParticipantMemento memento)
    {
        Verify(); if (!_current()) throw new EditCommandRejectedException("asset_project_stale");
        var (before, after) = States(memento);
        foreach (var image in new[] { before, after }) if (image.Data is not null) { var record = AssetRecordCodec.Decode(image.Data); _scope.Require(image.Path, record);
            if (record.Kind is not (AssetKind.Character or AssetKind.StaticMesh)) throw new ArgumentException("Unexpected import kind."); }
        if (before.Data is not null && after.Data is not null) { var a = AssetRecordCodec.Decode(before.Data); var b = AssetRecordCodec.Decode(after.Data);
            if (a.AssetId != b.AssetId || a.SourcePath != b.SourcePath || a.Kind != b.Kind) throw new ArgumentException("Import history changes identity/source/kind."); }
    }
    public void Validate(ParticipantMemento memento, bool forward)
    {
        Authorize(memento); var (before, after) = States(memento);
        foreach (var image in new[] { before, after }) if (image.Data is not null) Generations.RequirePinned(AssetRecordCodec.Decode(image.Data));
        if (File.Exists(_paths.Resolve(before.Path + ".journal"))) throw new EditCommandRejectedException("asset_recovery_required");
        _disk.Validate(memento, forward); _ = checked(_clock.Revision + 1);
    }
    public void Publish(ParticipantMemento memento, bool forward)
    {
        Validate(memento, forward); _disk.Publish(memento, forward, States(memento).Before.Path + ".journal"); _clock.Advance();
    }
    public void Complete(ParticipantMemento memento, bool forward)
    {
        _disk.Complete();
        // Source/candidate leases can now be retired; immutable generations remain pinned for all history/cache replay.
        foreach (var pair in _tickets.ToArray()) if (pair.Value.Task.IsCompletedSuccessfully && pair.Value.Task.Result.Memento.After.SequenceEqual(memento.After))
        { pair.Value.Coordinator.Cancel(pair.Value.JobId); _tickets.Remove(pair.Key); }
    }
    public void Compensate(ParticipantMemento memento, bool forward) => _disk.Compensate(memento, forward, States(memento).Before.Path + ".journal");
    public void Recover(string metadataPath) { Verify(); _disk.Recover(metadataPath + ".journal", Authorize); _clock.Advance(); }
    public void Forget(Guid ticket)
    {
        Verify(); var t = Find(ticket); if (!t.Task.IsCompleted) throw new EditCommandRejectedException("asset_import_preparing"); _tickets.Remove(ticket);
    }
    private static (DiskImage Before, DiskImage After) States(ParticipantMemento memento)
    {
        var (a, b) = AssetDiskTransaction.States(memento, true);
        if (a.Length != 1 || a[0].Blob is not null || b[0].Blob is not null || b[0].Data is null || !a[0].Path.EndsWith(".ncmeta", StringComparison.Ordinal)) throw new ArgumentException("Expected import metadata history.");
        return (a[0], b[0]);
    }
    private void Verify() { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Import command owner required."); ObjectDisposedException.ThrowIf(_closed, this); }
    public Task Completion { get { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Owner required."); return Task.WhenAll(_tickets.Values.Select(t => t.Task).Append(_preload)); } }
    public void Dispose()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Import command owner required."); if (_closed) return; _closed = true;
        _preloadCancellation.Cancel(); _disk.Dispose(); _ = Completion.ContinueWith(_ => { Generations.Dispose(); _preloadCancellation.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
