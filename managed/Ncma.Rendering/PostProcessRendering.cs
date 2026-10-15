using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;
[StructLayout(LayoutKind.Sequential)] internal struct PostProgramNative {public ShaderPairNative Pair;public uint Inputs,Reserved;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct PostDescriptionNative {public uint Size,Version,Width,Height,Programs,Targets,Passes,Output;public PostProgramNative* ProgramData;public PostTargetNative* TargetData;public PostPassNative* PassData;}
[StructLayout(LayoutKind.Sequential)] internal struct PostApiNative {public uint Size,Version;public ulong Caps;public nint Validate,Bind,Stats;}
[StructLayout(LayoutKind.Sequential)] public struct PostProcessStats {public uint Size,Version;public ulong Generation,Bindings,Targets,Programs,ResidentBytes,Publications,Draws,ConstantUploadBytes;}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint PostValidateNative(ulong c,ulong r,PostProgramNative* p,uint count,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint PostBindNative(ulong c,ulong r,GpuMeshKey scene,PostDescriptionNative* d,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint PostStatsNative(ulong c,ulong r,PostProcessStats* stats,PluginError* error);

public sealed class PreparedPostProcessPrograms
{
    internal RendererSession Owner{get;}public PostProcessPackage Package{get;}
    private readonly Func<bool> _allowed;
    private PreparedPostProcessPrograms(RendererSession owner,PostProcessPackage package,Func<bool> allowed){Owner=owner;Package=package;_allowed=allowed;}
    public static PreparedPostProcessPrograms Prepare(RendererSession renderer,PostProcessPackage package,Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(package);ArgumentNullException.ThrowIfNull(preparationAllowed);
        renderer.BeginToneOperation();try{var prepared=new PreparedPostProcessPrograms(renderer,package,preparationAllowed);prepared.Verify(renderer);renderer.ValidatePost(package);prepared.Verify(renderer);return prepared;}finally{renderer.EndToneOperation();}
    }
    internal void Verify(RendererSession renderer){if(!ReferenceEquals(renderer,Owner))throw new ArgumentException("Foreign post preparation.");renderer.VerifyShaderPreparation();if(!_allowed())throw new InvalidOperationException("Trusted off-simulation post preparation required.");renderer.VerifyShaderPreparation();}
}
public sealed unsafe partial class RendererSession
{
    private PostValidateNative? _postValidate;private PostBindNative? _postBind;private PostStatsNative? _postStats;
    private void EnsurePost()
    {
        Verify();if(_postBind is not null)return;
        if(sizeof(PostApiNative)!=40||sizeof(PostProgramNative)!=40||sizeof(PostTargetNative)!=16||sizeof(PostPassNative)!=32||sizeof(PostDescriptionNative)!=56||sizeof(PostProcessStats)!=72)throw new PlatformNotSupportedException("Post x64 contract.");
        PostApiNative api=default;PluginError e=default;PluginModule.Check(Module.Id,"query_post",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,16,&api,40,&e),e);
        if(api.Size!=40||api.Version!=1||api.Caps!=3||api.Validate==0||api.Bind==0||api.Stats==0)throw new InvalidOperationException("Post query16/API1.");
        _postValidate=Marshal.GetDelegateForFunctionPointer<PostValidateNative>(api.Validate);_postBind=Marshal.GetDelegateForFunctionPointer<PostBindNative>(api.Bind);_postStats=Marshal.GetDelegateForFunctionPointer<PostStatsNative>(api.Stats);
    }
    private sealed class PostPins : IDisposable
    {
        private readonly GCHandle[] _pins;public PostProgramNative[] Programs{get;}
        public PostPins(PostProcessPackage package){Programs=new PostProgramNative[package.Programs.Length];_pins=new GCHandle[Programs.Length*2];try{
            for(int i=0;i<Programs.Length;i++){var p=package.Programs[i];_pins[i*2]=GCHandle.Alloc(p.Vertex,GCHandleType.Pinned);_pins[i*2+1]=GCHandle.Alloc(p.Pixel,GCHandleType.Pinned);
                Programs[i]=new(){Pair=new(){Size=32,Version=1,VertexBytes=(uint)p.Vertex.Length,PixelBytes=(uint)p.Pixel.Length,Vertex=(byte*)_pins[i*2].AddrOfPinnedObject(),Pixel=(byte*)_pins[i*2+1].AddrOfPinnedObject()},Inputs=(uint)p.Metadata.Inputs};}
        }catch{Dispose();throw;}}
        public void Dispose(){foreach(var pin in _pins)if(pin.IsAllocated)pin.Free();}
    }
    internal void ValidatePost(PostProcessPackage package){EnsurePost();using var pins=new PostPins(package);PluginError e=default;fixed(PostProgramNative* p=pins.Programs)PluginModule.Check(Module.Id,"validate_post",_postValidate!(Module.Context,Handle,p,(uint)pins.Programs.Length,&e),e);}
    internal void BindPost(ScenePipelineSession scene,PostProcessPlan? plan,PreparedPostProcessPrograms? programs,Func<bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);BeginToneOperation();try{
            if(!allowed())throw new InvalidOperationException("Trusted post binding approval required.");VerifyShaderPreparation();EnsurePost();
            if(scene.Owner!=this||scene.Key.Value==0||(plan is null)!=(programs is null))throw new ArgumentException("Exact post scene/plan/program pair.");
            PluginError e=default;
            if(plan is null){PluginModule.Check(Module.Id,"disable_post",_postBind!(Module.Context,Handle,scene.Key,null,&e),e);return;}
            programs!.Verify(this);
            if(plan.ProgramHash!=programs.Package.ContentHash||plan.Width!=scene.Plan.Width||plan.Height!=scene.Plan.Height||scene.Operations.Count(p=>p.Operation==8)!=1)throw new ArgumentException("Post plan identity/size/single Tone.");
            using var pins=new PostPins(programs.Package);
            fixed(PostProgramNative* p=pins.Programs)fixed(PostTargetNative* t=plan.Targets)fixed(PostPassNative* f=plan.Passes){
                PostDescriptionNative d=new(){Size=56,Version=1,Width=plan.Width,Height=plan.Height,Programs=(uint)pins.Programs.Length,Targets=(uint)plan.Targets.Length,Passes=(uint)plan.Passes.Length,Output=plan.Output,ProgramData=p,TargetData=t,PassData=f};
                // Recheck after all managed preparation; callbacks cannot mutate this scene.
                if(!allowed())throw new InvalidOperationException("Post approval revoked.");programs.Verify(this);scene.VerifyPreparedCandidate();
                PluginModule.Check(Module.Id,"bind_post",_postBind!(Module.Context,Handle,scene.Key,&d,&e),e);
            }
        }finally{EndToneOperation();}
    }
    public PostProcessStats PostStats{get{EnsurePost();PostProcessStats s=default;PluginError e=default;PluginModule.Check(Module.Id,"post_stats",_postStats!(Module.Context,Handle,&s,&e),e);if(s.Size!=72||s.Version!=1||s.Generation!=Handle)throw new InvalidOperationException("Post stats contract.");return s;}}
}
