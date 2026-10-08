using System.Text.Json.Serialization;
namespace Ncma.Animation;

public sealed record AnimationMontageSlot([property:JsonRequired]Guid Id,[property:JsonRequired]string Name,
    [property:JsonRequired]Guid EntrySection,[property:JsonRequired]int Priority,[property:JsonRequired]bool Interruptible,
    [property:JsonRequired]bool RootMotion,[property:JsonRequired]double BlendIn,[property:JsonRequired]double BlendOut);
public sealed record AnimationMontageSection([property:JsonRequired]Guid Id,[property:JsonRequired]string Name,
    [property:JsonRequired]Guid SlotId,[property:JsonRequired]Guid ClipId,[property:JsonRequired]double Start,
    [property:JsonRequired]double End,[property:JsonRequired]Guid NextSection);
public sealed record AnimationMontageDefinition([property:JsonRequired]int Version,[property:JsonRequired]Guid AssetId,
    [property:JsonRequired]string Name,[property:JsonRequired]Guid SkeletonId,[property:JsonRequired]AnimationMontageSlot[] Slots,
    [property:JsonRequired]AnimationMontageSection[] Sections);
