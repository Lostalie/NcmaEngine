using System.Numerics;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Text;
using Ncma.Ui;
using Ncma.Ui.Rendering;

namespace Ncma.Editor.Services;

// Derived presentation only. No World, Play, editor command or file IO in Produce/Acquire.
public sealed class EditorUiPreview(RendererSession renderer,string plugins) : IDisposable
{
    private readonly PluginLoader _loader=new(); private TextService? _text;
    private UiCanvas? _canvas; private UiRenderTarget? _target; private UiPresentationLease? _lease;
    private readonly List<IDisposable> _retained=[];
    private EditorUiStamp _stamp; private UiViewport _viewport; private ulong _content,_produced, _runtimeRevision=ulong.MaxValue;
    private bool _dirty,_disposed,_recovery;
    public ulong Preparations { get; private set; }
    public UiLayoutBox[] Boxes { get; private set; }=[];
    public UiRuntime? Runtime=>_canvas?.Runtime;
    public bool RecoveryRequired=>_recovery;
    public bool Prepared {get;private set;}
    public TextService Text { get {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(_text is null){_loader.Load(plugins,[new("ncma.text",ModuleKind.Text,"NcmaText.dll","NcmaText.dll",1,0,[])]);_text=new(_loader.Modules.Single());}
        return _text;
    } }
    public void Prepare(EditorUiWorkspace workspace,UiViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_recovery)throw new InvalidOperationException("UI preview recovery required; explicitly close owners before unload.");
        viewport.Validate();var stamp=workspace.Stamp;
        if(stamp.Document==Guid.Empty){CloseCanvas();_stamp=stamp;return;}
        if(_canvas is not null&&stamp==_stamp&&viewport==_viewport&&_canvas.Runtime.PresentationRevision==_runtimeRevision)return;
        if(_lease is not null)throw new InvalidOperationException("Release previous UI presentation first.");Prepared=false;
        if(_canvas is null||stamp!=_stamp){
            var definition=workspace.Capture()!;workspace.Resources.Require(definition);UiCanvas? candidate=null;
            try {
                candidate=UiCanvas.Create(new(definition),renderer,definition.Elements.Any(e=>e.Font!=Guid.Empty)?Text:null,workspace.Resources.Fonts,workspace.Resources.Images);
                candidate.Prepare(viewport);try{_canvas?.Dispose();}catch{_recovery=true;throw;}_canvas=candidate;
            }catch(UiCanvasRecoveryException failure){_retained.Add(failure.Owner);_recovery=true;throw;}
            catch { if(candidate is not null){try{candidate.Dispose();}catch{_retained.Add(candidate);_recovery=true;throw;}}throw; }
        }else _canvas.Prepare(viewport);
        uint width=checked((uint)Math.Ceiling(viewport.Width)),height=checked((uint)Math.Ceiling(viewport.Height));
        if(_target is null||_target.Width!=width||_target.Height!=height){
            var candidate=renderer.CreateUiTarget(width,height);
            try{_target?.Dispose();}catch{_retained.Add(candidate);_recovery=true;throw;}_target=candidate;
        }
        _stamp=stamp;_viewport=viewport;_runtimeRevision=_canvas.Runtime.PresentationRevision;
        Boxes=_canvas.Runtime.Layout(viewport).ToArray();_dirty=true;Preparations++;Prepared=true;
    }
    public GuiCachedImageToken? Acquire(ulong frame)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_recovery)throw new InvalidOperationException("UI preview recovery required.");
        if(!Prepared||_canvas is null||_target is null)return null;if(_lease is not null)throw new InvalidOperationException("UI lease still owned.");
        if(_dirty){_canvas.Produce(_target,frame,checked(_content+1),new(.04f,.055f,.075f,1));_content++;_produced=frame;_dirty=false;}
        _lease=renderer.AcquireUiPresentation(_target,_content,frame);return _lease.Token;
    }
    public byte[] Capture() => _target is not null&&_produced>0?renderer.CaptureUiTarget(_target):throw new InvalidOperationException("UI not produced.");
    public void ReleasePresentation(){_lease?.Dispose();_lease=null;}
    public void ResetRuntime(){if(_lease is not null)throw new InvalidOperationException("Release UI presentation before reset.");_stamp=default;Prepared=false;}
    private void CloseCanvas(){try{ReleasePresentation();_target?.Dispose();_target=null;_canvas?.Dispose();_canvas=null;Boxes=[];_dirty=false;_produced=0;Prepared=false;}catch{_recovery=true;Prepared=false;throw;}}
    public void Dispose(){if(_disposed)return;CloseCanvas();while(_retained.Count>0){_retained[0].Dispose();_retained.RemoveAt(0);}_text?.Dispose();_text=null;_loader.Dispose();_disposed=true;}
}
