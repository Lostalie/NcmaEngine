using Ncma.Assets;
using Ncma.Rendering;
using System.Security.Cryptography;

internal static class ShadowLibraryCook
{
    internal static void Write(RendererSession renderer,string destination,RuntimeShaderFileSet sceneFiles)
    {
        Directory.CreateDirectory(Path.Combine(destination,"assets/shadows"));
        void FileOut(string path,byte[] bytes){using var stream=new FileStream(Path.Combine(destination,path),FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);}
        var files=new List<ShadowProgramFile>();
        foreach(var profile in new[]{ShaderProfile.Scene3D,ShaderProfile.SceneEnvironment})foreach(bool skin in new[]{false,true}){
            var catalog=profile==ShaderProfile.Scene3D?DefaultSceneShaders.CopyCatalog(renderer,true):DefaultEnvironmentSceneShaders.CopyCatalog(renderer,true);
            var selection=profile==ShaderProfile.Scene3D?DefaultSceneShaders.Select(catalog,true):DefaultEnvironmentSceneShaders.Select(catalog,true);
            var high=SceneShadowShaders.ComposeHigh(catalog,selection);
            using var compiler=new ShaderCompilerService(renderer,()=>true);var inputs=new List<RuntimeShaderInput>();
            ShaderDescriptor[] stages=[high.Selection.GeometryVertex,high.Selection.GeometryPixel,high.Selection.ToneVertex,high.Selection.TonePixel,high.Selection.ShadowVertex!,high.Selection.ShadowPixel!];
            for(int i=0;i<stages.Length;i++)inputs.Add(new(profile==ShaderProfile.SceneEnvironment&&i<2?(i==0?RuntimeShaderRole.EnvironmentGeometryVertex:RuntimeShaderRole.EnvironmentGeometryPixel):(RuntimeShaderRole)(i+2),compiler.Prepare(stages[i])));
            if(skin)inputs.Add(new(RuntimeShaderRole.SkinCompute,compiler.Prepare(DefaultSkinShader.Select(DefaultSkinShader.CopyCatalog(renderer)))));
            var package=RuntimeShaderPackage.Cook(profile,true,skin,inputs);string path=$"assets/shadows/High-{profile}-{(skin?1:0)}.ncshader";
            FileOut(path,package.CopyBytes());files.Add(new(profile.ToString(),skin,path,package.ContentHash));
        }
        var selectionIndex=new ShadowLibrarySelection(1,files.ToArray());byte[] index=selectionIndex.Encode();FileOut(ShadowLibrarySelection.IndexPath,index);
        using var library=RuntimeShadowLibrary.Load(destination,Convert.ToHexString(SHA256.HashData(index)));_=library.Prepare(renderer,sceneFiles,()=>true);
        if(renderer.PostStats.Bindings!=0||renderer.PostStats.ResidentBytes!=0||renderer.PipelineStats.Pipelines!=0||renderer.SkinStats.Meshes!=0||renderer.Stats.ValidationErrors!=0||renderer.Stats.ValidationWarnings!=0)throw new InvalidOperationException("Shadow cook must not install scene/effect/skin resources.");
        Console.WriteLine("Cooked shadow library: 4 High variants; source-free whole-group admission; scene/post/skin resources=0.");
    }
}
