using Ncma.Application;
using Ncma.Assets;
using Ncma.Rendering;
using Ncma.Rendering.Scene.Examples;
using Ncma.Scene;
using Ncma.Scene.Rendering;

// Trusted test startup customization, loaded only by strict product probes. No executable
// scene data or new product authority. Original no-selection examples remain unchanged.
internal static class ShadowJointPipelines
{
    internal static bool SelectedHigh(ProjectContext context)=>SceneShadowState.Read(SceneDocumentCodec.Decode(File.ReadAllBytes(context.StartupScenePath)))?.Quality==SceneShadowQuality.High;
    internal static RenderPipeline Create(int mode,float exposure,float ambient,bool shadows)=>mode==1?
        new Scene3DPipeline(exposure,ambient,2048,shadows,geometry:new ExampleAmbientStage(ambient)):
        new Complete(exposure,ambient,shadows);
    private sealed class Complete(float exposure,float ambient,bool shadows):RenderPipeline
    {
        public override RenderGraph Build(uint width,uint height)
        {
            var graph=new RenderGraph();RenderResourceId? shadow=shadows?graph.AddResource(new("Test High shadow",RenderRole.SceneShadow,RenderFormat.Depth32,RenderUsage.Sampled|RenderUsage.DepthTarget,2048,2048)):null;
            var hdr=graph.AddResource(new("Test HDR",RenderRole.SceneHdr,RenderFormat.Rgba16Float,RenderUsage.Sampled|RenderUsage.ColorTarget,width,height));
            var depth=graph.AddResource(new("Test depth",RenderRole.SceneDepth,RenderFormat.Depth32,RenderUsage.Sampled|RenderUsage.DepthTarget,width,height));
            var output=graph.AddResource(new("Test output",RenderRole.SceneOutput,RenderFormat.Rgba8,RenderUsage.ColorTarget,width,height,Imported:true));
            if(shadow is {} s)graph.AddPass("Test High shadow pass",RenderOperation.SceneShadow,[],[s],4);
            new ExampleAmbientStage(ambient).Add(graph,shadow,hdr,depth);
            graph.AddPass("Test one final Tone",RenderOperation.SceneTone,[hdr,depth],[output],4,parameters:[exposure]);graph.SetOutput(output);return graph;
        }
    }
}
