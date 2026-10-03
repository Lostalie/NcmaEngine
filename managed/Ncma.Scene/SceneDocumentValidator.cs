using Ncma.Runtime;
namespace Ncma.Scene;

public static class SceneDocumentValidator
{
    public const int MaxBindingsPerObject = 1024;
    public const int MaxExportsPerBinding = 1024;
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
            ValidateBindings(item.Behaviours, bindings, $"objects[{i}]");
        }
    }
    internal static void ValidateBindings(BehaviourBindingData[]? values, HashSet<Guid> identities, string path)
    {
        if (values is null || values.Length > MaxBindingsPerObject) throw new ArgumentException(path + ": invalid binding capacity.");
        foreach (var binding in values)
        {
            if (binding is null || binding.Id == Guid.Empty || !identities.Add(binding.Id) ||
                string.IsNullOrWhiteSpace(binding.TypeName) || binding.TypeName.Length > 512 || binding.TypeName.Any(char.IsControl))
                throw new ArgumentException(path + ": invalid/duplicate Behaviour identity.");
            if (binding.Exports is null || binding.Exports.Length > MaxExportsPerBinding) throw new ArgumentException(path + ": invalid Export capacity.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in binding.Exports)
            {
                if (property is null || string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > 128 ||
                    property.Name.Any(char.IsControl) || !names.Add(property.Name) || !double.IsFinite(property.Value) ||
                    !(property.Kind switch {
                        ExportKind.Float => float.IsFinite((float)property.Value),
                        ExportKind.Double => true,
                        ExportKind.Integer => property.Value == Math.Truncate(property.Value) && property.Value >= int.MinValue && property.Value <= int.MaxValue,
                        ExportKind.Boolean => property.Value == 0 || property.Value == 1,
                        _ => false }))
                    throw new ArgumentException(path + ": invalid/duplicate Export.");
            }
        }
    }
}
