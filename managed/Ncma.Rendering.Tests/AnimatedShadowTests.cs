using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Rendering;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    static void TestAnimatedShadow(RendererSession renderer,ref ulong frame,string native,string output) {
        var fixture=new SkinFixture(output);var m=fixture.Manifest;
        var blocks=DerivedAssetCodec.Decode(fixture.Plan.DerivedBytes).ToDictionary(b=>b.AssetId,b=>b.Data);
        var payload=ModelPayloadCodec.DecodeMesh(blocks[m.Meshes[0].Mesh]);var skeleton=ModelPayloadCodec.DecodeSkeleton(blocks[m.Skeleton!.Value]);
        string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        using var rig=kernel.CreateRig(skeleton);using var clip=kernel.CreateClip(rig,ModelPayloadCodec.DecodeClip(blocks[m.Clips[0]]));
        using var skin=renderer.CreateSkinnedMesh(SkinUploadData.Prepare(payload));using var ground=renderer.CreateStaticMesh(Quad());
        using var cache=new RenderResourceCache(renderer);var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.6f,.25f,.1f,1),Metallic=.1f,Roughness=.6f};
        using var material=cache.AcquireMaterial(Version(definition),definition,_=>null,true,true,out _);
        using var target=renderer.CreateViewTarget(256,256);using var pipeline=new ScenePipelineSession(renderer,new Scene3DPipeline(),256,256);
        using var plain=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),256,256);
        var camera=new SceneCameraView(Guid.Empty,Matrix4x4.CreateOrthographic(4,4,.1f,50),Vector3.Zero,CameraData.Default);
        var toLight=Vector3.Normalize(new Vector3(1,0,1));var lighting=new ResourceLighting(Vector3.Zero,toLight,new(1,1,1,3));var lightVP=Ncma.Rendering.Scene.ShadowVolume.Create(camera,toLight);
        var groundModel=Matrix4x4.CreateScale(4,4,1)*Matrix4x4.CreateTranslation(0,0,-4.5f);
        var mainModel=Matrix4x4.CreateScale(.8f,1.1f,1)*Matrix4x4.CreateRotationZ(.13f)*Matrix4x4.CreateTranslation(-.7f,.3f,-2);
        var shadowModel=Matrix4x4.CreateScale(1.2f,.8f,1)*Matrix4x4.CreateTranslation(2.2f,0,-.5f);
        var models=new Matrix4x4[rig.BoneCount];var locals=new PoseTrs[rig.BoneCount];var matrices=new Matrix4x4[payload.Bindings.Length];var bindings=new MeshBindingPalette(payload,rig.BoneCount);
        byte[] gpuImage=new byte[256*256*4],cpuImage=new byte[gpuImage.Length],unshadowed=new byte[gpuImage.Length];var rows=new List<object>();int maximumError=0;
        foreach(double time in new[]{0,.5,1}) {
            kernel.Sample(new[]{new PoseSample(rig,clip,new(time,time,1))},locals,models);bindings.Compose(models,matrices);
            var upload=SkinUploadData.Prepare(payload);var cpuVertices=new ImportVertex[payload.Vertices.Length];var tangents=new Vector4[cpuVertices.Length];
            for(int i=0;i<cpuVertices.Length;i++) {
                var v=payload.Vertices[i];Vector3 p=default,n=default,t=default;
                var values=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(upload.Vertices.Slice(i*80+32,16));var sourceT=new Vector4(values[0],values[1],values[2],values[3]);
                Add(v.Joints.X,v.Weights.X);Add(v.Joints.Y,v.Weights.Y);Add(v.Joints.Z,v.Weights.Z);Add(v.Joints.W,v.Weights.W);
                n=Vector3.Normalize(n);t=Vector3.Normalize(t-n*Vector3.Dot(n,t));cpuVertices[i]=v with{Position=p,Normal=n,Joints=default,Weights=default};tangents[i]=new(t,sourceT.W);
                void Add(ushort j,float w){if(w==0)return;p+=Vector3.Transform(v.Position,matrices[j])*w;Matrix4x4.Invert(matrices[j],out var inverse);n+=Vector3.TransformNormal(v.Normal,Matrix4x4.Transpose(inverse))*w;t+=Vector3.TransformNormal(new(sourceT.X,sourceT.Y,sourceT.Z),matrices[j])*w;}
            }
            var bounds=new SkinInfluenceBounds(payload).Evaluate(matrices,mainModel);
            foreach(var vertex in cpuVertices){var position=Vector3.Transform(vertex.Position,mainModel);Check(position.X>=bounds.Min.X-1e-5&&position.Y>=bounds.Min.Y-1e-5&&position.Z>=bounds.Min.Z-1e-5&&position.X<=bounds.Max.X+1e-5&&position.Y<=bounds.Max.Y+1e-5&&position.Z<=bounds.Max.Z+1e-5,"Conservative posed influence bounds did not cover CPU skin oracle");}
            using var cpu=renderer.CreateStaticMesh(MeshUploadData.PrepareStatic(new(false,0,cpuVertices,tangents,payload.Indices,payload.TriangleMaterials,[],1)));
            SceneGpuDraw Draw(GpuMesh mesh,Matrix4x4 model)=>SceneGpuDraw.Create(mesh,material.Resource,upload.Ranges[0],model,camera.ViewProjection);
            SceneGpuDraw groundDraw=SceneGpuDraw.Create(ground,material.Resource,Quad().Ranges[0],groundModel,camera.ViewProjection);
            var settings=new SceneShadowSettings(.0001f,.0001f,false,3);
            ulong before=renderer.PipelineStats.CopiedBytes,invalidFrame=frame;
            Reject(()=>pipeline.Submit(invalidFrame,[groundDraw,Draw(skin,mainModel)],[Draw(skin,shadowModel)],lighting,lightVP,settings,target));
            Check(renderer.PipelineStats.CopiedBytes==before,"Stale animated main/shadow frame performed GPU work");
            renderer.UpdateSkins(frame,[new(skin,0)],matrices.Select(GpuSkinPalette.Create).ToArray());
            pipeline.Submit(frame++,[groundDraw,Draw(skin,mainModel)],[Draw(skin,shadowModel)],lighting,lightVP,settings,target);renderer.CaptureTarget(target,gpuImage);renderer.Present();
            pipeline.Submit(frame++,[groundDraw,Draw(cpu,mainModel)],[Draw(cpu,shadowModel)],lighting,lightVP,settings,target);renderer.CaptureTarget(target,cpuImage);renderer.Present();
            int error=0;for(int i=0;i<gpuImage.Length;i++)error=Math.Max(error,Math.Abs(gpuImage[i]-cpuImage[i]));maximumError=Math.Max(maximumError,error);Check(error<=2,"Animated main/shadow CPU pixel oracle error "+error);
            plain.Submit(frame++,[groundDraw,Draw(cpu,mainModel)],[],lighting,lightVP,settings,target);renderer.CaptureTarget(target,unshadowed);renderer.Present();
            int dark=0;for(int i=0;i<gpuImage.Length;i+=4)if(unshadowed[i]-gpuImage[i]>20)dark++;Check(dark>100,"Animated caster produced no visible shadow: "+dark);
            File.WriteAllBytes(Path.Combine(output,"skin-shadow-"+time.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"),Png(256,256,gpuImage));
            rows.Add(new{time,maximumChannelError=error,shadowPixels=dark});
        }
        File.WriteAllText(Path.Combine(output,"skin-shadow.json"),JsonSerializer.Serialize(new{maximumError,rows,validationErrors=renderer.Stats.ValidationErrors,validationWarnings=renderer.Stats.ValidationWarnings}));
        Console.WriteLine("PASS GPU animated main+off-camera shadow inclusive endpoints, mixed weights/helper/nonuniform object transforms vs CPU image oracle");
    }
}
