using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe partial class Program
{
    private static int SceneUiJointProductTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int checks=0;
        void Pass(bool ok,string label){Check(ok,"D3-C full joint "+label);checks++;}
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent!;
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        using var index=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",configuration,"packages.json")));
        string product=index.RootElement.GetProperty("editor").GetString()!;DeploymentManifest.Validate(product);
        EnvironmentPackage environment;using(var library=EnvironmentPresetLibrary.LoadDeployment(product))environment=library.Package.CloneForAsset(Guid.NewGuid(),1);
        string importPath=Path.Combine(Directory.GetParent(plugins)!.Parent!.FullName,"NcmaImportKernel.dll");
        foreach(string file in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}){
            string label=file.Contains("ascii")?"ascii":"binary",folder=Path.Combine(output,label);Directory.CreateDirectory(folder);
            string fbx=Path.Combine(repo.FullName,"tests/assets/fbx",file);SkinFixture skin;
            using(var importer=new Ncma.Asset.Import.ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath)))))skin=new(folder,importer.LoadAndCopy(fbx,30));
            var(doc,camera,objects)=skin.Scene();var blocks=DerivedAssetCodec.Decode(skin.Plan.DerivedBytes);
            var rig=ModelPayloadCodec.DecodeSkeleton(blocks.Single(b=>b.AssetId==skin.Manifest.Skeleton!.Value).Data);
            var mesh=ModelPayloadCodec.DecodeMesh(blocks.Single(b=>b.AssetId==skin.Manifest.Meshes[0].Mesh).Data);var bounds=BindPoseMeshUploadData.Prepare(mesh,rig).Bounds;
            float scale=2.5f/Math.Max(bounds.Max.X-bounds.Min.X,Math.Max(bounds.Max.Y-bounds.Min.Y,bounds.Max.Z-bounds.Min.Z));var character=doc.World.FindObject(objects[0]);
            character.Set(Ncma.Runtime.TransformData.Identity with{Position=-(bounds.Min+bounds.Max)*(.5f*scale)+new Vector3(0,0,-3.5f),Scale=new(scale)});
            var clips=skin.Manifest.Clips.Select(id=>(Id:id,Clip:ModelPayloadCodec.DecodeClip(blocks.Single(b=>b.AssetId==id).Data).Clip)).ToArray();
            var animated=clips.OrderByDescending(c=>c.Clip.Tracks.Sum(t=>t.Keys.Count(k=>k.Value!=t.Keys[0].Value))).First();
            Pass(animated.Clip.Tracks.Any(t=>t.Keys.Any(k=>k.Value!=t.Keys[0].Value)),label+" real non-bind clip");character.Set(character.Get<Ncma.Animation.ClipPlaybackData>() with{ClipId=animated.Id});
            var descriptor=EnvironmentAssetDescriptor.FromPackage(skin.Project,environment);string envPath=Path.Combine(skin.Root,descriptor.Path);Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);File.WriteAllBytes(envPath,environment.CopyBytes());File.WriteAllBytes(Path.Combine(skin.Root,"assets/environment.ncenv"),descriptor.Encode());
            doc.World.CreateObject("IBL").Set(new EnvironmentLightingData(1,environment.AssetId,1,environment.ContentHash,1,.4f,true));
            doc.World.CreateObject("Post").Set(PostProcessData.Off with{Enabled=true,StackJson=PostProcessData.EncodeStack([Ncma.Rendering.Scene.PostProcessModules.Bloom(new(Levels:3,Threshold:.03f,Intensity:.8f))])});
            string runtime=Path.Combine(folder,"runtime");Directory.CreateDirectory(Path.Combine(runtime,"assets"));
            File.WriteAllBytes(Path.Combine(runtime,"assets/game.ncpak"),SceneAssetPreparation.CreateRuntimePackage(skin.Root,skin.Project,doc.CaptureSnapshot(),[]));
            var ui=UiDefinition.Create(Guid.NewGuid(),"Joint HUD");var rect=UiElement.Create(Guid.NewGuid(),"Tone-independent marker",UiKind.Rectangle,ui.Root)with{Layout=UiLayout.Fixed(8,8,64,40),Style=UiStyle.Default with{Fill=new(.2f,.4f,.8f,1)}};
            ui=ui with{Elements=[ui.Elements[0],rect]};File.WriteAllBytes(Path.Combine(skin.Root,"assets/hud.ncmaui"),UiCodec.Encode(ui));
            byte[] hud=RuntimeUiPackage.Encode(skin.Project,2,ui,[],[]);File.WriteAllBytes(Path.Combine(runtime,"assets/hud.ncupak"),hud);
            doc.World.CreateObject("Scene HUD").Set(SceneUiData.From(new(1,ui.AssetId,2,"assets/hud.ncupak",Convert.ToHexString(SHA256.HashData(hud)))));
            string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(runtime,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(runtime,"gameplay.deps.json"));
            SceneDocumentFiles.Save(doc,Path.Combine(runtime,"start.ncmascene"));File.WriteAllBytes(Path.Combine(runtime,"project.ncmaproject"),JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,skin.Project,label+" joint","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera,AssetPackage:"assets/game.ncpak"),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            // Valid value envelopes with intentionally unresolved exact dependencies. Keep the
            // healthy fixture unchanged and preserve every rejected project for diagnosis.
            void Bad(string name,string type,JsonElement data){var snapshot=doc.CaptureSnapshot();snapshot=snapshot with{Objects=snapshot.Objects.Select(o=>o with{Components=o.Components.Select(c=>c.TypeId==type?c with{Data=data}:c).ToArray()}).ToArray()};
                File.WriteAllBytes(Path.Combine(runtime,name+".ncmascene"),SceneDocumentCodec.Encode(snapshot));
                var config=ProjectContext.Load(Path.Combine(runtime,"project.ncmaproject")).Configuration with{StartupScene=name+".ncmascene"};File.WriteAllBytes(Path.Combine(runtime,name+".ncmaproject"),JsonSerializer.SerializeToUtf8Bytes(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));}
            Bad("bad-model",SkinnedMeshData.TypeId,doc.World.Components.Encode(character.Get<SkinnedMeshData>() with{MeshId=Guid.NewGuid()}));
            var envValue=doc.World.GetObjects().Single(o=>o.Has<EnvironmentLightingData>()).Get<EnvironmentLightingData>();Bad("bad-environment",EnvironmentLightingData.TypeId,doc.World.Components.Encode(envValue with{Generation=999}));
            var uiValue=doc.World.GetObjects().Single(o=>o.Has<SceneUiData>()).Get<SceneUiData>();Bad("bad-ui",SceneUiData.TypeId,doc.World.Components.Encode(uiValue with{Path="assets/absent.ncupak"}));
            var nextUi=ui with{Elements=[ui.Elements[0],rect with{Style=rect.Style with{Fill=new(.8f,.3f,.1f,1)}}]};byte[] nextHud=RuntimeUiPackage.Encode(skin.Project,3,nextUi,[],[]);File.WriteAllBytes(Path.Combine(runtime,"assets/hud-next.ncupak"),nextHud);
            Bad("next-ui",SceneUiData.TypeId,doc.World.Components.Encode(SceneUiData.From(new(1,ui.AssetId,3,"assets/hud-next.ncupak",Convert.ToHexString(SHA256.HashData(nextHud))))));
            var postValue=doc.World.GetObjects().Single(o=>o.Has<PostProcessData>()).Get<PostProcessData>();Bad("bad-post-module",PostProcessData.TypeId,doc.World.Components.Encode(postValue with{StackJson=PostProcessData.EncodeStack([new(Guid.NewGuid(),1,[])])}));
            Pass(!Directory.GetFiles(runtime,"*",SearchOption.AllDirectories).Any(p=>new[]{".fbx",".nca",".ncmeta",".nce",".ncenv",".ncmaui",".hlsl"}.Contains(Path.GetExtension(p))),"runtime no author sources");
            var pins=Directory.GetFiles(skin.Root,"*",SearchOption.AllDirectories).Append(fbx).Select(p=>new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.None)).ToArray();
            try{SceneUiPublicationProducts(repo.FullName,runtime,folder,Pass,"scene-ui-joint");}finally{foreach(var pin in pins)pin.Dispose();}
        }
        File.WriteAllText(Path.Combine(output,"joint-results.json"),JsonSerializer.Serialize(new{checks,configuration,fullShadingOracle=false,manualAccepted=false,wholeD3Accepted=false}));Console.WriteLine($"PASS full FBX IBL shadow Bloom HUD products: {checks}; {output}");return 0;
    }
}
