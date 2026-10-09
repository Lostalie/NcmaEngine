using System.Numerics;
using System.Security.Cryptography;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;
using Vector3 = System.Numerics.Vector3;

// Owner-thread, explicit OFF-frame immutable resource preparation. No importer or live World.
public sealed class SceneGpuResources : IDisposable
{
    private PreparedSceneAssetLease? _assets;
    private readonly List<IDisposable> _leases = [];
    private readonly Dictionary<Guid, (GpuMesh Mesh, MeshDrawRange[] Ranges)> _meshes = [];
    private readonly Dictionary<Guid, (GpuMesh Mesh, MeshDrawRange[] Ranges)> _animated = [];
    private readonly GpuSkinRequest[] _skinRequests = new GpuSkinRequest[32];
    private readonly Dictionary<(Guid Mesh, Guid Set), GpuMaterial[]> _sets = [];
    private readonly Dictionary<(Guid Mesh, Guid Material), GpuMaterial> _overrides = [];
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed;
    public PreparedSceneAssets Metadata => Assets.Metadata;
    private PreparedSceneAssetLease Assets { get { Verify(); return _assets!; } }
    public IReadOnlyList<SceneRenderDiagnostic> Diagnostics { get; private set; } = [];
    private void Verify() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("GPU resources require owner thread."); ObjectDisposedException.ThrowIf(_disposed, this); }
    public static SceneGpuResources Prepare(RenderResourceCache cache, PreparedSceneAssetLease prepared, SceneDocumentSnapshot scene,
        RendererSession? renderer = null, SceneAnimationSession? animation = null,RegisteredSkinShader? skinShader=null)
    {
        ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(scene);
        var result = new SceneGpuResources(); var diagnostics = new List<SceneRenderDiagnostic>();
        try
        {
            result._assets = prepared.AcquireLease(); var cpu = result._assets.Assets;
            var registry = RenderComponentRegistry.CreateRegistry();
            var uploads = new Dictionary<Guid, MeshUploadData>();
            var materials = cpu.List().OfType<RuntimeMaterialAsset>().ToArray();
            foreach (var obj in scene.Objects)
            {
                var component = obj.Components.SingleOrDefault(c => c.TypeId == StaticMeshData.TypeId);
                var skin = obj.Components.SingleOrDefault(c => c.TypeId == SkinnedMeshData.TypeId);
                if (component is null && skin is null) continue;
                var data = component is not null ? registry.Decode<StaticMeshData>(component) : ToStatic(registry.Decode<SkinnedMeshData>(skin!));
                var kind = skin is null ? AssetKind.StaticMesh : AssetKind.SkinnedMesh;
                if (!cpu.TryResolve(data.MeshId, kind, out _)) { diagnostics.Add(new(obj.Id, "asset_missing", data.MeshId)); continue; }
                var source = cpu.RequireMesh(data.MeshId, kind);
                MeshUploadData upload;
                if (skin is not null) {
                    var character = animation?.Characters.SingleOrDefault(c => c.Id == obj.Id);
                    if (character is null || renderer is null) { diagnostics.Add(new(obj.Id, "gpu_skin_unprepared", data.MeshId)); continue; }
                    upload = character.Upload.Attributes;
                    result._leases.EnsureCapacity(result._leases.Count+1);
                    var gpu = skinShader is null?renderer.CreateSkinnedMesh(character.Upload):renderer.CreateSkinnedMesh(character.Upload,skinShader); result._leases.Add(gpu);
                    result._animated.Add(obj.Id, (gpu, character.Upload.Ranges.ToArray()));
                } else if (!uploads.TryGetValue(source.Id, out upload!)) { upload = MeshUploadData.PrepareStatic(source.CopyPayload()); uploads.Add(source.Id, upload); }
                if (skin is null && !result._meshes.ContainsKey(source.Id))
                {
                    var lease = cache.AcquireMesh(Version(source), upload); result._leases.Add(lease); result._meshes.Add(source.Id, (lease.Resource, upload.Ranges.ToArray()));
                }
                if (!result._sets.ContainsKey((source.Id, data.MaterialSetId)))
                {
                    RuntimeMaterialSetAsset? set = cpu.TryResolve(data.MaterialSetId, AssetKind.MaterialSet, out var asset) ? (RuntimeMaterialSetAsset)asset! : null;
                    int count = set?.SlotCount ?? source.MaterialSlots; if (count < source.MaterialSlots) throw new ArgumentException("Material set slot coverage.");
                    var slots = new GpuMaterial[count];
                    for (int i = 0; i < count; i++)
                    {
                        Guid id = set?.MaterialAt(i) ?? Guid.Empty; RuntimeMaterialAsset? material = id != Guid.Empty && cpu.TryResolve(id, AssetKind.Material, out var resolved) ? (RuntimeMaterialAsset)resolved! : null;
                        if (material is null) diagnostics.Add(new(obj.Id, set is { ImportedSlotsOnly: true } ? "imported_material_pbr_default" : "missing_material_default", data.MaterialSetId));
                        slots[i] = Acquire(material, upload.CanUseNormalMap);
                    }
                    result._sets.Add((source.Id, data.MaterialSetId), slots);
                }
                foreach (var material in materials)
                    if (!result._overrides.ContainsKey((source.Id, material.Id))) result._overrides.Add((source.Id, material.Id), Acquire(material, upload.CanUseNormalMap));
                foreach (var diagnostic in upload.Diagnostics) diagnostics.Add(new(obj.Id, diagnostic.Code, source.Id));
            }
            result.Diagnostics = diagnostics.AsReadOnly(); return result;

            GpuMaterial Acquire(RuntimeMaterialAsset? asset, bool normal)
            {
                // Explicit ephemeral default, NOT a persistent source-material identity or authoring conversion.
                var definition = asset?.Definition ?? MaterialDefinition.Default(Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff"));
                var version = asset is null ? new RenderAssetVersion(definition.AssetId, 1, Convert.ToHexString(SHA256.HashData(MaterialCodec.Encode(definition)))) : Version(asset);
                var lease = cache.AcquireMaterial(version, definition, id => cpu.TryResolve(id, AssetKind.Texture, out var texture) ? new ResolvedTexture(Version(texture!), ((RuntimeTextureAsset)texture!).Data) : null, true, normal, out var losses);
                result._leases.Add(lease); foreach (var loss in losses) diagnostics.Add(new(Guid.Empty, loss.Code, definition.AssetId)); return lease.Resource;
            }
        }
        catch { result.Dispose(); cache.Trim(); throw; }
    }
    private static RenderAssetVersion Version(RuntimeAsset asset) => new(asset.Id, asset.Generation, asset.ContentHash);
    private static StaticMeshData ToStatic(SkinnedMeshData s) => new(s.MeshId,s.MaterialSetId,s.Visible,s.CastShadow,s.LayerMask);
    public bool UpdateAnimation(RendererSession renderer, SceneAnimationSession animation, ulong frame)
    {
        Verify(); int count=0,offset=0;
        for(int i=0;i<animation.Characters.Count;i++) { var c=animation.Characters[i];if(!c.Active)continue;
            if(!_animated.TryGetValue(c.Id,out var instance))throw new InvalidOperationException("Animated GPU instance is unprepared.");
            _skinRequests[count++]=new(instance.Mesh,offset);offset+=c.Bindings.BindingCount;
        }
        return count==0 || renderer.TryUpdateSkins(frame,_skinRequests.AsSpan(0,count),animation.Palette);
    }
    public void CaptureCharacterVertices(RendererSession renderer,Guid objectId,Span<byte> output)
    { Verify();if(!_animated.TryGetValue(objectId,out var instance))throw new ArgumentException("Unknown animated instance.");renderer.CaptureSkinVertices(instance.Mesh,output); }
    public int Encode(IReadOnlyList<SceneDrawItem> items, Matrix4x4 cameraVP, Matrix4x4? cullVP, Span<SceneGpuDraw> output, List<SceneRenderDiagnostic> diagnostics, uint lightMask = uint.MaxValue)
    {
        Verify(); int count = 0;
        for (int itemIndex=0;itemIndex<items.Count;itemIndex++)
        {
            var item=items[itemIndex];
            bool skin = item.Mesh.Kind == AssetKind.SkinnedMesh;
            // Bind-pose bounds are not conservative for animation; keep all layer-selected casters.
            if (!skin && cullVP is { } clip && !ShadowVolume.Intersects(item.Mesh.BoundsMin, item.Mesh.BoundsMax, item.Model * clip)) continue;
            bool found = skin ? _animated.TryGetValue(item.ObjectId,out var mesh) : _meshes.TryGetValue(item.Mesh.Id,out mesh);
            if (!found || !_sets.TryGetValue((item.Mesh.Id, item.MaterialSetId), out var set)) { diagnostics.Add(new(item.ObjectId, "gpu_asset_unprepared", item.Mesh.Id)); continue; }
            foreach (var range in mesh.Ranges)
            {
                if (count >= output.Length) throw new ArgumentException("Scene draw/material range budget.");
                GpuMaterial material = set[range.MaterialSlot]; var scalar = item.MaterialOverride;
                if (scalar is { MaterialId: var id } && id != Guid.Empty)
                { if (!_overrides.TryGetValue((item.Mesh.Id, id), out var replacement)) { diagnostics.Add(new(item.ObjectId, "gpu_override_unprepared", id)); continue; } material = replacement; }
                output[count++] = SceneGpuDraw.Create(mesh.Mesh, material, range, item.Model, cameraVP, scalar?.OverrideScalars ?? false, scalar?.Metallic ?? 0, scalar?.Roughness ?? .5f, (item.LayerMask & lightMask)!=0);
            }
        }
        return count;
    }
    public void Dispose()
    { if (_disposed) return; Verify(); for (int i = _leases.Count - 1; i >= 0; i--) _leases[i].Dispose(); _leases.Clear(); _assets?.Dispose(); _assets = null; _disposed = true; }
}
public static class ShadowVolume
{
    public static Matrix4x4 Create(SceneCameraView camera, Vector3 toLight, float distance = 40)
    {
        if (!float.IsFinite(distance) || distance is < 1 or > 1000 || !Finite(toLight) || toLight.LengthSquared() < .0001f || !Matrix4x4.Invert(camera.ViewProjection, out var inverse)) throw new ArgumentException("Shadow volume.");
        var middle = Vector4.Transform(new Vector4(0, 0, .5f, 1), inverse); var forward = new Vector3(middle.X, middle.Y, middle.Z) / middle.W - camera.Position;
        if (!Finite(forward) || forward.LengthSquared() < .0001f) throw new ArgumentException("Shadow camera.");
        Vector3 center = camera.Position + Vector3.Normalize(forward) * distance * .35f; toLight = Vector3.Normalize(toLight);
        Vector3 up = Math.Abs(Vector3.Dot(toLight, Vector3.UnitY)) > .95f ? Vector3.UnitX : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(center + toLight * distance * 2, center, up);
        return view * Matrix4x4.CreateOrthographic(distance * 2, distance * 2, .1f, distance * 4);
    }
    public static bool Intersects(Vector3 min, Vector3 max, Matrix4x4 m)
    {
        int outside = 63;
        for (int i = 0; i < 8; i++) { var q = Vector4.Transform(new Vector4((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z, 1), m);
            if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W)) throw new ArgumentException("Shadow culling matrix.");
            int code = (q.X < -q.W ? 1 : 0) | (q.X > q.W ? 2 : 0) | (q.Y < -q.W ? 4 : 0) | (q.Y > q.W ? 8 : 0) | (q.Z < 0 ? 16 : 0) | (q.Z > q.W ? 32 : 0); outside &= code; }
        return outside == 0;
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
