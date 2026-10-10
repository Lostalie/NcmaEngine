namespace Ncma.Rendering.Scene;

// Independent profile3 pair, admitted before scene/environment resources. No World, IO or GPU
// ownership. Preparing this pair alone does not enable IBL in a formal application.
public sealed class SceneEnvironmentRuntimeShaders
{
    public EnvironmentShaderPreparation Unshadowed { get; }
    public EnvironmentShaderPreparation Shadowed { get; }
    public SceneEnvironmentRuntimeShaders(RendererSession renderer, EnvironmentShaderPreparation unshadowed, EnvironmentShaderPreparation shadowed)
    {
        ArgumentNullException.ThrowIfNull(unshadowed); ArgumentNullException.ThrowIfNull(shadowed);
        if (unshadowed.Package.Profile != ShaderProfile.SceneEnvironment || shadowed.Package.Profile != ShaderProfile.SceneEnvironment ||
            unshadowed.Package.Shadows || !shadowed.Package.Shadows || unshadowed.Package.Skinning != shadowed.Package.Skinning)
            throw new ArgumentException("Exact environment scene shader variants required.");
        if (unshadowed.Package.Skinning && !unshadowed.Package.CopyBytecode(RuntimeShaderRole.SkinCompute)
            .SequenceEqual(shadowed.Package.CopyBytecode(RuntimeShaderRole.SkinCompute)))
            throw new ArgumentException("Environment variants require the same shared skin program.");
        unshadowed.VerifyFor(renderer); shadowed.VerifyFor(renderer);
        Unshadowed = unshadowed; Shadowed = shadowed;
    }
    public void VerifyFor(RendererSession renderer) { Unshadowed.VerifyFor(renderer); Shadowed.VerifyFor(renderer); }
    public static SceneEnvironmentRuntimeShaders PrepareDefault(RendererSession renderer, bool skin, Func<bool> allowed) =>
        new(renderer,renderer.DefaultRuntimeShaders.PrepareEnvironment(false,skin,allowed),
            renderer.DefaultRuntimeShaders.PrepareEnvironment(true,skin,allowed));
}
