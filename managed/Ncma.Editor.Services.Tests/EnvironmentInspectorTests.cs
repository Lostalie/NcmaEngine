using System.Text;
using System.Text.Json;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Scene.Rendering;

internal static class EnvironmentInspectorTests
{
    private const string Strength = "环境光强度", Rotation = "环境 Y 旋转（度）", Enabled = "环境启用（关闭后可撤销恢复）";
    private static void Check(bool value) { if (!value) throw new Exception("Environment Inspector assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid environment field accepted."); }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner = new("Environment Inspector", components:RenderComponentRegistry.CreateRegistry(), validateComposition:CandidatePresentation.RequireComposition);
        internal readonly EditorWorkspace Workspace;
        internal readonly EditorPresenter View;
        internal readonly Guid Id;
        internal readonly EnvironmentLightingData Initial = new(1, Guid.NewGuid(), 1, new string('A',64), 1, 0, true);
        private ulong _frame;
        internal Fixture(bool active = true) {
            Workspace = new(Owner); Id = Workspace.CreateObject(Workspace.Stamp);
            var request = RenderingEditorAdapter.EnvironmentRequest(Owner.Edit!, Id, active ? Initial : EnvironmentLightingData.Off, Owner.Edit!.Revision);
            Check(Owner.Edit.Invoke(request,new(["ncma.scene.transaction"])).Changed);
            View = new(Workspace, null, workspaceStyle:true); Build();
        }
        internal EnvironmentLightingData Value => Owner.Document.World.Get<EnvironmentLightingData>(Owner.Document.World.FindEntity(Id));
        internal void Build() => View.Build(++_frame,1280,720);
        internal GuiItem Item(string label) => View.Items.ToArray().Single(i => Encoding.UTF8.GetString(View.Text.Slice((int)i.LabelOffset,(int)i.LabelLength)) == label);
        internal GuiEvent Event(string label, uint phase, double value = 0) { var i = Item(label); var f = View.Frame; return new() {
            Kind = i.Kind, WidgetHigh = i.WidgetHigh, WidgetLow = i.WidgetLow, Phase = phase, Value = value,
            Frame = f.Frame, ViewGeneration = f.ViewGeneration, DocumentGeneration = f.DocumentGeneration, Revision = f.Revision }; }
        internal void Change(string label, double value) => View.Apply([Event(label,1),Event(label,2,value),Event(label,3,value)],[]);
        public void Dispose() => Owner.Dispose();
    }
    internal static IEnumerable<(string,Action)> Cases(string output)
    {
        yield return ("M7.3-C3-B1 Inspector draft/commit/shared Undo/Redo and degrees", () => {
            using var f = new Fixture(); byte[] before = f.Owner.Document.CaptureBytes(); int history = f.Owner.Edit!.State.UndoCount;
            f.View.Apply([f.Event(Strength,1),f.Event(Strength,2,2.5)],[]);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Workspace.HasDraft);
            f.Build(); f.View.Apply([f.Event(Strength,3,2.5)],[]);
            Check(f.Value.Strength == 2.5f && f.Owner.Edit.State.UndoCount == history + 1 && !f.Workspace.HasDraft);
            f.Workspace.History(f.Workspace.Stamp,false); Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            f.Workspace.History(f.Workspace.Stamp,true); Check(f.Value.Strength == 2.5f);
            f.Build(); f.Change(Rotation,180); Check(f.Value.RotationRadians == MathF.PI && f.Value.AssetId == f.Initial.AssetId);
            f.Build(); f.Change(Rotation,-180); Check(f.Value.RotationRadians == -MathF.PI);
        });
        yield return ("M7.3-C3-B1 canceled/invalid/nonfinite/forged/stale UI preserves document", () => {
            using var f = new Fixture(); byte[] before = f.Owner.Document.CaptureBytes(); int history = f.Owner.Edit!.State.UndoCount;
            var oldCommit = f.Event(Strength,3,8);
            f.View.Apply([f.Event(Strength,1),f.Event(Strength,2,8)],[]); f.View.CancelInteraction(); f.View.Apply([oldCommit],[]);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && !f.Workspace.HasDraft);
            foreach (double bad in new[]{-1,16.1,double.NaN,double.PositiveInfinity}) { f.Build(); f.Change(Strength,bad); Check(f.Value == f.Initial && !f.Workspace.HasDraft); }
            f.Build(); f.Change(Rotation,180.1); Check(f.Value == f.Initial);
            f.Build(); var forged = f.Event(Strength,1); forged.Kind = (uint)GuiItemKind.Text; f.View.Apply([forged],[]); Check(!f.Workspace.HasDraft);
            Check(f.Owner.Edit.State.UndoCount == history);
            f.Build(); var stale = f.Event(Strength,1); f.Workspace.Transaction(f.Workspace.Stamp,"Revision",[new {op="rename",objectId=f.Id,name="New revision"}]);
            f.View.Apply([stale],[]); Check(!f.Workspace.HasDraft && f.Value == f.Initial);
        });
        yield return ("M7.3-C3-B1 Off clears identity and cannot implicitly reactivate; Undo restores", () => {
            using var f = new Fixture(); f.Change(Enabled,0); Check(f.Value == EnvironmentLightingData.Off); f.Build();
            Check(f.Item(Strength).Enabled == 0 && f.Item(Rotation).Enabled == 0 && f.Item(Enabled).Enabled == 0);
            f.Change(Enabled,1); Check(f.Value == EnvironmentLightingData.Off);
            f.Workspace.History(f.Workspace.Stamp,false); Check(f.Value == f.Initial);
            Reject(()=>RenderingEditorAdapter.EnvironmentFieldOperation(f.Id,EnvironmentLightingData.Off,"enabled",1));
            Reject(()=>RenderingEditorAdapter.EnvironmentFieldOperation(f.Id,f.Initial,"assetId",1));
            Reject(()=>RenderingEditorAdapter.EnvironmentFieldOperation(f.Id,f.Initial,"enabled",.5));
        });
        yield return ("M7.3-C3-B1 single configuration and opt-in Off attachment", () => {
            using var owner = new EditorSessionOwner("Empty",components:RenderComponentRegistry.CreateRegistry(),validateComposition:CandidatePresentation.RequireComposition);
            var workspace = new EditorWorkspace(owner); Guid id = workspace.CreateObject(workspace.Stamp); var view = new EditorPresenter(workspace,null); view.Build(1,1280,720);
            var i = view.Items.ToArray().Single(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="添加环境配置（关闭）"); var f=view.Frame;
            view.Apply([new(){Kind=i.Kind,WidgetHigh=i.WidgetHigh,WidgetLow=i.WidgetLow,Phase=3,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);
            Check(owner.Document.World.Get<EnvironmentLightingData>(owner.Document.World.FindEntity(id)) == EnvironmentLightingData.Off);
            Guid other = workspace.CreateObject(workspace.Stamp); view.Build(2,1280,720);
            Check(!Encoding.UTF8.GetString(view.Text).Contains("添加环境配置（关闭）"));
            var result = owner.Edit!.Invoke(RenderingEditorAdapter.EnvironmentRequest(owner.Edit,other,EnvironmentLightingData.Off,owner.Edit.Revision),new(["ncma.scene.transaction"]));
            Check(!result.Changed && !owner.Document.World.Has<EnvironmentLightingData>(owner.Document.World.FindEntity(other)));
        });
        yield return ("M7.3-C3-B1 Agent exact scope/revision/session/revocation and original history", () => {
            using var f = new Fixture(); var edit=f.Owner.Edit!; bool current=true;
            var grant=new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"],objectScope:[f.Id],componentScope:[EnvironmentLightingData.TypeId],isCurrent:()=>current);
            CapabilityRequest Request(Guid id,ulong revision)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,revision,"ncma.scene.transaction",
                JsonSerializer.SerializeToElement(new{label="Environment field",operations=new[]{RenderingEditorAdapter.EnvironmentFieldOperation(id,f.Initial,"strength",3)}}));
            var request=Request(f.Id,edit.Revision); byte[] before=f.Owner.Document.CaptureBytes();
            Check(edit.Invoke(request).Status=="denied");
            Check(!edit.Invoke(request with{RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid()},grant).Changed);
            Check(!edit.Invoke(Request(Guid.NewGuid(),edit.Revision),grant).Changed);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            request=Request(f.Id,edit.Revision); Check(edit.Invoke(request,grant).Changed && f.Value.Strength==3);
            Check(!edit.Invoke(request with{RequestId=Guid.NewGuid()},grant).Changed);
            current=false; Check(!edit.Invoke(Request(f.Id,edit.Revision),grant).Changed);
            CapabilityRequest History()=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.history.undo",JsonSerializer.SerializeToElement(new{}));
            Check(!edit.Invoke(History(),grant).Changed && f.Value.Strength==3);
            current=true; Check(edit.Invoke(History(),grant).Changed && f.Value==f.Initial);
        });
        yield return ("M7.3-C3-B1 save/reopen and Play freeze", () => {
            using var f=new Fixture(); f.Change(Strength,4); string path=Path.Combine(output,"environment-inspector.ncmascene"); f.Workspace.Save(f.Workspace.Stamp,path);
            using var reopened=new EditorSessionOwner("Reopen",components:RenderComponentRegistry.CreateRegistry(),validateComposition:CandidatePresentation.RequireComposition);
            var workspace=new EditorWorkspace(reopened); workspace.Open(workspace.Stamp,path,true); Check(reopened.Document.World.Get<EnvironmentLightingData>(reopened.Document.World.FindEntity(f.Id))==f.Value);
            byte[] before=f.Owner.Document.CaptureBytes(); f.Owner.StartPlay(); f.Build(); Check(f.Item(Strength).Enabled==0); f.Change(Strength,8);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())); f.Owner.StopPlay(); f.Build(); Check(f.Item(Strength).Enabled!=0);
        });
        yield return ("M7.3-C3-B1 status is world/revision/Play-bound and clears on recovery", () => {
            using var f=new Fixture(); var world=f.Owner.Document.World;
            Check(f.View.EnvironmentPreparationStatus.Contains("等待"));
            f.View.ShowEnvironmentPreparation(world.Identity,world.Revision,Guid.Empty,true,false,f.Value); Check(f.View.EnvironmentPreparationStatus.Contains("就绪"));
            f.View.ShowEnvironmentPreparation(world.Identity,world.Revision,Guid.Empty,false,true,null); Check(f.View.EnvironmentPreparationStatus.Contains("失败"));
            f.View.ShowEnvironmentPreparation(world.Identity,world.Revision,Guid.Empty,true,false,f.Value); Check(!f.View.EnvironmentPreparationStatus.Contains("失败"));
            f.Change(Strength,2); Check(f.View.EnvironmentPreparationStatus.Contains("等待"));
            world=f.Owner.Document.World; f.View.ShowEnvironmentPreparation(Guid.NewGuid(),world.Revision,Guid.Empty,true,false,f.Value); Check(f.View.EnvironmentPreparationStatus.Contains("等待"));
            var play=f.Owner.StartPlay(); f.View.ShowEnvironmentPreparation(play.Document.World.Identity,play.Document.World.Revision,play.SessionId,true,false,f.Value);
            Check(f.View.EnvironmentPreparationStatus.Contains("Play")); f.Owner.StopPlay(); Check(f.View.EnvironmentPreparationStatus.Contains("等待"));
        });
        yield return ("M7.3-C3-B1 unchanged Inspector reuses document cache", () => {
            using var f=new Fixture(); int reads=f.Workspace.SnapshotReads;
            for(int i=0;i<32;i++)f.Build(); Check(f.Workspace.SnapshotReads==reads);
        });
    }
}
