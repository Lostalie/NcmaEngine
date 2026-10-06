using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Prefabs;

internal static class PrefabTests
{
    private static void Check(bool value,string label="Prefab assertion failed"){if(!value)throw new InvalidOperationException(label);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException){return;}throw new Exception("Invalid prefab operation accepted");}
    private sealed record Fixture(SceneDocument Scene,Guid[] Ids,PrefabPolicy Policy,PrefabExtractionScope Scope,Guid Asset);
    private static Fixture Make(Func<BehaviourBindingData,bool>? trusted=null)
    {
        var registry=ComponentRegistry.CreateDefault();
        registry.Register<Link>("game.link",1,"""{"type":"object","additionalProperties":false,"required":["target","asset","label"],"properties":{"target":{"type":"string"},"asset":{"type":"string"},"label":{"type":"string"}}}""",v=>v);
        registry.Register<RuntimeOnly>("game.runtime_only",1,"""{"type":"object","additionalProperties":false,"required":["handle"],"properties":{"handle":{"type":"integer"}}}""",v=>v);
        var scene=new SceneDocument("Source",registry);var a=scene.World.CreateObject("A");var b=scene.World.CreateObject("B");var empty=scene.World.CreateObject("Empty");Guid asset=Guid.NewGuid();
        a.Set(new TransformData(new(3,4,5),Quaternion.CreateFromYawPitchRoll(.4f,.2f,-.3f),new(2,.5f,3)));b.Set(new TransformData(new(-2,7,1),Quaternion.Identity,Vector3.One));
        a.Set(new Link(b.PersistentId,asset,b.PersistentId.ToString("D")));
        scene.SetBindings(a.PersistentId,[new(Guid.NewGuid(),"Game.Known",true,[new("Speed",ExportKind.Float,.75)])]);
        var policy=new PrefabPolicy(registry,trusted??(binding=>binding.TypeName=="Game.Known"&&binding.Exports.Length==1&&binding.Exports[0].Name=="Speed"&&binding.Exports[0].Kind==ExportKind.Float))
            .AllowComponent("game.link",1,[new("/target",PrefabReferenceRole.Object),new("/asset",PrefabReferenceRole.Asset,"StaticMesh")]);
        Guid[] ids=[a.PersistentId,b.PersistentId,empty.PersistentId];return new(scene,ids,policy,new(scene,ids,()=>true),asset);
    }
    private static PrefabDocument Extract(Fixture f)=>PrefabTemplates.Extract(f.Scene,f.Ids,f.Scope,f.Policy,Guid.NewGuid(),"Template",new(1,2,3),f.Scene.Revision,f.Scene.World.Identity);
    public static IEnumerable<(string,Action)> Cases()
    {
        yield return("M3.7-A exact read-only extraction and declared references",()=> {
            var f=Make();byte[] before=f.Scene.CaptureBytes();ulong rev=f.Scene.Revision;Guid world=f.Scene.World.Identity;
            var prefab=Extract(f);Check(prefab.Objects.Length==3&&prefab.Objects.All(o=>!f.Ids.Contains(o.Id)));Check(prefab.ObjectReferences.Length==1&&prefab.Dependencies.Single()==new PrefabDependency(f.Asset,"StaticMesh"));
            var a=prefab.Objects.Single(o=>o.Name=="A");var b=prefab.Objects.Single(o=>o.Name=="B");var link=f.Scene.World.Components.Decode<Link>(a.Components.Single(c=>c.TypeId=="game.link"));
            Check(link.Target==b.Id&&link.Asset==f.Asset&&link.Label==f.Ids[1].ToString("D"),"Explicit remap changed unrelated UUID-looking text");
            Check(a.Behaviours[0].Id!=f.Scene.GetBindings(f.Ids[0])[0].Id&&a.Behaviours[0].Exports[0].Value==.75);
            Check(before.SequenceEqual(f.Scene.CaptureBytes())&&f.Scene.Revision==rev&&f.Scene.World.Identity==world,"Read-only extraction changed live scene/handles");
            Check(prefab.Objects.Single(o=>o.Name=="Empty").Components.Length==0);
        });
        yield return("M3.7-A source scope, stale identities, foreign thread and callback writes rejected",()=> {
            var f=Make();byte[] before=f.Scene.CaptureBytes();ulong rev=f.Scene.Revision;Guid world=f.Scene.World.Identity;
            void Run(Guid[] ids,PrefabExtractionScope scope,ulong revision,Guid identity)=>PrefabTemplates.Extract(f.Scene,ids,scope,f.Policy,Guid.NewGuid(),"Template",new(0,0,0),revision,identity);
            Reject(()=>Run(f.Ids,new(f.Scene,[f.Ids[0]],()=>true),rev,world));Reject(()=>Run(f.Ids,new(f.Scene,f.Ids,()=>false),rev,world));
            Reject(()=>Run(f.Ids,new(new SceneDocument(),f.Ids,()=>true),rev,world));Reject(()=>Run(f.Ids,f.Scope,rev+1,world));Reject(()=>Run(f.Ids,f.Scope,rev,Guid.NewGuid()));
            Reject(()=>Run([f.Ids[0],f.Ids[0]],f.Scope,rev,world));Reject(()=>Run([],f.Scope,rev,world));Reject(()=>Run([Guid.Empty],f.Scope,rev,world));
            Reject(()=>Run(f.Ids,new(f.Scene,f.Ids,()=>{f.Scene.World.CreateObject("Callback mutation");return true;}),rev,world));
            Task.Run(()=>Reject(()=>Run(f.Ids,f.Scope,rev,world))).GetAwaiter().GetResult();Check(before.SequenceEqual(f.Scene.CaptureBytes()));
        });
        yield return("M3.7-A outside references, unknown components and untrusted behaviours rejected",()=> {
            var f=Make();Reject(()=>PrefabTemplates.Extract(f.Scene,[f.Ids[0]],f.Scope,f.Policy,Guid.NewGuid(),"Template",new(0,0,0),f.Scene.Revision,f.Scene.World.Identity));
            f.Scene.World.FindObject(f.Ids[2]).Set(new RuntimeOnly(123));Reject(()=>Extract(f));
            var noBindings=Make();var policy=new PrefabPolicy(noBindings.Scene.World.Components).AllowComponent("game.link",1,[new("/target",PrefabReferenceRole.Object),new("/asset",PrefabReferenceRole.Asset,"StaticMesh")]);
            Reject(()=>PrefabTemplates.Extract(noBindings.Scene,noBindings.Ids,noBindings.Scope,policy,Guid.NewGuid(),"Template",new(0,0,0),noBindings.Scene.Revision,noBindings.Scene.World.Identity));
            Fixture? mutating=null;mutating=Make(_=>{mutating!.Scene.SetBindings(mutating.Ids[0],[]);return true;});byte[] before=mutating.Scene.CaptureBytes();Reject(()=>Extract(mutating));Check(before.SequenceEqual(mutating.Scene.CaptureBytes()));
        });
        yield return("M3.7-A strict JSON v1, duplicate/missing/unknown keys and removed formats rejected",()=> {
            var f=Make();var prefab=Extract(f);byte[] bytes=PrefabDocumentCodec.Encode(prefab,f.Policy);
            void Bad(Action<JsonNode> change){var node=JsonNode.Parse(bytes)!;change(node);Reject(()=>PrefabDocumentCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()),f.Policy));}
            Bad(n=>n["version"]=2);Bad(n=>n["version"]=0);Bad(n=>n["baseVersion"]=0);Bad(n=>n["parent"]=Guid.NewGuid().ToString());Bad(n=>n["basePrefab"]=Guid.NewGuid().ToString());
            Bad(n=>n.AsObject().Remove("pivot"));Bad(n=>n["pivot"]!.AsObject().Remove("z"));Bad(n=>n["objects"]![0]!["children"]=new JsonArray());
            string text=Encoding.UTF8.GetString(bytes);Reject(()=>PrefabDocumentCodec.Decode(Encoding.UTF8.GetBytes(text.Replace("\"version\":1","\"version\":1,\"version\":1",StringComparison.Ordinal)),f.Policy));
            Reject(()=>PrefabDocumentCodec.Decode(f.Scene.CaptureBytes(),f.Policy));Reject(()=>PrefabDocumentCodec.Decode([],f.Policy));Reject(()=>PrefabDocumentCodec.Decode(new byte[PrefabDocumentCodec.MaxBytes+1],f.Policy));
            foreach(string path in new[]{"Hero.ncscene","Hero.ncmascene","Hero.prefab","Hero.NCPREFAB"})Reject(()=>PrefabDocumentCodec.RequireExtension(path));Check(PrefabDocumentCodec.RequireExtension("Hero.ncprefab")=="Hero.ncprefab");
        });
        yield return("M3.7-A exact reference manifest, UUID namespaces and invalid registered payloads",()=> {
            var f=Make();var prefab=Extract(f);void Bad(PrefabDocument p)=>Reject(()=>PrefabDocumentCodec.Encode(p,f.Policy));
            Bad(prefab with{AssetId=prefab.Objects[0].Id});Bad(prefab with{AssetId=Guid.Empty});Bad(prefab with{AssetId=prefab.Objects[0].Behaviours[0].Id});
            Bad(prefab with{ObjectReferences=[]});Bad(prefab with{ObjectReferences=[prefab.ObjectReferences[0] with{TargetId=Guid.NewGuid()}]});Bad(prefab with{ObjectReferences=[prefab.ObjectReferences[0],prefab.ObjectReferences[0]]});
            Bad(prefab with{Dependencies=[]});Bad(prefab with{Dependencies=[prefab.Dependencies[0] with{Kind="SkinnedMesh"}]});Bad(prefab with{Dependencies=[prefab.Dependencies[0],prefab.Dependencies[0]]});
            var objects=(PrefabObject[])prefab.Objects.Clone();objects[0]=objects[0] with{Components=[new("game.not_registered",1,JsonSerializer.SerializeToElement(new{value=1}))]};Bad(prefab with{Objects=objects});
            objects=(PrefabObject[])prefab.Objects.Clone();objects[0]=objects[0] with{Components=[new("ncma.transform",1,JsonSerializer.SerializeToElement(new{position=new{x=0,y=0,z=0},rotation=new{x=0,y=0,z=0,w=0},scale=new{x=1,y=1,z=1}}))]};Bad(prefab with{Objects=objects});
            Bad(prefab with{Pivot=new(float.NaN,0,0)});Bad(prefab with{Pivot=new(1000001,0,0)});Bad(prefab with{Name=new string('x',257)});
        });
        yield return("M3.7-A flat expansion preview remaps identities and preserves matrix order",()=> {
            var f=Make();var prefab=Extract(f);var preview=PrefabTemplates.PreviewExpansion(prefab,f.Policy,new(10,20,30),f.Ids);
            var second=PrefabTemplates.PreviewExpansion(prefab,f.Policy,new(0,0,0),preview.Objects.Select(o=>o.Id));
            Check(preview.InstanceId!=second.InstanceId&&!preview.Objects.Select(o=>o.Id).Intersect(second.Objects.Select(o=>o.Id)).Any());
            Check(preview.Objects.All(o=>!prefab.Objects.Any(t=>t.Id==o.Id))&&preview.Objects.Single(o=>o.Name=="Empty").Components.Length==0);
            var a=preview.Objects.Single(o=>o.Name=="A");var b=preview.Objects.Single(o=>o.Name=="B");Check(f.Policy.Components.Decode<Link>(a.Components.Single(c=>c.TypeId=="game.link")).Target==b.Id);
            var original=f.Scene.World.FindObject(f.Ids[0]).Get<TransformData>();var placed=f.Policy.Components.Decode<TransformData>(a.Components.Single(c=>c.TypeId=="ncma.transform"));
            Check(placed.Position==new Vector3(12,22,32)&&placed.Scale==original.Scale&&placed.Rotation==original.Rotation);
            Matrix4x4 Matrix(TransformData t)=>Matrix4x4.CreateScale(t.Scale)*Matrix4x4.CreateFromQuaternion(t.Rotation)*Matrix4x4.CreateTranslation(t.Position);
            var expected=Vector3.Transform(new(2,-3,4),Matrix(original)*Matrix4x4.CreateTranslation(new Vector3(9,18,27)));
            Check(Vector3.Distance(expected,Vector3.Transform(new(2,-3,4),Matrix(placed)))<1e-4,"Template * translation matrix order");
            var expanded=new SceneDocument("Preview",f.Policy.Components);expanded.RestoreSnapshot(new(1,"Preview",preview.Objects));expanded.World.FindObject(a.Id).Destroy();Check(expanded.World.Count==2&&expanded.World.FindObject(b.Id).PersistentId==b.Id,"Flat deletion unexpectedly removed a related object");
            Check(f.Scene.World.Count==3&&preview.OperationCount==7); // create3 + Transform2 + link1 + bindings1
        });
        yield return("M3.7-A owned codec/preview copies and stable cold bytes",()=> {
            var f=Make();var prefab=Extract(f);byte[] encoded=PrefabDocumentCodec.Encode(prefab,f.Policy);var decoded=PrefabDocumentCodec.Decode(encoded,f.Policy);
            Check(encoded.SequenceEqual(PrefabDocumentCodec.Encode(decoded,f.Policy)));
            var preview=PrefabTemplates.PreviewExpansion(prefab,f.Policy,new(0,0,0),[]);preview.Objects[0].Behaviours[0].Exports[0]=new("Tampered",ExportKind.Boolean,1);
            prefab.Objects[0].Behaviours[0].Exports[0]=new("Changed",ExportKind.Boolean,1);Check(decoded.Objects[0].Behaviours[0].Exports[0].Name=="Speed");
            string directory=Path.Combine(AppContext.BaseDirectory,"prefab-codec-work",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);string file=Path.Combine(directory,"Hero.ncprefab");
            File.WriteAllBytes(file,encoded);var restarted=PrefabDocumentCodec.Decode(File.ReadAllBytes(file),f.Policy);Check(encoded.SequenceEqual(PrefabDocumentCodec.Encode(restarted,f.Policy))); // Test fixture IO only, not a public save entry.
        });
        yield return("M3.7-A policy registration is explicit, immutable and rejects array/nested paths",()=> {
            var f=Make();var registry=f.Scene.World.Components;
            Reject(()=>new PrefabPolicy(registry).AllowComponent("game.unknown",1,[]));Reject(()=>new PrefabPolicy(registry).AllowComponent("game.link",2,[]));
            foreach(string path in new[]{"target","/0","/items/0","/~0","/target/"})Reject(()=>new PrefabPolicy(registry).AllowComponent("game.link",1,[new(path,PrefabReferenceRole.Object)]));
            Reject(()=>new PrefabPolicy(registry).AllowComponent("game.link",1,[new("/target",PrefabReferenceRole.Object,"Asset")]));Reject(()=>new PrefabPolicy(registry).AllowComponent("game.link",1,[new("/asset",PrefabReferenceRole.Asset,"Prefab")]));
            Reject(()=>new PrefabPolicy(registry).AllowComponent("game.link",1,[new("/target",PrefabReferenceRole.Object),new("/target",PrefabReferenceRole.Object)]));
            _=Extract(f);Reject(()=>f.Policy.AllowComponent("game.runtime_only",1,[]));
        });
        yield return("M3.7-A codec copies before trusted callbacks and accepts JSON property reordering",()=> {
            var original=Make();var prefab=Extract(original);bool invoked=false;
            var checking=new PrefabPolicy(original.Scene.World.Components,_=>{invoked=true;prefab.Objects[0]=prefab.Objects[0] with{Name="Mutated caller DTO"};return true;})
                .AllowComponent("game.link",1,[new("/target",PrefabReferenceRole.Object),new("/asset",PrefabReferenceRole.Asset,"StaticMesh")]);
            byte[] bytes=PrefabDocumentCodec.Encode(prefab,checking);Check(invoked&&PrefabDocumentCodec.Decode(bytes,checking).Objects[0].Name=="A","Encoder exposed caller mutations after validation");
            var node=JsonNode.Parse(bytes)!;var data=node["objects"]![0]!["components"]![0]!["data"]!.AsObject();
            var properties=data.Reverse().Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,p.Value?.DeepClone())).ToArray();data.Clear();foreach(var p in properties)data.Add(p.Key,p.Value);
            _=PrefabDocumentCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()),checking);
        });
        yield return("M3.7-A empty-container templates do not require Transform registration",()=> {
            var registry=new ComponentRegistry();var scene=new SceneDocument("Empty",registry);var obj=scene.World.CreateObject("Logic container");var policy=new PrefabPolicy(registry);
            var prefab=PrefabTemplates.Extract(scene,[obj.PersistentId],new(scene,[obj.PersistentId],()=>true),policy,Guid.NewGuid(),"Empty template",new(0,0,0),scene.Revision,scene.World.Identity);
            var preview=PrefabTemplates.PreviewExpansion(prefab,policy,new(10,20,30),[]);Check(preview.OperationCount==1&&preview.Objects[0].Components.Length==0);
        });
        yield return("M3.7-A object, operation and placement budgets reject detached partial plans",()=> {
            var f=Make();var prefab=Extract(f);Reject(()=>PrefabDocumentCodec.Encode(prefab with{Objects=Enumerable.Repeat(prefab.Objects[0],17).ToArray()},f.Policy));
            Reject(()=>PrefabTemplates.PreviewExpansion(prefab,f.Policy,new(0,float.PositiveInfinity,0),[]));Reject(()=>PrefabTemplates.PreviewExpansion(prefab,f.Policy,new(0,0,0),[Guid.Empty]));Reject(()=>PrefabTemplates.PreviewExpansion(prefab,f.Policy,new(0,0,0),Enumerable.Repeat(Guid.NewGuid(),65537)));
            var registry=ComponentRegistry.CreateDefault();const string schema="""{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"integer"}}}""";
            registry.Register<C0>("game.c0",1,schema,v=>v);registry.Register<C1>("game.c1",1,schema,v=>v);registry.Register<C2>("game.c2",1,schema,v=>v);registry.Register<C3>("game.c3",1,schema,v=>v);
            registry.Register<C4>("game.c4",1,schema,v=>v);registry.Register<C5>("game.c5",1,schema,v=>v);registry.Register<C6>("game.c6",1,schema,v=>v);registry.Register<C7>("game.c7",1,schema,v=>v);
            var policy=new PrefabPolicy(registry);for(int i=0;i<8;i++)policy.AllowComponent("game.c"+i,1,[]);
            var large=new PrefabDocument(1,Guid.NewGuid(),1,"Large",new(0,0,0),Enumerable.Range(0,16).Select(i=>new PrefabObject(Guid.NewGuid(),"Object",Enumerable.Range(0,8).Select(c=>new ComponentSnapshot("game.c"+c,1,JsonSerializer.SerializeToElement(new{value=1}))).ToArray(),[])).ToArray(),[],[]);
            _=PrefabDocumentCodec.Encode(large,policy);Reject(()=>PrefabTemplates.PreviewExpansion(large,policy,new(0,0,0),[]));Check(f.Scene.World.Count==3);
        });
    }
    private readonly record struct Link(Guid Target,Guid Asset,string Label):IComponent;
    private readonly record struct RuntimeOnly(ulong Handle):IComponent;
    private readonly record struct C0(int Value):IComponent;private readonly record struct C1(int Value):IComponent;
    private readonly record struct C2(int Value):IComponent;private readonly record struct C3(int Value):IComponent;
    private readonly record struct C4(int Value):IComponent;private readonly record struct C5(int Value):IComponent;
    private readonly record struct C6(int Value):IComponent;private readonly record struct C7(int Value):IComponent;
}
