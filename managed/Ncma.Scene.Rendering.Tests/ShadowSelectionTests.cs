using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private static void RegisterShadowSelectionTests(List<(string,Action)> tests)
    {
        tests.Add(("shadow-absence-explicit-selection-roundtrip",()=>{
            var d=Document();Check(SceneShadowState.Read(d.CaptureSnapshot()) is null&&SceneShadowState.Read(d.World,d.World.Identity) is null);
            foreach(var quality in Enum.GetValues<SceneShadowQuality>()){
                var obj=d.World.CreateObject("Shadow policy");obj.Set(new SceneShadowData(1,quality));d.ValidateAuthoring();
                Check(!obj.Has<TransformData>()&&SceneShadowState.Read(d.World,d.World.Identity)==new SceneShadowData(1,quality));
                string path=Path.Combine(OutputRoot(),Guid.NewGuid()+".ncmascene");SceneDocumentFiles.Save(d,path);var copy=Document();SceneDocumentFiles.Load(copy,path);
                Check(copy.CaptureBytes().AsSpan().SequenceEqual(d.CaptureBytes())&&SceneShadowState.Read(copy.CaptureSnapshot())==new SceneShadowData(1,quality));
                obj.Remove<SceneShadowData>();Check(SceneShadowState.Read(d.CaptureSnapshot()) is null);
            }
        }));
        tests.Add(("shadow-strict-format-values",()=>{
            var registry=RenderComponentRegistry.CreateRegistry();
            foreach(string json in new[]{"{}","{\"version\":1}","{\"quality\":2}","{\"version\":1,\"quality\":4}","{\"version\":2,\"quality\":2}","{\"version\":1,\"quality\":-1}","{\"version\":1,\"quality\":\"High\"}","{\"version\":1,\"quality\":2,\"resolution\":8192}"}){
                using var doc=JsonDocument.Parse(json);Reject(()=>registry.Decode<SceneShadowData>(new(SceneShadowData.TypeId,1,doc.RootElement.Clone())));
            }
            Reject(()=>SceneShadowData.Validate(new(1,(SceneShadowQuality)100)));Reject(()=>registry.Decode<SceneShadowData>(new(SceneShadowData.TypeId,2,registry.Encode(SceneShadowData.Default))));
        }));
        tests.Add(("shadow-singleton-off-and-atomic-rejection",()=>{
            var d=Document();d.World.CreateObject("First").Set(new SceneShadowData(1,SceneShadowQuality.Off));var s=d.CaptureSnapshot();
            var duplicate=s.Objects[0] with{Id=Guid.NewGuid()};AtomicReject(d,s with{Objects=[s.Objects[0],duplicate]});
            d.World.CreateObject("Second").Set(SceneShadowData.Default);Reject(()=>SceneShadowState.Read(d.World,d.World.Identity));Reject(d.ValidateAuthoring);
        }));
        tests.Add(("shadow-original-scoped-transaction-history",()=>{
            var d=Document();Guid id=d.World.CreateObject("Policy").PersistentId;var edit=new EditSession(d);var initial=d.CaptureBytes();
            object Input(SceneShadowQuality quality)=>new{operations=new[]{new{op="set_component",objectId=id,typeId=SceneShadowData.TypeId,version=1,data=new{version=1,quality=(int)quality}}}};
            CapabilityRequest Request(SceneShadowQuality quality)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",Element(Input(quality)));
            var command=Request(SceneShadowQuality.High);Check(edit.Invoke(command).Status=="denied");Check(!edit.Invoke(command,new(["ncma.scene.transaction"],objectScope:[Guid.NewGuid()],componentScope:[SceneShadowData.TypeId])).Changed);
            var grant=new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"],objectScope:[id],componentScope:[SceneShadowData.TypeId]);
            Check(edit.Invoke(command,grant).Changed&&edit.State.UndoCount==1);var high=d.CaptureBytes();
            Check(!edit.Invoke(Request((SceneShadowQuality)9),grant).Changed&&high.AsSpan().SequenceEqual(d.CaptureBytes())&&edit.State.UndoCount==1);
            Check(Invoke(edit,"ncma.history.undo",new{}).Changed&&initial.AsSpan().SequenceEqual(d.CaptureBytes()));
            Check(Invoke(edit,"ncma.history.redo",new{}).Changed&&high.AsSpan().SequenceEqual(d.CaptureBytes()));
            ulong revision=edit.Revision;_=edit.Invoke(command,grant);Check(edit.Revision==revision&&edit.State.UndoCount==1&&high.AsSpan().SequenceEqual(d.CaptureBytes()));
        }));
        tests.Add(("shadow-committed-owner-and-identity",()=>{
            var d=Document();d.World.CreateObject("Policy").Set(SceneShadowData.Default);Reject(()=>SceneShadowState.Read(d.World,Guid.NewGuid()));
            Check(Task.Run(()=>{try{SceneShadowState.Read(d.World,d.World.Identity);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());
            var runner=new WorldRunner(d.World);runner.AddSystem(new ActionSystem(_=>Reject(()=>SceneShadowState.Read(d.World,d.World.Identity))));runner.Advance(1d/60);Check(!runner.IsFaulted);
        }));
    }
}
