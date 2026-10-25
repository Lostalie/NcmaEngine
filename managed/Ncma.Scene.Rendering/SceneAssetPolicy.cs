namespace Ncma.Scene.Rendering;

// Trusted host startup choice, not inferred from a failed/missing renderer or from null assets.
// Both modes retain simulation consumers. More domains must be explicit additions, not bool bypasses.
public enum SceneAssetPolicy
{
    Simulation = 1,
    PresentationAndSimulation = 2
}

public static class SceneAssetPolicies
{
    public static bool RequiresPresentation(SceneAssetPolicy policy) => policy switch {
        SceneAssetPolicy.Simulation => false,
        SceneAssetPolicy.PresentationAndSimulation => true,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
}
