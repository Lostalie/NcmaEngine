using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;
[StructLayout(LayoutKind.Sequential)]
internal struct GpuMeshKey { public ulong Value, Generation; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MeshDescription { public uint Size, Layout, VertexCount, IndexCount, VertexBytes, IndexBytes, Stride, Reserved; public byte* Vertices; public uint* Indices; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MeshFrame { public uint Size, Count; public ulong Frame, Generation; public fixed float Viewport[4]; public fixed float Clear[4]; public uint Reserved, Reserved2; }
[StructLayout(LayoutKind.Sequential)]
public struct SceneRenderStats { public uint Size, Reserved; public ulong DeviceGeneration, LiveMeshes, ResidentBytes, MeshCreates, UploadedBytes, Draws; }
[StructLayout(LayoutKind.Sequential)]
internal struct SceneRenderApi { public uint Size, Version; public ulong Capabilities; public nint Create, Destroy, Submit, Stats; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct BindPoseMeshDescription { public MeshDescription Mesh; public uint PaletteCount, PaletteBytes; public byte* Palette; }
[StructLayout(LayoutKind.Sequential)]
internal struct SceneRenderApiV2 { public SceneRenderApi Base; public nint CreateBindPose; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateBindPoseMesh(ulong module, ulong renderer, BindPoseMeshDescription* input, GpuMeshKey* output, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint QuerySceneRender(ulong module, uint version, void* output, uint capacity, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateMesh(ulong module, ulong renderer, MeshDescription* input, GpuMeshKey* output, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint DestroyMesh(ulong module, ulong renderer, GpuMeshKey mesh, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SubmitMeshes(ulong module, ulong renderer, MeshFrame* frame, StaticMeshDraw* draws, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadSceneRenderStats(ulong module, ulong renderer, SceneRenderStats* output, PluginError* error);

/// <summary>Copied draw value: no live GameObject, scene or COM reference. Static unlit preview only.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct StaticMeshDraw
{
    internal GpuMeshKey Mesh;
    public uint FirstIndex, IndexCount;
    internal uint Reserved, Reserved2;
    internal fixed float ModelViewProjection[16];
    internal fixed float Color[4];
    /// <param name="modelViewProjection">System.Numerics row-vector model * RH view * projection (depth [0,1]).</param>
    public static StaticMeshDraw Create(GpuMesh mesh, MeshDrawRange range, Matrix4x4 modelViewProjection, Vector4 linearColor)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var m = modelViewProjection;
        StaticMeshDraw result = new() { Mesh = mesh.Key, FirstIndex = range.FirstIndex, IndexCount = range.IndexCount };
        // Row-major flattening becomes the transpose when read as HLSL column-major/column vectors.
        ReadOnlySpan<float> values = [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        for (int i = 0; i < 16; i++) result.ModelViewProjection[i] = values[i];
        result.Color[0] = linearColor.X; result.Color[1] = linearColor.Y; result.Color[2] = linearColor.Z; result.Color[3] = linearColor.W;
        return result;
    }
}

/// <summary>Owner-thread GPU lease. Never serialize handles; dispose before the renderer.</summary>
public sealed class GpuMesh : IDisposable
{
    internal RendererSession Owner { get; }
    private GpuMeshKey _key;
    internal GpuMeshKey Key { get { _ = Owner.Handle; ObjectDisposedException.ThrowIf(_key.Value == 0, this); return _key; } }
    public MeshBounds Bounds { get; }
    public uint IndexCount { get; }
    public int VertexCount { get; }
    internal int SkinBindings { get; }
    public bool IsAnimated => SkinBindings!=0;
    public bool CanUseNormalMap { get; }
    private BindPoseMeshUploadData? _bindPoseSource;
    public bool IsBindPose { get; }
    public BindPoseMeshUploadData? BindPoseSource { get { _ = Key; return _bindPoseSource; } }
    internal GpuMesh(RendererSession owner, GpuMeshKey key, MeshUploadData data, int skinBindings=0)
    { Owner = owner; _key = key; Bounds = data.Bounds; IndexCount = (uint)data.Indices.Length; CanUseNormalMap = data.CanUseNormalMap;VertexCount=data.VertexCount;SkinBindings=skinBindings; }
    internal GpuMesh(RendererSession owner, BindPoseMeshUploadData data)
    { Owner = owner; Bounds = data.Bounds; IndexCount = (uint)data.Indices.Length; _bindPoseSource = data; IsBindPose = true;VertexCount=data.VertexCount; }
    internal void Publish(GpuMeshKey key) => _key = key;
    public void Dispose() { if (_key.Value == 0) return; Owner.ReleaseMesh(this, _key); _key = default; _bindPoseSource = null; }
}

public sealed unsafe partial class RendererSession
{
    private readonly HashSet<GpuMesh> _gpuMeshes = [];
    private CreateMesh? _createMesh;
    private CreateBindPoseMesh? _createBindPoseMesh;
    private DestroyMesh? _destroyMesh;
    private SubmitMeshes? _submitMeshes;
    private ReadSceneRenderStats? _meshStats;
    public bool SupportsStaticMeshes { get { Verify(); return Module.AbiMinor >= 2; } }
    private void EnsureSceneRender()
    {
        Verify(); if (_createMesh is not null) return;
        if (!SupportsStaticMeshes) throw new NotSupportedException("Renderer ABI 1.2 is required for static meshes.");
        var query = Module.ReadFunction<QuerySceneRender>(144);
        SceneRenderApi api = default; PluginError error = default;
        PluginModule.Check(Module.Id, "query_scene_render", query(Module.Context, 1, &api, 48, &error), error);
        if (api.Size != 48 || api.Version != 1 || api.Capabilities != 1 || api.Create == 0 || api.Destroy == 0 || api.Submit == 0 || api.Stats == 0)
            throw new PluginException(Module.Id, "query_scene_render", PluginResult.AbiMismatch, "Invalid static scene-render service.");
        var create = Marshal.GetDelegateForFunctionPointer<CreateMesh>(api.Create);
        var destroy = Marshal.GetDelegateForFunctionPointer<DestroyMesh>(api.Destroy);
        var submit = Marshal.GetDelegateForFunctionPointer<SubmitMeshes>(api.Submit);
        var stats = Marshal.GetDelegateForFunctionPointer<ReadSceneRenderStats>(api.Stats);
        _destroyMesh = destroy; _submitMeshes = submit; _meshStats = stats; _createMesh = create;
    }
    public GpuMesh CreateStaticMesh(MeshUploadData data)
    {
        ArgumentNullException.ThrowIfNull(data); EnsureSceneRender();
        // Allocate/register the managed lease BEFORE native publication. No managed allocation remains
        // after GPU create succeeds; an OOM cannot leave an untracked native handle or require a risky wait.
        var mesh = new GpuMesh(this, default, data); _gpuMeshes.Add(mesh);
        GpuMeshKey key = default; PluginError error = default;
        try {
            fixed (byte* vertices = data.Vertices) fixed (uint* indices = data.Indices)
            {
                MeshDescription d = new() { Size = 48, Layout = MeshUploadData.LayoutVersion, VertexCount = (uint)data.VertexCount,
                    IndexCount = (uint)data.Indices.Length, VertexBytes = (uint)data.Vertices.Length, IndexBytes = checked((uint)data.Indices.Length * 4),
                    Stride = MeshUploadData.VertexStride, Vertices = vertices, Indices = indices };
                PluginModule.Check(Module.Id, "create_mesh", _createMesh!(Module.Context, Handle, &d, &key, &error), error);
            }
            mesh.Publish(key); return mesh;
        } catch { _gpuMeshes.Remove(mesh); throw; }
    }
    /// <summary>Immutable CPU-prepared bind pose. Retains original skin/palette; not animated GPU skinning.</summary>
    public GpuMesh CreateBindPoseMesh(BindPoseMeshUploadData data)
    {
        ArgumentNullException.ThrowIfNull(data); EnsureSceneRender();
        if (_createBindPoseMesh is null) {
            SceneRenderApiV2 api = default; PluginError queryError = default;
            var query = Module.ReadFunction<QuerySceneRender>(144);
            PluginModule.Check(Module.Id, "query_bind_pose", query(Module.Context, 2, &api, 56, &queryError), queryError);
            if (api.Base.Size != 56 || api.Base.Version != 2 || api.Base.Capabilities != 3 || api.Base.Create == 0 ||
                api.Base.Destroy == 0 || api.Base.Submit == 0 || api.Base.Stats == 0 || api.CreateBindPose == 0)
                throw new PluginException(Module.Id, "query_bind_pose", PluginResult.AbiMismatch, "Invalid bind-pose scene-render service.");
            _createBindPoseMesh = Marshal.GetDelegateForFunctionPointer<CreateBindPoseMesh>(api.CreateBindPose);
        }
        var mesh = new GpuMesh(this, data); _gpuMeshes.Add(mesh);
        GpuMeshKey key = default; PluginError error = default;
        try {
            fixed (byte* vertices = data.Vertices) fixed (uint* indices = data.Indices) fixed (byte* palette = data.Palette) {
                BindPoseMeshDescription d = new() { Mesh = new() { Size = 64, Layout = BindPoseMeshUploadData.LayoutVersion,
                    VertexCount = (uint)data.VertexCount, IndexCount = (uint)data.Indices.Length, VertexBytes = (uint)data.Vertices.Length,
                    IndexBytes = checked((uint)data.Indices.Length * 4), Stride = BindPoseMeshUploadData.VertexStride, Vertices = vertices, Indices = indices },
                    PaletteCount = (uint)data.BindingCount, PaletteBytes = (uint)data.Palette.Length, Palette = palette };
                PluginModule.Check(Module.Id, "create_bind_pose_mesh", _createBindPoseMesh!(Module.Context, Handle, &d, &key, &error), error);
            }
            mesh.Publish(key); return mesh;
        } catch { _gpuMeshes.Remove(mesh); throw; }
    }
    internal void ReleaseMesh(GpuMesh mesh, GpuMeshKey key)
    {
        Verify(); if (mesh.Owner != this) throw new ArgumentException("Foreign renderer resource.");
        PluginError error = default;
        PluginModule.Check(Module.Id, "destroy_mesh", (mesh.IsAnimated?_destroySkin:_destroyMesh)!(Module.Context, Handle, key, &error), error);
        _gpuMeshes.Remove(mesh);
    }
    public SceneRenderStats SceneStats
    {
        get {
            EnsureSceneRender(); SceneRenderStats stats = default; PluginError error = default;
            PluginModule.Check(Module.Id, "scene_render_stats", _meshStats!(Module.Context, Handle, &stats, &error), error);
            if (stats.Size != 56 || stats.Reserved != 0 || stats.DeviceGeneration != Handle)
                throw new PluginException(Module.Id, "scene_render_stats", PluginResult.AbiMismatch, "Invalid resource stats.");
            return stats;
        }
    }
    /// <summary>One bounded batch, resident geometry, two-sided unlit colors. Does not perform scene traversal.</summary>
    public void SubmitStaticMeshes(ulong frame, ReadOnlySpan<StaticMeshDraw> draws, Vector4 viewport, Vector4 linearClear)
    {
        EnsureSceneRender(); if (draws.Length > 4096) throw new ArgumentException("Static draw budget exceeded.");
        MeshFrame f = new() { Size = 64, Count = (uint)draws.Length, Frame = frame, Generation = Handle };
        f.Viewport[0] = viewport.X; f.Viewport[1] = viewport.Y; f.Viewport[2] = viewport.Z; f.Viewport[3] = viewport.W;
        f.Clear[0] = linearClear.X; f.Clear[1] = linearClear.Y; f.Clear[2] = linearClear.Z; f.Clear[3] = linearClear.W;
        PluginError error = default;
        fixed (StaticMeshDraw* pointer = draws)
            PluginModule.Check(Module.Id, "submit_meshes", _submitMeshes!(Module.Context, Handle, &f, pointer, &error), error);
        SubmitCalls++; CopiedBytes += 64 + (ulong)draws.Length * 112;
    }
}
