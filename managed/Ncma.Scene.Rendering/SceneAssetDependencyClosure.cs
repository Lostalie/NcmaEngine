using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Runtime;

namespace Ncma.Scene.Rendering;

public sealed record ResolvedSceneAsset(Guid Id, AssetKind Kind, ulong Generation, string ContentHash);

// Companion to the baked scene, not an asset loader, live store or permission. Members describe
// the actual held lease (which can include model siblings), rather than claiming a minimal set.
public sealed class SceneAssetDependencyClosure
{
    public const int Version = 2;
    private readonly AssetRef[] _roots;
    private readonly ResolvedSceneAsset[] _members;
    public Guid ProjectId { get; }
    public Guid LeaseIdentity { get; }
    public string ContentHash { get; }
    public SceneAssetPolicy Policy { get; }
    public bool PresentationRequired => SceneAssetPolicies.RequiresPresentation(Policy);
    public string Resolution => _roots.Length == 0 ? "not-required" : "resolved";
    internal SceneAssetDependencyClosure(SceneAssetPolicy policy, Guid project, Guid lease, AssetRef[] roots, ResolvedSceneAsset[] members)
    {
        Policy = policy; ProjectId = project; LeaseIdentity = lease; _roots = roots; _members = members;
        // Runtime lease identity is provenance, deliberately excluded from deterministic data.
        ContentHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Version, policy, project, roots, members })));
    }
    public AssetRef[] CopyRoots() => (AssetRef[])_roots.Clone();
    public ResolvedSceneAsset[] CopyMembers() => (ResolvedSceneAsset[])_members.Clone();
    public void RequireCurrent(PreparedSceneAssetLease? assets)
    {
        if (_roots.Length == 0) return;
        if (assets is null || assets.Policy != Policy || assets.Assets.Identity != LeaseIdentity || assets.Assets.ProjectId != ProjectId)
            throw new InvalidOperationException("Stale or foreign scene asset closure.");
    }
}

public static partial class SceneAssetPreparation
{
    public static SceneAssetDependencyClosure ResolveDependencies(ISceneContent scene, PreparedSceneAssetLease? assets,
        SceneAssetPolicy policy = SceneAssetPolicy.PresentationAndSimulation)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var roots = References(scene, policy);
        if (assets is not null && assets.Policy != policy) throw new ArgumentException("Scene asset preparation policy mismatch.");
        if (roots.Length != 0 && assets is null) throw new ArgumentException("Scene assets must be explicitly prepared before Play.");
        // Structural/value contracts remain mandatory even when visual resources are inactive.
        _ = SceneRenderValidation.Inspect(scene, assets?.Metadata, strictMissing: true, policy: policy);
        // A logic/UI-only scene neither borrows a previous 3D lease nor scans an asset directory.
        if (roots.Length == 0) return new(policy, Guid.Empty, Guid.Empty, [], []);
        if (assets is null) throw new ArgumentException("Scene assets must be explicitly prepared before Play.");
        var lease = assets.Assets;
        if (lease.IsAuthoringPreview) throw new ArgumentException("Authoring preview assets cannot become a Play closure.");
        // Imported material slots are an explicitly supported fallback. Every missing-resource
        // diagnostic from lax Editor preparation must fail closed at the runtime boundary.
        if (lease.Diagnostics.Any(d => d.Code != "imported_material_slots_only"))
            throw new ArgumentException("Scene asset closure contains unresolved dependencies.");
        foreach (var root in roots) _ = lease.Require(root.Id.Value, root.ExpectedKind);
        ValidateAnimators(scene, lease);
        var members = lease.List();
        if (members.Count > RuntimeAssetLoader.MaxEntries) throw new ArgumentException("Scene asset closure member budget.");
        return new(policy, lease.ProjectId, lease.Identity, roots, members.Select(a =>
            new ResolvedSceneAsset(a.Id, a.Kind, a.Generation, a.ContentHash)).ToArray());
    }
}
