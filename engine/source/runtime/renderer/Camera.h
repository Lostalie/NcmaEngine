#pragma once

#include "Core.h"
#include <Eigen/Dense>

namespace NcmaEngine
{
	class Camera
	{
	public:
		Camera() = default;
		Camera(float fov, float aspectRatio, float nearClip, float farClip);

		void SetProjection(float fov, float aspectRatio, float nearClip, float farClip);

		// View matrix
		const Eigen::Matrix4f& GetViewMatrix() const { return m_ViewMatrix; }
		const Eigen::Matrix4f& GetProjectionMatrix() const { return m_ProjectionMatrix; }
		const Eigen::Matrix4f& GetViewProjectionMatrix() const { return m_VPMatrix; }

		// Position
		void SetPosition(const Eigen::Vector3f& position);
		const Eigen::Vector3f& GetPosition() const { return m_Position; }

		// Rotation (Euler angles in degrees)
		void SetRotation(float yaw, float pitch, float roll);
		void GetRotation(float& yaw, float& pitch, float& roll) const;

		// Front and right vectors
		Eigen::Vector3f GetFront() const;
		Eigen::Vector3f GetRight() const;
		Eigen::Vector3f GetUp() const;

	private:
		void RecalculateViewMatrix();
		void RecalculateVPMatrix();

		Eigen::Matrix4f m_ViewMatrix = Eigen::Matrix4f::Identity();
		Eigen::Matrix4f m_ProjectionMatrix = Eigen::Matrix4f::Identity();
		Eigen::Matrix4f m_VPMatrix = Eigen::Matrix4f::Identity();

		Eigen::Vector3f m_Position = Eigen::Vector3f(0.0f, 0.0f, 0.0f);
		float m_Yaw = 0.0f;   // Rotation around Y axis (degrees)
		float m_Pitch = 0.0f; // Rotation around X axis (degrees)
		float m_Roll = 0.0f;   // Rotation around Z axis (degrees)
	};
}
