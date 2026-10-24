using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int EnvironmentProductTests(string root,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;void Pass(bool ok,string label){Check(ok,"C3-C "+label);checks++;}
        var repo=new DirectoryInfo(Path.GetFullPath(root));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repository not found");
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        using var index=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",configuration,"packages.json")));
        var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
        string CopyProduct(string source,string name){
            DeploymentManifest.Validate(source);string dest=Path.Combine(output,name);Directory.CreateDirectory(dest);
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source,"deployment-manifest.json")));
            foreach(var item in manifest.RootElement.GetProperty("files").EnumerateArray()){
                string relative=item.GetProperty("path").GetString()!;string from=Path.GetFullPath(Path.Combine(source,relative)),to=Path.GetFullPath(Path.Combine(dest,relative));
                Pass(from.StartsWith(Path.GetFullPath(source)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&to.StartsWith(dest+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"copy path within exact product");
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(from,to);
            }
            File.Copy(Path.Combine(source,"deployment-manifest.json"),Path.Combine(dest,"deployment-manifest.json"));DeploymentManifest.Validate(dest);
            Pass(Directory.GetFiles(dest,"*",SearchOption.AllDirectories).Length==manifest.RootElement.GetProperty("files").GetArrayLength()+1,"exact initial checked copy");return dest;
        }
        string editor=CopyProduct(index.RootElement.GetProperty("editor").GetString()!,"editor"),player=CopyProduct(index.RootElement.GetProperty("playerDx11").GetString()!,"player"),headless=CopyProduct(index.RootElement.GetProperty("playerNull").GetString()!,"null");
        EnvironmentPackage environment;using(var library=EnvironmentPresetLibrary.LoadDeployment(editor))environment=library.Package.CloneForAsset(Guid.NewGuid(),1);
        string oracle=Path.Combine(output,"oracle.nce");File.WriteAllBytes(oracle,environment.CopyBytes());
        void Publish(string directory,Guid project,EnvironmentPackage package){var d=EnvironmentAssetDescriptor.FromPackage(project,package);string path=Path.Combine(directory,d.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,package.CopyBytes());File.WriteAllBytes(Path.Combine(directory,"assets/environment.ncenv"),d.Encode());}
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");
        var runs=new List<object>();
        int ProcessRun(string exe,IEnumerable<string> args,string label,int? expected=0){
            var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=output};foreach(var arg in args)info.ArgumentList.Add(arg);
            using var process=Process.Start(info)??throw new Exception("Cannot start "+label);var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(60000)){process.Kill(entireProcessTree:true);process.WaitForExit();throw new Exception("Product timeout "+label);}
            string text=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(output,label+".log"),text);runs.Add(new{label,exit=process.ExitCode});
            if(expected is not null)Pass(process.ExitCode==expected,label+" exit="+process.ExitCode+": "+text);return process.ExitCode;
        }
        void Actual(string product,string project,string label,bool isEditor=false,bool isNull=false,int expected=0){
            var args=new List<string>{"--project",project};if(isEditor)args.AddRange(["--smoke-test","--frames","3"]);else{args.AddRange(["--ticks","8","--report",Path.Combine(output,label+"-report.json")]);if(isNull)args.Add("--headless");}
            ProcessRun(Path.Combine(product,isEditor?"NcmaEngine.exe":"NcmaPlayer.exe"),args,label,expected);
            if(expected==0&&!isEditor){using var report=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,label+"-report.json")));var v=report.RootElement;Pass(v.GetProperty("exitCode").GetInt32()==0&&(isNull?v.GetProperty("tick").GetUInt64()==8:v.GetProperty("tick").GetUInt64()>=8)&&v.GetProperty("validationErrors").GetUInt64()==0&&v.GetProperty("validationWarnings").GetUInt64()==0&&v.GetProperty("shutdownErrors").GetArrayLength()==0,"actual apphost fixed ticks/API/close "+label);}
        }
        void Probe(string product,string project,string label,Guid env,bool skin=false,bool dynamic=false,string? replacement=null,string? replacementOracle=null,bool isNull=false){
            string evidence=Path.Combine(output,label);Directory.CreateDirectory(evidence);string request=Path.Combine(evidence,"request.json");
            File.WriteAllText(request,JsonSerializer.Serialize(new{productRoot=product,project,output=evidence,kind=isNull?"null":product==editor?"editor":"player",skin,dynamic,environment=env,oracle,replacementDescriptor=replacement,replacementOracle},json));
            ProcessRun("dotnet",[typeof(Program).Assembly.Location,"--environment-product-child",request],label);
            using var result=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"probe.json")));Pass(result.RootElement.GetProperty("Checks").GetInt32()>0,"probe checks "+label);
            using var loaded=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence,"loaded-product-assemblies.json")));Pass(loaded.RootElement.GetArrayLength()>=5,"strict product assembly evidence "+label);
        }
        (string Path,Guid Env) Project(string source,Guid id,SceneDocument document,Guid camera,string label,bool packed=true){
            Guid env=document.CreateObject("Environment");document.SetComponent(env, new EnvironmentLightingData(1,environment.AssetId,1,environment.ContentHash,1,0,true));Publish(source,id,environment);
            string destination=packed?Path.Combine(output,label):source;Directory.CreateDirectory(Path.Combine(destination,"assets"));
            if(packed)File.WriteAllBytes(Path.Combine(destination,"assets/game.ncpak"),SceneAssetPreparation.CreateRuntimePackage(source,id,document.CaptureSnapshot(),[]));
            File.Copy(gameplay,Path.Combine(destination,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(destination,"gameplay.deps.json"));SceneDocumentFiles.Save(document,Path.Combine(destination,"start.ncmascene"));
            string path=Path.Combine(destination,"project.ncmaproject");File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,id,label,"start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera,AssetPackage:packed?"assets/game.ncpak":null),json));
            if(packed)Pass(!Directory.EnumerateFiles(destination,"*",SearchOption.AllDirectories).Any(p=>new[]{".fbx",".nca",".ncmeta",".ncenv",".nce",".ncshader"}.Contains(Path.GetExtension(p))),"source-free runtime project "+label);return(path,env);
        }
        void Locked(string source,Action action){
            var locks=new List<FileStream>();try{foreach(string p in Directory.GetFiles(source,"*",SearchOption.AllDirectories))locks.Add(new(p,FileMode.Open,FileAccess.Read,FileShare.None));Pass(locks.Count>0,"author sources exclusively locked");action();}finally{foreach(var pin in locks)pin.Dispose();}
        }
        var fixture=new SceneGpuFixture(output);var(doc,camera,_,_)=fixture.Scene();Guid light=doc.GetObjectIds().Single(o=>doc.HasComponent<DirectionalLightData>(o));doc.SetComponent(light, doc.GetComponent<DirectionalLightData>(light) with{Intensity=0,CastShadow=false});doc.SetComponent(doc.CreateObject("Rendering"), RenderConfiguration.Default(Guid.NewGuid()) with{Ambient=0});
        var project=Project(fixture.Root,fixture.Project,doc,camera,"runtime-static");
        Locked(fixture.Root,()=>{Actual(editor,project.Path,"editor-static-apphost",true);Actual(player,project.Path,"player-static-apphost");Probe(editor,project.Path,"editor-static",project.Env);Probe(player,project.Path,"player-static",project.Env);Actual(headless,project.Path,"null-apphost",isNull:true);Probe(headless,project.Path,"null-probe",project.Env,isNull:true);});
        Pass(!Directory.Exists(Path.Combine(headless,"shaders"))&&!Directory.Exists(Path.Combine(headless,"environments"))&&!Directory.Exists(Path.Combine(headless,"plugins")),"Null deployment no native/3D resources");
        string build=Directory.GetParent(root)!.Parent!.FullName,importPath=Path.Combine(build,"NcmaImportKernel.dll");
        foreach(string file in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}){
            using var importer=new Ncma.Asset.Import.ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))));string fbx=Path.Combine(repo.FullName,"tests/assets/fbx",file);var skin=new SkinFixture(output,importer.LoadAndCopy(fbx,30));var(sd,sc,objects)=skin.Scene();
            var blocks=DerivedAssetCodec.Decode(skin.Plan.DerivedBytes);var rig=ModelPayloadCodec.DecodeSkeleton(blocks.Single(b=>b.AssetId==skin.Manifest.Skeleton!.Value).Data);var mesh=ModelPayloadCodec.DecodeMesh(blocks.Single(b=>b.AssetId==skin.Manifest.Meshes[0].Mesh).Data);var bounds=BindPoseMeshUploadData.Prepare(mesh,rig).Bounds;float scale=2.5f/Math.Max(bounds.Max.X-bounds.Min.X,Math.Max(bounds.Max.Y-bounds.Min.Y,bounds.Max.Z-bounds.Min.Z));sd.SetComponent(objects[0], Ncma.Runtime.TransformData.Identity with{Position=-(bounds.Min+bounds.Max)*(.5f*scale)+new Vector3(0,0,-3.5f),Scale=new(scale)});
            string label=file.Contains("ascii")?"ascii":"binary";
            var clips=skin.Manifest.Clips.Select(id=>(Id:id,Clip:ModelPayloadCodec.DecodeClip(blocks.Single(b=>b.AssetId==id).Data).Clip)).ToArray();
            File.WriteAllText(Path.Combine(output,label+"-clips.json"),JsonSerializer.Serialize(clips.Select(c=>new{c.Id,c.Clip.Name,c.Clip.Duration,changed=c.Clip.Tracks.Sum(t=>t.Keys.Count(k=>k.Value!=t.Keys[0].Value))}),json));
            var animated=clips.OrderByDescending(c=>c.Clip.Tracks.Sum(t=>t.Keys.Count(k=>k.Value!=t.Keys[0].Value))).First();Guid character=objects[0];sd.SetComponent(character, sd.GetComponent<Ncma.Animation.ClipPlaybackData>(character) with{ClipId=animated.Id});
            var sp=Project(skin.Root,skin.Project,sd,sc,"runtime-"+label);
            Locked(skin.Root,()=>{using var fbxLock=new FileStream(fbx,FileMode.Open,FileAccess.Read,FileShare.None);Actual(editor,sp.Path,label+"-editor-apphost",true);Actual(player,sp.Path,label+"-player-apphost");Probe(editor,sp.Path,label+"-editor",sp.Env,true);Probe(player,sp.Path,label+"-player",sp.Env,true);});
        }
        var dynamicFixture=new SceneGpuFixture(output);var(dd,dc,_,_)=dynamicFixture.Scene();Guid dl=dd.GetObjectIds().Single(o=>dd.HasComponent<DirectionalLightData>(o));dd.SetComponent(dl, dd.GetComponent<DirectionalLightData>(dl) with{Intensity=0,CastShadow=false});dd.SetComponent(dd.CreateObject("Rendering"), RenderConfiguration.Default(Guid.NewGuid()) with{Ambient=0});var dp=Project(dynamicFixture.Root,dynamicFixture.Project,dd,dc,"dynamic-source",false);
        EnvironmentPackage next;using(var loader=new PluginLoader()){loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());using var cook=new EnvironmentCookService(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),()=>true);float[] pixels=new float[128*64*4];for(int y=0;y<64;y++)for(int x=0;x<128;x++){float z=MathF.Sin((y+.5f)/64*MathF.PI)*MathF.Cos((x+.5f)/128*2*MathF.PI);int i=(y*128+x)*4;pixels[i]=1.1f+.9f*z;pixels[i+1]=.8f+.6f*z;pixels[i+2]=.5f+.3f*z;pixels[i+3]=1;}next=cook.Cook(environment.AssetId,2,HdrEnvironmentSource.Prepare(Guid.NewGuid(),128,64,pixels),new(16,8,32,512));}
        var replacement=EnvironmentAssetDescriptor.FromPackage(dynamicFixture.Project,next);string nce=Path.Combine(dynamicFixture.Root,replacement.Path);Directory.CreateDirectory(Path.GetDirectoryName(nce)!);File.WriteAllBytes(nce,next.CopyBytes());string descriptor=Path.Combine(output,"replacement.ncenv");File.WriteAllBytes(descriptor,replacement.Encode());string oracle2=Path.Combine(output,"oracle2.nce");File.WriteAllBytes(oracle2,next.CopyBytes());Probe(editor,dp.Path,"editor-generation",dp.Env,dynamic:true,replacement:descriptor,replacementOracle:oracle2);
        // Exact generated test copies only; retain all corrupt fixtures/logs for diagnosis.
        string bad=CopyProduct(player,"bad-default");string badNce=Directory.GetFiles(bad,"*.nce",SearchOption.AllDirectories).Single();byte[] corrupted=File.ReadAllBytes(badNce);corrupted[^1]^=1;File.WriteAllBytes(badNce,corrupted);Actual(bad,project.Path,"bad-default-apphost",expected:3);
        Pass(!File.Exists(Path.Combine(output,"bad-default-apphost-report.json")),"default NCE manifest rejected before runner");
        string badShader=CopyProduct(editor,"bad-shader");string shader=Directory.GetFiles(badShader,"*.ncshader",SearchOption.AllDirectories).First();corrupted=File.ReadAllBytes(shader);corrupted[^1]^=1;File.WriteAllBytes(shader,corrupted);Actual(badShader,project.Path,"bad-shader-apphost",isEditor:true,expected:2);
        string ncp=Path.Combine(Path.GetDirectoryName(project.Path)!,"assets/game.ncpak");byte[] good=File.ReadAllBytes(ncp);corrupted=good.ToArray();corrupted[^1]^=1;File.WriteAllBytes(ncp,corrupted);Actual(player,project.Path,"bad-ncp-apphost",expected:3);File.WriteAllBytes(ncp,good);
        using(var rejected=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"bad-ncp-apphost-report.json")))){var v=rejected.RootElement;Pass(v.GetProperty("tick").GetUInt64()==0&&v.GetProperty("sessionId").GetGuid()==Guid.Empty&&v.GetProperty("renderedFrames").GetUInt64()==0,"bad NCP before gameplay");}
        Guid envObject=project.Env;var original=doc.GetComponent<EnvironmentLightingData>(envObject);int badIdentity=0;
        foreach(var value in new[]{original with{AssetId=Guid.NewGuid()},original with{Generation=999},original with{ContentHash=new string('A',64)}}){
            doc.SetComponent(envObject, value);SceneDocumentFiles.Save(doc,Path.Combine(Path.GetDirectoryName(project.Path)!,"start.ncmascene"));string label="bad-identity-"+(++badIdentity);Actual(player,project.Path,label,expected:3);
            using var report=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,label+"-report.json")));var v=report.RootElement;Pass(v.GetProperty("tick").GetUInt64()==0&&v.GetProperty("sessionId").GetGuid()==Guid.Empty&&v.GetProperty("renderedFrames").GetUInt64()==0&&v.GetProperty("shutdownErrors").GetArrayLength()==0,"identity rejected before gameplay "+badIdentity);
        }
        doc.SetComponent(envObject, original);SceneDocumentFiles.Save(doc,Path.Combine(Path.GetDirectoryName(project.Path)!,"start.ncmascene"));Actual(player,project.Path,"healthy-after-negative");
        DeploymentManifest.Validate(editor);DeploymentManifest.Validate(player);DeploymentManifest.Validate(headless);
        File.WriteAllText(Path.Combine(output,"product-results.json"),JsonSerializer.Serialize(new{checks,configuration,runs,manualAccepted=false,fullPerformanceAccepted=false},json));Console.WriteLine($"PASS M7.3-C3-C relocated checked products: {checks} checks; reports={output}");return 0;
    }
}
