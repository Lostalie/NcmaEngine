using System.Numerics;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;

internal static unsafe partial class Program
{
    private static int RegisteredUiTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0,maxError=0;bool allowed=true;long staticBytes;
        void Pass(bool value,string message){Check(value,"M7.1-C3 "+message);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Independent registered flat UI",256,256,false);
        RegisteredUiShaders retained;
        using(var renderer=new RendererSession(native,window,256,256,pureUi:true)){
            var defaults=DefaultUiShaders.CopyCatalog(renderer);var selected=DefaultUiShaders.Select(defaults);
            var vd=selected.Vertex.CopyDefinition();var pd=selected.Pixel.CopyDefinition();
            ShaderDefinition Change(ShaderDefinition d,string before,string after){Check(d.Source.Contains(before,StringComparison.Ordinal),"UI source mutation fixture");string source=d.Source.Replace(before,after,StringComparison.Ordinal);return d with{AssetId=Guid.NewGuid(),Name="User UI",Source=source,SourceHash=ShaderContractCodec.HashSource(source)};}
            // Swap both texture and tint RGB; retain coverage and straight alpha exactly.
            var user=Change(pd,"Image.Sample(Linear,v.uv)*v.c", "Image.Sample(Linear,v.uv).bgra*v.c.bgra");
            var shifted=Change(vd,"v.p.x/Size.x*2-1","(v.p.x+8)/Size.x*2-1");
            var catalog=ShaderCatalog.Create(ShaderProfile.Flat2D,[vd,pd,user,shifted]);
            retained=RegisteredUiShaders.Prepare(renderer,catalog,selected,()=>allowed);
            var changed=RegisteredUiShaders.Prepare(renderer,catalog,selected with{Pixel=ShaderDescriptor.Prepare(user)},()=>allowed);
            var moved=RegisteredUiShaders.Prepare(renderer,catalog,selected with{Vertex=ShaderDescriptor.Prepare(shifted)},()=>allowed);
            Pass(retained.CopyMetadata().Length==2&&retained.CopyMetadata().All(m=>m.Compiled)&&retained.CopyMetadata()[0].CatalogHash==changed.CopyMetadata()[0].CatalogHash,"default/user SAME Flat2D catalog/compiler");
            Pass(renderer.UiStats.ResidentBytes==0&&renderer.UiStats.Images==0&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.Stats.SubmittedFrames==0,"preparation creates no UI/3D resources or frames");
            Bad(()=>renderer.ReplaceUiShaders(retained));renderer.CreateUiShaders(retained);Bad(()=>renderer.CreateUiShaders(changed));
            Pass(renderer.UiShaderGeneration==1&&ReferenceEquals(renderer.RegisteredUiShaders,retained),"explicit create and no implicit replacement");
            using var white=renderer.CreateUiImage(1,1,[255,255,255,255]);using var textured=renderer.CreateUiImage(1,1,[255,128,64,255]);
            var builder=new UiDisplayListBuilder(renderer);
            void Quad(float x,float y,float w,float h,UiGpuImage image,UiColor color,float radius=0,UiRect? clip=null){builder.Quad(new(Guid.NewGuid(),new(0,0,w,h),Matrix3x2.CreateTranslation(x,y),clip??new(0,0,256,256),1,true,-1),image,color,radius:radius);}
            Quad(16,16,128,128,white,new(.8f,.2f,.1f,1));Quad(64,64,128,128,textured,new(.2f,.8f,.4f,.5f));
            Quad(96,96,128,128,white,new(.1f,.2f,.9f,.5f),clip:new(112,96,112,112));
            Quad(192,16,48,48,white,new(.3f,.6f,.9f,1),12);
            Pass(builder.VertexCount==24&&builder.BatchCount==4,"ordered overlap/image/clip changes never reordered");
            using var list=builder.Build();using var target=renderer.CreateUiTarget(256,256);ulong frame=1,revision=1;
            byte[] Draw(){renderer.SubmitUiTarget(target,list,frame++,revision++,Vector4.Zero);return renderer.CaptureUiTarget(target);}
            byte[] baseline=Draw();
            // Independent scalar blending at interior samples (exclude analytic AA boundary).
            foreach(var p in new[]{(32,32),(80,80),(120,120),(104,104),(160,160),(220,220),(193,17),(216,40),(4,4)}){
                Vector4 result=Vector4.Zero;void Over(Vector3 rgb,float a){result=new Vector4(rgb*a+new Vector3(result.X,result.Y,result.Z)*(1-a),a+result.W*(1-a));}
                if(p.Item1>=16&&p.Item1<144&&p.Item2>=16&&p.Item2<144)Over(new(.8f,.2f,.1f),1);
                if(p.Item1>=64&&p.Item1<192&&p.Item2>=64&&p.Item2<192)Over(new(.2f,.8f*128/255,.4f*64/255),.5f);
                if(p.Item1>=112&&p.Item1<224&&p.Item2>=96&&p.Item2<208)Over(new(.1f,.2f,.9f),.5f);
                if(p==(216,40))Over(new(.3f,.6f,.9f),1);
                for(int c=0;c<4;c++)maxError=Math.Max(maxError,Math.Abs(baseline[(p.Item2*256+p.Item1)*4+c]-(int)MathF.Round(result[c]*255)));
            }
            Pass(maxError<=2,"independent order/texture/straight-alpha/scissor/rounded-corner oracle "+maxError);
            ulong oldRevision=revision-1;
            using(var lease=renderer.AcquireUiPresentation(target,oldRevision,frame)){Bad(()=>renderer.ReplaceUiShaders(changed));}
            renderer.ReplaceUiShaders(changed);
            Pass(renderer.CaptureUiTarget(target).SequenceEqual(baseline),"replacement keeps old cached target content until explicit refresh");
            byte[] userPixels=Draw();int channelError=0;for(int i=0;i<baseline.Length;i++)channelError=Math.Max(channelError,Math.Abs(userPixels[i]-baseline[i/4*4+(i%4==3?3:2-i%4)]));
            Pass(channelError<=1&&!userPixels.SequenceEqual(baseline),"actual user pixel shader full-image channel oracle "+channelError);
            renderer.ReplaceUiShaders(retained);Pass(Draw().SequenceEqual(baseline),"default restore same resident lists/images");
            renderer.ReplaceUiShaders(moved);byte[] movedPixels=Draw();
            Pass(movedPixels[(32*256+20)*4+3]==0&&movedPixels[(32*256+28)*4]==baseline[(32*256+20)*4]&&!movedPixels.SequenceEqual(baseline),"actual user vertex shader eight-pixel displacement");
            renderer.ReplaceUiShaders(retained);Pass(Draw().SequenceEqual(baseline),"vertex shader restore exact pixels");
            RegisteredUiShaders PrepareBad(ShaderDefinition bad,bool vertex=false){var c=ShaderCatalog.Create(ShaderProfile.Flat2D,[vd,pd,bad]);return RegisteredUiShaders.Prepare(renderer,c,vertex?selected with{Vertex=ShaderDescriptor.Prepare(bad)}:selected with{Pixel=ShaderDescriptor.Prepare(bad)},()=>allowed);}
            var wrongOutput=PrepareBad(Change(pd,":SV_TARGET",":SV_TARGET1"));Bad(()=>renderer.ReplaceUiShaders(wrongOutput));
            var wrongLink=PrepareBad(Change(vd,"struct O {float4 p:SV_POSITION;float2 uv:TEXCOORD0;","struct O {float4 p:SV_POSITION;float2 uv:TEXCOORD4;"),true);Bad(()=>renderer.ReplaceUiShaders(wrongLink));
            Bad(()=>PrepareBad(Change(pd,"register(t0)","register(t1)")));
            Bad(()=>PrepareBad(Change(vd,"float2 Pad;","float2 WrongPad;"),true));
            Bad(()=>PrepareBad(vd with{AssetId=Guid.NewGuid(),Name="Wrong layout",Inputs=vd.Inputs.Select(i=>i.ByteOffset==48?i with{ByteOffset=44}:i).ToArray()},true));
            Bad(()=>RegisteredUiShaders.Prepare(renderer,catalog,selected with{Vertex=selected.Pixel},()=>true));
            allowed=false;Bad(()=>renderer.ReplaceUiShaders(changed));Bad(()=>RegisteredUiShaders.Prepare(renderer,catalog,selected,()=>allowed));allowed=true;
            Bad(()=>RegisteredUiShaders.Prepare(renderer,catalog,selected,()=>{renderer.ReplaceUiShaders(changed);return true;}));
            renderer.SubmitUi(list,frame++,Vector4.Zero);Bad(()=>renderer.ReplaceUiShaders(changed));Bad(()=>DefaultUiShaders.CopyCatalog(renderer));renderer.Present();
            Exception? threadError=null;var worker=new Thread(()=>{try{renderer.ReplaceUiShaders(changed);}catch(Exception e){threadError=e;}});worker.Start();worker.Join();Pass(threadError is PluginException{Result:PluginResult.WrongThread},"owner thread guard");
            Pass(renderer.UiShaderGeneration==5&&ReferenceEquals(renderer.RegisteredUiShaders,retained)&&Draw().SequenceEqual(baseline),"all bad candidates leave publication/generation/pixels intact");
            var resources=renderer.UiStats;var targets=renderer.UiTargetStats;
            for(int i=0;i<8;i++)renderer.ReplaceUiShaders(i%2==0?changed:retained);
            Pass(renderer.UiStats.UploadedBytes==resources.UploadedBytes&&renderer.UiStats.ResidentBytes==resources.ResidentBytes&&renderer.UiTargetStats.ResidentBytes==targets.ResidentBytes,"shader swaps no image/list/target rebuild or uploads");
            // Cached static policy reads are allocation-free after warmup; no submit/compile/upload.
            for(int i=0;i<64;i++){_ = renderer.UiStats;_ = renderer.UiShaderGeneration;}
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1024;i++){_ = renderer.UiStats;_ = renderer.UiShaderGeneration;}staticBytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Pass(staticBytes==0&&renderer.UiStats.Draws==resources.Draws&&renderer.UiStats.UploadedBytes==resources.UploadedBytes,"1024 cached static checks zero allocations/draws/uploads");
            Pass(renderer.UiStats.PureUi==1&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.SkinStats.ResidentBytes==0&&renderer.Stats.LiveGroups==0,"pure2D no scene/skin/reference groups");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"real DX11 API0/0");
            File.WriteAllBytes(Path.Combine(output,"ui-default.png"),Png(256,256,baseline));File.WriteAllBytes(Path.Combine(output,"ui-user.png"),Png(256,256,userPixels));
        }
        using(var renderer=new RendererSession(native,window,256,256,pureUi:true)){
            Bad(()=>renderer.CreateUiShaders(retained));Pass(renderer.UiStats.ResidentBytes==0&&renderer.UiShaderGeneration==0,"foreign/released preparation rejected before UI initialization");
        }
        File.WriteAllText(Path.Combine(output,"registered-ui-results.json"),JsonSerializer.Serialize(new{schema=1,cases,query=11,api=1,backend="DX11",sameCatalogDefaultUser=true,pure2D=true,maxError,staticBytes,validationErrors=0,validationWarnings=0,formalHostSwitch=false,runtimeShaderPackage=false,agentExecutionAuthority=false,manualAcceptance=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.1-C3 {cases} registered UI actual pixels/alpha/clip/order/atomic/cache/owner cases; maxError={maxError}; staticBytes={staticBytes}; API0/0; C4 pending");return 0;
    }
}
