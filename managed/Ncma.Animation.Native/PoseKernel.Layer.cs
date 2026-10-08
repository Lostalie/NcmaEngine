using System.Numerics;
using System.Runtime.InteropServices;
namespace Ncma.Animation.Native;

public enum PoseLayerMode { Override, Additive }
public readonly record struct PoseLayer(PoseRig Rig,int SourceA,int SourceB,int Reference,int MaskOffset,float Weight,PoseLayerMode Mode);
[StructLayout(LayoutKind.Sequential)] public struct PoseLayerStatistics {public uint Size,MaxRequests;public ulong LayerCalls,LayeredBones;}
public sealed unsafe partial class PoseKernel
{
    private LayerApi _layerApi;
    private void LoadLayerExtension()
    {
        if(sizeof(LayerApi)!=32||sizeof(LayerRequest)!=40||sizeof(PoseLayerStatistics)!=24)throw new PlatformNotSupportedException("Validated layer layouts required.");
        var get=(delegate* unmanaged[Cdecl]<uint,uint,void*,uint,Error*,uint>)NativeLibrary.GetExport(_library,"ncma_pose_get_layer_api");LayerApi api=default;Error error=default;
        Check(get(1,0,&api,32,&error),error);if(api.Size!=32||api.Major!=1||api.Minor!=0||api.MaxRequests!=32||api.Layer==null||api.Stats==null)throw new ArgumentException("Layer API rejected.");_layerApi=api;
    }
    public int Layer(ReadOnlySpan<PoseLayer> layers,ReadOnlySpan<PoseTrs> input,ReadOnlySpan<float> masks,Span<PoseTrs> local,Span<Matrix4x4> model)
    {
        Verify();if(_layerApi.Layer==null)throw new NotSupportedException("Explicit layerSupport=true required.");
        if(layers.Length is <1 or >32||input.Length is <1 or >65536||masks.Length is <1 or >32768)throw new ArgumentException("Layer batch/input budgets.");
        Span<LayerRequest> requests=stackalloc LayerRequest[32];int offset=0;
        for(int i=0;i<layers.Length;i++){var p=layers[i];CheckRig(p.Rig);int count=p.Rig.BoneCount;
            if(p.SourceA<0||p.SourceA>input.Length-count||p.SourceB<0||p.SourceB>input.Length-count||p.MaskOffset<0||p.MaskOffset>masks.Length-count||!float.IsFinite(p.Weight)||p.Weight is <0 or >1||!Enum.IsDefined(p.Mode)||
                (p.Mode==PoseLayerMode.Override?p.Reference!=0:p.Reference<0||p.Reference>input.Length-count))throw new ArgumentException("Exact layer ranges/mode/reference required.");
            requests[i]=new(){Rig=p.Rig.Key,A=(uint)p.SourceA,B=(uint)p.SourceB,Reference=(uint)p.Reference,Mask=(uint)p.MaskOffset,Output=(uint)offset,Mode=(uint)p.Mode,Weight=p.Weight};offset=checked(offset+count);
        }
        if(offset>32768||local.Length<offset||model.Length<offset)throw new ArgumentException("Layer output capacity.");
        var inputBytes=MemoryMarshal.AsBytes(input);var maskBytes=MemoryMarshal.AsBytes(masks);var localBytes=MemoryMarshal.AsBytes(local);var modelBytes=MemoryMarshal.AsBytes(model);
        if(inputBytes.Overlaps(maskBytes)||inputBytes.Overlaps(localBytes)||inputBytes.Overlaps(modelBytes)||maskBytes.Overlaps(localBytes)||maskBytes.Overlaps(modelBytes)||localBytes.Overlaps(modelBytes))throw new ArgumentException("Layer buffers cannot alias.");
        Error error=default;fixed(LayerRequest* r=requests)fixed(PoseTrs* p=input)fixed(float* w=masks)fixed(PoseTrs* l=local)fixed(Matrix4x4* m=model)
            Check(_layerApi.Layer(_context,r,(uint)layers.Length,p,(uint)input.Length,w,(uint)masks.Length,l,m,(uint)offset,&error),error);return offset;
    }
    public PoseLayerStatistics LayerStatistics {get{Verify();if(_layerApi.Stats==null)throw new NotSupportedException("Layer extension not negotiated.");PoseLayerStatistics s=default;Error error=default;Check(_layerApi.Stats(_context,&s,&error),error);if(s.Size!=24||s.MaxRequests!=32)throw new ArgumentException("Layer stats contract.");return s;}}
    [StructLayout(LayoutKind.Sequential)] private struct LayerRequest {public ulong Rig;public uint A,B,Reference,Mask,Output,Mode;public float Weight;public uint Reserved;}
    [StructLayout(LayoutKind.Sequential)] private struct LayerApi {
        public uint Size,Major,Minor,MaxRequests;
        public delegate* unmanaged[Cdecl]<ulong,LayerRequest*,uint,PoseTrs*,uint,float*,uint,PoseTrs*,Matrix4x4*,uint,Error*,uint> Layer;
        public delegate* unmanaged[Cdecl]<ulong,PoseLayerStatistics*,Error*,uint> Stats;
    }
}
