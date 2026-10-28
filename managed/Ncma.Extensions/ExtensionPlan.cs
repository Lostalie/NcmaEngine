using System.Collections.ObjectModel;

namespace Ncma.Extensions;

// Metadata from explicitly trusted startup code, not an assembly loader,
// signature verifier, security sandbox, permission grant or runtime service registry.
public sealed class ExtensionRejectedException(string code, string? extensionId = null, string? dependencyId = null)
    : ArgumentException(code)
{
    public string Code { get; } = code;
    public string? ExtensionId { get; } = extensionId;
    public string? DependencyId { get; } = dependencyId;
}

public readonly record struct ExtensionVersion : IComparable<ExtensionVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public ExtensionVersion(int major, int minor, int patch)
    {
        if (major is < 1 or > 65535 || minor is < 0 or > 65535 || patch is < 0 or > 65535)
            throw new ExtensionRejectedException("extension_version_invalid");
        Major = major; Minor = minor; Patch = patch;
    }
    internal void Validate()
    {
        // default(struct) must not bypass the public constructor's stable-version rule.
        if (Major == 0) throw new ExtensionRejectedException("extension_version_invalid");
    }
    public int CompareTo(ExtensionVersion other)
    {
        int major = Major.CompareTo(other.Major), minor = Minor.CompareTo(other.Minor);
        return major != 0 ? major : minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }
}

public sealed class ExtensionDependency
{
    public string Id { get; }
    public ExtensionVersion Minimum { get; }
    public ExtensionVersion Maximum { get; }
    // Closed, inclusive range within one major version. No prerelease/wildcards/resolver.
    public ExtensionDependency(string id, ExtensionVersion minimum, ExtensionVersion maximum)
    {
        ExtensionDescriptor.ValidateId(id); minimum.Validate(); maximum.Validate();
        if (minimum.Major != maximum.Major || minimum.CompareTo(maximum) > 0)
            throw new ExtensionRejectedException("extension_dependency_range_invalid", dependencyId: id);
        Id = id; Minimum = minimum; Maximum = maximum;
    }
    internal bool Accepts(ExtensionVersion version) => Minimum.CompareTo(version) <= 0 && Maximum.CompareTo(version) >= 0;
}

public sealed class ExtensionDescriptor
{
    public const int MaxIdLength = 96;
    public const int MaxDependencies = 16;
    public string Id { get; }
    public ExtensionVersion Version { get; }
    public ExtensionVersion MinimumHostApi { get; }
    public ReadOnlyCollection<ExtensionDependency> Dependencies { get; }

    public ExtensionDescriptor(string id, ExtensionVersion version, ExtensionVersion minimumHostApi,
        params ExtensionDependency[] dependencies)
    {
        ValidateId(id); version.Validate(); minimumHostApi.Validate();
        ArgumentNullException.ThrowIfNull(dependencies);
        if (dependencies.Length > MaxDependencies) throw new ExtensionRejectedException("extension_dependency_budget", id);
        var copy = (ExtensionDependency[])dependencies.Clone();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in copy) {
            if (dependency is null) throw new ExtensionRejectedException("extension_dependency_null", id);
            if (!seen.Add(dependency.Id)) throw new ExtensionRejectedException("extension_dependency_duplicate", id, dependency.Id);
        }
        Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        Id = id; Version = version; MinimumHostApi = minimumHostApi;
        Dependencies = Array.AsReadOnly(copy);
    }
    internal static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id[0] is < 'a' or > 'z' ||
            id[^1] is '.' or '-' || id.Contains("..", StringComparison.Ordinal))
            throw new ExtensionRejectedException("extension_id_invalid");
        foreach (char c in id)
            if (c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-')
                throw new ExtensionRejectedException("extension_id_invalid");
    }
}

public sealed class ExtensionPlan
{
    public const int MaxExtensions = 64;
    public const int MaxEdges = 256;
    public ExtensionVersion HostApi { get; }
    public ReadOnlyCollection<ExtensionDescriptor> StartupOrder { get; }
    public int DependencyCount { get; }
    private ExtensionPlan(ExtensionVersion hostApi, ExtensionDescriptor[] ordered, int edges)
    { HostApi = hostApi; StartupOrder = Array.AsReadOnly(ordered); DependencyCount = edges; }

    // Complete explicit input set; dependencies must already be present. No implicit
    // installation, callbacks, reflection, directory probing or effects on a live host.
    public static ExtensionPlan Compile(ExtensionVersion hostApi, params ExtensionDescriptor[] registrations)
    {
        hostApi.Validate(); ArgumentNullException.ThrowIfNull(registrations);
        if (registrations.Length > MaxExtensions) throw new ExtensionRejectedException("extension_count_budget");
        var selected = (ExtensionDescriptor[])registrations.Clone();
        var entries = new SortedDictionary<string, ExtensionDescriptor>(StringComparer.Ordinal);
        foreach (var entry in selected) {
            if (entry is null) throw new ExtensionRejectedException("extension_registration_null");
            if (!entries.TryAdd(entry.Id, entry)) throw new ExtensionRejectedException("extension_registration_duplicate", entry.Id);
        }
        int edges = 0;
        foreach (var entry in entries.Values) {
            if (entry.MinimumHostApi.Major != hostApi.Major || hostApi.CompareTo(entry.MinimumHostApi) < 0)
                throw new ExtensionRejectedException("extension_host_api_incompatible", entry.Id);
            foreach (var dependency in entry.Dependencies) {
                if (++edges > MaxEdges) throw new ExtensionRejectedException("extension_edge_budget", entry.Id);
                if (!entries.TryGetValue(dependency.Id, out var target))
                    throw new ExtensionRejectedException("extension_dependency_missing", entry.Id, dependency.Id);
                if (!dependency.Accepts(target.Version))
                    throw new ExtensionRejectedException("extension_dependency_version", entry.Id, dependency.Id);
            }
        }
        var ordered = new List<ExtensionDescriptor>(entries.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        void Visit(ExtensionDescriptor entry)
        {
            if (active.Contains(entry.Id)) throw new ExtensionRejectedException("extension_dependency_cycle", entry.Id);
            if (visited.Contains(entry.Id)) return;
            active.Add(entry.Id);
            foreach (var dependency in entry.Dependencies) Visit(entries[dependency.Id]);
            active.Remove(entry.Id); visited.Add(entry.Id); ordered.Add(entry);
        }
        foreach (var entry in entries.Values) Visit(entry);
        return new(hostApi, ordered.ToArray(), edges);
    }
}
