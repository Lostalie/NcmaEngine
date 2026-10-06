using V3=System.Numerics.Vector3;
using Q=System.Numerics.Quaternion;
using M4=System.Numerics.Matrix4x4;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Characters;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Samples;

public sealed record ActionSampleResult(string Root,string Project,Guid ProjectId,Guid[] Actors,Guid[] Targets,Guid Camera);
// Trusted off-frame sample generation, not a cook/import/Agent tool. Never overwrites an existing sample.
public static class ActionSample
{
    public static ActionSampleResult Create(string parent,string gameplayAssembly,int characters=1)
    {
        if(characters is <0 or >32||!File.Exists(gameplayAssembly)||!File.Exists(Path.ChangeExtension(gameplayAssembly,".deps.json")))throw new ArgumentException("Sample needs 0..32 characters and a built gameplay assembly/deps.");
        parent=Path.GetFullPath(parent);
        for(var ancestor=new DirectoryInfo(parent);ancestor is not null;ancestor=ancestor.Parent)
            if(ancestor.Exists&&ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Sample output ancestry must not traverse a reparse point.");
        Directory.CreateDirectory(parent);string root=Path.Combine(parent,"action-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Guid Id(int index)=>Guid.Parse($"{index:x8}-dddd-4444-8888-000000000001");
        Guid project=Id(1),model=Id(2),mesh=Id(3),rig=Id(4),slots=Id(5),definition=Id(6),cameraId=Id(7);Guid[] clips=[Id(10),Id(11),Id(12),Id(13)];
        var identity=new ImportTransform(V3.Zero,Q.Identity,V3.One);
        var skeleton=new SkeletonPayload([new("root",-1,identity),new("hand",0,identity with{Position=V3.UnitY})]);
        ImportVertex Vertex(float x,float y,int bone)=>new(new(x,y,0),V3.UnitZ,default,new((ushort)bone,0,0,0),new(1,0,0,0));
        var vertices=new[]{Vertex(-.3f,0,0),Vertex(.3f,0,0),Vertex(-.3f,1.5f,0),Vertex(.3f,1.5f,0),Vertex(.1f,1.1f,1),Vertex(1,1.1f,1),Vertex(.1f,1.3f,1),Vertex(1,1.3f,1)};
        var payload=new MeshPayload(true,2,vertices,[],[0,1,2,2,1,3,4,5,6,6,5,7],[0,0,0,0],
            [new(0,AssetMatrices.EncodeColumnMajor(M4.Identity)),new(1,AssetMatrices.EncodeColumnMajor(M4.CreateTranslation(0,-1,0)))],1);
        ClipPayload Clip(int i) {
            double duration=i==2?.6:i==3?.4:1;
            V3 motion=i==2?new(0,0,-.4f):i==3?new(.8f,0,-.1f):V3.Zero;
            float angle=new[]{.05f,.3f,-1.1f,.6f}[i];
            return new(2,new(new[]{"Idle","Run","Attack","Dodge"}[i],duration,
                [new(0,[new(0,identity),new(duration,identity with{Position=motion})]),
                 new(1,[new(0,identity with{Position=V3.UnitY}),new(duration/2,identity with{Position=V3.UnitY,Rotation=Q.CreateFromAxisAngle(V3.UnitZ,angle)}),new(duration,identity with{Position=V3.UnitY})])]));
        }
        var settings=new ImportSettings(1,30,true);string sourceHash=Convert.ToHexString(SHA256.HashData("Ncma procedural four-action sample v1"u8));
        var manifest=new ModelAssetManifest(1,model,false,sourceHash,settings,rig,[new(mesh,slots)],clips);
        var blocks=new List<DerivedAssetBlock>{new(model,AssetKind.Character,ModelAssetManifestCodec.Encode(manifest)),new(rig,AssetKind.Skeleton,ModelPayloadCodec.Encode(skeleton)),
            new(mesh,AssetKind.SkinnedMesh,ModelPayloadCodec.Encode(payload)),new(slots,AssetKind.MaterialSet,ModelPayloadCodec.Encode(new MaterialSlotsPayload(["Procedural diagnostic"]))) };
        blocks.AddRange(clips.Select((id,i)=>new DerivedAssetBlock(id,AssetKind.Clip,ModelPayloadCodec.Encode(Clip(i)))));
        byte[] data=DerivedAssetCodec.Encode(blocks);string hash=Convert.ToHexString(SHA256.HashData(data)),relative=$"out/assets/{project:N}/{model:N}/1-{hash}.nca";
        void Write(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(bytes);}
        Write(Path.Combine(root,relative),data);
        var sub=new List<SubassetRecord>{new(mesh,AssetKind.SkinnedMesh,"mesh/0","Diagnostic body/hand",false),new(rig,AssetKind.Skeleton,"rig/0","Two bones",false),new(slots,AssetKind.MaterialSet,"materials/0","Material",false)};
        sub.AddRange(clips.Select((id,i)=>new SubassetRecord(id,AssetKind.Clip,$"clip/{i}",new[]{"Idle","Run","Attack","Dodge"}[i],false)));
        // Provenance explicitly procedural. No raw FBX is produced or required; this is not cold-source import.
        Write(Path.Combine(root,"assets/procedural.fbx.ncmeta"),AssetRecordCodec.Encode(new(1,model,AssetKind.Character,"assets/procedural.fbx",sourceHash,"procedural_sample",1,settings,sub.ToArray(),[],new(1,hash,relative))));
        var doc=new SceneDocument("Procedural action diagnostic (not user FBX acceptance)",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);
        var actors=new List<Guid>();var targets=new List<Guid>();
        for(int i=0;i<characters;i++) {
            var actor=doc.World.CreateObject("Character "+i,Id(100+i));actor.Set(TransformData.Identity with{Position=new(i*3,.1f,0)});actor.Set(CharacterData.Default with{Controlled=i==0});
            actor.Set(new SkinnedMeshData(model,mesh,rig,slots,true,true,uint.MaxValue));actor.Set(new ClipPlaybackData(clips[0],true,true,1,0));actor.Set(new RootMotionData(0));actor.Set(new HealthData(100,100));
            actor.Set(new ActionDefinitionData(definition,clips[0],clips[1],clips[2],clips[3],.2f,.5f,.2f,.7f,.5f,.95f,0,1,25,2.5f,2,6));actors.Add(actor.PersistentId);
            var target=doc.World.CreateObject("Damage target "+i,Id(200+i));target.Set(TransformData.Identity with{Position=new(i*3,1,-2)});target.Set(new BoxColliderData(.3f,.9f,.3f,1000,2,false));target.Set(new HealthData(100,100));targets.Add(target.PersistentId);
        }
        if(characters!=0){var floor=doc.World.CreateObject("Physics floor",Id(8));floor.Set(TransformData.Identity with{Position=new(45,-.5f,0)});floor.Set(new BoxColliderData(100,.5f,20,1000,1,false));}
        var camera=doc.World.CreateObject("Camera",cameraId);camera.Set(TransformData.Identity with{Position=new(0,3,7)});camera.Set(CameraData.Default);
        if(characters!=0)camera.Set(new FollowCameraData(actors[0],0,3,7,1));
        var light=doc.World.CreateObject("Sun",Id(9));light.Set(TransformData.Identity);light.Set(DirectionalLightData.Default);
        SceneDocumentFiles.Save(doc,Path.Combine(root,"start.ncmascene"));
        Write(Path.Combine(root,"assets/action.ncpak"),SceneAssetPreparation.CreateRuntimePackage(root,project,doc.CaptureSnapshot(),[]));
        string assemblyName=Path.GetFileName(gameplayAssembly);Write(Path.Combine(root,assemblyName),File.ReadAllBytes(gameplayAssembly));Write(Path.Combine(root,Path.ChangeExtension(assemblyName,".deps.json")),File.ReadAllBytes(Path.ChangeExtension(gameplayAssembly,".deps.json")));
        string config=Path.Combine(root,"action.ncmaproject");Write(config,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,project,"Procedural M4 Action Sample","start.ncmascene",assemblyName,"Direct3D11",[],characters!=0,cameraId){AssetPackage="assets/action.ncpak"},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        Write(Path.Combine(root,"README.txt"),System.Text.Encoding.UTF8.GetBytes("Procedural diagnostic only, not real FBX acceptance. WASD movement, Space jump, J attack, K dodge. Review Character Debug's exact Play/object/audience UUIDs before granting MCP reads. Targets/floor are numerical colliders, not rendered environment meshes. No old scene formats. No Python gameplay or native World.\n"));
        return new(root,config,project,actors.ToArray(),targets.ToArray(),cameraId);
    }
}
