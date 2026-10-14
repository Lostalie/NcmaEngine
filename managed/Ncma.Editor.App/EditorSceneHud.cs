using System.Numerics;
using Ncma.Application.Runtime;
using Ncma.Gameplay;
using Ncma.Platform;
using Ncma.Ui;
using Ncma.Ui.Rendering;

namespace Ncma.Editor.App;

internal sealed unsafe partial class CandidatePresentation
{
    private Ncma.Editor.Services.EditorUiPackagePreparation? _uiPackagePreparation;
    private Ncma.Editor.Services.EditorUiPublication? _uiPublication;
    private Ncma.Assets.UiPackageReference? _sceneHudSelection;
    private bool _hudReady=true;
    private bool _hudRecovery;
    private Guid _hudObservedWorld;
    private ulong _hudObservedRevision;
    private readonly List<IDisposable> _hudRetained=[];
    private SceneUiPresentation? _playHud;
    private readonly UiTextResources _uiTextResources=new();
    private PlaySession? _playHudOwner;
    private SceneUiInput? _sceneHudInput;
    private readonly UiWindowInput _sceneHudWindowInput = new();
    private (float X,float Y,float Width,float Height,uint WindowWidth,uint WindowHeight,uint PixelsWidth,uint PixelsHeight)? _hudInputGeometry;
    private InputEvent[]? _verificationWindowEvents;
    private ReadOnlySpan<InputEvent> WindowEvents => _verificationWindowEvents is { } input ? input : _window!.Events;
    // Test-only input seam feeds the ordinary accumulator and SubmitInput pipeline. It cannot grant
    // routes or execute callbacks; actual GUI, scene/HUD rendering and gameplay still run normally.
    internal void SetWindowInputForVerification(WindowState state, ReadOnlySpan<InputEvent> events)
    { _input.Update(state,events); _state=state; _verificationWindowEvents=events.ToArray(); }
    private SceneUiPresentation? ActiveSceneHud => !_hudReady?null:_editor?.Play is not null && uiActions is not null ? _playHud : _sceneHud;

    // Explicit committed revision boundary. No per-frame document serialization/file probing.
    private void RefreshEditSceneHud(Ncma.Scene.SceneDocumentSnapshot? startup=null)
    {
        if(project is null||_editor is null||_hudRecovery)return;
        if(startup is null){
            if(_editor.Play is not null)return;
            var doc=_editor.Document;Guid world=doc.World.Identity;ulong revision=doc.Revision;
            if(_hudObservedWorld==world&&_hudObservedRevision==revision)return;
            _hudObservedWorld=world;_hudObservedRevision=revision;startup=doc.CaptureSnapshot();
        }
        SceneUiPresentation? candidate=null;RuntimeUiPackage? package=null;bool retiring=false;
        try{
            var desired=Ncma.Scene.Rendering.SceneUiState.Resolve(startup,project.Configuration.UiPackage);
            if(desired==_sceneHudSelection&&_hudReady)return;
            if(desired is not null){
                if(_renderer is null)throw new NotSupportedException("Scene HUD requires graphical presentation.");
                if(_shaderFiles is not null)_=_shaderFiles.Get(Ncma.Rendering.ShaderProfile.Flat2D,false,false);
                package=RuntimeUiPackage.Open(project.Root,project.Configuration.ProjectId,desired);
                SceneUiPresentation.Validate(package,uiActions is not null);uiActions?.Validate(package.Capture());
                candidate=new(_uiTextResources);candidate.Prepare(package,_renderer,plugins,new(1280,720,1,default),interactive:uiActions is not null);
            }
            retiring=true;_sceneHud?.Dispose();_sceneHudPackage?.Dispose();
            _sceneHud=candidate;_sceneHudPackage=package;candidate=null;package=null;_sceneHudSelection=desired;_hudReady=true;
        }catch(Exception error){
            _hudReady=false; // Never display an old image as the newly committed selection.
            _hudRecovery|=retiring;
            if(candidate is not null){try{candidate.Dispose();}catch{_hudRecovery=true;_hudRetained.Add(candidate);if(package is not null)_hudRetained.Add(package);package=null;}}
            package?.Dispose();_log?.Write("error","ui.selection.prepare_failed",error.Message,_correlation);
        }
    }

