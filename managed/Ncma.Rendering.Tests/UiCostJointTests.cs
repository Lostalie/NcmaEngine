using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe partial class Program
{
    private static int UiCostJointTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0,maxError=0;void Pass(bool ok,string text){Check(ok,"M7.7 2D joint: "+text);checks++;}
        const int columns=64,rows=48,count=columns*rows;
        var definition=UiDefinition.Create(Guid.NewGuid(),"Large flat application");
        var rectangles=Enumerable.Range(0,count).Select(i=>UiElement.Create(Guid.NewGuid(),"Cell "+i,UiKind.Rectangle,definition.Root) with{
            Order=i+1,Layout=UiLayout.Fixed(i%columns*8,i/columns*8,6,6),
            Style=UiStyle.Default with{Fill=i%3==0?new(1,0,0,1):i%3==1?new(0,1,0,1):new(0,0,1,1)}}).ToArray();
        var slider=UiElement.Create(Guid.NewGuid(),"Value",UiKind.Slider,definition.Root) with{Order=count+1,Action="value",Layout=UiLayout.Fixed(8,388,128,8),Style=UiStyle.Default with{Fill=new(64f/255,64f/255,64f/255,1),Foreground=new(0,1,0,1)}};
        Guid imageId=Guid.NewGuid();var icon=UiElement.Create(Guid.NewGuid(),"Image",UiKind.Image,definition.Root) with{Order=count+2,Image=imageId,Layout=UiLayout.Fixed(152,388,16,8)};
        definition=definition with{Elements=[definition.Elements[0] with{Style=UiStyle.Default with{Fill=new(0,0,0,1)}},..rectangles,slider,icon]};
        var observations=new List<object>();var diagnosticFrames=new HashSet<ulong>();ulong frame=0,renderedRevision=ulong.MaxValue;float value=0;bool hidden=false;
        using(var loader=new PluginLoader()){
            loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"2D cost/oracle",512,400,false);
            using var renderer=new RendererSession(native,window,512,400,pureUi:true);
            var capture=new PresentationCostCapture(renderer);var viewport=new UiViewport(512,400,1,default);
            long coldBytes=GC.GetAllocatedBytesForCurrentThread(),coldTime=Stopwatch.GetTimestamp();
            using var canvas=UiCanvas.Create(new(definition),renderer,null,images:new Dictionary<Guid,UiImageAsset>{{imageId,new(imageId,1,1,[255,255,0,255])}});
            double createMs=Stopwatch.GetElapsedTime(coldTime).TotalMilliseconds;coldBytes=GC.GetAllocatedBytesForCurrentThread()-coldBytes;
            Pass(coldBytes<12*1024*1024,"font-free 3075-element cold allocation regression bound; not a general UI resource budget");
            // Independent integer-grid oracle; does not call UI layout, renderer math,
            // shader code, canvas geometry or a second copy of the product pipeline.
            byte[] Oracle(int scale){int width=512*scale,height=400*scale;byte[] rgba=new byte[width*height*4];for(int i=3;i<rgba.Length;i+=4)rgba[i]=255;
                void Rect(int x,int y,int w,int h,byte r,byte g,byte b){for(int py=y*scale;py<(y+h)*scale;py++)for(int px=x*scale;px<(x+w)*scale;px++){int at=(py*width+px)*4;rgba[at]=r;rgba[at+1]=g;rgba[at+2]=b;}}
                for(int i=0;i<count;i++)if(!(hidden&&i==7))Rect(i%columns*8,i/columns*8,6,6,(byte)(i%3==0?255:0),(byte)(i%3==1?255:0),(byte)(i%3==2?255:0));
                Rect(8,388,128,8,64,64,64);Rect(8,388,(int)(128*value),8,0,255,0);Rect(152,388,16,8,255,255,0);return rgba;}
            // Dedicated diagnostic frame outside all cost intervals. Capture is before
            // Present as required; its delayed GPU sample remains explicitly labeled.
            void Pixels(string name,int scale){diagnosticFrames.Add(++frame);canvas.Submit(frame,Vector4.Zero);byte[] actual=new byte[512*400*scale*scale*4];renderer.Capture(actual);renderer.Present();var expected=Oracle(scale);int error=0,at=-1;for(int i=0;i<actual.Length;i++){int delta=Math.Abs(actual[i]-expected[i]);if(delta>error){error=delta;at=i;}}maxError=Math.Max(maxError,error);File.WriteAllBytes(Path.Combine(output,name+".rgba"),actual);File.WriteAllBytes(Path.Combine(output,name+"-expected.rgba"),expected);if(error>1)Console.WriteLine($"IMAGE {name} error={error} x={at/4%(512*scale)} y={at/4/(512*scale)} channel={at%4} actual={actual[at]} expected={expected[at]}; {output}");Pass(error<=1,"independent full image "+name);}
            void Measure(string name,Action change,bool image=false){var before=renderer.UiStats;ulong prepared=canvas.Preparations;long allocation=GC.GetAllocatedBytesForCurrentThread(),time=Stopwatch.GetTimestamp();change();canvas.Prepare(viewport);double ms=Stopwatch.GetElapsedTime(time).TotalMilliseconds;allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
                var afterPrepare=renderer.UiStats;bool redraw=renderedRevision!=canvas.Runtime.PresentationRevision||canvas.Preparations!=prepared;PresentationCostReport? submitted=null;
                if(redraw){capture.Begin(++frame);canvas.Submit(frame,Vector4.Zero);renderer.Present();submitted=capture.End();renderedRevision=canvas.Runtime.PresentationRevision;}
                var after=renderer.UiStats;observations.Add(new{name,prepareMilliseconds=ms,prepareAllocatedBytes=allocation,preparations=canvas.Preparations-prepared,uploadedBytes=afterPrepare.UploadedBytes-before.UploadedBytes,redraw,draws=after.Draws-before.Draws,submitted,gpuSourceIncludesReadback=submitted?.Main is {} main&&diagnosticFrames.Contains(main.Sample.Timing.OriginFrame)});
                if(name.StartsWith("dirty-",StringComparison.Ordinal))Pass(allocation<=64,"warmed paint-only prepare allocation bounded independently of 3075 elements");
                if(redraw){ulong quads=(ulong)(count+3+(value>0?1:0)-(hidden?1:0));Pass(afterPrepare.UploadedBytes-before.UploadedBytes==quads*6*52,"explicit complete-list dirty upload, not partial-update claim: "+name);Pass(after.Draws-before.Draws==2,"order-correct adjacent batching, one white and one image batch");}
                else Pass(after.UploadedBytes==before.UploadedBytes&&after.Submits==before.Submits,"unchanged event does not upload/draw");
                if(image)Pixels(name,(int)viewport.Scale);
            }
            Measure("initial",()=>{},true);
            Measure("slider",()=>{value=.5f;canvas.Runtime.SetValue(slider.Id,value);},true);
            Measure("hidden",()=>{hidden=true;canvas.Runtime.SetFlags(rectangles[7].Id,false,true);},true);
            var staticStats=renderer.UiStats;ulong staticPreparations=canvas.Preparations;
            for(int i=0;i<32;i++)canvas.Prepare(viewport);
            long idleBytes=GC.GetAllocatedBytesForCurrentThread(),idleTime=Stopwatch.GetTimestamp();
            for(int i=0;i<256;i++)canvas.Prepare(viewport);
            double idleMs=Stopwatch.GetElapsedTime(idleTime).TotalMilliseconds;idleBytes=GC.GetAllocatedBytesForCurrentThread()-idleBytes;
            Pass(idleBytes==0&&canvas.Preparations==staticPreparations&&renderer.UiStats.UploadedBytes==staticStats.UploadedBytes&&renderer.UiStats.Submits==staticStats.Submits,"256 unchanged full Prepare calls zero owner allocation/upload/draw");
            Measure("same-value",()=>canvas.Runtime.SetValue(slider.Id,value));
            // No diagnostic capture or explicit WaitIdle in the dirty cost run.
            long dirtyBytes=GC.GetAllocatedBytesForCurrentThread(),dirtyTime=Stopwatch.GetTimestamp();
            for(int i=0;i<24;i++)Measure("dirty-"+i,()=>{value=value==.25f?.75f:.25f;canvas.Runtime.SetValue(slider.Id,value);});
            double dirtyMs=Stopwatch.GetElapsedTime(dirtyTime).TotalMilliseconds;dirtyBytes=GC.GetAllocatedBytesForCurrentThread()-dirtyBytes;
            Pixels("dirty-final",1);
            renderer.Resize(1024,800);viewport=new(1024,800,2,default);Measure("dpi2",()=>{},true);
            renderer.Resize(512,400);viewport=new(512,400,1,default);Measure("restored",()=>{},true);
            var final=renderer.UiStats;Pass(final.Images==2&&final.Lists==1,"dirty replacements retain bounded image/list resources");
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.EnvironmentStats.Live==0&&renderer.PostStats.Programs==0&&renderer.ResourceStats.Targets==0&&renderer.Stats.LiveGroups==0&&native.Status.LiveResources>0,"pure2D no 3D/workload initialization");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"actual API0/0");
            observations.Add(new{createMs,coldBytes,idleMs,idleBytes,dirtyMs,dirtyBytes,dirtyScope="24 updates including harness observation/Present/retirement, not pure CPU work",residentBytes=final.ResidentBytes});
            canvas.Dispose();Pass(renderer.UiStats.Images==0&&renderer.UiStats.Lists==0&&renderer.UiStats.ResidentBytes==0,"all UI resources released");
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,maxError,elements=count+3,observations,fullPerformanceAccepted=false,partialDirtyUploadImplemented=false}));
        Console.WriteLine($"PASS M7.7 2D cost/image joint {checks} checks; maxError={maxError}; {output}");return 0;
    }
}
