using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

// Issued by this local service only. Display the full review and these expanded original commands
// before asking the human to stop Play and apply. Possession is not a remote capability grant.
public sealed class PreparedRuntimeChanges
{
    internal PreparedRuntimeChanges(RuntimeChangeReview review, OriginalCommandBatchReview batch, Guid[] deletes)
    {
        Review = review; Batch = batch; _deletes = (Guid[])deletes.Clone();
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "runtime-change-apply-v1:" + review.Fingerprint + ":" + batch.Fingerprint)));
    }
    private readonly Guid[] _deletes;
    internal OriginalCommandBatchReview Batch { get; }
    public RuntimeChangeReview Review { get; }
    public string Fingerprint { get; }
    public Guid[] CopyDeletionTargets() => (Guid[])_deletes.Clone();
    public byte[] CopyOriginalCommands() => Batch.CopyCommandBytes();
}

public sealed partial class EditorRuntimeChanges
{
    private PreparedRuntimeChanges? _prepared;

    public PreparedRuntimeChanges PrepareApply(RuntimeChangeReview review)
    {
        _ = owner.Edit!.State;
        if (_capturing || !MatchesCurrent(review)) throw new EditRejectedException("runtime_change_review_stale");
        _capturing = true;
        try
        {
            var data = _pending!.Data;
            if (data.Changes.Length == 0) throw new EditRejectedException("runtime_change_empty");
            OriginalEditCommand[] commands = ExpandOriginalCommands(data);
            var batch = owner.Edit.ReviewOriginalBatch("Keep reviewed Play changes", data.Edit.Revision, commands);
            // Do not silently apply normalized values that were absent from the human's diff.
            if (!batch.PreservesProposedValues) throw new EditRejectedException("runtime_change_normalization_requires_review");
            // Component normalization/policy callbacks during preparation may invalidate the source.
            if (!MatchesCurrent(review)) throw new EditRejectedException("runtime_change_source_changed");
            var prepared = new PreparedRuntimeChanges(review, batch,
                data.Changes.Where(c => c.Kind == "delete_object").Select(c => c.ObjectId).Order().ToArray());
            _prepared = prepared;
            return prepared;
        }
        finally { _capturing = false; }
    }

    public bool IsPreparedCurrent(PreparedRuntimeChanges? prepared) =>
        prepared is not null && !_capturing && ReferenceEquals(prepared, _prepared) && MatchesCurrent(prepared.Review);

    // Trusted local human-UI route, just like EditorWorkspace.Delete; not registered with the
    // transport. An AI proposal must not call this in lieu of the human confirmation boundary.
    // One shot: stopping failure or commit rejection requires a new review, never automatic retry.
    public CapabilityResult ConfirmStopAndApply(PreparedRuntimeChanges prepared, string confirmedFingerprint,
        IReadOnlyList<Guid> confirmedDeletionTargets)
    {
        _ = owner.Edit!.State;
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(confirmedDeletionTargets);
        if (!IsPreparedCurrent(prepared)) throw new EditRejectedException("runtime_change_review_stale");
        if (!StringComparer.Ordinal.Equals(confirmedFingerprint, prepared.Fingerprint))
            throw new EditRejectedException("runtime_change_confirmation_required");
        Guid[] deletes = CopyBoundedIds(confirmedDeletionTargets, true);
        // IReadOnlyList implementations may invoke arbitrary caller code from Count/indexers.
        if (!IsPreparedCurrent(prepared)) throw new EditRejectedException("runtime_change_review_stale");
        if (deletes.Distinct().Count() != deletes.Length ||
            !deletes.Order().SequenceEqual(prepared.CopyDeletionTargets()))
            throw new EditRejectedException("runtime_change_delete_confirmation_required");
        var data = _pending!.Data;
        var edit = owner.Edit;
        var document = owner.Document;
        var permissions = PermissionsFor(data, deletes);
        _capturing = true;
        try
        {
            // Consume before callbacks. Normal Stop also invalidates the service. The local copied
            // plan survives only this stack frame; no retained runtime reference is used after Stop.
            Invalidate();
            using (document.ReadOnly()) owner.StopPlay();
            if (owner.Play is not null || !ReferenceEquals(owner.Edit, edit) || edit.State.Frozen ||
                owner.Catalog.Snapshot.Generation != data.CatalogGeneration)
                throw new EditRejectedException("runtime_change_stop_incomplete");
            return edit.ApplyOriginalBatch(prepared.Batch, Guid.NewGuid(), permissions);
        }
        finally { _capturing = false; }
    }

