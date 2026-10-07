using System.Numerics;
using Ncma.Animation.Native;
using Ncma.Assets;

internal static class PoseBlendTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Pose blend assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new Exception("Invalid pose mix accepted."); }
    private static Matrix4x4 Matrix(PoseTrs t) => Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Position);
    private static bool Near(Matrix4x4 a, Matrix4x4 b) { var difference = new[] { a.M11-b.M11,a.M12-b.M12,a.M13-b.M13,a.M21-b.M21,a.M22-b.M22,a.M23-b.M23,a.M31-b.M31,a.M32-b.M32,a.M33-b.M33,a.M41-b.M41,a.M42-b.M42,a.M43-b.M43 }; return difference.All(v=>Math.Abs(v)<.0001f); }
    internal static void Add(List<(string,Action)> cases,string path,string hash)
    {
        cases.Add(("M6.3-A independent optional blend API, shortest TRS and parent composition oracle",()=>{
            using var kernel=new PoseKernel(path,hash,blendSupport:true);
            var bind=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);
            using var rig=kernel.CreateRig(new([new("root",-1,bind),new("child",0,bind)]));
            var input=new PoseTrs[]{new(new(1,2,3),Quaternion.CreateFromAxisAngle(Vector3.UnitY,.2f),new(1)),new(Vector3.UnitX,Quaternion.Identity,new(1)),
                new(new(3,4,5),Quaternion.CreateFromAxisAngle(Vector3.UnitY,1.2f),new(2)),new(Vector3.UnitY,Quaternion.Identity,new(2))};
            var output=new PoseTrs[2];var model=new Matrix4x4[2];var blends=new[]{new PoseBlend(rig,0,2,.5f)};
            Check(kernel.Blend(blends,input,output,model)==2);
            var expected=new PoseTrs(Vector3.Lerp(input[0].Position,input[2].Position,.5f),Quaternion.Slerp(input[0].Rotation,input[2].Rotation,.5f),new(1.5f));
            var child=new PoseTrs(new(.5f,.5f,0),Quaternion.Identity,new(1.5f));
            Check(Near(model[0],Matrix(expected))&&Near(model[1],Matrix(child)*Matrix(expected)));
            input[2]=input[2] with{Rotation=new(-input[2].Rotation.X,-input[2].Rotation.Y,-input[2].Rotation.Z,-input[2].Rotation.W)};
            kernel.Blend(blends,input,output,model);Check(Near(model[0],Matrix(expected)));
            foreach(float weight in new[]{0f,1f}){blends[0]=blends[0] with{Weight=weight};kernel.Blend(blends,input,output,model);Check(Near(model[0],Matrix(input[weight==0?0:2])));}
            Reject(()=>kernel.Dispose());
        }));
        cases.Add(("M6.3-A blend whole-batch rejection, aliasing, foreign/stale and owner thread",()=>{
            using var kernel=new PoseKernel(path,hash,blendSupport:true);var bind=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);
            using var rig=kernel.CreateRig(new([new("root",-1,bind)]));var input=new[]{new PoseTrs(Vector3.Zero,Quaternion.Identity,Vector3.One),new PoseTrs(Vector3.UnitY,Quaternion.Identity,Vector3.One)};
            var blends=new[]{new PoseBlend(rig,0,1,.5f),new PoseBlend(rig,1,0,.5f)};var output=new PoseTrs[2];var model=new Matrix4x4[2];model[0]=Matrix4x4.CreateTranslation(77,0,0);
            ulong calls=kernel.BlendStatistics.BlendCalls;input[1]=input[1] with{Scale=new(1,2,1)};
            Reject(()=>kernel.Blend(blends,input,output,model));Check(model[0].M41==77&&kernel.BlendStatistics.BlendCalls==calls);
            input[1]=input[1] with{Scale=Vector3.One};Reject(()=>kernel.Blend(blends,input,input,model));
            Reject(()=>kernel.Blend([blends[0] with{SourceB=2}],input,output,model));Reject(()=>kernel.Blend([blends[0] with{Weight=float.NaN}],input,output,model));
            Check(Task.Run(()=>{try{kernel.Blend(blends,input,output,model);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());
            using var other=new PoseKernel(path,hash,blendSupport:true);Reject(()=>other.Blend(blends,input,output,model));
            rig.Dispose();Reject(()=>kernel.Blend(blends,input,output,model));Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&kernel.Statistics.RetainedBytes==0);
        }));
        cases.Add(("M6.3-A explicit negotiation, 32 by 1024 batch and warm zero-allocation mix",()=>{
            using(var disabled=new PoseKernel(path,hash)){Reject(()=>{_ = disabled.BlendStatistics;});}
            using var kernel=new PoseKernel(path,hash,blendSupport:true);
            var bones=Enumerable.Range(0,1024).Select(i=>new ImportBone("bone",i-1,new(Vector3.Zero,Quaternion.Identity,Vector3.One))).ToArray();
            using var rig=kernel.CreateRig(new(bones));var input=Enumerable.Repeat(new PoseTrs(Vector3.Zero,Quaternion.Identity,Vector3.One),2048).ToArray();
            var batch=Enumerable.Repeat(new PoseBlend(rig,0,1024,.5f),32).ToArray();var local=new PoseTrs[32768];var models=new Matrix4x4[32768];
            for(int i=0;i<8;i++)Check(kernel.Blend(batch,input,local,models)==32768);long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<32;i++)kernel.Blend(batch,input,local,models);Check(GC.GetAllocatedBytesForCurrentThread()==before&&models[^1]==Matrix4x4.Identity);
            Reject(()=>kernel.Blend(batch.Append(batch[0]).ToArray(),input,local,models));
            Check(kernel.BlendStatistics.BlendCalls==40&&kernel.BlendStatistics.BlendedBones==40ul*32768);
        }));
    }
}
