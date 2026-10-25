namespace Ncma.Runtime;

// Explicit trusted code only. Neither manifest nor module grants access to a World.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ComponentRegistrationAttribute : Attribute;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class GeneratedComponentModuleAttribute(Type provider) : Attribute
{
    public Type Provider { get; } = provider;
}

public sealed class ComponentModule
{
    internal string Id { get; }
    internal ComponentModule[] Dependencies { get; }
    internal Action<ComponentRegistry> Register { get; }
    public ComponentModule(string id, ComponentModule[] dependencies, Action<ComponentRegistry> register)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(register);
        if (id.Length > 512 || dependencies.Length > 128 || dependencies.Any(d => d is null))
            throw new ArgumentException("Invalid component module identity/dependencies.");
        Id = id; Dependencies = (ComponentModule[])dependencies.Clone(); Register = register;
    }
}

/// <summary>One cold composition entry. Explicit roots preserve dependency direction;
/// no roots means Runtime only. Hosts pass their generated assembly root, not a hand-maintained type list.</summary>
public static class CompositionRegistry
{
    public static ComponentRegistry Build(params ComponentModule[] roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var selected = (ComponentModule[])roots.Clone();
        if (selected.Length > 128 || selected.Any(m => m is null)) throw new ArgumentException("Invalid composition roots.");
        var ordered = new List<ComponentModule>();
        var seen = new Dictionary<string, ComponentModule>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        void Visit(ComponentModule module)
        {
            if (active.Contains(module.Id)) throw new ArgumentException("Component module dependency cycle.");
            if (seen.TryGetValue(module.Id, out var prior)) {
                if (!ReferenceEquals(prior, module)) throw new ArgumentException("Conflicting component module identity: " + module.Id);
                return;
            }
            if (seen.Count >= 128) throw new ArgumentException("Component module budget exceeded.");
            seen.Add(module.Id, module); active.Add(module.Id);
            foreach (var dependency in module.Dependencies.OrderBy(d => d.Id, StringComparer.Ordinal)) Visit(dependency);
            active.Remove(module.Id); ordered.Add(module);
        }
        Visit(Generated.ComponentModule.Module);
        foreach (var root in selected.OrderBy(m => m.Id, StringComparer.Ordinal)) Visit(root);
        var registry = new ComponentRegistry();
        foreach (var module in ordered) module.Register(registry);
        // Original Register still owns duplicate IDs/types, validation, metadata, callbacks and freeze.
        // A failed build never returns its partial registry; trusted callback side effects are not rolled back.
        return registry;
    }
}
