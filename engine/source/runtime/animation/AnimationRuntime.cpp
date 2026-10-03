#include "animation/AnimationRuntime.h"

#include <algorithm>
#include <cmath>
#include <stdexcept>
#include <unordered_set>

namespace NcmaEngine::Animation
{
    namespace
    {
        void Require(bool condition, const char* message)
        {
            if (!condition) throw std::invalid_argument(message);
        }

        bool ValidTransform(const Transform& value)
        {
            return value.Position.allFinite() && value.Scale.allFinite() &&
                (value.Scale.array() > 0.000001F).all() && value.Rotation.coeffs().allFinite() &&
                std::abs(value.Rotation.norm() - 1.0F) < 0.001F;
        }

        Transform Mix(const Transform& a, const Transform& b, float weight)
        {
            Transform result;
            result.Position = a.Position + weight * (b.Position - a.Position);
            result.Scale = a.Scale + weight * (b.Scale - a.Scale);
            result.Rotation = a.Rotation.slerp(weight, b.Rotation).normalized();
            return result;
        }

        Transform SampleTrack(const BoneTrack& track, double time)
        {
            const auto right = std::upper_bound(track.Keys.begin(), track.Keys.end(), time,
                [](double t, const TransformKey& key) { return t < key.Time; });
            if (right == track.Keys.begin()) return right->Value;
            if (right == track.Keys.end()) return track.Keys.back().Value;
            const auto& left = *(right - 1);
            return Mix(left.Value, right->Value, static_cast<float>((time - left.Time) / (right->Time - left.Time)));
        }

        Transform RelativeMotion(const Transform& from, const Transform& to)
        {
            Transform result;
            result.Rotation = (from.Rotation.conjugate() * to.Rotation).normalized();
            result.Position = from.Rotation.conjugate() * (to.Position - from.Position);
            return result;
        }

        void ValidateInterval(const AnimationClip& clip, double from, double to)
        {
            Require(std::isfinite(from) && std::isfinite(to) && from >= 0 && to >= from &&
                to <= 1.0e9 && to - from <= clip.Duration * 64, "Invalid animation interval (maximum 64 cycles per update)");
        }
    }

    AnimationLibrary::AnimationLibrary(Skeleton skeleton, std::vector<AnimationClip> clips)
        : m_Skeleton(std::move(skeleton)), m_Clips(std::move(clips))
    {
        Require(m_Skeleton.Id.IsValid() && !m_Skeleton.Bones.empty() && m_Skeleton.Bones.size() <= 1024,
            "Skeleton requires a UUID and 1..1024 bones");
        Require(!m_Clips.empty(), "Animation library must contain clips");
        std::unordered_set<std::string> names, ids;
        std::vector<Matrix4> bindModel(m_Skeleton.Bones.size());
        m_InverseBind.resize(m_Skeleton.Bones.size());
        for (std::size_t i = 0; i < m_Skeleton.Bones.size(); ++i)
        {
            const auto& bone = m_Skeleton.Bones[i];
            Require(!bone.Name.empty() && names.insert(bone.Name).second, "Bone names must be unique and non-empty");
            Require(i == 0 ? bone.Parent == -1 : bone.Parent >= 0 && static_cast<std::size_t>(bone.Parent) < i,
                "Skeleton must have one root and parent-before-child ordering");
            Require(ValidTransform(bone.BindLocal), "Invalid bone bind transform");
            bindModel[i] = bone.Parent < 0 ? bone.BindLocal.ToMatrix() :
                (bindModel[static_cast<std::size_t>(bone.Parent)] * bone.BindLocal.ToMatrix()).eval();
            Require(bindModel[i].allFinite(), "Bone bind hierarchy overflow");
            m_InverseBind[i] = bindModel[i].inverse();
            Require(m_InverseBind[i].allFinite(), "Bone inverse bind matrix is not finite");
        }
        names.clear();
        for (const auto& clip : m_Clips)
        {
            Require(clip.Id.IsValid() && ids.insert(clip.Id.ToString()).second && clip.SkeletonId == m_Skeleton.Id,
                "Clip UUID must be unique and target this skeleton");
            Require(!clip.Name.empty() && names.insert(clip.Name).second, "Clip names must be unique and non-empty");
            Require(std::isfinite(clip.Duration) && clip.Duration >= 0.001 && clip.Duration <= 3600,
                "Clip duration must be finite and within 0.001..3600 seconds");
            if (clip.ExtractRootMotion)
                Require(m_Skeleton.Bones[0].BindLocal.Scale.isApprox(Vector3::Ones()),
                    "Root motion requires unit root bind scale");
            std::vector<int> indices(m_Skeleton.Bones.size(), -1);
            for (std::size_t index = 0; index < clip.Tracks.size(); ++index)
            {
                const auto& track = clip.Tracks[index];
                Require(track.BoneIndex < indices.size() && indices[track.BoneIndex] == -1 && !track.Keys.empty(),
                    "Tracks require a unique valid bone index and non-empty keys");
                indices[track.BoneIndex] = static_cast<int>(index);
                double previous = -1;
                for (const auto& key : track.Keys)
                {
                    Require(std::isfinite(key.Time) && key.Time >= 0 && key.Time > previous &&
                        key.Time <= clip.Duration && ValidTransform(key.Value), "Invalid or unordered animation keys");
                    if (clip.ExtractRootMotion && track.BoneIndex == 0)
                        Require(key.Value.Scale.isApprox(Vector3::Ones()), "Root motion does not support animated root scale");
                    previous = key.Time;
                }
            }
            double previous = 0;
            Require(clip.Notifies.size() <= 256, "At most 256 notifies per clip");
            for (const auto& notify : clip.Notifies)
            {
                Require(std::isfinite(notify.Time) && notify.Time > 0 && notify.Time >= previous &&
                    notify.Time <= clip.Duration && !notify.Name.empty(), "Notifies must be ordered in (0, duration]");
                previous = notify.Time;
            }
            m_TrackIndices.push_back(std::move(indices));
        }
    }

