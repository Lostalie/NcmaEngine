using System.Text.Json.Serialization;
namespace Ncma.Animation;

// Owned authoring/numeric inputs only. No clocks, resource handles or executable expressions.
public sealed record BlendSpaceAxis([property:JsonRequired] Guid ParameterId,[property:JsonRequired] string Name,
    [property:JsonRequired] string Unit,[property:JsonRequired] double Minimum,[property:JsonRequired] double Maximum);
public sealed record BlendSpaceSample([property:JsonRequired] Guid Id,[property:JsonRequired] Guid ClipId,
    [property:JsonRequired] double X,[property:JsonRequired] double Y);
public sealed record BlendSpaceDefinition([property:JsonRequired] Guid Id,[property:JsonRequired] int Dimensions,
    [property:JsonRequired] BlendSpaceAxis AxisX,[property:JsonRequired] BlendSpaceAxis? AxisY,
    [property:JsonRequired] double CycleSeconds,[property:JsonRequired] Guid SyncGroup,
    [property:JsonRequired] BlendSpaceSample[] Samples);
public readonly record struct BlendSpaceTriangle(Guid A,Guid B,Guid C);
public readonly record struct BlendSpaceContribution(Guid SampleId,Guid ClipId,double Weight);
public readonly record struct BlendSpaceWeights(int Count,BlendSpaceContribution A,BlendSpaceContribution B,BlendSpaceContribution C,
    Guid PrimarySample,Guid PrimaryClip,double X,double Y,bool Projected);
