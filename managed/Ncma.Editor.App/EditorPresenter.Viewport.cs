using System.Globalization;
using Ncma.Editor.Services;
using Ncma.Editor.Core;
using Ncma.Gui;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private EditorOrbitCamera _orbit=new();
    private SceneRenderSession? _pickScene;
    private Guid _pickView;
    private ulong _pickFrame;
    private double? _pendingScrub;
    private double _previewScrub;
    public bool EditPreviewPaused {get;private set;}
    public SceneCameraView BrowserCamera(uint width,uint height)=>_orbit.View(width,height);
    public double? ConsumeScrub(){var value=_pendingScrub;_pendingScrub=null;return value;}
    public void SetViewportFrame(SceneRenderSession? scene,ulong frame)
    {
        _pickScene=scene is {Costs.GpuSubmitted:true}?scene:null;_pickView=_pickScene?.View?.FrameIdentity??Guid.Empty;_pickFrame=frame;
    }
    private void BuildBrowserControls()
    {
        Add(GuiItemKind.Number,19,1,"Browser yaw",new("browser_yaw"),number:_orbit.Yaw,min:-MathF.PI,max:MathF.PI);
        Add(GuiItemKind.Number,19,2,"Browser pitch",new("browser_pitch"),number:_orbit.Pitch,min:-1.5,max:1.5);
        Add(GuiItemKind.Number,19,3,"Browser distance",new("browser_zoom"),number:_orbit.Distance,min:.05,max:10000);
        Add(GuiItemKind.Button,19,4,"Pan left",new("browser_pan",Index:-1));Line();Add(GuiItemKind.Button,19,5,"Pan right",new("browser_pan",Index:1));
        Add(GuiItemKind.Button,19,6,"Pan up",new("browser_pan",Index:2));Line();Add(GuiItemKind.Button,19,7,"Pan down",new("browser_pan",Index:-2));
        Add(GuiItemKind.Button,19,8,"Reset independent camera",new("browser_reset"));
        Add(GuiItemKind.Checkbox,19,9,"Pause isolated Edit animation clock",new("scene_preview_pause"),number:EditPreviewPaused?1:0,max:1,enabled:workspace.Owner.Play is null);
        Add(GuiItemKind.Number,19,10,"Edit clip scrub seconds (nonpersistent)",new("scene_preview_scrub"),number:_previewScrub,min:0,max:600,enabled:workspace.Owner.Play is null);
    }
    private bool ApplyViewportAction(ActionView action,double value,string text,EditorViewStamp stamp)
    {
        switch(action.Kind) {
            case "viewport":
                if(workspace.Owner.Play is not null||workspace.HasDraft||_pickFrame!=_frame.Frame)throw new InvalidOperationException("stale_or_frozen_pick");
                if(value==1) {
                    if(_assetWorkflow is null||!Guid.TryParseExact(text,"D",out Guid id))throw new ArgumentException("Invalid model drop UUID.");
                    Ncma.Assets.AssetRecord root;
                    try{root=_assetWorkflow.Inspect(id,Ncma.Assets.AssetKind.Character);}catch(EditRejectedException){root=_assetWorkflow.Inspect(id,Ncma.Assets.AssetKind.StaticMesh);}
                    _assetWorkflow.PreparePlacement(stamp,id,root.Kind,_placement);return true;
                }
                if(value!=0)throw new ArgumentException("Invalid viewport intent.");
                var uv=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
                if(uv.Length!=2||!float.TryParse(uv[0],NumberStyles.Float,CultureInfo.InvariantCulture,out float u)||!float.TryParse(uv[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float v))throw new ArgumentException("Invalid pick coordinates.");
                if(!float.IsFinite(u)||!float.IsFinite(v)||u is <0 or >1||v is <0 or >1)throw new ArgumentException("Pick range.");
                Guid? picked=_pickScene?.Pick(_pickFrame,_pickView,u,v);
                workspace.Select(stamp,picked);CancelInteraction();return true;
            case "browser_yaw":_orbit.Orbit((float)value,_orbit.Pitch);break;
            case "browser_pitch":_orbit.Orbit(_orbit.Yaw,(float)value);break;
            case "browser_zoom":_orbit.Zoom((float)value);break;
            case "browser_pan":_orbit.Pan(Math.Abs(action.Index)==1?Math.Sign(action.Index)*_orbit.Distance*.1f:0,Math.Abs(action.Index)==2?Math.Sign(action.Index)*_orbit.Distance*.1f:0);break;
            case "browser_reset":_orbit=new();break;
            case "scene_preview_pause":if(workspace.Owner.Play is not null)throw new InvalidOperationException("play_preview_isolated");EditPreviewPaused=value!=0;return true;
            case "scene_preview_scrub":if(workspace.Owner.Play is not null||value is <0 or >600)throw new ArgumentException("Invalid Edit scrub.");_previewScrub=value;_pendingScrub=value;EditPreviewPaused=true;return true;
            default:return false;
        }
        SceneCamera=Guid.Empty;_pickScene=null;_generation=checked(_generation+1);return true;
    }
}