    std::size_t AnimationLibrary::FindClip(const std::string& name) const
    {
        for (std::size_t i = 0; i < m_Clips.size(); ++i)
            if (m_Clips[i].Name == name) return i;
        throw std::invalid_argument("Unknown animation clip: " + name);
    }

    void AnimationLibrary::Sample(std::size_t index, double time, std::span<Transform> output, bool removeRootMotion) const
    {
        const auto& clip = m_Clips.at(index);
        Require(std::isfinite(time) && time >= 0 && output.size() == m_Skeleton.Bones.size(), "Invalid pose sample input");
        time = clip.Loop ? std::fmod(time, clip.Duration) : std::min(time, clip.Duration);
        for (std::size_t i = 0; i < output.size(); ++i)
        {
            const int track = m_TrackIndices[index][i];
            output[i] = track < 0 ? m_Skeleton.Bones[i].BindLocal : SampleTrack(clip.Tracks[static_cast<std::size_t>(track)], time);
        }
        if (removeRootMotion && clip.ExtractRootMotion)
        {
            output[0].Position = m_Skeleton.Bones[0].BindLocal.Position;
            output[0].Rotation = m_Skeleton.Bones[0].BindLocal.Rotation;
        }
    }

    void AnimationLibrary::BuildMatrices(std::span<const Transform> local, std::span<Matrix4> model,
        std::span<Matrix4> skin) const
    {
        Require(local.size() == m_Skeleton.Bones.size() && model.size() == local.size() && skin.size() == local.size(),
            "Pose buffer size does not match skeleton");
        for (std::size_t i = 0; i < local.size(); ++i)
        {
            const int parent = m_Skeleton.Bones[i].Parent;
            model[i] = parent < 0 ? local[i].ToMatrix() : (model[static_cast<std::size_t>(parent)] * local[i].ToMatrix()).eval();
            skin[i] = model[i] * m_InverseBind[i];
        }
    }

    Transform AnimationLibrary::SampleRoot(std::size_t clip, double time) const
    {
        const int track = m_TrackIndices.at(clip)[0];
        return track < 0 ? m_Skeleton.Bones[0].BindLocal : SampleTrack(m_Clips[clip].Tracks[static_cast<std::size_t>(track)], time);
    }

