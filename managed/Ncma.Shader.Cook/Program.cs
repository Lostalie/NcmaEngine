using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

// Trusted build-time tool only; never published inside a Player or exposed through MCP.
try {
    if (args.Length != 3 || args[2] is not ("editor" or "player" or "ui")) throw new ArgumentException("plugins fresh-output-root editor|player|ui");
    string destination=Path.GetFullPath(args[1]);
    if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("Cook destination must be fresh.");
    for (var p=new DirectoryInfo(destination);p is not null;p=p.Parent)
        if(p.Exists && (p.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Cook rejects reparse ancestors.");
    using var loader=new PluginLoader();loader.Load(Path.GetFullPath(args[0]),[
        new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
        new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"])]);
    using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Shader package cook",64,64,false);
    using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64,pureUi:args[2]=="ui");
    var packages=new List<RuntimeShaderPackage>();
    // Formal Player HUDs need the same checked UI shader closure as Editor/standalone UI.
    packages.Add(renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Flat2D,false,false,()=>true).Package);
    if(args[2]!="ui")foreach(bool skin in new[]{false,true})foreach(bool shadows in new[]{false,true})
        packages.Add(renderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,shadows,skin,()=>true).Package);
    if(args[2]!="ui")foreach(bool skin in new[]{false,true})foreach(bool shadows in new[]{false,true})
        packages.Add(renderer.DefaultRuntimeShaders.PrepareEnvironment(shadows,skin,()=>true).Package);
    var files=new List<ShaderPackageFile>();
    Directory.CreateDirectory(Path.Combine(destination,"assets/shaders"));
    foreach(var package in packages) {
        string path=$"assets/shaders/{package.Profile}-{(package.Shadows?1:0)}-{(package.Skinning?1:0)}.ncshader";
        byte[] bytes=package.CopyBytes(); _=RuntimeShaderPackage.Preflight(bytes,package.ContentHash);
        using(var stream=new FileStream(Path.Combine(destination,path),FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}
        files.Add(new(path,package.ContentHash,package.Profile.ToString(),package.Shadows,package.Skinning));
    }
    var selection=new ShaderPackageSelection(args[2]=="ui"?1:2,files.ToArray());
    using(var stream=new FileStream(Path.Combine(destination,RuntimeShaderFileSet.DefaultSelectionPath),FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(selection.Encode());stream.Flush(true);}
    using var verified=RuntimeShaderFileSet.Load(destination);
    if(verified.Count!=packages.Count || renderer.Stats.ValidationErrors!=0 || renderer.Stats.ValidationWarnings!=0)throw new InvalidOperationException("Cook verification failed.");
    if(args[2]!="ui") {
        PostLibraryCook.Write(renderer,destination,verified);
        using var cook=new EnvironmentCookService(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),()=>true);
        var environment=cook.Cook(EnvironmentPresetIndex.NeutralAssetId,1,HdrEnvironmentSource.Neutral(EnvironmentPresetIndex.NeutralSourceId),EnvironmentCookSettings.Default);
        var index=EnvironmentPresetIndex.FromPackage(environment);byte[] indexBytes=index.Encode();
        Directory.CreateDirectory(Path.Combine(destination,"assets/environments"));
        void Write(string path,byte[] bytes){using var stream=new FileStream(Path.Combine(destination,path),FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);}
        Write(index.Path,environment.CopyBytes());Write(EnvironmentPresetIndex.IndexPath,indexBytes);
        using var presets=EnvironmentPresetLibrary.Load(destination,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(indexBytes)));
        if(presets.Package.ContentHash!=environment.ContentHash||renderer.EnvironmentStats.Live!=0)throw new InvalidOperationException("CPU preset verification failed.");
        Console.WriteLine($"Cooked synthetic neutral preset {environment.ContentHash}; no environment GPU resources.");
    }
    Console.WriteLine($"Cooked {verified.Count} source-free shader packages ({args[2]}); API0/0.");
} catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
