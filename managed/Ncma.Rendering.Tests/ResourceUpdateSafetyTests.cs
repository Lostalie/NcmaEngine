using System.Text.Json;
using System.Numerics;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int ResourceUpdateSafetyTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int cases=0;void Pass(bool value,string message){Check(value,"M7.6 resource preparation: "+message);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Resource update safety",64,64,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);
        using var cache=new RenderResourceCache(renderer);
        Guid textureId=Guid.NewGuid();var texture=TextureData.Prepare(1,1,TextureSemantic.Color,[128,64,32,255]);
        var resolved=new ResolvedTexture(Version(textureId,texture),texture);
        var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseTexture=textureId};
        // This first case reproduced installation after a resolver reentered Trim.
        Reject(()=>{using var unexpected=cache.AcquireMaterial(Version(definition),definition,_=>{cache.Trim();return resolved;},true,true,out _);});
        Pass(cache.Count==0,"resolver reentry rejected before candidate allocation");
        foreach(Action mutation in new Action[]{cache.Dispose,cache.Trim,()=>{using var x=cache.AcquireTexture(resolved.Version,texture);},
            ()=>{using var x=cache.AcquireMaterial(Version(definition),definition,_=>resolved,true,true,out _);}}) {
            Reject(()=>{using var unexpected=cache.AcquireMaterial(Version(definition),definition,_=>{try{mutation();}catch(InvalidOperationException){}return resolved;},true,true,out _);});
            Pass(cache.Count==0,"caught cache violation cannot publish outer candidate");
        }
        var meshData=Quad();using var mesh=renderer.CreateStaticMesh(meshData);using var target=renderer.CreateViewTarget(64,64);
        var pipeline=new ResourcePipelineSession(renderer,new ResourceMeshPipeline(),64,64);ulong frame=1;
        byte[] Capture(GpuMaterial material) {
            pipeline.Submit(frame++,[ResourceDraw.Create(mesh,material,meshData.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity)],
                new(new(0,0,2),Vector3.UnitZ,Vector4.One),target,Vector4.UnitW);
            byte[] pixels=new byte[64*64*4];renderer.CaptureTarget(target,pixels);renderer.Present();return pixels;
        }
        using(var old=cache.AcquireMaterial(Version(definition),definition,_=>resolved,true,true,out _)) {
            var resource=old.Resource;byte[] before=Capture(resource);Pass(before.Distinct().Count()>4,"nonempty old material image");
            Reject(resource.Dispose);
            Pass(before.SequenceEqual(Capture(resource)),"borrower cannot directly destroy cache-owned material");
            Reject(()=>{using var unexpected=cache.AcquireMaterial(Version(definition),definition,_=>{try{resource.Dispose();}catch(InvalidOperationException){}return resolved;},true,true,out _);});
            Pass(before.SequenceEqual(Capture(resource)),"caught raw material close in resolver rejects candidate without changing pixels");
            using(var textureLease=cache.AcquireTexture(resolved.Version,texture)) {
                var borrowed=textureLease.Resource;Reject(borrowed.Dispose);
                Task.Run(()=>Reject(borrowed.Dispose)).GetAwaiter().GetResult();
                Pass(before.SequenceEqual(Capture(resource)),"borrowed texture owner/foreign-thread close preserves dependent material pixels");
                textureLease.Dispose();Reject(borrowed.Dispose);
                cache.Trim();Pass(before.SequenceEqual(Capture(resource)),"dropped texture lease cannot release a material dependency");
            }
            Reject(()=>{using var unexpected=cache.AcquireMaterial(Version(definition),definition,_=>{try{old.Dispose();}catch(InvalidOperationException){}return resolved;},true,true,out _);});
            Pass(ReferenceEquals(old.Resource,resource),"borrowed old generation retained after refused callback release");
            Reject(()=>{using var unexpected=cache.AcquireMaterial(Version(definition),definition,_=>throw new InvalidOperationException("resolver fault"),true,true,out _);});
            using(var retry=cache.AcquireMaterial(Version(definition),definition,_=>resolved,true,true,out _))
                Pass(ReferenceEquals(retry.Resource,resource),"resolver failure clears guard and preserves valid cache hit");
            Task.Run(()=>Reject(cache.Trim)).GetAwaiter().GetResult();
            Pass(ReferenceEquals(old.Resource,resource),"foreign thread rejected without invalidating original lease");
            Pass(before.SequenceEqual(Capture(old.Resource)),"all refused replacements leave exact original GPU pixels");
            var changed=definition with{BaseColor=new(.1f,1,.1f,1)};
            Reject(()=>{using var wrongGeneration=cache.AcquireMaterial(Version(changed),changed,_=>resolved,true,true,out _);});
            using(var next=cache.AcquireMaterial(Version(changed,2),changed,_=>resolved,true,true,out _)) {
                Pass(!before.SequenceEqual(Capture(next.Resource)),"explicit new immutable generation has new pixels");
                Pass(before.SequenceEqual(Capture(old.Resource)),"old pinned generation retains pixels while new generation coexists");
            }
        }
        cache.Trim();Pass(cache.Count==0,"initial generation drained");
        var meshVersion=new RenderAssetVersion(Guid.NewGuid(),1,new string('A',64));
        using(var cachedMesh=cache.AcquireMesh(meshVersion,meshData)) {
            var borrowed=cachedMesh.Resource;var stats=renderer.SceneStats;
            Reject(borrowed.Dispose);Task.Run(()=>Reject(borrowed.Dispose)).GetAwaiter().GetResult();
            Pass(renderer.SceneStats.LiveMeshes==stats.LiveMeshes,"cached static mesh cannot be retired by borrower");
            var draw=StaticMeshDraw.Create(borrowed,meshData.Ranges[0],Matrix4x4.Identity,Vector4.One);
            byte[] before=new byte[64*64*4],after=new byte[before.Length];
            renderer.SubmitStaticMeshes(frame++,[draw],new(0,0,64,64),Vector4.UnitW);
            renderer.Capture(before);
            cachedMesh.Dispose();Reject(borrowed.Dispose);
            ulong retainedBytes=cache.GeometryBytes;
            try {
                bool busy=false;
                try { cache.Trim(); }
                catch(PluginException error) when(error.PluginId=="renderer"&&error.Phase=="destroy_mesh"&&error.Result==PluginResult.Busy) { busy=true; }
                Pass(busy&&cache.Count==1&&cache.GeometryBytes==retainedBytes&&renderer.SceneStats.LiveMeshes==stats.LiveMeshes,"exact native destroy_mesh Busy retains cache ownership and accounting");
            }
            finally { renderer.Present(); }
            using(var retry=cache.AcquireMesh(meshVersion,meshData)) {
                Pass(ReferenceEquals(borrowed,retry.Resource),"failed native retirement retains exact reusable generation");
                Reject(borrowed.Dispose);
                renderer.SubmitStaticMeshes(frame++,[StaticMeshDraw.Create(retry.Resource,meshData.Ranges[0],Matrix4x4.Identity,Vector4.One)],new(0,0,64,64),Vector4.UnitW);
                renderer.Capture(after);renderer.Present();
                Pass(before.SequenceEqual(after)&&before.Distinct().Count()>1,"busy retirement leaves exact rendered mesh image");
            }
            cache.Trim();Reject(()=>StaticMeshDraw.Create(borrowed,meshData.Ranges[0],Matrix4x4.Identity,Vector4.One));
            Pass(cache.Count==0&&cache.GeometryBytes==0&&renderer.SceneStats.LiveMeshes==stats.LiveMeshes-1,"explicit post-frame trim drains retained resource and invalidates raw reference");
        }
        var (bindSource,bindRig)=BindFixture();var bindData=BindPoseMeshUploadData.Prepare(bindSource,bindRig);
        using(var bind=cache.AcquireBindPoseMesh(meshVersion with{AssetId=Guid.NewGuid()},bindData)) {
            var borrowed=bind.Resource;Reject(borrowed.Dispose);
            Pass(ReferenceEquals(borrowed.BindPoseSource,bindData),"cache-owned bind pose retains numerical source");
            bind.Dispose();Reject(borrowed.Dispose);cache.Trim();
            Reject(()=>{_=borrowed.BindPoseSource;});Pass(cache.Count==0,"bind pose retired only by owning cache");
        }
        var directStats=renderer.ResourceStats;var directMeshes=renderer.SceneStats.LiveMeshes;
        using(var directTexture=renderer.CreateTexture(texture))using(var directMesh=renderer.CreateStaticMesh(meshData))
        using(var directBind=renderer.CreateBindPoseMesh(bindData))using(var directMaterial=renderer.CreateMaterial(definition,[directTexture,null,null,null,null,null],true)) {
            directMaterial.Dispose();directBind.Dispose();directMesh.Dispose();directTexture.Dispose();
        }
        Pass(renderer.ResourceStats.Materials==directStats.Materials&&renderer.ResourceStats.Textures==directStats.Textures&&renderer.SceneStats.LiveMeshes==directMeshes,"direct-created GPU resources retain ordinary disposal");
        var set=new MaterialSetDefinition(1,Guid.NewGuid(),[definition.AssetId]);
        var setVersion=new RenderAssetVersion(set.AssetId,1,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(MaterialCodec.Encode(set))));
        Reject(()=>{using var unexpected=cache.AcquireMaterialSet(setVersion,set,_=>{try{cache.Dispose();}catch(InvalidOperationException){}return new(Version(definition),definition);},_=>resolved,true,true);});
        Pass(cache.Count==0,"material-set resolver has same nonreentrant boundary");
        using(var valid=cache.AcquireMaterialSet(setVersion,set,_=>new(Version(definition),definition),_=>resolved,true,true))
            Pass(valid[0] is not null,"legitimate nested material-set composition remains supported");
        cache.Trim();
        using var cook=new EnvironmentCookService(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),()=>true);
        var environment=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Neutral(Guid.NewGuid()),new(8,4,8,64));
        for(int failAt=1;failAt<=2;failAt++) {
            int approvals=0;ulong publications=renderer.EnvironmentStats.Publications;
            Reject(()=>{using var invalid=cache.AcquireEnvironment(environment,()=>{if(++approvals==failAt)try{cache.Dispose();}catch(InvalidOperationException){}return true;});});
            Pass(cache.Count==0&&renderer.EnvironmentStats.Live==0&&renderer.EnvironmentStats.Publications==publications,"environment initial/final approval rejection leaves no candidate");
        }
        using(var old=cache.AcquireEnvironment(environment,()=>true)) {
            var resource=old.Resource;float[] before=resource.CaptureForDiagnostics(()=>true);
            Reject(resource.Dispose);Task.Run(()=>Reject(resource.Dispose)).GetAwaiter().GetResult();
            Pass(before.SequenceEqual(resource.CaptureForDiagnostics(()=>true)),"borrowed environment close preserves every GPU texel");
            Reject(()=>{using var invalid=cache.AcquireEnvironment(environment,()=>{try{resource.Dispose();}catch(InvalidOperationException){}return true;});});
            Pass(before.SequenceEqual(resource.CaptureForDiagnostics(()=>true)),"caught raw environment close rejects approval and retains texels");
            Reject(()=>{using var invalid=cache.AcquireEnvironment(environment,()=>{try{cache.Trim();}catch(InvalidOperationException){}return true;});});
            Pass(ReferenceEquals(resource,old.Resource)&&before.SequenceEqual(old.Resource.CaptureForDiagnostics(()=>true)),"environment cache hit rechecks approval and keeps GPU texels");
        }
        cache.Trim();Pass(cache.Count==0&&renderer.EnvironmentStats.Live==0&&renderer.ResourceStats.Materials==0&&renderer.ResourceStats.Textures==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"final drain and API0/0");
        using(var directEnvironment=renderer.CreateEnvironment(environment,()=>true))directEnvironment.Dispose();
        Pass(renderer.EnvironmentStats.Live==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"direct environment remains independently disposable; final API0/0");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,apiErrors=0,apiWarnings=0,deviceLossRecovery=false,fullStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 resource preparation {cases} checks; API0/0; {output}");return 0;
    }
}
