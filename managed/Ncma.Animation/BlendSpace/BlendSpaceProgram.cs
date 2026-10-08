namespace Ncma.Animation;

// Immutable off-frame triangulation. Evaluation never allocates/triangulates or owns a clock/World/native resource.
public sealed class BlendSpaceProgram
{
    public const int MaximumSamples=32;
    private const double Epsilon=1e-10;
    private readonly BlendSpaceDefinition _definition;
    private readonly Point[] _points;
    private readonly Triangle[] _triangles;
    private readonly Edge[] _hull;
    private readonly int[] _ordered;
    private readonly record struct Point(double X,double Y);
    private readonly record struct Triangle(int A,int B,int C);
    private readonly record struct Edge(int A,int B);
    public Guid Id=>_definition.Id;
    public int SampleCount=>_points.Length;
    public BlendSpaceDefinition CopyDefinition()=>_definition with{Samples=_definition.Samples.Select(s=>s with{}).ToArray()};
    public BlendSpaceTriangle[] CopyTriangles()=>_triangles.Select(t=>new BlendSpaceTriangle(Sample(t.A).Id,Sample(t.B).Id,Sample(t.C).Id)).ToArray();
    public BlendSpaceProgram(BlendSpaceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if(definition.Id==Guid.Empty||definition.Dimensions is not (1 or 2)||definition.AxisX is null||
            definition.Dimensions==1&&definition.AxisY is not null||definition.Dimensions==2&&definition.AxisY is null||
            definition.Samples is null||definition.Samples.Length<(definition.Dimensions==1?2:3)||definition.Samples.Length>MaximumSamples)
            throw new AnimationGraphValidationException("blendspace_shape",definition.Id,"Bounded typed BlendSpace required.");
        Axis(definition.AxisX);if(definition.AxisY is{} y){Axis(y);if(y.ParameterId==definition.AxisX.ParameterId)throw new AnimationGraphValidationException("blendspace_axes",definition.Id,"Distinct axis parameters required.");}
        if(definition.AxisX.ParameterId==definition.Id||definition.AxisY?.ParameterId==definition.Id)throw new AnimationGraphValidationException("blendspace_identity",definition.Id,"Space and axis references must be distinct.");
        AnimationGraphCodec.Scalar(definition.CycleSeconds,.001,600);
        _definition=definition with{Samples=definition.Samples.OrderBy(s=>s?.Id).Select(s=>s is null?throw new ArgumentException("Null BlendSpace sample."):s with{}).ToArray()};
        _points=new Point[definition.Samples.Length];var ids=new HashSet<Guid>{definition.Id,definition.AxisX.ParameterId};if(definition.AxisY is{} axisY)ids.Add(axisY.ParameterId);
        for(int i=0;i<_points.Length;i++) {
            var s=Sample(i);if(s.Id==Guid.Empty||!ids.Add(s.Id)||s.ClipId==Guid.Empty)throw new AnimationGraphValidationException("blendspace_identity",s.Id,"Distinct sample and exact clip UUID required.");
            AnimationGraphCodec.Scalar(s.X,definition.AxisX.Minimum,definition.AxisX.Maximum);
            if(definition.Dimensions==1){if(s.Y!=0)throw new AnimationGraphValidationException("blendspace_position",s.Id,"1D Y must be neutral zero.");}
            else AnimationGraphCodec.Scalar(s.Y,definition.AxisY!.Minimum,definition.AxisY.Maximum);
            _points[i]=new(Normalize(s.X,definition.AxisX),definition.AxisY is{} yy?Normalize(s.Y,yy):0);
            for(int j=0;j<i;j++)if(Distance(_points[i],_points[j])<=Epsilon*Epsilon)throw new AnimationGraphValidationException("blendspace_duplicate_position",s.Id,"Distinct normalized sample positions required.");
        }
        foreach(var s in _definition.Samples)if(ids.Contains(s.ClipId))throw new AnimationGraphValidationException("blendspace_identity",s.Id,"Clip UUID cannot alias a space/sample/axis identity.");
        _ordered=Enumerable.Range(0,_points.Length).OrderBy(i=>_points[i].X).ThenBy(i=>Sample(i).Id).ToArray();
        if(definition.Dimensions==1){_triangles=[];_hull=[];}else{_triangles=Triangulate();_hull=Edges(_triangles).Where(p=>p.Value==1).Select(p=>p.Key).OrderBy(e=>e.A).ThenBy(e=>e.B).ToArray();if(_triangles.Length==0||_hull.Length<3)throw new AnimationGraphValidationException("blendspace_degenerate",definition.Id,"Non-collinear 2D area required.");}
    }
    private static void Axis(BlendSpaceAxis a){if(a.ParameterId==Guid.Empty)throw new ArgumentException("Axis parameter UUID required.");AnimationGraphCodec.Text(a.Name);AnimationGraphCodec.Text(a.Unit);AnimationGraphCodec.Scalar(a.Minimum,-1000000,1000000);AnimationGraphCodec.Scalar(a.Maximum,-1000000,1000000);if(a.Maximum-a.Minimum<.000001)throw new ArgumentException("Nonzero bounded axis range required.");}
    private BlendSpaceSample Sample(int i)=>_definition.Samples[i];
    private static double Normalize(double v,BlendSpaceAxis a)=>(v-a.Minimum)/(a.Maximum-a.Minimum);
    private static double Distance(Point a,Point b)=>(a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y);
    private static double Area(Point a,Point b,Point c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    private static Edge Key(int a,int b)=>a<b?new(a,b):new(b,a);
    private static Dictionary<Edge,int> Edges(IEnumerable<Triangle> triangles){var edges=new Dictionary<Edge,int>();foreach(var t in triangles)foreach(var e in new[]{Key(t.A,t.B),Key(t.B,t.C),Key(t.C,t.A)})edges[e]=edges.GetValueOrDefault(e)+1;return edges;}
    private Triangle[] Triangulate()
    {
        var p=_points.Concat(new[]{new Point(-16,-16),new Point(16,-16),new Point(0,16)}).ToArray();int n=_points.Length;
        var triangles=new List<Triangle>{new(n,n+1,n+2)};
        for(int i=0;i<n;i++) {
            var bad=triangles.Where(t=>InsideCircle(p[t.A],p[t.B],p[t.C],p[i])).ToArray();
            if(bad.Length==0)throw new AnimationGraphValidationException("blendspace_triangulation",Sample(i).Id,"Numerically ambiguous insertion rejected.");
            var boundary=Edges(bad).Where(e=>e.Value==1).Select(e=>e.Key).OrderBy(e=>e.A).ThenBy(e=>e.B).ToArray();
            foreach(var t in bad)triangles.Remove(t);
            foreach(var edge in boundary){double area=Area(p[edge.A],p[edge.B],p[i]);if(Math.Abs(area)<=Epsilon)continue;triangles.Add(area>0?new(edge.A,edge.B,i):new(edge.B,edge.A,i));}
            if(triangles.Count>2*MaximumSamples+1)throw new ArgumentException("Triangulation topology budget.");
        }
        var result=triangles.Where(t=>t.A<n&&t.B<n&&t.C<n).Select(t=>Rotate(t)).OrderBy(t=>t.A).ThenBy(t=>t.B).ThenBy(t=>t.C).ToArray();
        if(result.Any(t=>Area(p[t.A],p[t.B],p[t.C])<=Epsilon)||result.SelectMany(t=>new[]{t.A,t.B,t.C}).Distinct().Count()!=n||Edges(result).Any(e=>e.Value>2))throw new AnimationGraphValidationException("blendspace_degenerate",Id,"Every sample needs a valid non-overlapping triangulation.");
        return result;
        static Triangle Rotate(Triangle t)=>t.A<t.B&&t.A<t.C?t:t.B<t.C?new(t.B,t.C,t.A):new(t.C,t.A,t.B);
    }
    private static bool InsideCircle(Point a,Point b,Point c,Point d)
    {
        double ax=a.X-d.X,ay=a.Y-d.Y,bx=b.X-d.X,by=b.Y-d.Y,cx=c.X-d.X,cy=c.Y-d.Y;
        double determinant=(ax*ax+ay*ay)*(bx*cy-by*cx)-(bx*bx+by*by)*(ax*cy-ay*cx)+(cx*cx+cy*cy)*(ax*by-ay*bx);
        // Fixed UUID insertion order and inclusive cocircular removal choose one reproducible diagonal.
        return determinant>=-Epsilon;
    }
    public BlendSpaceWeights Evaluate(double x,double y=0)
    {
        AnimationGraphCodec.Scalar(x,-1000000,1000000);AnimationGraphCodec.Scalar(y,-1000000,1000000);
        if(_definition.Dimensions==1&&y!=0)throw new ArgumentException("Neutral 1D Y required.");
        double cx=Math.Clamp(x,_definition.AxisX.Minimum,_definition.AxisX.Maximum),cy=_definition.AxisY is{} axis?Math.Clamp(y,axis.Minimum,axis.Maximum):0;bool projected=cx!=x||cy!=y;
        var p=new Point(Normalize(cx,_definition.AxisX),_definition.AxisY is{} yy?Normalize(cy,yy):0);
        if(_definition.Dimensions==1) {
            int lo=_ordered[0],hi=_ordered[^1];if(p.X<=_points[lo].X)return Result(lo,1,-1,0,-1,0,_points[lo],projected||p.X!=_points[lo].X);
            if(p.X>=_points[hi].X)return Result(hi,1,-1,0,-1,0,_points[hi],projected||p.X!=_points[hi].X);
            for(int k=1;k<_ordered.Length;k++){hi=_ordered[k];lo=_ordered[k-1];if(p.X<=_points[hi].X){double w=(p.X-_points[lo].X)/(_points[hi].X-_points[lo].X);return Result(lo,1-w,hi,w,-1,0,p,projected);}}
        } else {
            foreach(var t in _triangles){double area=Area(_points[t.A],_points[t.B],_points[t.C]);double a=Area(_points[t.B],_points[t.C],p)/area,b=Area(_points[t.C],_points[t.A],p)/area,c=1-a-b;if(a>=-Epsilon&&b>=-Epsilon&&c>=-Epsilon)return Result(t.A,a,t.B,b,t.C,c,p,projected);}
            double distance=double.PositiveInfinity;Edge nearest=default;Point projection=default;double weight=0;
            foreach(var edge in _hull){var a=_points[edge.A];var b=_points[edge.B];double w=Math.Clamp(((p.X-a.X)*(b.X-a.X)+(p.Y-a.Y)*(b.Y-a.Y))/Distance(a,b),0,1);var q=new Point(a.X+(b.X-a.X)*w,a.Y+(b.Y-a.Y)*w);double d=Distance(p,q);if(d<distance-Epsilon){distance=d;nearest=edge;projection=q;weight=w;}}
            return Result(nearest.A,1-weight,nearest.B,weight,-1,0,projection,true);
        }
        throw new InvalidOperationException("Prepared BlendSpace topology missing.");
    }
    private BlendSpaceWeights Result(int ia,double wa,int ib,double wb,int ic,double wc,Point position,bool projected)
    {
        wa=Math.Max(0,wa);wb=Math.Max(0,wb);wc=Math.Max(0,wc);double sum=wa+wb+wc;if(!double.IsFinite(sum)||sum<=0)throw new ArgumentException("Finite normalized blend weights required.");wa/=sum;wb/=sum;wc/=sum;
        Span<(int Id,double Weight)> values=stackalloc (int,double)[3];int count=0;if(wa>0)values[count++]=(ia,wa);if(wb>0)values[count++]=(ib,wb);if(wc>0)values[count++]=(ic,wc);
        for(int a=0;a<count;a++)for(int b=a+1;b<count;b++)if(values[b].Id<values[a].Id)(values[a],values[b])=(values[b],values[a]);
        int primary=0;for(int i=1;i<count;i++)if(values[i].Weight>values[primary].Weight+Epsilon)primary=i;
        // Stack storage cannot escape; copy only value contributions into the returned bounded record.
        var a0=new BlendSpaceContribution(Sample(values[0].Id).Id,Sample(values[0].Id).ClipId,values[0].Weight);
        var b0=count>1?new BlendSpaceContribution(Sample(values[1].Id).Id,Sample(values[1].Id).ClipId,values[1].Weight):default;
        var c0=count>2?new BlendSpaceContribution(Sample(values[2].Id).Id,Sample(values[2].Id).ClipId,values[2].Weight):default;
        var dominant=Sample(values[primary].Id);
        return new(count,a0,b0,c0,dominant.Id,dominant.ClipId,_definition.AxisX.Minimum+position.X*(_definition.AxisX.Maximum-_definition.AxisX.Minimum),_definition.AxisY is{} y?y.Minimum+position.Y*(y.Maximum-y.Minimum):0,projected);
    }
}
