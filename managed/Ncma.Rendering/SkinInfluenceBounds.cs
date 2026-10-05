using System.Numerics;
using Ncma.Assets;

namespace Ncma.Rendering;
using Vector3=System.Numerics.Vector3;

// Off-frame source summaries. Nonnegative normalized skin weights keep every posed vertex
// inside the union of its transformed influence bounds. This is bounds picking, not surface picking.
public sealed class SkinInfluenceBounds
{
    private readonly MeshBounds?[] _bounds;
    public SkinInfluenceBounds(MeshPayload mesh)
    {
        _=ModelPayloadCodec.Encode(mesh);if(!mesh.Skinned)throw new ArgumentException("Skinned mesh required.");
        _bounds=new MeshBounds?[mesh.Bindings.Length];
        foreach(var v in mesh.Vertices){Add(v.Joints.X,v.Weights.X,v.Position);Add(v.Joints.Y,v.Weights.Y,v.Position);Add(v.Joints.Z,v.Weights.Z,v.Position);Add(v.Joints.W,v.Weights.W,v.Position);}
        void Add(int joint,float weight,Vector3 point){if(weight<=0)return;var prior=_bounds[joint];_bounds[joint]=prior is { } b?new(Vector3.Min(b.Min,point),Vector3.Max(b.Max,point)):new(point,point);}
    }
    public MeshBounds Evaluate(ReadOnlySpan<Matrix4x4> palette,Matrix4x4 model)
    {
        if(palette.Length!=_bounds.Length)throw new ArgumentException("Influence palette size.");
        Vector3 min=new(float.PositiveInfinity),max=new(float.NegativeInfinity);
        for(int joint=0;joint<_bounds.Length;joint++)if(_bounds[joint] is { } b) {
            var matrix=palette[joint]*model;
            for(int corner=0;corner<8;corner++){var point=Vector3.Transform(new Vector3((corner&1)==0?b.Min.X:b.Max.X,(corner&2)==0?b.Min.Y:b.Max.Y,(corner&4)==0?b.Min.Z:b.Max.Z),matrix);
                if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z))throw new ArgumentException("Posed bounds overflow.");min=Vector3.Min(min,point);max=Vector3.Max(max,point);}
        }
        return new(min,max);
    }
}
