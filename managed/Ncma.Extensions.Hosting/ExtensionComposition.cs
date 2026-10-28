using Ncma.Runtime;

namespace Ncma.Extensions.Hosting;

// Exact trusted source binding to an EXISTING generated assembly module, not a second registry.
public sealed class ExtensionComponentContribution(ExtensionDescriptor descriptor, ComponentModule module)
{
    public ExtensionDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    public ComponentModule Module { get; } = module ?? throw new ArgumentNullException(nameof(module));
}

public static class ExtensionComposition
{
    public static ComponentRegistry Build(ExtensionPlan plan, ComponentModule host,
        params ExtensionComponentContribution[] contributions)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(contributions);
        if(contributions.Length>plan.StartupOrder.Count)throw new ExtensionRejectedException("extension_component_budget");
        var copy=(ExtensionComponentContribution[])contributions.Clone();
        var bindings=new Dictionary<string,ExtensionComponentContribution>(StringComparer.Ordinal);
        foreach(var contribution in copy) {
            if(contribution is null)throw new ExtensionRejectedException("extension_component_null");
            if(!plan.StartupOrder.Any(d=>ReferenceEquals(d,contribution.Descriptor)))
                throw new ExtensionRejectedException("extension_component_foreign",contribution.Descriptor.Id);
            if(!bindings.TryAdd(contribution.Descriptor.Id,contribution))
                throw new ExtensionRejectedException("extension_component_duplicate",contribution.Descriptor.Id);
        }
        // Preflight ALL bindings before any original registration callbacks. Host's generated
        // root still contributes its full referenced closure; this is NOT selective unload.
        var roots=new List<ComponentModule>{host};
        foreach(var descriptor in plan.StartupOrder)
            if(bindings.TryGetValue(descriptor.Id,out var contribution))roots.Add(contribution.Module);
        return CompositionRegistry.Build(roots.ToArray());
    }
}
