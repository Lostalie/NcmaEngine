using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Asset.Import;
using Ncma.Player.App;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class SkinnedHostChecks
{
    internal static string[] Run(string repository,string output,string plugins) {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        string native=Directory.GetParent(Directory.GetParent(plugins)!.FullName)!.FullName;
        foreach(string fixture in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}) {
            string root=Path.Combine(output,"FBX Player "+fixture);Directory.CreateDirectory(Path.Combine(root,"assets"));
            string path=Path.Combine(native,"NcmaImportKernel.dll"),source=Path.Combine(repository,"tests/assets/fbx",fixture);
            ModelImportPlan plan;Guid project=Guid.NewGuid(),model=Guid.NewGuid();
            using(var importer=new ImportKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))) {
                var imported=importer.LoadAndCopy(source,30);
                plan=ModelImportPlanner.Build(imported,new(1,model,AssetKind.Character,"assets/model.fbx",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),"ufbx",1,new(1,30,true),[],[],null),project,false);
            }
            // Only derived NCA and metadata saved. Source FBX/import DLL is NOT part of this runtime project.
            string generation=Path.Combine(root,plan.Record.Generation!.RelativePath);Directory.CreateDirectory(Path.GetDirectoryName(generation)!);File.WriteAllBytes(generation,plan.DerivedBytes);
            File.WriteAllBytes(Path.Combine(root,"assets/model.fbx.ncmeta"),AssetRecordCodec.Encode(plan.Record));
            var manifest=ModelAssetManifestCodec.ValidateBundle(plan.DerivedBytes,plan.Record);
            var document=new SceneDocument("Imported FBX Player",RenderComponentRegistry.CreateRegistry(),SceneRenderValidation.RequireComposition);
            int characters=0;
            foreach(var mesh in manifest.Meshes) {
                var obj=document.World.CreateObject("Character "+characters++);obj.Set(TransformData.Identity with{Position=new(0,0,-3)});
                obj.Set(new SkinnedMeshData(model,mesh.Mesh,manifest.Skeleton!.Value,mesh.Materials,true,true,uint.MaxValue));
                obj.Set(new ClipPlaybackData(manifest.Clips[0],true,true,1,0));
            }
            var camera=document.World.CreateObject("Camera");camera.Set(TransformData.Identity);camera.Set(CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=4});
            var light=document.World.CreateObject("Light");light.Set(TransformData.Identity);light.Set(DirectionalLightData.Default);
            string scene=Path.Combine(root,"start.ncmascene"),projectFile=Path.Combine(root,"scene.ncmaproject");SceneDocumentFiles.Save(document,scene);byte[] saved=File.ReadAllBytes(scene);
            string assembly=Assembly.GetExecutingAssembly().Location;File.Copy(assembly,Path.Combine(root,"gameplay.dll"));File.Copy(Path.ChangeExtension(assembly,".deps.json"),Path.Combine(root,"gameplay.deps.json"));
            File.WriteAllBytes(projectFile,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,project,"FBX scene","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera.PersistentId),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            for(int i=0;i<3;i++) {
                var options=PlayerOptions.Parse(["--project",projectFile,"--ticks","6","--max-runtime-seconds","20","--report",Path.Combine(output,fixture+"-player-"+i+".json")]);
                var report=PlayerRunner.Run(options,pluginRoot:plugins,visible:false);
                Check(report.ExitCode==0&&report.Tick>=6&&report.RenderedFrames>0&&report.ObjectCount==characters+2&&report.ValidationErrors==0&&report.ValidationWarnings==0&&report.ShutdownErrors.Length==0,
                    "FBX runtime scene Player failed: "+report.Reason+" / "+report.ExitCode+" / "+string.Join(',',report.ShutdownErrors));
                Check(saved.SequenceEqual(File.ReadAllBytes(scene)),"Player changed animation authoring data");
                using var unlocked=new FileStream(generation,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);
            }
            Check(!File.Exists(Path.Combine(root,"assets/model.fbx")),"Player accidentally deployed/reparsed FBX source");
        }
        return ["ASCII/binary FBX import->NCA save/restart->real animated Player, 3 cycles each, no source FBX runtime, validation 0/0"];
    }
}
