using System.Diagnostics;
using System.Text.Json;
using Ncma.Animation;

internal static class BlendSpaceTests
{
    private static void Check(bool v,string message=""){if(!v)throw new Exception("BlendSpace assertion: "+message);}
    private static void Reject(Action f){try{f();}catch(ArgumentException){return;}throw new Exception("Invalid BlendSpace accepted.");}
    internal static BlendSpaceDefinition Definition(params (double X,double Y)[] positions)=>new(Guid.NewGuid(),2,new(Guid.NewGuid(),"Speed","m/s",0,1),new(Guid.NewGuid(),"Turn","degree/s",0,1),1,Guid.Empty,
        positions.Select((p,i)=>new BlendSpaceSample(Guid.Parse($"00000000-0000-4000-8000-{i+1:000000000000}"),Guid.NewGuid(),p.X,p.Y)).ToArray());
    private static double Weight(BlendSpaceWeights w,Guid id)=>new[]{w.A,w.B,w.C}.Where(v=>v.SampleId==id).Sum(v=>v.Weight);
    private static void Valid(BlendSpaceWeights w){Check(w.Count is >=1 and <=3);var rows=new[]{w.A,w.B,w.C}.Take(w.Count).ToArray();Check(rows.All(r=>r.Weight>0&&double.IsFinite(r.Weight))&&Math.Abs(rows.Sum(r=>r.Weight)-1)<1e-10&&rows.Select(r=>r.SampleId).Distinct().Count()==w.Count);Check(rows.Any(r=>r.SampleId==w.PrimarySample&&r.ClipId==w.PrimaryClip));}
    public static void Add(List<(string,Action)> cases,string repository)
    {
        cases.Add(("M6.6-A 1D exact samples/segments/domain clamps and deterministic primary",()=>{
            var d=Definition((0,0),(.25,0),(1,0)) with{Dimensions=1,AxisY=null};var p=new BlendSpaceProgram(d);
            foreach(var sample in d.Samples){var w=p.Evaluate(sample.X);Valid(w);Check(w.Count==1&&Weight(w,sample.Id)==1&&!w.Projected);}
            var middle=p.Evaluate(.625);Valid(middle);Check(middle.Count==2&&middle.PrimarySample==d.Samples[1].Id&&Weight(middle,d.Samples[1].Id)==.5&&Weight(middle,d.Samples[2].Id)==.5);
            Check(p.Evaluate(-2).PrimarySample==d.Samples[0].Id&&p.Evaluate(2).PrimarySample==d.Samples[^1].Id&&p.Evaluate(-2).Projected);Reject(()=>p.Evaluate(.5,1));
        }));
        cases.Add(("M6.6-A 2D triangle barycentric vertices/edges/interior and convex projection",()=>{
            var d=Definition((0,0),(1,0),(0,1));var p=new BlendSpaceProgram(d);Check(p.CopyTriangles().Length==1);
            foreach(var s in d.Samples){var w=p.Evaluate(s.X,s.Y);Valid(w);Check(w.Count==1&&w.PrimarySample==s.Id);}
            var interior=p.Evaluate(.2,.3);Valid(interior);Check(Math.Abs(Weight(interior,d.Samples[0].Id)-.5)<1e-10&&Math.Abs(Weight(interior,d.Samples[1].Id)-.2)<1e-10&&Math.Abs(Weight(interior,d.Samples[2].Id)-.3)<1e-10);
            var edge=p.Evaluate(.5,.5);Valid(edge);Check(edge.Count==2&&edge.PrimarySample==d.Samples[1].Id&&!edge.Projected);
            var outside=p.Evaluate(.9,.9);Valid(outside);Check(outside.Projected&&Math.Abs(outside.X-.5)<1e-10&&Math.Abs(outside.Y-.5)<1e-10);Check(p.Evaluate(-1,-1).PrimarySample==d.Samples[0].Id);
        }));
        cases.Add(("M6.6-A cocircular topology/UUID permutation and exact square coverage",()=>{
            var d=Definition((0,0),(1,0),(1,1),(0,1));var p=new BlendSpaceProgram(d);var reverse=new BlendSpaceProgram(d with{Samples=d.Samples.Reverse().ToArray()});
            Check(p.CopyTriangles().Length==2&&p.CopyTriangles().SequenceEqual(reverse.CopyTriangles()));
            for(int x=0;x<=20;x++)for(int y=0;y<=20;y++){var w=p.Evaluate(x/20d,y/20d);Valid(w);Check(!w.Projected&&w==reverse.Evaluate(x/20d,y/20d));Reconstruct(w,d,x/20d,y/20d);}
        }));
        cases.Add(("M6.6-A interior points/collinear hull points and bounded random Delaunay affine oracle",()=>{
            var random=new Random(6601);for(int set=0;set<24;set++){
                var positions=new List<(double,double)>{(0,0),(1,0),(1,1),(0,1),(.5,0),(.5,1)};while(positions.Count<32)positions.Add((.05+random.NextDouble()*.9,.05+random.NextDouble()*.9));
                var d=Definition(positions.ToArray());var p=new BlendSpaceProgram(d);Check(p.CopyTriangles().Length<=2*BlendSpaceProgram.MaximumSamples-5);
                foreach(var s in d.Samples){var w=p.Evaluate(s.X,s.Y);Valid(w);Check(Weight(w,s.Id)>1-1e-8,"All samples represented");}
                for(int i=0;i<200;i++){double x=random.NextDouble(),y=random.NextDouble();var w=p.Evaluate(x,y);Valid(w);Check(!w.Projected,"Convex square coverage");Reconstruct(w,d,x,y);}
            }
        }));
        cases.Add(("M6.6-A axes normalized distance metric/outside tie and copied immutable topology",()=>{
            var d=Definition((0,0),(1,0),(0,1));d=d with{AxisX=d.AxisX with{Minimum=-100,Maximum=100},AxisY=d.AxisY! with{Minimum=-1000,Maximum=1000},Samples=d.Samples.Select(s=>s with{X=-100+200*s.X,Y=-1000+2000*s.Y}).ToArray()};
            var p=new BlendSpaceProgram(d);var w=p.Evaluate(80,800);Valid(w);Check(w.Projected&&Math.Abs(w.X)<1e-8&&Math.Abs(w.Y)<1e-8);
            var old=p.Evaluate(-60,-400);var copy=p.CopyDefinition();copy.Samples[0]=copy.Samples[0] with{X=99};d.Samples[0]=d.Samples[0] with{Y=999};var topology=p.CopyTriangles();topology[0]=default;Check(p.Evaluate(-60,-400)==old&&p.CopyTriangles()[0]!=default);
        }));
        cases.Add(("M6.6-A duplicate/null/UUID/axis/finite/sample budgets and degeneracy fail closed",()=>{
            var d=Definition((0,0),(1,0),(0,1));
            foreach(var bad in new[]{d with{Id=Guid.Empty},d with{Dimensions=3},d with{AxisX=null!},d with{AxisY=null},d with{Samples=null!},d with{Samples=[d.Samples[0],d.Samples[1]]},d with{Samples=[d.Samples[0],d.Samples[1],null!]},d with{Samples=[d.Samples[0],d.Samples[1],d.Samples[2] with{Id=d.Samples[0].Id}]},d with{CycleSeconds=0},d with{CycleSeconds=double.PositiveInfinity},d with{AxisY=d.AxisY! with{ParameterId=d.AxisX.ParameterId}},d with{AxisX=d.AxisX with{Maximum=0}},d with{AxisX=d.AxisX with{Unit="m/s\n"}},d with{Samples=d.Samples.Select(s=>s with{Y=0}).ToArray()},d with{Samples=d.Samples.Select(s=>s with{X=double.NaN}).ToArray()},d with{Samples=d.Samples.Concat(Enumerable.Range(0,30).Select(i=>new BlendSpaceSample(Guid.NewGuid(),Guid.NewGuid(),.1+i*.01,.1))).ToArray()}})Reject(()=>new BlendSpaceProgram(bad));
            Reject(()=>new BlendSpaceProgram(d with{Samples=[d.Samples[0],d.Samples[1],d.Samples[2] with{X=1,Y=0}]}));var p=new BlendSpaceProgram(d);Reject(()=>p.Evaluate(double.NaN,0));Reject(()=>p.Evaluate(0,double.PositiveInfinity));Reject(()=>p.Evaluate(1000001,0));
        }));
        cases.Add(("M6.6-A prepared 32-point weights warm zero allocation and bounded cost evidence",()=>{
            var random=new Random(66);var positions=new List<(double,double)>{(0,0),(1,0),(1,1),(0,1)};for(int i=4;i<32;i++)positions.Add((random.NextDouble(),random.NextDouble()));
            var p=new BlendSpaceProgram(Definition(positions.ToArray()));double sink=0;for(int i=0;i<4096;i++)sink+=p.Evaluate((i%97)/97d,(i%83)/83d).A.Weight;
            long before=GC.GetAllocatedBytesForCurrentThread();var time=Stopwatch.GetTimestamp();for(int i=0;i<16384;i++)sink+=p.Evaluate((i%97)/97d,(i%83)/83d).A.Weight;double ms=Stopwatch.GetElapsedTime(time).TotalMilliseconds;long allocation=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(allocation==0&&sink>0);string directory=Path.Combine(repository,"out/verification/m6-6");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"weights-"+(typeof(Program).Assembly.Location.Contains("Release")?"Release":"Debug")+".json"),JsonSerializer.Serialize(new{schema=1,samples=32,triangles=p.CopyTriangles().Length,queries=16384,allocation,milliseconds=ms,resourcePrepared=false,clockOwned=false,gpuExecuted=false,performanceAccepted=false}));
        }));
    }
    private static void Reconstruct(BlendSpaceWeights w,BlendSpaceDefinition d,double x,double y){double sx=0,sy=0;foreach(var c in new[]{w.A,w.B,w.C}.Take(w.Count)){var s=d.Samples.Single(s=>s.Id==c.SampleId);sx+=s.X*c.Weight;sy+=s.Y*c.Weight;}Check(Math.Abs(sx-x)<1e-8&&Math.Abs(sy-y)<1e-8,"Independent affine reconstruction");}
}
