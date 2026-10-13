using Ncma.Gui;
using Ncma.Scene.Rendering;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private sealed record EnvironmentPreparationView(Guid World, ulong Revision, Guid Play, bool Ready, bool Failed, EnvironmentLightingData? Prepared);
    private EnvironmentPreparationView? _environmentPreparation;

    // Copied presentation state only. Never an approval, resource handle or Agent capability.
    internal void ShowEnvironmentPreparation(Guid world, ulong revision, Guid play, bool ready, bool failed, EnvironmentLightingData? prepared) =>
        _environmentPreparation = new(world, revision, play, ready, failed, prepared);

    internal string EnvironmentPreparationStatus
    {
        get {
            var play = workspace.Owner.Play;
            var world = play?.Document.World ?? workspace.Owner.Document.World;
            if (_environmentPreparation is not { } state || state.World != world.Identity || state.Revision != world.Revision || state.Play != (play?.SessionId ?? Guid.Empty))
                return "环境准备：等待当前版本";
            string prefix = play is null ? "环境准备：" : "环境准备（Play，编辑冻结）：";
            if (state.Failed) return prefix + "失败；视口已清空，详见控制台";
            if (!state.Ready || state.Prepared is null) return prefix + "等待当前版本";
            return prefix + (state.Prepared.Value.Enabled ? $"就绪 / 代次 {state.Prepared.Value.Generation}" : "关闭");
        }
    }

    private void BuildEnvironmentInspector(Guid objectId, EnvironmentLightingData value, ref ulong control, ref ulong label, bool writable)
    {
        Add(GuiItemKind.Label, 3, label++, "环境光 / IBL");
        Add(GuiItemKind.Label, 3, label++, value.Enabled ? $"环境 UUID：{value.AssetId:D}" : "未绑定环境；关闭状态不保留资源身份");
        if (value.Enabled) {
            Add(GuiItemKind.Label, 3, label++, $"资源代次：{value.Generation}");
            Add(GuiItemKind.Label, 3, label++, "SHA256：" + value.ContentHash);
        }
        Add(GuiItemKind.Label, 3, label++, EnvironmentPreparationStatus);
        Add(GuiItemKind.Checkbox, 2, control++, "环境启用（关闭后可撤销恢复）", new("environment_field", objectId, "enabled"),
            number: value.Enabled ? 1 : 0, max: 1, enabled: writable && value.Enabled);
        Add(GuiItemKind.Number, 2, control++, "环境光强度", new("environment_field", objectId, "strength"),
            number: value.Strength, min: 0, max: 16, enabled: writable && value.Enabled);
        Add(GuiItemKind.Number, 2, control++, "环境 Y 旋转（度）", new("environment_field", objectId, "rotationDegrees"),
            number: Math.Clamp(value.RotationRadians * 180 / Math.PI, -180, 180), min: -180, max: 180, enabled: writable && value.Enabled);
        Add(GuiItemKind.Label, 3, label++, "预设发布尚未接入；不自动导入、替换或重新启用资源");
    }
}
