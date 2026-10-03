#pragma once

#include "../entity\Entity.h"
#include <Eigen/Dense>

namespace NcmaEngine
{
	class TransformComponent : public Component
	{
	public:
		TransformComponent()
		{
			m_LocalPosition = Eigen::Vector3f::Zero();
			m_LocalRotation = Eigen::Vector3f::Zero();  // Euler angles in degrees
			m_LocalScale = Eigen::Vector3f(1.0f, 1.0f, 1.0f);
			m_bDirty = true;
		}

		// World position (simplified - no parent support yet)
		const Eigen::Vector3f& GetPosition() const { return m_LocalPosition; }
		const Eigen::Vector3f& GetRotation() const { return m_LocalRotation; }  // Degrees
		const Eigen::Vector3f& GetScale() const { return m_LocalScale; }

		void SetPosition(const Eigen::Vector3f& position)
		{
			m_LocalPosition = position;
			m_bDirty = true;
		}

		void SetRotation(const Eigen::Vector3f& rotation)
		{
			m_LocalRotation = rotation;
			m_bDirty = true;
		}

		void SetScale(const Eigen::Vector3f& scale)
		{
			m_LocalScale = scale;
			m_bDirty = true;
		}

		void Translate(const Eigen::Vector3f& delta)
		{
			m_LocalPosition += delta;
			m_bDirty = true;
		}

		void Rotate(const Eigen::Vector3f& delta)
		{
			m_LocalRotation += delta;
			m_bDirty = true;
		}

		// Get transform matrix
		const Eigen::Matrix4f& GetMatrix()
		{
			if (m_bDirty)
			{
				RecalculateMatrix();
				m_bDirty = false;
			}
			return m_LocalMatrix;
		}

		// Front/Right/Up vectors
		Eigen::Vector3f GetForward() const
		{
			float yawRad = m_LocalRotation.y() * 3.14159265f / 180.0f;
			float pitchRad = m_LocalRotation.x() * 3.14159265f / 180.0f;

			Eigen::Vector3f forward;
			forward.x() = -std::sin(yawRad) * std::cos(pitchRad);
			forward.y() = std::sin(pitchRad);
			forward.z() = -std::cos(yawRad) * std::cos(pitchRad);
			return forward.normalized();
		}

		Eigen::Vector3f GetRight() const
		{
			Eigen::Vector3f worldUp(0.0f, 1.0f, 0.0f);
			return GetForward().cross(worldUp).normalized();
		}

		Eigen::Vector3f GetUp() const
		{
			return GetRight().cross(GetForward());
		}

	private:
		void RecalculateMatrix()
		{
			// Build transformation matrix from position, rotation, scale
			float yawRad = m_LocalRotation.y() * 3.14159265f / 180.0f;
			float pitchRad = m_LocalRotation.x() * 3.14159265f / 180.0f;
			float rollRad = m_LocalRotation.z() * 3.14159265f / 180.0f;

			// Rotation from Euler angles (YXZ order - yaw, pitch, roll)
			Eigen::Matrix3f rotMatrix;
			rotMatrix = Eigen::AngleAxisf(yawRad, Eigen::Vector3f::UnitY())
				* Eigen::AngleAxisf(pitchRad, Eigen::Vector3f::UnitX())
				* Eigen::AngleAxisf(rollRad, Eigen::Vector3f::UnitZ());

			// Build full transform matrix
			m_LocalMatrix = Eigen::Matrix4f::Identity();
			m_LocalMatrix.block<3, 3>(0, 0) = rotMatrix * m_LocalScale.asDiagonal();
			m_LocalMatrix.block<3, 1>(0, 3) = m_LocalPosition;
		}

		Eigen::Vector3f m_LocalPosition;
		Eigen::Vector3f m_LocalRotation;  // Euler angles in degrees
		Eigen::Vector3f m_LocalScale;

		Eigen::Matrix4f m_LocalMatrix;
		bool m_bDirty;
	};
}
