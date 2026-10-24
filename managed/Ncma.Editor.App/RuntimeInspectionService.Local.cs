using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Gameplay;
using Ncma.Scene;

namespace Ncma.Editor.App;

// Local UI observation only, never serialized as a capability or used as a write grant.
internal sealed record RuntimeDetailRequest(Guid PlaySessionId, RuntimeInspectionStamp Stamp, Guid ObjectId);
internal sealed record RuntimeDetailPage(RuntimeDetailRequest Source, string Section, Guid? BindingId,
    int Offset, int TotalCount, JsonElement Data, string Text);

internal sealed partial class RuntimeInspectionService
{
    private PlaySession LocalSource(RuntimeDetailRequest request)
    {
        var play = Source();
        if (request.ObjectId == Guid.Empty || request.PlaySessionId != play.SessionId ||
            request.Stamp != play.Scene.GetInspectionStamp()) throw Stale();
        // Exact revision includes structural generations; a recreated UUID cannot reuse a ticket.
        _ = play.Scene.World.FindEntity(request.ObjectId);
        return play;
    }

    internal bool IsLocalCurrent(RuntimeDetailRequest request)
    {
        try { _ = LocalSource(request); return true; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditCommandRejectedException) { return false; }
    }

    internal RuntimeDetailRequest RefreshLocal(RuntimeDetailRequest previous)
    {
        var play = Source();
        // Refresh within one scene/World only. Reload/Stop/replacement requires choosing again.
        if (play.SessionId != previous.PlaySessionId || play.Scene.SessionId != previous.Stamp.SceneSessionId ||
            play.Scene.World.Identity != previous.Stamp.WorldId) throw Stale();
        // Refresh is explicit; if the object was recreated, this is a NEW observation, not an inherited grant.
        _ = play.Scene.World.FindEntity(previous.ObjectId);
        return new(play.SessionId, play.Scene.GetInspectionStamp(), previous.ObjectId);
    }

    internal RuntimeDetailPage LocalDetails(RuntimeDetailRequest request, string section,
        int offset = 0, Guid? bindingId = null)
    {
        var play = LocalSource(request);
        if (offset is < 0 or > 65536 || section is not ("components" or "bindings" or "exports") ||
            (section == "exports") != bindingId.HasValue || bindingId == Guid.Empty)
            throw new EditRejectedException("runtime_local_input_invalid");
        // One complete item per page: large components remain inspectable without relaxing the
        // existing full-response byte budget. No whole-scene serialization or per-frame encoding.
        object page = section switch
        {
            "components" => play.Scene.InspectComponents(request.Stamp, request.ObjectId, offset, 1),
            "bindings" => play.Scene.InspectBindings(request.Stamp, request.ObjectId, offset, 1),
            _ => play.Scene.InspectExports(request.Stamp, request.ObjectId, bindingId!.Value, offset, 1)
        };
        var data = Bounded(page);
        _ = LocalSource(request); // Custom encoders must not invalidate the captured source.
        return new(request, section, bindingId, offset, data.GetProperty("totalCount").GetInt32(),
            data, data.GetRawText());
    }
}
