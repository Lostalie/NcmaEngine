using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Asset.Import;
using Ncma.Assets;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    static byte[] Png(uint width, uint height, byte[] rgba)
    {
        using var output = new MemoryStream(); output.Write(new byte[] {137,80,78,71,13,10,26,10});
        void Chunk(string name, byte[] data) {
            byte[] type=Encoding.ASCII.GetBytes(name); Span<byte> number=stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number,(uint)data.Length);output.Write(number);output.Write(type);output.Write(data);
            uint crc=uint.MaxValue; foreach(byte value in type.Concat(data)){crc^=value;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);}BinaryPrimitives.WriteUInt32BigEndian(number,~crc);output.Write(number);
        }
        byte[] header=new byte[13];BinaryPrimitives.WriteUInt32BigEndian(header,width);BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;Chunk("IHDR",header);
        using var compressed=new MemoryStream(); using(var zip=new ZLibStream(compressed,CompressionLevel.SmallestSize,true)) {
            int row=rgba.Length==0?0:checked((int)width*4);for(int y=0;y<(rgba.Length==0?0:height);y++){zip.WriteByte(0);zip.Write(rgba,y*row,row);}
        }
        Chunk("IDAT",compressed.ToArray());Chunk("IEND",[]);return output.ToArray();
    }
    static void TestResourceData()
    {
        Check(Marshal.SizeOf<TextureMip>()==16&&Marshal.SizeOf<ResourceDraw>()==240&&Marshal.SizeOf<ResourceRenderStats>()==72,"Resource POD layout");
        byte[] source=[0,0,0,255,255,255,255,255];var color=TextureData.Prepare(2,1,TextureSemantic.Color,source);var linear=TextureData.Prepare(2,1,TextureSemantic.Data,source);
        Check(color.Pixels[8] is >=187 and <=188&&linear.Pixels[8] is >=127 and <=128,"sRGB linear-light mip versus data mip");source[0]=99;Check(color.Pixels[0]==0,"Texture snapshot borrowed source");
        var round=TextureData.Decode(color.Encode());Check(round.ContentHash==color.ContentHash&&round.Pixels.SequenceEqual(color.Pixels),"Texture strict roundtrip");
        var odd=TextureData.Prepare(3,1,TextureSemantic.Data,new byte[]{0,0,0,255,0,0,0,255,255,255,255,255});Check(odd.Pixels[12]==85,"Odd edge included in area mip");
        var alpha=TextureData.Prepare(2,1,TextureSemantic.Color,new byte[]{255,0,0,255,0,0,255,0});Check(alpha.Pixels[8]==255&&alpha.Pixels[10]==0&&alpha.Pixels[11] is >=127 and <=128,"Alpha-aware color mip");
        var normal=TextureData.Prepare(2,1,TextureSemantic.Normal,new byte[]{255,128,128,255,128,255,128,255},true);var n=new Vector3(normal.Pixels[8],normal.Pixels[9],normal.Pixels[10])/127.5f-Vector3.One;Check(Math.Abs(n.Length()-1)<.01,"Normal mip normalized");
        Reject(()=>TextureData.Prepare(4097,1,TextureSemantic.Data,[]));Reject(()=>TextureData.Prepare(4096,4096,TextureSemantic.Data,[]));Reject(()=>TextureData.Prepare(1,1,TextureSemantic.Color,new byte[4],true));Reject(()=>TextureData.Prepare(2,1,TextureSemantic.Data,new byte[4]));
        var bytes=color.Encode();bytes[40]++;Reject(()=>TextureData.Decode(bytes));bytes=color.Encode();Reject(()=>TextureData.Decode(bytes.Concat(new byte[]{0}).ToArray()));bytes[4]=2;Reject(()=>TextureData.Decode(bytes));
        using var cancel=new CancellationTokenSource();cancel.Cancel();try{TextureData.Prepare(1,1,TextureSemantic.Data,new byte[4],cancellation:cancel.Token);throw new Exception("Cancellation");}catch(OperationCanceledException){}
        var definition=MaterialDefinition.Default(Guid.NewGuid());byte[] encoded=MaterialCodec.Encode(definition);Check(MaterialCodec.Decode(encoded)==definition&&!Encoding.UTF8.GetString(encoded).Contains("textureIds"),"Material UUID-only strict roundtrip");
        Reject(()=>MaterialCodec.Encode(definition with{Roughness=0}));Reject(()=>MaterialCodec.Encode(definition with{BaseColor=new(float.NaN,0,0,1)}));Reject(()=>MaterialCodec.Decode(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(encoded).Replace("\"version\":1","\"version\":2"))));
        try{MaterialCodec.Decode(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(encoded).Replace("\"version\":1","\"version\":1,\"version\":1")));throw new Exception("Duplicate material");}catch(ArgumentException){}
        var set=new MaterialSetDefinition(1,Guid.NewGuid(),[definition.AssetId,definition.AssetId]);Check(MaterialCodec.DecodeSet(MaterialCodec.Encode(set)).Materials.SequenceEqual(set.Materials),"Material set slot identity");Reject(()=>MaterialCodec.Encode(set with{Materials=[Guid.Empty]}));
        Check(MaterialConversion.FromImportedSlot(definition.AssetId,"Phong source").Diagnostics.Length==2,"Explicit lossy default mapping");
        var graph=new ResourceMeshPipeline().Build(256,256);var plan=graph.Compile(RenderCapabilities.ResourceDx11);Check(!plan.RequiresReferenceResources&&plan.RequiresResourceService&&ReferenceEquals(plan,graph.Compile(RenderCapabilities.ResourceDx11)),"Typed graph cached without mandatory reference/shadows");
        Reject(()=>graph.Compile(RenderCapabilities.ReferenceDx11),"capability");Reject(()=>new ResourceMeshPipeline(new(0,.03f)).Build(256,256).Compile(RenderCapabilities.ResourceDx11),"parameter");Reject(()=>new ResourceMeshPipeline(new(1,.03f,(ResourceDrawMode)99)).Build(256,256).Compile(RenderCapabilities.ResourceDx11),"parameter");
        Console.WriteLine("PASS B/C/D strict texture/mips/material codecs and public graph validation");
    }
    static MeshUploadData Quad(float shift=0)
    {
        ImportVertex V(float x,float y,float u,float v)=>new(new(x+shift,y,.5f),Vector3.UnitZ,new(u,v),default,Vector4.Zero);
        return MeshUploadData.PrepareStatic(new(false,0,[V(-.8f,.8f,0,0),V(.8f,.8f,1,0),V(-.8f,-.8f,0,1),V(.8f,-.8f,1,1)],Enumerable.Repeat(new Vector4(1,0,0,-1),4).ToArray(),[0,1,2,2,1,3],[0,0],[],1));
    }
    static RenderAssetVersion Version(Guid id,TextureData data,ulong generation=1)=>new(id,generation,data.ContentHash);
    static RenderAssetVersion Version(MaterialDefinition data,ulong generation=1)=>new(data.AssetId,generation,Convert.ToHexString(SHA256.HashData(MaterialCodec.Encode(data))));
    static void TestResourceDrawing(RendererSession renderer,ref ulong frame,string repository,string kernel,string output)
    {
        using var decoder=new ImageDecoder(kernel,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernel))));
        byte[] texels=[128,64,32,255,32,128,64,255,64,32,128,255,220,180,120,0];byte[] encoded=Png(2,2,texels);
        var decoded=decoder.Decode(encoded,TextureSemantic.Color);Check(decoded.Width==2&&decoded.Height==2&&decoded.Pixels[..16].SequenceEqual(texels),"PNG RGBA channels/top-left alpha");
        Task.Run(()=>Reject(()=>decoder.Decode(encoded,TextureSemantic.Color))).GetAwaiter().GetResult();
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{decoder.Decode(encoded,TextureSemantic.Color,cancellation:cancelled.Token);throw new Exception("Decoder cancellation");}catch(OperationCanceledException){}}
        Reject(()=>decoder.Decode(Png(4097,1,[]),TextureSemantic.Color));Reject(()=>decoder.Decode(new byte[16],TextureSemantic.Color));Reject(()=>decoder.Decode(encoded[..20],TextureSemantic.Color));
        Reject(()=>decoder.Decode(Png(4096,4096,[]),TextureSemantic.Color));
        TestImageAbi(kernel,encoded);
        bool jpg=false;foreach(string name in new[]{"MotorDamping.jpg","MotorFrequency.jpg","ShapeCenterOfMass.jpg"}){try{var image=decoder.Decode(File.ReadAllBytes(Path.Combine(repository,"engine/sdk/JoltPhysics/Docs/Images",name)),TextureSemantic.Color);jpg|=image.Width>0&&image.Height>0;}catch(ArgumentException){}}
        Check(jpg,"No bounded JPEG fixture decoded");
        var data=Quad();var meshVersion=new RenderAssetVersion(Guid.NewGuid(),1,new string('A',64));Guid textureId=Guid.NewGuid();var textureVersion=Version(textureId,decoded);
        var lighting=new ResourceLighting(new(0,0,2),Vector3.UnitZ,new(1,1,1,1));var before=renderer.ResourceStats;
        int checkedPixels=0,maxError=0;long allocated=0;double elapsed=0;byte[] pixels=new byte[256*256*4];ulong stableCreates=0,stableUploads=0;RendererStats resourceProfile=default;
        for(int cycle=0;cycle<8;cycle++) {
            using var cache=new RenderResourceCache(renderer);using var mesh=cache.AcquireMesh(meshVersion,data);using var target=renderer.CreateViewTarget(256,256);
            using(var same=cache.AcquireMesh(meshVersion,data))Check(ReferenceEquals(mesh.Resource,same.Resource),"Geometry resident reuse");
            if(cycle==0)Reject(()=>cache.AcquireMesh(meshVersion,Quad(.1f)));
            if(cycle==0)Reject(()=>cache.AcquireMesh(meshVersion with{ContentHash=new string('B',64)},data));
            var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseTexture=textureId};
            ResolvedTexture? Resolve(Guid id)=>id==textureId?new(textureVersion,decoded):null;
            using(var material=cache.AcquireMaterial(Version(definition),definition,Resolve,true,true,out var diagnostics)) {
                Check(diagnostics.Count==0,"Unexpected material fallback");ResourceDraw[] draws=[ResourceDraw.Create(mesh.Resource,material.Resource,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity)];
                _oldResourceDraw=draws[0];
                var unlit=new ResourcePipelineSession(renderer,new ResourceMeshPipeline(new(1,.03f,ResourceDrawMode.Unlit)),256,256);
                unlit.Submit(frame++,draws,lighting,target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);
                for(int y=32;y<224;y++)for(int x=32;x<224;x++){int slot=((y>=128?1:0)*2+(x>=128?1:0))*4;int at=(y*256+x)*4;for(int c=0;c<3;c++)maxError=Math.Max(maxError,Math.Abs(pixels[at+c]-texels[slot+c]));checkedPixels++;}
                Check(maxError<=1,"sRGB GPU roundtrip/top-left UV mismatch: "+maxError);if(cycle==0)ExportBmp(Path.Combine(output,"resource-uv-srgb.bmp"),pixels,256,256);renderer.Present();
                if(cycle==0){renderer.Resize(300,256);unlit.Submit(frame++,draws,lighting,target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);Check(pixels[(64*256+64)*4]==128,"Resize invalidated independent target/texture leases");renderer.Present();renderer.Resize(256,256);unlit.Submit(frame++,draws,lighting,null,Vector4.UnitW);renderer.Capture(pixels);Check(pixels[(64*256+64)*4]==128,"Typed swapchain output");renderer.Present();}
                Reject(()=>cache.Dispose());Reject(()=>renderer.Dispose());
                using(var same=cache.AcquireMaterial(Version(definition),definition,Resolve,true,true,out _))Check(ReferenceEquals(same.Resource,material.Resource),"Material cached lease reuse");
                stableCreates=renderer.ResourceStats.Creates;stableUploads=renderer.ResourceStats.UploadedBytes;
                for(int warm=0;warm<8;warm++){unlit.Submit(frame++,draws,lighting,target,Vector4.UnitW);renderer.Present();}
                long startBytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();for(int i=0;i<64;i++){unlit.Submit(frame++,draws,lighting,target,Vector4.UnitW);renderer.Present();}
                allocated+=GC.GetAllocatedBytesForCurrentThread()-startBytes;elapsed+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Check(renderer.ResourceStats.Creates==stableCreates&&renderer.ResourceStats.UploadedBytes==stableUploads,"Stable scene re-uploaded resources");
                var masked=definition with{AssetId=Guid.NewGuid(),Mode=MaterialMode.AlphaMask};
                using var mask=cache.AcquireMaterial(Version(masked),masked,Resolve,true,true,out _);draws[0]=ResourceDraw.Create(mesh.Resource,mask.Resource,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity);
                unlit.Submit(frame++,draws,lighting,target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);Check(pixels[(192*256+192)*4]==0&&pixels[(64*256+64)*4]==128,"AlphaMask did not discard only transparent quadrant");if(cycle==0)ExportBmp(Path.Combine(output,"resource-alpha-mask.bmp"),pixels,256,256);renderer.Present();
            }
            if(cycle==0) {
                Guid normalId=Guid.NewGuid();
                foreach(bool yDown in new[]{false,true}) {
                    var normalData=TextureData.Prepare(1,1,TextureSemantic.Normal,new byte[]{128,230,204,255},yDown);var normalVersion=Version(normalId,normalData,yDown?2ul:1ul);
                    var nd=MaterialDefinition.Default(Guid.NewGuid()) with{NormalTexture=normalId};
                    using var mat=cache.AcquireMaterial(Version(nd),nd,id=>id==normalId?new(normalVersion,normalData):null,true,true,out _);
                    ResourceDraw[] draws=[ResourceDraw.Create(mesh.Resource,mat.Resource,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity)];
                    var diagnostic=new ResourcePipelineSession(renderer,new ResourceMeshPipeline(new(1,.03f,ResourceDrawMode.NormalDiagnostic)),256,256);diagnostic.Submit(frame++,draws,lighting,target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);
                    int center=(128*256+128)*4;Check(yDown?pixels[center+1]>220:pixels[center+1]<35,"Normal tangent handedness/Y flip");Check(pixels[center+2]>195,"Normal diagnostic z");ExportBmp(Path.Combine(output,yDown?"resource-normal-y-down.bmp":"resource-normal-y-up.bmp"),pixels,256,256);renderer.Present();
                    Reject(()=>ResourceDraw.Create(mesh.Resource,mat.Resource,data.Ranges[0],Matrix4x4.CreateScale(-1,1,1),Matrix4x4.Identity));
                }
                TestPbrMaps(renderer,ref frame,cache,mesh.Resource,data,target,lighting,pixels,output);
                var transformMaterial=MaterialDefinition.Default(Guid.NewGuid());using(var transformLease=cache.AcquireMaterial(Version(transformMaterial),transformMaterial,_=>null,true,true,out _)) {
                    Matrix4x4 model=Matrix4x4.CreateScale(1.2f,.7f,.5f)*Matrix4x4.CreateRotationY(.3f);ResourceDraw[] transformed=[ResourceDraw.Create(mesh.Resource,transformLease.Resource,data.Ranges[0],model,Matrix4x4.Identity)];renderer.SubmitResources(frame++,transformed,new(0,0,256,256),Vector4.UnitW,lighting,target,ResourceDrawMode.NormalDiagnostic);renderer.CaptureTarget(target,pixels);int center=(128*256+128)*4;Check(Math.Abs(pixels[center]-(MathF.Sin(.3f)*.5f+.5f)*255)<=1&&Math.Abs(pixels[center+2]-(MathF.Cos(.3f)*.5f+.5f)*255)<=1,"Inverse-transpose normal under nonuniform transformed model");renderer.Present();
                }
                var (bindSource,bindRig)=BindFixture();var bindData=BindPoseMeshUploadData.Prepare(bindSource,bindRig);var bindVersion=meshVersion with{AssetId=Guid.NewGuid()};using(var binding=cache.AcquireBindPoseMesh(bindVersion,bindData))using(var same=cache.AcquireBindPoseMesh(bindVersion,bindData))Check(ReferenceEquals(binding.Resource,same.Resource)&&!binding.Resource.CanUseNormalMap,"Bind numerical resource/source lease cache");
                var set=new MaterialSetDefinition(1,Guid.NewGuid(),[Guid.NewGuid(),Guid.NewGuid()]);var setVersion=new RenderAssetVersion(set.AssetId,1,Convert.ToHexString(SHA256.HashData(MaterialCodec.Encode(set))));
                using(var setLease=cache.AcquireMaterialSet(setVersion,set,_=>null,_=>null,true,true)){Check(setLease.Diagnostics.Count==2,"Material-set named fallback");ResourceDraw[] draws=[ResourceDraw.Create(mesh.Resource,setLease[0],new(0,0,3),Matrix4x4.Identity,Matrix4x4.Identity),ResourceDraw.Create(mesh.Resource,setLease[1],new(1,3,3),Matrix4x4.Identity,Matrix4x4.Identity)];renderer.SubmitResources(frame++,draws,new(0,0,256,256),Vector4.UnitW,lighting,target,ResourceDrawMode.Unlit);renderer.CaptureTarget(target,pixels);Check(pixels[(64*256+64)*4]==255&&pixels[(192*256+192)*4]==255,"Material-set typed submesh batch");renderer.Present();}
                byte[] checker=new byte[64*64*4];for(int y=0;y<64;y++)for(int x=0;x<64;x++){int at=(y*64+x)*4;byte value=(byte)(((x+y)&1)*255);checker[at]=checker[at+1]=checker[at+2]=value;checker[at+3]=255;}
                var mipTexture=TextureData.Prepare(64,64,TextureSemantic.Color,checker);Guid mipId=Guid.NewGuid();var mipDefinition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseTexture=mipId};using(var mipMaterial=cache.AcquireMaterial(Version(mipDefinition),mipDefinition,_=>new(Version(mipId,mipTexture),mipTexture),true,true,out _)){ResourceDraw[] draws=[ResourceDraw.Create(mesh.Resource,mipMaterial.Resource,data.Ranges[0],Matrix4x4.CreateScale(.04f,.04f,1),Matrix4x4.Identity)];renderer.SubmitResources(frame++,draws,new(0,0,256,256),Vector4.UnitW,lighting,target,ResourceDrawMode.Unlit);renderer.CaptureTarget(target,pixels);Check(Math.Abs(pixels[(128*256+128)*4]-188)<=1,"GPU complete sRGB mip minification");ExportBmp(Path.Combine(output,"resource-mip-minification.bmp"),pixels,256,256);renderer.Present();}
                var missing=MaterialDefinition.Default(Guid.NewGuid()) with{BaseTexture=Guid.NewGuid(),NormalTexture=Guid.NewGuid()};
                using(var fallback=cache.AcquireMaterial(Version(missing),missing,_=>null,true,false,out var losses)) {
                    Check(losses.Any(l=>l.Code=="missing_texture")&&losses.Any(l=>l.Code=="unsafe_normal_basis")&&!fallback.Resource.UsesNormalMap,"Named missing/unsafe normal fallback");
                }
                using var limited=new RenderResourceCache(renderer,0,0);ulong creates=renderer.ResourceStats.Creates;Reject(()=>limited.AcquireTexture(textureVersion,decoded));Check(renderer.ResourceStats.Creates==creates,"Managed budget uploaded failed candidate");
                using var oldGeneration=cache.AcquireMesh(meshVersion with{Generation=2},data);Check(!ReferenceEquals(oldGeneration.Resource,mesh.Resource),"Pinned generation replaced in place");
            }
            renderer.WaitIdle();resourceProfile=renderer.Stats;mesh.Dispose();target.Dispose();cache.Trim();Check(cache.Count==0&&cache.GeometryBytes==0&&cache.TextureBytes==0,"Cache dependency drain/leaked generation");
            Check(renderer.ResourceStats.Textures==0&&renderer.ResourceStats.Materials==0&&renderer.ResourceStats.Targets==0&&renderer.ResourceStats.ResidentBytes==0,"Native typed resource leak");
        }
        Check(allocated<=1024,"Stable typed batch allocated: "+allocated);var after=renderer.ResourceStats;var validation=renderer.Stats;Check(validation.ValidationErrors==0&&validation.ValidationWarnings==0,"Resource DX11 validation errors/warnings");
        File.WriteAllText(Path.Combine(output,"resource-results.json"),JsonSerializer.Serialize(new{schema=1,service="resource-pbr-v3",cycles=8,checkedPixels,maxError,stableFrames=512,stableOwnerAllocatedBytes=allocated,stableElapsedMs=elapsed,perDrawFrameBytes=376,submitMilliseconds=resourceProfile.SubmitMilliseconds,gpuSampleValid=resourceProfile.GpuSampleValid,gpuMilliseconds=resourceProfile.GpuMilliseconds,creates=after.Creates-before.Creates,uploadedBytes=after.UploadedBytes-before.UploadedBytes,stableReuploads=false,liveTextures=after.Textures,liveMaterials=after.Materials,liveTargets=after.Targets,residentBytes=after.ResidentBytes,validationErrors=validation.ValidationErrors,validationWarnings=validation.ValidationWarnings,png=true,jpeg=true,uvTopLeft=true,gpuMipMinification=true,alphaMask=true,normalYFlip=true,pbrChannels=true,cacheLeases=true,materialSet=true,shadows=false,ibl=false,gpuSkinning=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS B/C/D actual UV/sRGB/AlphaMask/normal/PBR channels/cache/budget/drain: pixels={checkedPixels} error={maxError} allocation={allocated} frames=512");
    }
    static void TestPbrMaps(RendererSession renderer,ref ulong frame,RenderResourceCache cache,GpuMesh mesh,MeshUploadData data,GpuViewTarget target,ResourceLighting lighting,byte[] pixels,string output)
    {
        byte Encode(float x) {x=(x*(2.51f*x+.03f))/(x*(2.43f*x+.59f)+.14f);x=Math.Clamp(x,0,1);x=x<=.0031308f?x*12.92f:1.055f*MathF.Pow(x,1/2.4f)-.055f;return (byte)Math.Clamp((int)MathF.Round(x*255),0,255);}
        Guid id=Guid.NewGuid();var channels=TextureData.Prepare(1,1,TextureSemantic.Data,new byte[]{0,255,128,255});var version=Version(id,channels);
        var black=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.25f,.5f,.75f,1),AOTexture=id,AOChannel=0};
        byte[] baseline=[];
        foreach(uint channel in new[]{0u,1u,2u}) {
            var d=black with{AssetId=Guid.NewGuid(),AOChannel=channel};using var mat=cache.AcquireMaterial(Version(d),d,_=>new(version,channels),true,true,out _);ResourceDraw[] draws=[ResourceDraw.Create(mesh,mat.Resource,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity)];
            var pbr=new ResourcePipelineSession(renderer,new ResourceMeshPipeline(new(1,1)),256,256);pbr.Submit(frame++,draws,lighting with{LightColor=Vector4.Zero},target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);int center=(128*256+128)*4;float value=channels.Pixels[(int)channel]/255f;
            Check(Math.Abs(pixels[center]-Encode(.25f*value))<=1&&Math.Abs(pixels[center+1]-Encode(.5f*value))<=1&&Math.Abs(pixels[center+2]-Encode(.75f*value))<=1,"Independent CPU ACES/sRGB/AO channel oracle");renderer.Present();
            if(channel==1){baseline=(byte[])pixels.Clone();ExportBmp(Path.Combine(output,"resource-pbr-ao.bmp"),pixels,256,256);}
        }
        foreach(var pipeline in new RenderPipeline[]{new ResourceMeshPipeline(new(2,1)),new ResourceMeshPipeline(new(1,1),[new ResourceExposureFeature(2)]),new ResourceMeshPipeline(new(1,1),stage:new ReplacementResourceStage()),new ReplacementResourcePipeline()}) {
            var d=black with{AssetId=Guid.NewGuid(),AOChannel=1};using var mat=cache.AcquireMaterial(Version(d),d,_=>new(version,channels),true,true,out _);ResourceDraw[] draws=[ResourceDraw.Create(mesh,mat.Resource,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity)];new ResourcePipelineSession(renderer,pipeline,256,256).Submit(frame++,draws,lighting with{LightColor=Vector4.Zero},target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);Check(!pixels.SequenceEqual(baseline),"Feature/Stage/replacement had no pixels");renderer.Present();
        }
        // Real GGX lighting distinguishes metallic/roughness sampling independently of unlit/ambient.
        var outputs=new List<byte[]>();foreach((uint metal,uint rough) in new[]{(0u,1u),(1u,1u),(1u,2u)}) {
            var d=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.8f,.2f,.1f,1),Metallic=1,Roughness=1,MetallicTexture=id,RoughnessTexture=id,MetallicChannel=metal,RoughnessChannel=rough};using var mat=cache.AcquireMaterial(Version(d),d,_=>new(version,channels),true,true,out _);ResourceDraw[] draws=[ResourceDraw.Create(mesh,mat.Resource,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity)];new ResourcePipelineSession(renderer,new ResourceMeshPipeline(),256,256).Submit(frame++,draws,lighting,target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);outputs.Add((byte[])pixels.Clone());ExportBmp(Path.Combine(output,$"resource-ggx-{metal}-{rough}.bmp"),pixels,256,256);renderer.Present();
        }
        Check(!outputs[0].SequenceEqual(outputs[1])&&!outputs[1].SequenceEqual(outputs[2]),"Metallic/roughness map channel no visible effect");
        var emissive=MaterialDefinition.Default(Guid.NewGuid()) with{Emissive=new(.1f,.2f,.3f,0)};using var glow=cache.AcquireMaterial(Version(emissive),emissive,_=>null,true,true,out _);ResourceDraw[] glowDraws=[ResourceDraw.Create(mesh,glow.Resource,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity)];new ResourcePipelineSession(renderer,new ResourceMeshPipeline(new(1,0)),256,256).Submit(frame++,glowDraws,lighting with{LightColor=Vector4.Zero},target,Vector4.UnitW);renderer.CaptureTarget(target,pixels);int at=(128*256+128)*4;Check(Math.Abs(pixels[at]-Encode(.1f))<=1&&Math.Abs(pixels[at+2]-Encode(.3f))<=1,"Emissive fallback/color oracle");renderer.Present();
    }
    sealed class ReplacementResourcePipeline : RenderPipeline
    {
        public override RenderGraph Build(uint width,uint height){var graph=new RenderGraph();var target=graph.AddResource(new("User output",RenderRole.ResourceOutput,RenderFormat.Rgba8,RenderUsage.ColorTarget,width,height,Imported:true));graph.AddPass("User replacement",RenderOperation.ResourceGeometry,[],[target],3,parameters:[2,1,0,0]);graph.SetOutput(target);return graph;}
    }
    sealed class ReplacementResourceStage : IResourceGeometryStage
    {public void Add(RenderGraph graph,RenderResourceId output,ResourcePipelineSettings settings)=>graph.AddPass("User stage",RenderOperation.ResourceGeometry,[],[output],3,parameters:[2,settings.Ambient,(float)settings.Mode,0]);}
    [StructLayout(LayoutKind.Sequential)] struct ImageInfo { public uint Size,Width,Height,Pitch,Bytes,Container,Reserved,Reserved2; }
    [StructLayout(LayoutKind.Sequential)] unsafe struct ImageError {public uint Code,Reserved,Required,Length;public fixed byte Message[512];}
    static void TestImageAbi(string library,byte[] encoded)
    {
        nint module=NativeLibrary.Load(library);
        try {
            var decode=(delegate* unmanaged[Cdecl]<uint,byte*,uint,ImageInfo*,byte*,uint,ImageError*,uint>)NativeLibrary.GetExport(module,"ncma_image_decode_v1");
            ImageInfo info=new(){Size=32};ImageError error=default;byte[] output=Enumerable.Repeat((byte)0xcc,16).ToArray();
            fixed(byte* source=encoded)fixed(byte* target=output){
                Check(decode(2,source,(uint)encoded.Length,&info,target,16,&error)==1,"Unknown image ABI");
                info.Size=31;Check(decode(1,source,(uint)encoded.Length,&info,target,16,&error)==2,"Image info size");info.Size=32;
                Check(decode(1,source,(uint)encoded.Length,&info,target,15,&error)==5&&error.Required==16&&output.All(b=>b==0xcc),"Short image output wrote pixels");
                Check(decode(1,source,(uint)encoded.Length,&info,target,16,&error)==0&&info.Width==2&&info.Height==2&&info.Container==1,"PNG memory ABI");
            }
        } finally {NativeLibrary.Free(module);}
    }
}
