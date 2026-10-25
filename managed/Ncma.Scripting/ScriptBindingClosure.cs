using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Scene;

namespace Ncma.Scripting;

// Cold, constructor-free binding resolution against an already loaded trusted catalog.
// Module IDs identify loaded managed types, NOT a deployment/OS/native dependency manifest.
public sealed record ResolvedScriptType(string TypeName, string AssemblyIdentity, Guid ModuleId);
public sealed class ScriptBindingClosure
{
    internal readonly ScriptCatalogService Owner;
    private readonly ResolvedScriptType[] _types;
    public const int Version = 1;
    public Guid CatalogGeneration { get; }
    public int BindingCount { get; }
    public string ContentHash { get; }
    internal ScriptBindingClosure(ScriptCatalogService owner, Guid generation, int count,
        ResolvedScriptType[] types, byte[] content)
    {
        Owner = owner; CatalogGeneration = generation; BindingCount = count;
        _types = types; ContentHash = Convert.ToHexString(SHA256.HashData(content));
    }
    public ResolvedScriptType[] CopyTypes() => (ResolvedScriptType[])_types.Clone();
}

public sealed partial class ScriptCatalogService
{
    public ScriptBindingClosure ResolveBindings(RuntimeSceneImage image)
    {
        Verify(); ArgumentNullException.ThrowIfNull(image);
        return ResolveBindings(image.CopySnapshot());
    }
    public ScriptBindingClosure ResolveBindings(RuntimeSceneInstance scene)
    {
        Verify(); ArgumentNullException.ThrowIfNull(scene);
        // CaptureSnapshot enforces the scene owner thread and committed-read boundary.
        return ResolveBindings(scene.CaptureSnapshot());
    }
    public void RequireCurrent(ScriptBindingClosure closure)
    {
        Verify(); ArgumentNullException.ThrowIfNull(closure);
        if (!ReferenceEquals(closure.Owner, this) || closure.CatalogGeneration != _generation)
            throw new InvalidOperationException("Script binding closure belongs to a stale or foreign catalog.");
    }
    private ScriptBindingClosure ResolveBindings(RuntimeSceneSnapshot scene)
    {
        // Both callers supply owned, bounded, fully validated runtime snapshots. Disabled
        // bindings are resolved too: enabling them later must not reveal a missing type.
        var catalog = _types.ToDictionary(t => t.Type.FullName!, StringComparer.Ordinal);
        var used = new Dictionary<string, ResolvedScriptType>(StringComparer.Ordinal);
        var bindings = scene.Objects.OrderBy(o => o.Id).SelectMany(o => o.Behaviours.OrderBy(b => b.Id)
            .Select(b => new { ObjectId = o.Id, Binding = b })).ToArray();
        foreach (var item in bindings)
        {
            var binding = item.Binding;
            if (!catalog.TryGetValue(binding.TypeName, out var type))
                throw new ArgumentException("Missing Behaviour type: " + binding.TypeName);
            foreach (var value in binding.Exports)
                if (!type.Exports.Any(e => e.Member.Name == value.Name && e.Kind == (uint)value.Kind))
                    throw new ArgumentException("Missing or changed Export: " + binding.TypeName + "." + value.Name);
            used.TryAdd(binding.TypeName, new(binding.TypeName, type.Type.Assembly.FullName!, type.Type.Module.ModuleVersionId));
        }
        var types = used.Values.OrderBy(t => t.TypeName, StringComparer.Ordinal).ToArray();
        // Include defaulted Export contracts, not only overrides. Session generation stays
        // outside deterministic content; it is still checked by RequireCurrent.
        var schemas = types.Select(t => new { Type = t, Exports = catalog[t.TypeName].Exports.Select(e =>
            new { Name = e.Member.Name, e.Kind, e.DefaultValue }).ToArray() }).ToArray();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = ScriptBindingClosure.Version, schemas, bindings });
        return new(this, _generation, bindings.Length, types, bytes);
    }
}
