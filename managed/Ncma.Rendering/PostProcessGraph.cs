using System.Numerics;
using System.Runtime.InteropServices;
namespace Ncma.Rendering;

public enum PostColorSpace { LinearHdr=1, Display=2 }
public enum PostFormat { Rgba16Float=1, Rgba8=2 }
public readonly record struct PostResource(int Value);
public sealed record PostTarget(uint Width,uint Height,PostFormat Format=PostFormat.Rgba16Float,PostColorSpace ColorSpace=PostColorSpace.LinearHdr);
public sealed record PostPass(Guid Program,PostResource Input,PostResource? Second,PostResource Output,Vector4 Parameters);
public sealed record PostProgramMetadata(Guid Id,int Inputs,string VertexAuthorHash,string PixelAuthorHash,string VertexBytecodeHash,string PixelBytecodeHash);
[StructLayout(LayoutKind.Sequential)] internal struct PostTargetNative {public uint Width,Height,Format,ColorSpace;}
[StructLayout(LayoutKind.Sequential)] internal struct PostPassNative {public uint Program,Input0,Input1,Output;public Vector4 Parameters;}

// Bounded off-frame builder. Resource0 is the borrowed linear scene HDR, never an imported display image.
public sealed class PostProcessGraph
{
    public static PostResource SceneHdr=>new(0);
    private readonly List<PostTarget> _targets=[];
    private readonly List<PostPass> _passes=[];
    public PostResource AddTarget(PostTarget target){ArgumentNullException.ThrowIfNull(target);if(_targets.Count==32)throw new ArgumentException("Post target budget.");_targets.Add(target);return new(_targets.Count);}
    public void AddPass(PostPass pass){ArgumentNullException.ThrowIfNull(pass);if(_passes.Count==32)throw new ArgumentException("Post pass budget.");_passes.Add(pass);}
    public PostProcessPlan Compile(uint width,uint height,PostResource output,PostProcessPackage programs)
    {
        ArgumentNullException.ThrowIfNull(programs);if(width is 0 or >4096||height is 0 or >4096||_targets.Count==0||_passes.Count==0)throw new ArgumentException("Post size/budget.");
        int n=_targets.Count;bool Valid(PostResource r)=>r.Value>=0&&r.Value<=n;
        if(!Valid(output)||output.Value==0)throw new ArgumentException("Post output.");
        ulong bytes=16;foreach(var t in _targets){if(t.Width is 0 or >4096||t.Height is 0 or >4096||t.Format!=PostFormat.Rgba16Float||t.ColorSpace!=PostColorSpace.LinearHdr)throw new ArgumentException("Post linear HDR target required.");bytes+=(ulong)t.Width*t.Height*8;}
        if(bytes>128ul*1024*1024||_targets[output.Value-1].Width!=width||_targets[output.Value-1].Height!=height)throw new ArgumentException("Post output dimensions/byte budget.");
        var writer=Enumerable.Repeat(-1,n+1).ToArray();var programRows=programs.CopyPage();
        foreach(var (pass,i) in _passes.Select((p,i)=>(p,i))){
            if(!Valid(pass.Input)||!Valid(pass.Output)||pass.Output.Value==0||pass.Input==pass.Output||pass.Second is {} second&&(!Valid(second)||second==pass.Output)||writer[pass.Output.Value]>=0)throw new ArgumentException("Post resource/alias/duplicate writer.");
            if(!float.IsFinite(pass.Parameters.X)||!float.IsFinite(pass.Parameters.Y)||!float.IsFinite(pass.Parameters.Z)||!float.IsFinite(pass.Parameters.W)||Vector4.Abs(pass.Parameters).X>65504||Vector4.Abs(pass.Parameters).Y>65504||Vector4.Abs(pass.Parameters).Z>65504||Vector4.Abs(pass.Parameters).W>65504)throw new ArgumentException("Post finite parameters.");
            var p=programRows.SingleOrDefault(p=>p.Id==pass.Program);if(p is null||p.Inputs!=(pass.Second is null?1:2))throw new ArgumentException("Post exact program/input arity.");writer[pass.Output.Value]=i;
        }
        var state=new byte[_passes.Count];var ordered=new List<PostPass>();
        void VisitResource(PostResource r){if(r.Value==0)return;int i=writer[r.Value];if(i<0)throw new ArgumentException("Post uninitialized input.");if(state[i]==1)throw new ArgumentException("Post dependency cycle.");if(state[i]==2)return;state[i]=1;var p=_passes[i];VisitResource(p.Input);if(p.Second is {} second)VisitResource(second);state[i]=2;ordered.Add(p);}
        VisitResource(output);if(ordered.Count!=_passes.Count||writer.Skip(1).Any(i=>i<0))throw new ArgumentException("Post unused target/pass.");
        var nativeTargets=_targets.Select(t=>new PostTargetNative{Width=t.Width,Height=t.Height,Format=(uint)t.Format,ColorSpace=(uint)t.ColorSpace}).ToArray();
        var nativePasses=ordered.Select(p=>new PostPassNative{Program=(uint)Array.FindIndex(programRows,r=>r.Id==p.Program),Input0=(uint)p.Input.Value,Input1=p.Second is {} s?(uint)s.Value:uint.MaxValue,Output=(uint)p.Output.Value,Parameters=p.Parameters}).ToArray();
        return new(width,height,(uint)output.Value,bytes,programs.ContentHash,nativeTargets,nativePasses,ordered.ToArray());
    }
}
public sealed class PostProcessPlan
{
    public uint Width{get;}public uint Height{get;}public ulong ResidentBytes{get;}public string ProgramHash{get;}public int PassCount=>Passes.Length;
    internal uint Output{get;}internal PostTargetNative[] Targets{get;}internal PostPassNative[] Passes{get;}
    private readonly PostPass[] _metadata;
    internal PostProcessPlan(uint width,uint height,uint output,ulong bytes,string hash,PostTargetNative[] targets,PostPassNative[] passes,PostPass[] metadata)
    {Width=width;Height=height;Output=output;ResidentBytes=bytes;ProgramHash=hash;Targets=targets;Passes=passes;_metadata=metadata;}
    public PostPass[] CopyPasses()=>(PostPass[])_metadata.Clone();
    public PostTarget[] CopyTargets()=>Targets.Select(t=>new PostTarget(t.Width,t.Height,(PostFormat)t.Format,(PostColorSpace)t.ColorSpace)).ToArray();
}
