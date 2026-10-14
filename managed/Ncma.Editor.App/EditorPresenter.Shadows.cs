using Ncma.Assets;
using Ncma.Gui;
using Ncma.Rendering;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private object ShadowOperation(Guid target,SceneShadowQuality quality)=>new{op="set_component",objectId=target,typeId=SceneShadowData.TypeId,version=1,
        data=workspace.Owner.Document.World.Components.Encode(new SceneShadowData(1,quality))};
    private void BuildShadowInspector(Guid target,SceneShadowData value,ref ulong control,ref ulong label,bool writable)
    {
        var preset=SceneShadowPresets.Get(SceneShadowData.Validate(value).Quality);
        Add(GuiItemKind.Label,3,label++,"场景阴影档位 / "+value.Quality);
        Add(GuiItemKind.Label,3,label++,$"预设：{preset.Budget.Resolution}² Depth32 / {preset.Budget.DepthTextureBytes/1048576} MiB / 最多 {preset.Budget.MaximumDepthReadsPerShadedPixel} 次深度读取");
        Add(GuiItemKind.Label,3,label++,"逻辑预设预算，非完整显存/帧率保证；无投影主光时为零。自定义 Shader 成本另测。");
        if(preset.RequiresHighShader)Add(GuiItemKind.Label,3,label++,"High：近似 PCSS，非物理面积光源；必须通过受检程序库准入。");
        foreach(var quality in Enum.GetValues<SceneShadowQuality>())
            Button(control++,"阴影："+quality,"transaction",writable&&value.Quality!=quality,target,ShadowOperation(target,quality));
    }
    private void BuildShadowSelection(Guid target,ref ulong control,bool writable)
    {
        if(workspace.Owner.Document.World.Components.Describe().Any(c=>c.TypeId==SceneShadowData.TypeId)&&!workspace.HasSceneComponent(SceneShadowData.TypeId))
            Button(control++,"添加场景阴影配置（Medium）","transaction",writable,target,ShadowOperation(target,SceneShadowQuality.Medium));
    }
}
