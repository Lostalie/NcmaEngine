using Ncma.Assets;
using Ncma.Scene.Rendering;

namespace Ncma.Rendering.Scene;

// Authoring values only, not a second scene store or a per-frame override. Applying a preset
// is explicit and undoable. Output resolution, frame pacing, materials and simulation are separate.
public readonly record struct SceneQualitySelection(SceneShadowData Shadows, PostProcessData PostProcess);

public static class SceneQualityPresets
{
    public const string LowV1 = "ncma.quality.low.v1";

    public static SceneQualitySelection Create(string preset, PostProcessModules modules, PostProcessData current)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (preset != LowV1) throw new ArgumentException("Unknown exact scene quality preset.", nameof(preset));
        // Validate the input BEFORE dropping features; Low must not mask corrupt or unregistered data.
        current = modules.Validate(current);
        return new(new(1, SceneShadowQuality.Low), modules.Validate(current with { StackJson = "[]" }));
    }
}
