using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Editor.Transport;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

namespace Ncma.Editor.App;

internal sealed record RuntimeReadReview(Guid Ticket, Guid EditSessionId, ulong DocumentGeneration,
    Guid PlaySessionId, Guid SceneSessionId, Guid WorldId, Guid EndpointId, ulong AudienceRevision,
    RuntimeObjectInfo[] Objects, Guid[] Audience, string Fingerprint);

// Local human approval only. No capability can create, extend or broaden this grant.
// Scope includes component values and script Exports on the exact selected object generations,
// including later value/layout changes, for 60 seconds. Values are not secret-redacted.
internal sealed partial class RuntimeInspectionService(EditorSessionOwner owner, TimeProvider? clock = null)
{
    internal const int MaxObjects = 32;
    private const ulong MaxExactNumber = 9007199254740991;
    // Reserve space for the existing capability/IPC envelopes within the 256 KiB response budget.
    private const int MaxDataBytes = RuntimeSceneInstance.MaxInspectionBytes - 4096;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private sealed record Scope(RuntimeReadReview Review, PlaySession Play, EditorEndpoint Endpoint,
        Entity[] Entities, long Created);
    private Scope? _pending, _grant;

    private static string Hash(RuntimeReadReview review) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(review with { Fingerprint = "" }, Wire.Json)));
    private static Guid[] Audience(EditorEndpoint endpoint) => endpoint.View.Connections
        .Where(c => c.Paired && c.Connected).Select(c => c.ConnectionId).Order().ToArray();
    private PlaySession Source()
    {
        _ = owner.Edit!.State;
        if (owner.Play is not { } play || play.State is not (PlayState.Running or PlayState.Paused)) throw Stale();
        var stamp = play.Scene.GetInspectionStamp(); // Reject candidate-step, wrong-thread and encoder reentry reads.
        if (stamp.Tick > MaxExactNumber || stamp.Revision > MaxExactNumber) throw Stale();
        return play;
    }
    internal void Register() => owner.Edit!.RegisterInspections(RuntimeInspectionSchemas.Descriptors()
        .Select(d => (d, (Func<JsonElement, object>)(input => Inspect(d.Name, input)))).ToArray());

    internal (Guid PlaySessionId, RuntimeInspectionPage<RuntimeObjectInfo> Page) LocalObjects(int offset = 0)
    {
        var play = Source();
        return (play.SessionId, play.Scene.InspectObjects(play.Scene.GetInspectionStamp(), offset, 16));
    }
    internal RuntimeReadReview Capture(Guid[] exactIds)
    {
        var play = Source();
        if (exactIds.Length is < 1 or > MaxObjects || exactIds.Distinct().Count() != exactIds.Length)
            throw new EditRejectedException("runtime_review_invalid");
        var endpoint = owner.Endpoint ?? throw new EditRejectedException("runtime_endpoint_missing");
        Guid[] audience = Audience(endpoint);
        if (audience.Length == 0) throw new EditRejectedException("runtime_audience_missing");
        var world = play.Scene.World;
        var entities = exactIds.Order().Select(world.FindEntity).ToArray();
        var objects = entities.Select(e => new RuntimeObjectInfo(world.GetUuid(e), world.GetName(e))).ToArray();
        var page = new RuntimeReadReview(Guid.NewGuid(), owner.Edit!.SessionId, owner.Edit.DocumentGeneration,
            play.SessionId, play.Scene.SessionId, world.Identity, endpoint.View.InstanceId,
            endpoint.AudienceRevision, objects, audience, "");
        page = page with { Fingerprint = Hash(page) };
        // Keep private copies. Returned arrays cannot mutate the pending review or grant.
        _pending = new(Copy(page), play, endpoint, entities, _clock.GetTimestamp());
        return page;
    }
    private static RuntimeReadReview Copy(RuntimeReadReview page) => page with
    { Objects = (RuntimeObjectInfo[])page.Objects.Clone(), Audience = (Guid[])page.Audience.Clone() };
    private bool Current(Scope scope, bool requireReviewedNames)
    {
        try
        {
            var play = Source(); var r = scope.Review; var endpoint = owner.Endpoint;
            if (!ReferenceEquals(play, scope.Play) || !ReferenceEquals(endpoint, scope.Endpoint) ||
                endpoint is null || _clock.GetElapsedTime(scope.Created).TotalSeconds >= 60 ||
                owner.Edit!.SessionId != r.EditSessionId || owner.Edit.DocumentGeneration != r.DocumentGeneration ||
                play.SessionId != r.PlaySessionId || play.Scene.SessionId != r.SceneSessionId ||
                play.Scene.World.Identity != r.WorldId || endpoint.View.InstanceId != r.EndpointId ||
                endpoint.AudienceRevision != r.AudienceRevision || !Audience(endpoint).SequenceEqual(r.Audience)) return false;
            for (int i = 0; i < r.Objects.Length; i++)
            {
                if (!play.Scene.World.TryFindEntity(r.Objects[i].Id, out var entity) || entity != scope.Entities[i]) return false;
                if (requireReviewedNames && play.Scene.World.GetName(entity) != r.Objects[i].Name) return false;
            }
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditCommandRejectedException) { return false; }
    }
    internal bool IsCurrent(RuntimeReadReview review) => _pending is { } pending &&
        review.Ticket == pending.Review.Ticket && review.Fingerprint == pending.Review.Fingerprint &&
        Hash(review) == pending.Review.Fingerprint && Current(pending, true);
    internal void Approve(RuntimeReadReview displayed, string fingerprint, bool reviewed)
    {
        if (!reviewed || fingerprint != displayed.Fingerprint || !IsCurrent(displayed))
            throw new EditRejectedException("runtime_review_stale");
        _grant = _pending! with { Created = _clock.GetTimestamp() }; _pending = null;
    }
    internal void Revoke() { _ = owner.Edit!.State; _grant = null; _pending = null; }
    private Scope Approved()
    {
        if (_grant is { } grant && Current(grant, false)) return grant;
        _grant = null;
        throw new EditRejectedException("runtime_not_visible");
    }
    internal (int Objects, int Seconds) Grant
    {
        get
        {
            _ = owner.Edit!.State;
            try { var g = Approved(); return (g.Review.Objects.Length, (int)Math.Ceiling(60 - _clock.GetElapsedTime(g.Created).TotalSeconds)); }
            catch (EditRejectedException) { return (0, 0); }
        }
    }
    private object Inspect(string name, JsonElement input)
    {
        try
        {
            _ = owner.Edit!.State;
            if (input.ValueKind != JsonValueKind.Object) throw Invalid();
            Wire.RejectDuplicates(input);
            if (name == "ncma.runtime.state")
            {
                if (input.EnumerateObject().Any()) throw Invalid();
                var g = Approved(); var play = Source();
                return Bounded(new { playSessionId = play.SessionId, stamp = play.Scene.GetInspectionStamp(),
                    objectIds = g.Review.Objects.Select(o => o.Id).ToArray() });
            }
            string[] required = ["playSessionId", "stamp", "section", "offset", "limit"];
            string[] optional = ["objectId", "bindingId"];
            if (input.EnumerateObject().Any(p => !required.Contains(p.Name) && !optional.Contains(p.Name)) ||
                required.Any(p => !input.TryGetProperty(p, out _))) throw Invalid();
            string section = input.GetProperty("section").ValueKind == JsonValueKind.String ? input.GetProperty("section").GetString()! : "";
            if (section is not ("objects" or "components" or "bindings" or "exports")) throw Invalid();
            bool hasObject = input.TryGetProperty("objectId", out var objectValue), hasBinding = input.TryGetProperty("bindingId", out var bindingValue);
            if (hasObject != (section != "objects") || hasBinding != (section == "exports")) throw Invalid();
            Guid objectId = hasObject ? Id(objectValue) : Guid.Empty, bindingId = hasBinding ? Id(bindingValue) : Guid.Empty;
            Guid playId = Id(input.GetProperty("playSessionId"));
            RuntimeInspectionStamp expected = Stamp(input.GetProperty("stamp"));
            int offset = Count(input.GetProperty("offset"), 0, 65536), limit = Count(input.GetProperty("limit"), 1, 128);
            var grant = Approved(); var source = Source();
            if (playId != source.SessionId || expected != source.Scene.GetInspectionStamp()) throw Stale();
            if (hasObject && !grant.Review.Objects.Any(o => o.Id == objectId)) throw new EditRejectedException("runtime_not_visible");
            object page = section switch
            {
                // Only approved objects contribute names, count and pagination; never enumerate hidden objects remotely.
                "objects" => new RuntimeInspectionPage<RuntimeObjectInfo>(expected, null, null, offset,
                    grant.Review.Objects.Length, grant.Review.Objects.Skip(offset).Take(limit)
                        .Select(o => new RuntimeObjectInfo(o.Id, source.Scene.World.GetName(source.Scene.World.FindEntity(o.Id)))).ToArray()),
                "components" => source.Scene.InspectComponents(expected, objectId, offset, limit),
                "bindings" => source.Scene.InspectBindings(expected, objectId, offset, limit),
                _ => source.Scene.InspectExports(expected, objectId, bindingId, offset, limit)
            };
            return Bounded(new { playSessionId = source.SessionId, section, page });
        }
        catch (Exception e) when (e is EditRejectedException or EditCommandRejectedException) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException) { throw new EditCommandRejectedException("runtime_inspection_failed"); }
    }
    private static JsonElement Bounded(object data)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, Wire.Json);
        if (bytes.Length > MaxDataBytes) throw new EditCommandRejectedException("runtime_response_budget");
        using var document = JsonDocument.Parse(bytes); return document.RootElement.Clone();
    }
    private static Guid Id(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "D", out Guid id) ||
            id == Guid.Empty || value.GetString() != id.ToString("D")) throw Invalid();
        return id;
    }
    private static int Count(JsonElement value, int min, int max)
    { if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int n) || n < min || n > max) throw Invalid(); return n; }
    private static RuntimeInspectionStamp Stamp(JsonElement value)
    {
        string[] names = ["version", "sceneSessionId", "worldId", "revision", "tick"];
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length ||
            value.EnumerateObject().Any(p => !names.Contains(p.Name))) throw Invalid();
        ulong Number(string key) { var v = value.GetProperty(key); if (v.ValueKind != JsonValueKind.Number || !v.TryGetUInt64(out ulong n) || n > MaxExactNumber) throw Invalid(); return n; }
        return new(Count(value.GetProperty("version"), 1, 1), Id(value.GetProperty("sceneSessionId")),
            Id(value.GetProperty("worldId")), Number("revision"), Number("tick"));
    }
    private static EditCommandRejectedException Invalid() => new("runtime_input_invalid");
    private static EditCommandRejectedException Stale() => new("runtime_snapshot_stale");
}
