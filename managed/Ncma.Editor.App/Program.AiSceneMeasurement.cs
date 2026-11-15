using Ncma.Application;
using Ncma.Editor.Services;
#if NCMA_AI && !NCMA_EXTENSIONS
using System.Diagnostics;
using System.Security.Cryptography;
using Ncma.Editor.Modules;
using Ncma.Modules;
using Ncma.Platform;
#endif

namespace Ncma.Editor.App;

internal static partial class Program
{
    private static MeasurementInputLease? _sceneInputs = null;
    private static MeasurementRunDriver? _sceneDriver = null;
    private static bool AiSceneMeasurementRetained => _sceneInputs is not null || _sceneDriver is not null;
    internal static bool AiSceneMeasurementArguments(string[] args) => args.Length == 4 && args[0] == "--measure-ai-scene" &&
        args[2].Length == 64 && args[2].All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static int RunAiSceneMeasurement(string[] args)
    {
        if (!AiSceneMeasurementArguments(args)) { Console.Error.WriteLine("measurement_arguments"); return 2; }
#if !NCMA_AI || NCMA_EXTENSIONS
        Console.Error.WriteLine("measurement_requires_ai_only_product"); return 3;
#else
        if (_execution is not null || _idleInputs is not null || _idleDriver is not null) { Console.Error.WriteLine("editor_shutdown_retained"); return 4; }
        bool cleanupAttempted = false;
        try {
            var request = AiSceneMeasurementRequest.Read(args[1], args[2]);
#if DEBUG
            const MeasurementConfiguration configuration = MeasurementConfiguration.Debug;
#else
            const MeasurementConfiguration configuration = MeasurementConfiguration.Release;
#endif
            if (request.Inputs.Identity.Configuration != configuration) throw new ArgumentException("measurement_build_variant");
            request.ValidateOutput(args[3]);
            _sceneInputs = request.Inputs.OpenInputs(); _sceneInputs.Prepare();
            var prepared = request.Prepare(_sceneInputs); var entry = _sceneInputs.BindCurrentEditor();
            var limits = request.Recipient;
            string summary = $"AI 场景诊断（不是游戏性能验收）\n运行：{request.Inputs.Identity.RunId}\n候选：{request.Inputs.ProductDirectory}\n负载：{request.Inputs.WorkloadDirectory}\n" +
                $"场景SHA：{request.SceneSha256}\n对象：{prepared.ObjectCount}\n输出：{args[3]}\n" +
                $"接收方：{limits.ProviderId} / {limits.Endpoint}\n模型：{limits.Model}\n最多 {limits.MaximumAttempts} 次工作区尝试（含诊断/拒绝），每次输出≤{limits.MaximumOutputTokens} token、超时≤{limits.TimeoutMilliseconds} ms。不是费用上限。\n" +
                $"窗口≤{request.Inputs.DurationMilliseconds} ms / {request.Inputs.MaximumFrames} 帧；采样间隔{request.Inputs.IntervalMilliseconds} ms；30 FPS。帧/关闭阻塞可超时。\n" +
                "只在内存编辑场景；关闭后丢弃编辑，不保存、不Play、不导入。创建独立临时日志和新证据文件。不会自动配置或调用模型；每次外发与应用仍需原完整人审，远程密钥仅从原界面本次输入。关闭窗口可停止。是否启动？";
            if (!new WindowsConfirmation().Confirm(summary, "NcmaEngine 场景诊断确认")) {
                cleanupAttempted = true; return CloseSceneMeasurementResources() is null ? 5 : 4;
            }
            _sceneInputs.RequireCurrent(request.Inputs); request.ValidateOutput(args[3]);
            string? failure = null; MeasurementRunStatus? run = null; ProcessMeasurementReport? process = null;
            CandidatePresentation? presentation = null; byte[]? joint = null;
            var lifetime = _execution = new ApplicationLifetime();
            try {
                // Visible original CPU GUI/editor, NOT preview or a hidden substitute. No project/scripts.
                presentation = new(Path.Combine(request.Inputs.ProductDirectory, "plugins"), null, false, true, false, frameRateLimit: 30);
                presentation.ConfigureAiSceneMeasurement(prepared); lifetime.Start([presentation]);
                _sceneDriver = new(request.Inputs.Plan(), _sceneInputs);
                _sceneDriver.RunAndClose(lifetime, new MonotonicClock(), presentation, pacer: presentation.Pacer);
            } catch (Exception error) { failure = error.GetType().Name; }
            finally {
                try { if (_sceneDriver is {} driver) { run = driver.Status; process = driver.ProcessReport; } }
                catch (Exception error) { failure ??= error.GetType().Name; }
                finally { cleanupAttempted = true; string? closeFailure = CloseSceneMeasurementResources(); failure ??= closeFailure; }
            }
            // Explicit diagnostic prefix if original close retained the host; never retry/cancel/drain here.
            if (presentation is not null) {
                try { if (presentation.AiMeasurementStatus is { Configured: true, SealAttempted: false }) presentation.SealAiMeasurement(); }
                catch (Exception error) { failure ??= error.GetType().Name; }
            }
            var hostStatus = presentation?.AiMeasurementStatus;
            failure ??= hostStatus?.FailureType;
            var scene = presentation?.AiSceneMeasurementState ?? default;
            try {
                if (presentation?.CopyAiMeasurementEvidence() is {} workspace && process is not null) {
                    byte[] processBytes = ProcessMeasurementEvidence.Encode(process);
                    var observation = AiWorkspaceObservationEvidence.Decode(workspace).Identity;
                    joint = AiWorkspaceProcessEvidence.Encode(AiWorkspaceProcessEvidence.Create(request.Inputs.Plan(), prepared.Inputs, observation,
                        workspace, processBytes, Convert.ToHexString(SHA256.HashData(workspace)), Convert.ToHexString(SHA256.HashData(processBytes))));
                } else failure ??= "MeasurementEvidenceMissing";
            } catch (Exception error) { failure ??= error.GetType().Name; }
            var record = new AiSceneCandidateRecord(1, args[2], request.SceneSha256, prepared.PolicySha256, prepared.Inputs, entry,
                run, hostStatus?.Budget, scene.InitialSha256, scene.FinalSha256, _execution is not null, _sceneInputs is not null, joint, failure);
            request.ValidateOutput(args[3]); // Original writer repeats ancestor/no-overwrite checks; no output reservation.
            var output = AiSceneCandidateEvidence.ExportNew(args[3], record);
            Console.WriteLine($"measurement_ai_scene bytes={output.Bytes}; sha256={output.Sha256}; performance_accepted=false");
            return failure is null && run?.State == MeasurementRunState.Closed ? 0 : 4;
        } catch (Exception error) { Console.Error.WriteLine("measurement_failed: " + error.GetType().Name); return 4; }
        finally { if (!cleanupAttempted) { cleanupAttempted = true; _ = CloseSceneMeasurementResources(); } }
#endif
    }
    private static string? CloseSceneMeasurementResources()
    {
        if (_sceneDriver?.Status.State == MeasurementRunState.Retained) return "MeasurementCloseRetained";
        if (_execution is {} lifetime) {
            if (lifetime.RetainedServiceCount != 0 && lifetime.ShutdownAttempts == 0) {
                try { lifetime.Dispose(); } catch (Exception error) { return error.GetType().Name; }
            }
            if (lifetime.RetainedServiceCount != 0) return "MeasurementCloseRetained";
            _execution = null;
        }
        if (_sceneInputs is {} inputs) {
            try { inputs.Dispose(); } catch (Exception error) { return error.GetType().Name; }
            _sceneInputs = null;
        }
        _sceneDriver = null; return null;
    }
}
