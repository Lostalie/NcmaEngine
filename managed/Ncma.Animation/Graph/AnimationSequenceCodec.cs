using System.Text.Json;
namespace Ncma.Animation;

// Closed data, not executable test code. The actual program validates UUID/type assertions separately.
public static class AnimationSequenceCodec
{
    public static AnimationSequenceCase Decode(JsonElement input)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(input.GetRawText())>48*1024)throw new ArgumentException("Sequence byte budget.");
        AnimationGraphEdits.Closed(input,"fixedDelta","steps","writes","assertions");
        double Number(JsonElement value,string key){var n=value.GetProperty(key);if(n.ValueKind!=JsonValueKind.Number||!n.TryGetDouble(out double d)||!double.IsFinite(d))throw new ArgumentException("Finite sequence number.");return d;}
        int Integer(JsonElement value,string key,int max){var n=value.GetProperty(key);if(n.ValueKind!=JsonValueKind.Number||!n.TryGetInt32(out int d)||d<1||d>max)throw new ArgumentException("Bounded sequence integer.");return d;}
        T Kind<T>(JsonElement row)where T:struct,Enum {var name=row.GetProperty("kind");if(name.ValueKind==JsonValueKind.String)foreach(T v in Enum.GetValues<T>())if(JsonNamingPolicy.CamelCase.ConvertName(v.ToString())==name.GetString())return v;throw new ArgumentException("Exact sequence enum.");}
        var writes=input.GetProperty("writes");var assertions=input.GetProperty("assertions");
        if(writes.ValueKind!=JsonValueKind.Array||writes.GetArrayLength()>AnimationGraphSequence.MaxWrites||assertions.ValueKind!=JsonValueKind.Array||assertions.GetArrayLength()>AnimationGraphSequence.MaxAssertions)throw new ArgumentException("Sequence collection budget.");
        var w=new List<AnimationSequenceWrite>();foreach(var row in writes.EnumerateArray()){AnimationGraphEdits.Closed(row,"step","parameterId","kind","value");w.Add(new(Integer(row,"step",256),AnimationGraphEdits.Uuid(row,"parameterId"),Kind<AnimationParameterKind>(row),Number(row,"value")));}
        var a=new List<AnimationSequenceAssertion>();foreach(var row in assertions.EnumerateArray()){AnimationGraphEdits.Closed(row,"step","kind","subjectId","value");a.Add(new(Integer(row,"step",256),Kind<AnimationSequenceAssertionKind>(row),AnimationGraphEdits.Uuid(row,"subjectId",true),Number(row,"value")));}
        double delta=Number(input,"fixedDelta");AnimationGraphCodec.Scalar(delta,.001,1);return new(delta,Integer(input,"steps",256),w.ToArray(),a.ToArray());
    }
    public static JsonElement Encode(AnimationSequenceCase input)=>JsonSerializer.SerializeToElement(new{input.FixedDelta,input.Steps,
        writes=input.Writes.Select(w=>new{w.Step,w.ParameterId,kind=JsonNamingPolicy.CamelCase.ConvertName(w.Kind.ToString()),w.Value}),
        assertions=input.Assertions.Select(a=>new{a.Step,kind=JsonNamingPolicy.CamelCase.ConvertName(a.Kind.ToString()),a.SubjectId,a.Value})},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
}
