using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
using System.Numerics;

internal static unsafe partial class Program
{
    private static int EnvironmentPresetTests(string root, string output)
    {
        root=Path.GetFullPath(root);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int cases=0,maxPixelError=0;void Pass(bool value,string label){Check(value,"M7.3-C3-A "+label);cases++;}
        void Bad(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException or OperationCanceledException or KeyNotFoundException){cases++;return;}throw new Exception("Expected C3-A rejection.");}
        var repository=new DirectoryInfo(root);while(!File.Exists(Path.Combine(repository.FullName,"Build.bat")))repository=repository.Parent??throw new Exception("Repository missing");
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string cooker=Path.Combine(repository.FullName,"managed/Ncma.Shader.Cook/bin",configuration,"net8.0/Ncma.Shader.Cook.dll");
        string Cook(string mode){
            string destination=Path.Combine(output,mode);var start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{cooker,root,destination,mode})start.ArgumentList.Add(arg);
            using var process=System.Diagnostics.Process.Start(start)??throw new Exception("Cooker start");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(60000)){process.Kill(true);process.WaitForExit();throw new Exception("Cooker watchdog");}
            Pass(process.ExitCode==0,mode+" build-only cook: "+stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());return destination;
        }
        string editor=Cook("editor"),player=Cook("player"),ui=Cook("ui");string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        byte[] indexBytes=File.ReadAllBytes(Path.Combine(editor,EnvironmentPresetIndex.IndexPath)),packageBytes=File.ReadAllBytes(Path.Combine(editor,EnvironmentPresetIndex.PackagePath));
        var index=EnvironmentPresetIndex.Decode(indexBytes);
        Pass(packageBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(player,index.Path)))&&indexBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(player,EnvironmentPresetIndex.IndexPath))),"independent cooks deterministic");
        using(var files=RuntimeShaderFileSet.Load(editor))Pass(files.Count==9,"Editor9");
        using(var files=RuntimeShaderFileSet.Load(player))Pass(files.Count==9&&files.Get(ShaderProfile.Flat2D,false,false).Profile==ShaderProfile.Flat2D,"Player original8 plus checked HUD profile");
        using(var files=RuntimeShaderFileSet.Load(ui))Pass(files.Count==1&&Directory.GetFiles(ui,"*",SearchOption.AllDirectories).Length==2&&!Directory.Exists(Path.Combine(ui,"assets/environments")),"UI1+index only");
        var originals=new Dictionary<string,string>{
            // M7.7 intentionally fixes zero-radius square edge coverage. UiCostJoint's
            // independent integer-grid oracle verifies this delta; all 3D hashes stay frozen.
            ["Flat2D-0-0.ncshader"]="447012BF011FF89E2241A8ABA15ED73BF7C9F3C4E0C27A5037FCF9EB6DA82416",
            ["Scene3D-0-0.ncshader"]="45A8FCB09245305D60E26B782BA6417B3403CF933C2DD721E214460959526716",
            ["Scene3D-0-1.ncshader"]="418B1E852877637446ECDF0D823FC0C5E66893072D8A8BCCB4AEE90CCAE34058",
            ["Scene3D-1-0.ncshader"]="7EC30FF9BDE840058C0DE7468B9B6B0A2E90ECCD17A77EFD9C08B70BFA1B4B40",
            ["Scene3D-1-1.ncshader"]="86E0F92FFB7CB7F91F932266E00B0C38D685E4DAAA38DBC65635DF221A794313"};
        foreach(var old in originals)Pass(Hash(File.ReadAllBytes(Path.Combine(editor,"assets/shaders",old.Key)))==old.Value,"pinned shader "+old.Key);
        string Fixture(byte[]? metadata=null,byte[]? data=null){string path=Path.Combine(output,"cases",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(path,"assets/environments"));File.WriteAllBytes(Path.Combine(path,EnvironmentPresetIndex.IndexPath),metadata??indexBytes);File.WriteAllBytes(Path.Combine(path,index.Path),data??packageBytes);return path;}
        string Manifest(string path,Action<JsonObject>? change=null){
            var files=new[]{EnvironmentPresetIndex.IndexPath,index.Path}.Select(p=>{byte[] bytes=File.ReadAllBytes(Path.Combine(path,p));return new{path=p,size=bytes.Length,sha256=Hash(bytes)};}).ToArray();
            var value=JsonSerializer.SerializeToNode(new{schemaVersion=1,files})!.AsObject();change?.Invoke(value);string target=Path.Combine(path,"deployment-manifest.json");File.WriteAllText(target,value.ToJsonString());return target;
        }
        bool Writable(string path){try{using var file=File.Open(path,FileMode.Open,FileAccess.Write,FileShare.None);return true;}catch(IOException){return false;}}
        string good=Fixture(),manifest=Manifest(good);EnvironmentPackage copied;
        using(var library=EnvironmentPresetLibrary.LoadDeployment(good)){
            copied=library.Package;Pass(library.Index==index&&!copied.GpuValidated&&copied.Settings==EnvironmentCookSettings.Default,"immutable owned preset");
            foreach(string path in new[]{manifest,Path.Combine(good,EnvironmentPresetIndex.IndexPath),Path.Combine(good,index.Path)})Pass(!Writable(path),"pin");
            Bad(()=>Directory.Move(Path.Combine(good,"assets"),Path.Combine(good,"renamed-assets")));
            Exception? error=null;var thread=new Thread(()=>{try{_=library.Package;}catch(Exception e){error=e;}});thread.Start();thread.Join();Pass(error is InvalidOperationException,"owner thread");
        }
        Pass(Writable(manifest)&&Writable(Path.Combine(good,index.Path))&&copied.ContentHash==index.Sha256,"close releases pins/copied package remains");
        var disposed=EnvironmentPresetLibrary.Load(good,Hash(indexBytes));disposed.Dispose();Bad(()=>_=disposed.Package);
        for(int i=0;i<12;i++){using var lease=EnvironmentPresetLibrary.LoadDeployment(good);Pass(lease.Package.ContentHash==index.Sha256,"reopen");}
        Bad(()=>EnvironmentPresetLibrary.Load(good,new string('0',64)).Dispose());
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Bad(()=>EnvironmentPresetLibrary.LoadDeployment(good,cancelled.Token).Dispose());}
        foreach(var invalid in new[]{index with{Version=2},index with{Preset="hdr-import"},index with{Synthetic=false},index with{AssetId=Guid.NewGuid()},index with{Generation=2},index with{Path="assets/other.nce"},index with{ByteCount=0},index with{Sha256=new string('a',64)}})Bad(()=>invalid.Encode());
        foreach(string text in new[]{"{}","[]",Encoding.UTF8.GetString(indexBytes).Replace("{","{\"version\":1,",StringComparison.Ordinal),Encoding.UTF8.GetString(indexBytes).Replace("{","{\"unknown\":1,",StringComparison.Ordinal)})Bad(()=>EnvironmentPresetIndex.Decode(Encoding.UTF8.GetBytes(text)));
        Bad(()=>EnvironmentPresetIndex.Decode(new byte[EnvironmentPresetIndex.MaxBytes+1]));
        foreach(Action<JsonObject> change in new Action<JsonObject>[]{
            m=>m["schemaVersion"]=2,m=>m["files"]![0]!["sha256"]=new string('0',64),m=>m["files"]![1]!["size"]=1,m=>m["files"]![1]!["sha256"]=new string('0',64),
            m=>m["files"]!.AsArray().Add(m["files"]![0]!.DeepClone()),m=>m["files"]!.AsArray().RemoveAt(1)}){
            string bad=Fixture();Manifest(bad,change);Bad(()=>EnvironmentPresetLibrary.LoadDeployment(bad).Dispose());Pass(Writable(Path.Combine(bad,index.Path))&&Writable(Path.Combine(bad,EnvironmentPresetIndex.IndexPath)),"failed manifest releases pins");
        }
        byte[] corrupt=(byte[])packageBytes.Clone();corrupt[^1]^=1;string corruptRoot=Fixture(data:corrupt);Manifest(corruptRoot);Bad(()=>EnvironmentPresetLibrary.LoadDeployment(corruptRoot).Dispose());
        byte[] forged=(byte[])packageBytes.Clone();Guid.NewGuid().TryWriteBytes(forged.AsSpan(64,16));byte[] forgedIndex=(index with{Sha256=Hash(forged)}).Encode();string forgedRoot=Fixture(forgedIndex,forged);Manifest(forgedRoot);
        Pass(EnvironmentPackage.Decode(forged,Hash(forged)).SourceId!=EnvironmentPresetIndex.NeutralSourceId,"different source package structurally readable");Bad(()=>EnvironmentPresetLibrary.LoadDeployment(forgedRoot).Dispose());
        string missing=Fixture();File.Move(Path.Combine(missing,index.Path),Path.Combine(missing,"saved.nce"));Bad(()=>EnvironmentPresetLibrary.Load(missing,Hash(indexBytes)).Dispose());
        string linked=Fixture();Pass(CreateHardLinkW(Path.Combine(linked,"same-data.nce"),Path.Combine(linked,index.Path),0),"retained hardlink fixture");Bad(()=>EnvironmentPresetLibrary.Load(linked,Hash(indexBytes)).Dispose());
        string junctionRoot=Path.Combine(output,"junction");Directory.CreateDirectory(Path.Combine(junctionRoot,"assets"));
        var junction=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"/d","/c","mklink","/J",Path.GetFullPath(Path.Combine(junctionRoot,"assets/environments")),Path.GetFullPath(Path.Combine(good,"assets/environments"))})junction.ArgumentList.Add(arg);
        using(var process=System.Diagnostics.Process.Start(junction)??throw new Exception("Junction fixture")){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(10000))throw new Exception("Junction timeout");Pass(process.ExitCode==0,stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());}
        Bad(()=>EnvironmentPresetLibrary.Load(junctionRoot,Hash(indexBytes)).Dispose());
        using(var loader=new PluginLoader()){
            loader.Load(root,Specs().Take(2).ToArray());using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Deployed environment",256,256,false);
            using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,256,256);using var shaders=RuntimeShaderFileSet.Load(editor);shaders.InstallSelection(renderer,()=>true);
            Pass(renderer.EnvironmentStats.Live==0,"shader admission no environment textures");using var environment=renderer.CreateEnvironment(copied,()=>true);
            using var pipeline=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),256,256,environmentShaders:renderer.DefaultRuntimeShaders.PrepareEnvironment(false,false,()=>true).Scene);
            pipeline.ConfigureEnvironment(environment,new(copied.AssetId,copied.Generation,copied.ContentHash,1,0,true),()=>true);
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"actual default binding/API0/0");
            var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
            var light=document.World.GetObjects().Single(o=>o.Has<DirectionalLightData>());light.Set(light.Get<DirectionalLightData>() with{Intensity=0,CastShadow=false});
            document.World.CreateObject("Environment").Set(new EnvironmentLightingData(1,copied.AssetId,copied.Generation,copied.ContentHash,1,0,true));
            var descriptor=EnvironmentAssetDescriptor.FromPackage(fixture.Project,copied);string path=Path.Combine(fixture.Root,descriptor.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,copied.CopyBytes());File.WriteAllBytes(Path.Combine(fixture.Root,"assets/environment.ncenv"),descriptor.Encode());
            using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
            using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
            using var scene=new SceneRenderSession(renderer,cache,document.World);scene.Prepare(assets,document.CaptureSnapshot(),environmentPreparationAllowed:()=>true);
            scene.PrepareEnvironmentView(256,256,camera,ambient:0);Pass(scene.Submit(1,256,256,camera,ambient:0,target:target),"actual preset scene submit");
            byte[] pixels=new byte[256*256*4];renderer.CaptureTarget(target,pixels);renderer.Present();
            var oracle=new IblImageOracle(copied);
            foreach(int y in new[]{104,128,152})foreach(int x in new[]{104,128,152}){
                var expected=oracle.Evaluate(new((x+.5f)/256*4-2,2-(y+.5f)/256*4,-4),Vector3.UnitZ,new(.6f,.25f,.1f),0,.6f,1,1,0);
                int error=0;for(int c=0;c<3;c++)error=Math.Max(error,Math.Abs(pixels[(y*256+x)*4+c]-(int)MathF.Round(expected[c]*255)));
                maxPixelError=Math.Max(maxPixelError,error);Pass(error<=3,"independent deployed preset pixel error="+error);
            }
            File.WriteAllBytes(Path.Combine(output,"default-preset.png"),Png(256,256,pixels));
            Pass(maxPixelError<=3&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"default preset image/API0/0");
        }
        using(var staged=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName,"out/verification/m2-7",configuration,"packages.json")) )){
            foreach(string kind in new[]{"editor","playerDx11","playerNull"}){
                string directory=staged.RootElement.GetProperty(kind).GetString()!;Ncma.Application.DeploymentManifest.Validate(directory);
                if(kind=="playerNull")Pass(!Directory.Exists(Path.Combine(directory,"assets/environments"))&&!Directory.Exists(Path.Combine(directory,"assets/shaders")),"checked Null no3D files");
                else{using var library=EnvironmentPresetLibrary.LoadDeployment(directory);using var files=RuntimeShaderFileSet.Load(directory);Pass(library.Package.ContentHash==index.Sha256&&files.Count==9&&files.Get(ShaderProfile.Flat2D,false,false).Profile==ShaderProfile.Flat2D,"checked product manifest/environment plus HUD closure "+kind);}
            }
        }
        File.WriteAllText(Path.Combine(output,"preset-results.json"),JsonSerializer.Serialize(new{cases,maxPixelError,index.Sha256,index.ByteCount,synthetic=true,defaultSceneMutation=false}));
        Console.WriteLine($"PASS M7.3-C3-A environment default cook/deployment: {cases} cases; pixelError={maxPixelError}; API0/0; no automatic scene mutation");return 0;
    }
}
