using System.Security.Cryptography;
namespace Ncma.Animation;

// Prepared copied metadata, not asset leases, numerical resources, a clock or World authority.
public sealed class AnimationMontageProgram
{
    private readonly AnimationMontageDefinition _definition;
    private readonly Dictionary<Guid,AnimationMontageSection> _sections;
    private readonly Dictionary<Guid,AnimationMontageSlot> _slots;
    public Guid AssetId=>_definition.AssetId;public Guid SkeletonId=>_definition.SkeletonId;
    public string ContentHash{get;}public ulong ResourceGeneration{get;}
    public AnimationMontageProgram(AnimationMontageDefinition definition,ulong skeletonGeneration,IReadOnlyList<AnimationClipDescriptor> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);_definition=AnimationMontageCodec.Decode(AnimationMontageCodec.Encode(definition));ResourceGeneration=skeletonGeneration;
        if(skeletonGeneration==0||clips.Count is <1 or >128)throw new ArgumentException("Bounded committed metadata required.");
        var required=_definition.Sections.Select(s=>s.ClipId).ToHashSet();var durations=new Dictionary<Guid,double>();
        foreach(var clip in clips){if(!required.Contains(clip.Id)||!durations.TryAdd(clip.Id,clip.Duration)||clip.SkeletonId!=SkeletonId||clip.Generation!=skeletonGeneration||!double.IsFinite(clip.Duration)||clip.Duration is <.001 or >600)throw new ArgumentException("Exact same-skeleton/generation Clip metadata required.");}
        if(!required.SetEquals(durations.Keys)||_definition.Sections.Any(s=>s.End>durations[s.ClipId]))throw new ArgumentException("Complete actual Clip durations must cover every Section interval.");
        ContentHash=Convert.ToHexString(SHA256.HashData(AnimationMontageCodec.Encode(_definition)));_sections=_definition.Sections.ToDictionary(s=>s.Id);_slots=_definition.Slots.ToDictionary(s=>s.Id);
    }
    public AnimationMontageDefinition CopyDefinition()=>_definition with{Slots=_definition.Slots.ToArray(),Sections=_definition.Sections.ToArray()};
    public AnimationMontageSlot RequireSlot(Guid id)=>_slots.TryGetValue(id,out var value)?value:throw new ArgumentException("Exact Slot UUID required.");
    public AnimationMontageSection RequireSection(Guid id)=>_sections.TryGetValue(id,out var value)?value:throw new ArgumentException("Exact Section UUID required.");
}
