using Ncma.Animation;

internal static class BlendSpaceSequenceTests
{
    private static void Check(bool v){if(!v)throw new Exception("M6.6-C sequence weights assertion.");}
    private static void Reject(Action f){try{f();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){return;}throw new Exception("Invalid space scan accepted.");}
    private static AnimationProgram Compile(AnimationGraphDefinition d)=>AnimationProgram.Compile(d,1,AnimationGraphValidation.ClipIds(d).Select(id=>new AnimationClipDescriptor(id,d.SkeletonId,1,1)).ToArray());
    public static void Add(List<(string,Action)> cases)
    {
        cases.Add(("M6.6-C independent sequence coordinate scan agrees with prepared geometric oracle",()=>{
            var d=BlendSpaceRuntimeTests.Graph(true);var n=d.Nodes.Single(v=>v.BlendSpace is not null);var program=Compile(d);var geometric=new BlendSpaceProgram(n.BlendSpace!);
            var input=new AnimationSequenceCase(.1,3,[new(1,d.Parameters[0].Id,AnimationParameterKind.Float,.2),new(1,d.Parameters[1].Id,AnimationParameterKind.Float,.3),new(2,d.Parameters[0].Id,AnimationParameterKind.Float,.9),new(2,d.Parameters[1].Id,AnimationParameterKind.Float,.9)],[]);
            var result=AnimationGraphSequence.Run(program,input);Check(!result.ResourcesPrepared&&result.Timeline.Count==3);Check(result.Timeline[0].Spaces.Single().Weights==geometric.Evaluate(.2,.3)&&result.Timeline[1].Spaces.Single().Weights==geometric.Evaluate(.9,.9)&&result.Timeline[2].Spaces.Single().Weights==result.Timeline[1].Spaces.Single().Weights);
            Check(result.Timeline.All(t=>t.Spaces.Single().Step==t.Frame.Context.Tick&&t.Spaces.Single().NodeId==n.Id));Reject(()=>AnimationGraphSequence.Run(program,new(.1,1,[new(1,d.Parameters[0].Id,AnimationParameterKind.Float,double.NaN)],[])));
        }));
        cases.Add(("M6.6-C bounded maximum sixteen spaces x256 steps yields4096 isolated weight observations",()=>{
            var d=BlendSpaceRuntimeTests.Graph(true);var source=d.Nodes[0];var nodes=new List<AnimationGraphNode>();var links=new List<AnimationGraphLink>();Guid root=Guid.Empty;
            for(int i=0;i<16;i++){var node=source with{Id=Guid.NewGuid(),Name="Space "+i,BlendSpace=source.BlendSpace! with{Id=Guid.NewGuid(),Samples=source.BlendSpace.Samples.Select(s=>s with{Id=Guid.NewGuid()}).ToArray()}};nodes.Add(node);if(i==0)root=node.Id;else{var blend=AnimationGraphNode.Create(Guid.NewGuid(),"Blend "+i,AnimationNodeKind.Blend) with{Weight=.5};nodes.Add(blend);links.Add(new(Guid.NewGuid(),root,"pose",blend.Id,"a"));links.Add(new(Guid.NewGuid(),node.Id,"pose",blend.Id,"b"));root=blend.Id;}}
            nodes.Add(d.Nodes[1]);links.Add(new(Guid.NewGuid(),root,"pose",d.Nodes[1].Id,"pose"));d=d with{Nodes=nodes.ToArray(),Links=links.ToArray()};var result=AnimationGraphSequence.Run(Compile(d),new(.01,256,[],[]));Check(result.Timeline.Sum(t=>t.Spaces.Count)==4096&&result.Timeline.All(t=>t.Spaces.Select(s=>s.NodeId).Distinct().Count()==16&&t.Spaces.All(s=>Math.Abs(s.Weights.A.Weight+s.Weights.B.Weight+s.Weights.C.Weight-1)<1e-10)));
        }));
    }
}