    Transform AnimationLibrary::ExtractMotion(std::size_t index, double from, double to) const
    {
        const auto& clip = m_Clips.at(index);
        ValidateInterval(clip, from, to);
        Transform result;
        if (!clip.ExtractRootMotion || from == to) return result;
        if (!clip.Loop) return RelativeMotion(SampleRoot(index, std::min(from, clip.Duration)),
            SampleRoot(index, std::min(to, clip.Duration)));
        const auto firstCycle = static_cast<std::uint64_t>(std::floor(from / clip.Duration));
        const auto lastCycle = static_cast<std::uint64_t>(std::floor(to / clip.Duration));
        for (auto cycle = firstCycle; cycle <= lastCycle; ++cycle)
        {
            const double origin = static_cast<double>(cycle) * clip.Duration;
            const double begin = std::clamp(from - origin, 0.0, clip.Duration);
            const double end = std::clamp(to - origin, 0.0, clip.Duration);
            result = Transform::Combine(result, RelativeMotion(SampleRoot(index, begin), SampleRoot(index, end)));
        }
        return result;
    }

    void AnimationLibrary::CollectNotifies(std::size_t index, double from, double to, std::vector<FiredNotify>& output) const
    {
        const auto& clip = m_Clips.at(index);
        ValidateInterval(clip, from, to);
        output.clear();
        const auto first = clip.Loop ? static_cast<std::uint64_t>(std::floor(from / clip.Duration)) : 0;
        const auto last = clip.Loop ? static_cast<std::uint64_t>(std::floor(to / clip.Duration)) : 0;
        for (auto cycle = first; cycle <= last; ++cycle)
        {
            for (const auto& notify : clip.Notifies)
            {
                const double occurrence = static_cast<double>(cycle) * clip.Duration + notify.Time;
                if (occurrence > from && occurrence <= to)
                    output.push_back({clip.Id, notify.Name, occurrence - from});
            }
        }
    }

    void BlendPoses(std::span<const Transform> a, std::span<const Transform> b, float weight,
        std::span<Transform> output, std::span<const float> mask)
    {
        Require(a.size() == b.size() && output.size() == a.size() && (mask.empty() || mask.size() == a.size()) &&
            std::isfinite(weight) && weight >= 0 && weight <= 1, "Invalid blend inputs");
        for (const float value : mask)
            Require(std::isfinite(value) && value >= 0 && value <= 1, "Invalid bone mask");
        for (std::size_t i = 0; i < a.size(); ++i)
            output[i] = Mix(a[i], b[i], weight * (mask.empty() ? 1.0F : mask[i]));
    }

    AnimationPlayer::AnimationPlayer(std::shared_ptr<const AnimationLibrary> library) : m_Library(std::move(library))
    {
        Require(m_Library != nullptr, "Animation library is null");
        const auto count = m_Library->GetSkeleton().Bones.size();
        m_Pose.resize(count); m_FromPose.resize(count); m_SamplePose.resize(count);
        m_Model.resize(count); m_Skin.resize(count);
        Evaluate();
    }

    void AnimationPlayer::Play(const std::string& clip, double blendSeconds)
    {
        Require(std::isfinite(blendSeconds) && blendSeconds >= 0 && blendSeconds <= 5, "Invalid crossfade duration");
        const auto target = m_Library->FindClip(clip);
        // Cache the actually visible pose, including an interrupted transition: no pop on interruption.
        m_FromPose = m_Pose;
        m_Clip = target; m_Time = 0; m_BlendTime = 0; m_BlendDuration = blendSeconds;
        m_RootDelta = {}; m_Events.clear();
        Evaluate();
    }

    float AnimationPlayer::BlendWeight() const noexcept
    {
        return m_BlendDuration == 0 ? 1.0F : static_cast<float>(std::min(1.0, m_BlendTime / m_BlendDuration));
    }

    void AnimationPlayer::Evaluate()
    {
        m_Library->Sample(m_Clip, m_Time, m_SamplePose);
        BlendPoses(m_FromPose, m_SamplePose, BlendWeight(), m_Pose);
        m_Library->BuildMatrices(m_Pose, m_Model, m_Skin);
    }

    void AnimationPlayer::Advance(double seconds)
    {
        Require(std::isfinite(seconds) && seconds >= 0 && seconds <= 1, "Animation update must be within 0..1 seconds");
        const double next = Clip().Loop ? m_Time + seconds : std::min(m_Time + seconds, Clip().Duration);
        const Transform motion = m_Library->ExtractMotion(m_Clip, m_Time, next);
        m_Library->CollectNotifies(m_Clip, m_Time, next, m_Events);
        m_RootDelta = motion;
        // Keep loop clocks bounded, avoiding precision loss during long-running sessions.
        m_Time = Clip().Loop ? std::fmod(next, Clip().Duration) : next;
        m_BlendTime = std::min(m_BlendTime + seconds, m_BlendDuration);
        Evaluate();
    }
}
