#if NCMA_EXTENSIONS
using Ncma.Application;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Extensions;
using Ncma.Extensions.Hosting;

namespace Ncma.Editor.App;

// Explicit compiled-in trusted sample, NOT a loader, authority registry or code sandbox.
internal sealed class EditorTrustedExtensions : IApplicationService
{
    internal static readonly ExtensionDescriptor Descriptor = new("ncma.editor.summary", new(1,2,0), new(1,0,0));
    internal static readonly ExtensionPlan Plan = ExtensionPlan.Compile(new(1,0,0),Descriptor);
    internal static Ncma.Runtime.ComponentRegistry BuildComponents() => ExtensionComposition.Build(Plan,
        global::NcmaEngine.Generated.ComponentModule.Module,
        new ExtensionComponentContribution(Descriptor,global::Ncma.Extensions.Hosting.Generated.ComponentModule.Module));
    private readonly ExtensionApplicationService group;
    private readonly EditorWorkspace workspace;
    private readonly Func<bool>? outerRetained;
    private SummaryTool? tool;
    internal EditorExtensionCommands Commands { get; }
    internal EditorTrustedExtensions(EditorWorkspace workspace, Func<bool>? outerRetained = null)
    {
        this.workspace = workspace;
        this.outerRetained = outerRetained;
        group = new(Plan, new ExtensionRegistration(Descriptor, construction => {
            var candidate = new SummaryTool(new SummaryReadFacade(workspace));
            construction.Attach(candidate); // no resources before ownership transfer
            tool = candidate;
        }));
        Commands = new(group,workspace);
    }
    internal ExtensionGroupStatus Status
    {
        get {
            Commands.VerifyIdle();
            var status = group.Status;
            return status with { OuterRetained = outerRetained?.Invoke() ?? false };
        }
    }
    internal void RequireOwner(EditorSessionOwner owner)
    { Commands.VerifyIdle();if(!ReferenceEquals(owner,workspace.Owner))throw new EditRejectedException("extension_owner_mismatch"); }
    public void Start() { Commands.VerifyIdle(); group.Start(); }
    public void Dispose() { Commands.Cancel(); group.Dispose(); tool = null; }
    // Only an explicit trusted host recovery may retry retained cleanup; never auto-retry.
    internal void RetryClose() { Commands.Cancel(); group.RetryClose(); tool = null; }
    internal ExtensionSceneSummary Capture()
    {
        Commands.VerifyIdle();
        group.GetAccess(Descriptor.Id).RequireActive();
        return (tool ?? throw new InvalidOperationException("extension_tool_missing")).Capture();
    }
    internal bool IsCurrent(ExtensionSceneSummary summary)
    {
        Commands.VerifyIdle();
        if (group.Status.State != ExtensionGroupState.Running) return false;
        var access = group.GetAccess(Descriptor.Id); access.RequireActive();
        var state = workspace.Owner.Edit!.State;
        return !state.EditBusy && !state.HistoryInvalidated && summary.ExtensionSession == access.SessionId &&
            summary.Source == workspace.Stamp && summary.Document == workspace.Owner.Document.SessionId && summary.Selection == state.Selection;
    }

    private sealed class SummaryTool(SummaryReadFacade reader) : IManagedExtension
    {
        public void Start(ExtensionAccess access) => reader.Bind(access);
        internal ExtensionSceneSummary Capture() => reader.Capture();
        public void Dispose() => reader.Close();
    }
    // The extension receives only this read facade, not raw EditSession/World/commands.
    // This is a cooperative trusted API contract; compiled C# itself is not sandboxed.
    private sealed class SummaryReadFacade(EditorWorkspace workspace)
    {
        private ExtensionAccess? token;
        internal void Bind(ExtensionAccess access) => token = access;
        internal void Close() => token = null;
        internal ExtensionSceneSummary Capture()
        {
            var access = token ?? throw new InvalidOperationException("extension_tool_closed");
            access.RequireActive();
            var state = workspace.Owner.Edit!.State;
            if (state.EditBusy || state.HistoryInvalidated) throw new EditRejectedException("extension_source_uncommitted");
            var stamp = workspace.Stamp;
            var document = workspace.Owner.Document;
            using var read = document.ReadOnly();
            var result = new ExtensionSceneSummary(access.SessionId, stamp, document.SessionId, document.ObjectCount, state.Selection);
            access.RequireActive();
            if (stamp != workspace.Stamp) throw new EditRejectedException("extension_source_stale");
            return result;
        }
    }
}

// Detached fixed-size metadata only; no names, component JSON, runtime handles or secrets.
internal sealed record ExtensionSceneSummary(Guid ExtensionSession, EditorViewStamp Source, Guid Document, int Objects, Guid? Selection);

internal sealed unsafe partial class CandidatePresentation
{
    private EditorTrustedExtensions? _extensions;
    private void StartTrustedExtensions()
    {
        if (_extensions is not null) throw new InvalidOperationException("extension_already_owned");
        _extensions = new(new EditorWorkspace(_editor!), () => {
            VerifyGraphicsOwner();
            // Read the actual host close boundary, not the inner group's state.
            // This remains valid through the presenter's reference after _extensions
            // is cleared, including AI drain or downstream renderer close failure.
            return _graphicsShutdown && !_closed;
        }); // retained BEFORE Start can throw
        _extensions.Start();
        _presenter!.AttachTrustedExtensions(_extensions);
    }
    internal void RetryExtensionClose()
    {
        VerifyGraphicsOwner();
        if (!_graphicsShutdown || _extensions is null) throw new InvalidOperationException("extension_shutdown_required");
        _extensions.RetryClose();
    }
}
#endif
