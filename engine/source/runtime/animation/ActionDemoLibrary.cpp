#include "animation/ActionDemoLibrary.h"
#include <Eigen/Geometry>
namespace NcmaEngine::Animation {
namespace {
SceneUuid Id(const char* value) { return SceneUuid::Parse(value).value(); }
Transform At(float x,float y,float z,float roll=0) {
Transform t; t.Position={x,y,z}; t.Rotation=Quaternion(Eigen::AngleAxisf(roll,Vector3::UnitZ())); return t;
}
}
    std::shared_ptr<const AnimationLibrary> CreateActionDemoLibrary()
    {
        static const auto library = [] {
            Skeleton skeleton{Id("51bb55ce-7181-4c1d-9913-431e27c2a001"), {
                {"Root", -1, At(0, 0, 0)}, {"Hips", 0, At(0, 0.95F, 0)},
                {"Spine", 1, At(0, 0.35F, 0)}, {"Head", 2, At(0, 0.4F, 0)},
                {"Arm.L", 2, At(-0.3F, 0.18F, 0)}, {"Hand.L", 4, At(-0.4F, 0, 0)},
                {"Arm.R", 2, At(0.3F, 0.18F, 0)}, {"Hand.R", 6, At(0.4F, 0, 0)},
                {"Leg.L", 1, At(-0.17F, -0.08F, 0)}, {"Foot.L", 8, At(0, -0.85F, 0)},
                {"Leg.R", 1, At(0.17F, -0.08F, 0)}, {"Foot.R", 10, At(0, -0.85F, 0)}
            }};
            AnimationClip idle{Id("51bb55ce-7181-4c1d-9913-431e27c2a002"), skeleton.Id, "Idle", 2, true, false, {}, {}};
            idle.Tracks.push_back({2, {{0, At(0, 0.35F, 0)}, {1, At(0, 0.37F, 0, 0.03F)}, {2, At(0, 0.35F, 0)}}});
            AnimationClip run{Id("51bb55ce-7181-4c1d-9913-431e27c2a003"), skeleton.Id, "Run", 1, true, true, {}, {}};
            run.Tracks.push_back({0, {{0, At(0, 0, 0)}, {1, At(0, 0, 2)}}});
            for (const std::size_t bone : {4U, 6U, 8U, 10U})
            {
                const Transform bind = skeleton.Bones[bone].BindLocal;
                Transform left = bind, right = bind;
                const float sign = (bone == 4 || bone == 10) ? 1.0F : -1.0F;
                left.Rotation = Quaternion(Eigen::AngleAxisf(sign * 0.65F, Vector3::UnitX()));
                right.Rotation = left.Rotation.conjugate();
                run.Tracks.push_back({bone, {{0, left}, {0.5, right}, {1, left}}});
            }
            run.Notifies = {{0.25, "Footstep.L"}, {0.75, "Footstep.R"}};
            AnimationClip attack{Id("51bb55ce-7181-4c1d-9913-431e27c2a004"), skeleton.Id, "Attack", 0.8, false, true, {}, {}};
            attack.Tracks.push_back({0, {{0, At(0, 0, 0)}, {0.32, At(0, 0, 0.8F)}, {0.8, At(0, 0, 0.8F)}}});
            attack.Tracks.push_back({2, {{0, At(0, 0.35F, 0)}, {0.22, At(0, 0.35F, 0, -0.5F)}, {0.8, At(0, 0.35F, 0)}}});
            attack.Tracks.push_back({6, {{0, At(0.3F, 0.18F, 0)}, {0.22, At(0.3F, 0.18F, 0, -1.3F)}, {0.8, At(0.3F, 0.18F, 0)}}});
            attack.Notifies = {{0.18, "Hit.Start"}, {0.32, "Hit.End"}, {0.42, "Combo.Open"}, {0.62, "Combo.Close"}};
            AnimationClip dodge{Id("51bb55ce-7181-4c1d-9913-431e27c2a005"), skeleton.Id, "Dodge", 0.6, false, true, {}, {}};
            dodge.Tracks.push_back({0, {{0, At(0, 0, 0)}, {0.6, At(0, 0, 2.4F)}}});
            dodge.Tracks.push_back({1, {{0, At(0, 0.95F, 0)}, {0.3, At(0, 0.55F, 0, -0.7F)}, {0.6, At(0, 0.95F, 0)}}});
            dodge.Notifies = {{0.05, "Invulnerability.Start"}, {0.4, "Invulnerability.End"}};
            return std::make_shared<const AnimationLibrary>(std::move(skeleton),
                std::vector<AnimationClip>{std::move(idle), std::move(run), std::move(attack), std::move(dodge)});
        }();
        return library;
    }


}