    private void PreparePlayHud(PlaySession play)
    {
        ClosePlayHud();
        // Composition callback uses its supplied committed Play snapshot, never reenters owner.
        RefreshEditSceneHud(play.Document.CaptureSnapshot());if(!_hudReady)throw new InvalidOperationException("Current scene UI generation is not prepared.");
        if (_sceneHudPackage is null || uiActions is null) return;
        uiActions.Validate(_sceneHudPackage.Capture()); uiActions.Install(play);
        // Retain a failed candidate for explicit orderly disposal; do not fall back to Edit state.
        _playHud=new(_uiTextResources); _playHudOwner=play;
        _playHud.Prepare(_sceneHudPackage,_renderer!,plugins,new(1280,720,1,default),interactive:true);
    }
    private void SuspendSceneHudInput()
    {
        _sceneHudInput?.Dispose(); _sceneHudInput=null;
        _hudInputGeometry=null;
    }
    private void ClosePlayHud()
    {
        SuspendSceneHudInput(); _playHud?.Dispose(); _playHud=null; _playHudOwner=null;
    }
    private void SynchronizeSceneHud()
    {
        if(_editor?.Play is null)ClosePlayHud();
        RefreshEditSceneHud();
        if (_sceneHudPackage is null || uiActions is null) return;
        if (_editor!.Play is not { } play) { ClosePlayHud(); return; }
        if (_playHud is null || !ReferenceEquals(_playHudOwner,play)) throw new InvalidOperationException("Play HUD was not prepared before gameplay startup.");
        _sceneHudInput ??= new(_playHud.Runtime,play,uiActions);
    }
    // Called at the same owner-thread boundary as physical input, before shortcuts and simulation.
    // Internal for bounded native-window regression only; no GUI/Agent callback mutates the World.
    internal UiInputDisposition ProcessSceneHudInput(WindowState state, ReadOnlySpan<InputEvent> events)
    {
        if (_sceneHudInput is null || _playHud is null) return default;
        var area=_presenter?.Viewport??default;
        var geometry=(area.X,area.Y,area.Width,area.Height,state.Width,state.Height,state.FramebufferWidth,state.FramebufferHeight);
        bool resized=_hudInputGeometry is { } previous && previous!=geometry;
        _hudInputGeometry=geometry;
        if (_presenter?.SceneHudInputAllowed!=true || state.Minimized!=0 || state.FramebufferWidth==0 || state.FramebufferHeight==0 ||
            area.Width<=0 || area.Height<=0 || resized || _capture.Keyboard!=0 || _capture.CancelInteraction!=0 || !_sceneHudInput.Observe()) {
            _sceneHudInput.Cancel(); return new(true,true,true,true);
        }
        float scaleX=(float)state.FramebufferWidth/Math.Max(1,state.Width),scaleY=(float)state.FramebufferHeight/Math.Max(1,state.Height);
        uint width=Math.Clamp((uint)Math.Ceiling(area.Width*scaleX),1,4096),height=Math.Clamp((uint)Math.Ceiling(area.Height*scaleY),1,4096);
        var view=new UiViewport(width,height,scaleX,default);
        _playHud.PrepareView(view);
        var result=_sceneHudWindowInput.Pump(_playHud.Runtime,state,events,new Vector2(area.X*scaleX,area.Y*scaleY));
        if (result.Cancelled) _sceneHudInput.Cancel();
        else {
            try { _playHud.PrepareView(view); _sceneHudInput.Submit(_sceneHudWindowInput.Drain(_playHud.Runtime)); }
            catch { _sceneHudInput.Cancel(); throw; }
        }
        return result;
    }

    // Internal test observer only. Capture the final GUI-composited viewport, not merely
    // the 3D target: a separate target capture cannot prove that the HUD reaches the user.
    private Action<byte[],uint,uint>? _verificationComposite;
    internal void CaptureNextCompositeViewportForVerification(Action<byte[],uint,uint> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (_verificationComposite is not null) throw new InvalidOperationException("Composite capture already pending.");
        _verificationComposite=capture;
    }
    private void CaptureVerificationComposite()
    {
        if (_verificationComposite is null) return;
        var area=_presenter?.Viewport??throw new InvalidOperationException("Scene viewport required.");
        if (_presenter.UiWorkspaceActive||_presenter.GraphWorkspaceActive) throw new InvalidOperationException("Scene workspace required.");
        var width=_state.FramebufferWidth;var height=_state.FramebufferHeight;
        uint x=(uint)Math.Ceiling(area.X*width/Math.Max(_state.Width,1));
        uint y=(uint)Math.Ceiling(area.Y*height/Math.Max(_state.Height,1));
        uint w=(uint)Math.Ceiling(area.Width*width/Math.Max(_state.Width,1));
        uint h=(uint)Math.Ceiling(area.Height*height/Math.Max(_state.Height,1));
        if (x>=width||y>=height||w==0||h==0||w>width-x||h>height-y) throw new InvalidOperationException("Viewport capture bounds.");
        var full=new byte[checked((int)(width*height*4))];_renderer!.Capture(full);
        var cropped=new byte[checked((int)(w*h*4))];
        for(uint row=0;row<h;row++)full.AsSpan(checked((int)(((y+row)*width+x)*4)),checked((int)(w*4))).CopyTo(cropped.AsSpan(checked((int)(row*w*4))));
        var callback=_verificationComposite;_verificationComposite=null;callback(cropped,w,h);
    }
}
