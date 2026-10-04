using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Rendering;
namespace Ncma.Editor.Services;
// No private rendering history. Configuration is a value component on an ordinary flat GameObject;
// existing scoped Core transactions/inspection/persistence/Undo/Redo are the only mutation path.
public static class RenderingEditorAdapter
{
    private static JsonElement Json(string value){using var doc=JsonDocument.Parse(value);return doc.RootElement.Clone();}
    public static void RegisterInspections(EditSession edit,RenderPipelineService service,RendererSession renderer)
    {
        const string empty="""{"type":"object","additionalProperties":false,"properties":{}}""";
        void Add(string name,string description,Func<object> read,string dataSchema) {
            var output=Json("""
                {"type":"object","required":["contractVersion","requestId","sessionId","revision","status","code","changed","data","executionRevision","replayed"],
                 "properties":{"contractVersion":{"const":2},"requestId":{"type":"string"},"sessionId":{"type":"string"},"revision":{"type":"integer"},"status":{"type":"string"},"code":{"type":"string"},"changed":{"type":"boolean"},"data":__DATA_SCHEMA__,"executionRevision":{"type":"integer"},"replayed":{"type":"boolean"}}}
                """.Replace("__DATA_SCHEMA__",dataSchema,StringComparison.Ordinal));
            edit.RegisterInspection(new(name,description,MutationRisk.ReadOnly,Json(empty),output),input=>{
                if(input.EnumerateObject().Any())throw new ArgumentException("Empty inspection input required.");
                return read();
            });
        }
        Add("ncma.render.inspect_pipeline","Inspect the active managed reference pipeline generation and supported operation set.",
            ()=>new{version=1,generation=service.Generation,referenceOnly=true,operations=new[]{"shadow","geometry","tonemap","clear"}},
            """{"type":"object","required":["version","generation","referenceOnly","operations"],"properties":{"version":{"const":1},"generation":{"type":"integer"},"referenceOnly":{"const":true},"operations":{"type":"array","items":{"type":"string"}}}}""");
        Add("ncma.render.inspect_graph","Inspect the compiled copied resource contracts and ordered pass names.",
            ()=>new{version=1,generation=service.Generation,passes=service.Plan.PassNames,resources=service.Plan.Resources},
            """{"type":"object","required":["version","generation","passes","resources"],"properties":{"version":{"const":1},"generation":{"type":"integer"},"passes":{"type":"array","items":{"type":"string"}},"resources":{"type":"array","items":{"type":"object"}}}}""");
        Add("ncma.render.get_profile","Inspect bounded CPU timings, ABI counters and DX11 validation counts; no device access.",
            ()=>new{version=1,renderer=renderer.Stats,submitCalls=renderer.SubmitCalls,copiedBytes=renderer.CopiedBytes,encodeMilliseconds=renderer.LastEncodeMilliseconds,planBuilds=service.PlanBuilds},
            """{"type":"object","required":["version","renderer","submitCalls","copiedBytes","encodeMilliseconds","planBuilds"],"properties":{"version":{"const":1},"renderer":{"type":"object"},"submitCalls":{"type":"integer"},"copiedBytes":{"type":"integer"},"encodeMilliseconds":{"type":"number"},"planBuilds":{"type":"integer"}}}""");
    }
    public static RenderConfiguration ReadConfiguration(EditSession edit,Guid objectId)
    {
        var item=edit.Document.World.FindObject(objectId);
        return item.Get<RenderConfiguration>();
    }
    public static CapabilityRequest SettingsRequest(EditSession edit,Guid objectId,RenderConfiguration configuration,ulong expectedRevision)
    {
        var data=edit.Document.World.Components.Encode(configuration);
        var input=JsonSerializer.SerializeToElement(new{label="Configure rendering",operations=new[]{new{op="set_component",objectId,typeId=RenderConfiguration.ComponentType,version=1,data}}});
        return new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,expectedRevision,"ncma.scene.transaction",input);
    }
}
