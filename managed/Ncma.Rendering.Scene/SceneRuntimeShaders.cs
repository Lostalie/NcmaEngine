namespace Ncma.Rendering.Scene;

// Both exact closed variants are prepared outside submit; light toggles and target resizing
// consume immutable admitted bytes, never HLSL. No ownership of World/Agent permissions.
public sealed class SceneRuntimeShaders
{
    public RuntimeShaderPreparation Unshadowed { get; }
    public RuntimeShaderPreparation Shadowed { get; }
    public SceneRuntimeShaders(RuntimeShaderPreparation unshadowed,RuntimeShaderPreparation shadowed) {
        ArgumentNullException.ThrowIfNull(unshadowed);ArgumentNullException.ThrowIfNull(shadowed);
        if(unshadowed.Package.Profile!=ShaderProfile.Scene3D||shadowed.Package.Profile!=ShaderProfile.Scene3D||
            unshadowed.Package.Shadows||!shadowed.Package.Shadows||unshadowed.Package.Skinning!=shadowed.Package.Skinning)
            throw new ArgumentException("Exact scene shader variants required.");
        Unshadowed=unshadowed;Shadowed=shadowed;
        if(unshadowed.Package.Skinning&&!unshadowed.Package.CopyBytecode(RuntimeShaderRole.SkinCompute).SequenceEqual(shadowed.Package.CopyBytecode(RuntimeShaderRole.SkinCompute)))
            throw new ArgumentException("Both variants must use the same shared skin program.");
    }
    public static SceneRuntimeShaders PrepareDefault(RendererSession renderer,bool skin,Func<bool> allowed)=>
        new(renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,false,skin,allowed),
            renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,true,skin,allowed));
}
