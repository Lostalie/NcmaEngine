using System.Numerics;
using Ncma.Assets;
using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

public readonly record struct SceneRenderDiagnostic(Guid ObjectId, string Code, Guid AssetId = default);
// Prepared off-frame by the asset service, never by a render tick. Generation/hash identify immutable data.
public sealed record SceneAssetInfo(Guid Id, AssetKind Kind, ulong Generation, string ContentHash,
    int MaterialSlots = 0, Guid SkeletonId = default, Guid CharacterId = default,
    Vector3 BoundsMin = default, Vector3 BoundsMax = default);
public sealed class PreparedSceneAssets
{
    private readonly Dictionary<Guid, SceneAssetInfo> _assets = [];
    public Guid Identity { get; } = Guid.NewGuid();
    public PreparedSceneAssets(IEnumerable<SceneAssetInfo> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        foreach (var info in assets)
        {
            if (info is null || info.Id == Guid.Empty || !Enum.IsDefined(info.Kind) || info.Generation == 0 ||
                info.ContentHash is null || info.ContentHash.Length != 64 || info.ContentHash.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F')) ||
                info.MaterialSlots is < 0 or > 4096 || !Finite(info.BoundsMin) || !Finite(info.BoundsMax) ||
                info.BoundsMin.X > info.BoundsMax.X || info.BoundsMin.Y > info.BoundsMax.Y || info.BoundsMin.Z > info.BoundsMax.Z ||
                _assets.Count >= AssetCatalog.MaxIdentities || !_assets.TryAdd(info.Id, info))
                throw new ArgumentException("Invalid, duplicate or oversized prepared render metadata.");
        }
    }
    internal static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    public bool TryFind(Guid id, out SceneAssetInfo? info) => _assets.TryGetValue(id, out info);
    public static PreparedSceneAssets Empty { get; } = new([]);
}
public static class SceneRenderValidation
{
    private static readonly ComponentRegistry Registry = RenderComponentRegistry.CreateRegistry();
    public static void RequireComposition(SceneDocumentSnapshot snapshot) => _ = Inspect(snapshot);
    public static IReadOnlyList<SceneRenderDiagnostic> Inspect(SceneDocumentSnapshot snapshot,
        PreparedSceneAssets? assets = null, bool strictMissing = false, AssetCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot); assets ??= PreparedSceneAssets.Empty;
        var diagnostics = new List<SceneRenderDiagnostic>(); int primaryLights = 0;
        foreach (var obj in snapshot.Objects)
        {
            var components = obj.Components.ToDictionary(c => c.TypeId, StringComparer.Ordinal);
            bool mesh = components.ContainsKey(StaticMeshData.TypeId), skin = components.ContainsKey(SkinnedMeshData.TypeId);
            bool spatial = mesh || skin || components.ContainsKey(CameraData.TypeId) || components.ContainsKey(DirectionalLightData.TypeId);
            if (mesh && skin) throw new ArgumentException("An object cannot contain both static and skinned geometry.");
            if (components.ContainsKey(Ncma.Animation.ClipPlaybackData.TypeId) && !skin)
                throw new ArgumentException("Clip playback requires a skinned mesh on the same object.");
            if(components.ContainsKey(Ncma.Animation.RootMotionData.TypeId) && (!skin || !components.ContainsKey(Ncma.Animation.ClipPlaybackData.TypeId) || !components.ContainsKey("ncma.character.capsule")))
                throw new ArgumentException("Root motion requires character capsule/skinned clip composition.");
            if (components.ContainsKey(MaterialOverrideData.TypeId) && !mesh && !skin)
                throw new ArgumentException("Material override requires a mesh component.");
            if (spatial)
            {
                if (!components.TryGetValue("ncma.transform", out var t)) throw new ArgumentException("Render components require an explicit Transform.");
                _ = Model(Registry.Decode<TransformData>(t));
            }
            if (components.TryGetValue(CameraData.TypeId, out var camera)) _ = Registry.Decode<CameraData>(camera);
            if (components.TryGetValue(DirectionalLightData.TypeId, out var light) && Registry.Decode<DirectionalLightData>(light).IsPrimary)
                if (++primaryLights > 1) throw new ArgumentException("Only one primary directional light is supported.");
            SceneAssetInfo? geometry = null, materials = null;
            if (mesh)
            {
                var data = Registry.Decode<StaticMeshData>(components[StaticMeshData.TypeId]);
                geometry = Resolve(obj.Id, data.MeshId, AssetKind.StaticMesh); materials = Resolve(obj.Id, data.MaterialSetId, AssetKind.MaterialSet);
            }
            if (skin)
            {
                var data = Registry.Decode<SkinnedMeshData>(components[SkinnedMeshData.TypeId]);
                var character = Resolve(obj.Id, data.CharacterId, AssetKind.Character); var skeleton = Resolve(obj.Id, data.SkeletonId, AssetKind.Skeleton);
                geometry = Resolve(obj.Id, data.MeshId, AssetKind.SkinnedMesh); materials = Resolve(obj.Id, data.MaterialSetId, AssetKind.MaterialSet);
                if (geometry is not null && (geometry.SkeletonId != data.SkeletonId || geometry.CharacterId != data.CharacterId))
                    throw new ArgumentException("Skinned mesh/character/skeleton identity mismatch.");
                if (skeleton is not null && (skeleton.CharacterId != data.CharacterId || skeleton.SkeletonId != data.SkeletonId) ||
                    character is not null && skeleton is not null && character.Generation != skeleton.Generation ||
                    geometry is not null && skeleton is not null && geometry.Generation != skeleton.Generation)
                    throw new ArgumentException("Character/skeleton/mesh immutable generation mismatch.");
                if (components.TryGetValue(Ncma.Animation.ClipPlaybackData.TypeId, out var playback)) {
                    var clip = Resolve(obj.Id, Registry.Decode<Ncma.Animation.ClipPlaybackData>(playback).ClipId, AssetKind.Clip);
                    if (clip is not null && (clip.CharacterId != data.CharacterId || clip.SkeletonId != data.SkeletonId ||
                        skeleton is not null && clip.Generation != skeleton.Generation))
                        throw new ArgumentException("Clip must belong to the exact character/skeleton generation.");
                }
            }
            if (geometry is not null && materials is not null && materials.MaterialSlots < Math.Max(1, geometry.MaterialSlots))
                throw new ArgumentException("Material set does not cover all mesh slots.");
            if (components.TryGetValue(MaterialOverrideData.TypeId, out var material))
            {
                var data = Registry.Decode<MaterialOverrideData>(material);
                if (data.MaterialId != Guid.Empty) _ = Resolve(obj.Id, data.MaterialId, AssetKind.Material);
            }
        }
        return diagnostics.AsReadOnly();

