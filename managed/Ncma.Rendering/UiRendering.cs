using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
using Ncma.Ui;

namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal struct UiApi { public uint Size, Version; public ulong Caps; public nint Create, Image, List, Destroy, Submit, Stats; }
[StructLayout(LayoutKind.Sequential)] public readonly record struct UiGpuKey(ulong Value, ulong Generation);
[StructLayout(LayoutKind.Sequential)] public struct UiVertex { public Vector2 Position, UV; public Vector4 Color; public Vector2 Local, Extent; public float Radius; }
[StructLayout(LayoutKind.Sequential)] public struct UiBatch { public uint FirstVertex, VertexCount; internal UiGpuKey Image; public Vector4 Clip; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct UiImageDescription { public uint Size, Width, Height, Bytes; public byte* Pixels; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct UiListDescription { public uint Size, Vertices, Batches, Reserved; public UiVertex* VertexData; public UiBatch* BatchData; }
[StructLayout(LayoutKind.Sequential)] internal struct UiFrame { public uint Size, Overlay; public ulong Frame; public UiGpuKey List; public Vector4 Clear; }
[StructLayout(LayoutKind.Sequential)] public struct UiRenderStats { public uint Size, PureUi; public ulong Generation, Images, Lists, ResidentBytes, UploadedBytes, Draws, Submits; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint QueryUi(ulong context, uint version, void* output, uint bytes, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateUiImage(ulong context, ulong renderer, UiImageDescription* description, UiGpuKey* key, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateUiList(ulong context, ulong renderer, UiListDescription* description, UiGpuKey* key, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint DestroyUi(ulong context, ulong renderer, UiGpuKey key, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SubmitUi(ulong context, ulong renderer, UiFrame* frame, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadUiStats(ulong context, ulong renderer, UiRenderStats* stats, PluginError* error);

public sealed class UiGpuImage : IDisposable
{
    internal RendererSession Owner { get; } internal UiGpuKey Key { get; private set; }
    internal UiGpuImage(RendererSession owner, UiGpuKey key) { Owner = owner; Key = key; }
    public void Dispose() { if (Key.Value == 0) return; Owner.ReleaseUi(Key); Key = default; }
}
public sealed class UiGpuList : IDisposable
{
    internal RendererSession Owner { get; } internal UiGpuKey Key { get; private set; }
    internal UiGpuList(RendererSession owner, UiGpuKey key) { Owner = owner; Key = key; }
    public void Dispose() { if (Key.Value == 0) return; Owner.ReleaseUi(Key); Key = default; }
}
public sealed unsafe partial class RendererSession
{
    private UiApi? _uiApi;
    private CreateUiImage? _uiImage; private CreateUiList? _uiList; private DestroyUi? _uiDestroy; private SubmitUi? _uiSubmit; private ReadUiStats? _uiStats;
    internal static UiApi QueryUiApi(PluginModule module)
    {
        if (module.AbiMinor < 2) throw new NotSupportedException("UI rendering requires renderer query support.");
        var query = module.ReadFunction<QueryUi>(144); UiApi api = default; PluginError error = default;
        PluginModule.Check(module.Id, "query_ui", query(module.Context, 6, &api, 64, &error), error);
        if (api.Size != 64 || api.Version != 1 || api.Caps != 7 || api.Create == 0 || api.Image == 0 || api.List == 0 || api.Destroy == 0 || api.Submit == 0 || api.Stats == 0) throw new ArgumentException("UI API contract mismatch.");
        return api;
    }
    private void EnsureUi()
    {
        Verify(); if (_uiApi is not null) return; var api = QueryUiApi(Module);
        _uiImage = Marshal.GetDelegateForFunctionPointer<CreateUiImage>(api.Image); _uiList = Marshal.GetDelegateForFunctionPointer<CreateUiList>(api.List);
        _uiDestroy = Marshal.GetDelegateForFunctionPointer<DestroyUi>(api.Destroy); _uiSubmit = Marshal.GetDelegateForFunctionPointer<SubmitUi>(api.Submit);
        _uiStats = Marshal.GetDelegateForFunctionPointer<ReadUiStats>(api.Stats); _uiApi = api;
    }
    public UiGpuImage CreateUiImage(uint width, uint height, ReadOnlySpan<byte> rgba)
    {
        EnsureUi(); if (width == 0 || height == 0 || width > 4096 || height > 4096 || (ulong)width * height * 4 != (ulong)rgba.Length) throw new ArgumentException("UI RGBA8 image budget.");
        PluginError error = default; UiGpuKey key = default;
        fixed (byte* bytes = rgba) { UiImageDescription d = new() { Size = 24, Width = width, Height = height, Bytes = (uint)rgba.Length, Pixels = bytes };
            PluginModule.Check(Module.Id, "create_ui_image", _uiImage!(Module.Context, Handle, &d, &key, &error), error); }
        return new(this, key);
    }
    public UiGpuList CreateUiList(ReadOnlySpan<UiVertex> vertices, ReadOnlySpan<UiBatch> batches)
    {
        EnsureUi(); PluginError error = default; UiGpuKey key = default;
        if (vertices.Length is < 1 or > 65536 || batches.Length is < 1 or > 4096) throw new ArgumentException("UI display list budget.");
        fixed (UiVertex* v = vertices) fixed (UiBatch* b = batches) {
            UiListDescription d = new() { Size = 32, Vertices = (uint)vertices.Length, Batches = (uint)batches.Length, VertexData = v, BatchData = b };
            PluginModule.Check(Module.Id, "create_ui_list", _uiList!(Module.Context, Handle, &d, &key, &error), error);
        }
        return new(this, key);
    }
    internal void ReleaseUi(UiGpuKey key) { EnsureUi(); PluginError error = default; PluginModule.Check(Module.Id, "destroy_ui", _uiDestroy!(Module.Context, Handle, key, &error), error); }
    public void SubmitUi(UiGpuList list, ulong frame, Vector4 clear, bool overlay = false)
    {
        ArgumentNullException.ThrowIfNull(list); EnsureUi();
        if (list.Owner != this || list.Key.Value == 0) throw new ArgumentException("Foreign/released UI display list.");
        UiFrame f = new() { Size = 48, Frame = frame, Overlay = overlay ? 1u : 0u, List = list.Key, Clear = clear }; PluginError error = default;
        PluginModule.Check(Module.Id, "submit_ui", _uiSubmit!(Module.Context, Handle, &f, &error), error);
    }
    public UiRenderStats UiStats { get { EnsureUi(); UiRenderStats s = default; PluginError error = default;
        PluginModule.Check(Module.Id, "ui_stats", _uiStats!(Module.Context, Handle, &s, &error), error);
        if (s.Size != 64 || s.Generation != Handle || s.PureUi > 1) throw new ArgumentException("UI stats contract mismatch."); return s; } }
}

// Adjacent batches only. No sorting across overlap/clip/image changes. Resident lists are reused
// until document/widget/layout/resource changes. This is not the editor's ImGui draw list.
public sealed class UiDisplayListBuilder
{
    private readonly RendererSession _renderer; private readonly List<UiVertex> _vertices = []; private readonly List<UiBatch> _batches = [];
    private static readonly Vector2[] Corners = [new(0, 0), new(1, 0), new(0, 1), new(0, 1), new(1, 0), new(1, 1)];
    public UiDisplayListBuilder(RendererSession renderer) => _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    public void Quad(UiLayoutBox box, UiGpuImage image, UiColor color, float scale = 1, float radius = 0, Vector4? uvRegion = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Owner != _renderer || image.Key.Value == 0 || !float.IsFinite(scale) || scale <= 0 || !float.IsFinite(radius) || radius < 0) throw new ArgumentException("UI image/scale/radius.");
        if (box.Rect.Width <= 0 || box.Rect.Height <= 0 || box.Opacity <= 0 || box.Clip.Width <= 0 || box.Clip.Height <= 0) return;
        if (_vertices.Count + 6 > 65536) throw new ArgumentException("UI vertex budget.");
        Vector4 clip = new(box.Clip.X * scale, box.Clip.Y * scale, box.Clip.Width * scale, box.Clip.Height * scale);
        var batch = new UiBatch { FirstVertex = (uint)_vertices.Count, VertexCount = 6, Image = image.Key, Clip = clip };
        if (_batches.Count != 0 && _batches[^1].Image == batch.Image && _batches[^1].Clip == clip) {
            var previous = _batches[^1]; previous.VertexCount += 6; _batches[^1] = previous;
        } else { if (_batches.Count == 4096) throw new ArgumentException("UI batch budget."); _batches.Add(batch); }
        Vector2 size = new(box.Rect.Width, box.Rect.Height);
        Vector4 region = uvRegion ?? new(0, 0, 1, 1);
        foreach (Vector2 uv in Corners) {
            Vector2 p = uv * size;
            _vertices.Add(new() { Position = Vector2.Transform(p, box.Transform) * scale, UV = new(region.X + uv.X * region.Z, region.Y + uv.Y * region.W), Color = new(color.R, color.G, color.B, color.A * box.Opacity),
                Local = p * scale, Extent = size * scale, Radius = Math.Min(radius, Math.Min(size.X, size.Y) / 2) * scale });
        }
    }
    public UiGpuList Build() => _renderer.CreateUiList(CollectionsMarshal.AsSpan(_vertices), CollectionsMarshal.AsSpan(_batches));
    public int VertexCount => _vertices.Count;
    public int BatchCount => _batches.Count;
}
