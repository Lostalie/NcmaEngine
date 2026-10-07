using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Editor.Services;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private int _activeMenu = -1;
    private bool _showAiTools = true, _showSceneFiles;
    private string _objectSearch = "";
    private WorkspaceGeometry _geometry;
    private EditorWorkspaceSettingsStore? _layoutStore;
    private EditorWorkspaceSettings _layout=EditorWorkspaceSettings.Default;
    private EditorWorkspaceSettings? _layoutBefore;
    public void AttachLayout(EditorWorkspaceSettingsStore store){_layoutStore=store;_layout=store.Current;_showAiTools=_layout.ShowAi;if(store.Diagnostic.Length>0)Record(store.Diagnostic);}
    private readonly record struct WorkspaceRect(float X,float Y,float Width,float Height);
    private readonly record struct WorkspaceGeometry(WorkspaceRect Objects,WorkspaceRect Inspector,WorkspaceRect Assets,WorkspaceRect Console,WorkspaceRect Ai,WorkspaceRect Status);
    private void ArrangeWorkspace(float width,float height)
    {
        const float top=48,gap=6,status=28;
        float usable=Math.Max(1,height-top-status-gap);
        float bottom=usable*_layout.Bottom,body=Math.Max(1,usable-bottom-gap);
        float ai=_showAiTools&&width>=1200?Math.Clamp(width*_layout.Ai,180,400):0;
        float available=width-(ai>0?ai+gap:0);
        float left=Math.Clamp(available*_layout.Left,140,400),right=Math.Clamp(available*_layout.Right,190,440);
        if(available<640){left=available*.22f;right=available*.27f;}
        float center=Math.Max(1,available-left-right-gap*2);
        _geometry=new(new(0,top,left,body),new(left+gap+center+gap,top,right,body),
            new(0,top+body+gap,left+gap+center*_layout.Assets,bottom),
            new(left+gap+center*_layout.Assets+gap,top+body+gap,Math.Max(1,available-left-gap-center*_layout.Assets-gap),bottom),
            new(available+gap,top,ai,usable),new(0,height-status,width,status));
        Viewport=(left+gap+8,top+34+8,Math.Max(1,center-16),Math.Max(1,body-34-16));
    }
    private void WorkspaceSplitters(){
        if(_layoutStore is null||_geometry.Status.Width<640)return;
        var g=_geometry;float available=g.Inspector.X+g.Inspector.Width,center=g.Inspector.X-g.Objects.Width-12;
        Split(1,new(g.Objects.Width,48,6,g.Objects.Height),0,available*.12f,available*.3f);
        Split(2,new(g.Inspector.X-6,48,6,g.Inspector.Height),0,available*(1-.32f)-6,available*(1-.16f)-6);
        Split(3,new(0,g.Assets.Y-6,available,6),1,48+(g.Status.Y-54)*.55f,48+(g.Status.Y-54)*.85f);
        Split(4,new(g.Console.X-6,g.Console.Y,6,g.Console.Height),0,g.Objects.Width+6+center*.35f,g.Objects.Width+6+center*.8f);
        if(g.Ai.Width>0)Split(5,new(g.Ai.X-6,48,6,g.Ai.Height),0,g.Status.Width*(1-.28f)-6,g.Status.Width*(1-.14f)-6);
        void Split(ulong id,WorkspaceRect rect,int axis,float min,float max){Panel(300+id,"",rect.X,rect.Y,rect.Width,rect.Height);var panel=_items[^1];panel.Value=3;_items[^1]=panel;Add(GuiItemKind.Splitter,32,id,"",new("layout_split",Index:(int)id,Operation:_layoutStore.Revision),number:axis,min:min,max:max);var item=_items[^1];item.Rect[0]=rect.X;item.Rect[1]=rect.Y;item.Rect[2]=rect.Width;item.Rect[3]=rect.Height;_items[^1]=item;End();}
    }
    private bool ApplyLayout(ActionView action,GuiEvent e){
        if(action.Kind!="layout_split")return false;if(_layoutStore is null||(ulong)action.Operation! !=_layoutStore.Revision)throw new InvalidOperationException("Stale workspace split.");
        if(e.Phase==1){CancelUi();workspace.CancelDraft();_layoutBefore=_layout;return true;}
        if(_layoutBefore is null)throw new InvalidOperationException("Workspace split missing start.");
        float available=_geometry.Inspector.X+_geometry.Inspector.Width,center=_geometry.Inspector.X-_geometry.Objects.Width-12;
        _layout=EditorWorkspaceSettings.Validate(action.Index switch{
            1=>_layout with{Left=Math.Clamp((float)e.Value/available,.12f,.3f)},2=>_layout with{Right=Math.Clamp((available-6-(float)e.Value)/available,.16f,.32f)},
            3=>_layout with{Bottom=Math.Clamp(1-((float)e.Value-48)/(_geometry.Status.Y-54),.15f,.45f)},4=>_layout with{Assets=Math.Clamp(((float)e.Value-_geometry.Objects.Width-6)/center,.35f,.8f)},
            5=>_layout with{Ai=Math.Clamp((_geometry.Status.Width-6-(float)e.Value)/_geometry.Status.Width,.14f,.28f)},_=>throw new ArgumentException("Workspace split axis.")});
        // A drag keeps its presentation identity until deactivation. Changing it on
        // every motion clears ImGui's active ID and drops the remaining gesture.
        if(e.Phase==3){_layoutStore.Save(_layoutStore.Revision,_layout with{ShowAi=_showAiTools});_layoutBefore=null;_generation++;}return true;
    }
    private void Region(ulong id,string name,WorkspaceRect rect,string headerStatus = "")
    {
        Panel(id,name,rect.X,rect.Y,rect.Width,rect.Height,headerStatus);
        if(id==94){var item=_items[^1];item.Value=1;_items[^1]=item;}
    }
    private void WorkspaceToolbar(float width)
    {
        var page=_page!;
        Panel(90,"",0,0,1,1);Add(GuiItemKind.Theme,24,90,"",number:3);End();
        Add(GuiItemKind.ToolbarBegin,23,1,"NcmaEngine menu");SetRect(0,0,width,48);
        float x=8;
        string[] names=["项目","编辑","工具","游戏","AI","窗口","帮助","文件"];
        int[] menus=width>=1100?[7,0,1,2,3,4,5,6]:[7,0,5];
        foreach(int menu in menus)Menu((ulong)(10+menu),width<1100&&menu==5?"菜单":names[menu],"workspace_menu",menu,48,0,_activeMenu==menu?2:0);
        x=Math.Max(x+8,width-(width>=1100?352:200));
        bool paused=page.Play?.State==PlayState.Paused;
        Menu(30,paused?"继续":"运行",paused?"resume":"start",0,76,7,1,paused||page.Play is null&&!page.State.HistoryInvalidated&&!page.State.EditBusy);
        Menu(31,"","workspace_menu",3,28,14);
        Menu(32,"","stop",0,34,10,0,page.Play is not null);
        if(width>=1100)Menu(33,"Windows / DX11",null,0,120,0,0,false);
        if(width>=1100)Menu(35,"",null,0,30,13,0,false);
        Menu(34,"","preferences_toggle",0,34,4,0,preferences is not null);
        Add(GuiItemKind.ToolbarEnd,0,0,"");
        void Menu(ulong id,string label,string? action,int index,float size,int icon,int role=0,bool enabled=true)
        {
            if(x+size>width-6)return;
            string tip=id==35?"账户服务未实现":id==33?"当前平台和渲染 API；其他平台/API 未启用":id==34?"编辑器本机偏好":id==32?"停止 Play":id==31?"运行控制":label;
            Add(GuiItemKind.MenuButton,24,id,label,action is null?null:new(action,Index:index),value:tip,number:icon,min:role,max:2,enabled:enabled&&action is not null);SetRect(x,7,size,34);x+=size+4;
        }
    }
    internal static string WindowTitle(string? name)=>"NcmaEngine - "+BoundMessage(string.IsNullOrWhiteSpace(name)?"未命名项目":name.Trim(),200);
    private static string WorkspaceLabel(string label)=>label switch {
        "+ GameObject"=>"+ 新建对象","Previous page"=>"上一页","Next page"=>"下一页","Name"=>"名称",
        "Previous components"=>"上一页组件","Next components"=>"下一页组件","Add Transform"=>"添加变换",
        "Previous bindings"=>"上一页脚本","Next bindings"=>"下一页脚本","Previous script types"=>"上一页类型","Next script types"=>"下一页类型",
        "Type UUID to confirm deletion"=>"确认删除 UUID","Delete confirmed object"=>"删除已确认对象",
        "Enable local MCP"=>"启用本机 MCP","Disable local MCP"=>"停用本机 MCP",
        "Older Console page"=>"上一页日志","Newer Console page"=>"下一页日志","Latest Console page"=>"最新日志",
        "Previous assets"=>"上一页资源","Next assets"=>"下一页资源",_=>label
    };
    private void WorkspaceMenu(ref ulong labelId,bool writable)
    {
        if(_activeMenu<0)return;
        float width=Math.Min(360,_geometry.Status.Width-12);
        Panel(91,"菜单",Math.Min(_geometry.Status.Width-width-6,Math.Max(6,_geometry.Objects.Width)),48,width,Math.Min(340,_geometry.Status.Y-54));
        var overlay=_items[^1];overlay.Value=2;_items[^1]=overlay;
        int menu=_activeMenu;bool all=_geometry.Status.Width<1100&&menu==5;
        if(menu==7||all){Button(3,"新建场景","new",writable);Button(40,"打开场景…","browse_open",writable&&filePicker is not null);Button(5,"保存场景","save",writable);Button(41,"场景另存为…","browse_save",writable&&filePicker is not null);Add(GuiItemKind.Button,24,40,"文件路径与替换确认",new("workspace_files"));}
        if(menu==0||all){Add(GuiItemKind.Label,3,labelId++,"当前项目："+(string.IsNullOrWhiteSpace(projectName)?"未命名项目":BoundMessage(projectName.Trim(),80)));Add(GuiItemKind.Label,3,labelId++,"项目热切换尚未实现。");}
        if(menu==1||all){Button(6,"撤销","undo",writable&&_page!.State.UndoCount>0);Button(7,"重做","redo",writable&&_page!.State.RedoCount>0);}
        if(menu==2||all){Add(GuiItemKind.Button,24,41,"本机偏好",new("preferences_toggle"),enabled:preferences is not null);Add(GuiItemKind.Button,24,42,"动作动画实验室",new("animation_toggle"),enabled:animationPreview is not null);Add(GuiItemKind.Button,24,43,"角色调试",new("character_toggle"),enabled:_characters is not null);}
        if(menu==3||all){Button(16,"暂停","pause",_page!.Play?.State==PlayState.Running);Button(17,"继续","resume",_page.Play?.State==PlayState.Paused);Button(18,"固定步单步","step",_page.Play?.State==PlayState.Paused);Button(19,"重新运行","restart",_page.Play is not null);Button(20,"重载已配置 C# 逻辑","reload",gameplayAssembly is not null);Add(GuiItemKind.Button,14,1,"独立浏览相机",new("browser_camera"));Add(GuiItemKind.Button,14,2,"使用选中场景相机",new("scene_camera",_page.Selected?.Id??Guid.Empty),enabled:_page.Selected?.Components.Any(c=>c.TypeId==Ncma.Scene.Rendering.CameraData.TypeId)==true);if(projectRoot is not null)Add(GuiItemKind.Button,14,3,"刷新渲染资源",new("render_refresh"));BuildBrowserControls();}
        if(menu==4||menu==5||all)Add(GuiItemKind.Button,24,44,_showAiTools?"隐藏 AI 工具侧栏":"显示 AI 工具侧栏",new("workspace_ai"));
        if(menu==2||menu==5||all)Add(GuiItemKind.Button,24,46,_uiMode?"场景工作区":"UI 创作工作区",new("ui_switch"),enabled:_ui is not null);
        if(menu==2||menu==4||all)Add(GuiItemKind.Button,24,47,"动画图检查 / 只读 MCP 审批",new("graph_toggle"),enabled:_graphs is not null);
        if(menu==6||all){Add(GuiItemKind.Label,3,labelId++,"NcmaEngine · C# runtime / native plugins");Add(GuiItemKind.Label,3,labelId++,"AI 推理服务与 Vulkan 绘制未实现。");}
        Add(GuiItemKind.Button,24,45,"关闭菜单",new("workspace_close_menu"));End();
        void Button(ulong id,string label,string action,bool enabled)=>Add(GuiItemKind.Button,25,id,label,new(action),enabled:enabled);
    }
}
