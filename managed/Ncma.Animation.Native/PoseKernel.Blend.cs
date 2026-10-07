using System.Numerics;
using System.Runtime.InteropServices;

namespace Ncma.Animation.Native;

public readonly record struct PoseBlend(PoseRig Rig, int SourceA, int SourceB, float Weight);
[StructLayout(LayoutKind.Sequential)] public struct PoseBlendStatistics { public uint Size, MaxRequests; public ulong BlendCalls, BlendedBones; }
public sealed unsafe partial class PoseKernel
{
    private BlendApi _blendApi;
    private void LoadBlendExtension()
    {
        if(sizeof(BlendApi)!=32||sizeof(BlendRequest)!=32||sizeof(PoseBlendStatistics)!=24)throw new PlatformNotSupportedException("Validated x64 pose blend layouts required.");
        var get=(delegate* unmanaged[Cdecl]<uint,uint,void*,uint,Error*,uint>)NativeLibrary.GetExport(_library,"ncma_pose_get_blend_api");
        BlendApi api=default;Error error=default;Check(get(1,0,&api,32,&error),error);
        if(api.Size!=32||api.Major!=1||api.Minor!=0||api.MaxRequests!=32||api.Blend==null||api.Stats==null)throw new ArgumentException("Pose blend API table rejected.");_blendApi=api;
    }
    public int Blend(ReadOnlySpan<PoseBlend> blends,ReadOnlySpan<PoseTrs> input,Span<PoseTrs> local,Span<Matrix4x4> model)
    {
        Verify();if(_blendApi.Blend==null)throw new NotSupportedException("Explicit blendSupport=true negotiation required.");
        if(blends.Length is <1 or >32||input.Length is <1 or >65536)throw new ArgumentException("Pose blend request/input budget.");
        Span<BlendRequest> requests=stackalloc BlendRequest[32];int offset=0;
        for(int i=0;i<blends.Length;i++){
            var b=blends[i];CheckRig(b.Rig);int bones=b.Rig.BoneCount;
            if(b.SourceA<0||b.SourceA>input.Length-bones||b.SourceB<0||b.SourceB>input.Length-bones||!float.IsFinite(b.Weight)||b.Weight is <0 or >1)throw new ArgumentException("Pose blend source/weight range.");
            requests[i]=new(){Rig=b.Rig.Key,A=(uint)b.SourceA,B=(uint)b.SourceB,Output=(uint)offset,Weight=b.Weight};offset=checked(offset+bones);
        }
        if(offset>32768||local.Length<offset||model.Length<offset)throw new ArgumentException("Pose blend output capacity.");
        if(MemoryMarshal.AsBytes(input).Overlaps(MemoryMarshal.AsBytes(local))||MemoryMarshal.AsBytes(input).Overlaps(MemoryMarshal.AsBytes(model))||
            MemoryMarshal.AsBytes(local).Overlaps(MemoryMarshal.AsBytes(model)))throw new ArgumentException("Pose blend buffers cannot alias.");
        Error error=default;fixed(BlendRequest* r=requests)fixed(PoseTrs* p=input)fixed(PoseTrs* l=local)fixed(Matrix4x4* m=model)
            Check(_blendApi.Blend(_context,r,(uint)blends.Length,p,(uint)input.Length,l,m,(uint)offset,&error),error);return offset;
    }
    public PoseBlendStatistics BlendStatistics {
        get { Verify();if(_blendApi.Stats==null)throw new NotSupportedException("Explicit blend extension required.");PoseBlendStatistics s=default;Error error=default;
            Check(_blendApi.Stats(_context,&s,&error),error);if(s.Size!=24||s.MaxRequests!=32)throw new ArgumentException("Pose blend statistics contract.");return s; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BlendRequest { public ulong Rig;public uint A,B,Output,Reserved0;public float Weight;public uint Reserved1; }
    [StructLayout(LayoutKind.Sequential)] private struct BlendApi {
        public uint Size,Major,Minor,MaxRequests;
        public delegate* unmanaged[Cdecl]<ulong,BlendRequest*,uint,PoseTrs*,uint,PoseTrs*,Matrix4x4*,uint,Error*,uint> Blend;
        public delegate* unmanaged[Cdecl]<ulong,PoseBlendStatistics*,Error*,uint> Stats;
    }
}
