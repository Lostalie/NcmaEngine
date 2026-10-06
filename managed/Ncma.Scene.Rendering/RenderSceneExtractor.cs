using System.Numerics;
using Ncma.Assets;
using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

// Copied immutable values only. These records contain no World, GameObject, lease or native address.
public readonly record struct SceneCameraView(Guid ObjectId, Matrix4x4 ViewProjection, Vector3 Position, CameraData Data);
public readonly record struct SceneLightView(Guid ObjectId, Vector3 Direction, DirectionalLightData Data);
public readonly record struct SceneDrawItem(int FrameIndex, Guid ObjectId, SceneAssetInfo Mesh, Guid MaterialSetId,
    Matrix4x4 Model, MaterialOverrideData? MaterialOverride, uint LayerMask = uint.MaxValue);
public sealed class RenderSceneView
{
    public Guid WorldId { get; }
    public ulong Revision { get; }
    public Guid FrameIdentity { get; } = Guid.NewGuid();
    public SceneCameraView? Camera { get; }
    public SceneLightView? PrimaryLight { get; }
    public IReadOnlyList<SceneDrawItem> Geometry { get; }
    public IReadOnlyList<SceneDrawItem> ShadowCasters { get; }
    public IReadOnlyDictionary<Guid, int> ObjectIndices { get; }
    public IReadOnlyList<SceneRenderDiagnostic> Diagnostics { get; }
    internal RenderSceneView(Guid worldId, ulong revision, SceneCameraView? camera, SceneLightView? light,
        List<SceneDrawItem> geometry, List<SceneDrawItem> casters, Dictionary<Guid, int> indices, List<SceneRenderDiagnostic> diagnostics)
    {
        WorldId = worldId; Revision = revision; Camera = camera; PrimaryLight = light;
        Geometry = geometry.AsReadOnly(); ShadowCasters = casters.AsReadOnly(); Diagnostics = diagnostics.AsReadOnly();
        ObjectIndices = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, int>(indices);
    }
}
// One extractor per Edit/Play world. Version observation invalidates the cache, including external mutations and restore.
// No CaptureSnapshot/JSON/file IO/asset decoding/GPU work in extraction; cache hits allocate nothing.
public sealed class RenderSceneExtractor
{
    private readonly World _world;
    private RenderSceneView? _cached;
    private Guid _assets, _camera;
    private SceneCameraView? _browser;
    private uint _width, _height;
    private bool _skin;
    private IReadOnlyDictionary<Guid, TransformData>? _presentationTransforms;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    public RenderSceneExtractor(World world) { ArgumentNullException.ThrowIfNull(world); _ = world.Identity; _world = world; }
    // Optional owned immutable presentation values; never install them into World.
    public RenderSceneView Extract(PreparedSceneAssets assets, Guid cameraObject, uint width, uint height, SceneCameraView? browserCamera = null, bool gpuSkin = false,
        IReadOnlyDictionary<Guid, TransformData>? presentationTransforms = null)
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Extraction requires the World owner thread.");
        ArgumentNullException.ThrowIfNull(assets);
        if (width is 0 or > 16384 || height is 0 or > 16384 || cameraObject != Guid.Empty && browserCamera is not null)
            throw new ArgumentException("Select either an explicit scene camera or an independent browser camera and a bounded target.");
        if (_world.IsUpdating) throw new InvalidOperationException("Extraction requires a committed safe boundary.");
        Guid worldId = _world.Identity; ulong revision = _world.Revision;
        if (_cached is not null && _cached.WorldId == worldId && _cached.Revision == revision && _assets == assets.Identity &&
            _camera == cameraObject && _browser == browserCamera && _width == width && _height == height && _skin == gpuSkin && ReferenceEquals(_presentationTransforms,presentationTransforms)) return _cached;
        using var read = _world.ReadOnly();
        var objects = _world.GetObjects(); var diagnostics = new List<SceneRenderDiagnostic>();
        TransformData Transform(GameObject obj)=>presentationTransforms is not null && presentationTransforms.TryGetValue(obj.PersistentId,out var t) ? t : obj.Get<TransformData>();
        SceneCameraView? camera = browserCamera;
        if (camera is { } provided && (provided.ObjectId != Guid.Empty || !SceneRenderValidation.Finite(provided.ViewProjection) ||
            !Matrix4x4.Invert(provided.ViewProjection, out var inverseCamera) || !SceneRenderValidation.Finite(inverseCamera) || !PreparedSceneAssets.Finite(provided.Position)))
            throw new ArgumentException("Invalid browser camera values.");
        if (camera is { } b) _ = CameraData.Validate(b.Data);
        if (camera is null && cameraObject != Guid.Empty)
        {
            GameObject? selected = null;
            foreach (var item in objects) if (item.PersistentId == cameraObject) { selected = item; break; }
            if (selected is null || !selected.Has<CameraData>() || !selected.Has<TransformData>()) diagnostics.Add(new(cameraObject, "camera_missing"));
            else try { camera = CreateCamera(cameraObject, Transform(selected), selected.Get<CameraData>(), width, height); }
                catch (ArgumentException) { diagnostics.Add(new(cameraObject, "camera_invalid")); }
        }
        else if (camera is null) diagnostics.Add(new(Guid.Empty, "camera_selection_required"));
        SceneLightView? light = null; int primaryLights = 0;
        foreach (var obj in objects)
        {
            if (!obj.Has<DirectionalLightData>()) continue;
            var data = obj.Get<DirectionalLightData>(); if (!data.IsPrimary) continue;
            if (++primaryLights > 1) { diagnostics.Add(new(obj.PersistentId, "multiple_primary_lights")); continue; }
            try
            {
                _ = DirectionalLightData.Validate(data);
                if (!obj.Has<TransformData>()) throw new ArgumentException();
                var t = Transform(obj); _ = SceneRenderValidation.Model(t);
                light = new(obj.PersistentId, Vector3.Transform(-Vector3.UnitZ, t.Rotation), data);
            }
            catch (ArgumentException) { diagnostics.Add(new(obj.PersistentId, "light_invalid")); }
        }
        if (primaryLights > 1) light = null; // Never silently select the first light in an invalid committed World.
        var geometry = new List<SceneDrawItem>(); var casters = new List<SceneDrawItem>(); var indices = new Dictionary<Guid, int>();
        foreach (var obj in objects)
        {
            Guid id = obj.PersistentId;
            if (obj.Has<StaticMeshData>() && obj.Has<SkinnedMeshData>()) { diagnostics.Add(new(id, "geometry_conflict")); continue; }
            bool skin = obj.Has<SkinnedMeshData>();
            if (skin && !gpuSkin) { diagnostics.Add(new(id, "skinning_unprepared")); continue; }
            if (!skin && !obj.Has<StaticMeshData>()) continue;
            var s = skin ? SkinnedMeshData.Validate(obj.Get<SkinnedMeshData>()) : default;
            var mesh = skin ? new StaticMeshData(s.MeshId,s.MaterialSetId,s.Visible,s.CastShadow,s.LayerMask) : obj.Get<StaticMeshData>(); if (!mesh.Visible) continue;
            try
            {
                _ = StaticMeshData.Validate(mesh);
                if (!obj.Has<TransformData>()) { diagnostics.Add(new(id, "transform_missing")); continue; }
                var model = SceneRenderValidation.Model(Transform(obj));
                if (!assets.TryFind(mesh.MeshId, out var info)) { diagnostics.Add(new(id, "asset_missing", mesh.MeshId)); continue; }
                if (info!.Kind != (skin ? AssetKind.SkinnedMesh : AssetKind.StaticMesh)) { diagnostics.Add(new(id, "asset_kind_mismatch", mesh.MeshId)); continue; }
                if (!assets.TryFind(mesh.MaterialSetId, out var material)) diagnostics.Add(new(id, "asset_missing", mesh.MaterialSetId));
                else if (material!.Kind != AssetKind.MaterialSet || material.MaterialSlots < Math.Max(1, info.MaterialSlots))
                { diagnostics.Add(new(id, "material_set_invalid", mesh.MaterialSetId)); continue; }
                MaterialOverrideData? overridden = obj.Has<MaterialOverrideData>() ? MaterialOverrideData.Validate(obj.Get<MaterialOverrideData>()) : null;
                if (overridden is { MaterialId: var materialId } && materialId != Guid.Empty)
                {
                    if (!assets.TryFind(materialId, out var overrideInfo)) diagnostics.Add(new(id, "asset_missing", materialId));
                    else if (overrideInfo!.Kind != AssetKind.Material) { diagnostics.Add(new(id, "asset_kind_mismatch", materialId)); continue; }
                }
                // Indices cover resolved visible geometry before camera culling, not just the camera's survivors.
                bool cameraVisible = camera is { } c && (mesh.LayerMask & c.Data.LayerMask) != 0 && (skin || IntersectsClip(info.BoundsMin, info.BoundsMax, model * c.ViewProjection));
                int index = indices.Count; indices.Add(id, index);
                var draw = new SceneDrawItem(index, id, info, mesh.MaterialSetId, model, overridden, mesh.LayerMask);
                if (cameraVisible) geometry.Add(draw);
                // Conservative independent caster set. C will perform light-space culling; never use the camera set.
                if (light is { } l && l.Data.CastShadow && mesh.CastShadow && (mesh.LayerMask & l.Data.LayerMask) != 0) casters.Add(draw);
            }
            catch (ArgumentException) { diagnostics.Add(new(id, "render_data_invalid")); }
        }
        var view = new RenderSceneView(worldId, revision, camera, light, geometry, casters, indices, diagnostics);
        _cached = view; _assets = assets.Identity; _camera = cameraObject; _browser = browserCamera; _width = width; _height = height; _skin = gpuSkin;_presentationTransforms=presentationTransforms;
        return view;
    }
    public static SceneCameraView CreateCamera(Guid id, TransformData transform, CameraData camera, uint width, uint height)
    {
        transform = TransformData.Validate(transform);
        _ = SceneRenderValidation.Model(transform); camera = CameraData.Validate(camera);
        if (width is 0 or > 16384 || height is 0 or > 16384) throw new ArgumentException("Invalid camera target size.");
        var orientation = Matrix4x4.CreateFromQuaternion(transform.Rotation) * Matrix4x4.CreateTranslation(transform.Position);
        if (!Matrix4x4.Invert(orientation, out var view)) throw new ArgumentException("Invalid camera orientation.");
        float aspect = width * camera.ViewportWidth / (height * camera.ViewportHeight);
        var projection = camera.Projection == CameraProjection.Perspective
            ? Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFovRadians, aspect, camera.Near, camera.Far)
            : Matrix4x4.CreateOrthographic(camera.OrthographicHeight * aspect, camera.OrthographicHeight, camera.Near, camera.Far);
        var result = view * projection;
        if (!SceneRenderValidation.Finite(result)) throw new ArgumentException("Camera matrix overflow.");
        return new(id, result, transform.Position, camera);
    }
    internal static bool IntersectsClip(Vector3 min, Vector3 max, Matrix4x4 matrix)
    {
        if (!SceneRenderValidation.Finite(matrix)) throw new ArgumentException("Clip matrix overflow.");
        int outside = 63;
        for (int i = 0; i < 8; ++i)
        {
            var p = Vector4.Transform(new Vector4((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z, 1), matrix);
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) || !float.IsFinite(p.W)) throw new ArgumentException("Clip coordinate overflow.");
            int mask = (p.X < -p.W ? 1 : 0) | (p.X > p.W ? 2 : 0) | (p.Y < -p.W ? 4 : 0) | (p.Y > p.W ? 8 : 0) |
                (p.Z < 0 ? 16 : 0) | (p.Z > p.W ? 32 : 0);
            outside &= mask;
        }
        return outside == 0;
    }
}
