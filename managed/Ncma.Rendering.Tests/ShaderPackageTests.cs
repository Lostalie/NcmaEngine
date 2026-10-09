using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int ShaderPackageChild(string path,string hash)
    {
        Check(new FileInfo(path).Length<=RuntimeShaderPackage.MaxBytes,"Bounded child input");
        var package=RuntimeShaderPackage.Preflight(File.ReadAllBytes(path),hash);
        Check(!package.GpuValidated&&package.Count>0,"CPU package is not GPU admission");
        var modules=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(m=>m.ModuleName).ToArray();
        Check(!modules.Any(n=>new[]{"NcmaRenderer.dll","NcmaPlatform.dll","NcmaNative.dll","NcmaPhysics.dll","NcmaGui.dll","NcmaAnimationKernel.dll","d3dcompiler_47.dll"}.Contains(n,StringComparer.OrdinalIgnoreCase)),"Preflight loaded a native plugin/compiler");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("Ncma.Editor",StringComparison.Ordinal)),"Preflight loaded Editor");
        Console.WriteLine("PASS source-free CPU shader package preflight; native/compiler/Editor=0; GPU admission=false");return 0;
    }
    private static int ShaderPackageTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0;var packages=new List<RuntimeShaderPackage>();
        void Pass(bool ok,string text){Check(ok,"M7.1-C4-A "+text);cases++;}
        void Bad(Action action,string? code=null){
            try{action();}catch(ShaderContractException e){Pass(code is null||e.Code==code,"redacted rejection "+code);Check(e.Message.Length<180&&!e.Message.Contains("PRIVATE_HLSL"),"Package diagnostic source leak");return;}
            throw new Exception("Expected package rejection "+code);
        }
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        RuntimeShaderPackage Read(byte[] bytes)=>RuntimeShaderPackage.Preflight(bytes,Hash(bytes));
        void U(byte[] bytes,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at,4),value);
        uint U32(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at,4));
        void RepairPayload(byte[] bytes){SHA256.HashData(bytes.AsSpan(60)).CopyTo(bytes,28);}
        byte[] original;
        using(var loader=new PluginLoader()){
            loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Shader package cooking",256,256,false);
            using var renderer=new RendererSession(native,window,256,256,pureUi:true);using var compiler=new ShaderCompilerService(renderer,()=>true);
            var defaults=DefaultUiShaders.CopyCatalog(renderer);var pair=DefaultUiShaders.Select(defaults);
            var vertex=compiler.Prepare(pair.Vertex);var pixel=compiler.Prepare(pair.Pixel);
            var inputs=new[]{new RuntimeShaderInput(RuntimeShaderRole.UiVertex,vertex),new RuntimeShaderInput(RuntimeShaderRole.UiPixel,pixel)};
            var ui=RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,inputs);packages.Add(ui);original=ui.CopyBytes();
            Pass(ui.Count==2&&ui.Profile==ShaderProfile.Flat2D&&!ui.Shadows&&!ui.Skinning&&!ui.GpuValidated,"exact pure2D no3D closure and honest admission");
            Pass(Read(original).CopyBytes().SequenceEqual(original)&&ui.ContentHash==Hash(original),"strict canonical roundtrip");
            Pass(RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,inputs.Reverse()).ContentHash==ui.ContentHash,"canonical role order");
            Pass(ui.CopyBytecode(RuntimeShaderRole.UiVertex).SequenceEqual(vertex.CopyBytecode())&&ui.CopyBytecode(RuntimeShaderRole.UiPixel).SequenceEqual(pixel.CopyBytecode()),"actual compiler bytes unchanged");
            byte[] copy=ui.CopyBytes();copy[0]=0;byte[] code=ui.CopyBytecode(RuntimeShaderRole.UiVertex);code[0]=0;var rows=ui.CopyPage(0);rows[0]=default;
            Pass(ui.CopyBytes().SequenceEqual(original)&&ui.CopyBytecode(RuntimeShaderRole.UiVertex)[0]==(byte)'D'&&ui.CopyPage(0)[0].AssetId==pair.Vertex.AssetId,"private owned storage and defensive copies");
            byte[] callerBytes=(byte[])original.Clone();var owned=Read(callerBytes);Array.Clear(callerBytes);
            Pass(owned.CopyBytes().SequenceEqual(original),"preflight owns input snapshot after caller clears bytes");
            Parallel.For(0,16,_=>Check(owned.CopyBytecode(RuntimeShaderRole.UiPixel).SequenceEqual(pixel.CopyBytecode()),"Concurrent immutable read"));
            Pass(owned.ContentHash==ui.ContentHash,"CPU immutable reads need no renderer/thread owner");
            Bad(()=>ui.CopyPage(-1));Bad(()=>ui.CopyPage(1));Bad(()=>ui.CopyBytecode(RuntimeShaderRole.SkinCompute));
            Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,true,false,inputs));
            Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,true,inputs));
            Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Skinning,false,false,inputs));
            Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,inputs.Take(1)));
            Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,[inputs[0],inputs[0]]));
            Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,[new(RuntimeShaderRole.UiVertex,pixel),inputs[1]]));
            int enumerated=0;IEnumerable<RuntimeShaderInput> Endless(){while(true){enumerated++;yield return inputs[0];}}
            Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,Endless()));Pass(enumerated==10,"unbounded input enumerable stops at10");
            var definition=pair.Pixel.CopyDefinition();string source="// PRIVATE_HLSL_MARKER_NEVER_DEPLOY\n"+definition.Source.Replace("Image.Sample(Linear,v.uv)*v.c","Image.Sample(Linear,v.uv).bgra*v.c.bgra",StringComparison.Ordinal);
            var user=ShaderDescriptor.Prepare(definition with{AssetId=Guid.NewGuid(),Name="Private named user",Source=source,SourceHash=ShaderContractCodec.HashSource(source)});
            var userCode=compiler.Prepare(user);var userPackage=RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,[inputs[0],new(RuntimeShaderRole.UiPixel,userCode)]);packages.Add(userPackage);
            Pass(userPackage.ContentHash!=ui.ContentHash&&userPackage.CopyPage(0)[1].AuthorContentHash==user.ContentHash,"exact trusted user author identity, independent package hash");
            Pass(!Encoding.UTF8.GetString(userPackage.CopyBytes()).Contains("PRIVATE_HLSL")&&!Encoding.UTF8.GetString(userPackage.CopyBytes()).Contains("Private named user"),"no author source/name/debug text serialized");
            var deferred=definition with{AssetId=Guid.NewGuid(),Name="Deferred output check",Source=definition.Source.Replace(":SV_TARGET",":SV_TARGET1",StringComparison.Ordinal)};
            deferred=deferred with{SourceHash=ShaderContractCodec.HashSource(deferred.Source)};
            var deferredPackage=RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,[inputs[0],new(RuntimeShaderRole.UiPixel,compiler.Prepare(ShaderDescriptor.Prepare(deferred)))]);
            Pass(!deferredPackage.GpuValidated,"CPU envelope explicitly does NOT approve wrong output signature; native gate remains required");
            foreach(bool shadows in new[]{false,true}){
                var catalog=DefaultSceneShaders.CopyCatalog(renderer,shadows);var selected=DefaultSceneShaders.Select(catalog,shadows);
                var scene=new List<RuntimeShaderInput>{new(RuntimeShaderRole.GeometryVertex,compiler.Prepare(selected.GeometryVertex)),new(RuntimeShaderRole.GeometryPixel,compiler.Prepare(selected.GeometryPixel)),
                    new(RuntimeShaderRole.ToneVertex,compiler.Prepare(selected.ToneVertex)),new(RuntimeShaderRole.TonePixel,compiler.Prepare(selected.TonePixel))};
                if(shadows){scene.Add(new(RuntimeShaderRole.ShadowVertex,compiler.Prepare(selected.ShadowVertex!)));scene.Add(new(RuntimeShaderRole.ShadowPixel,compiler.Prepare(selected.ShadowPixel!)));}
                var unskinned=RuntimeShaderPackage.Cook(ShaderProfile.Scene3D,shadows,false,scene);packages.Add(unskinned);
                var skinCatalog=DefaultSkinShader.CopyCatalog(renderer);var skin=compiler.Prepare(DefaultSkinShader.Select(skinCatalog));scene.Add(new(RuntimeShaderRole.SkinCompute,skin));
                var skinned=RuntimeShaderPackage.Cook(ShaderProfile.Scene3D,shadows,true,scene);packages.Add(skinned);
                Pass(unskinned.Count==(shadows?6:4)&&skinned.Count==(shadows?7:5)&&skinned.Skinning,"exact static/animated shadow variant closure "+shadows);
                Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Scene3D,shadows,false,scene));
                Bad(()=>RuntimeShaderPackage.Cook(ShaderProfile.Scene3D,!shadows,true,scene));
                Pass(Read(skinned.CopyBytes()).ContentHash==skinned.ContentHash,"actual Scene/Skin code pure CPU roundtrip "+shadows);
            }
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.UiStats.ResidentBytes==0&&renderer.Stats.SubmittedFrames==0,"cook creates no GPU resources/frames");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"actual cooking API0/0");
        }
        // After plugin shutdown, rehashed negative inputs still undergo every structural check.
        void Mutate(Action<byte[]> change,bool repair=true,string? expected=null){byte[] b=(byte[])original.Clone();change(b);if(repair)RepairPayload(b);Bad(()=>Read(b),expected);}
        Bad(()=>RuntimeShaderPackage.Preflight(original,new string('0',64)),"runtime_hash");
        Bad(()=>RuntimeShaderPackage.Preflight(original,Hash(original).ToLowerInvariant()),"runtime_hash");
        foreach(int n in new[]{0,1,31,59,60,61,127,188,original.Length-1})Bad(()=>Read(original.AsSpan(0,n).ToArray()));
        Bad(()=>Read(new byte[RuntimeShaderPackage.MaxBytes+1]),"runtime_budget");
        Mutate(b=>U(b,0,0),false,"runtime_version");Mutate(b=>U(b,4,2),false,"runtime_version");
        Mutate(b=>U(b,8,2),false,"runtime_backend");Mutate(b=>U(b,12,uint.MaxValue),false,"runtime_header");
        Mutate(b=>U(b,16,4),false,"runtime_header");Mutate(b=>U(b,16,1),false,"runtime_profile");
        Mutate(b=>U(b,20,9),false,"runtime_closure_hash");Mutate(b=>U(b,24,uint.MaxValue),false,"runtime_header");
        Mutate(b=>b[28]^=1,false,"runtime_closure_hash");
        Mutate(b=>U(b,60,1),true,"runtime_role_order");
        Mutate(b=>Array.Clear(b,64,16),true,"runtime_identity");
        Mutate(b=>Array.Clear(b,80,32),true,"runtime_identity");
        Mutate(b=>b[112]^=1,true,"runtime_binding_contract");
        Mutate(b=>U(b,144,46),true,"runtime_compiler");Mutate(b=>U(b,148,0),true,"runtime_compiler");
        Mutate(b=>U(b,152,uint.MaxValue),true,"runtime_bytecode_budget");Mutate(b=>b[156]^=1,true,"runtime_bytecode_hash");
        int second=188+(int)U32(original,152);
        Mutate(b=>Array.Copy(b,64,b,second+4,16),true,"runtime_identity");
        Mutate(b=>b[second+52]^=1,true,"runtime_binding_contract");
        Mutate(b=>b[^1]^=1,true,"runtime_bytecode_hash");
        var trailing=original.Concat(new byte[]{0}).ToArray();U(trailing,24,(uint)trailing.Length);RepairPayload(trailing);Bad(()=>Read(trailing),"runtime_trailing_bytes");
        void CorruptCode(Action<byte[],int,int> change,string? expected=null){
            byte[] b=(byte[])original.Clone();int length=(int)U32(b,152);change(b,188,length);
            SHA256.HashData(b.AsSpan(188,length)).CopyTo(b,156);RepairPayload(b);Bad(()=>Read(b),expected);
        }
        CorruptCode((b,s,n)=>b[s]=0,"runtime_dxbc_header");
        CorruptCode((b,s,n)=>U(b,s+28,17),"runtime_dxbc_chunks");
        CorruptCode((b,s,n)=>U(b,s+32,uint.MaxValue),"runtime_dxbc_offset");
        CorruptCode((b,s,n)=>U(b,s+(int)U32(b,s+32),0x47424453),"runtime_dxbc_chunk_kind");
        CorruptCode((b,s,n)=>U(b,s+(int)U32(b,s+32)+4,uint.MaxValue),"runtime_dxbc_chunk_budget");
        CorruptCode((b,s,n)=>U(b,s+36,U32(b,s+32)),"runtime_dxbc_offset");
        CorruptCode((b,s,n)=>{for(int i=0;i<U32(b,s+28);i++){int chunk=s+(int)U32(b,s+32+i*4);if(U32(b,chunk)==0x58454853){U(b,chunk+8,0x50);return;}}throw new Exception("SHEX fixture");},"runtime_dxbc_stage");
        // Separate process: only source-free package inputs; no compiler/plugin initialization.
        foreach(var package in packages){
            string path=Path.Combine(Path.GetFullPath(output),"runtime-"+packages.IndexOf(package)+".ncshaderpak");File.WriteAllBytes(path,package.CopyBytes());
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetFullPath(output)};
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--shader-package-preflight");start.ArgumentList.Add(path);start.ArgumentList.Add(package.ContentHash);
            using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(30000)){child.Kill(true);throw new Exception("Package child deadline");}
            Pass(child.ExitCode==0&&stdout.Result.Contains("native/compiler/Editor=0"),"separate process preflight "+stderr.Result);
        }
        File.WriteAllText(Path.Combine(output,"shader-package-results.json"),JsonSerializer.Serialize(new{schema=1,cases,packages=packages.Count,sourceFree=true,nativeFreePreflight=true,gpuValidated=false,formalHostSwitch=false,agentAuthority=false,manualAcceptance=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.1-C4-A {cases} source-free package/closure/hash/DXBC/copy/budget/child cases; {packages.Count} actual compiled variants; GPU admission/host switch pending B/C");return 0;
    }
}
