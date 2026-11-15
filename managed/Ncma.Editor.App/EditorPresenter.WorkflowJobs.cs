using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private string _jobWorkspace = "", _jobCapture = "", _jobCache = "", _jobConfiguration = "Debug", _jobVariant = "default";
    private WorkflowLocalJobReview? _jobReview;
    private string[] _jobReviewPages = [];
    private readonly HashSet<int> _jobReviewSeen = [];
    private int _jobReviewPage;
    private bool _jobReviewed;
    private string[] _jobEvidencePages = [];
    private int _jobEvidencePage;
    private string _jobVsRoot = "", _jobVcVersion = "", _jobSdkRoot = "", _jobSdkVersion = "";
    private WorkflowSourceReview? _jobSourceReview;
    private string _jobSourceBundle = "", _jobGit = "";
    private void ClearWorkflowSourceReview()
    {
        if (_jobSourceReview is { } review) workspace.Owner.WorkflowJobs?.RevokeSourceReview(review);
        _jobSourceReview = null;
    }
    private void BuildWorkflowJobs(ref ulong label)
    {
        if (workspace.Owner.WorkflowJobs is not { } jobs) return;
#if NCMA_EXTENSIONS
        if (_jobSourceReview is not null && _trustedExtensions?.Status.State != Ncma.Extensions.Hosting.ExtensionGroupState.Running) ClearWorkflowSourceReview();
#endif
        if (_jobSourceReview is { } stale && !jobs.IsSourceReviewCurrent(stale)) ClearWorkflowSourceReview();
        Add(GuiItemKind.Label, 62, label++, "独立离线依赖准备 · 不联网，不自动构建/测试/安装");
        Add(GuiItemKind.Text, 62, 1, "可信构建工作区", new("wjob_workspace"), _jobWorkspace);
        Add(GuiItemKind.Text, 62, 2, "现有捕获 ID（32位）", new("wjob_capture"), _jobCapture);
        Add(GuiItemKind.Text, 62, 3, "本地 NuGet 归档缓存目录", new("wjob_cache"), _jobCache);
        Add(GuiItemKind.Text, 62, 4, "配置 Debug / Release", new("wjob_configuration"), _jobConfiguration);
        Add(GuiItemKind.Text, 62, 5, "变体 default / ai / extensions / ai-extensions", new("wjob_variant"), _jobVariant);
        Add(GuiItemKind.Text, 62, 17, "原生准备：Visual Studio 安装目录", new("wjob_vs_root"), _jobVsRoot);
        Add(GuiItemKind.Text, 62, 18, "MSVC 精确版本目录名", new("wjob_vc_version"), _jobVcVersion);
        Add(GuiItemKind.Text, 62, 19, "Windows Kits / 10 根目录", new("wjob_sdk_root"), _jobSdkRoot);
        Add(GuiItemKind.Text, 62, 20, "Windows SDK 精确版本目录名", new("wjob_sdk_version"), _jobSdkVersion);
        Add(GuiItemKind.Button, 62, 6, "检查捕获与本地缓存（不执行还原）", new("wjob_prepare", _workflowSelected),
            enabled: _workflowSelected != Guid.Empty && jobs.CanPrepare(_workflowSelected) && (_jobSourceReview is null ||
                (_jobSourceReview.WorkflowId == _workflowSelected && jobs.CanPrepareSourceReview(_jobSourceReview) && !jobs.RequiresCreatedCapture(_jobSourceReview))));
        if (_jobSourceReview is { } sourceReview) {
            Add(GuiItemKind.Label, 62, label++, $"已审阅源码交接（不是执行批准）\n计划 {sourceReview.WorkflowId:D}\n编辑版本 {sourceReview.Source}\n领域资产来源 {sourceReview.Assets?.ToString() ?? "不涉及资产"}\n补丁 {sourceReview.PatchFingerprint}\n审阅包 SHA256 {sourceReview.BundleSha256}\n交接 {sourceReview.Fingerprint}\n可显式生成完整捕获；仅接受原捕获算法和完全匹配的审阅包。准备尝试后不可复用。助手创建的结果必须从下方原 owner 进入准备，不能改填其他 ID。");
            Add(GuiItemKind.Button, 62, 21, "撤销源码交接（保留作业与证据）", new("wjob_source_clear", Operation: sourceReview));
            Add(GuiItemKind.Text, 62, 22, "已导出审阅包绝对路径", new("wjob_source_bundle"), _jobSourceBundle);
            Add(GuiItemKind.Text, 62, 23, "可信 Git 可执行文件绝对路径（只读发现）", new("wjob_source_git"), _jobGit);
            Add(GuiItemKind.Button, 62, 24, "生成完整捕获副本（只复制，不还原/构建）", new("wjob_source_capture", Operation: sourceReview),
                enabled: sourceReview.WorkflowId == _workflowSelected && jobs.CanCreateSourceCapture(sourceReview) && _jobSourceBundle.Length != 0 && _jobGit.Length != 0);
            Add(GuiItemKind.Label, 62, label++, "助手仅调用原捕获算法，固定本地 Git/PowerShell 是可信工具，不是沙箱或完整运行库快照。捕获失败/取消保留副本，不自动重试或删除。");
        } else Add(GuiItemKind.Label, 62, label++, "普通捕获入口不接受源码补丁；请先在可信扩展页面完整审阅并交接，不能只填写审阅哈希。");
        ulong sourceRow = 1000;
        foreach (var capture in jobs.SourceCaptures()) {
            Add(GuiItemKind.Label, 63, label++, $"源码捕获 {capture.Id:D} · 计划 {capture.WorkflowId:D}\nReady {capture.Ready} · Closing {capture.Closing} · Closed {capture.Closed}\nWorker {capture.WorkerId} · 根进程退出 {capture.RootExited} · 活动进程 {capture.ActiveProcesses} · 持有句柄 {capture.RetainedHandles}\n捕获 {capture.CaptureId:N}\n源码 {capture.SourceSha256}\nManifest {capture.ManifestSha256}\n诊断 {capture.Failure} · 关闭诊断 {capture.CloseFailure}");
            Add(GuiItemKind.Button, 63, sourceRow++, "使用该捕获准备离线依赖（仍需独立审阅批准）", new("wjob_source_prepare", capture.Id),
                enabled: capture.WorkflowId == _workflowSelected && capture.Ready && jobs.CanPrepareCreatedOffline(capture.Id));
            Add(GuiItemKind.Button, 63, sourceRow++, "取消捕获（不强杀，不删除）", new("wjob_source_cancel", capture.Id), enabled: !capture.Closed && !capture.Closing);
            Add(GuiItemKind.Button, 63, sourceRow++, "关闭 / 显式重试捕获释放", new("wjob_source_close", capture.Id), enabled: !capture.Closed);
        }
        ulong row = 1000;
        var views = jobs.Capture();
        if (_jobReview is { } oldReview && !views.Any(j => j.JobId == oldReview.JobId && j.SourceCurrent && j.Phase == "Review")) {
            _jobReview = null; _jobReviewPages = []; _jobReviewSeen.Clear(); _jobReviewed = false;
        }
        foreach (var job in views) {
            Add(GuiItemKind.Label, 62, label++, $"{job.JobId:D} | {job.Phase} / {job.Outcome}\n工作流 {job.WorkflowId:D} · 来源仍有效 {job.SourceCurrent}\n计划 SHA256 {job.PlanHash}\n已启动 {job.Started} · 已排空 {job.Drained} · 保留资源 {job.Retained} · 已放弃等待 {job.Abandoned}\n执行/准备诊断 {job.Diagnostic}\n停止请求诊断 {job.StopDiagnostic}\n释放资源诊断 {job.CloseDiagnostic}\n封存指纹 {job.ProductFingerprint}\n证据结论 {job.EvidenceConclusion}\n证据指纹 {job.EvidenceFingerprint}");
            Add(GuiItemKind.Button, 62, row++, "审阅此固定作业", new("wjob_review", job.JobId), enabled: job.SourceCurrent && job.Prepared && job.Phase == "Preparing" && job.Diagnostic.Length == 0);
            Add(GuiItemKind.Button, 62, row++, "取消 / 请求停止", new("wjob_cancel", job.JobId), enabled: job.Retained);
            Add(GuiItemKind.Button, 62, row++, "显式重试停止请求（不强杀）", new("wjob_stop_retry", job.JobId),
                enabled: job.Retained && (job.Phase is "Stopping" or "Abandoned" or "Preparing") && !job.Drained && job.StopDiagnostic.Length != 0);
            if (job.Phase is "Stopping" or "Abandoned")
                Add(GuiItemKind.Label, 62, label++, "放弃仅释放活动槽位：旧进程可能继续运行和写入，资源及日志保留至自然排空，旧结果不算验证通过。");
            Add(GuiItemKind.Button, 62, row++, "放弃等待并释放活动槽位（不强杀，保留资源）", new("wjob_abandon", job.JobId),
                enabled: job.Retained && !job.Drained && job.Phase == "Stopping");
            Add(GuiItemKind.Button, 62, row++, "封存还原结果（不是构建通过）", new("wjob_seal", job.JobId),
                enabled: job.Recipe == "editor-services-restore-offline-v1" && job.Phase == "AwaitingClose" && job.Outcome == "ProcessExited" && job.EvidenceFingerprint.Length == 0 && job.Diagnostic.Length == 0);
            Add(GuiItemKind.Button, 62, row++, "准备独立无还原构建（仍需审批）", new("wjob_build_prepare", job.JobId), enabled: job.SourceCurrent && job.Retained && job.ProductFingerprint.Length != 0 && job.Recipe == "editor-services-restore-offline-v1");
            Add(GuiItemKind.Button, 62, row++, "关闭 / 显式重试释放", new("wjob_close", job.JobId), enabled: job.Retained);
            Add(GuiItemKind.Button, 62, row++, "封存实际还原 / 构建 / 测试回执", new("wjob_evidence", job.JobId),
                enabled: job.Retained && job.Drained && job.EvidenceFingerprint.Length == 0);
            Add(GuiItemKind.Button, 62, row++, "查看全部证据（只读）", new("wjob_evidence_view", job.JobId), enabled: job.EvidenceFingerprint.Length != 0);
            bool compiled = job.SourceCurrent && job.Retained && job.Recipe == "editor-services-build-v1" && job.EvidenceConclusion == "CompileSucceeded";
            Add(GuiItemKind.Button, 62, row++, "准备同源原生依赖（另行审批，不代表测试通过）", new("wjob_native_prepare", job.JobId), enabled: compiled);
            Add(GuiItemKind.Button, 62, row++, $"准备合同测试 · {WorkflowTestInventory.Count("workflow-contracts-v1")} 项（另行审批）", new("wjob_test_contracts", job.JobId), enabled: compiled);
            Add(GuiItemKind.Button, 62, row++, $"准备宿主测试 · {WorkflowTestInventory.Count("workflow-host-v1")} 项（独立输入暂存，另行审批）", new("wjob_test_host", job.JobId),
                enabled: job.SourceCurrent && job.Retained && job.Recipe == "workflow-native-prepare-v1" && job.EvidenceConclusion == "NativeInputsPrepared");
        }
        if (_jobReview is { } review) {
            Add(GuiItemKind.Label, 62, label++, $"独立作业审批 {review.Fingerprint}\n页 {_jobReviewPage + 1}/{_jobReviewPages.Length}");
            Add(GuiItemKind.Label, 62, label++, _jobReviewPages[_jobReviewPage]); _jobReviewSeen.Add(_jobReviewPage);
            Add(GuiItemKind.Button, 62, 10, "上一页依赖审批", new("wjob_previous"), enabled: _jobReviewPage > 0); Line();
            Add(GuiItemKind.Button, 62, 11, "下一页依赖审批", new("wjob_next"), enabled: _jobReviewPage + 1 < _jobReviewPages.Length);
            bool all = _jobReviewSeen.Count == _jobReviewPages.Length;
            Add(GuiItemKind.Checkbox, 62, 12, "已核对全部精确输入、配方、路径和风险，仅批准所显示操作", new("wjob_checked"), number: _jobReviewed ? 1 : 0, max: 1, enabled: all);
            Add(GuiItemKind.Button, 62, 13, "批准此固定作业（60秒，单次）", new("wjob_approve", review.JobId, Field: review.Fingerprint), enabled: all && _jobReviewed);
        }
        Add(GuiItemKind.Button, 62, 14, "启动下一条已批准作业", new("wjob_start"), enabled: views.Any(j => j.Phase == "Queued"));
        if (_jobEvidencePages.Length != 0) {
            Add(GuiItemKind.Label, 62, label++, $"只读证据 · {_jobEvidencePage + 1}/{_jobEvidencePages.Length} · 不是审批许可\n{_jobEvidencePages[_jobEvidencePage]}");
            Add(GuiItemKind.Button, 62, 15, "上一页证据", new("wjob_evidence_previous"), enabled: _jobEvidencePage > 0); Line();
            Add(GuiItemKind.Button, 62, 16, "下一页证据", new("wjob_evidence_next"), enabled: _jobEvidencePage + 1 < _jobEvidencePages.Length);
        }
        Add(GuiItemKind.Label, 62, label++, "取消/撤权/过期/来源冲突会停止待办作业；不隐式 Undo 已提交的领域命令，不回滚已发生的文件或构建 IO。失败修复需新来源/捕获与新审批，旧证据保留。");
        Add(GuiItemKind.Label, 62, label++, "缺包明确失败；联网下载需另行审批。取消不强杀 SDK，排空前保持资源；卡住时可显式放弃等待以释放活动槽位。关闭面板不启动、不批准、不重试。");
    }
    private bool ApplyWorkflowJob(ActionView action, double value, string text)
    {
        if (!action.Kind.StartsWith("wjob_", StringComparison.Ordinal)) return false;
        var jobs = workspace.Owner.WorkflowJobs ?? throw new EditRejectedException("workflow_jobs_missing");
        switch (action.Kind) {
            case "wjob_workspace": ClearWorkflowSourceReview(); _jobWorkspace = text; break;
            case "wjob_capture": _jobCapture = text; break;
            case "wjob_cache": _jobCache = text; break;
            case "wjob_configuration": _jobConfiguration = text; break;
            case "wjob_variant": _jobVariant = text; break;
            case "wjob_vs_root": _jobVsRoot = text; break;
            case "wjob_vc_version": _jobVcVersion = text; break;
            case "wjob_sdk_root": _jobSdkRoot = text; break;
            case "wjob_sdk_version": _jobSdkVersion = text; break;
            case "wjob_native_prepare": _ = jobs.PrepareNative(action.Object, _jobVsRoot, _jobVcVersion, _jobSdkRoot, _jobSdkVersion); break;
            case "wjob_prepare":
                if (action.Object != _workflowSelected || !Guid.TryParseExact(_jobCapture, "N", out var capture)) throw new EditRejectedException("workflow_local_capture_selection");
                if (_jobSourceReview is { } sourceReview) {
                    if (sourceReview.WorkflowId != action.Object || sourceReview.Workspace != _jobWorkspace) throw new EditRejectedException("workflow_source_selection_mismatch");
                    _ = jobs.PrepareReviewedOffline(sourceReview, capture, _jobCache, _jobConfiguration, _jobVariant);
                } else _ = jobs.PrepareOffline(_jobWorkspace, capture, _jobCache, action.Object, _jobConfiguration, _jobVariant);
                break;
            case "wjob_source_clear":
                if (!ReferenceEquals(action.Operation, _jobSourceReview)) throw new EditRejectedException("workflow_source_foreign_handoff");
                ClearWorkflowSourceReview(); break;
            case "wjob_source_bundle": _jobSourceBundle = text; break;
            case "wjob_source_git": _jobGit = text; break;
            case "wjob_source_capture":
                if (_jobSourceReview is null || !ReferenceEquals(action.Operation, _jobSourceReview) || _jobSourceReview.WorkflowId != _workflowSelected)
                    throw new EditRejectedException("workflow_source_foreign_handoff");
                _ = jobs.BeginSourceCapture(_jobSourceReview, _jobSourceBundle, _jobGit); break;
            case "wjob_source_prepare": _ = jobs.PrepareCreatedOffline(action.Object, _jobCache, _jobConfiguration, _jobVariant); break;
            case "wjob_source_cancel": jobs.CancelSourceCapture(action.Object); break;
            case "wjob_source_close": _ = jobs.CloseSourceCapture(action.Object); break;
            case "wjob_review":
                _jobReview = jobs.Review(action.Object);
                _jobReviewPages = InspectionText.Split(_jobReview.Display, 512 * 1024).ToArray();
                _jobReviewPage = 0; _jobReviewSeen.Clear(); _jobReviewed = false; break;
            case "wjob_previous": _jobReviewPage = Math.Max(0, _jobReviewPage - 1); break;
            case "wjob_next": _jobReviewPage = Math.Min(_jobReviewPages.Length - 1, _jobReviewPage + 1); break;
            case "wjob_checked":
                if (_jobReview is null || value is not (0 or 1) || _jobReviewSeen.Count != _jobReviewPages.Length) throw new EditRejectedException("workflow_local_review_incomplete");
                _jobReviewed = value == 1; break;
            case "wjob_approve":
                if (_jobReview is null || action.Object != _jobReview.JobId || _jobReviewSeen.Count != _jobReviewPages.Length) throw new EditRejectedException("workflow_local_review_incomplete");
                var review = _jobReview; _jobReview = null;
                jobs.Approve(review, action.Field, _jobReviewed); _jobReviewed = false; break;
            case "wjob_start": _ = jobs.StartApproved(); break;
            case "wjob_cancel": jobs.Cancel(action.Object); if (_jobReview?.JobId == action.Object) _jobReview = null; break;
            case "wjob_stop_retry": jobs.RetryStop(action.Object); break;
            case "wjob_abandon": jobs.Abandon(action.Object); if (_jobReview?.JobId == action.Object) _jobReview = null; break;
            case "wjob_close": _ = jobs.Close(action.Object); if (_jobReview?.JobId == action.Object) _jobReview = null; break;
            case "wjob_seal": _ = jobs.SealRestore(action.Object); break;
            case "wjob_build_prepare": _ = jobs.PrepareBuild(action.Object); break;
            case "wjob_test_contracts": _ = jobs.PrepareTests(action.Object, "workflow-contracts-v1"); break;
            case "wjob_test_host": _ = jobs.PrepareHostTests(action.Object); break;
            case "wjob_evidence": _ = jobs.CaptureEvidence(action.Object); break;
            case "wjob_evidence_view":
                var evidence = jobs.Evidence(action.Object) ?? throw new EditRejectedException("workflow_evidence_missing");
                _jobEvidencePages = InspectionText.Split(System.Text.Json.JsonSerializer.Serialize(new {
                    jobId = action.Object, evidence.Version, evidence.TaskFingerprint, evidence.Fingerprint, evidence.Conclusion,
                    evidence.ProcessOutcome, evidence.ExitCode, evidence.DiagnosticCountScope, evidence.WarningCount, evidence.ErrorCount,
                    evidence.EvidenceComplete, evidence.InputsObserved, evidence.InputFilesMatch, evidence.DiagnosticCode, evidence.InstallationAuthorized,
                    inputs = evidence.CopyObservedInputs(), outputs = evidence.CopyOutputs(), logs = evidence.CopyLogs(), cases = evidence.CopyCases()
                }), 512 * 1024).ToArray(); _jobEvidencePage = 0; break;
            case "wjob_evidence_previous": _jobEvidencePage = Math.Max(0, _jobEvidencePage - 1); break;
            case "wjob_evidence_next": _jobEvidencePage = Math.Min(_jobEvidencePages.Length - 1, _jobEvidencePage + 1); break;
            default: throw new EditRejectedException("workflow_local_intent");
        }
        return true;
    }
}
