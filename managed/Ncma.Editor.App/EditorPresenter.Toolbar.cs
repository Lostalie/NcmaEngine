using Ncma.Gameplay;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private void Toolbar(float width)
    {
        var page=_page??throw new InvalidOperationException("Capture the editor before building its toolbar.");
        Add(GuiItemKind.ToolbarBegin,23,1,"NcmaEngine toolbar");
        SetRect(0,0,width,64);
        float brand=width>=1100?218:186;
        Add(GuiItemKind.ToolbarBrand,23,2,"NcmaEngine",value:width>=800?"C# Runtime  /  Native Plugins":"Editor");SetRect(14,8,brand-20,48);
        const float slot=54,gap=4;float runWidth=4*slot+3*gap;float right=width-runWidth-14;
        float x=brand;
        Tool(3,"打开",1,filePicker is null?null:"browse_open",filePicker is not null&&!page.State.Frozen,"打开场景文件 / Open scene");
        Tool(4,"保存",3,"save",!page.State.Frozen&&!page.State.HistoryInvalidated,"保存当前场景 / Save scene");
        Tool(5,"撤销",11,"undo",!page.State.Frozen&&page.State.UndoCount>0,"撤销最近的已授权操作 / Undo");
        Tool(6,"重做",12,"redo",!page.State.Frozen&&page.State.RedoCount>0,"重做 / Redo");
        Tool(7,"动画",5,"animation_toggle",animationPreview is not null,"打开现有动作动画实验室 / Action Animation Lab",_showAnimation?2:0);
        Tool(8,"设置",4,"preferences_toggle",preferences is not null,"编辑器本机偏好 / Preferences",_showPreferences?2:0);
        if(x+8<right){Add(GuiItemKind.ToolbarDivider,23,9,"");SetRect(x,14,1,36);}
        x=Math.Max(brand,right);
        var play=page.Play;bool paused=play?.State==PlayState.Paused;
        Run(10,paused?"继续":"运行",7,paused?"resume":"start",paused||play is null&&!page.State.HistoryInvalidated,"运行配置的C#逻辑 / Run or resume",1);
        Run(11,"暂停",8,"pause",play?.State==PlayState.Running,"暂停Play / Pause");
        Run(12,"单步",9,"step",paused,"仅Paused时前进一个固定步 / Step");
        Run(13,"停止",10,"stop",play is not null,"停止Play / Stop");
        Add(GuiItemKind.ToolbarEnd,0,0,"");

        void Tool(ulong id,string label,int icon,string? action,bool enabled,string tip,int role=0)
        {
            if(x+slot>right-12)return;AddTool(id,label,icon,action,enabled,tip,role);x+=slot+gap;
        }
        void Run(ulong id,string label,int icon,string action,bool enabled,string tip,int role=0)
        {if(x+slot>width-4)return;AddTool(id,label,icon,action,enabled,tip,role);x+=slot+gap;}
        void AddTool(ulong id,string label,int icon,string? action,bool enabled,string tip,int role)
        {
            Add(GuiItemKind.ToolbarButton,23,id,label,action is null?null:new(action),value:tip,number:icon,min:role,max:2,enabled:enabled&&action is not null);SetRect(x,5,slot,54);
        }
    }
    private void SetRect(float x,float y,float width,float height)
    {var item=_items[^1];item.Rect[0]=x;item.Rect[1]=y;item.Rect[2]=width;item.Rect[3]=height;_items[^1]=item;}
}
