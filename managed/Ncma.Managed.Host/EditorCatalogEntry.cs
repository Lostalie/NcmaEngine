using Ncma.Editor.Core;
namespace Ncma.ManagedHost;

public static unsafe partial class NativeEntry
{
    private static BehaviourCatalog CurrentCatalog() => new(Guid.NewGuid(), s_types.Select(t => new BehaviourTypeDescriptor(
        t.Type.FullName!, t.Exports.Select(e => new BehaviourExportDescriptor(e.Member.Name, e.Kind, e.DisplayName, e.Category, e.DefaultValue)).ToArray())).ToArray());
    private static void PublishEditorCatalog()
    {
        var catalog = CurrentCatalog();
        foreach (var scene in s_scenes.Values) scene.Editor?.SetBehaviourCatalog(catalog);
    }
}
