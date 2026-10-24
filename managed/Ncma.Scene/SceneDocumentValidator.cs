using Ncma.Runtime;
namespace Ncma.Scene;

public static class SceneDocumentValidator
{
    internal static void CheckMetadata(SceneDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != SceneDocumentCodec.Version || snapshot.Objects is null || snapshot.Objects.Length > World.MaxObjects)
            throw new ArgumentException("Unsupported document snapshot version/capacity.");
        World.ValidateName(snapshot.Name);
        var objects = new HashSet<Guid>();
        var bindings = new HashSet<Guid>();
        for (int i = 0; i < snapshot.Objects.Length; i++)
        {
            var item = snapshot.Objects[i];
            if (item is null || item.Id == Guid.Empty || !objects.Add(item.Id))
                throw new ArgumentException($"objects[{i}]: invalid/duplicate UUID.");
            World.ValidateName(item.Name);
            if (item.Components is null || item.Components.Length > World.MaxComponentsPerObject)
                throw new ArgumentException($"objects[{i}]: invalid component capacity.");
            BehaviourBindingRules.Validate(item.Behaviours, bindings, $"objects[{i}]");
        }
    }
}
