using System.Text.Json;
using Ncma.Scene;
using Ncma.Runtime;

namespace Ncma.Editor.Core;

public sealed record OriginalEditCommand(string Capability, JsonElement Input);

// Trusted-host review only; no JSON gateway constructor or new remote capability.
// Stores bounded copied ORIGINAL commands, not a replacement scene or an alternate history.
public sealed class OriginalCommandBatchReview
{
    private readonly byte[] _payload;
    internal OriginalCommandBatchReview(Guid session, Guid document, Guid catalog, ulong generation, ulong revision,
        byte[] payload, string beforeHash, string afterHash, string fingerprint, bool preservesProposedValues)
    {
        SessionId = session; DocumentSessionId = document; BehaviourCatalogGeneration = catalog; DocumentGeneration = generation; Revision = revision;
        _payload = (byte[])payload.Clone(); BeforeHash = beforeHash; AfterHash = afterHash; Fingerprint = fingerprint;
        PreservesProposedValues = preservesProposedValues;
    }
    public Guid SessionId { get; }
    public Guid DocumentSessionId { get; }
    public Guid BehaviourCatalogGeneration { get; }
    public ulong DocumentGeneration { get; }
    public ulong Revision { get; }
    public string Fingerprint { get; }
    public bool PreservesProposedValues { get; }
    internal string BeforeHash { get; }
    internal string AfterHash { get; }
    public byte[] CopyCommandBytes() => (byte[])_payload.Clone();
    internal JsonElement Input() { using var json = JsonDocument.Parse(_payload); return json.RootElement.Clone(); }
}

public sealed partial class EditSession
{
    private sealed record BatchCandidate(byte[] Before, SceneDocumentSnapshot After, Guid? Selection,
        Guid[] Deletes, CommandImpact Intent);
    private static bool DestructiveAllowed(CapabilityPermissions permissions, Guid[] targets) =>
        targets.All(id => permissions.Allows("ncma.scene.delete_object", id) && permissions.ObjectAllowed(id));
    private static CommandImpact MergeImpact(CommandImpact a, CommandImpact b) => new(
        a.Objects.Union(b.Objects).Order().ToArray(), a.CreatedObjects.Union(b.CreatedObjects).Order().ToArray(),
        a.ComponentTypes.Union(b.ComponentTypes).Order(StringComparer.Ordinal).ToArray(),
        a.Bindings.Union(b.Bindings).Order().ToArray(), a.DocumentReplacement || b.DocumentReplacement);

    public Guid[] HistoryDestructiveTargets(bool redo)
    {
        Observe(); int index = redo ? _cursor : _cursor - 1;
        if (_invalidated || index < 0 || index >= _history.Length) return [];
        var entry = _history[index];
        return entry.DestructiveTargets.Concat(entry.Target is Guid id ? [id] : []).Distinct().Order().ToArray();
    }

