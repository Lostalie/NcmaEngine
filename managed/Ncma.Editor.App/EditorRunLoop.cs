using Ncma.Application;
using Ncma.Interop;

namespace Ncma.Editor.App;

internal sealed record EditorRecoveryRequest(ApplicationFrameFailure Failure,EditorGraphicsReview Review,bool Retry,NativeFailureDiagnostic? NativeDiagnostic=null)
{
    public string Message=>$"编辑器呈现故障，帧 {Failure.FrameId}，阶段 {Failure.Phase}。\n"+
        $"场景版本 {Review.Revision}，图形设备代 {Review.RendererGeneration}。\n"+
        (NativeDiagnostic is {} d?$"设备生命周期首个诊断：操作 {d.Operation}，原生码 {(d.NativeCode is int code?$"0x{unchecked((uint)code):X8}":"无")}，观察帧 {d.Frame}。\n":"")+
        (Review.Play is {} play?$"Play 状态 {play.State}，已提交步 {play.Tick}；恢复期间停止推进，不重启脚本。\n":"")+
        (Retry?"上次恢复未成功，本次需要重新确认。\n":"")+
        "选择“是”：丢弃未完成的图形帧，关闭故障图形资源并创建新设备；保留当前场景和已提交的撤销历史。\n"+
        "选择“否”：退出并尝试有序关闭；未保存的更改不会自动保存。\n\n是否尝试恢复图形设备？";
}

// The formal Editor entry and automatic probes share this loop. Human confirmation occurs
// after Run has unwound, never in a render/simulation tick. Null prompt is fail-closed.
internal static class EditorRunLoop
{
    private sealed class Recovery(CandidatePresentation owner,EditorGraphicsReview review):IApplicationPresentationRecovery
    {
        private bool _restored;
        public IFramePipeline Pipeline=>owner;
        public void Restore(){owner.RebuildGraphics(review);_restored=true;}
        public bool IsReady=>_restored&&owner.GraphicsPhase==EditorGraphicsPhase.Ready;
    }
    internal static ulong Run(ApplicationLifetime lifetime,CandidatePresentation presentation,IFrameClock clock,ulong maxFrames,
        Func<EditorRecoveryRequest,bool>? confirm=null,LoopCostCapture? costs=null,FramePacer? pacer=null)
    {
        if(maxFrames==0)throw new ArgumentException("Positive frame budget required.");
        ulong start=lifetime.LastFrameId;
        while(lifetime.LastFrameId-start<maxFrames) {
            try{lifetime.Run(clock,presentation,maxFrames-(lifetime.LastFrameId-start),costs,pacer);return lifetime.LastFrameId-start;}
            catch(Exception error)when(error is not OutOfMemoryException) {
                if(confirm is null||!TryRestore(lifetime,presentation,confirm))throw;
            }
        }
        return lifetime.LastFrameId-start;
    }
    private static bool TryRestore(ApplicationLifetime lifetime,CandidatePresentation presentation,Func<EditorRecoveryRequest,bool> confirm)
    {
        bool retry=false;
        while(lifetime.CanRecoverPresentation&&presentation.CanReviewGraphicsRecovery) {
            var failure=lifetime.FrameFailure!.Value;
            var review=presentation.ReviewGraphicsRecovery(abandonActiveFrame:true);
            if(!confirm(new(failure,review,retry,presentation.GraphicsNativeFailure)))return false;
            try{lifetime.RecoverPresentation(failure,new Recovery(presentation,review));return true;}
            catch(Exception error)when(error is not OutOfMemoryException) {retry=true;}
        }
        return false;
    }
}
