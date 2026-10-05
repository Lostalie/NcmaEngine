using System.Diagnostics;
using System.Runtime.InteropServices;
using Ncma.Interop;
using Ncma.Platform;
namespace Ncma.Rendering;
[StructLayout(LayoutKind.Sequential)]
internal struct RendererDescription { public uint Size, Backend, Validation, Vsync; public ulong Platform, Window; public uint Width, Height, Reserved, Reserved2; }
[StructLayout(LayoutKind.Sequential)]
public unsafe struct RenderFrame
{
    public uint StructSize, PassCount; public ulong Frame;
    public fixed float Model[16]; public fixed float Viewport[4];
    public float Exposure, Metallic, Roughness, Reserved;
    public static RenderFrame Reference(ulong frame, uint width, uint height, float exposure = 1)
    {
        RenderFrame result = new() { StructSize = 112, Frame = frame, Exposure = exposure, Metallic = .35f, Roughness = .28f };
        for (int i = 0; i < 16; i++) result.Model[i] = i % 5 == 0 ? 1 : 0;
        result.Viewport[2] = width; result.Viewport[3] = height; return result;
    }
}
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RenderPass { public uint Operation, ShaderContract; public ulong Resources; public fixed float Color[4]; }
[StructLayout(LayoutKind.Sequential)]
public struct RendererStats { public uint Size, State, Width, Height; public ulong SubmittedFrames, Presents, LiveGroups, ValidationErrors, ValidationWarnings; public double SubmitMilliseconds, PresentMilliseconds, GpuMilliseconds; public uint GpuSampleValid, Reserved; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateRenderer(ulong module, RendererDescription* desc, ulong* handle, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateResources(ulong module, ulong renderer, ulong* handle, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint DestroyResources(ulong module, ulong renderer, ulong handle, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SubmitRenderer(ulong module, ulong renderer, RenderFrame* frame, RenderPass* passes, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint RendererOperation(ulong module, ulong renderer, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ResizeRenderer(ulong module, ulong renderer, uint width, uint height, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadStats(ulong module, ulong renderer, RendererStats* stats, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CaptureRenderer(ulong module, ulong renderer, byte* bytes, uint capacity, uint* required, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ConfigureReference(ulong module,ulong renderer,ulong resources,ReferenceSettings* settings,PluginError* error);
public sealed unsafe partial class RendererSession : IDisposable
{
    private readonly PluginLease _lease;
    private readonly PluginLease? _platformLease;
    private readonly CreateResources _createResources;
    private readonly DestroyResources _destroyResources;
    private readonly SubmitRenderer _submit;
    private readonly RendererOperation _present, _wait, _destroy;
    private readonly ResizeRenderer _resize;
    private readonly ReadStats _stats;
    private readonly CaptureRenderer _capture;
    private readonly ConfigureReference? _configureReference;
    private readonly HashSet<RenderResources> _groups = [];
    private readonly RenderPass[] _passes = new RenderPass[64];
    private ulong _handle;
    internal PluginModule Module => _lease.Module;
    internal ulong Handle { get { Verify(); return _handle; } }
    public ulong SubmitCalls { get; private set; }
    public ulong CopiedBytes { get; private set; }
    public double LastEncodeMilliseconds { get; private set; }
    public RenderCapabilities Capabilities { get; } = RenderCapabilities.ReferenceDx11;
    public RendererSession(PluginModule module, PlatformWindow window, uint width, uint height, bool validation = true, bool vsync = false, uint backend = 1)
    {
        if (module.Kind != ModuleKind.Renderer) throw new ArgumentException("Renderer module required.");
        _lease = module.AcquireLease();
        try {
            _platformLease = window.Module.AcquireLease();
            var create = module.ReadFunction<CreateRenderer>(56);
            if(module.AbiMinor>=1)_configureReference=module.ReadFunction<ConfigureReference>(136);
            _createResources = module.ReadFunction<CreateResources>(64); _destroyResources = module.ReadFunction<DestroyResources>(72);
            _submit = module.ReadFunction<SubmitRenderer>(80); _present = module.ReadFunction<RendererOperation>(88);
            _resize = module.ReadFunction<ResizeRenderer>(96); _stats = module.ReadFunction<ReadStats>(104);
            _wait = module.ReadFunction<RendererOperation>(112); _destroy = module.ReadFunction<RendererOperation>(120);
            _capture = module.ReadFunction<CaptureRenderer>(128);
            RendererDescription desc = new() { Size = 48, Backend = backend, Validation = validation ? 1u : 0, Vsync = vsync ? 1u : 0,
                Platform = window.Module.Context, Window = window.Handle, Width = width, Height = height };
            PluginError error = default; ulong handle = 0;
            PluginModule.Check(module.Id, "create_renderer", create(module.Context, &desc, &handle, &error), error);
            _handle = handle;
        } catch { _platformLease?.Dispose(); _lease.Dispose(); throw; }
    }
    private void Verify() { _ = Module; ObjectDisposedException.ThrowIf(_handle == 0, this); }
    public void ConfigureReference(RenderResources resources,ReferenceSettings settings)
    {
        Verify();
        if(resources.Owner!=this || !resources.IsAlive || !_groups.Contains(resources))throw new ArgumentException("Foreign/released render resources.");
        if(_configureReference is null) {
            if(settings.Equals(ReferenceSettings.From(RenderConfiguration.Default(Guid.Empty))))return;
            throw new NotSupportedException("Reference lighting controls require Renderer ABI 1.1.");
        }
        PluginError error=default;
        PluginModule.Check(Module.Id,"configure_reference",_configureReference(Module.Context,_handle,resources.Handle,&settings,&error),error);
        CopiedBytes+=76;
    }
    public RendererStats Stats
    {
        get { Verify(); PluginError error = default; RendererStats stats = default;
            PluginModule.Check(Module.Id, "renderer_stats", _stats(Module.Context, _handle, &stats, &error), error);
            if (stats.Size != 88 || stats.State is < 1 or > 2) throw new InvalidOperationException("Invalid renderer stats.");
            return stats; }
    }
    public RenderResources CreateReferenceResources()
    {
        Verify(); PluginError error = default; ulong handle = 0;
        PluginModule.Check(Module.Id, "create_render_resources", _createResources(Module.Context, _handle, &handle, &error), error);
        var resources = new RenderResources(this, handle); _groups.Add(resources); return resources;
    }
    internal void Release(RenderResources resources)
    {
        Verify(); PluginError error = default;
        PluginModule.Check(Module.Id, "destroy_render_resources", _destroyResources(Module.Context, _handle, resources.Handle, &error), error);
        _groups.Remove(resources);
    }
    public void Submit(CompiledRenderGraph graph, RenderResources? resources, RenderFrame frame)
    {
        if (graph.RequiresResourceService) throw new ArgumentException("Typed resources require ResourcePipelineSession, not the v1 reference ABI.");
        Verify(); ArgumentNullException.ThrowIfNull(graph);
        if (graph.RequiresReferenceResources && (resources is null || resources.Owner != this || !resources.IsAlive))
            throw new ArgumentException("Valid renderer-owned resources required.");
        if (graph.Capabilities != Capabilities) throw new ArgumentException("Graph compiled for another capability set.");
        if(frame.Viewport[2] != graph.Width || frame.Viewport[3] != graph.Height) throw new ArgumentException("Graph viewport dimensions mismatch.");
        long started = Stopwatch.GetTimestamp();
        graph.Encode(_passes, resources?.Handle ?? 0); frame.StructSize = 112; frame.PassCount = (uint)graph.PassCount;
        LastEncodeMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        PluginError error = default;
        fixed (RenderPass* passes = _passes) PluginModule.Check(Module.Id, "submit_render", _submit(Module.Context, _handle, &frame, passes, &error), error);
        SubmitCalls++; CopiedBytes += 112 + frame.PassCount * 32;
    }
    public void Present() { Verify(); PluginError error = default; PluginModule.Check(Module.Id, "present", _present(Module.Context, _handle, &error), error); }
    public void WaitIdle() { Verify(); PluginError error = default; PluginModule.Check(Module.Id, "wait_idle", _wait(Module.Context, _handle, &error), error); }
    public void Resize(uint width, uint height) { Verify(); PluginError error = default; PluginModule.Check(Module.Id, "resize", _resize(Module.Context, _handle, width, height, &error), error); }
    public void Capture(Span<byte> output)
    {
        Verify(); PluginError error = default; uint count = 0;
        fixed (byte* data = output) PluginModule.Check(Module.Id, "capture", _capture(Module.Context, _handle, data, (uint)output.Length, &count, &error), error);
        if (count > output.Length) throw new InvalidOperationException("Invalid capture response.");
    }
    public void Dispose()
    {
        if (_handle == 0) return; Verify();
        if (_groups.Count != 0 || _gpuMeshes.Count != 0 || _resources.Count != 0) throw new InvalidOperationException("Render resource leases remain.");
        PluginError error = default; PluginModule.Check(Module.Id, "destroy_renderer", _destroy(Module.Context, _handle, &error), error);
        _handle = 0; _platformLease?.Dispose(); _lease.Dispose();
    }
}
public sealed class RenderResources : IDisposable
{
    internal RendererSession Owner { get; }
    internal ulong Handle { get; private set; }
    internal bool IsAlive => Handle != 0;
    internal RenderResources(RendererSession owner, ulong handle) { Owner = owner; Handle = handle; }
    public void Dispose() { if (Handle == 0) return; Owner.Release(this); Handle = 0; }
}
