#pragma once

#include "foundation/MathTypes.h"
#include "scene/SceneUuid.h"

#include <memory>
#include <span>
#include <string>
#include <vector>

namespace NcmaEngine::Animation
{
    struct Bone final
    {
        std::string Name;
        int Parent = -1; // Parent-before-child order; exactly one root at index zero.
        Transform BindLocal;
    };

    struct Skeleton final
    {
        SceneUuid Id;
        std::vector<Bone> Bones;
    };

    struct TransformKey final { double Time = 0; Transform Value; };
    struct BoneTrack final { std::size_t BoneIndex = 0; std::vector<TransformKey> Keys; };
    struct Notify final { double Time = 0; std::string Name; };

    struct AnimationClip final
    {
        SceneUuid Id;
        SceneUuid SkeletonId;
        std::string Name;
        double Duration = 1;
        bool Loop = true;
        bool ExtractRootMotion = false;
        std::vector<BoneTrack> Tracks;
        std::vector<Notify> Notifies; // (0, Duration], ordered; never evaluated by sampling alone.
    };

    struct FiredNotify final
    {
        SceneUuid ClipId;
        std::string Name;
        double Offset = 0; // Seconds from the beginning of this update, not wall-clock time.
    };

    // Immutable, validated assets shared by all instances. No renderer/platform dependency.
    class AnimationLibrary final
    {
    public:
        AnimationLibrary(Skeleton skeleton, std::vector<AnimationClip> clips);
        [[nodiscard]] const Skeleton& GetSkeleton() const noexcept { return m_Skeleton; }
        [[nodiscard]] const std::vector<AnimationClip>& GetClips() const noexcept { return m_Clips; }
        [[nodiscard]] std::size_t FindClip(const std::string& name) const;
        void Sample(std::size_t clip, double time, std::span<Transform> output, bool removeRootMotion = true) const;
        void BuildMatrices(std::span<const Transform> local, std::span<Matrix4> model,
            std::span<Matrix4> skin) const;
        [[nodiscard]] Transform ExtractMotion(std::size_t clip, double from, double to) const;
        void CollectNotifies(std::size_t clip, double from, double to, std::vector<FiredNotify>& output) const;

    private:
        [[nodiscard]] Transform SampleRoot(std::size_t clip, double time) const;
        Skeleton m_Skeleton;
        std::vector<AnimationClip> m_Clips;
        std::vector<Matrix4> m_InverseBind;
        std::vector<std::vector<int>> m_TrackIndices;
    };

    // Quaternion shortest-path interpolation; optional per-bone weights in [0, 1].
    void BlendPoses(std::span<const Transform> a, std::span<const Transform> b, float weight,
        std::span<Transform> output, std::span<const float> mask = {});

    class AnimationPlayer final
    {
    public:
        explicit AnimationPlayer(std::shared_ptr<const AnimationLibrary> library);
        void Play(const std::string& clip, double blendSeconds = 0.15);
        void Advance(double seconds);
        [[nodiscard]] const AnimationLibrary& Library() const noexcept { return *m_Library; }
        [[nodiscard]] const AnimationClip& Clip() const { return m_Library->GetClips().at(m_Clip); }
        [[nodiscard]] double Time() const noexcept { return m_Time; }
        [[nodiscard]] bool Finished() const { return !Clip().Loop && m_Time >= Clip().Duration; }
        [[nodiscard]] const auto& LocalPose() const noexcept { return m_Pose; }
        [[nodiscard]] const auto& ModelMatrices() const noexcept { return m_Model; }
        [[nodiscard]] const auto& SkinMatrices() const noexcept { return m_Skin; }
        [[nodiscard]] const auto& Events() const noexcept { return m_Events; }
        [[nodiscard]] const Transform& RootDelta() const noexcept { return m_RootDelta; }
        [[nodiscard]] float BlendWeight() const noexcept;

    private:
        void Evaluate();
        std::shared_ptr<const AnimationLibrary> m_Library;
        std::size_t m_Clip = 0;
        double m_Time = 0;
        double m_BlendTime = 0;
        double m_BlendDuration = 0;
        std::vector<Transform> m_Pose, m_FromPose, m_SamplePose;
        std::vector<Matrix4> m_Model, m_Skin;
        std::vector<FiredNotify> m_Events;
        Transform m_RootDelta;
    };
}
