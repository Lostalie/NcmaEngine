using System.Numerics;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Text;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static class DirtyCanvasTests
{
    private static void Check(bool value,string text){if(!value)throw new Exception("Dirty UI: "+text);}
    public static IEnumerable<(string Name,Action Run)> Cases(string[] args)
    {
        if(args.Length==0||!OperatingSystem.IsWindows())yield break;
        yield return("Real wrapped text survives paint-only input, clears glyph resources, reshapes on text/DPI, builder reset preserves published lists",()=>{
            using var loader=new PluginLoader();loader.Load(args[0],[
                new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
                new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),
                new("text",ModuleKind.Text,"NcmaText.dll","NcmaText.dll",1,0,[])]);
            var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);var textModule=loader.Modules.Single(m=>m.Kind==ModuleKind.Text);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"UI dirty text",256,128,false);
            using var renderer=new RendererSession(native,window,256,128,pureUi:true);using var textService=new TextService(textModule);
            var font=new FontAsset(Guid.NewGuid(),File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc")),"Windows regression only; no redistribution",false);
            var d=UiDefinition.Create(Guid.NewGuid(),"Text");var blank=UiStyle.Default with{Fill=UiColor.Transparent,Foreground=UiColor.White};
            var panel=UiElement.Create(Guid.NewGuid(),"Panel",UiKind.Frame,d.Root) with{Layout=UiLayout.Fixed(16,16,128,32),Style=blank with{Clip=true}};
            var label=UiElement.Create(Guid.NewGuid(),"Label",UiKind.Text,panel.Id) with{Text="Long wrapped label 中文",Font=font.Id,FontSize=14,Style=blank,Layout=UiLayout.Fixed(0,0,0,32) with{WidthMode=UiSizing.Fill}};
            var button=UiElement.Create(Guid.NewGuid(),"Button",UiKind.Button,d.Root) with{Order=1,Text="Run",Font=font.Id,FontSize=14,Style=blank,Layout=UiLayout.Fixed(160,16,80,24)};
            var input=UiElement.Create(Guid.NewGuid(),"Input",UiKind.TextInput,d.Root) with{Order=2,Text="中文 e\u0301",Font=font.Id,FontSize=14,Style=blank,Layout=UiLayout.Fixed(160,52,80,24)};
            var slider=UiElement.Create(Guid.NewGuid(),"Slider",UiKind.Slider,d.Root) with{Order=3,Action="value",Style=blank with{Fill=new(.2f,.2f,.2f,1)},Layout=UiLayout.Fixed(8,100,160,12)};
            d=d with{Elements=[d.Elements[0] with{Style=blank with{Fill=new(0,0,0,1)}},panel,label,button,input,slider]};
            using var canvas=UiCanvas.Create(new(d),renderer,textService,new Dictionary<Guid,FontAsset>{{font.Id,font}});
            var viewport=new UiViewport(256,128,1,default);canvas.Prepare(viewport);var layout=canvas.Runtime.Layout(viewport);
            ulong frame=0,sequence=0;byte[] pixels=new byte[256*128*4];
            void Draw(){canvas.Submit(++frame,Vector4.Zero);renderer.Capture(pixels);renderer.Present();}
            bool Black(int x,int y,int w,int h){for(int py=y;py<y+h;py++)for(int px=x;px<x+w;px++)for(int c=0;c<3;c++)if(pixels[(py*256+px)*4+c]!=0)return false;return true;}
            Draw();byte[] original=(byte[])pixels.Clone();Check(!Black(16,16,128,32),"actual wrapped glyphs");
            ulong shapes=textService.PrepareCalls,images=renderer.UiStats.Images;UiInput[] events=new UiInput[1];var actions=new UiAction[128];
            canvas.Runtime.Input(canvas.Runtime.Stamp,[new(UiInputKind.PointerDown,++sequence,new(40,106))]);canvas.Runtime.Drain(canvas.Runtime.Stamp,actions);
            long inputTime=0,inputBytes=0;ulong inputUploads=0;
            for(int i=0;i<96;i++){
                if(i==32){inputUploads=renderer.UiStats.UploadedBytes;inputBytes=GC.GetAllocatedBytesForCurrentThread();inputTime=System.Diagnostics.Stopwatch.GetTimestamp();}
                events[0]=new(UiInputKind.PointerMove,++sequence,new(i%2==0?48:112,106));canvas.Runtime.Input(canvas.Runtime.Stamp,events);canvas.Runtime.Drain(canvas.Runtime.Stamp,actions);
                canvas.Prepare(viewport);canvas.Submit(++frame,Vector4.Zero);renderer.Present();
            }
            double inputMs=System.Diagnostics.Stopwatch.GetElapsedTime(inputTime).TotalMilliseconds;inputBytes=GC.GetAllocatedBytesForCurrentThread()-inputBytes;inputUploads=renderer.UiStats.UploadedBytes-inputUploads;
            Check(inputBytes<=64*64,"warmed input/prepare/submit has bounded list wrappers, not per-element allocations");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{scope="64 warmed pointer input/drain/Prepare/Submit/Present; no readback/wait; excludes observation and warmup",inputMs,inputBytes,inputUploads,shapes=textService.PrepareCalls-shapes,fullPerformanceAccepted=false}));
            Draw();Check(shapes==textService.PrepareCalls&&images==renderer.UiStats.Images&&layout.Overlaps(canvas.Runtime.Layout(viewport)),"paint does not reshape/reupload glyphs or recopy layout");
            Check(original.AsSpan(0,256*90*4).SequenceEqual(pixels.AsSpan(0,256*90*4)),"all top glyph pixels unchanged after actual input");
            canvas.Runtime.SetText(label.Id,"");canvas.Runtime.SetText(button.Id,"");canvas.Runtime.SetText(input.Id,"");canvas.Prepare(viewport);Draw();
            Check(Black(16,16,128,32)&&Black(160,16,80,24)&&Black(160,52,80,24),"cleared Text/Button/Input have no old glyphs");
            Check(renderer.UiStats.Images==1,"cleared glyph images retired after old display list");
            canvas.Runtime.SetText(label.Id,label.Text);canvas.Runtime.SetText(button.Id,button.Text);canvas.Runtime.SetText(input.Id,input.Text);canvas.Prepare(viewport);Draw();
            Check(original.AsSpan(0,256*90*4).SequenceEqual(pixels.AsSpan(0,256*90*4)),"text restoration exact pixels");
            canvas.Runtime.SetText(input.Id,"😀");bool refused=false;
            // Earlier runs prepare at DPI2 before this last run fails. The last
            // successful viewport is still DPI1, so matching text alone is not enough.
            try{canvas.Prepare(new(512,256,2,default));}catch(PluginException e) when(e.PluginId=="text"&&e.Phase=="prepare_text"&&e.Result==PluginResult.UnsupportedFeature){refused=true;}
            Check(refused,"missing glyph remains explicit refusal, never an implicit OS fallback");
            bool stale=false;try{canvas.Submit(++frame,Vector4.Zero);}catch(InvalidOperationException){stale=true;}
            Check(stale,"failed text preparation cannot submit stale content");
            shapes=textService.PrepareCalls;
            canvas.Runtime.SetText(input.Id,input.Text);canvas.Prepare(viewport);Draw();
            Check(textService.PrepareCalls>=shapes+2,"partial failed DPI preparation cannot reuse mismatched-scale glyph runs");
            Check(original.AsSpan(0,256*90*4).SequenceEqual(pixels.AsSpan(0,256*90*4)),"explicit valid text recovery restores exact pixels");
            canvas.Runtime.Input(canvas.Runtime.Stamp,[new(UiInputKind.PointerDown,++sequence,new(180,60)),new(UiInputKind.PointerUp,++sequence,new(180,60))]);canvas.Prepare(viewport);
            shapes=textService.PrepareCalls;long textBytes=GC.GetAllocatedBytesForCurrentThread(),textTime=System.Diagnostics.Stopwatch.GetTimestamp();ulong textUploads=renderer.UiStats.UploadedBytes;
            for(int i=0;i<16;i++){
                events[0]=new(i%2==0?UiInputKind.Text:UiInputKind.Backspace,++sequence,default,i%2==0?"x":"");
                canvas.Runtime.Input(canvas.Runtime.Stamp,events);canvas.Runtime.Drain(canvas.Runtime.Stamp,actions);canvas.Prepare(viewport);canvas.Submit(++frame,Vector4.Zero);renderer.Present();
            }
            double textMs=System.Diagnostics.Stopwatch.GetElapsedTime(textTime).TotalMilliseconds;textBytes=GC.GetAllocatedBytesForCurrentThread()-textBytes;textUploads=renderer.UiStats.UploadedBytes-textUploads;
            Check(canvas.Runtime.Text(input.Id)==input.Text&&textService.PrepareCalls-shapes==16&&renderer.UiStats.Images==4,"text edits reshape only changed run and retire prior glyph images");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{scope="16 alternating text/backspace input/drain/Prepare/Submit/Present; no readback/wait; not cold or total application cost",textMs,textBytes,textUploads,shapes=textService.PrepareCalls-shapes,fullPerformanceAccepted=false}));
            canvas.Runtime.CancelInput();canvas.Prepare(viewport);Draw();
            Check(original.AsSpan(0,256*90*4).SequenceEqual(pixels.AsSpan(0,256*90*4)),"actual typed/backspace/cancel preserves Unicode glyph pixels");
            shapes=textService.PrepareCalls;renderer.Resize(512,256);viewport=new(512,256,2,default);canvas.Prepare(viewport);
            Check(textService.PrepareCalls>shapes,"DPI invalidates shaping");canvas.Submit(++frame,Vector4.Zero);renderer.Present();
            canvas.Dispose();Check(renderer.UiStats.Images==0&&renderer.UiStats.Lists==0&&textModule.Status.LiveResources==0,"font/glyph/list close drains");
            renderer.Resize(256,128);using var white=renderer.CreateUiImage(1,1,[255,255,255,255]);var builder=new UiDisplayListBuilder(renderer);
            void Quad(UiColor color)=>builder.Quad(new(Guid.NewGuid(),new(0,0,32,32),Matrix3x2.Identity,new(0,0,256,128),1,true,-1),white,color);
            Quad(new(1,0,0,1));using var red=builder.Build();builder.Reset();Quad(new(0,1,0,1));using var green=builder.Build();
            renderer.SubmitUi(red,++frame,Vector4.Zero);renderer.Capture(pixels);renderer.Present();Check(pixels[(8*256+8)*4]==255,"Reset did not mutate old published native list");
            renderer.SubmitUi(green,++frame,Vector4.Zero);renderer.Capture(pixels);renderer.Present();Check(pixels[(8*256+8)*4+1]==255,"rebuilt list independently green");
            Check(Task.Run(()=>{try{builder.Reset();return false;}catch(PluginException e){return e.Result==PluginResult.WrongThread;}}).GetAwaiter().GetResult(),"reset owner guard");
            Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"API0/0");
        });
    }
}
