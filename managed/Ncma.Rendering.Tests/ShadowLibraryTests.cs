using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int ShadowLibraryChild(string root,string hash)
    {
        using var library=RuntimeShadowLibrary.Load(root,hash);
        Check(library.CopySelection().Programs.Length==4,"High CPU closure");
        string[] names=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(m=>m.ModuleName).ToArray();
        Check(!names.Any(n=>new[]{"NcmaRenderer.dll","NcmaPlatform.dll","NcmaNative.dll","NcmaGui.dll","NcmaPhysics.dll","d3dcompiler_47.dll"}.Contains(n,StringComparer.OrdinalIgnoreCase)),"High CPU native/compiler=0");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("Ncma.Editor",StringComparison.Ordinal)),"High CPU Editor=0");
        Console.WriteLine("PASS High library CPU preflight; native/compiler/Editor=0");return 0;
    }
    private static int ShadowLibraryTests(string root,string output)
    {
        root=Path.GetFullPath(root);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0,maxError=0;
        void Pass(bool condition,string label){Check(condition,"M7.5 shadow library: "+label);cases++;}
        void Bad(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException or OperationCanceledException or PluginException){cases++;return;}throw new Exception("Expected High library rejection.");}
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        var repo=new DirectoryInfo(root);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");string config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        void Run(string label,params string[] arguments){var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in arguments)start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(60000)){process.Kill(true);process.WaitForExit();throw new Exception("High library child deadline.");}string text=stdout.Result+stderr.Result;File.WriteAllText(Path.Combine(output,label+".log"),text);Pass(process.ExitCode==0,label+": "+text);}
        string Cook(string mode){string folder=Path.Combine(output,mode);Run("cook-"+mode,Path.Combine(repo.FullName,"managed/Ncma.Shader.Cook/bin",config,"net8.0/Ncma.Shader.Cook.dll"),root,folder,mode);return folder;}
        string editor=Cook("editor"),player=Cook("player"),ui=Cook("ui");byte[] indexBytes=File.ReadAllBytes(Path.Combine(editor,ShadowLibrarySelection.IndexPath));string hash=Hash(indexBytes);var index=ShadowLibrarySelection.Decode(indexBytes);
        string[] entries=new[]{ShadowLibrarySelection.IndexPath}.Concat(index.Programs.Select(p=>p.Path)).ToArray();
        Pass(Directory.GetFiles(Path.Combine(editor,"assets/shadows")).Length==5&&index.Programs.Length==4,"exact four variants and index");
        foreach(string path in entries)Pass(File.ReadAllBytes(Path.Combine(editor,path)).SequenceEqual(File.ReadAllBytes(Path.Combine(player,path))),"independent deterministic cook "+path);
        Pass(!Directory.Exists(Path.Combine(ui,"assets/shadows"))&&Directory.GetFiles(ui,"*",SearchOption.AllDirectories).Length==2,"pureUI no 3D shadow closure");
        Run("cpu-child",typeof(Program).Assembly.Location,"--shadow-library-preflight",editor,hash);
        string Fixture(ShadowLibrarySelection? selection=null){string folder=Path.Combine(output,"cases",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(folder,"assets/shadows"));foreach(string p in entries)File.Copy(Path.Combine(editor,p),Path.Combine(folder,p));if(selection is not null)File.WriteAllBytes(Path.Combine(folder,ShadowLibrarySelection.IndexPath),selection.Encode());return folder;}
        void Manifest(string folder,Action<JsonObject>? change=null){var rows=entries.Select(p=>{byte[] bytes=File.ReadAllBytes(Path.Combine(folder,p));return new{path=p,size=bytes.Length,sha256=Hash(bytes)};});var json=JsonSerializer.SerializeToNode(new{schemaVersion=1,files=rows})!.AsObject();change?.Invoke(json);File.WriteAllText(Path.Combine(folder,"deployment-manifest.json"),json.ToJsonString());}
        bool Writable(string path){try{using var stream=File.Open(path,FileMode.Open,FileAccess.Write,FileShare.None);return true;}catch(IOException){return false;}}
        string good=Fixture();Manifest(good);RuntimeShaderPackage copied;
        using(var library=RuntimeShadowLibrary.LoadDeployment(good)){
            copied=library.Get(ShaderProfile.Scene3D,false);var copy=library.CopySelection();copy.Programs[0]=copy.Programs[0] with{Sha256=new string('0',64)};
            Pass(library.CopySelection().Programs[0].Sha256==index.Programs[0].Sha256&&!copied.GpuValidated,"owned metadata not GPU permission");
            foreach(string path in entries.Append("deployment-manifest.json"))Pass(!Writable(Path.Combine(good,path)),"pinned "+path);
            Bad(()=>Directory.Move(Path.Combine(good,"assets"),Path.Combine(good,"renamed-assets")));
            Exception? failure=null;var thread=new Thread(()=>{try{library.Dispose();}catch(Exception e){failure=e;}});thread.Start();thread.Join();Pass(failure is InvalidOperationException,"wrong-thread cannot close pins");
        }
        Pass(Writable(Path.Combine(good,index.Programs[0].Path))&&copied.CopyBytes().Length>0,"close releases files, immutable copied bytes survive");
        Bad(()=>RuntimeShadowLibrary.Load(good,new string('0',64)).Dispose());using(var cancel=new CancellationTokenSource()){cancel.Cancel();Bad(()=>RuntimeShadowLibrary.LoadDeployment(good,cancel.Token).Dispose());}
        foreach(var broken in new[]{index with{SchemaVersion=2},index with{Programs=[]},index with{Programs=index.Programs[..3]},index with{Programs=Enumerable.Repeat(index.Programs[0],4).ToArray()},index with{Programs=index.Programs.Select(p=>p with{Profile="Unknown"}).ToArray()},index with{Programs=index.Programs.Select(p=>p with{Path="../escape.ncshader"}).ToArray()},index with{Programs=index.Programs.Select(p=>p with{Sha256="bad"}).ToArray()}})Bad(()=>broken.Encode());
        foreach(string text in new[]{"{}","[]",Encoding.UTF8.GetString(indexBytes).Replace("\"schemaVersion\": 1","\"schemaVersion\": 1, \"schemaVersion\": 1",StringComparison.Ordinal),Encoding.UTF8.GetString(indexBytes).Replace("\"schemaVersion\": 1","\"schemaVersion\": 1, \"unknown\": true",StringComparison.Ordinal),Encoding.UTF8.GetString(indexBytes).Replace("\"skinning\": false,","",StringComparison.Ordinal)})Bad(()=>ShadowLibrarySelection.Decode(Encoding.UTF8.GetBytes(text)));
        string corrupt=Fixture();byte[] damaged=File.ReadAllBytes(Path.Combine(corrupt,index.Programs[0].Path));damaged[^1]^=1;File.WriteAllBytes(Path.Combine(corrupt,index.Programs[0].Path),damaged);Bad(()=>RuntimeShadowLibrary.Load(corrupt,hash).Dispose());Pass(Writable(Path.Combine(corrupt,index.Programs[0].Path)),"failed load releases pins");
        foreach(string field in new[]{"size","sha256","duplicate","case"}){string folder=Fixture();Manifest(folder,json=>{var rows=json["files"]!.AsArray();if(field=="duplicate")rows.Add(rows[0]!.DeepClone());else if(field=="case")rows[0]!["path"]=ShadowLibrarySelection.IndexPath.ToUpperInvariant();else if(field=="size")rows[1]![field]=1;else rows[1]![field]=new string('0',64);});Bad(()=>RuntimeShadowLibrary.LoadDeployment(folder).Dispose());}
        string linked=Fixture();Pass(CreateHardLinkW(Path.Combine(linked,"retained-alias.ncshader"),Path.Combine(linked,index.Programs[0].Path),0),"retained hardlink fixture");Bad(()=>RuntimeShadowLibrary.Load(linked,hash).Dispose());
        string missing=Fixture();File.Move(Path.Combine(missing,index.Programs[^1].Path),Path.Combine(missing,"retained-missing.ncshader"));Bad(()=>RuntimeShadowLibrary.Load(missing,hash).Dispose());Pass(Writable(Path.Combine(missing,index.Programs[0].Path)),"late missing file releases early pins");
        var swapped=index with{Programs=index.Programs.Select(p=>p with{Profile=p.Profile=="Scene3D"?"SceneEnvironment":"Scene3D"}).ToArray()};string wrong=Fixture(swapped);Bad(()=>RuntimeShadowLibrary.Load(wrong,Hash(swapped.Encode())).Dispose());
        using var loader=new PluginLoader();loader.Load(root,Specs().Take(2).ToArray());using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"High source-free library",64,64,false);
        using(var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64))using(var files=RuntimeShaderFileSet.Load(editor))using(var library=RuntimeShadowLibrary.Load(editor,hash))
        using(var post=RuntimePostProcessLibrary.Load(editor,Hash(File.ReadAllBytes(Path.Combine(editor,PostProcessLibrarySelection.IndexPath))))){
            bool allowed=true;Action? onApproval=null;var prepared=library.Prepare(renderer,files,()=>{onApproval?.Invoke();return allowed;});var preparedPost=post.Prepare(renderer,files,()=>allowed);
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.PostStats.ResidentBytes==0,"admission allocates no scene/skin/post resources");
            var basis=files.Get(ShaderProfile.Scene3D,true,false);Bad(()=>RuntimeShaderPackage.ComposeGeometryPixel(files.Get(ShaderProfile.Flat2D,false,false),library.Get(ShaderProfile.Scene3D,false)));
            Bad(()=>RuntimeShaderPackage.ComposeGeometryPixel(basis,library.Get(ShaderProfile.SceneEnvironment,false)));Bad(()=>RuntimeShaderPackage.ComposeGeometryPixel(basis,library.Get(ShaderProfile.Scene3D,true)));Bad(()=>RuntimeShaderPackage.ComposeGeometryPixel(files.Get(ShaderProfile.Scene3D,false,false),library.Get(ShaderProfile.Scene3D,false)));
            RuntimeShaderPackage Altered(bool wrongOutput){var catalog=DefaultSceneShaders.CopyCatalog(renderer,true);var high=SceneShadowShaders.ComposeHigh(catalog,DefaultSceneShaders.Select(catalog,true));var s=high.Selection;
                ShaderDescriptor Change(ShaderDescriptor descriptor,string before,string after){var d=descriptor.CopyDefinition();string source=d.Source.Replace(before,after,StringComparison.Ordinal);Check(source!=d.Source,"changed shader fixture");return ShaderDescriptor.Prepare(d with{Source=source,SourceHash=ShaderContractCodec.HashSource(source)});}
                using var compiler=new ShaderCompilerService(renderer,()=>true);
                return RuntimeShaderPackage.Cook(ShaderProfile.Scene3D,true,false,[
                    new(RuntimeShaderRole.GeometryVertex,compiler.Prepare(wrongOutput?s.GeometryVertex:Change(s.GeometryVertex,"mul(MVP,","mul(MVP*1.1,"))),
                    new(RuntimeShaderRole.GeometryPixel,compiler.Prepare(wrongOutput?Change(s.GeometryPixel,":SV_TARGET",":SV_TARGET1"):s.GeometryPixel)),
                    new(RuntimeShaderRole.ToneVertex,compiler.Prepare(s.ToneVertex)),new(RuntimeShaderRole.TonePixel,compiler.Prepare(s.TonePixel)),
                    new(RuntimeShaderRole.ShadowVertex,compiler.Prepare(s.ShadowVertex!)),new(RuntimeShaderRole.ShadowPixel,compiler.Prepare(s.ShadowPixel!))]);}
            Bad(()=>RuntimeShaderPackage.ComposeGeometryPixel(Altered(false),library.Get(ShaderProfile.Scene3D,false)));
            string altered=Fixture();var alteredRow=index.Programs.Single(p=>p.Profile=="Scene3D"&&!p.Skinning);var badOutput=Altered(true);
            File.WriteAllBytes(Path.Combine(altered,alteredRow.Path),badOutput.CopyBytes());var changedIndex=index with{Programs=index.Programs.Select(p=>p==alteredRow?p with{Sha256=badOutput.ContentHash}:p).ToArray()};File.WriteAllBytes(Path.Combine(altered,ShadowLibrarySelection.IndexPath),changedIndex.Encode());Bad(()=>RuntimeShadowLibrary.Load(altered,Hash(changedIndex.Encode())).Dispose());
            // Metadata is not trusted GPU evidence: forge the declared author hash, keep actual
            // wrong SV_TARGET1 bytecode, and require native whole-group reflection to reject it.
            byte[] forged=badOutput.CopyBytes();int at=RuntimeShaderPackage.HeaderBytes;while(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(at,4))!=(int)RuntimeShaderRole.GeometryPixel)at+=RuntimeShaderPackage.EntryHeaderBytes+System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(at+92,4));
            Convert.FromHexString(SceneShadowShaders.CreateHighPixel(ShaderProfile.Scene3D).ContentHash).CopyTo(forged,at+20);SHA256.HashData(forged.AsSpan(RuntimeShaderPackage.HeaderBytes)).CopyTo(forged,28);
            File.WriteAllBytes(Path.Combine(altered,alteredRow.Path),forged);changedIndex=index with{Programs=index.Programs.Select(p=>p==alteredRow?p with{Sha256=Hash(forged)}:p).ToArray()};File.WriteAllBytes(Path.Combine(altered,ShadowLibrarySelection.IndexPath),changedIndex.Encode());
            using(var invalid=RuntimeShadowLibrary.Load(altered,Hash(changedIndex.Encode())))Bad(()=>invalid.Prepare(renderer,files,()=>true));
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.PostStats.Bindings==0,"wrong actual pixel output rejected before allocation");
            foreach(Action callback in new Action[]{()=>library.Dispose(),()=>renderer.Dispose(),()=>prepared.Resolve(basis),()=>library.Prepare(renderer,files,()=>true)}){onApproval=callback;Bad(()=>prepared.Resolve(basis));onApproval=null;Pass(prepared.Resolve(basis).Scene is not null,"cache approval reentry rejection leaves live library");}
            allowed=false;Bad(()=>prepared.Resolve(basis));allowed=true;
            int count=0;_=library.Prepare(renderer,files,()=>{count++;return true;});int call=0;Bad(()=>library.Prepare(renderer,files,()=>++call<count));
            Exception? owner=null;var worker=new Thread(()=>{try{prepared.Resolve(basis);}catch(Exception e){owner=e;}});worker.Start();worker.Join();Pass(owner is InvalidOperationException,"prepared owner thread");
            using var target=renderer.CreateViewTarget(64,64);byte[] image=new byte[64*64*4];ulong frame=1;
            var bloom=preparedPost.Program(renderer,PostProcessLibrarySelection.BloomModule);
            int Display(float value,Guid module){double c=(float)(Half)value;c=module==PostProcessLibrarySelection.AcesModule?Math.Clamp(c*(2.51*c+.03)/(c*(2.43*c+.59)+.14),0,1):module==PostProcessLibrarySelection.ReinhardModule?c/(1+c):Math.Clamp(c,0,1);return (int)Math.Round((c<=.0031308?12.92*c:1.055*Math.Pow(c,1/2.4)-.055)*255);}
            foreach(var row in post.CopySelection().Tones.Where(t=>t.Shadows)){
                var profile=row.Profile=="Scene3D"?ShaderProfile.Scene3D:ShaderProfile.SceneEnvironment;var tone=post.Tone(row.ModuleId,profile,true,row.Skinning);var high=library.Get(profile,row.Skinning);var composed=RuntimeShaderPackage.ComposeGeometryPixel(tone,high);var role=profile==ShaderProfile.Scene3D?RuntimeShaderRole.GeometryPixel:RuntimeShaderRole.EnvironmentGeometryPixel;
                Pass(composed.ContentHash==RuntimeShaderPackage.ComposeGeometryPixel(tone,high).ContentHash&&!composed.GpuValidated,"deterministic source-free composition");
                foreach(var metadata in tone.CopyPage(0))Pass(composed.CopyBytecode(metadata.Role).SequenceEqual((metadata.Role==role?high:tone).CopyBytecode(metadata.Role)),"only geometry pixel bytes replaced "+metadata.Role);
                var group=prepared.Resolve(tone);using var scene=new ScenePipelineSession(renderer,SceneShadowPresets.Get(SceneShadowQuality.High).CreatePipeline(true,ambient:0,profile:profile),64,64,shaders:group.Scene,environmentShaders:group.Environment);
                scene.ConfigurePostProcess(BloomFeature.Build(64,64,new(Threshold:.25f,SoftKnee:0,Intensity:.4f),bloom.Package),bloom,()=>allowed);
                scene.Submit(frame++,[],[],new ResourceLighting(Vector3.Zero,Vector3.UnitZ,Vector4.UnitW),Matrix4x4.Identity,SceneShadowPresets.Get(SceneShadowQuality.High).Settings,target,clear:new(.5f,.25f,.125f,1));renderer.CaptureTarget(target,image);renderer.Present();int[] expected=[Display(.6f,row.ModuleId),Display(.3f,row.ModuleId),Display(.15f,row.ModuleId),255];int error=image.Select((v,i)=>Math.Abs(v-expected[i%4])).Max();maxError=Math.Max(maxError,error);Pass(error<=1,"whole Tone/Bloom image retained "+row.Path);
            }
            // A new metadata identity is a distinct bounded cache key; never silently evict live borrowers.
            byte[] extra=basis.CopyBytes();Guid.NewGuid().TryWriteBytes(extra.AsSpan(RuntimeShaderPackage.HeaderBytes+4,16));SHA256.HashData(extra.AsSpan(RuntimeShaderPackage.HeaderBytes)).CopyTo(extra,28);var distinct=RuntimeShaderPackage.Preflight(extra,Hash(extra));Bad(()=>prepared.Resolve(distinct));
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"all 12 High/Tone/Bloom variants close API0/0");
            library.Dispose();Bad(()=>prepared.Resolve(basis));
        }
        using(var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64,pureUi:true))using(var files=RuntimeShaderFileSet.Load(editor))using(var library=RuntimeShadowLibrary.Load(editor,hash)){
            Bad(()=>library.Prepare(renderer,files,()=>true));Pass(renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.PostStats.Bindings==0,"pureUI rejects high admission without 3D resources");
        }
        using(var staged=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",config,"packages.json")))){
            foreach(string kind in new[]{"editor","playerDx11","playerNull"}){
                string directory=staged.RootElement.GetProperty(kind).GetString()!;Ncma.Application.DeploymentManifest.Validate(directory);
                if(kind=="playerNull"){Pass(!Directory.Exists(Path.Combine(directory,"assets/shadows"))&&!Directory.Exists(Path.Combine(directory,"assets/shaders")),"checked Null deploys no 3D shader files");continue;}
                using(var productLibrary=RuntimeShadowLibrary.LoadDeployment(directory))Pass(productLibrary.ContentHash==hash,"checked product High file closure "+kind);
                string relocated=Path.Combine(output,"relocated-library-"+kind);Directory.CreateDirectory(Path.Combine(relocated,"assets/shadows"));
                foreach(string path in entries.Append("deployment-manifest.json"))File.Copy(Path.Combine(directory,path),Path.Combine(relocated,path));
                var locks=new List<FileStream>();try{
                    foreach(string path in entries)locks.Add(File.Open(Path.Combine(directory,path),FileMode.Open,FileAccess.Read,FileShare.None));
                    using var moved=RuntimeShadowLibrary.LoadDeployment(relocated);Pass(moved.ContentHash==hash,"relocated immutable library uses only pinned copy "+kind);
                    Run("cpu-product-"+kind,typeof(Program).Assembly.Location,"--shadow-library-preflight",relocated,hash);
                }finally{foreach(var stream in locks)stream.Dispose();}
            }
        }
        File.WriteAllText(Path.Combine(output,"shadow-library-results.json"),JsonSerializer.Serialize(new{cases,maxError,wholeImages=12,apiErrors=0,apiWarnings=0,formalHosts=false,animatedShadowOracle=false,manualAcceptance=false}));Console.WriteLine($"PASS M7.5 shadow library: {cases} checks; 12 Tone/Bloom images; maxError={maxError}; API0/0; {output}");return 0;
    }
}
