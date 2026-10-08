using System.Numerics;
using Ncma.Animation;
using Ncma.Animation.Native;
namespace Ncma.Rendering.Scene;

// Bounded caller-owned scratch, no live World/clock/IO. Native owns only sample/blend numerics.
internal sealed class AnimationGraphPose
{
    private readonly PoseKernel _kernel;
    private readonly PoseRig _rig;
    private readonly IReadOnlyDictionary<Guid, PoseClip> _clips;
    private readonly PoseTrs[] _scratch, _temporary;
    private readonly Matrix4x4[] _models;
    internal readonly AnimationPoseInstruction[] Plan;
    internal AnimationGraphPose(AnimationProgram program, PoseRig rig, IReadOnlyDictionary<Guid, PoseClip> clips, PoseKernel kernel)
    {
        _kernel = kernel; _rig = rig; _clips = clips;
        int capacity = checked(program.MaximumPlanInstructions * rig.BoneCount);
        if (capacity > 65536) throw new ArgumentException("Graph pose scratch capacity.");
        _scratch = new PoseTrs[capacity]; _temporary = new PoseTrs[rig.BoneCount]; _models = new Matrix4x4[rig.BoneCount];
        Plan = new AnimationPoseInstruction[program.MaximumPlanInstructions];
    }
    internal void Evaluate(int count, int output, float alpha, Span<PoseTrs> local, Span<Matrix4x4> model)
    {
        if (count < 1 || count > Plan.Length || output < 0 || output >= count || !float.IsFinite(alpha) || alpha is < 0 or > 1 || local.Length < _rig.BoneCount || model.Length < _rig.BoneCount) throw new ArgumentException("Graph pose plan/capacity.");
        for (int i = 0; i < count; i++) {
            var instruction = Plan[i]; int offset = i * _rig.BoneCount;
            if (instruction.Operation == AnimationPoseOperation.Clip) {
                if (!_clips.TryGetValue(instruction.ClipId, out var clip) || clip.Rig != _rig || clip.Duration != instruction.Duration) throw new ArgumentException("Unpinned graph numerical clip.");
                _sample[0] = new(_rig, clip, new(Wrap(instruction.Previous), Wrap(instruction.Current), alpha));
                _kernel.Sample(_sample, _temporary, _models);
                double Wrap(double time) => instruction.Loop ? time % instruction.Duration : Math.Min(time, instruction.Duration);
            } else if (instruction.Operation == AnimationPoseOperation.Blend && instruction.SourceA >= 0 && instruction.SourceA < i && instruction.SourceB >= 0 && instruction.SourceB < i) {
                _blend[0] = new(_rig, instruction.SourceA * _rig.BoneCount, instruction.SourceB * _rig.BoneCount, instruction.Weight);
                _kernel.Blend(_blend, _scratch.AsSpan(0, count * _rig.BoneCount), _temporary, _models);
            } else throw new ArgumentException("Graph recipe topology.");
            _temporary.CopyTo(_scratch, offset);
        }
        if (output != count - 1) {
            _blend[0] = new(_rig, output * _rig.BoneCount, output * _rig.BoneCount, 0);
            _kernel.Blend(_blend, _scratch.AsSpan(0, count * _rig.BoneCount), _temporary, _models);
        }
        _scratch.AsSpan(output * _rig.BoneCount, _rig.BoneCount).CopyTo(local); _models.AsSpan().CopyTo(model);
    }
    private readonly PoseSample[] _sample = new PoseSample[1];
    private readonly PoseBlend[] _blend = new PoseBlend[1];
}
