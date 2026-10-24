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
        Guid obj=document.CreateObject("Graph root recovery actor");document.SetComponent(obj, TransformData.Identity with{Position=new(-1,0,-3)});
        document.SetComponent(obj, new SkinnedMeshData(actor.Model,actor.Manifest.Meshes[0].Mesh,graph.SkeletonId,actor.Manifest.Meshes[0].Materials,true,true,uint.MaxValue));
        document.SetComponent(obj, new AnimatorData(graph.AssetId,graph.SkeletonId));document.SetComponent(obj, new RootMotionData(0));document.SetComponent(obj, CharacterData.Default with{Controlled=false,Gravity=0});
        Guid floor=document.CreateObject("Solver floor");document.SetComponent(floor, TransformData.Identity with{Position=new(0,-1.1f,-3)});document.SetComponent(floor, new BoxColliderData(20,.2f,20,1000,1,false));
        foreach(var(name,type)in new[]{("Private recovery state","RecoveryProbe"),("HUD recovery state","HudProbe")}){
            Guid probe=document.CreateObject(name);document.SetComponent(probe, TransformData.Identity);document.SetBindings(probe,[new(Guid.NewGuid(),"Ncma.Gameplay.UiFixture."+type,true,[])]);
        }
        document.ValidateAuthoring();return document;
    }
}
