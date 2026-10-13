namespace Ncma.Rendering;

public sealed record BloomSettings(bool Enabled=true,int Levels=4,float Threshold=1,float SoftKnee=.5f,float Intensity=.08f,float Scatter=.7f)
{
    public void Validate(){if(Levels is <1 or >6||!float.IsFinite(Threshold)||Threshold is <0 or >16||!float.IsFinite(SoftKnee)||SoftKnee is <0 or >1||!float.IsFinite(Intensity)||Intensity is <0 or >4||!float.IsFinite(Scatter)||Scatter is <0 or >1)throw new ArgumentException("Bloom bounded settings.");}
}
public sealed class BloomProgramSet
{
    public ShaderCatalog Catalog{get;}private readonly PostProgramDefinition[] _programs;
    internal BloomProgramSet(ShaderCatalog catalog,PostProgramDefinition[] programs){Catalog=catalog;_programs=programs;}
    public PostProgramDefinition[] CopyPrograms()=>(PostProgramDefinition[])_programs.Clone();
}
// The official effect is just public shader declarations + the same public HDR graph as user effects.
public static class BloomFeature
{
    public static readonly Guid ThresholdProgram=Guid.Parse("7a14ee02-00b1-44b2-8844-000000000001");
    public static readonly Guid DownsampleProgram=Guid.Parse("7a14ee02-00b1-44b2-8844-000000000002");
    public static readonly Guid BlurProgram=Guid.Parse("7a14ee02-00b1-44b2-8844-000000000003");
    public static readonly Guid UpsampleProgram=Guid.Parse("7a14ee02-00b1-44b2-8844-000000000004");
    public static readonly Guid CombineProgram=Guid.Parse("7a14ee02-00b1-44b2-8844-000000000005");
    private const string Source="""
        cbuffer Post:register(b0){float4 Params;};
        Texture2D Input0:register(t0);Texture2D Input1:register(t1);SamplerState LinearClamp:register(s0);
        struct Full{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
        Full VSPost(uint id:SV_VertexID){Full o;o.uv=float2((id<<1)&2,id&2);o.p=float4(o.uv*float2(2,-2)+float2(-1,1),0,1);return o;}
        float3 Box(float2 uv){float2 d=Params.xy*.5;return (Input0.SampleLevel(LinearClamp,uv+float2(-d.x,-d.y),0).rgb+
            Input0.SampleLevel(LinearClamp,uv+float2(d.x,-d.y),0).rgb+Input0.SampleLevel(LinearClamp,uv+float2(-d.x,d.y),0).rgb+
            Input0.SampleLevel(LinearClamp,uv+d,0).rgb)*.25;}
        float4 Threshold(Full i):SV_TARGET{float3 c=max(Box(i.uv),0);float b=max(c.r,max(c.g,c.b));
            float soft=clamp(b-Params.z+Params.w,0,2*Params.w);soft=soft*soft/max(4*Params.w,1e-5);
            return float4(clamp(c*max(b-Params.z,soft)/max(b,1e-5),0,65504),1);}
        float4 Downsample(Full i):SV_TARGET{return float4(clamp(Box(i.uv),0,65504),1);}
        float4 Blur(Full i):SV_TARGET{float2 d=Params.xy;float3 c=Input0.SampleLevel(LinearClamp,i.uv,0).rgb*6;
            c+=(Input0.SampleLevel(LinearClamp,i.uv-d,0).rgb+Input0.SampleLevel(LinearClamp,i.uv+d,0).rgb)*4;
            c+=Input0.SampleLevel(LinearClamp,i.uv-2*d,0).rgb+Input0.SampleLevel(LinearClamp,i.uv+2*d,0).rgb;
            return float4(clamp(c/16,0,65504),1);}
        float4 Upsample(Full i):SV_TARGET{return float4(clamp(lerp(Input0.SampleLevel(LinearClamp,i.uv,0).rgb,Input1.SampleLevel(LinearClamp,i.uv,0).rgb,Params.x),0,65504),1);}
        float4 Combine(Full i):SV_TARGET{return float4(clamp(Input0.SampleLevel(LinearClamp,i.uv,0).rgb+Input1.SampleLevel(LinearClamp,i.uv,0).rgb*Params.x,0,65504),1);}
        """;
    public static BloomProgramSet CreatePrograms()
    {
        string hash=ShaderContractCodec.HashSource(Source);
        var v=ShaderDescriptor.Prepare(new(1,Guid.Parse("7a14ee02-00b1-44b2-8844-000000000000"),"HDR post fullscreen",ShaderProfile.HdrPostProcess,ShaderStage.Vertex,"VSPost",Source,hash,PostProgramContract.Inputs,[],[],[]));
        var stages=new[]{(ThresholdProgram,"Threshold",1),(DownsampleProgram,"Downsample",1),(BlurProgram,"Blur",1),(UpsampleProgram,"Upsample",2),(CombineProgram,"Combine",2)};
        var programs=stages.Select(s=>new PostProgramDefinition(s.Item3,v,ShaderDescriptor.Prepare(new(1,s.Item1,"Bloom "+s.Item2,ShaderProfile.HdrPostProcess,ShaderStage.Pixel,s.Item2,Source,hash,[],PostProgramContract.Constants,PostProgramContract.Resources(s.Item3),[])))).ToArray();
        return new(ShaderCatalog.Create(ShaderProfile.HdrPostProcess,new[]{v.CopyDefinition()}.Concat(programs.Select(p=>p.Pixel.CopyDefinition()))),programs);
    }
    public static PostProcessPlan? Build(uint width,uint height,BloomSettings settings,PostProcessPackage? programs)
    {
        ArgumentNullException.ThrowIfNull(settings);settings.Validate();if(width is 0 or >4096||height is 0 or >4096)throw new ArgumentException("Bloom dimensions.");
        if(!settings.Enabled||settings.Intensity==0)return null;ArgumentNullException.ThrowIfNull(programs);
        var rows=programs.CopyPage();foreach(var id in new[]{ThresholdProgram,DownsampleProgram,BlurProgram,UpsampleProgram,CombineProgram})if(!rows.Any(p=>p.Id==id))throw new ArgumentException("Bloom exact program closure.");
        var graph=new PostProcessGraph();var levels=new List<(PostResource Resource,uint Width,uint Height)>();PostResource previous=PostProcessGraph.SceneHdr;uint pw=width,ph=height;
        for(int i=0;i<settings.Levels;i++){
            uint w=Math.Max(1,(pw+1)/2),h=Math.Max(1,(ph+1)/2);var down=graph.AddTarget(new(w,h));var horizontal=graph.AddTarget(new(w,h));var vertical=graph.AddTarget(new(w,h));
            graph.AddPass(new(i==0?ThresholdProgram:DownsampleProgram,previous,null,down,new(1f/pw,1f/ph,settings.Threshold,settings.Threshold*settings.SoftKnee)));
            graph.AddPass(new(BlurProgram,down,null,horizontal,new(1f/w,0,0,0)));graph.AddPass(new(BlurProgram,horizontal,null,vertical,new(0,1f/h,0,0)));
            levels.Add((vertical,w,h));previous=vertical;pw=w;ph=h;if(w==1&&h==1)break;
        }
        for(int i=levels.Count-2;i>=0;i--){var level=levels[i];var up=graph.AddTarget(new(level.Width,level.Height));graph.AddPass(new(UpsampleProgram,level.Resource,previous,up,new(settings.Scatter,0,0,0)));previous=up;}
        var output=graph.AddTarget(new(width,height));graph.AddPass(new(CombineProgram,PostProcessGraph.SceneHdr,previous,output,new(settings.Intensity,0,0,0)));
        return graph.Compile(width,height,output,programs);
    }
}
