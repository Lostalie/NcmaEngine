using System.Text.Json;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.Services;

public static partial class RenderingEditorAdapter
{
    // CPU-only builders for the ORIGINAL scene transaction. They confer no execution permission.
    public static object PostOperation(PostProcessModules modules,Guid id,PostProcessData value)
    {
        if(id==Guid.Empty)throw new ArgumentException("Post target UUID required.");
        value=modules.Validate(value);
        return new{op="set_component",objectId=id,typeId=PostProcessData.TypeId,version=1,
            data=JsonSerializer.SerializeToElement(value,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})};
    }
    public static PostProcessData PostField(PostProcessModules modules,PostProcessData current,string field,double number)
    {
        current=modules.Validate(current);if(!double.IsFinite(number)||number is <-65504 or >65504)throw new ArgumentException("Post field finite range.");
        if(field=="enabled"&&number is 0 or 1)return modules.Validate(current with{Enabled=number==1});
        if(field=="ev"&&number is >=-6 and <=4)return modules.Validate(current with{ExposureEv=(float)number});
        string[] key=field.Split('/');if(key.Length!=2||!Guid.TryParseExact(key[0],"D",out var id))throw new ArgumentException("Exact Feature/parameter required.");
        var stack=PostProcessData.DecodeStack(current.StackJson);var feature=stack.SingleOrDefault(f=>f.ModuleId==id)??throw new ArgumentException("Feature not installed.");
        int index=Array.FindIndex(feature.Parameters,p=>p.Name==key[1]);if(index<0)throw new ArgumentException("Unknown Feature parameter.");
        var rule=modules.Describe().Single(m=>m.ModuleId==id).Parameters.Single(p=>p.Name==key[1]);
        if(number<rule.Minimum||number>rule.Maximum||rule.Integer&&number!=Math.Truncate(number))throw new ArgumentException("Registered parameter range/type.");
        feature.Parameters[index]=feature.Parameters[index] with{Value=(float)number};
        return modules.Validate(current with{StackJson=PostProcessData.EncodeStack(stack)});
    }
    public static PostProcessData PostTone(PostProcessModules modules,PostProcessData current,Guid id)
    {
        var tone=modules.Describe().Single(m=>m.ModuleId==id&&m.Kind=="tone");
        return modules.Validate(current with{ToneModule=id,ToneVersion=tone.Version});
    }
    public static PostProcessData PostFeature(PostProcessModules modules,PostProcessData current,Guid id,int action)
    {
        current=modules.Validate(current);var stack=PostProcessData.DecodeStack(current.StackJson).ToList();int index=stack.FindIndex(f=>f.ModuleId==id);
        if(action==0){if(index<0)throw new ArgumentException("Feature not installed.");stack.RemoveAt(index);}
        else if(action==2){
            if(index>=0)throw new ArgumentException("Feature already installed.");var m=modules.Describe().Single(m=>m.ModuleId==id&&m.Kind=="feature");
            var value=new PostFeatureInput(id,m.Version,m.Parameters.Select(p=>new PostParameterInput(p.Name,p.Default??p.Minimum)).ToArray());
            stack.Add(value);var order=modules.Describe().ToDictionary(m=>m.ModuleId,m=>m.Order);stack=stack.OrderBy(f=>order[f.ModuleId]).ToList();
        }else if(action is -1 or 1){int next=index+action;if(index<0||next<0||next>=stack.Count)throw new ArgumentException("Feature order boundary.");(stack[index],stack[next])=(stack[next],stack[index]);}
        else throw new ArgumentException("Unknown Feature operation.");
        return modules.Validate(current with{StackJson=PostProcessData.EncodeStack(stack)});
    }
}
