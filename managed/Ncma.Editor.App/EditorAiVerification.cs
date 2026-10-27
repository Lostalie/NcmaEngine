#if NCMA_AI
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

namespace Ncma.Editor.App;

internal sealed unsafe partial class CandidatePresentation
{
    // Internal trusted harness seam, configured before Start; no CLI/IPC/tool surface.
    internal Func<EditorDomainProposals?, AiAuthoringWorkspace>? AiFactoryForVerification { private get; init; }
    private AiAuthoringWorkspace CreateAiAuthoring(EditorDomainProposals? domains) =>
        AiFactoryForVerification is { } factory ? factory(domains) : new(domains, _uiWorkspace, _animationGraphs,
            _editor!.Assets is null ? null : _editor.CreateMetadataContentReview());
    internal AiAuthoringWorkspace VerificationAi { get { VerifyGraphicsOwner(); return _aiAuthoring ?? throw new InvalidOperationException("AI owner absent."); } }
    internal (bool Closed, bool Closing, object? Editor, object? Gui, object? Renderer, object? Window, object? Physics, int Modules) VerificationAiLifetime {
        get { VerifyGraphicsOwner(); return (_closed, _graphicsShutdown, _editor, _gui, _renderer, _window, _physics, _closed ? 0 : _loader.Modules.Count); }
    }
}
#endif
