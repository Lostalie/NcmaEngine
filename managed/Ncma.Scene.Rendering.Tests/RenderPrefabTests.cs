using Ncma.Animation;
using Ncma.Scene.Prefabs;

internal static partial class Program
{
    private static void PrefabRenderReferences()
    {
        var scene=Document();Guid skin=scene.CreateObject("Character");scene.SetComponent(skin, Ncma.Runtime.TransformData.Identity);
        var mesh=new Ncma.Scene.Rendering.SkinnedMeshData(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),true,true,uint.MaxValue);
        scene.SetComponent(skin, mesh);var clip=new ClipPlaybackData(Guid.NewGuid(),true,true,1,0);scene.SetComponent(skin, clip);
        scene.SetComponent(skin, new Ncma.Scene.Rendering.MaterialOverrideData(Guid.Empty,true,.2f,.5f));
        var policy=Ncma.Scene.Rendering.RenderPrefabPolicy.Create(scene.Components);
        var document=PrefabTemplates.Extract(scene,[skin],new(scene,[skin],()=>true),policy,Guid.NewGuid(),"Hero",new(0,0,0),scene.Revision,scene.SessionId);
        Check(document.Dependencies.Length==5&&document.Dependencies.Single(d=>d.AssetId==clip.ClipId).Kind=="Clip"&&document.ObjectReferences.Length==0);
        var preview=PrefabTemplates.PreviewExpansion(document,policy,new(5,0,-3),[]);
        var value=new Ncma.Scene.SceneDocument("Preview",scene.Components,Ncma.Scene.Rendering.SceneRenderValidation.RequireComposition);value.RestoreSnapshot(new(1,"Preview",preview.Objects));
        Check(value.GetComponent<Ncma.Scene.Rendering.SkinnedMeshData>(value.GetObjectIds()[0])==mesh&&value.GetComponent<ClipPlaybackData>(value.GetObjectIds()[0])==clip);
        Check(value.GetComponent<Ncma.Runtime.TransformData>(value.GetObjectIds()[0]).Position==new System.Numerics.Vector3(5,0,-3));
    }
    private static void PrefabRenderComposition()
    {
        var scene=Document();Guid obj=Geometry(scene);var policy=Ncma.Scene.Rendering.RenderPrefabPolicy.Create(scene.Components);
        var document=PrefabTemplates.Extract(scene,[obj],new(scene,[obj],()=>true),policy,Guid.NewGuid(),"Static",new(0,0,0),scene.Revision,scene.SessionId);
        Check(document.Dependencies.Length==2);byte[] before=scene.CaptureBytes();
        var objects=(PrefabObject[])document.Objects.Clone();objects[0]=objects[0] with{Components=objects[0].Components.Where(c=>c.TypeId!="ncma.transform").ToArray()};
        Reject(()=>PrefabDocumentCodec.Encode(document with{Objects=objects},policy));
        objects=(PrefabObject[])document.Objects.Clone();objects[0]=objects[0] with{Components=objects[0].Components.Append(new(ClipPlaybackData.TypeId,1,scene.Components.Encode(new ClipPlaybackData(Guid.NewGuid(),true,true,1,0)))).ToArray()};
        Reject(()=>PrefabDocumentCodec.Encode(document with{Objects=objects},policy));Check(before.SequenceEqual(scene.CaptureBytes()));
    }
}
