using System.Text.Json;
using Ncma.Application;
using Ncma.Player.App;
using Ncma.Rendering.Scene.Examples;

internal static class SceneUiHostProbe
{
    internal static void Run(string requestPath)
    {
        using var request=JsonDocument.Parse(File.ReadAllBytes(requestPath));var r=request.RootElement;
        string project=r.GetProperty("project").GetString()!,output=r.GetProperty("output").GetString()!,plugins=r.GetProperty("plugins").GetString()!,root=r.GetProperty("productRoot").GetString()!;
        bool headless=r.GetProperty("kind").GetString()=="null",relocated=r.GetProperty("relocated").GetBoolean();int checks=0;
        void Pass(bool ok,string name){if(!ok)throw new Exception("HUD host "+name);checks++;}
        if(relocated)DeploymentManifest.Validate(root);
        PlayerOptions Options(string name)=>new(Path.Combine(Path.GetDirectoryName(project)!,name+".ncmaproject"),headless,null,4,1d/60,30,Path.Combine(output,Guid.NewGuid()+"-report.json"),false,false);
        byte[]? baseline=null;uint width=0;var images=new List<object>();
        foreach(string name in new[]{"no-ui","project","no-font"}) {
            byte[]? image=null;uint w=0;
            var report=headless?PlayerRunner.Run(Options(name),pluginRoot:plugins,visible:false):PlayerRunner.RunForVerification(Options(name),plugins,(p,x,_)=>{image=p;w=x;});
            // Graphical AdvanceFrame can complete several fixed steps; only Null promises exact ticks.
            Pass(report.ExitCode==0&&report.Tick>=4&&(!headless||report.Tick==4)&&report.ShutdownErrors.Length==0,name+" runs/closes");
            Pass(report.ValidationErrors==0&&report.ValidationWarnings==0,name+" API0/0");
            Pass(report.Modules.Any(m=>m.Id=="ncma.text")==(!headless&&name=="project"),name+" text lazy");
            if(headless){Pass(report.RenderedFrames==0&&report.Modules.Length==0,"Null no graphics native load");continue;}
            Pass(image is not null,"actual frame captured");if(name=="no-ui"){baseline=image;width=w;continue;}
            int at=checked((int)((20*w+20)*4));Pass(Math.Abs(image![at]-51)<=1&&Math.Abs(image[at+1]-102)<=1&&Math.Abs(image[at+2]-204)<=1,"HUD after scene display domain");
            int changed=0,outside=0;for(int i=0;i<image.Length;i+=4){int x=i/4%(int)w,y=i/4/(int)w;if(!image.AsSpan(i,4).SequenceEqual(baseline!.AsSpan(i,4))){changed++;if(x>255||y>80)outside++;}}
            Pass(width==w&&changed>500&&outside==0,"HUD is visible, untouched scene preserved");
            File.WriteAllBytes(Path.Combine(output,name+".rgba"),image);images.Add(new{name,w,changed,outside});
        }
        foreach(string bad in new[]{"bad-hash","bad-generation","missing","interactive"}.Concat(headless?[]:new[]{"bad-font","missing-ui-shader"})) {
            int captured=0;var report=headless?PlayerRunner.Run(Options(bad),pluginRoot:plugins,visible:false):PlayerRunner.RunForVerification(Options(bad),plugins,(_,_,_)=>captured++);
            Pass(report.ExitCode!=0&&report.SessionId==Guid.Empty&&report.Tick==0&&captured==0&&report.ShutdownErrors.Length==0,bad+" rejects before gameplay and closes");
        }
        if(!headless){
            byte[]? image=null;uint w=0;var custom=PlayerRunner.RunForVerification(Options("project"),plugins,(p,x,_)=>{image=p;w=x;},pipelineFactory:(e,a,s)=>SceneCustomizationExamples.ReplacePipeline(e*.5f,a,s));
            int at=checked((int)((20*w+20)*4));Pass(custom.ExitCode==0&&image is not null&&Math.Abs(image[at+2]-204)<=1,"custom scene pipeline preserves display HUD");
        }
        if(relocated){
            // Also execute the real copied apphost, not only the assembly probe.
            var info=new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"NcmaPlayer.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{"--project",project,"--ticks","4","--report",Path.Combine(output,"apphost.json")})info.ArgumentList.Add(arg);if(headless)info.ArgumentList.Add("--headless");
            using var process=System.Diagnostics.Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(45000)){process.Kill(entireProcessTree:true);process.WaitForExit();throw new Exception("Owned apphost timeout");}
            File.WriteAllText(Path.Combine(output,"apphost.log"),stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());Pass(process.ExitCode==0,"actual copied apphost");
        }
        File.WriteAllText(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(new{checks,headless,relocated,images}));
    }
}