        SceneAssetInfo? Resolve(Guid obj, Guid id, AssetKind kind)
        {
            if (catalog is not null)
            {
                bool found = catalog.TryResolve(new(new(id), kind), out var root, out var code);
                if (code == "asset_kind_mismatch") throw new ArgumentException("Catalog render asset kind mismatch: " + id);
                if (found && kind == AssetKind.StaticMesh && root!.AssetId == id)
                    throw new ArgumentException("Mesh components require a typed mesh subasset, not a model root.");
            }
            if (!assets.TryFind(id, out var info))
            {
                if (strictMissing) throw new ArgumentException("Required render asset is unresolved: " + id);
                diagnostics.Add(new(obj, "asset_missing", id)); return null;
            }
            if (info!.Kind != kind) throw new ArgumentException("Render asset kind mismatch: " + id);
            return info;
        }
    }
    internal static Matrix4x4 Model(TransformData transform)
    {
        transform = TransformData.Validate(transform);
        if (transform.Scale.X <= 0 || transform.Scale.Y <= 0 || transform.Scale.Z <= 0)
            throw new ArgumentException("M3 render transforms require positive nonsingular scale.");
        var model = Matrix4x4.CreateScale(transform.Scale) * Matrix4x4.CreateFromQuaternion(transform.Rotation) * Matrix4x4.CreateTranslation(transform.Position);
        if (!Finite(model) || !Matrix4x4.Invert(model, out var inverse) || !Finite(inverse))
            throw new ArgumentException("Render transform matrix cannot be represented or inverted.");
        return model;
    }
    internal static bool Finite(Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
        float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
        float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
        float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
