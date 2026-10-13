using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;
using Ncma.Rendering.Scene;
namespace Ncma.Editor.Services;

public sealed record PostModulesReadReview(EditorViewStamp Edit,Guid EndpointId,ulong AudienceRevision,Guid[] Modules,ConnectionView[] Audience,string Fingerprint);

// Only immutable registered metadata; never opens a file, resolves a program or builds a graph.
public sealed class EditorPostProcessWorkspace : IDisposable
{
    public const string ListCapability="ncma.postprocess.modules.list";
    private readonly EditorWorkspace _workspace;private readonly TimeProvider _time;
    private readonly int _owner=Environment.CurrentManagedThreadId;private bool _disposed;
    private Guid[] _approved=[];private EditorViewStamp _stamp;private long _approvedAt;private Func<bool>? _audience;
    public PostProcessModules Modules { get; }
    private EditSession Edit=>_workspace.Owner.Edit!;
    public EditorPostProcessWorkspace(EditorWorkspace workspace,PostProcessModules modules,TimeProvider? time=null)
    {_workspace=workspace;Modules=modules;_time=time??TimeProvider.System;Edit.RegisterInspection(ListDescriptor,Inspect);}
    private void Verify(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Post workspace owner required.");ObjectDisposedException.ThrowIf(_disposed,this);_=Edit.State;}
    private void Writable(){Verify();var s=Edit.State;if(s.Frozen||s.EditBusy||s.HistoryInvalidated)throw new EditRejectedException("post_review_busy_or_frozen");}
    private static string Fingerprint(PostModulesReadReview review)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(review with{Fingerprint=""})));
    private static ConnectionView[] Audience(EditorEndpoint endpoint)=>endpoint.View.Connections.Where(c=>c.Paired&&c.Connected).OrderBy(c=>c.ConnectionId).Select(c=>c with{PendingCount=0}).ToArray();
    public PostModulesReadReview ReviewRead(Guid[] modules)
    {
        Writable();ArgumentNullException.ThrowIfNull(modules);var known=Modules.Describe();
        if(modules.Length is <1 or >64||modules.Distinct().Count()!=modules.Length||modules.Any(id=>known.All(m=>m.ModuleId!=id)))throw new EditRejectedException("post_read_scope_invalid");
        var endpoint=_workspace.Owner.Endpoint??throw new EditRejectedException("post_endpoint_missing");var audience=Audience(endpoint);
        if(audience.Length==0)throw new EditRejectedException("post_audience_missing");
        var review=new PostModulesReadReview(_workspace.Stamp,endpoint.View.InstanceId,endpoint.AudienceRevision,modules.Order().ToArray(),audience,"");return review with{Fingerprint=Fingerprint(review)};
    }
    public void ApproveRead(PostModulesReadReview shown,string fingerprint,bool reviewed)
    {
        Writable();var actual=ReviewRead(shown.Modules);
        if(!reviewed||shown.Fingerprint!=fingerprint||Fingerprint(shown)!=fingerprint||actual.Fingerprint!=fingerprint)throw new EditRejectedException("post_read_review_stale");
        var endpoint=_workspace.Owner.Endpoint!;var audience=shown.Audience.Select(c=>c.ConnectionId).ToArray();Guid instance=shown.EndpointId;ulong epoch=shown.AudienceRevision;
        _approved=shown.Modules.ToArray();_stamp=shown.Edit;_approvedAt=_time.GetTimestamp();
        _audience=()=>ReferenceEquals(_workspace.Owner.Endpoint,endpoint)&&endpoint.View.InstanceId==instance&&endpoint.AudienceRevision==epoch&&Audience(endpoint).Select(c=>c.ConnectionId).SequenceEqual(audience);
    }
    private bool Current()
    {
        var s=Edit.State;bool current=!s.Frozen&&!s.EditBusy&&!s.HistoryInvalidated&&_stamp==_workspace.Stamp&&_approved.Length>0&&
            _time.GetElapsedTime(_approvedAt,_time.GetTimestamp())<TimeSpan.FromSeconds(60)&&(_audience?.Invoke()??false);
        if(!current)RevokeRead();return current;
    }
    private object Inspect(JsonElement input)
    {
        Verify();if(input.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Post page object required.");var fields=input.EnumerateObject().ToArray();
        if(fields.Select(f=>f.Name).Distinct().Count()!=fields.Length||fields.Any(f=>f.Name is not "offset" and not "limit"))throw new ArgumentException("Closed post page input.");
        int offset=input.TryGetProperty("offset",out var o)?o.GetInt32():0,limit=input.TryGetProperty("limit",out var l)?l.GetInt32():8;
        if(offset is <0 or >64||limit is <1 or >8)throw new ArgumentException("Post module page budget.");
        var rows=Current()?Modules.Describe().Where(m=>_approved.Contains(m.ModuleId)).ToArray():[];
        return new{revision=Edit.Revision,total=rows.Length,items=rows.Skip(offset).Take(limit).ToArray(),nextOffset=offset+limit<rows.Length?(int?)(offset+limit):null};
    }
    public void RevokeRead(){Verify();_approved=[];_audience=null;}
    public void Dispose(){if(_disposed)return;Verify();RevokeRead();_disposed=true;}
    private static JsonElement Json(string text){using var d=JsonDocument.Parse(text);return d.RootElement.Clone();}
    private static JsonElement Output()
    {
        var success=JsonNode.Parse(Ncma.Assets.Authoring.MaterialCommands.Descriptor.OutputSchema.GetRawText())!.AsObject();var p=success["properties"]!.AsObject();
        p["status"]=JsonNode.Parse("{\"const\":\"ok\"}");p["code"]=JsonNode.Parse("{\"const\":\"ok\"}");p["changed"]=JsonNode.Parse("{\"const\":false}");p["replayed"]=JsonNode.Parse("{\"const\":false}");
        p["data"]=JsonNode.Parse("""
        {"type":"object","additionalProperties":false,"required":["revision","total","items","nextOffset"],"properties":{
          "revision":{"type":"integer","minimum":0},"total":{"type":"integer","minimum":0,"maximum":64},"nextOffset":{"anyOf":[{"type":"integer","minimum":0,"maximum":72},{"type":"null"}]},
          "items":{"type":"array","maxItems":8,"items":{"type":"object","additionalProperties":false,"required":["moduleId","version","kind","name","order","parameters","requires"],"properties":{
            "moduleId":{"type":"string","format":"uuid"},"version":{"type":"integer","minimum":1},"kind":{"enum":["tone","feature"]},"name":{"type":"string","maxLength":64},"order":{"type":"integer","minimum":0,"maximum":1024},
            "requires":{"type":"array","maxItems":8,"items":{"type":"string","format":"uuid"}},"parameters":{"type":"array","maxItems":16,"items":{"type":"object","additionalProperties":false,"required":["name","minimum","maximum","integer","default"],"properties":{
              "name":{"type":"string","maxLength":48},"minimum":{"type":"number","minimum":-65504,"maximum":65504},"maximum":{"type":"number","minimum":-65504,"maximum":65504},"integer":{"type":"boolean"},"default":{"anyOf":[{"type":"number","minimum":-65504,"maximum":65504},{"type":"null"}]}}}}}}}}}
        """);
        var failure=success.DeepClone().AsObject();var e=failure["properties"]!.AsObject();e["status"]=JsonNode.Parse("{\"enum\":[\"error\",\"denied\",\"conflict\"]}");e["code"]=JsonNode.Parse("{\"type\":\"string\",\"maxLength\":128}");e["data"]=JsonNode.Parse("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"message\":{\"type\":\"string\",\"maxLength\":4194304}}}");
        return JsonSerializer.SerializeToElement(new JsonObject{["type"]="object",["oneOf"]=new JsonArray(success,failure)});
    }
    public static CapabilityDescriptor ListDescriptor=>new(ListCapability,"Read human-approved registered post module UUID/version/ranges/order only; no files, code, GPU or mutation grant.",MutationRisk.ReadOnly,
        Json("""{"type":"object","additionalProperties":false,"properties":{"offset":{"type":"integer","minimum":0,"maximum":64},"limit":{"type":"integer","minimum":1,"maximum":8}}}"""),Output());
}
