#pragma once

#include <Eigen/Core>
#include <Eigen/Geometry>

namespace NcmaEngine
{
    using Vector2 = Eigen::Vector2f;
    using Vector3 = Eigen::Vector3f;
    using Vector4 = Eigen::Vector4f;
    using Quaternion = Eigen::Quaternionf;
    using Matrix4 = Eigen::Matrix4f;

    struct Transform final
    {
        Vector3 Position = Vector3::Zero();
        Quaternion Rotation = Quaternion::Identity();
        Vector3 Scale = Vector3::Ones();

        [[nodiscard]] static Transform Combine(const Transform& parent, const Transform& local)
        {
            Transform result;
            result.Scale = parent.Scale.cwiseProduct(local.Scale);
            result.Rotation = (parent.Rotation * local.Rotation).normalized();
            result.Position = parent.Position + parent.Rotation * parent.Scale.cwiseProduct(local.Position);
            return result;
        }

        [[nodiscard]] Matrix4 ToMatrix() const
        {
            Matrix4 matrix = Matrix4::Identity();
            matrix.block<3, 3>(0, 0) = Rotation.toRotationMatrix() * Scale.asDiagonal();
            matrix.block<3, 1>(0, 3) = Position;
            return matrix;
        }
    };
}
