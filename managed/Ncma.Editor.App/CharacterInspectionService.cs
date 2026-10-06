using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Characters;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Editor.Transport;
using Ncma.Gameplay;
namespace Ncma.Editor.App;

internal sealed record CharacterReadReview(Guid EditSessionId,ulong DocumentGeneration,Guid PlaySessionId,Guid WorldId,Guid EndpointId,Guid[] ObjectIds,Guid[] Audience,string Fingerprint);
// Human host review only. Scope is shared by the displayed paired audience, NOT a per-client ACL.
internal sealed class CharacterInspectionService(EditorSessionOwner owner,Func<CharacterPlayRuntime?> current,TimeProvider? clock=null)
{
    private readonly TimeProvider _clock=clock??TimeProvider.System;
    private CharacterReadReview? _grant;
    private long _granted;
    private static string Hash(CharacterReadReview page)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page with{Fingerprint=""})));
    private static Guid[] Audience(EditorEndpoint endpoint)=>endpoint.View.Connections.Where(c=>c.Paired&&c.Connected).Select(c=>c.ConnectionId).Order().ToArray();
    private (PlaySession Play,CharacterPlayRuntime Runtime) Source()
    {if(owner.Play is not {} play||current() is not {} runtime||play.State is not (PlayState.Running or PlayState.Paused or PlayState.Faulted))throw new EditCommandRejectedException("character_snapshot_stale");return(play,runtime);}
    internal void Register()=>owner.Edit!.RegisterInspections(CharacterInspectionSchemas.Descriptors().Select(d=>(d,(Func<JsonElement,object>)(i=>Inspect(d.Name,i)))).ToArray());
    internal CharacterDebugFrame? LocalFrame(){try{return Source().Runtime.ReadDebugFrame();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditCommandRejectedException){return null;}}
    internal CharacterReadReview Capture(Guid[] exactIds)
    {
        _=owner.Edit!.State;var source=Source();var frame=source.Runtime.ReadDebugFrame();
        if(!frame.SnapshotValid||frame.PlaySessionId!=source.Play.SessionId||frame.WorldId!=source.Play.Document.World.Identity||exactIds.Length is <1 or >32||exactIds.Distinct().Count()!=exactIds.Length||exactIds.Any(id=>!frame.Characters.Any(c=>c.ObjectId==id)))throw new EditRejectedException("character_review_invalid");
        var endpoint=owner.Endpoint??throw new EditRejectedException("character_endpoint_missing");var audience=Audience(endpoint);
        if(audience.Length==0)throw new EditRejectedException("character_audience_missing");
        var page=new CharacterReadReview(owner.Edit.SessionId,owner.Edit.DocumentGeneration,frame.PlaySessionId,frame.WorldId,endpoint.View.InstanceId,exactIds.Order().ToArray(),audience,"");
        return page with{Fingerprint=Hash(page)};
    }
    internal bool IsCurrent(CharacterReadReview page)
    {
        try{return Hash(page)==page.Fingerprint&&Capture(page.ObjectIds).Fingerprint==page.Fingerprint;}
        catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditCommandRejectedException or EditRejectedException){return false;}
    }
    internal void Approve(CharacterReadReview displayed,string fingerprint,bool reviewed)
    {
        if(!reviewed||fingerprint!=displayed.Fingerprint||!IsCurrent(displayed))throw new EditRejectedException("character_review_stale");
        _grant=displayed with{ObjectIds=(Guid[])displayed.ObjectIds.Clone(),Audience=(Guid[])displayed.Audience.Clone()};_granted=_clock.GetTimestamp();
    }
    internal void Revoke(){_=owner.Edit!.State;_grant=null;}
    internal (Guid[] Objects,int Seconds) Grant
    {get{if(!Active())return([],0);return((Guid[])_grant!.ObjectIds.Clone(),Math.Max(0,(int)Math.Ceiling(60-_clock.GetElapsedTime(_granted).TotalSeconds)));}}
    private bool Active()
    {
        _=owner.Edit!.State;
        try {
            var source=Source();var endpoint=owner.Endpoint;var g=_grant;
            if(g is not null&&_clock.GetElapsedTime(_granted).TotalSeconds<60&&owner.Edit.SessionId==g.EditSessionId&&owner.Edit.DocumentGeneration==g.DocumentGeneration&&source.Play.SessionId==g.PlaySessionId&&source.Play.Document.World.Identity==g.WorldId&&endpoint is not null&&endpoint.View.InstanceId==g.EndpointId&&Audience(endpoint).SequenceEqual(g.Audience))return true;
        }catch(Exception e)when(e is InvalidOperationException or EditCommandRejectedException){}
        _grant=null;return false;
    }
    private object Inspect(string name,JsonElement input)
    {
        try {
            string[] allowed=["playSessionId","worldId","objectIds"];
            if(input.ValueKind!=JsonValueKind.Object||input.EnumerateObject().Count()!=3||input.EnumerateObject().Select(p=>p.Name).Distinct().Count()!=3||input.EnumerateObject().Any(p=>!allowed.Contains(p.Name)))throw new EditCommandRejectedException("character_input_invalid");
            Guid Id(JsonElement value){if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"D",out Guid id)||id==Guid.Empty||value.GetString()!=id.ToString("D"))throw new EditCommandRejectedException("character_input_invalid");return id;}
            Guid playId=Id(input.GetProperty("playSessionId")),worldId=Id(input.GetProperty("worldId"));var list=input.GetProperty("objectIds");
            if(list.ValueKind!=JsonValueKind.Array||list.GetArrayLength() is <1 or >32)throw new EditCommandRejectedException("character_input_invalid");
            Guid[] ids=list.EnumerateArray().Select(Id).ToArray();if(ids.Distinct().Count()!=ids.Length)throw new EditCommandRejectedException("character_input_invalid");
            if(!Active()||ids.Any(id=>!_grant!.ObjectIds.Contains(id)))throw new EditRejectedException("character_not_visible");
            if(playId!=_grant!.PlaySessionId||worldId!=_grant.WorldId)throw new EditCommandRejectedException("character_snapshot_stale");
            var frame=Source().Runtime.ReadDebugFrame();if(frame.PlaySessionId!=playId||frame.WorldId!=worldId||frame.Tick>9007199254740991||frame.Characters.Any(c=>c.Instance>9007199254740991))throw new EditCommandRejectedException("character_snapshot_stale");
            if(name=="ncma.character.inspect")return new{frame.PlaySessionId,frame.WorldId,frame.Tick,frame.State,frame.SnapshotValid,frame.FaultCode,characters=frame.Characters.Where(c=>ids.Contains(c.ObjectId)).ToArray()};
            var events=frame.Events.Where(e=>ids.Contains(e.Actor)).Select(e=>new{e.Tick,e.Actor,target=e.Target==Guid.Empty||!ids.Contains(e.Target)?(Guid?)null:e.Target,e.NotifyId,e.Instance,kind=e.Kind.ToString(),damage=e.Target!=Guid.Empty&&!ids.Contains(e.Target)?(float?)null:e.Damage}).ToArray();
            return new{frame.PlaySessionId,frame.WorldId,frame.Tick,frame.State,frame.SnapshotValid,frame.FaultCode,events};
        }catch(Exception e)when(e is EditRejectedException or EditCommandRejectedException){throw;}
        catch(Exception e)when(e is not OutOfMemoryException){throw new EditCommandRejectedException("character_inspection_failed");}
    }
}
