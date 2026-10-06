using System.Text.Json;
using Ncma.Runtime;
namespace Ncma.Animation;

// Small explicit persistent definition, NOT an Animator graph or old demo type alias.
// Windows are normalized clip phase; UUIDs resolve only in the pinned model generation.
public readonly record struct ActionDefinitionData(Guid DefinitionId, Guid IdleClip, Guid RunClip, Guid AttackClip, Guid DodgeClip,
    float HitStart, float HitEnd, float CancelStart, float CancelEnd, float ComboStart, float ComboEnd,
    float InvulnerableStart, float InvulnerableEnd, float Damage, float Reach, uint HitMask, int BufferTicks) : IComponent
{
    public const string TypeId="ncma.action.definition";
    public static ActionDefinitionData Validate(ActionDefinitionData v)
    {
        if(v.DefinitionId==Guid.Empty || new[]{v.IdleClip,v.RunClip,v.AttackClip,v.DodgeClip}.Any(id=>id==Guid.Empty) ||
            new[]{v.IdleClip,v.RunClip,v.AttackClip,v.DodgeClip}.Distinct().Count()!=4 ||
            !Window(v.HitStart,v.HitEnd)||!Window(v.CancelStart,v.CancelEnd)||!Window(v.ComboStart,v.ComboEnd)||!Window(v.InvulnerableStart,v.InvulnerableEnd)||
            !float.IsFinite(v.Damage)||v.Damage is <=0 or >100000 || !float.IsFinite(v.Reach)||v.Reach is <=0 or >100 ||v.HitMask==0||v.BufferTicks is <1 or >120)
            throw new ArgumentException("Invalid action UUIDs/windows/budgets.");
        return v;
    }
    private static bool Window(float a,float b)=>float.IsFinite(a)&&float.IsFinite(b)&&a>=0&&a<b&&b<=1;
    public static ComponentRegistry Register(ComponentRegistry registry)
    {
        string[] ids=["definitionId","idleClip","runClip","attackClip","dodgeClip"];
        string[] numbers=["hitStart","hitEnd","cancelStart","cancelEnd","comboStart","comboEnd","invulnerableStart","invulnerableEnd","damage","reach"];
        var properties=ids.ToDictionary(n=>n,n=>new{type="string"});foreach(string n in numbers)properties.Add(n,new{type="number"});properties.Add("hitMask",new{type="integer"});properties.Add("bufferTicks",new{type="integer"});
        registry.Register<ActionDefinitionData>(TypeId,1,JsonSerializer.Serialize(new{type="object",additionalProperties=false,required=properties.Keys,properties}),Validate,runtimeAttachable:false);return registry;
    }
}
