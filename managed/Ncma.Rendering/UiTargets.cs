using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;

namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal struct UiTargetApi { public uint Size,Version; public ulong Caps; public nint Create,Destroy,Submit,Capture,Acquire,Release,Stats; }
[StructLayout(LayoutKind.Sequential)] internal struct UiTargetDescription { public uint Size,Width,Height,Reserved; }
[StructLayout(LayoutKind.Sequential)] internal struct UiTargetFrame { public uint Size,Reserved; public ulong Frame,Content; public UiGpuKey Target,List; public Vector4 Clear; }
[StructLayout(LayoutKind.Sequential)] public struct UiTargetStats { public uint Size,Reserved; public ulong Generation,Targets,Leases,ResidentBytes,Productions,Presentations; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateUiTarget(ulong c,ulong r,UiTargetDescription* d,UiGpuKey* k,PluginError* e);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SubmitUiTarget(ulong c,ulong r,UiTargetFrame* f,PluginError* e);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CaptureUiTarget(ulong c,ulong r,UiGpuKey k,byte* b,uint n,PluginError* e);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint AcquireUiTarget(ulong c,ulong r,UiGpuKey k,ulong content,ulong frame,UiGpuKey* lease,PluginError* e);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadUiTargetStats(ulong c,ulong r,UiTargetStats* s,PluginError* e);

// Distinct types prevent passing a cached UI lease to the frozen scene Image contract.
[StructLayout(LayoutKind.Sequential)] public readonly record struct GuiCachedImageToken(ulong Value,ulong Generation);
public sealed class UiRenderTarget : IDisposable
{
    internal RendererSession Owner { get; }
    internal UiGpuKey Key { get; private set; }
    public uint Width { get; } public uint Height { get; }
    internal UiRenderTarget(RendererSession owner,UiGpuKey key,uint width,uint height) { Owner=owner;Key=key;Width=width;Height=height; }
    public void Dispose() { if(Key.Value==0)return;Owner.ReleaseUiTarget(Key,false);Key=default; }
}
public sealed class UiPresentationLease : IDisposable
{
    private readonly RendererSession _owner; private UiGpuKey _key;
    public GuiCachedImageToken Token { get { _=_owner.Handle;ObjectDisposedException.ThrowIf(_key.Value==0,this);return new(_key.Value,_key.Generation); } }
    public ulong Frame { get; }
    internal UiPresentationLease(RendererSession owner,UiGpuKey key,ulong frame) { _owner=owner;_key=key;Frame=frame; }
    public void Dispose() { if(_key.Value==0)return;_owner.ReleaseUiTarget(_key,true);_key=default; }
}
public sealed unsafe partial class RendererSession
{
    private UiTargetApi? _uiTargetApi;
    private CreateUiTarget? _createUiTarget; private DestroyUi? _destroyUiTarget,_releaseUiTarget;
    private SubmitUiTarget? _submitUiTarget; private CaptureUiTarget? _captureUiTarget;
    private AcquireUiTarget? _acquireUiTarget; private ReadUiTargetStats? _readUiTargetStats;
    private void EnsureUiTargets()
    {
        Verify();if(_uiTargetApi is not null)return;
        if(Module.AbiMinor<2)throw new NotSupportedException("UI targets require renderer query7.");
        var query=Module.ReadFunction<QueryUi>(144);UiTargetApi api=default;PluginError error=default;
        PluginModule.Check(Module.Id,"query_ui_targets",query(Module.Context,7,&api,72,&error),error);
        if(api.Size!=72||api.Version!=1||api.Caps!=7||api.Create==0||api.Destroy==0||api.Submit==0||api.Capture==0||api.Acquire==0||api.Release==0||api.Stats==0)throw new ArgumentException("UI target contract.");
        _createUiTarget=Marshal.GetDelegateForFunctionPointer<CreateUiTarget>(api.Create);_destroyUiTarget=Marshal.GetDelegateForFunctionPointer<DestroyUi>(api.Destroy);
        _submitUiTarget=Marshal.GetDelegateForFunctionPointer<SubmitUiTarget>(api.Submit);_captureUiTarget=Marshal.GetDelegateForFunctionPointer<CaptureUiTarget>(api.Capture);
        _acquireUiTarget=Marshal.GetDelegateForFunctionPointer<AcquireUiTarget>(api.Acquire);_releaseUiTarget=Marshal.GetDelegateForFunctionPointer<DestroyUi>(api.Release);
        _readUiTargetStats=Marshal.GetDelegateForFunctionPointer<ReadUiTargetStats>(api.Stats);_uiTargetApi=api;
    }
    public UiRenderTarget CreateUiTarget(uint width,uint height)
    {
        EnsureUiTargets();UiTargetDescription d=new(){Size=16,Width=width,Height=height};UiGpuKey k=default;PluginError error=default;
        PluginModule.Check(Module.Id,"create_ui_target",_createUiTarget!(Module.Context,Handle,&d,&k,&error),error);_uiKernelReady=true;return new(this,k,width,height);
    }
    internal void ReleaseUiTarget(UiGpuKey key,bool lease)
    {
        EnsureUiTargets();PluginError error=default;
        PluginModule.Check(Module.Id,lease?"release_ui_presentation":"destroy_ui_target",(lease?_releaseUiTarget!:_destroyUiTarget!)(Module.Context,Handle,key,&error),error);
    }
    private void RequireUiTarget(UiRenderTarget target) { ArgumentNullException.ThrowIfNull(target);EnsureUiTargets();if(target.Owner!=this||target.Key.Value==0)throw new ArgumentException("Foreign/released UI target."); }
    public void SubmitUiTarget(UiRenderTarget target,UiGpuList list,ulong frame,ulong contentRevision,Vector4 clear)
    {
        RequireUiTarget(target);ArgumentNullException.ThrowIfNull(list);if(list.Owner!=this||list.Key.Value==0)throw new ArgumentException("Foreign/released UI list.");
        UiTargetFrame f=new(){Size=72,Frame=frame,Content=contentRevision,Target=target.Key,List=list.Key,Clear=clear};PluginError error=default;
        PluginModule.Check(Module.Id,"submit_ui_target",_submitUiTarget!(Module.Context,Handle,&f,&error),error);
    }
    public UiPresentationLease AcquireUiPresentation(UiRenderTarget target,ulong contentRevision,ulong frame)
    {
        RequireUiTarget(target);UiGpuKey key=default;PluginError error=default;
        PluginModule.Check(Module.Id,"acquire_ui_presentation",_acquireUiTarget!(Module.Context,Handle,target.Key,contentRevision,frame,&key,&error),error);return new(this,key,frame);
    }
    public byte[] CaptureUiTarget(UiRenderTarget target)
    {
        RequireUiTarget(target);byte[] bytes=new byte[checked((int)(target.Width*target.Height*4))];PluginError error=default;
        fixed(byte* p=bytes)PluginModule.Check(Module.Id,"capture_ui_target",_captureUiTarget!(Module.Context,Handle,target.Key,p,(uint)bytes.Length,&error),error);return bytes;
    }
    public UiTargetStats UiTargetStats { get { EnsureUiTargets();UiTargetStats s=default;PluginError error=default;
        PluginModule.Check(Module.Id,"ui_target_stats",_readUiTargetStats!(Module.Context,Handle,&s,&error),error);
        if(s.Size!=56||s.Reserved!=0||s.Generation!=Handle)throw new ArgumentException("UI target stats contract.");return s; } }
}