    private static Guid[] CopyBoundedIds(IReadOnlyList<Guid> values, bool allowEmpty)
    {
        int count = values.Count;
        if (count < (allowEmpty ? 0 : 1) || count > MaxObjects) throw new EditRejectedException("runtime_change_object_budget");
        var result = new Guid[count];
        for (int i = 0; i < count; ++i) result[i] = values[i];
        if (values.Count != count) throw new EditRejectedException("runtime_change_object_scope");
        return result; // Never enumerate an unbounded caller-controlled sequence.
    }

    private static CapabilityPermissions PermissionsFor(RuntimeChangeReviewData data, Guid[] deletes)
    {
        var types = new HashSet<string>(StringComparer.Ordinal); var bindings = new HashSet<Guid>();
        void ReadBindings(JsonElement value)
        { foreach (var binding in value.EnumerateArray()) bindings.Add(binding.GetProperty("id").GetGuid()); }
        foreach (var change in data.Changes)
        {
            if (change.TypeId is { } type) types.Add(type);
            foreach (var value in new[] { change.Before, change.After })
            {
                if (value is not { } item) continue;
                if (change.Kind is "create_object" or "delete_object")
                {
                    foreach (var component in item.GetProperty("components").EnumerateArray())
                        types.Add(component.GetProperty("typeId").GetString()!);
                    ReadBindings(item.GetProperty("behaviours"));
                }
                else if (change.Kind == "set_bindings") ReadBindings(item);
            }
        }
        return new(["ncma.scene.transaction", "ncma.scene.delete_object"], deletes,
            objectScope: data.ObjectIds, createScope: data.Changes.Where(c => c.Kind == "create_object").Select(c => c.ObjectId),
            componentScope: types, bindingScope: bindings, allowDocumentHistory: false, requireTrustedBindings: true);
    }

    private static OriginalEditCommand[] ExpandOriginalCommands(RuntimeChangeReviewData data)
    {
        var commands = new List<OriginalEditCommand>(); var operations = new List<JsonElement>(); int count = 0;
        void Add(object operation)
        {
            if (++count > EditSession.MaxOperations) throw new EditRejectedException("runtime_change_expanded_operation_budget");
            operations.Add(Encode(operation));
        }
        void Flush()
        {
            if (operations.Count == 0) return;
            commands.Add(new("ncma.scene.transaction", Encode(new { operations = operations.ToArray() })));
            operations.Clear();
        }
        void Component(Guid id, JsonElement component) => Add(new { op = "set_component", objectId = id,
            typeId = component.GetProperty("typeId").GetString(), version = component.GetProperty("version").GetInt32(),
            data = component.GetProperty("data") });
        foreach (var change in data.Changes)
        {
            Guid id = change.ObjectId;
            switch (change.Kind)
            {
                case "create_object":
                    var item = change.After!.Value;
                    Add(new { op = "create", objectId = id, name = item.GetProperty("name").GetString() });
                    foreach (var component in item.GetProperty("components").EnumerateArray()) Component(id, component);
                    if (item.GetProperty("behaviours").GetArrayLength() != 0)
                        Add(new { op = "set_bindings", objectId = id, bindings = item.GetProperty("behaviours") });
                    break;
                case "delete_object":
                    if (++count > EditSession.MaxOperations) throw new EditRejectedException("runtime_change_expanded_operation_budget");
                    Flush(); commands.Add(new("ncma.scene.delete_object", Encode(new { objectId = id })));
                    break;
                case "rename_object": Add(new { op = "rename", objectId = id, name = change.After!.Value.GetString() }); break;
                case "set_component": Component(id, change.After!.Value); break;
                case "remove_component": Add(new { op = "remove_component", objectId = id, typeId = change.TypeId }); break;
                case "set_bindings": Add(new { op = "set_bindings", objectId = id, bindings = change.After!.Value }); break;
                default: throw new EditRejectedException("runtime_change_kind_unknown");
            }
        }
        Flush(); return commands.ToArray(); // Original batch enforces its complete 64 KiB envelope too.
    }
}
