using Ncma.Assets;
using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

// One scene-wide configuration on an ordinary flat object. No Transform, file path or GPU token.
public readonly record struct EnvironmentLightingData(int Version, Guid AssetId, ulong Generation,
    string ContentHash, float Strength, float RotationRadians, bool Enabled) : IComponent
{
    public const string TypeId = "ncma.render.environment";
    public static EnvironmentLightingData Off => new(1, Guid.Empty, 0, "", 0, 0, false);
    public EnvironmentLightingConfiguration ToConfiguration() => new(AssetId, Generation, ContentHash, Strength, RotationRadians, Enabled);
    public static EnvironmentLightingData Validate(EnvironmentLightingData value)
    {
        if (value.Version != 1 || value.Generation > long.MaxValue) throw new ArgumentException("Environment component version/generation.");
        value.ToConfiguration().Validate(); return value;
    }
}
