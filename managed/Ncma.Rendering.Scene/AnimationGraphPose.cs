using System.Numerics;
using Ncma.Animation;
using Ncma.Animation.Native;
namespace Ncma.Rendering.Scene;
using Vector3=System.Numerics.Vector3;

// Bounded caller-owned scratch, no live World/clock/IO. Native owns only sample/blend numerics.
internal sealed class AnimationGraphPose
{
    private readonly PoseKernel _kernel;
    private readonly AnimationProgram _program;
    private readonly PoseRig _rig;
    private readonly IReadOnlyDictionary<Guid, PoseClip> _clips;
    private readonly PoseTrs[] _scratch, _temporary;
    private readonly Matrix4x4[] _models;
    private readonly Dictionary<Guid,AnimationLayerBinding> _layers=[];
    private readonly AnimationPoseScratchLayout _layout;
    internal readonly AnimationPoseInstruction[] Plan;
    internal readonly AnimationLocalTransform[] Frozen;
    internal AnimationGraphPose(AnimationProgram program, PoseRig rig, IReadOnlyDictionary<Guid, PoseClip> clips, PoseKernel kernel)
    {
        _program=program;_kernel = kernel; _rig = rig; _clips = clips;
        int capacity = checked(program.MaximumPlanInstructions * rig.BoneCount);
        if (capacity > 65536) throw new ArgumentException("Graph pose scratch capacity.");
        _scratch = new PoseTrs[capacity]; _temporary = new PoseTrs[rig.BoneCount]; _models = new Matrix4x4[rig.BoneCount];
        Plan = new AnimationPoseInstruction[program.MaximumPlanInstructions];
        Frozen=new AnimationLocalTransform[rig.BoneCount];
        _layout=new(program.MaximumPlanInstructions);
        foreach(var l in program.CopyLayers()){if(l.Weights.Length!=rig.BoneCount)throw new ArgumentException("Exact layer rig required.");_layers.Add(l.NodeId,l);}
    }
    internal void Evaluate(int count, int output, float alpha, Span<PoseTrs> local, Span<Matrix4x4> model,ulong frozenGeneration=0)
    {
        if (count < 1 || count > Plan.Length || output < 0 || output >= count || !float.IsFinite(alpha) || alpha is < 0 or > 1 || local.Length < _rig.BoneCount || model.Length < _rig.BoneCount) throw new ArgumentException("Graph pose plan/capacity.");
        AnimationPoseRecipe.Validate(Plan.AsSpan(0,count),output,frozenGeneration);
        _layout.Prepare(Plan.AsSpan(0,count),output);
        for (int i = 0; i < count; i++) {
            var instruction = Plan[i]; int offset = _layout.Slot(i) * _rig.BoneCount;
            if (instruction.Operation == AnimationPoseOperation.Clip) {
                if (instruction.CacheGeneration!=0 || !_clips.TryGetValue(instruction.ClipId, out var clip) || clip.Rig != _rig || clip.Duration != instruction.Duration) throw new ArgumentException("Unpinned graph numerical clip.");
                _sample[0] = new(_rig, clip, new(Wrap(instruction.Previous), Wrap(instruction.Current), alpha));
                _kernel.Sample(_sample, _temporary, _models);
                double Wrap(double time) => instruction.Loop ? time % instruction.Duration : Math.Min(time, instruction.Duration);
            } else if (instruction.Operation is AnimationPoseOperation.Blend or AnimationPoseOperation.Slot && instruction.CacheGeneration==0 && instruction.SourceA >= 0 && instruction.SourceA < i && instruction.SourceB >= 0 && instruction.SourceB < i) {
                if(instruction.Operation==AnimationPoseOperation.Slot)_program.ValidateSlotPose(instruction,Plan[instruction.SourceB]);
                _blend[0] = new(_rig, _layout.Slot(instruction.SourceA) * _rig.BoneCount, _layout.Slot(instruction.SourceB) * _rig.BoneCount, instruction.Weight);
                _kernel.Blend(_blend, _scratch.AsSpan(0, count * _rig.BoneCount), _temporary, _models);
            } else if(instruction.Operation is AnimationPoseOperation.LayerOverride or AnimationPoseOperation.LayerAdditive){
                if(!_layers.TryGetValue(instruction.NodeId,out var layer)||instruction.Operation==AnimationPoseOperation.LayerAdditive&&(Plan[instruction.SourceC].ClipId!=layer.Definition.ReferenceClip||Plan[instruction.SourceC].Current!=layer.Definition.ReferenceTime))throw new ArgumentException("Exact bound layer/reference required.");
                _layer[0]=new(_rig,_layout.Slot(instruction.SourceA)*_rig.BoneCount,_layout.Slot(instruction.SourceB)*_rig.BoneCount,instruction.SourceC<0?0:_layout.Slot(instruction.SourceC)*_rig.BoneCount,0,instruction.Weight,instruction.Operation==AnimationPoseOperation.LayerOverride?PoseLayerMode.Override:PoseLayerMode.Additive);
                _kernel.Layer(_layer,_scratch.AsSpan(0,count*_rig.BoneCount),layer.Weights,_temporary,_models);
            } else if(instruction.Operation==AnimationPoseOperation.RootSource&&instruction.CacheGeneration==0&&instruction.SourceA>=0&&instruction.SourceA<i&&instruction.SourceB>=0&&instruction.SourceB<i&&Plan[instruction.SourceB].Operation==AnimationPoseOperation.Clip&&instruction.ClipId==Guid.Empty&&instruction.Previous==0&&instruction.Current==0&&instruction.Duration==0&&!instruction.Loop&&instruction.Weight==0) {
                _blend[0]=new(_rig,_layout.Slot(instruction.SourceA)*_rig.BoneCount,_layout.Slot(instruction.SourceA)*_rig.BoneCount,0);_kernel.Blend(_blend,_scratch.AsSpan(0,count*_rig.BoneCount),_temporary,_models);
            } else if(instruction.Operation==AnimationPoseOperation.Frozen&&instruction.CacheGeneration!=0&&instruction.CacheGeneration==frozenGeneration&&instruction.SourceA==-1&&instruction.SourceB==-1&&instruction.ClipId==Guid.Empty&&instruction.Previous==0&&instruction.Current==0&&instruction.Duration==0&&!instruction.Loop&&instruction.Weight==0) {
                for(int b=0;b<Frozen.Length;b++){var v=Frozen[b];_scratch[offset+b]=new(v.Position,v.Rotation,new Vector3(v.Scale));}
                _blend[0]=new(_rig,offset,offset,0);_kernel.Blend(_blend,_scratch.AsSpan(0,count*_rig.BoneCount),_temporary,_models);
            } else throw new ArgumentException("Graph recipe topology.");
            _temporary.CopyTo(_scratch, offset);
        }
        if (output != count - 1) {
            _blend[0] = new(_rig, _layout.Slot(output) * _rig.BoneCount, _layout.Slot(output) * _rig.BoneCount, 0);
            _kernel.Blend(_blend, _scratch.AsSpan(0, count * _rig.BoneCount), _temporary, _models);
        }
        _scratch.AsSpan(_layout.Slot(output) * _rig.BoneCount, _rig.BoneCount).CopyTo(local); _models.AsSpan().CopyTo(model);
    }
    private readonly PoseSample[] _sample = new PoseSample[1];
    private readonly PoseBlend[] _blend = new PoseBlend[1];
    private readonly PoseLayer[] _layer=new PoseLayer[1];
}
