using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Ncma.Assets;
using Ncma.Application;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Player.App;

internal static unsafe partial class Program
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string name,string existing,nint security);
    private static int ShaderFileTests(string nativeRoot,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(output,"assets/shaders"));
        int cases=0;
        void Pass(bool value,string name){Check(value,"C4-C "+name);cases++;}
        void Bad(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException or OperationCanceledException or PluginException){cases++;return;}throw new Exception("Expected C4-C rejection");}
        using var loader=new PluginLoader(); loader.Load(Path.GetFullPath(nativeRoot),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Shader file tests",256,256,false);
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        RuntimeShaderPackage ui,wrong,user; var all=new List<RuntimeShaderPackage>();
        using(var cook=new RendererSession(native,window,256,256)) {
            ui=cook.DefaultRuntimeShaders.Prepare(ShaderProfile.Flat2D,false,false,()=>true).Package; all.Add(ui);
            foreach(bool skin in new[]{false,true})foreach(bool shadow in new[]{false,true}) all.Add(cook.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,shadow,skin,()=>true).Package);
            using var compiler=new ShaderCompilerService(cook,()=>true);
            var selected=DefaultUiShaders.Select(DefaultUiShaders.CopyCatalog(cook));var d=selected.Pixel.CopyDefinition();
            string source=d.Source.Replace(":SV_TARGET",":SV_TARGET1",StringComparison.Ordinal);
            wrong=RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,[new(RuntimeShaderRole.UiVertex,compiler.Prepare(selected.Vertex)),
                new(RuntimeShaderRole.UiPixel,compiler.Prepare(ShaderDescriptor.Prepare(d with{AssetId=Guid.NewGuid(),Source=source,SourceHash=ShaderContractCodec.HashSource(source)})))]);
            source=d.Source.Replace("Image.Sample(Linear,v.uv)*v.c","Image.Sample(Linear,v.uv).bgra*v.c.bgra",StringComparison.Ordinal);
            Pass(source!=d.Source,"real user channel fixture");
            user=RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,[new(RuntimeShaderRole.UiVertex,compiler.Prepare(selected.Vertex)),
                new(RuntimeShaderRole.UiPixel,compiler.Prepare(ShaderDescriptor.Prepare(d with{AssetId=Guid.NewGuid(),Source=source,SourceHash=ShaderContractCodec.HashSource(source)})))]);
        }
        ShaderPackageFile Save(RuntimeShaderPackage p,string name){string relative="assets/shaders/"+name+".ncshader";File.WriteAllBytes(Path.Combine(output,relative),p.CopyBytes());return new(relative,p.ContentHash,p.Profile.ToString(),p.Shadows,p.Skinning);}
        var entries=all.Select((p,i)=>Save(p,"package"+i)).ToArray();var selection=new ShaderPackageSelection(1,entries);var uiSelection=new ShaderPackageSelection(1,[entries[0]]);
        string index=Path.Combine(output,RuntimeShaderFileSet.DefaultSelectionPath);File.WriteAllBytes(index,selection.Encode());
        Pass(ShaderPackageSelection.Decode(selection.Encode()).Packages.SequenceEqual(entries),"strict selection roundtrip");
        var copy=selection.CopyValidated();copy.Packages[0]=entries[0] with{Path="assets/changed.ncshader"};Pass(selection.Packages[0]==entries[0],"owned selection copy");
        foreach(string invalid in new[]{"{}","null","{\"schemaVersion\":2,\"packages\":[]}","{\"schemaVersion\":1,\"schemaVersion\":1,\"packages\":[]}","{\"schemaVersion\":1,\"packages\":[],\"unknown\":0}"})
            Bad(()=>ShaderPackageSelection.Decode(Encoding.UTF8.GetBytes(invalid)));
        string validJson=Encoding.UTF8.GetString(uiSelection.Encode());
        foreach(string field in new[]{"path","sha256","profile","shadows","skinning"}) {
            var node=System.Text.Json.Nodes.JsonNode.Parse(validJson)!;node["packages"]![0]!.AsObject().Remove(field);Bad(()=>ShaderPackageSelection.Decode(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        Bad(()=>new ShaderPackageSelection(1,[]).CopyValidated());Bad(()=>new ShaderPackageSelection(1,[entries[0],entries[0]]).CopyValidated());
        Bad(()=>new ShaderPackageSelection(1,[entries[1]]).CopyValidated());
        foreach(string path in new[]{"../x.ncshader","assets/../x.ncshader","assets\\x.ncshader","C:/x.ncshader","assets/CON.ncshader","assets/x.hlsl","assets/x.ncshader:stream"})
            Bad(()=>new ShaderPackageSelection(1,[entries[0] with{Path=path}]).CopyValidated());
        foreach(var entry in new[]{entries[0] with{Sha256=new string('0',64)},entries[0] with{Path="assets/shaders/Package0.ncshader"},entries[0] with{Path="assets/shaders/missing.ncshader"}})
            Bad(()=>RuntimeShaderFileSet.Load(output,new(1,[entry])).Dispose());
        Bad(()=>RuntimeShaderFileSet.Load(output,selection,new CancellationToken(true)).Dispose());
        using(var files=RuntimeShaderFileSet.Load(output)) {
            Exception? threadError=null;var worker=new Thread(()=>{try{_=files.Count;}catch(Exception e){threadError=e;}});worker.Start();worker.Join();Pass(threadError is InvalidOperationException,"file lease owner thread");
            Pass(files.Count==5 && files.Get(ShaderProfile.Flat2D,false,false).ContentHash==ui.ContentHash,"five exact pinned packages");
            Bad(()=>File.Open(Path.Combine(output,entries[0].Path),FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());
            Bad(()=>File.Open(index,FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());
            Bad(()=>Directory.Move(Path.Combine(output,"assets/shaders"),Path.Combine(output,"assets/moved")));
            using var renderer=new RendererSession(native,window,256,256);
            Bad(()=>files.InstallSelection(renderer,()=>false));
            files.InstallSelection(renderer,()=>true);
            foreach(var p in all)Pass(renderer.DefaultRuntimeShaders.Prepare(p.Profile,p.Shadows,p.Skinning,()=>true).Package.ContentHash==p.ContentHash,"file-backed official variant");
            Bad(()=>files.InstallSelection(renderer,()=>true));
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"full group admission API0/0");
        }
        using(File.Open(index,FileMode.Open,FileAccess.Write,FileShare.Read))Pass(true,"pins released after shutdown");
        string manifestPath=Path.Combine(output,"deployment-manifest.json");
        byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new{files=new[]{new{path=RuntimeShaderFileSet.DefaultSelectionPath,size=new FileInfo(index).Length,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(index)))}}});
        File.WriteAllBytes(manifestPath,manifest);
        using(var deployed=RuntimeShaderFileSet.ForHost(output,null,null)) {
            Pass(deployed!.Count==5,"pinned default index bound to independent manifest hash");
            Bad(()=>File.Open(manifestPath,FileMode.Open,FileAccess.Write,FileShare.ReadWrite).Dispose());
        }
        File.WriteAllBytes(index,uiSelection.Encode());Bad(()=>RuntimeShaderFileSet.ForHost(output,null,null)?.Dispose());
        File.WriteAllBytes(index,selection.Encode());
        File.WriteAllText(manifestPath,"{\"files\":[]}");Bad(()=>RuntimeShaderFileSet.ForHost(output,null,null)?.Dispose());File.WriteAllBytes(manifestPath,manifest);
        var userSelection=new ShaderPackageSelection(1,[Save(user,"user")]);
        using(var files=RuntimeShaderFileSet.Load(output,userSelection))using(var renderer=new RendererSession(native,window,256,256,pureUi:true)) {
            files.InstallSelection(renderer,()=>true);Bad(()=>renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,false,false,()=>true));
            renderer.PrepareDefaultUiShaders();using var image=renderer.CreateUiImage(1,1,[255,255,255,255]);
            var builder=new UiDisplayListBuilder(renderer);builder.Quad(new(Guid.NewGuid(),new(0,0,128,128),Matrix3x2.CreateTranslation(16,16),new(0,0,256,256),1,true,-1),image,new(.8f,.2f,.1f,1));
            using var list=builder.Build();using var target=renderer.CreateUiTarget(256,256);renderer.SubmitUiTarget(target,list,1,1,Vector4.Zero);var pixels=renderer.CaptureUiTarget(target);
            int pixel=(32*256+32)*4;
            Pass(pixels[pixel+2]>150 && pixels[pixel]<50 && renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0,"pinned user UI channel oracle and no3D");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"pure UI API0/0");
        }
        var badEntry=Save(wrong,"wrong-output");var wrongSelection=new ShaderPackageSelection(1,[badEntry]);
        using(var files=RuntimeShaderFileSet.Load(output,new(1,[entries[1],entries[2],entries[3],entries[4],badEntry])))using(var renderer=new RendererSession(native,window,256,256)) {
            Bad(()=>files.InstallSelection(renderer,()=>true));Pass(renderer.UiShaderGeneration==0,"wrong actual output publishes nothing");
            using var good=RuntimeShaderFileSet.Load(output,uiSelection);good.InstallSelection(renderer,()=>true);renderer.PrepareDefaultUiShaders();Pass(renderer.UiShaderGeneration==1,"failed admission does not poison selection");
        }
        string link=Path.Combine(output,"assets/shaders/hardlink.ncshader");Pass(CreateHardLinkW(link,Path.Combine(output,badEntry.Path),0),"hardlink fixture");
        Bad(()=>RuntimeShaderFileSet.Load(output,wrongSelection).Dispose());
        // Keep the rejected hardlink fixture; fresh bad-output bytes for Player rejection.
        badEntry=Save(wrong,"player-wrong-output");wrongSelection=new(1,[badEntry]);
        string corruptPath="assets/shaders/corrupt.ncshader";File.WriteAllBytes(Path.Combine(output,corruptPath),[1,2,3]);
        Bad(()=>RuntimeShaderFileSet.Load(output,new(1,[entries[0] with{Path=corruptPath}])).Dispose());
        using(File.Open(Path.Combine(output,corruptPath),FileMode.Open,FileAccess.Write,FileShare.Read))Pass(true,"failed preflight releases pin");
        using(var disposed=RuntimeShaderFileSet.Load(output,uiSelection))using(var renderer=new RendererSession(native,window,256,256,pureUi:true)) {
            Bad(()=>disposed.InstallSelection(renderer,()=>{disposed.Dispose();return true;}));
            Bad(()=>disposed.Get(ShaderProfile.Flat2D,false,false));
        }
        // Formal Player rejects CPU-invalid and actually invalid GPU bindings before any PlaySession.
        string gameplay=typeof(Ncma.Gameplay.PlaySession).Assembly.Location;File.Copy(gameplay,Path.Combine(output,"gameplay.dll"));
        SceneDocumentFiles.Save(new SceneDocument("Shader project"),Path.Combine(output,"start.ncmascene"));
        var config=new ProjectConfiguration(1,Guid.NewGuid(),"Shader project","start.ncmascene","gameplay.dll","Direct3D11",[],ShaderPackages:wrongSelection);
        string project=Path.Combine(output,"test.ncmaproject");var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        foreach(var invalid in new[]{wrongSelection,new ShaderPackageSelection(1,[entries[0] with{Sha256=new string('0',64)}]),new ShaderPackageSelection(1,[entries[0] with{Path=corruptPath}])}) {
            File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(config with{ShaderPackages=invalid},json));
            var result=PlayerRunner.Run(PlayerOptions.Parse(["--project",project,"--ticks","1","--report",Path.Combine(output,Guid.NewGuid()+".json")]),pluginRoot:nativeRoot,visible:false);
            Pass(result.ExitCode==3&&result.Tick==0&&result.SessionId==Guid.Empty,"bad shader Player pre-start rejection");
        }
        File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(config with{ShaderPackages=uiSelection},json));
        var context=ProjectContext.Load(project);var configuration=context.Configuration;configuration.ShaderPackages!.Packages[0]=badEntry;
        Pass(context.Configuration.ShaderPackages!.Packages[0]==entries[0],"project configuration owns array");
        var headless=PlayerRunner.Run(PlayerOptions.Parse(["--project",project,"--headless","--ticks","1","--report",Path.Combine(output,"headless.json")]),pluginRoot:Path.Combine(output,"no-native"),visible:false);
        Pass(headless.ExitCode==0&&headless.Tick==1&&headless.Modules.Length==0,"Headless needs no shader/GPU deployment");
        File.WriteAllText(Path.Combine(output,"shader-files.json"),JsonSerializer.Serialize(new{cases,pinned=true,playerPreStart=true,sourceFree=true,agentAuthority=false,manualAcceptance=false}));
        Console.WriteLine($"PASS C4-C shader files/project/whole admission/Player pre-start: {cases} cases; API0/0");return 0;
    }
}
