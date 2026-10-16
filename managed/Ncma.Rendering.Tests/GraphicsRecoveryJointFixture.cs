using System.Numerics;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Characters;
using Ncma.Rendering;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    // Keep the original real FBX actor/clip unmodified. An explicitly synthetic second
    // actor supplies an independently known nonzero root delta (0.5 m/s), not fake FBX data.
    private static SceneDocument AddRecoveryJointCharacter(SkinFixture imported,SceneDocument source,string folder)
    {
        var actor=new SkinFixture(Path.Combine(folder,"root-author"));
        actor.Plan=ModelImportPlanner.Build(actor.Source,actor.Plan.Record with{SourcePath="assets/root.fbx"},imported.Project,false);
        string generation=Path.Combine(imported.Root,actor.Plan.Record.Generation!.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(generation)!);File.WriteAllBytes(generation,actor.Plan.DerivedBytes);
        File.WriteAllBytes(Path.Combine(imported.Root,"assets/root.fbx.ncmeta"),AssetRecordCodec.Encode(actor.Plan.Record));
        var graph=AnimatorGraph(actor);File.WriteAllBytes(Path.Combine(imported.Root,"assets/root.ncmaanim"),AnimationGraphCodec.Encode(graph));
        var document=new SceneDocument("Full recovery joint",CharacterComponents.Register(RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry())),CharacterComponents.RequireComposition);
        document.RestoreSnapshot(source.CaptureSnapshot());
        global::Ncma.Runtime.Entity obj=document.World.CreateEntity("Graph root recovery actor");document.World.Set(obj, TransformData.Identity with{Position=new(-1,0,-3)});
        document.World.Set(obj, new SkinnedMeshData(actor.Model,actor.Manifest.Meshes[0].Mesh,graph.SkeletonId,actor.Manifest.Meshes[0].Materials,true,true,uint.MaxValue));
        document.World.Set(obj, new AnimatorData(graph.AssetId,graph.SkeletonId));document.World.Set(obj, new RootMotionData(0));document.World.Set(obj, CharacterData.Default with{Controlled=false,Gravity=0});
        global::Ncma.Runtime.Entity floor=document.World.CreateEntity("Solver floor");document.World.Set(floor, TransformData.Identity with{Position=new(0,-1.1f,-3)});document.World.Set(floor, new BoxColliderData(20,.2f,20,1000,1,false));
        foreach(var(name,type)in new[]{("Private recovery state","RecoveryProbe"),("HUD recovery state","HudProbe")}){
            global::Ncma.Runtime.Entity probe=document.World.CreateEntity(name);document.World.Set(probe, TransformData.Identity);document.SetBindings(document.World.GetUuid(probe),[new(Guid.NewGuid(),"Ncma.Gameplay.UiFixture."+type,true,[])]);
        }
        document.ValidateAuthoring();return document;
    }
}
