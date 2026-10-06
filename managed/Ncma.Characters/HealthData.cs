using Ncma.Runtime;
namespace Ncma.Characters;
public readonly record struct HealthData(float Current,float Maximum) : IComponent
{
    public const string TypeId="ncma.combat.health";
    public static HealthData Validate(HealthData v) {
        if(!float.IsFinite(v.Current)||!float.IsFinite(v.Maximum)||v.Maximum is <=0 or >1000000 ||v.Current<0||v.Current>v.Maximum)throw new ArgumentException("Invalid bounded health value.");
        return v;
    }
}
