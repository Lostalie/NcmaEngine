using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Characters;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Editor.Transport;
using Ncma.Gameplay;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.App;

internal sealed record AnimationRuntimeReview(Guid EditSessionId,ulong DocumentGeneration,Guid PlaySessionId,Guid WorldId,
    Guid EndpointId,ulong AudienceRevision,Guid[] Audience,AnimatorRuntimeBinding[] Bindings,string Fingerprint);
internal sealed class AnimationRuntimeInspectionService(EditorSessionOwner owner,Func<ScenePlayRuntime?> current,
    Func<SceneRenderSession?>? presentation=null,TimeProvider? clock=null)
{
    private readonly TimeProvider _clock=clock??TimeProvider.System;
    private AnimationRuntimeReview? _grant;
    private EditorEndpoint? _endpoint;
    private long _granted;
    private static string Hash(AnimationRuntimeReview value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value with{Fingerprint=""})));
    private static Guid[] Audience(EditorEndpoint e)=>e.View.Connections.Where(c=>c.Paired&&c.Connected).Select(c=>c.ConnectionId).Order().ToArray();
    private (PlaySession Play,ScenePlayRuntime Runtime,SceneAnimatorRuntime Graphs) Source()
    {
        _=owner.Edit!.State;
        if(owner.Play is not {} p||current() is not {} runtime||runtime.Animators is not {} graphs||p.State is not (PlayState.Running or PlayState.Paused or PlayState.Faulted))throw new EditCommandRejectedException("animator_snapshot_stale");
        return(p,runtime,graphs);
    }
    internal void Register()=>owner.Edit!.RegisterInspections([(AnimationRuntimeInspectionSchemas.Descriptor,Inspect)]);
    internal AnimatorRuntimeBinding[] LocalBindings(){try{return Source().Graphs.DescribeBindings().ToArray();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditCommandRejectedException){return [];}}
    private AnimationRuntimeReview Page(Guid[] exact,bool requireValid)
    {
        var s=Source();var endpoint=owner.Endpoint??throw new EditRejectedException("animator_endpoint_missing");
        if(exact.Length is <1 or >8||exact.Distinct().Count()!=exact.Length)throw new EditRejectedException("animator_review_invalid");
        var bindings=s.Graphs.DescribeBindings();var selected=exact.Order().Select(id=>bindings.SingleOrDefault(b=>b.ObjectId==id)??throw new EditRejectedException("animator_review_invalid")).ToArray();
        if(selected.Sum(b=>b.Resources.Count)>4096)throw new EditRejectedException("animator_review_budget");
        if(requireValid)foreach(var b in selected){if(s.Play.State==PlayState.Faulted||s.Graphs.ReadFrame(b.ObjectId).Context.Tick!=s.Play.Tick)throw new EditRejectedException("animator_snapshot_stale");}
        var audience=Audience(endpoint);if(audience.Length==0)throw new EditRejectedException("animator_audience_missing");
        var page=new AnimationRuntimeReview(owner.Edit!.SessionId,owner.Edit.DocumentGeneration,s.Play.SessionId,s.Play.Document.World.Identity,endpoint.View.InstanceId,endpoint.AudienceRevision,audience,selected,"");
        return page with{Fingerprint=Hash(page)};
    }
    internal AnimationRuntimeReview Capture(Guid[] exact)=>Page(exact,true);
    internal bool IsCurrent(AnimationRuntimeReview review)
    {try{return Hash(review)==review.Fingerprint&&Page(review.Bindings.Select(b=>b.ObjectId).ToArray(),true).Fingerprint==review.Fingerprint;}catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditRejectedException or EditCommandRejectedException){return false;}}
    internal void Approve(AnimationRuntimeReview displayed,string fingerprint,bool reviewed)
    {
        if(!reviewed||fingerprint!=displayed.Fingerprint||!IsCurrent(displayed))throw new EditRejectedException("animator_review_stale");
        _grant=displayed with{Audience=(Guid[])displayed.Audience.Clone(),Bindings=(AnimatorRuntimeBinding[])displayed.Bindings.Clone()};_endpoint=owner.Endpoint;_granted=_clock.GetTimestamp();
    }
    internal void Revoke(){_=owner.Edit!.State;_grant=null;_endpoint=null;}
    private bool Active()
    {
        _=owner.Edit!.State;
        try {if(_grant is {} g&&ReferenceEquals(_endpoint,owner.Endpoint)&&_clock.GetElapsedTime(_granted).TotalSeconds<60&&Hash(g)==g.Fingerprint&&Page(g.Bindings.Select(b=>b.ObjectId).ToArray(),false).Fingerprint==g.Fingerprint)return true;}
        catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditRejectedException or EditCommandRejectedException){}
        Revoke();return false;
    }
    internal (int Objects,int Seconds) Grant=>Active()?(_grant!.Bindings.Length,Math.Max(0,(int)Math.Ceiling(60-_clock.GetElapsedTime(_granted).TotalSeconds))):(0,0);
    internal object LocalObservation(Guid id,int offset=0,int limit=16)=>Observe(id,offset,limit,null);
    private object Inspect(JsonElement input)
    {
        _=owner.Edit!.State;
        string[] allowed=["playSessionId","worldId","objectId","offset","limit","expectedTick"];
        if(input.ValueKind!=JsonValueKind.Object||input.EnumerateObject().Any(p=>!allowed.Contains(p.Name))||input.EnumerateObject().Select(p=>p.Name).Distinct().Count()!=input.EnumerateObject().Count())throw Invalid();
        Guid Id(string key){if(!input.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(v.GetString(),"D",out Guid id)||id==Guid.Empty||v.GetString()!=id.ToString("D"))throw Invalid();return id;}
        int Count(string key,int fallback,int min,int max){if(!input.TryGetProperty(key,out var v))return fallback;if(v.ValueKind!=JsonValueKind.Number||!v.TryGetInt32(out int n)||n<min||n>max)throw Invalid();return n;}
        Guid play=Id("playSessionId"),world=Id("worldId"),actor=Id("objectId");int offset=Count("offset",0,0,AnimationGraphCodec.MaxPlanInstructions-1),limit=Count("limit",16,1,32);ulong? tick=null;
        if(input.TryGetProperty("expectedTick",out var t)){if(t.ValueKind!=JsonValueKind.Number||!t.TryGetUInt64(out ulong n)||n>9007199254740991)throw Invalid();tick=n;}
        if(!Active()||!_grant!.Bindings.Any(b=>b.ObjectId==actor))throw new EditRejectedException("animator_not_visible");
        if(play!=_grant.PlaySessionId||world!=_grant.WorldId)throw new EditCommandRejectedException("animator_snapshot_stale");
        return Observe(actor,offset,limit,tick);
    }
    private object Observe(Guid actor,int offset,int limit,ulong? expected)
    {
        var s=Source();if(s.Play.Tick>9007199254740991||expected.HasValue&&s.Play.Tick!=expected)throw new EditCommandRejectedException("animator_snapshot_stale");
        object Result(bool valid,string fault,object? observation)=>new{playSessionId=s.Play.SessionId,worldId=s.Play.Document.World.Identity,tick=s.Play.Tick,snapshotValid=valid,faultCode=fault,observation};
        if(s.Play.State==PlayState.Faulted)return Result(false,"play_faulted",null);
        var binding=s.Graphs.DescribeBindings().SingleOrDefault(b=>b.ObjectId==actor)??throw Invalid();
        var debug=s.Graphs.ReadDebug(actor);if(!debug.SnapshotValid||debug.CommittedSequence>9007199254740991)return Result(false,"snapshot_invalid",null);
        object? root=null;var obj=s.Play.Document.World.FindObject(actor);
        if(obj.Has<RootMotionData>()) {
            if(s.Runtime.Characters is not {} characters||!characters.Status.SnapshotValid)return Result(false,"movement_invalid",null);
            var motion=characters.InspectRootMotion(actor);if(motion.Tick!=s.Play.Tick||characters.Status.CommittedSequence>9007199254740991)return Result(false,"movement_invalid",null);
            root=new{desired=motion.DesiredDisplacement,accepted=motion.AcceptedDisplacement,yaw=motion.DesiredYaw,numericalEpoch=characters.NumericalEpoch,numericalSequence=characters.Status.CommittedSequence};
        }
        object? pose=null;
        if(presentation?.Invoke()?.ReadAnimationPresentation() is {} stamp&&stamp.WorldId==debug.Frame.Context.WorldId&&stamp.PublicationId==binding.PublicationId&&stamp.Tick==debug.Frame.Context.Tick&&stamp.RendererFrame<=9007199254740991&&stamp.PoseGeneration<=9007199254740991)
            pose=new{stamp.RendererFrame,stamp.PoseGeneration,stamp.GeometryDraws,stamp.ShadowDraws};
        return Result(true,"none",new{binding=new{binding.ObjectId,binding.GraphId,binding.SkeletonId,binding.PublicationId,binding.GraphHash},debug.Frame.InstanceId,debug.Frame.StateId,debug.Frame.FromStateId,debug.Frame.TransitionId,debug.Frame.TransitionWeight,
            sequence=debug.CommittedSequence,debug.Frame.FrozenPoseGeneration,debug.Frame.Output,instructionTotal=debug.Instructions.Count,offset,nextOffset=offset+limit<debug.Instructions.Count?(int?)(offset+limit):null,
            instructions=debug.Instructions.Skip(offset).Take(limit).Select(r=>new{operation=r.Operation.ToString(),r.NodeId,r.ClipId,r.Previous,r.Current,r.Duration,r.Loop,r.SourceA,r.SourceB,r.Weight,r.CacheGeneration}).ToArray(),
            parameters=debug.Parameters.Select(p=>new{p.Id,kind=p.Kind.ToString(),p.Value}).ToArray(),root,pose});
    }
    private static EditCommandRejectedException Invalid()=>new("animator_input_invalid");
}
