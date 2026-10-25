using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Rendering.Scene.Examples;

internal static class SceneUiJointPlayerProbe
{
    internal static void Run(string requestPath)
    {
        using var e=new UiPublicationProductEvidence(requestPath);bool headless=e.Kind=="null";string[] native=[];int builds=0;
        bool high=ShadowJointPipelines.SelectedHigh(Ncma.Application.ProjectContext.Load(e.Project));
        foreach(int mode in new[]{0,1,2}){

            using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
            using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();
            var options=new PlayerOptions(e.Project,headless,headless?"null":"d3d11",8,1d/60,60,Path.Combine(e.Output,$"player-{mode}.json"),false,false);
            RenderPipeline Factory(float exposure,float ambient,bool shadows){builds++;return high?ShadowJointPipelines.Create(mode,exposure*.5f,ambient,shadows):mode==1?SceneCustomizationExamples.ReplaceGeometryStage(exposure*.5f,ambient,shadows):SceneCustomizationExamples.ReplacePipeline(exposure*.5f,ambient,shadows);}
            int before=builds;Func<float,float,bool,RenderPipeline>? factory=mode==0?null:Factory;
            var result=headless?playerRunOwner0.Run(options,pluginRoot:e.Plugins,visible:false,pipelineFactory:factory):playerRunOwner1.RunForVerification(options,e.Plugins,(pixels,w,h)=>{
                int at=checked((int)((20*w+20)*4));e.Pass(Math.Abs(pixels[at]-51)<=1&&Math.Abs(pixels[at+1]-102)<=1&&Math.Abs(pixels[at+2]-204)<=1,"HUD after Tone independent color");native=EnvironmentProductEvidence.NativePaths(e.Root);File.WriteAllBytes(Path.Combine(e.Output,$"player-{mode}.rgba"),pixels);
            },pipelineFactory:factory);
            e.Pass(result.ExitCode==0&&(headless?result.Tick==8:result.Tick>=8)&&result.ShutdownErrors.Length==0,"real joint Player fixed steps");e.Pass(result.ValidationErrors==0&&result.ValidationWarnings==0,"API0/0");
            if(headless)e.Pass(builds==before&&result.Modules.Length==0&&result.RenderedFrames==0,"Null no pipeline callback/native");else if(mode!=0)e.Pass(builds==before+3,"two preflight variants plus viewport custom graph");
        }
        foreach(string name in new[]{"bad-model","bad-environment","bad-ui","bad-post-module"}.Concat(high?(headless?new[]{"bad-shadow-quality"}:new[]{"bad-shadow-quality","bad-shadow-library"}):[])){

            using var playerRunOwner2 = new Ncma.Player.App.PlayerRunner();
            var options=new PlayerOptions(Path.Combine(Path.GetDirectoryName(e.Project)!,name+".ncmaproject"),headless,headless?"null":"d3d11",2,1d/60,30,Path.Combine(e.Output,name+".json"),false,false);
            var rejected=playerRunOwner2.Run(options,pluginRoot:e.Plugins,visible:false);
            Console.WriteLine($"JOINT CLOSURE headless={headless} case={name} exit={rejected.ExitCode} tick={rejected.Tick}");
            if(headless && name is "bad-model" or "bad-environment")
                e.Pass(rejected.ExitCode==0&&rejected.Tick==2&&rejected.RenderedFrames==0&&rejected.Modules.Length==0&&rejected.ShutdownErrors.Length==0,"Simulation does not consume presentation-only closure: "+name);
            else e.Pass(rejected.ExitCode!=0&&rejected.Tick==0&&rejected.SessionId==Guid.Empty&&rejected.RenderedFrames==0&&rejected.ShutdownErrors.Length==0,"joint closure rejected before gameplay: "+name);
            e.Pass(EnvironmentProductEvidence.NativePaths(e.Root).Length==0,"failed closure leaves no native: "+name);
        }
        if(high&&headless){

            using var playerRunOwner3 = new Ncma.Player.App.PlayerRunner();
            var options=new PlayerOptions(Path.Combine(Path.GetDirectoryName(e.Project)!,"bad-shadow-library.ncmaproject"),true,"null",2,1d/60,30,Path.Combine(e.Output,"ignored-shadow-library.json"),false,false);
            var ignored=playerRunOwner3.Run(options,pluginRoot:e.Plugins,visible:false);e.Pass(ignored.ExitCode==0&&ignored.Tick==2&&ignored.Modules.Length==0&&ignored.RenderedFrames==0,"Null never opens irrelevant GPU shadow dependency");
        }
        e.Finish(native);
    }
}
