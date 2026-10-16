using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Rendering.Scene.Examples;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int PipelineHostTests(string plugins,string output,bool sourceOnly=false)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repository missing");
        string deployed=Path.Combine(repo.FullName,"out/bin");int checks=0;
        checks+=PipelineStartupBoundaryTests(plugins);
        void Pass(bool ok,string label){Check(ok,"D3-A "+label);checks++;}
        var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};var runs=new List<object>();
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        global::Ncma.Runtime.Entity light=document.World.GetEntities().Single(o=>document.World.Has<DirectionalLightData>(o));document.World.Set(light, document.World.Get<DirectionalLightData>(light) with{CastShadow=false});
        global::Ncma.Runtime.Entity post=document.World.CreateEntity("Post");document.World.Set(post, PostProcessData.Off with{Enabled=true,StackJson=PostProcessData.EncodeStack([new(ExampleHdrGain.ModuleId,1,[new("gain",.25f)]),PostProcessModules.Bloom(new(Levels:2,Threshold:0,Intensity:.3f))])});
        Guid envObject;
        using(var presets=EnvironmentPresetLibrary.LoadDeployment(deployed)){
            var env=presets.Package.CloneForAsset(Guid.NewGuid(),1);var descriptor=EnvironmentAssetDescriptor.FromPackage(fixture.Project,env);string path=Path.Combine(fixture.Root,descriptor.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,env.CopyBytes());File.WriteAllBytes(Path.Combine(fixture.Root,"assets/environment.ncenv"),descriptor.Encode());
            global::Ncma.Runtime.Entity obj=document.World.CreateEntity("Environment");document.World.Set(obj, new EnvironmentLightingData(1,env.AssetId,1,env.ContentHash,1,0,true));envObject=document.World.GetUuid(obj);
        }
        // Runtime-only copied project. Source files are kept but exclusively locked during every host run.
        string runtime=Path.Combine(output,"runtime");Directory.CreateDirectory(Path.Combine(runtime,"assets"));
        File.WriteAllBytes(Path.Combine(runtime,"assets/game.ncpak"),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,document.CaptureSnapshot(),[]));
        var shaders=ShaderPackageSelection.Decode(File.ReadAllBytes(Path.Combine(deployed,RuntimeShaderFileSet.DefaultSelectionPath)));
        var postFiles=PostProcessLibrarySelection.Decode(File.ReadAllBytes(Path.Combine(deployed,PostProcessLibrarySelection.IndexPath)));
        foreach(string path in shaders.Packages.Select(p=>p.Path).Concat(postFiles.Programs.Select(p=>p.Path)).Concat(postFiles.Tones.Select(p=>p.Path))){string dest=Path.Combine(runtime,path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(Path.Combine(deployed,path),dest);}
        var bloom=postFiles.Programs.Single(p=>p.ModuleId==PostProcessLibrarySelection.BloomModule);const string gain="assets/postprocess/example-gain.nph";
        File.Copy(Path.Combine(runtime,bloom.Path),Path.Combine(runtime,gain));byte[] index=(postFiles with{Programs=[..postFiles.Programs,new(ExampleHdrGain.ModuleId,gain,bloom.Sha256)]}).Encode();
        File.WriteAllBytes(Path.Combine(runtime,PostProcessLibrarySelection.IndexPath),index);
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(runtime,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(runtime,"gameplay.deps.json"));
        SceneDocumentFiles.Save(document,Path.Combine(runtime,"start.ncmascene"));
        var plain=document.CaptureSnapshot();plain=plain with{Objects=plain.Objects.Where(o=>o.Id!=document.World.GetUuid(post)&&o.Id!=envObject).ToArray()};File.WriteAllBytes(Path.Combine(runtime,"plain.ncmascene"),SceneDocumentCodec.Encode(plain));
        var config=new ProjectConfiguration(1,fixture.Project,"Pipeline product","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera,AssetPackage:"assets/game.ncpak",ShaderPackages:shaders,PostProcessLibrary:new(1,Convert.ToHexString(SHA256.HashData(index))));
        string project=Path.Combine(runtime,"project.ncmaproject"),plainProject=Path.Combine(runtime,"plain.ncmaproject");File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(config,json));File.WriteAllBytes(plainProject,JsonSerializer.SerializeToUtf8Bytes(config with{StartupScene="plain.ncmascene",PostProcessLibrary=null},json));
        Pass(!Directory.GetFiles(runtime,"*",SearchOption.AllDirectories).Any(p=>new[]{".fbx",".nca",".ncenv",".nce",".ncmeta",".hlsl",".cs"}.Contains(Path.GetExtension(p))),"runtime project has no author model/environment/shader/code sources");
        var locks=new List<FileStream>();
        try {
            foreach(string path in Directory.GetFiles(fixture.Root,"*",SearchOption.AllDirectories))locks.Add(new(path,FileMode.Open,FileAccess.Read,FileShare.None));Pass(locks.Count>0,"author files unavailable");
            void Probe(string productRoot,string native,string kind,string label,bool relocated,string input){
                string evidence=Path.Combine(output,label);Directory.CreateDirectory(evidence);string request=Path.Combine(evidence,"request.json");
                File.WriteAllText(request,JsonSerializer.Serialize(new{probe="pipeline",productRoot,plugins=native,project=input,output=evidence,kind,relocated},json));
                if(relocated){
                    var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=output};foreach(string arg in new[]{typeof(Program).Assembly.Location,"--environment-product-child",request})info.ArgumentList.Add(arg);
                    using var process=Process.Start(info)??throw new Exception("Child start failed");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(90000)){process.Kill(entireProcessTree:true);process.WaitForExit();throw new Exception("Owned product probe timeout");}
                    string log=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(evidence,"child.log"),log);Pass(process.ExitCode==0,label+" "+log);
                    using var loaded=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"loaded-product-assemblies.json")));Pass(loaded.RootElement.GetArrayLength()>=5,"strict checked assembly evidence");
                }else PipelineHostProbe.Run(request);
                using var result=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"probe.json")));int count=result.RootElement.GetProperty("checks").GetInt32();Pass(count>0,"host assertions");checks+=count;runs.Add(new{label,checks=count,relocated});
            }
            Probe(deployed,Path.GetFullPath(plugins),"player","source-player",false,project);
            Probe(deployed,Path.GetFullPath(plugins),"player","source-player-plain",false,plainProject);
            Probe(deployed,Path.GetFullPath(plugins),"editor","source-editor",false,project);
            Probe(deployed,Path.GetFullPath(plugins),"null","source-null",false,project);
            if(!sourceOnly){
                string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;using var products=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",configuration,"packages.json")));
                foreach(var (key,kind) in new[]{("editor","editor"),("playerDx11","player"),("playerNull","null")}){
                    string source=products.RootElement.GetProperty(key).GetString()!,dest=Path.Combine(output,"product-"+kind);DeploymentManifest.Validate(source);Directory.CreateDirectory(dest);
                    using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source,DeploymentManifest.FileName)));
                    foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateArray()){
                        string path=entry.GetProperty("path").GetString()!,from=Path.GetFullPath(Path.Combine(source,path)),to=Path.GetFullPath(Path.Combine(dest,path));
                        Pass(from.StartsWith(Path.GetFullPath(source)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&to.StartsWith(dest+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"checked copy path");Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(from,to);
                    }
                    File.Copy(Path.Combine(source,DeploymentManifest.FileName),Path.Combine(dest,DeploymentManifest.FileName));DeploymentManifest.Validate(dest);
                    Pass(Directory.GetFiles(dest,"*",SearchOption.AllDirectories).Length==manifest.RootElement.GetProperty("files").GetArrayLength()+1,"exact product files");
                    Probe(dest,Path.Combine(dest,"plugins"),kind,"product-probe-"+kind,true,project);
                    if(kind=="player")Probe(dest,Path.Combine(dest,"plugins"),kind,"product-probe-plain",true,plainProject);
                    if(kind=="null")Pass(!Directory.Exists(Path.Combine(dest,"plugins"))&&!Directory.Exists(Path.Combine(dest,"shaders")),"Null product no graphics resources");
                }
            }
        }finally{foreach(var pin in locks)pin.Dispose();}
        File.WriteAllText(Path.Combine(output,"pipeline-host-results.json"),JsonSerializer.Serialize(new{checks,sourceOnly,runs,wholeD3Accepted=false,manualAccepted=false},json));
        Console.WriteLine($"PASS M7.4-D3-A formal pipeline hosts: {checks} checks; {output}");return 0;
    }
}