    // Pure preflight can run while Play freezes editing; it never unfreezes or installs a candidate.
    public OriginalCommandBatchReview ReviewOriginalBatch(string label, ulong expectedRevision,
        IReadOnlyList<OriginalEditCommand> commands)
    {
        Observe();
        if (_invoking || InteractionBusy || _invalidated) throw new EditRejectedException("edit_busy");
        if (expectedRevision != Revision) throw new EditRejectedException("revision_conflict");
        _invoking = true;
        try
        {
            ArgumentNullException.ThrowIfNull(commands);
            if (commands.Count is < 1 or > MaxOperations) throw new ArgumentException("Invalid command count.");
            World.ValidateName(label);
            // Own inputs before any validation callbacks; JSON budget includes the complete batch envelope.
            var input = JsonSerializer.SerializeToElement(new { label, commands = commands.ToArray() }, OutputJson);
            CheckInput(input);
            var candidate = BuildOriginalBatch(input, null);
            byte[] after = [];
            _ = _document.PrepareRestore(candidate.After, expectedRevision, normalized => after = SceneDocumentCodec.Encode(normalized));
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input.GetRawText());
            string beforeHash = Hash(candidate.Before), afterHash = Hash(after);
            string fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { version = 1, SessionId,
                document = _document.SessionId, BehaviourCatalogGeneration, DocumentGeneration, revision = expectedRevision, beforeHash, afterHash, input }, OutputJson));
            // Component ordering is the document's canonical representation, not a value change.
            // Original set_component replaces by append; compare after that same ordering only,
            // without invoking normalizers a second time or treating altered values as reviewed.
            var orderedProposal = candidate.After with { Objects = candidate.After.Objects.Select(o => o with {
                Components = o.Components.OrderBy(c => c.TypeId, StringComparer.Ordinal).ToArray() }).ToArray() };
            return new(SessionId, _document.SessionId, BehaviourCatalogGeneration, DocumentGeneration, expectedRevision,
                bytes, beforeHash, afterHash, fingerprint, Hash(SceneDocumentCodec.Encode(orderedProposal)) == afterHash);
        }
        finally { _invoking = false; }
    }

    public CapabilityResult ApplyOriginalBatch(OriginalCommandBatchReview review, Guid requestId,
        CapabilityPermissions? permissions = null)
    {
        ArgumentNullException.ThrowIfNull(review); _document.VerifyAccess();
        permissions ??= CapabilityPermissions.ReadOnly;
        var request = new CapabilityRequest(ContractVersion, requestId, review.SessionId, review.Revision,
            "ncma.scene.transaction", review.Input());
        if (_invoking) return Result(request, "error", "edit_busy", false, new { });
        _invoking = true;
        try
        {
            Observe();
            if (requestId == Guid.Empty) return Result(request, "error", "invalid_envelope", false, new { });
            if (review.SessionId != SessionId || review.DocumentSessionId != _document.SessionId ||
                review.DocumentGeneration != DocumentGeneration || review.BehaviourCatalogGeneration != BehaviourCatalogGeneration)
                return Result(request, "conflict", "batch_source_stale", false, new { });
            if (!permissions.Allows(request.Capability)) return Result(request, "denied", "permission_denied", false, new { });
            if (_frozen) return Result(request, "denied", "play_frozen", false, new { });
            if (InteractionBusy || _invalidated) return Result(request, "conflict", "edit_busy", false, new { });
            string fingerprint = "original-command-batch-v1:" + review.Fingerprint;
            if (_cache.TryGetValue(requestId, out var cached))
            {
                if (cached.Fingerprint != fingerprint) return Result(request, "conflict", "request_id_reused", false, new { });
                if (!permissions.Allows(cached.Permission, cached.Target) || !permissions.InScope(cached.Impact) ||
                    !DestructiveAllowed(permissions, cached.DestructiveTargets)) return Result(request, "denied", "permission_denied", false, new { });
                return cached.Result with { Revision = Revision, Replayed = true,
                    Data = JsonSerializer.SerializeToElement(new { history = State, execution = cached.Result.Data }, OutputJson) };
            }
            if (review.Revision != Revision) return Result(request, "conflict", "revision_conflict", false, new { });
            var candidate = BuildOriginalBatch(request.Input, permissions);
            if (Hash(candidate.Before) != review.BeforeHash) throw new EditRejectedException("batch_source_stale");
            return Commit(request, fingerprint, candidate.After, candidate.Before, Text(request.Input, "label"),
                request.Capability, null, candidate.Selection, permissions: permissions,
                destructiveTargets: candidate.Deletes, expectedAfterHash: review.AfterHash, requiredImpact: candidate.Intent);
        }
        catch (EditRejectedException e) { return Result(request, "denied", e.Code, false, new { }); }
        catch (EditCommandRejectedException e) { return Result(request, e.Denied ? "denied" : "conflict", e.Code, false, new { }); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or OverflowException)
        { return Result(request, "error", "invalid_input", false, new { message = e.Message }); }
        catch (Exception e) when (e is not OutOfMemoryException)
        { return Result(request, "error", "operation_failed", false, new { message = e.Message }); }
        finally { _invoking = false; }
    }

    private BatchCandidate BuildOriginalBatch(JsonElement input, CapabilityPermissions? permissions)
    {
        CheckInput(input); Closed(input, ["label", "commands"], ["label", "commands"]); World.ValidateName(Text(input, "label"));
        var commands = input.GetProperty("commands");
        if (commands.ValueKind != JsonValueKind.Array || commands.GetArrayLength() is < 1 or > MaxOperations)
            throw new ArgumentException("Invalid command count.");
        byte[] before = _document.CaptureBytes(); var candidate = SceneDocumentCodec.Decode(before);
        Guid? selection = _selection; var deleted = new HashSet<Guid>(); int operations = 0;
        var requested = new CommandImpact([], [], [], [], false);
        foreach (var command in commands.EnumerateArray())
        {
            Closed(command, ["capability", "input"], ["capability", "input"]);
            string capability = Text(command, "capability"); var value = command.GetProperty("input");
            if (capability == "ncma.scene.delete_object")
            {
                Closed(value, ["objectId"], ["objectId"]); Guid id = Uuid(value, "objectId");
                if (++operations > MaxOperations) throw new ArgumentException("Original operation budget exceeded.");
                if (permissions is not null && !DestructiveAllowed(permissions, [id])) throw new EditRejectedException("target_not_authorized");
                var old = candidate.Objects.SingleOrDefault(o => o.Id == id) ?? throw new ArgumentException("Unknown deletion UUID.");
                deleted.Add(id);
                requested = MergeImpact(requested, new([id], [], old.Components.Select(c => c.TypeId).ToArray(), old.Behaviours.Select(b => b.Id).ToArray(), false));
                candidate = DeleteOriginalObject(candidate, id);
            }
            else if (capability == "ncma.scene.transaction")
            {
                Closed(value, ["operations"], ["operations"]); var ops = value.GetProperty("operations");
                if (ops.ValueKind != JsonValueKind.Array || ops.GetArrayLength() < 1) throw new ArgumentException("Empty original transaction.");
                if ((operations = checked(operations + ops.GetArrayLength())) > MaxOperations) throw new ArgumentException("Original operation budget exceeded.");
                foreach (var op in ops.EnumerateArray())
                {
                    if (permissions is not null) AuthorizeIntent(permissions, op);
                    candidate = Apply(candidate, op); // Unchanged original operation whitelist: no delete/restore escape.
                    if (Text(op, "op") == "set_bindings")
                        foreach (var binding in candidate.Objects.Single(o => o.Id == Uuid(op, "objectId")).Behaviours) RequireBinding(binding);
                }
                requested = RequestedImpact(new(ContractVersion, Guid.NewGuid(), SessionId, Revision, capability, value), requested);
            }
            else throw new ArgumentException("Batch supports only original scene transaction/delete commands.");
        }
        if (selection is Guid selected && !Exists(candidate, selected)) selection = null;
        return new(before, candidate, selection, deleted.Order().ToArray(), requested);
    }

    private static SceneDocumentSnapshot DeleteOriginalObject(SceneDocumentSnapshot candidate, Guid target)
    {
        if (!Exists(candidate, target)) throw new ArgumentException("Unknown object UUID.");
        return candidate with { Objects = candidate.Objects.Where(o => o.Id != target).ToArray() };
    }
}
