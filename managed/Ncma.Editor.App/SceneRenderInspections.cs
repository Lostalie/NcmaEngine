using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
namespace Ncma.Editor.App;

internal static class SceneRenderInspections
{
    internal static void Register(EditSession edit,RendererSession renderer,Func<SceneRenderSession?> current)
    {
        static JsonElement Json(string text) { using var doc=JsonDocument.Parse(text);return doc.RootElement.Clone(); }
        void Add(string name,string description,Func<object> read,string schema) {
            var output=Json("""
                {"type":"object","required":["contractVersion","requestId","sessionId","revision","status","code","changed","data","executionRevision","replayed"],
                 "properties":{"contractVersion":{"const":2},"requestId":{"type":"string"},"sessionId":{"type":"string"},"revision":{"type":"integer"},"status":{"type":"string"},"code":{"type":"string"},"changed":{"type":"boolean"},"data":__SCHEMA__,"executionRevision":{"type":"integer"},"replayed":{"type":"boolean"}}}
                """.Replace("__SCHEMA__",schema,StringComparison.Ordinal));
            edit.RegisterInspection(new(name,description,MutationRisk.ReadOnly,Json("""{"type":"object","additionalProperties":false,"properties":{}}"""),output),input=>{
                if(input.EnumerateObject().Any())throw new ArgumentException("Empty inspection input required.");return read();
            });
        }
        Add("ncma.render.inspect_pipeline","Inspect managed scene-v4 pipeline identity, explicit camera and bounded diagnostics; no GPU mutation.",
            ()=>new{version=4,referenceOnly=false,worldId=current()?.View?.WorldId,frameIdentity=current()?.View?.FrameIdentity,camera=current()?.View?.Camera?.ObjectId,diagnostics=current()?.Diagnostics.Take(64).ToArray()??[]},
            """{"type":"object","required":["version","referenceOnly","worldId","frameIdentity","camera","diagnostics"],"properties":{"version":{"const":4},"referenceOnly":{"const":false},"worldId":{"type":["string","null"]},"frameIdentity":{"type":["string","null"]},"camera":{"type":["string","null"]},"diagnostics":{"type":"array","maxItems":64,"items":{"type":"object"}}}}""");
        Add("ncma.render.inspect_graph","Inspect copied scene-v4 typed resources and ordered public passes; empty before first 3D draw.",
            ()=>new{version=4,passes=current()?.Plan?.PassNames??[],resources=current()?.Plan?.Resources??[]},
            """{"type":"object","required":["version","passes","resources"],"properties":{"version":{"const":4},"passes":{"type":"array","maxItems":16,"items":{"type":"string"}},"resources":{"type":"array","maxItems":16,"items":{"type":"object"}}}}""");
        Add("ncma.render.get_profile","Inspect bounded scene/pose/palette ABI/GPU costs and DX11 validation; no World/GPU mutation or animation control.",
            ()=>new{version=4,renderer=renderer.Stats,pipeline=renderer.PipelineStats,costs=current()?.Costs,submitCalls=renderer.SubmitCalls,copiedBytes=renderer.CopiedBytes,
                animation=current()?.Animation?.Costs,skinAbiMilliseconds=current()?.SkinAbiMilliseconds,skinBackpressureFrames=current()?.SkinBackpressureFrames},
            """{"type":"object","required":["version","renderer","pipeline","costs","submitCalls","copiedBytes","animation","skinAbiMilliseconds","skinBackpressureFrames"],"properties":{"version":{"const":4},"renderer":{"type":"object"},"pipeline":{"type":"object"},"costs":{"type":["object","null"]},"submitCalls":{"type":"integer"},"copiedBytes":{"type":"integer"},"animation":{"type":["object","null"]},"skinAbiMilliseconds":{"type":["number","null"]},"skinBackpressureFrames":{"type":["integer","null"]}}}""");
    }
}
