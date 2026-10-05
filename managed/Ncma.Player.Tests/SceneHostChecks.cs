using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Player.App;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class SceneHostChecks
{
    internal static string[] Run(string output,string plugins)
    {
        static void Check(bool value,string diagnostic){if(!value)throw new Exception(diagnostic);}
        string root=Path.Combine(output,"Static Scene Player");Directory.CreateDirectory(Path.Combine(root,"assets"));
        Guid project=Guid.NewGuid(),model=Guid.NewGuid(),mesh=Guid.NewGuid(),slots=Guid.NewGuid();var settings=new ImportSettings(1,30,true);string sourceHash=new('C',64);
        ImportVertex V(float x,float y)=>new(new(x,y,0),Vector3.UnitZ,Vector2.Zero,default,Vector4.Zero);
        var payload=new MeshPayload(false,0,[V(-1,-1),V(1,-1),V(-1,1)],[],[0,1,2],[0],[],1);
        var manifest=new ModelAssetManifest(1,model,true,sourceHash,settings,null,[new(mesh,slots)],[]);
        byte[] bytes=DerivedAssetCodec.Encode([new(model,AssetKind.StaticMesh,ModelAssetManifestCodec.Encode(manifest)),new(mesh,AssetKind.StaticMesh,ModelPayloadCodec.Encode(payload)),new(slots,AssetKind.MaterialSet,ModelPayloadCodec.Encode(new MaterialSlotsPayload(["Imported default"]))) ]);
        string hash=Convert.ToHexString(SHA256.HashData(bytes)),relative=$"out/assets/{project:N}/{model:N}/1-{hash}.nca",generation=Path.Combine(root,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(generation)!);File.WriteAllBytes(generation,bytes);
        File.WriteAllBytes(Path.Combine(root,"assets/model.fbx.ncmeta"),AssetRecordCodec.Encode(new(1,model,AssetKind.StaticMesh,"assets/model.fbx",sourceHash,"ufbx",1,settings,
            [new(mesh,AssetKind.StaticMesh,"mesh/0","Triangle",false),new(slots,AssetKind.MaterialSet,"materials/0","Slots",false)],[],new(1,hash,relative))));
        var document=new SceneDocument("Actual static Player",RenderComponentRegistry.CreateRegistry(),SceneRenderValidation.RequireComposition);
        var item=document.World.CreateObject("Mesh");item.Set(TransformData.Identity with{Position=new(0,0,-4)});item.Set(new StaticMeshData(mesh,slots,true,true,uint.MaxValue));
        var camera=document.World.CreateObject("Explicit camera");camera.Set(TransformData.Identity);camera.Set(CameraData.Default);
        var light=document.World.CreateObject("Primary light");light.Set(TransformData.Identity);light.Set(DirectionalLightData.Default);
        string scene=Path.Combine(root,"start.ncmascene"),projectFile=Path.Combine(root,"scene.ncmaproject");SceneDocumentFiles.Save(document,scene);byte[] saved=File.ReadAllBytes(scene);
        string assembly=Assembly.GetExecutingAssembly().Location;File.Copy(assembly,Path.Combine(root,"gameplay.dll"));File.Copy(Path.ChangeExtension(assembly,".deps.json"),Path.Combine(root,"gameplay.deps.json"));
        var config=new ProjectConfiguration(1,project,"Actual scene","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera.PersistentId);
        var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};File.WriteAllBytes(projectFile,JsonSerializer.SerializeToUtf8Bytes(config,json));
        for(int i=0;i<3;i++) {
            var options=PlayerOptions.Parse(["--project",projectFile,"--ticks","3","--report",Path.Combine(output,"scene-player-"+i+".json")]);
            var report=PlayerRunner.Run(options,pluginRoot:plugins,visible:false);
            Check(report.ExitCode==0&&report.Tick>=3&&report.RenderedFrames>0&&report.ObjectCount==3&&report.ValidationErrors==0&&report.ValidationWarnings==0&&report.ShutdownErrors.Length==0,
                "Actual scene Player failed: "+report.Reason+" "+report.ExitCode);
            Check(saved.SequenceEqual(File.ReadAllBytes(scene)),"Player modified authoring scene");
            using var unlocked=new FileStream(generation,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);
        }
        File.WriteAllBytes(projectFile,JsonSerializer.SerializeToUtf8Bytes(config with{SceneCamera=Guid.NewGuid()},json));
        var bad=PlayerRunner.Run(PlayerOptions.Parse(["--project",projectFile,"--ticks","1","--report",Path.Combine(output,"scene-camera-missing.json")]),pluginRoot:plugins,visible:false);
        Check(bad.ExitCode==2&&bad.RenderedFrames==0&&bad.Modules.Length==0,"Invalid scene camera did not reject before GPU");
        return ["Real static-scene Player/explicit camera/3 restart cycles/validation 0/0", "Missing scene camera rejected before GPU/no reference fallback"];
    }
}
