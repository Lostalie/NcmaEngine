using Ncma.Application;
using Ncma.Editor.Services;
using Ncma.Platform;

namespace Ncma.Editor.App;

internal static partial class Program
{
    private static MeasurementInputLease? _idleInputs;
    private static MeasurementRunDriver? _idleDriver;
    internal static bool CpuMeasurementArguments(string[] args) => args.Length==4&&args[0]=="--measure-cpu-idle"&&
        args[2].Length==64&&args[2].All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static int RunCpuMeasurement(string[] args)
    {
        if(!CpuMeasurementArguments(args)){Console.Error.WriteLine("measurement_arguments");return 2;}
        if(_execution is not null){Console.Error.WriteLine("editor_shutdown_retained");return 4;}
        bool cleanupAttempted=false;
        try {
            var request=MeasurementCandidateRequest.Read(args[1],args[2]);
#if DEBUG
            const MeasurementConfiguration configuration=MeasurementConfiguration.Debug;
#else
            const MeasurementConfiguration configuration=MeasurementConfiguration.Release;
#endif
#if NCMA_AI && NCMA_EXTENSIONS
            const MeasurementVariant variant=MeasurementVariant.AiExtensions;
#elif NCMA_AI
            const MeasurementVariant variant=MeasurementVariant.Ai;
#elif NCMA_EXTENSIONS
            const MeasurementVariant variant=MeasurementVariant.Extensions;
#else
            const MeasurementVariant variant=MeasurementVariant.Default;
#endif
            if(request.Identity.Configuration!=configuration||request.Identity.Variant!=variant)throw new ArgumentException("measurement_build_variant");
            request.ValidateOutput(args[3]);
            _idleInputs=request.OpenInputs();var receipt=_idleInputs.Prepare();
            if(receipt.Scenario!=MeasurementScenario.EditorIdle||receipt.WorkloadFiles!=0)throw new ArgumentException("measurement_cpu_idle_only");
            var binding=_idleInputs.BindCurrentEditor();
            // Local human action only; there is no approval bit in the request and no test callback here.
            string summary=$"CPU 空闲诊断（非游戏性能验收）\n运行：{request.Identity.RunId}\n候选：{request.ProductDirectory}\n负载：{request.WorkloadDirectory}\n输出：{args[3]}\n"+
                $"上限 {request.DurationMilliseconds} ms / {request.MaximumFrames} 帧；间隔 {request.IntervalMilliseconds} ms；30 FPS 上限。\n"+
                "显示独立窗口，可关闭窗口停止；不加载项目、不执行游戏脚本、不调用模型。创建独立临时日志和新证据文件，不覆盖旧文件。是否运行？";
            if(!new WindowsConfirmation().Confirm(summary,"NcmaEngine 测量确认")) {
                cleanupAttempted=true;return CloseIdleResources() is null?5:4;
            }
            _idleInputs.RequireCurrent(); // Human wait does not substitute for a fresh input check.
            string? failure=null;MeasurementRunStatus? status=null;ProcessMeasurementReport? report=null;
            var lifetime=_execution=new ApplicationLifetime();
            try {
                // Existing CPU preview: visible diagnostic window, unique temp logs, no authoring presenter,
                // preferences, AI workspace/secret input, extension startup or project/gameplay loading.
                var presentation=new CandidatePresentation(Path.Combine(request.ProductDirectory,"plugins"),null,false,true,true,frameRateLimit:30);
                lifetime.Start([presentation]);
                _idleDriver=new(request.Plan(),_idleInputs);
                _idleDriver.RunAndClose(lifetime,new MonotonicClock(),presentation,pacer:presentation.Pacer);
            } catch(Exception error) { failure=error.GetType().Name; }
            finally {
                try { if(_idleDriver is {} driver) { status=driver.Status;report=driver.ProcessReport; } }
                catch(Exception error) { failure??=error.GetType().Name; }
                finally { cleanupAttempted=true;string? closeFailure=CloseIdleResources();failure??=closeFailure; }
            }
            var evidence=new MeasurementCandidateRecord(1,"cpu-idle",args[2],request.Identity,request.DurationMilliseconds,
                request.IntervalMilliseconds,request.Capacity,request.MaximumFrames,request.Plan().Fingerprint,receipt,binding,status,
                report is null?null:ProcessMeasurementEvidence.Encode(report),failure);
            var output=MeasurementCandidateEvidence.ExportNew(args[3],evidence);
            Console.WriteLine($"measurement_cpu_idle bytes={output.Bytes}; sha256={output.Sha256}; performance_accepted=false");
            return failure is null&&status?.State==MeasurementRunState.Closed?0:4;
        } catch(Exception error) { Console.Error.WriteLine("measurement_failed: "+error.GetType().Name);return 4; }
        finally { if(!cleanupAttempted) {cleanupAttempted=true;_ = CloseIdleResources();} }
    }
    private static string? CloseIdleResources()
    {
        // Original lifetime may already have attempted startup/driver cleanup. Never retry it implicitly.
        if(_idleDriver?.Status.State==MeasurementRunState.Retained)return "MeasurementCloseRetained";
        if(_execution is {} lifetime) {
            if(lifetime.RetainedServiceCount!=0&&lifetime.ShutdownAttempts==0) {
                try{lifetime.Dispose();}catch(Exception error){return error.GetType().Name;}
            }
            if(lifetime.RetainedServiceCount!=0)return "MeasurementCloseRetained";
            _execution=null;
        }
        if(_idleInputs is {} inputs) {
            try{inputs.Dispose();}catch(Exception error){return error.GetType().Name;}
            _idleInputs=null;
        }
        _idleDriver=null;return null;
    }
}
