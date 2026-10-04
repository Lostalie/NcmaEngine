using Ncma.Editor.Core;
namespace Ncma.ManagedHost;

public static unsafe partial class NativeEntry
{
    private static BehaviourCatalog CurrentCatalog() => new(s_catalog.Snapshot.Generation, s_catalog.Snapshot.Types.Select(t => new BehaviourTypeDescriptor(
        t.TypeName, t.Exports.Select(e => new BehaviourExportDescriptor(e.Name, e.Kind, e.DisplayName, e.Category, e.DefaultValue)).ToArray())).ToArray());
    private static void PublishEditorCatalog()
    {
        var catalog = CurrentCatalog();
        foreach (var scene in s_scenes.Values) scene.Editor?.SetBehaviourCatalog(catalog);
    }
}
