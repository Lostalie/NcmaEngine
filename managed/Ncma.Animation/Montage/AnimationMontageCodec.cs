using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Animation;

// Strict standalone authored data only; no UE import, code, World/native references or execution authority.
public static class AnimationMontageCodec
{
    public const int CurrentVersion=1,MaxBytes=256*1024,MaxSlots=16,MaxSections=64;
    public const string Extension=".ncmamontage";
    private static readonly UTF8Encoding Utf8=new(false,true);
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12};
    public static void RequireExtension(string path){if(string.IsNullOrWhiteSpace(path)||path.Contains('\0')||!string.Equals(Path.GetExtension(path),Extension,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Strict .ncmamontage required.");}
    public static byte[] Encode(AnimationMontageDefinition definition){Validate(definition);var copy=definition with{Slots=definition.Slots.OrderBy(s=>s.Id).ToArray(),Sections=definition.Sections.OrderBy(s=>s.Id).ToArray()};byte[] data=JsonSerializer.SerializeToUtf8Bytes(copy,Json);if(data.Length>MaxBytes)throw new ArgumentException("Montage byte budget.");return data;}
    public static AnimationMontageDefinition Decode(ReadOnlySpan<byte> input){if(input.Length is <2 or >MaxBytes)throw new ArgumentException("Montage byte budget.");_=Utf8.GetCharCount(input);using var document=JsonDocument.Parse(input.ToArray(),new(){MaxDepth=12});Visit(document.RootElement);var value=JsonSerializer.Deserialize<AnimationMontageDefinition>(input,Json)??throw new ArgumentException("Montage data required.");Validate(value);return value;}
    private static void Visit(JsonElement e){if(e.ValueKind==JsonValueKind.Object){var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!keys.Add(p.Name))throw new ArgumentException("Duplicate montage property.");Visit(p.Value);}}else if(e.ValueKind==JsonValueKind.Array)foreach(var child in e.EnumerateArray())Visit(child);}
    // Author-owned incomplete rows only. Cross references/reachability remain mandatory at publication.
    public static AnimationMontageDefinition CopyDraft(AnimationMontageDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if(d.Version!=CurrentVersion||d.AssetId==Guid.Empty||d.SkeletonId==Guid.Empty||d.AssetId==d.SkeletonId||d.Slots is null||d.Sections is null||d.Slots.Length>MaxSlots||d.Sections.Length>MaxSections)throw new ArgumentException("Bounded Montage draft required.");
        AnimationGraphCodec.Text(d.Name,256);var ids=new HashSet<Guid>{d.AssetId,d.SkeletonId};var names=new HashSet<string>(StringComparer.Ordinal);
        void Id(Guid id){if(id==Guid.Empty||!ids.Add(id))throw new ArgumentException("Distinct Montage draft UUIDs required.");}
        foreach(var s in d.Slots){if(s is null)throw new ArgumentException("Slot required.");Id(s.Id);AnimationGraphCodec.Text(s.Name);if(!names.Add(s.Name)||s.Priority is <0 or >255)throw new ArgumentException("Unique Slot name/bounded priority.");AnimationGraphCodec.Scalar(s.BlendIn,0,10);AnimationGraphCodec.Scalar(s.BlendOut,0,10);}
        foreach(var s in d.Sections){if(s is null)throw new ArgumentException("Section required.");Id(s.Id);AnimationGraphCodec.Text(s.Name);if(s.SlotId==Guid.Empty||s.ClipId==Guid.Empty)throw new ArgumentException("Explicit Section Slot/Clip UUID required.");AnimationGraphCodec.Scalar(s.Start,0,600);AnimationGraphCodec.Scalar(s.End,.001,600);if(s.End-s.Start<.001)throw new ArgumentException("Section duration at least1ms.");}
        foreach(var group in d.Sections.GroupBy(s=>s.SlotId))if(group.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=group.Count())throw new ArgumentException("Unique per-Slot Section names.");
        if(d.Sections.Any(s=>ids.Contains(s.ClipId)))throw new ArgumentException("External Clip cannot alias Montage identities.");
        if(JsonSerializer.SerializeToUtf8Bytes(d,Json).Length>MaxBytes)throw new ArgumentException("Montage draft byte budget.");
        return d with{Slots=d.Slots.Select(s=>s with{}).ToArray(),Sections=d.Sections.Select(s=>s with{}).ToArray()};
    }
    public static void Validate(AnimationMontageDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);if(d.Version!=CurrentVersion||d.AssetId==Guid.Empty||d.SkeletonId==Guid.Empty||d.AssetId==d.SkeletonId||d.Slots is null||d.Sections is null||d.Slots.Length is <1 or >MaxSlots||d.Sections.Length is <1 or >MaxSections)throw new ArgumentException("Strict bounded montage identity/collections required.");
        AnimationGraphCodec.Text(d.Name,256);var ids=new HashSet<Guid>{d.AssetId,d.SkeletonId};var slots=new Dictionary<Guid,AnimationMontageSlot>();var sections=new Dictionary<Guid,AnimationMontageSection>();var names=new HashSet<string>(StringComparer.Ordinal);
        void Id(Guid id){if(id==Guid.Empty||!ids.Add(id))throw new ArgumentException("Unique persistent montage elements required.");}
        foreach(var s in d.Slots){if(s is null)throw new ArgumentException("Slot required.");Id(s.Id);AnimationGraphCodec.Text(s.Name);if(!names.Add(s.Name)||s.Priority is <0 or >255)throw new ArgumentException("Unique Slot name/bounded priority.");AnimationGraphCodec.Scalar(s.BlendIn,0,10);AnimationGraphCodec.Scalar(s.BlendOut,0,10);slots.Add(s.Id,s);}
        foreach(var s in d.Sections){if(s is null)throw new ArgumentException("Section required.");Id(s.Id);AnimationGraphCodec.Text(s.Name);if(!slots.ContainsKey(s.SlotId)||s.ClipId==Guid.Empty)throw new ArgumentException("Section exact Slot/Clip required.");AnimationGraphCodec.Scalar(s.Start,0,600);AnimationGraphCodec.Scalar(s.End,.001,600);if(s.End-s.Start<.001)throw new ArgumentException("Section duration at least1ms.");sections.Add(s.Id,s);}
        foreach(var group in d.Sections.GroupBy(s=>s.SlotId))if(group.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=group.Count())throw new ArgumentException("Unique per-Slot Section names.");
        foreach(var s in d.Sections)if(s.NextSection!=Guid.Empty&&(!sections.TryGetValue(s.NextSection,out var next)||next.SlotId!=s.SlotId))throw new ArgumentException("Section successor must belong to the same Slot.");
        foreach(var slot in d.Slots){if(!sections.TryGetValue(slot.EntrySection,out var entry)||entry.SlotId!=slot.Id)throw new ArgumentException("Exact Slot entry Section required.");var reached=new HashSet<Guid>();Guid at=entry.Id;while(at!=Guid.Empty&&reached.Add(at))at=sections[at].NextSection;if(d.Sections.Any(s=>s.SlotId==slot.Id&&!reached.Contains(s.Id)))throw new ArgumentException("Every Section must be reachable through its Slot entry sequence.");}
        foreach(Guid clip in d.Sections.Select(s=>s.ClipId).Distinct())if(ids.Contains(clip))throw new ArgumentException("External Clip cannot alias persistent montage elements.");
    }
}
