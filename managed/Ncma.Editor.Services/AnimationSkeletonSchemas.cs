using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
namespace Ncma.Editor.Services;

public static class AnimationSkeletonSchemas
{
    public const string Name="ncma.animgraph.bones";
    private static JsonObject Id()=>new(){["type"]="string",["pattern"]="^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"};
    private static JsonObject Number(int low,int high)=>new(){["type"]="integer",["minimum"]=low,["maximum"]=high};
    private static JsonObject Hash()=>new(){["type"]="string",["pattern"]="^[0-9A-F]{64}$"};
    private static JsonObject Closed(JsonObject p)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=p,["required"]=new JsonArray(p.Select(f=>(JsonNode?)JsonValue.Create(f.Key)).ToArray())};
    public static CapabilityDescriptor Descriptor{get{
        var input=Closed(new(){["graphId"]=Id(),["offset"]=Number(0,1024),["limit"]=Number(1,8)});
        var page=Closed(new(){["skeletonId"]=Id(),["hash"]=Hash(),["total"]=Number(1,1024),["offset"]=Number(0,1024),["nextOffset"]=new JsonObject{["oneOf"]=new JsonArray(Number(0,1032),new JsonObject{["type"]="null"})},["bones"]=new JsonObject{["type"]="array",["minItems"]=0,["maxItems"]=8,["items"]=Closed(new(){["path"]=new JsonObject{["type"]="string",["maxLength"]=4096},["parent"]=Number(-1,1023)})}});
        var outer=JsonNode.Parse(AnimationGraphInspectionSchemas.Descriptors()[0].OutputSchema.GetRawText())!;outer["oneOf"]![0]!["properties"]!["data"]=Closed(new(){["graphId"]=Id(),["graphHash"]=Hash(),["publication"]=Id(),["resourcesPrepared"]=new JsonObject{["const"]=true},["livePlay"]=new JsonObject{["const"]=false},["poseMemory"]=new JsonObject{["const"]=false},["page"]=page});
        return new(Name,"Read copied full stable bone paths from the separately human-approved exact graph/NCA publication. No files, pose memory, live controls, preparation or approval authority.",MutationRisk.ReadOnly,JsonSerializer.SerializeToElement(input),JsonSerializer.SerializeToElement(outer));
    }}
}
