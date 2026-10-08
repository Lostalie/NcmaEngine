using System.Text.Json.Serialization;
namespace Ncma.Animation;

public sealed record AnimationBoneIdentity(string Path,int Parent);
public sealed record AnimationBoneWeight([property:JsonRequired]string BonePath,[property:JsonRequired]float Weight);
public sealed record AnimationBoneMask([property:JsonRequired]Guid Id,[property:JsonRequired]Guid SkeletonId,
    [property:JsonRequired]string SkeletonHash,[property:JsonRequired]AnimationBoneWeight[] Bones);

// Exact immutable preparation identity, not asset ownership or live pose memory. No name-only fallback.
public sealed class AnimationBoneMaskProgram
{
    private readonly AnimationBoneMask _definition;private readonly float[] _weights;
    public Guid Id=>_definition.Id;public int BoneCount=>_weights.Length;
    public AnimationBoneMaskProgram(AnimationBoneMask mask,Guid skeleton,string hash,IReadOnlyList<AnimationBoneIdentity> bones)
    {
        ArgumentNullException.ThrowIfNull(mask);ArgumentNullException.ThrowIfNull(bones);Validate(mask);
        if(skeleton!=mask.SkeletonId||hash!=mask.SkeletonHash||bones.Count is <1 or >1024)throw new ArgumentException("Exact skeleton identity required.");
        _weights=new float[bones.Count];var indices=new Dictionary<string,int>(StringComparer.Ordinal);
        for(int i=0;i<bones.Count;i++){var b=bones[i]??throw new ArgumentException("Bone identity required.");Path(b.Path);if(b.Parent< -1||b.Parent>=i||!indices.TryAdd(b.Path,i)||b.Parent<0&&b.Path.Contains('/')||
            (b.Parent>=0&&!b.Path.StartsWith(bones[b.Parent].Path+"/",StringComparison.Ordinal))||b.Parent>=0&&b.Path[(bones[b.Parent].Path.Length+1)..].Contains('/'))throw new ArgumentException("Stable unique full bone paths/topology required.");}
        foreach(var b in mask.Bones){if(!indices.TryGetValue(b.BonePath,out int index)||bones[index].Parent<0&&b.Weight!=0)throw new ArgumentException("Missing bone or root layer authority rejected.");_weights[index]=b.Weight;}
        _definition=mask with{Bones=mask.Bones.OrderBy(b=>b.BonePath,StringComparer.Ordinal).ToArray()};
    }
    public static void Validate(AnimationBoneMask mask,bool allowEmpty=false)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if(mask.Id==Guid.Empty||mask.SkeletonId==Guid.Empty||mask.SkeletonHash is null||mask.SkeletonHash.Length!=64||mask.SkeletonHash.Any(c=>c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))||mask.Bones is null||mask.Bones.Length>1024||!allowEmpty&&mask.Bones.Length==0)throw new ArgumentException("Bounded mask UUID/hash/bones required.");
        var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var bone in mask.Bones){if(bone is null)throw new ArgumentException("Mask bone required.");Path(bone.BonePath);if(!seen.Add(bone.BonePath)||!float.IsFinite(bone.Weight)||bone.Weight is <0 or >1)throw new ArgumentException("Unique finite bone weights required.");}
    }
    private static void Path(string path){AnimationGraphCodec.Text(path,4096);if(path.StartsWith('/')||path.EndsWith('/')||path.Contains("//")||path.Split('/').Any(p=>p is "." or ".."||p.Contains('\\')))throw new ArgumentException("Canonical stable bone path required.");}
    public AnimationBoneMask CopyDefinition()=>_definition with{Bones=_definition.Bones.ToArray()};
    public void CopyWeights(Span<float> destination){if(destination.Length<_weights.Length)throw new ArgumentException("Mask copy capacity.");_weights.CopyTo(destination);}
}
