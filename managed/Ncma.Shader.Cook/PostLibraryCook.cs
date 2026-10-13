using Ncma.Assets;
using Ncma.Rendering;
using System.Security.Cryptography;

internal static class PostLibraryCook
{
    internal static void Write(RendererSession renderer,string destination,RuntimeShaderFileSet sceneFiles)
    {
        Directory.CreateDirectory(Path.Combine(destination,"assets/postprocess"));
        void FileOut(string path,byte[] bytes){using var stream=new FileStream(Path.Combine(destination,path),FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);}
        var bloom=BloomFeature.CreatePrograms();var programs=PostProcessPackage.Cook(renderer,bloom.Catalog,bloom.CopyPrograms(),()=>true);
        string programPath="assets/postprocess/bloom-v1.nph";FileOut(programPath,programs.CopyBytes());var tones=new List<PostToneFile>();
        foreach(var item in new[]{(SceneToneCurve.AcesFitted,PostProcessLibrarySelection.AcesModule),(SceneToneCurve.Reinhard,PostProcessLibrarySelection.ReinhardModule),(SceneToneCurve.LinearClamp,PostProcessLibrarySelection.LinearModule)})
        foreach(var profile in new[]{ShaderProfile.Scene3D,ShaderProfile.SceneEnvironment})foreach(bool skin in new[]{false,true})foreach(bool shadows in new[]{false,true}){
            var catalog=profile==ShaderProfile.Scene3D?DefaultSceneShaders.CopyCatalog(renderer,shadows):DefaultEnvironmentSceneShaders.CopyCatalog(renderer,shadows);
            var selected=profile==ShaderProfile.Scene3D?DefaultSceneShaders.Select(catalog,shadows):DefaultEnvironmentSceneShaders.Select(catalog,shadows);
            var c=SceneToneComposition.Compose(catalog,selected,SceneTonePresets.Create(item.Item1,profile));
            using var compiler=new ShaderCompilerService(renderer,()=>true);var inputs=new List<RuntimeShaderInput>();
            ShaderDescriptor[] stages=shadows?[c.Selection.GeometryVertex,c.Selection.GeometryPixel,c.Selection.ToneVertex,c.Selection.TonePixel,c.Selection.ShadowVertex!,c.Selection.ShadowPixel!]:[c.Selection.GeometryVertex,c.Selection.GeometryPixel,c.Selection.ToneVertex,c.Selection.TonePixel];
            for(int i=0;i<stages.Length;i++)inputs.Add(new(profile==ShaderProfile.SceneEnvironment&&i<2?(i==0?RuntimeShaderRole.EnvironmentGeometryVertex:RuntimeShaderRole.EnvironmentGeometryPixel):(RuntimeShaderRole)(i+2),compiler.Prepare(stages[i])));
            if(skin)inputs.Add(new(RuntimeShaderRole.SkinCompute,compiler.Prepare(DefaultSkinShader.Select(DefaultSkinShader.CopyCatalog(renderer)))));
            var package=RuntimeShaderPackage.Cook(profile,shadows,skin,inputs);string path=$"assets/postprocess/{item.Item1}-{profile}-{(shadows?1:0)}-{(skin?1:0)}.ncshader";FileOut(path,package.CopyBytes());tones.Add(new(item.Item2,path,package.ContentHash,profile.ToString(),shadows,skin));
        }
        var selection=new PostProcessLibrarySelection(1,[new(PostProcessLibrarySelection.BloomModule,programPath,programs.ContentHash)],tones.ToArray());byte[] index=selection.Encode();FileOut(PostProcessLibrarySelection.IndexPath,index);
        using var library=RuntimePostProcessLibrary.Load(destination,Convert.ToHexString(SHA256.HashData(index)));_=library.Prepare(renderer,sceneFiles,()=>true);
        if(renderer.PostStats.Bindings!=0||renderer.PostStats.ResidentBytes!=0||renderer.PipelineStats.Pipelines!=0||renderer.Stats.ValidationErrors!=0||renderer.Stats.ValidationWarnings!=0)throw new InvalidOperationException("Post library cook must not install scene/effect resources.");
        Console.WriteLine("Cooked post library: 24 Tone variants + Bloom NPH1; complete actual admission; GPU scene/post resources=0.");
    }
}
