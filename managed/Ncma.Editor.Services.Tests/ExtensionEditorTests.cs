using System.Text;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
#if NCMA_EXTENSIONS
using Ncma.Extensions.Hosting;
using Ncma.Application;
#endif

internal static class ExtensionEditorTests
{
    private static void Check(bool value) { if(!value)throw new Exception("Extension editor assertion"); }
    private static string Labels(EditorPresenter view) => Encoding.UTF8.GetString(view.Text);
    private static GuiEvent Click(EditorPresenter view,ulong high,ulong low)
    {
        var item=view.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low);
        return new(){Kind=item.Kind,WidgetHigh=high,WidgetLow=low,Phase=3,Frame=view.Frame.Frame,
            ViewGeneration=view.Frame.ViewGeneration,DocumentGeneration=view.Frame.DocumentGeneration,Revision=view.Frame.Revision};
    }
    private static void Open(EditorPresenter view)
    {
        view.Build(1,1280,720);view.Apply([Click(view,24,12)],[]); // original Tools menu
        view.Build(2,1280,720);view.Apply([Click(view,24,76)],[]);
        view.Build(3,1280,720);
    }
    internal static IEnumerable<(string,Action)> Cases()
    {
        yield return ("M8.5-C actual presenter default/optional explicit product entry and modal no mutation",()=>{
            using var owner=new EditorSessionOwner("Extension view");var workspace=new EditorWorkspace(owner);
            var view=new EditorPresenter(workspace,null,workspaceStyle:true);
            byte[] before=owner.Document.CaptureBytes();var state=owner.Edit!.State;
            Open(view);Check(!view.SceneHudInputAllowed);
            Check(!view.Shortcut(78,true,false,false,true));
            view.Apply([Click(view,2,8)],[]);Check(before.AsSpan().SequenceEqual(owner.Document.CaptureBytes()));
            Check(owner.Edit.State.UndoCount==state.UndoCount);
#if NCMA_EXTENSIONS
            Check(Labels(view).Contains("当前宿主尚未装配",StringComparison.Ordinal));
#else
            Check(Labels(view).Contains("默认产品未包含扩展模块",StringComparison.Ordinal));
            Check(!typeof(EditorPresenter).Assembly.GetReferencedAssemblies().Any(a=>a.Name!.StartsWith("Ncma.Extensions",StringComparison.Ordinal)));
#endif
            view.Apply([Click(view,84,1)],[]);view.Build(4,1280,720);Check(!Labels(view).Contains("示例只沿原组件",StringComparison.Ordinal));
        });
#if NCMA_EXTENSIONS
        yield return ("M8.5-C outer retained survives closed extension group and clears only after host retry",()=>{
            using var owner=new EditorSessionOwner("Extension outer retention");var workspace=new EditorWorkspace(owner);
            using var outer=new ApplicationLifetime();
            bool fail=true;int closes=0;
            var dependency=new RetainedDependency(()=>{closes++;if(fail)throw new InvalidOperationException("retained");});
            using var extensions=new EditorTrustedExtensions(workspace,
                ()=>outer.ShutdownAttempts!=0&&outer.RetainedServiceCount!=0);
            outer.Start([dependency,extensions]);
            var view=new EditorPresenter(workspace,null,workspaceStyle:true);view.AttachTrustedExtensions(extensions);Open(view);
            Check(!extensions.Status.OuterRetained&&Labels(view).Contains("外层持有：否",StringComparison.Ordinal));
            byte[] before=owner.Document.CaptureBytes();var history=owner.Edit!.State;
            try{outer.Dispose();throw new Exception("Expected outer close failure");}catch(AggregateException){}
            var status=extensions.Status;
            Check(status.State==ExtensionGroupState.Closed&&status.RetainedServices==0&&!status.CloseRetryRequired&&status.OuterRetained);
            Check(outer.RetainedServiceCount==1&&closes==1);
            view.Build(4,1280,720);
            Check(Labels(view).Contains("扩展持有 0",StringComparison.Ordinal)&&Labels(view).Contains("外层持有：是",StringComparison.Ordinal));
            // Reading/building diagnostics must neither retry cleanup nor grant access.
            Check(closes==1);Reject(()=>extensions.Capture());
            fail=false;outer.Dispose();view.Build(5,1280,720);
            Check(!extensions.Status.OuterRetained&&Labels(view).Contains("外层持有：否",StringComparison.Ordinal)&&closes==2);
            Check(before.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&history==owner.Edit.State);
        });
        yield return ("M8.5-C original author summary detached no full snapshot no history no runtime",()=>{
            using var owner=new EditorSessionOwner("Extension read");var workspace=new EditorWorkspace(owner);
            workspace.CreateObject(workspace.Stamp);using var extensions=new EditorTrustedExtensions(workspace);extensions.Start();
            byte[] before=owner.Document.CaptureBytes();var state=owner.Edit!.State;
            int reads=workspace.SnapshotReads;var summary=extensions.Capture();
            Check(summary.Objects==1&&summary.Document==owner.Document.SessionId&&summary.Source==workspace.Stamp&&extensions.IsCurrent(summary));
            Check(reads==workspace.SnapshotReads&&owner.Play is null&&owner.Edit.State==state&&before.AsSpan().SequenceEqual(owner.Document.CaptureBytes()));
        });
        yield return ("M8.5-C exact selection document revision and extension session invalidate summary",()=>{
            using var owner=new EditorSessionOwner("Extension invalidation");var workspace=new EditorWorkspace(owner);
            workspace.CreateObject(workspace.Stamp);using var extensions=new EditorTrustedExtensions(workspace);extensions.Start();
            var first=extensions.Capture();owner.Edit!.Select(null);Check(!extensions.IsCurrent(first));
            var second=extensions.Capture();workspace.CreateObject(workspace.Stamp);Check(!extensions.IsCurrent(second));
            var third=extensions.Capture();workspace.New(workspace.Stamp,true);Check(!extensions.IsCurrent(third));
            var old=extensions.Capture();extensions.Dispose();Check(!extensions.IsCurrent(old));Reject(()=>extensions.Capture());
            using var next=new EditorTrustedExtensions(workspace);next.Start();Check(!next.IsCurrent(old));Reject(extensions.Start);
        });
        yield return ("M8.5-C draft and external author writes fail closed without changing original history",()=>{
            using var owner=new EditorSessionOwner("Extension draft");var workspace=new EditorWorkspace(owner);
            workspace.CreateObject(workspace.Stamp);using var extensions=new EditorTrustedExtensions(workspace);extensions.Start();
            var first=extensions.Capture();Guid id=owner.Edit!.State.Selection!.Value;
            workspace.BeginDraft(workspace.Stamp,id,"Draft");Check(!extensions.IsCurrent(first));Reject(()=>extensions.Capture());workspace.CancelDraft();
            owner.Document.RenameObject(id,"External");Reject(()=>extensions.Capture());Check(!extensions.IsCurrent(first));
        });
        yield return ("M8.5-C owner thread early close and active group requirement",()=>{
            using var owner=new EditorSessionOwner("Extension thread");var workspace=new EditorWorkspace(owner);
            using var extensions=new EditorTrustedExtensions(workspace);Reject(()=>extensions.Capture());extensions.Start();
            Exception? failure=null;var thread=new Thread(()=>{try{extensions.Capture();}catch(Exception e){failure=e;}});thread.Start();thread.Join();
            Check(failure is InvalidOperationException);extensions.Dispose();Reject(()=>extensions.Capture());
            using var unopened=new EditorTrustedExtensions(workspace);unopened.Dispose();Reject(unopened.Start);
        });
        yield return ("M8.5-C actual optional presenter explicit refresh stale hide original event epochs",()=>{
            using var owner=new EditorSessionOwner("Extension presenter");var workspace=new EditorWorkspace(owner);
            using var extensions=new EditorTrustedExtensions(workspace);extensions.Start();
            var view=new EditorPresenter(workspace,null,workspaceStyle:true);view.AttachTrustedExtensions(extensions);Open(view);
            Check(!Labels(view).Contains("编辑文档",StringComparison.Ordinal));var refresh=Click(view,84,2);
            view.Apply([refresh],[]);view.Build(4,1280,720);Check(Labels(view).Contains("编辑文档",StringComparison.Ordinal));
            workspace.CreateObject(workspace.Stamp);view.Build(5,1280,720);Check(!Labels(view).Contains("编辑文档",StringComparison.Ordinal));
            view.Apply([refresh],[]);view.Build(6,1280,720);Check(!Labels(view).Contains("编辑文档",StringComparison.Ordinal));
            view.Apply([Click(view,84,2)],[]);view.Build(7,1280,720);Check(Labels(view).Contains("对象 1",StringComparison.Ordinal));
            extensions.Dispose();view.Build(8,1280,720);Check(!Labels(view).Contains("编辑文档",StringComparison.Ordinal));
            Check(view.Items.ToArray().Single(i=>i.WidgetHigh==84&&i.WidgetLow==2).Enabled==0&&extensions.Status.State==ExtensionGroupState.Closed);
        });
#endif
    }
#if NCMA_EXTENSIONS
    private sealed class RetainedDependency(Action close) : IApplicationService
    {
        public void Start() { }
        public void Dispose() => close();
    }
    private static void Reject(Action action)
    {
        try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){return;}
        throw new Exception("Expected extension rejection");
    }
#endif
}
