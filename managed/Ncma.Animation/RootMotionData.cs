using System.Text.Json;
using Ncma.Runtime;
namespace Ncma.Animation;

// Opt-in XZ/+Y-yaw extraction in canonical model metres. Capsule foot offset remains CharacterData.
// The index identifies the single top-level rig root, not a GameObject parent or runtime handle.
public readonly record struct RootMotionData(int RootBoneIndex) : IComponent
{
    public const string TypeId = "ncma.animation.root_motion";
    public static RootMotionData Validate(RootMotionData value)
    {
        if(value.RootBoneIndex is <0 or >1023)throw new ArgumentException("Invalid rig root index.");
        return value;
    }
    public static ComponentRegistry Register(ComponentRegistry registry)
    {
        registry.Register<RootMotionData>(TypeId,1,JsonSerializer.Serialize(new {
            type="object",additionalProperties=false,required=new[]{"rootBoneIndex"},
            properties=new{rootBoneIndex=new{type="integer"}}}),Validate,runtimeAttachable:false);
        return registry;
    }
}
