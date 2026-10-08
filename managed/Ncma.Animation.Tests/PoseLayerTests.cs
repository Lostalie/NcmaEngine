using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;

internal static class PoseLayerTests
{
    private static void Check(bool v,string reason=""){if(!v)throw new Exception("M6.7-A layer: "+reason);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or NotSupportedException){return;}throw new Exception("Invalid layer accepted.");}
    private static Matrix4x4 Matrix(PoseTrs p)=>Matrix4x4.CreateScale(p.Scale)*Matrix4x4.CreateFromQuaternion(p.Rotation)*Matrix4x4.CreateTranslation(p.Position);
    private static bool Near(Matrix4x4 a,Matrix4x4 b){ReadOnlySpan<Matrix4x4> x=[a],y=[b];var xs=MemoryMarshal.Cast<Matrix4x4,float>(x);var ys=MemoryMarshal.Cast<Matrix4x4,float>(y);for(int i=0;i<16;i++)if(Math.Abs(xs[i]-ys[i])>1e-4+Math.Abs(ys[i])*1e-5)return false;return true;}
    internal static void Add(List<(string,Action)> tests,string path,string hash)
    {
        tests.Add(("M6.7-A exact hash/full-path masks, copied weights/root forbidden/reimport ambiguity",()=>{
            Guid skeleton=Guid.NewGuid();string content=new('A',64);var bones=new[]{new AnimationBoneIdentity("root",-1),new AnimationBoneIdentity("root/arm",0),new AnimationBoneIdentity("root/leg",0),new AnimationBoneIdentity("root/leg/arm",2)};
            var mask=new AnimationBoneMask(Guid.NewGuid(),skeleton,content,[new("root/arm",1),new("root/leg/arm",.25f)]);var p=new AnimationBoneMaskProgram(mask,skeleton,content,bones);float[] weights=new float[4];p.CopyWeights(weights);Check(weights.SequenceEqual(new[]{0,1,0,.25f}));
            mask.Bones[0]=new("root/arm",0);p.CopyDefinition().Bones[0]=new("root/arm",0);p.CopyWeights(weights);Check(weights[1]==1);
            Reject(()=>new AnimationBoneMaskProgram(mask,skeleton,new('B',64),bones));Reject(()=>new AnimationBoneMaskProgram(mask,Guid.NewGuid(),content,bones));Reject(()=>new AnimationBoneMaskProgram(mask with{Bones=[new("arm",1)]},skeleton,content,bones));
            Reject(()=>new AnimationBoneMaskProgram(mask with{Bones=[new("root",1)]},skeleton,content,bones));Reject(()=>new AnimationBoneMaskProgram(mask with{Bones=[new("root/arm",1),new("root/arm",0)]},skeleton,content,bones));
            Reject(()=>new AnimationBoneMaskProgram(mask with{Bones=[new("root/../arm",1)]},skeleton,content,bones));Reject(()=>new AnimationBoneMaskProgram(mask with{Bones=[new("root/arm",float.NaN)]},skeleton,content,bones));
            Reject(()=>new AnimationBoneMaskProgram(mask,skeleton,content,[bones[0],bones[1],bones[1]]));Reject(()=>p.CopyWeights(new float[3]));
        }));
        tests.Add(("M6.7-A optional native override and additive explicit reference/shortest rotation independent oracle",()=>{
            using var kernel=new PoseKernel(path,hash,layerSupport:true);var bind=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);using var rig=kernel.CreateRig(new([new("root",-1,bind),new("arm",0,bind)]));
            PoseTrs Root(float x)=>new(new(x,0,0),Quaternion.Identity,Vector3.One);
            var a=new PoseTrs(new(1,2,3),Quaternion.CreateFromAxisAngle(Vector3.UnitX,.4f),new(1.5f));var b=new PoseTrs(new(4,5,6),Quaternion.CreateFromAxisAngle(Vector3.UnitY,1),new(2));var reference=new PoseTrs(new(2,1,3),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.3f),new(.5f));
            var input=new[]{Root(1),a,Root(3),b,Root(2),reference};float[] mask=[0,.5f];var local=new PoseTrs[2];var model=new Matrix4x4[2];
            foreach(var mode in Enum.GetValues<PoseLayerMode>())foreach(float weight in new[]{0f,.5f,1f}) {
                kernel.Layer([new(rig,0,2,mode==PoseLayerMode.Additive?4:0,0,weight,mode)],input,mask,local,model);float w=weight*.5f;
                var expected=mode==PoseLayerMode.Override?new PoseTrs(Vector3.Lerp(a.Position,b.Position,w),Quaternion.Slerp(a.Rotation,b.Rotation,w),Vector3.Lerp(a.Scale,b.Scale,w)):
                    new(a.Position+(b.Position-reference.Position)*w,Quaternion.Normalize(a.Rotation*Quaternion.Slerp(Quaternion.Identity,Quaternion.Normalize(Quaternion.Inverse(reference.Rotation)*b.Rotation),w)),a.Scale*(Vector3.One+(b.Scale/reference.Scale-Vector3.One)*w));
                Check(local[0].Position==input[0].Position&&Near(model[0],Matrix(input[0]))&&Near(model[1],Matrix(expected)*Matrix(input[0])),mode+" matrix oracle");
            }
            input[3]=b with{Rotation=new(-b.Rotation.X,-b.Rotation.Y,-b.Rotation.Z,-b.Rotation.W)};kernel.Layer([new(rig,0,2,4,0,1,PoseLayerMode.Additive)],input,mask,local,model);
            var negative=local[1];input[3]=b;kernel.Layer([new(rig,0,2,4,0,1,PoseLayerMode.Additive)],input,mask,local,model);Check(Near(Matrix(negative),Matrix(local[1])),"Antipodal shortest rotation");
            Reject(kernel.Dispose);Check(kernel.Statistics.SampleCalls==0&&kernel.BlendStatisticsDisabled(),"Layer does not change original sampling counters");
        }));
        tests.Add(("M6.7-A layer invalid complete batch leaves outputs/counters, alias/foreign/owner/dispose",()=>{
            using var kernel=new PoseKernel(path,hash,layerSupport:true);var bind=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);using var rig=kernel.CreateRig(new([new("root",-1,bind)]));var pose=new[]{new PoseTrs(Vector3.Zero,Quaternion.Identity,Vector3.One),new PoseTrs(Vector3.One,Quaternion.Identity,Vector3.One),new PoseTrs(Vector3.Zero,Quaternion.Identity,Vector3.One)};
            var layers=new[]{new PoseLayer(rig,0,1,2,0,.5f,PoseLayerMode.Additive),new PoseLayer(rig,0,1,0,0,.5f,PoseLayerMode.Override)};float[] weights=[1];var output=new PoseTrs[2];var models=new Matrix4x4[2];models[0]=Matrix4x4.CreateTranslation(77,0,0);ulong calls=kernel.LayerStatistics.LayerCalls;
            weights[0]=float.NaN;Reject(()=>kernel.Layer(layers,pose,weights,output,models));weights[0]=1;
            pose[2]=pose[2] with{Scale=new(1,2,1)};Reject(()=>kernel.Layer(layers,pose,weights,output,models));pose[2]=pose[2] with{Scale=Vector3.One};
            Reject(()=>kernel.Layer([layers[0],layers[1] with{Weight=float.NaN}],pose,weights,output,models));Reject(()=>kernel.Layer([layers[0] with{Mode=(PoseLayerMode)5}],pose,weights,output,models));
            Reject(()=>kernel.Layer([layers[0] with{Reference=int.MaxValue}],pose,weights,output,models));Reject(()=>kernel.Layer([layers[1] with{Reference=1}],pose,weights,output,models));Reject(()=>kernel.Layer(layers,pose,weights,pose,models));
            Check(models[0].M41==77&&kernel.LayerStatistics.LayerCalls==calls);Check(Task.Run(()=>{try{kernel.Layer(layers,pose,weights,output,models);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());
            using(var other=new PoseKernel(path,hash,layerSupport:true))Reject(()=>other.Layer(layers,pose,weights,output,models));rig.Dispose();Reject(()=>kernel.Layer(layers,pose,weights,output,models));
            using var disabled=new PoseKernel(path,hash);Reject(()=>{_=disabled.LayerStatistics;});
        }));
        tests.Add(("M6.7-A layer full32x1024 budget, bounded positive uniform scale and warm zero allocations",()=>{
            using var kernel=new PoseKernel(path,hash,layerSupport:true);var bind=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);using var rig=kernel.CreateRig(new(Enumerable.Range(0,1024).Select(i=>new ImportBone("bone",i-1,bind)).ToArray()));
            var pose=Enumerable.Repeat(new PoseTrs(Vector3.Zero,Quaternion.Identity,Vector3.One),3072).ToArray();float[] weights=Enumerable.Repeat(.5f,1024).ToArray();var batch=Enumerable.Repeat(new PoseLayer(rig,0,1024,2048,0,1,PoseLayerMode.Additive),32).ToArray();var local=new PoseTrs[32768];var model=new Matrix4x4[32768];
            for(int i=0;i<8;i++)Check(kernel.Layer(batch,pose,weights,local,model)==32768);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<32;i++)kernel.Layer(batch,pose,weights,local,model);Check(GC.GetAllocatedBytesForCurrentThread()==before&&model[^1]==Matrix4x4.Identity);
            Reject(()=>kernel.Layer(batch.Append(batch[0]).ToArray(),pose,weights,local,model));Check(kernel.LayerStatistics.LayerCalls==40&&kernel.LayerStatistics.LayeredBones==40ul*32768);
            pose[1024]=pose[1024] with{Scale=new(1000)};pose[2048]=pose[2048] with{Scale=new(.00001f)};local[0]=new(new(77),Quaternion.Identity,Vector3.One);Reject(()=>kernel.Layer(batch,pose,weights,local,model));Check(local[0].Position==new Vector3(77)&&kernel.LayerStatistics.LayerCalls==40,"Finite scale ratio overflow rejects entire batch");
        }));
    }
    private static bool BlendStatisticsDisabled(this PoseKernel kernel){try{_=kernel.BlendStatistics;return false;}catch(NotSupportedException){return true;}}
}
