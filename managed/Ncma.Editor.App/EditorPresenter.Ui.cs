using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ncma.Asset.Import;
using Ncma.Assets.Authoring;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Ui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private EditorUiWorkspace? _ui; private EditorUiPreview? _uiPreview;
    private bool _uiMode,_uiTest,_uiWrite,_uiFocusCanvas; private string _uiPath="assets/main.ncmaui",_uiConfirm="",_uiDelete="";
    private UiDefinition? _uiDefinition; private ulong _uiRevision=ulong.MaxValue,_uiResources=ulong.MaxValue;
    private UiElement[] _uiLayers=[];private readonly Dictionary<Guid,int> _uiDepth=[];private readonly Dictionary<Guid,ulong> _uiRows=[];private ulong _uiRowId;
    private ulong _uiControllerPreparation=ulong.MaxValue;private int _uiCanvasEndIndex;
    private Guid _uiSelectionAnchor;
    private sealed record UiTextPage(UiElement Element,JsonTextPages Pages,int Page);
    private UiTextPage? _uiTextCache,_uiTextEdit;private int _uiTextPage;
    private EditorUiStamp? _uiFailedPreparation;private UiViewport _uiFailedViewport;
    private readonly UiCanvasController _uiController=new(); private UiCanvasController? _uiGesture;
    private Vector2 _uiStart,_uiLast; private bool _uiPan,_uiMarquee; private int _uiModifiers,_uiOffset;
    private (ulong High,ulong Low)? _uiActiveInput;
    private float _uiWidth=1024,_uiHeight=640,_uiDpi=1,_uiSafe;
    private ulong _uiSequence; private readonly Queue<string> _uiObservations=[];
    private string _resourcePath="",_resourceLicense="",_resourceConfirm="",_resourceId="";private bool _resourceRedistribute;
    private UiResourceReview? _resourceReview;
    private string _tokenId="",_tokenName="Color",_tokenColor="0.1 0.4 0.8 1";private float _tokenScalar=1;private bool _tokenIsScalar;
    private readonly record struct UiIntent(EditorUiStamp Stamp,object? Payload=null);
    public bool UiWorkspaceActive=>_uiMode;
    public UiViewport UiPreviewViewport=>new(_uiWidth,_uiHeight,_uiDpi,new(_uiSafe,_uiSafe,_uiSafe,_uiSafe));
    public void AttachUi(EditorUiWorkspace ui,EditorUiPreview? preview){_ui=ui;_uiPreview=preview;}
    public void SynchronizeUi()
    {
        if(_ui is null)return;
        try{_ui.Synchronize();if(_uiRevision!=_ui.Revision||_uiResources!=_ui.Resources.Revision){Guid previous=_uiDefinition?.AssetId??Guid.Empty;_uiDefinition=_ui.Capture();_uiRevision=_ui.Revision;_uiResources=_ui.Resources.Revision;if(previous!=(_uiDefinition?.AssetId??Guid.Empty)){_uiRows.Clear();_uiSelectionAnchor=Guid.Empty;_uiController.PruneSelection([]);}else _uiController.PruneSelection(_uiDefinition?.Elements.Select(e=>e.Id)??[]);CacheUiLayers();if(!_ui.HasDraft)_generation++;}
            if(_uiMode&&_uiPreview is not null){if(_uiFailedPreparation==_ui.Stamp&&_uiFailedViewport==UiPreviewViewport)return;_uiPreview.Prepare(_ui,UiPreviewViewport);_uiFailedPreparation=null;if(_uiDefinition is not null&&_uiGesture is null&&_uiControllerPreparation!=_uiPreview.Preparations){_uiController.Load(_uiDefinition,_uiPreview.Boxes);_uiControllerPreparation=_uiPreview.Preparations;}}
        }catch(Exception e) when(e is not OutOfMemoryException&&e is not PluginException{Result:PluginResult.DeviceLost or PluginResult.InternalError}){_uiFailedPreparation=_ui.Stamp;_uiFailedViewport=UiPreviewViewport;CancelUi();Record("UI: "+e.Message);}
    }
    private void CacheUiLayers(){
        _uiDepth.Clear();var rows=new List<UiElement>();if(_uiDefinition is not null){var children=_uiDefinition.Elements.Where(e=>e.Parent!=Guid.Empty).GroupBy(e=>e.Parent).ToDictionary(g=>g.Key,g=>g.OrderBy(e=>e.Order).ToArray());Walk(_uiDefinition.Elements.Single(e=>e.Id==_uiDefinition.Root),0);void Walk(UiElement e,int depth){_uiDepth[e.Id]=depth;rows.Add(e);foreach(var child in children.GetValueOrDefault(e.Id,[]))Walk(child,depth+1);}}
        _uiLayers=rows.ToArray();foreach(var id in _uiRows.Keys.Where(id=>!_uiDepth.ContainsKey(id)).ToArray())_uiRows.Remove(id);_uiOffset=Math.Min(_uiOffset,Math.Max(0,(_uiLayers.Length-1)/32*32));
    }
    private void CancelUi(){_ui?.Cancel();_uiGesture=null;_uiActiveInput=null;_uiTextEdit=null;_uiPan=_uiMarquee=false;if(_uiPreview?.Runtime is {} runtime&&_uiPreview.Prepared){runtime.Input(runtime.Stamp,[new(UiInputKind.FocusLost,++_uiSequence,default)]);}}
    private GuiFrame BuildUi(ulong frameId)
    {
        var ui=_ui!;ulong label=41000;bool writable=ui.Writable&&!_uiTest;
        if(_uiFocusCanvas)Viewport=(8,90,Math.Max(1,_geometry.Status.Width-16),Math.Max(1,_geometry.Status.Y-98));
        Viewport=(Viewport.X,Viewport.Y+32,Viewport.Width,Math.Max(1,Viewport.Height-32));
        if(!_uiFocusCanvas){
        Region(201,"UI 图层",_geometry.Objects);
        Add(GuiItemKind.Label,30,label++,ui.Path??"尚未打开 .ncmaui");
        UiButton(1,"新建 / 打开 / 审批","ui_files");
        if(_uiDefinition is not null){
            var all=_uiLayers;
            foreach(var e in all.Skip(_uiOffset).Take(32))Add(GuiItemKind.SelectionButton,31,10000+Row(e.Id),( _uiController.Selection.Contains(e.Id)?"[x] ":"[ ] ")+new string(' ',Depth(e)*2)+e.Name+(e.Locked?" [锁定]":""),new("ui_select",Operation:new UiIntent(ui.Stamp,e.Id)));
            UiButton(2,"上一页","ui_previous");Line();UiButton(3,"下一页","ui_next",enabled:_uiOffset+32<all.Length);
            foreach(var kind in new[]{UiKind.Rectangle,UiKind.Text,UiKind.Image,UiKind.Button,UiKind.Slider,UiKind.TextInput,UiKind.ScrollView,UiKind.Group})UiButton(20+(ulong)kind,"+ "+kind,"ui_add",kind,enabled:writable&&(kind!=UiKind.Image||ui.Resources.Images.Count>0));
        }
        End();
        }
        Region(202,"UI 画布",new(Viewport.X-8,Viewport.Y-74,Viewport.Width+16,Viewport.Height+82));
        var canvasPanel=_items[^1];canvasPanel.Value=4;_items[^1]=canvasPanel;
        UiButton(4,"场景工作区","ui_switch");Line();UiButton(5,_uiTest?"退出独立测试":"独立测试","ui_test",enabled:ui.DocumentId!=Guid.Empty&&_uiPreview?.Prepared==true&&!ui.HasDraft);
        Line();UiButton(87,_uiFocusCanvas?"恢复面板":"聚焦画布","ui_focus_workspace");
        if(ui.DocumentId!=Guid.Empty&&_uiPreview?.Prepared!=true)Add(GuiItemKind.Label,30,label++,"UI 预览不可用："+(_uiPreview?.RecoveryRequired==true?"资源恢复待明确关闭，不能自动重试":_uiPreview is null?"图形预览服务未启用":LastMessage.Length>0?LastMessage:"请批准文档需要的字体/图像资源。"));
        _uiCanvasEndIndex=_items.Count;End();
        if(!_uiFocusCanvas){
        Region(203,"UI 检查器",_geometry.Inspector);
        if(_uiDefinition is not null){
            var selected=_uiController.Selection;
            Add(GuiItemKind.Label,30,label++,"UI 选择 "+selected.Length+" / 与场景选择独立");
            if(selected.Length==1){
                var e=_uiDefinition.Elements.Single(i=>i.Id==selected[0]);bool edit=writable&&!_uiController.Locked(e.Id);Add(GuiItemKind.Label,30,label++,e.Id.ToString("D"));
                Field(100,"名称","Name",e.Name,edit);
                if(e.Kind is UiKind.Text or UiKind.Button or UiKind.TextInput){
                    if(e.Text.Length==0&&_uiTextEdit is null)Field(101,"文本","Text",e.Text,edit);
                    else{var page=_uiTextEdit;if(page is null){if(_uiTextCache is null||_uiTextCache.Element.Id!=e.Id||_uiTextCache.Element.Text!=e.Text)_uiTextCache=new(e,new(e.Text),0);_uiTextPage=Math.Clamp(_uiTextPage,0,_uiTextCache.Pages.Count-1);page=_uiTextCache with{Page=_uiTextPage};}
                        UiButton(108,"上一页文字","ui_text_previous",enabled:page.Page>0&&!ui.HasDraft);Line();UiButton(109,"下一页文字","ui_text_next",enabled:page.Page+1<page.Pages.Count&&!ui.HasDraft);
                        Add(GuiItemKind.Text,31,101,$"文本页 {page.Page+1}/{page.Pages.Count}（完整文字一起验证）",new("ui_text_page",Operation:new UiIntent(ui.Stamp,page)),page.Pages.Text(page.Page),enabled:edit);}
                }
                Field(102,"字体 UUID","Font",e.Font==Guid.Empty?"":e.Font.ToString("D"),edit);Field(103,"图像 UUID","Image",e.Image==Guid.Empty?"":e.Image.ToString("D"),edit);
                Field(104,"语义 Action（不执行代码）","Action",e.Action,edit);
                Add(GuiItemKind.Label,30,label++,"已注册语义："+string.Join(", ",ui.RegisteredActions));
                foreach(var f in new[]{("X",e.Layout.X,-65536f,65536f),("Y",e.Layout.Y,-65536f,65536f),("Width",e.Layout.Width,1f,2048f),("Height",e.Layout.Height,1f,2048f),("Rotation",e.Layout.Rotation,-360f,360f),("FontSize",e.FontSize,1f,256f),("Opacity",e.Style.Opacity,0f,1f),("CornerRadius",e.Style.CornerRadius,0f,1024f),("Gap",e.Layout.Gap,0f,1024f)})
                    Add(GuiItemKind.Number,31,300+(ulong)Array.IndexOf(new[]{"X","Y","Width","Height","Rotation","FontSize","Opacity","CornerRadius","Gap"},f.Item1),f.Item1,new("ui_field",e.Id,f.Item1,Operation:new UiIntent(ui.Stamp,e)),number:f.Item2,min:f.Item3,max:f.Item4,enabled:edit&&e.Id!=_uiDefinition.Root);
                foreach(var f in new[]{("Flow",(int)e.Layout.Flow),("WidthMode",(int)e.Layout.WidthMode),("HeightMode",(int)e.Layout.HeightMode)})Add(GuiItemKind.Number,31,(ulong)(f.Item1=="Flow"?350:f.Item1=="WidthMode"?351:352),f.Item1+"：0/1/2",new("ui_field",e.Id,f.Item1,Operation:new UiIntent(ui.Stamp,e)),number:f.Item2,min:0,max:2,enabled:edit&&e.Id!=_uiDefinition.Root);
                foreach(var f in new[]{("AnchorX",e.Layout.AnchorX,0f,1f),("AnchorY",e.Layout.AnchorY,0f,1f),("MinWidth",e.Layout.MinWidth,0f,2048f),("MinHeight",e.Layout.MinHeight,0f,2048f),("MaxWidth",e.Layout.MaxWidth,1f,65536f),("MaxHeight",e.Layout.MaxHeight,1f,65536f),("PaddingLeft",e.Layout.Padding.Left,0f,2048f),("PaddingTop",e.Layout.Padding.Top,0f,2048f),("PaddingRight",e.Layout.Padding.Right,0f,2048f),("PaddingBottom",e.Layout.Padding.Bottom,0f,2048f)})Add(GuiItemKind.Number,31,400+(ulong)Array.IndexOf(new[]{"AnchorX","AnchorY","MinWidth","MinHeight","MaxWidth","MaxHeight","PaddingLeft","PaddingTop","PaddingRight","PaddingBottom"},f.Item1),f.Item1,new("ui_field",e.Id,f.Item1,Operation:new UiIntent(ui.Stamp,e)),number:f.Item2,min:f.Item3,max:f.Item4,enabled:edit&&e.Id!=_uiDefinition.Root);
                foreach(var f in new[]{("Visible",e.Visible),("Enabled",e.Enabled),("Locked",e.Locked),("Clip",e.Style.Clip)})Add(GuiItemKind.Checkbox,31,(ulong)(f.Item1=="Visible"?360:f.Item1=="Enabled"?361:f.Item1=="Locked"?362:363),f.Item1,new("ui_field",e.Id,f.Item1,Operation:new UiIntent(ui.Stamp,e)),number:f.Item2?1:0,max:1,enabled:writable);
                Field(105,"Fill RGBA / 0..1","Fill",FormattableString.Invariant($"{e.Style.Fill.R} {e.Style.Fill.G} {e.Style.Fill.B} {e.Style.Fill.A}"),edit);
                Field(106,"Foreground RGBA","Foreground",FormattableString.Invariant($"{e.Style.Foreground.R} {e.Style.Foreground.G} {e.Style.Foreground.B} {e.Style.Foreground.A}"),edit);
                foreach(var token in new[]{("FillToken",e.Style.FillToken),("ForegroundToken",e.Style.ForegroundToken),("OpacityToken",e.Style.OpacityToken)})Field(110+(ulong)(token.Item1=="FillToken"?0:token.Item1=="ForegroundToken"?1:2),token.Item1,token.Item1,token.Item2==Guid.Empty?"":token.Item2.ToString("D"),edit);
                UiButton(6,"图层前移","ui_order",(e.Id,1),writable);Line();UiButton(7,"图层后移","ui_order",(e.Id,-1),writable);
                Add(GuiItemKind.Text,31,8,"确认删除 UI 子树 UUID",new("ui_delete_text",Operation:new UiIntent(ui.Stamp)),_uiDelete);
                UiButton(9,"删除确认的 UI 子树","ui_delete",e.Id,writable&&Guid.TryParse(_uiDelete,out var id)&&id==e.Id&&e.Id!=_uiDefinition.Root);
                void Field(ulong id,string name,string field,string value,bool enabled)=>Add(GuiItemKind.Text,31,id,name,new("ui_field",e.Id,field,Operation:new UiIntent(ui.Stamp,e)),value,enabled:enabled&&Encoding.UTF8.GetByteCount(value)<=1023);
            }
            if(selected.Length>1){var elements=_uiController.TopSelection();bool edit=writable&&elements.All(e=>e.Id!=_uiDefinition.Root&&!_uiController.Locked(e.Id));bool mixed=elements.Select(e=>e.Style.Opacity).Distinct().Count()>1;
                Add(GuiItemKind.Label,30,label++,mixed?"Opacity 混合值；修改会统一设置":"共享样式");
                Add(GuiItemKind.Number,31,420,"Opacity",new("ui_field",Field:"Opacity",Operation:new UiIntent(ui.Stamp,elements)),number:elements[0].Style.Opacity,min:0,max:1,enabled:edit);
                Add(GuiItemKind.Checkbox,31,421,"Visible（混合时统一设置）",new("ui_field",Field:"Visible",Operation:new UiIntent(ui.Stamp,elements)),number:elements.All(e=>e.Visible)?1:0,max:1,enabled:edit);
            }
        }
        End();
        Region(204,"UI 资源 / 文档",_geometry.Assets);
        Add(GuiItemKind.Text,31,10,"项目相对 .ncmaui 路径",new("ui_path",Operation:new UiIntent(ui.Stamp)),_uiPath);
        UiButton(11,"审阅新建","ui_review_new",enabled:!_page!.State.Frozen&&!ui.HasDraft);Line();UiButton(12,"审阅打开","ui_review_open",enabled:!ui.HasDraft);
        UiButton(18,"选择新建路径…","ui_choose_new",enabled:filePicker is not null&&!ui.HasDraft);Line();UiButton(19,"选择 UI 文件…","ui_choose_open",enabled:filePicker is not null&&!ui.HasDraft);
        if(ui.Review is {} review){Add(GuiItemKind.Label,30,label++,review.Path+"\nUUID "+review.Document+"\nSHA256 "+review.Hash);
            foreach(var dependency in ui.ReviewDependencies.Take(64))Add(GuiItemKind.Label,30,label++,$"依赖 {dependency.Kind}: {dependency.Id}");
            Add(GuiItemKind.Checkbox,31,13,"批准此精确文件的写入（只读打开无需勾选）",new("ui_write",Operation:new UiIntent(ui.Stamp)),number:_uiWrite?1:0,max:1,enabled:!_page!.State.Frozen);
            Add(GuiItemKind.Text,31,14,"输入文档 UUID 确认",new("ui_confirm_text",Operation:new UiIntent(ui.Stamp)),_uiConfirm);
            UiButton(15,"确认此文件","ui_confirm",review,Guid.TryParse(_uiConfirm,out var confirmed)&&confirmed==review.Document&&(!review.Create||_uiWrite));}
        UiButton(16,"撤销文件写权限","ui_revoke");Line();UiButton(17,"保存当前草稿 / 校验已保存文件","ui_save",enabled:ui.DocumentId!=Guid.Empty&&!_uiTest);
        Add(GuiItemKind.Text,31,50,"字体 / 图像相对路径",new("ui_resource_path",Operation:new UiIntent(ui.Stamp)),_resourcePath);
        Add(GuiItemKind.Text,31,57,"资源 UUID（空白新建；重启时输入文档依赖 UUID）",new("ui_resource_id",Operation:new UiIntent(ui.Stamp)),_resourceId);
        Add(GuiItemKind.Text,31,51,"字体许可证声明",new("ui_resource_license",Operation:new UiIntent(ui.Stamp)),_resourceLicense);
        Add(GuiItemKind.Checkbox,31,52,"声明允许字体再分发（非法律校验）",new("ui_resource_redistribute",Operation:new UiIntent(ui.Stamp)),number:_resourceRedistribute?1:0,max:1);
        UiButton(53,"审阅字体","ui_resource_review",UiResourceKind.Font,!ui.HasDraft&&_uiPreview is not null);Line();UiButton(54,"审阅图像","ui_resource_review",UiResourceKind.Image,!ui.HasDraft&&_uiPreview is not null);
        if(_resourceReview is {} resource){Add(GuiItemKind.Label,30,label++,$"{resource.Kind}: {resource.Source}\nUUID {resource.Id}\nSHA256 {resource.Hash}\n许可 {resource.License}");Add(GuiItemKind.Text,31,55,"确认资源 UUID",new("ui_resource_confirm_text",Operation:new UiIntent(ui.Stamp)),_resourceConfirm);UiButton(56,"批准资源","ui_resource_confirm",resource,Guid.TryParse(_resourceConfirm,out var rid)&&rid==resource.Id);}
        foreach(var row in ui.Resources.Rows.Take(64))Add(GuiItemKind.Label,30,label++,$"{row.Kind} {row.Id} {row.Source}");
        End();
        Region(205,"UI 工具 / 预览观察",_geometry.Console);
        foreach(var tool in Enum.GetValues<UiCanvasTool>())UiButton(60+(ulong)tool,tool.ToString(),"ui_tool",tool);
        foreach(var op in new[]{("左对齐","alignx"),("顶对齐","aligny"),("水平分布","distx"),("垂直分布","disty"),("Group","group")})UiButton(70+(ulong)Array.IndexOf(new[]{"alignx","aligny","distx","disty","group"},op.Item2),op.Item1,"ui_geometry",op.Item2,writable);
        UiButton(75,"适应画布","ui_fit");Line();UiButton(76,"1:1","ui_reset");Line();UiButton(77,"聚焦选择","ui_focus");
        UiButton(78,"重置隔离预览","ui_preview_reset",enabled:!ui.HasDraft&&_uiPreview is not null);
        foreach(var f in new[]{("Width",_uiWidth,64f,2048f),("Height",_uiHeight,64f,2048f),("DPI",_uiDpi,.25f,4f),("Safe",_uiSafe,0f,32f)})Add(GuiItemKind.Number,31,80+(ulong)Array.IndexOf(new[]{"Width","Height","DPI","Safe"},f.Item1),"预览 "+f.Item1,new("ui_preview_size",Field:f.Item1,Operation:new UiIntent(ui.Stamp)),number:f.Item2,min:f.Item3,max:f.Item4,enabled:!ui.HasDraft);
        (float Width,float Height)[] presets=[(1280,720),(1920,1080),(1080,1920),(640,360)];for(int i=0;i<presets.Length;i++)UiButton((ulong)(120+i),$"预设 {presets[i].Width}×{presets[i].Height}","ui_preset",presets[i],!ui.HasDraft);
        UiButton(84,"新建颜色 Token","ui_token",enabled:writable);foreach(var token in _uiDefinition?.Tokens.Take(8)??[])Add(GuiItemKind.Label,30,label++,$"{token.Name}: {token.Id}");
        foreach(var f in new[]{(500UL,"Token UUID（留空新建）","ui_token_id",_tokenId),(501UL,"Token 名称","ui_token_name",_tokenName),(502UL,"Token RGBA","ui_token_color",_tokenColor)})Add(GuiItemKind.Text,31,f.Item1,f.Item2,new(f.Item3,Operation:new UiIntent(ui.Stamp)),f.Item4);
        Add(GuiItemKind.Checkbox,31,503,"Scalar Token",new("ui_token_kind",Operation:new UiIntent(ui.Stamp)),number:_tokenIsScalar?1:0,max:1);
        Add(GuiItemKind.Number,31,504,"Scalar",new("ui_token_scalar",Operation:new UiIntent(ui.Stamp)),number:_tokenScalar,min:0,max:1);
        UiButton(505,"保存 Token","ui_token_save",enabled:writable);Line();UiButton(506,"移除未引用 Token","ui_token_remove",enabled:writable&&Guid.TryParse(_tokenId,out _));
        Add(GuiItemKind.Label,30,label++,"拖拽：移动/调整/旋转；Ctrl 多选；Shift 框选；空格/中键平移；滚轮缩放；Esc 取消。\n自动布局使用图层排序；Hug/Fill 调整前需显式切为 Fixed。");
        foreach(var text in _uiObservations)Add(GuiItemKind.Label,30,label++,text);foreach(var text in _messages.TakeLast(4))Add(GuiItemKind.Label,30,label++,text);End();
        }
        if(!_uiFocusCanvas&&_geometry.Ai.Width>0){Region(93,"AI 工具",_geometry.Ai,"未接入推理服务");Add(GuiItemKind.Label,30,label++,"UI Agent 写入未开放。现有场景/MCP 审批请返回场景工作区；切换不授予权限。");var authorization=_authorizationController.Capture();Add(GuiItemKind.Label,30,label++,"MCP: "+(authorization is not null?"本机端点启用":"关闭 / 默认拒绝"));if(authorization is not null){Add(GuiItemKind.Label,30,label++,authorization.Endpoint.DescriptorPath);foreach(var connection in authorization.Endpoint.Connections.Take(4))Add(GuiItemKind.Label,30,label++,$"{connection.ClientName}: paired={connection.Paired}; connected={connection.Connected}");foreach(var grant in authorization.Grants.Take(4))Add(GuiItemKind.Label,30,label++,$"Grant {grant.ConnectionId}: {grant.RemainingSeconds}s");}UiButton(85,"返回场景审批","ui_switch");End();}
        Region(94,"状态",_geometry.Status);Add(GuiItemKind.Label,30,label++,$"UI | {(ui.HasDraft?"草稿未提交":ui.Writable?"精确文件可写":"只读")} | {(_uiTest?"隔离测试":"编辑")} | 缩放 {_uiController.Zoom:P0} | Play {_page!.Play?.State.ToString()??"未运行"}");End();WorkspaceMenu(ref label,!_page.State.Frozen&&!_page.State.EditBusy);
        BuildWorkflows(_geometry.Status.Width,_geometry.Status.Y+28,ref label);
        if(!_uiFocusCanvas)WorkspaceSplitters();_frame=new(){StructSize=(uint)Marshal.SizeOf<GuiFrame>(),Frame=frameId,ViewGeneration=_generation,DocumentGeneration=_page.Stamp.Generation,Revision=_page.Stamp.Revision,ItemCount=(uint)_items.Count,TextBytes=(uint)_text.Count};return _frame;
        int Depth(UiElement e)=>_uiDepth[e.Id];
        ulong Row(Guid id){if(!_uiRows.TryGetValue(id,out var row))_uiRows.Add(id,row=++_uiRowId);return row;}
    }
    private void UiButton(ulong id,string label,string kind,object? payload=null,bool enabled=true)=>Add(GuiItemKind.Button,31,id,label,new(kind,Operation:new UiIntent(_ui!.Stamp,payload)),enabled:enabled);
    public GuiFrame AttachUiImage(GuiCachedImageToken token)
    {
        int first=_items.Count;
        float x=Viewport.X+_uiController.Pan.X,y=Viewport.Y+_uiController.Pan.Y,width=_uiWidth/_uiDpi*_uiController.Zoom,height=_uiHeight/_uiDpi*_uiController.Zoom;
        Add(GuiItemKind.OverlayBegin,30,91,"");var clip=_items[^1];clip.Rect[0]=Viewport.X;clip.Rect[1]=Viewport.Y;clip.Rect[2]=Viewport.Width;clip.Rect[3]=Viewport.Height;_items[^1]=clip;
        _items.Add(GuiItem.CachedImage(31,90,token,x,y,width,height,true));_actions.Add((31,90),new("ui_canvas",Operation:new UiIntent(_ui!.Stamp)));
        ulong line=1000;
        foreach(var guide in _uiGesture?.Guides??[]){var data=new List<string>();Segment(guide.Start*_uiController.Zoom+_uiController.Pan,guide.End*_uiController.Zoom+_uiController.Pan,data);if(data.Count>0)Add(GuiItemKind.CanvasLines,30,line++,"",value:string.Join(" ",data),number:0xffeea955,min:1,max:1);}
        foreach(var box in _uiPreview!.Boxes.Where(b=>_uiController.Selection.Contains(b.Id))){
            Vector2[] corners=[Vector2.Zero,new(box.Rect.Width,0),new(box.Rect.Width,box.Rect.Height),new(0,box.Rect.Height)];var p=corners.Select(v=>Vector2.Transform(v,box.Transform)*_uiController.Zoom+new Vector2(x-Viewport.X,y-Viewport.Y)).ToArray();
            var segments=new List<string>();for(int i=0;i<4;i++)Segment(p[i],p[(i+1)%4],segments);
            if(segments.Count>0)Add(GuiItemKind.CanvasLines,30,line++,"",value:string.Join(" ",segments),number:0xffffb546,min:2,max:2);
            if(_uiController.Tool==UiCanvasTool.Resize&&_uiController.Selection.Length==1){
                Vector2[] handles=[p[0],(p[0]+p[1])/2,p[1],(p[1]+p[2])/2,p[2],(p[2]+p[3])/2,p[3],(p[3]+p[0])/2];
                var visible=handles.Where(v=>v.X>=0&&v.X<=Viewport.Width&&v.Y>=0&&v.Y<=Viewport.Height).ToArray();
                if(visible.Length>0){string dots=string.Join(" ",visible.Select(v=>FormattableString.Invariant($"{v.X/Viewport.Width} {v.Y/Viewport.Height} {v.X/Viewport.Width} {v.Y/Viewport.Height}")));Add(GuiItemKind.CanvasLines,30,line++,"",value:dots,number:0xffffb546,min:4,max:4);}
            }
        }
        Add(GuiItemKind.CanvasEnd,30,92,"");var imageItems=_items.GetRange(first,_items.Count-first);_items.RemoveRange(first,_items.Count-first);_items.InsertRange(_uiCanvasEndIndex,imageItems);_frame.ItemCount=(uint)_items.Count;_frame.TextBytes=(uint)_text.Count;return _frame;
        void Segment(Vector2 a,Vector2 b,List<string> result){if(UiCanvasController.ClipSegment(ref a,ref b,new(0,0,Viewport.Width,Viewport.Height)))result.Add(FormattableString.Invariant($"{a.X/Viewport.Width} {a.Y/Viewport.Height} {b.X/Viewport.Width} {b.Y/Viewport.Height}"));}
    }
    private bool ApplyUi(ActionView action,GuiEvent e,string text)
    {
        if(action.Kind=="ui_switch") { if(e.Phase==3){CancelInteraction();_graphMode=false;_uiMode=!_uiMode;_activeMenu=-1;if(_uiTest){_uiTest=false;_uiPreview?.ResetRuntime();}SynchronizeUi();}return true; }
        if(!action.Kind.StartsWith("ui_",StringComparison.Ordinal))return false;
        var ui=_ui??throw new InvalidOperationException("Project UI authoring unavailable.");var intent=(UiIntent)action.Operation!;
        var current=ui.Stamp;
        bool ownDraft=ui.HasDraft&&e.Phase!=1&&_uiActiveInput==(e.WidgetHigh,e.WidgetLow)&&intent.Stamp.Edit==current.Edit&&intent.Stamp.Document==current.Document&&intent.Stamp.Resources==current.Resources;
        if(intent.Stamp!=current&&!ownDraft)throw new InvalidOperationException("Stale UI content/resource view.");
        if(action.Kind=="ui_canvas"){CanvasEvent(e,text);return true;}
        if(action.Kind=="ui_text_page"){
            if(e.Phase==1){ui.Begin(intent.Stamp);_uiTextEdit=(UiTextPage)intent.Payload!;_uiActiveInput=(e.WidgetHigh,e.WidgetLow);return true;}
            var page=_uiTextEdit??throw new InvalidOperationException("UI text page activation required.");ui.Update([new(UiEditKind.Replace,page.Element.Id,page.Element with{Text=page.Pages.Replace(page.Page,text)})]);
            if(e.Phase==3){_uiPreview?.Prepare(ui,UiPreviewViewport);ui.Confirm();_uiTextEdit=null;_uiActiveInput=null;}return true;
        }
        if(action.Kind=="ui_field"){
            if(e.Phase==1){ui.Begin(intent.Stamp);_uiActiveInput=(e.WidgetHigh,e.WidgetLow);return true;}
            var elements=intent.Payload is UiElement single?new[]{single}:(UiElement[])intent.Payload!;ui.Update(elements.Select(element=>new UiEdit(UiEditKind.Replace,element.Id,FieldValue(element,action.Field,text,e.Value))).ToArray());
            if(e.Phase==3){_uiPreview?.Prepare(ui,UiPreviewViewport);ui.Confirm();_uiActiveInput=null;}return true;
        }
        if(e.Phase!=3)return true;
        switch(action.Kind){
            case "ui_files":_uiOffset=0;break;
            case "ui_path":_uiPath=text;break;
            case "ui_choose_new":case "ui_choose_open":CancelUi();string? path=filePicker?.Invoke(action.Kind=="ui_choose_new"?Ncma.Platform.LocalFileKind.SaveUi:Ncma.Platform.LocalFileKind.OpenUi);if(path is not null){_uiPath=UiAuthoringSource.ValidatePath(Path.GetRelativePath(projectRoot!,path).Replace('\\','/'));ui.ReviewFile(workspace.Stamp,_uiPath,action.Kind=="ui_choose_new");_uiWrite=false;_uiConfirm="";}break;
            case "ui_review_new":case "ui_review_open":CancelUi();ui.ReviewFile(workspace.Stamp,_uiPath,action.Kind=="ui_review_new");_uiConfirm="";_uiWrite=false;break;
            case "ui_write":_uiWrite=e.Value!=0;break;
            case "ui_confirm_text":_uiConfirm=text;break;
            case "ui_confirm":var review=(UiFileReview)intent.Payload!;ui.ConfirmFile(review.Review,Guid.Parse(_uiConfirm),_uiWrite);_uiDelete="";break;
            case "ui_revoke":CancelUi();ui.Revoke();break;
            case "ui_save":if(ui.HasDraft){_uiPreview?.Prepare(ui,UiPreviewViewport);ui.Confirm();}else ui.ValidateFile();break;
            case "ui_text_previous":_uiTextPage=Math.Max(0,_uiTextPage-1);break;case "ui_text_next":_uiTextPage++;break;
            case "ui_focus_workspace":CancelUi();_uiFocusCanvas=!_uiFocusCanvas;break;
            case "ui_select":CancelUi();_uiTextPage=0;Guid selected=(Guid)intent.Payload!;if(e.Value!=Math.Floor(e.Value)||e.Value is <0 or >3)throw new ArgumentException("UI selection modifiers.");int modifiers=(int)e.Value;
                int anchor=Array.FindIndex(_uiLayers,i=>i.Id==_uiSelectionAnchor),end=Array.FindIndex(_uiLayers,i=>i.Id==selected);
                if((modifiers&2)!=0&&anchor>=0){int from=Math.Min(anchor,end),count=Math.Abs(end-anchor)+1;if(count>64)throw new ArgumentException("UI range selection exceeds64 elements.");var range=_uiLayers.Skip(from).Take(count).Select(i=>i.Id).ToArray();if((modifiers&1)!=0&&_uiController.Selection.Union(range).Count()>64)throw new ArgumentException("UI range selection exceeds64 elements.");for(int i=0;i<range.Length;i++)_uiController.Select(range[i],append:i>0||(modifiers&1)!=0);}
                else{_uiController.Select(selected,toggle:(modifiers&1)!=0);_uiSelectionAnchor=selected;}break;
            case "ui_previous":_uiOffset=Math.Max(0,_uiOffset-32);break;case "ui_next":_uiOffset+=32;break;
            case "ui_tool":CancelUi();_uiController.Tool=(UiCanvasTool)intent.Payload!;break;
            case "ui_fit":_uiController.Fit(Viewport.Width,Viewport.Height,_uiWidth/_uiDpi,_uiHeight/_uiDpi);BoundUiZoom();break;
            case "ui_reset":_uiController.ResetView();break;case "ui_focus":_uiController.Focus(Viewport.Width,Viewport.Height);BoundUiZoom();break;
            case "ui_preview_size":bool bounded=action.Field switch{"Width" or "Height"=>e.Value is >=64 and <=2048,"DPI"=>e.Value is >=.25 and <=4,"Safe"=>e.Value is >=0 and <=32,_=>false};if(!bounded)throw new ArgumentException("UI preview parameter budget.");var next=UiPreviewViewport;next=action.Field switch{"Width"=>next with{Width=(float)e.Value},"Height"=>next with{Height=(float)e.Value},"DPI"=>next with{Scale=(float)e.Value},"Safe"=>next with{SafeArea=new((float)e.Value,(float)e.Value,(float)e.Value,(float)e.Value)},_=>throw new ArgumentException("UI preview field.")};next.Validate();_uiWidth=next.Width;_uiHeight=next.Height;_uiDpi=next.Scale;_uiSafe=next.SafeArea.Left;BoundUiZoom();break;
            case "ui_preset":var preset=((float Width,float Height))intent.Payload!;var configuration=UiPreviewViewport with{Width=preset.Width,Height=preset.Height};configuration.Validate();_uiWidth=preset.Width;_uiHeight=preset.Height;BoundUiZoom();break;
            case "ui_test":CancelUi();_uiTest=!_uiTest;_uiPreview!.ResetRuntime();_uiObservations.Clear();break;
            case "ui_preview_reset":CancelUi();_uiPreview!.ResetRuntime();_uiFailedPreparation=null;_uiObservations.Clear();break;
            case "ui_add":var kind=(UiKind)intent.Payload!;Guid id=Guid.NewGuid();Guid parent=_uiController.Selection.Length==1?_uiController.Selection[0]:_uiDefinition!.Root;
                var p=_uiDefinition!.Elements.Single(i=>i.Id==parent);if(p.Kind is not (UiKind.Frame or UiKind.Group or UiKind.ScrollView))parent=_uiDefinition.Root;
                var added=UiElement.Create(id,kind.ToString(),kind,parent) with{Order=_uiDefinition.Elements.Where(i=>i.Parent==parent).Select(i=>i.Order).DefaultIfEmpty(-1).Max()+1,Layout=UiLayout.Fixed(32,32,120,48),Style=UiStyle.Default with{Fill=new(.12f,.32f,.6f,1)}};
                if(kind==UiKind.Image)added=added with{Image=ui.Resources.Images.Keys.First()};
                CommitUi([new(UiEditKind.Add,id,added)]);break;
            case "ui_order":var order=((Guid Id,int Delta))intent.Payload!;CommitUi(_uiController.Order(order.Id,order.Delta));break;
            case "ui_delete_text":_uiDelete=text;break;case "ui_delete":CommitUi(_uiController.Delete(Guid.Parse(_uiDelete)));_uiDelete="";break;
            case "ui_geometry":string geometry=(string)intent.Payload!;CommitUi(geometry=="group"?_uiController.Group():_uiController.Align(geometry.EndsWith('x'),geometry.StartsWith("dist",StringComparison.Ordinal)));break;
            case "ui_token":Guid token=Guid.NewGuid();CommitUi([new(UiEditKind.SetToken,token,Token:new(token,"Color",UiTokenKind.Color,new(.1f,.4f,.8f,1),0))]);break;
            case "ui_token_id":_tokenId=text;break;case "ui_token_name":_tokenName=text;break;case "ui_token_color":_tokenColor=text;break;case "ui_token_kind":_tokenIsScalar=e.Value!=0;break;case "ui_token_scalar":_tokenScalar=(float)e.Value;break;
            case "ui_token_save":Guid tid=_tokenId.Length==0?Guid.NewGuid():Guid.Parse(_tokenId);CommitUi([new(UiEditKind.SetToken,tid,Token:new(tid,_tokenName,_tokenIsScalar?UiTokenKind.Scalar:UiTokenKind.Color,ParseColor(_tokenColor),_tokenScalar))]);_tokenId=tid.ToString("D");break;
            case "ui_token_remove":CommitUi([new(UiEditKind.RemoveToken,Guid.Parse(_tokenId))]);break;
            case "ui_resource_path":_resourcePath=text;_resourceReview=null;break;case "ui_resource_license":_resourceLicense=text;_resourceReview=null;break;case "ui_resource_redistribute":_resourceRedistribute=e.Value!=0;_resourceReview=null;break;
            case "ui_resource_id":_resourceId=text;_resourceReview=null;break;
            case "ui_resource_review":byte[] bytes=UiAuthoringSource.Read(projectRoot!,_resourcePath,32*1024*1024);_resourceReview=new(_resourceId.Length==0?Guid.NewGuid():Guid.Parse(_resourceId),_resourcePath,(UiResourceKind)intent.Payload!,Convert.ToHexString(SHA256.HashData(bytes)),_resourceLicense,_resourceRedistribute);_resourceConfirm="";break;
            case "ui_resource_confirm_text":_resourceConfirm=text;break;
            case "ui_resource_confirm":var resource=(UiResourceReview)intent.Payload!;if(resource!=_resourceReview||Guid.Parse(_resourceConfirm)!=resource.Id)throw new ArgumentException("Exact reviewed resource required.");
                if(resource.Kind==UiResourceKind.Font)ui.Resources.AddFont(resource.Id,resource.Source,resource.Hash,resource.License,resource.Redistributable,_uiPreview!.Text);
                else {string dll=Path.Combine(AppContext.BaseDirectory,"tools/import-worker/NcmaImportKernel.dll");using var decoder=new ImageDecoder(dll,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));ui.Resources.AddImage(resource.Id,resource.Source,resource.Hash,decoder);}_resourceReview=null;break;
        }
        _generation++;return true;
    }
    private void CommitUi(UiEdit[] edits){var ui=_ui!;ui.Begin(ui.Stamp);try{ui.Update(edits);_uiPreview?.Prepare(ui,UiPreviewViewport);ui.Confirm();}catch{ui.Cancel();throw;}}
    private static UiElement FieldValue(UiElement e,string field,string text,double number)
    {
        float n=checked((float)number);Guid Resource()=>text.Length==0?Guid.Empty:Guid.Parse(text);int mode=checked((int)number);if(field is "Flow" or "WidthMode" or "HeightMode"&&number!=mode)throw new ArgumentException("UI enum integer required.");
        UiColor Color()=>ParseColor(text);
        return field switch {
            "Name"=>e with{Name=text},"Text"=>e with{Text=text},"Font"=>e with{Font=Resource()},"Image"=>e with{Image=Resource()},"Action"=>e with{Action=text},"FontSize"=>e with{FontSize=n},
            "X"=>e with{Layout=e.Layout with{X=n}},"Y"=>e with{Layout=e.Layout with{Y=n}},"Width"=>e with{Layout=e.Layout with{Width=n}},"Height"=>e with{Layout=e.Layout with{Height=n}},"Rotation"=>e with{Layout=e.Layout with{Rotation=n}},"Gap"=>e with{Layout=e.Layout with{Gap=n}},
            "Flow"=>e with{Layout=e.Layout with{Flow=(UiFlow)mode}},"WidthMode"=>e with{Layout=e.Layout with{WidthMode=(UiSizing)mode}},"HeightMode"=>e with{Layout=e.Layout with{HeightMode=(UiSizing)mode}},
            "AnchorX"=>e with{Layout=e.Layout with{AnchorX=n}},"AnchorY"=>e with{Layout=e.Layout with{AnchorY=n}},"MinWidth"=>e with{Layout=e.Layout with{MinWidth=n}},"MinHeight"=>e with{Layout=e.Layout with{MinHeight=n}},"MaxWidth"=>e with{Layout=e.Layout with{MaxWidth=n}},"MaxHeight"=>e with{Layout=e.Layout with{MaxHeight=n}},
            "PaddingLeft"=>e with{Layout=e.Layout with{Padding=e.Layout.Padding with{Left=n}}},"PaddingTop"=>e with{Layout=e.Layout with{Padding=e.Layout.Padding with{Top=n}}},"PaddingRight"=>e with{Layout=e.Layout with{Padding=e.Layout.Padding with{Right=n}}},"PaddingBottom"=>e with{Layout=e.Layout with{Padding=e.Layout.Padding with{Bottom=n}}},
            "Visible"=>e with{Visible=number!=0},"Enabled"=>e with{Enabled=number!=0},"Locked"=>e with{Locked=number!=0},"Clip"=>e with{Style=e.Style with{Clip=number!=0}},"Opacity"=>e with{Style=e.Style with{Opacity=n}},"CornerRadius"=>e with{Style=e.Style with{CornerRadius=n}},
            "Fill"=>e with{Style=e.Style with{Fill=Color()}},"Foreground"=>e with{Style=e.Style with{Foreground=Color()}},"FillToken"=>e with{Style=e.Style with{FillToken=Resource()}},"ForegroundToken"=>e with{Style=e.Style with{ForegroundToken=Resource()}},"OpacityToken"=>e with{Style=e.Style with{OpacityToken=Resource()}},_=>throw new ArgumentException("UI field.")};
    }
    private static UiColor ParseColor(string text){var parts=text.Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();if(parts.Length!=4)throw new ArgumentException("Four RGBA channels required.");return new(parts[0],parts[1],parts[2],parts[3]);}
    public bool UiKeyboard(uint key,string text,bool reverse,bool captured){
        if(!_uiMode||!_uiTest||captured||_uiPreview?.Prepared!=true||_uiPreview.Runtime is not {} runtime)return false;
        UiInputKind? kind=text.Length>0?UiInputKind.Text:key switch{258=>UiInputKind.Tab,257 or 32=>UiInputKind.Activate,259=>UiInputKind.Backspace,263=>UiInputKind.Left,262=>UiInputKind.Right,_=>null};
        if(kind is null)return false;runtime.Input(runtime.Stamp,[new(kind.Value,++_uiSequence,default,text,reverse)]);Span<UiAction> actions=new UiAction[128];int count=runtime.Drain(runtime.Stamp,actions);for(int i=0;i<count;i++){_uiObservations.Enqueue(actions[i].Action+" / "+actions[i].Kind);while(_uiObservations.Count>8)_uiObservations.Dequeue();}return true;
    }
    private void CanvasEvent(GuiEvent e,string text)
    {
        var values=text.Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();if(values.Length!=3||values.Any(v=>!float.IsFinite(v)||Math.Abs(v)>64)||e.Value!=Math.Floor(e.Value)||e.Value is <0 or >95)throw new ArgumentException("UI canvas input.");
        var point=new Vector2(values[0]*_uiWidth/_uiDpi,values[1]*_uiHeight/_uiDpi);int modifiers=(int)e.Value;
        if((modifiers&64)!=0){if(_ui!.HasDraft)CancelUi();if(_uiTest){var r=_uiPreview!.Runtime!;r.Input(r.Stamp,[new(UiInputKind.Scroll,++_uiSequence,point*_uiDpi,Delta:-values[2]*32)]);}else _uiController.ZoomAt(MathF.Pow(1.1f,values[2]),point*_uiController.Zoom+_uiController.Pan,Vector2.Zero,MaxUiZoom);return;}
        if(_uiTest){var runtime=_uiPreview!.Runtime!;runtime.Input(runtime.Stamp,[new(e.Phase==1?UiInputKind.PointerDown:e.Phase==2?UiInputKind.PointerMove:UiInputKind.PointerUp,++_uiSequence,point*_uiDpi)]);Span<UiAction> actions=new UiAction[UiRuntime.MaxQueuedActions];int count=runtime.Drain(runtime.Stamp,actions);for(int i=0;i<count;i++){_uiObservations.Enqueue($"{actions[i].Action} / {actions[i].Kind} / {actions[i].Element}");while(_uiObservations.Count>8)_uiObservations.Dequeue();}return;}
        if(e.Phase==1){_uiStart=_uiLast=point;_uiModifiers=modifiers;_uiPan=(modifiers&24)!=0;_uiMarquee=(modifiers&2)!=0;
            if(_uiPan){_uiLast=point*_uiController.Zoom+_uiController.Pan;return;}if(_uiMarquee)return;
            var handle=_uiController.Tool==UiCanvasTool.Resize?_uiController.HandleAt(point):null;
            Guid hit=handle is not null?_uiController.Selection.Single():_uiController.Hit(point);if(hit==Guid.Empty)return;
            if(!_uiController.Selection.Contains(hit)||(modifiers&1)!=0)_uiController.Select(hit,toggle:(modifiers&1)!=0);
            if(!_ui!.Writable||_uiController.Selection.Length==0||hit==_uiDefinition!.Root)return;
            _uiGesture=new(){Tool=_uiController.Tool,ResizeHandle=handle??UiResizeHandle.SouthEast};_uiGesture.ZoomAt(_uiController.Zoom,Vector2.Zero,Vector2.Zero);_uiGesture.Load(_uiDefinition,_uiPreview!.Boxes);foreach(Guid id in _uiController.Selection)_uiGesture.Select(id,append:true);_ui.Begin(_ui.Stamp);_uiActiveInput=(e.WidgetHigh,e.WidgetLow);
        }else{
            if(_uiPan){var screen=point*_uiController.Zoom+_uiController.Pan;_uiController.Translate(screen-_uiLast);_uiLast=screen;}
            else if(_uiMarquee&&e.Phase==3){if(Vector2.Distance(point,_uiStart)*_uiController.Zoom<4){Guid hit=_uiController.Hit(point);if(hit!=Guid.Empty)_uiController.Select(hit,append:true);}else _uiController.Marquee(new(Math.Min(point.X,_uiStart.X),Math.Min(point.Y,_uiStart.Y),Math.Abs(point.X-_uiStart.X),Math.Abs(point.Y-_uiStart.Y)),(_uiModifiers&1)!=0);}
            else if(_uiGesture is not null){var selection=_uiGesture.TopSelection();var edits=selection.Length==1&&_uiGesture.Tool==UiCanvasTool.Move&&_uiGesture.AutoLayout(selection[0].Id)?_uiGesture.ReorderAt(selection[0].Id,point):_uiGesture.Transform(point-_uiStart,(modifiers&4)==0);_ui!.Update(edits);if(e.Phase==3){_uiPreview!.Prepare(_ui,UiPreviewViewport);_ui.Confirm();}}
            if(!_uiPan)_uiLast=point;if(e.Phase==3){_uiGesture=null;_uiActiveInput=null;_uiPan=_uiMarquee=false;}
        }
    }
    private float MaxUiZoom=>Math.Min(8,16384/Math.Max(_uiWidth/_uiDpi,_uiHeight/_uiDpi));
    private void BoundUiZoom(){if(_uiController.Zoom>MaxUiZoom)_uiController.ZoomAt(MaxUiZoom/_uiController.Zoom,Vector2.Zero,Vector2.Zero,MaxUiZoom);}
}
