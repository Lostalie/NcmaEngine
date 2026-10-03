#include "Camera.h"
#include <Eigen/Geometry>
#include <cmath>

namespace NcmaEngine
{
	Camera::Camera(float fov, float aspectRatio, float nearClip, float farClip)
	{
		SetProjection(fov, aspectRatio, nearClip, farClip);
		RecalculateViewMatrix();
	}

	void Camera::SetProjection(float fov, float aspectRatio, float nearClip, float farClip)
	{
		float fovRad = fov * 3.14159265f / 180.0f;
		m_ProjectionMatrix = Eigen::Matrix4f::Zero();
		m_ProjectionMatrix(0, 0) = 1.0f / (aspectRatio * std::tan(fovRad / 2.0f));
		m_ProjectionMatrix(1, 1) = 1.0f / std::tan(fovRad / 2.0f);
		m_ProjectionMatrix(2, 2) = -(farClip + nearClip) / (farClip - nearClip);
		m_ProjectionMatrix(2, 3) = -(2.0f * farClip * nearClip) / (farClip - nearClip);
		m_ProjectionMatrix(3, 2) = -1.0f;

		RecalculateVPMatrix();
	}

	void Camera::SetPosition(const Eigen::Vector3f& position)
	{
		m_Position = position;
		RecalculateViewMatrix();
	}

	void Camera::SetRotation(float yaw, float pitch, float roll)
	{
		m_Yaw = yaw;
		m_Pitch = pitch;
		m_Roll = roll;
		RecalculateViewMatrix();
	}

	void Camera::GetRotation(float& yaw, float& pitch, float& roll) const
	{
		yaw = m_Yaw;
		pitch = m_Pitch;
		roll = m_Roll;
	}

	Eigen::Vector3f Camera::GetFront() const
	{
		float yawRad = m_Yaw * 3.14159265f / 180.0f;
		float pitchRad = m_Pitch * 3.14159265f / 180.0f;

		Eigen::Vector3f front;
		front.x = -std::sin(yawRad) * std::cos(pitchRad);
		front.y = std::sin(pitchRad);
		front.z = -std::cos(yawRad) * std::cos(pitchRad);
		return front.normalized();
	}

	Eigen::Vector3f Camera::GetRight() const
	{
		Eigen::Vector3f front = GetFront();
		Eigen::Vector3f worldUp(0.0f, 1.0f, 0.0f);
		return front.cross(worldUp).normalized();
	}

	Eigen::Vector3f Camera::GetUp() const
	{
		Eigen::Vector3f right = GetRight();
		return right.cross(GetFront());
	}

	void Camera::RecalculateViewMatrix()
	{
		float yawRad = m_Yaw * 3.14159265f / 180.0f;
		float pitchRad = m_Pitch * 3.14159265f / 180.0f;
		float rollRad = m_Roll * 3.14159265f / 180.0f;

		// Convert to rotation matrix using Eigen
		Eigen::Vector3f euler(yawRad, pitchRad, rollRad);
		Eigen::Quaternionf quat(Eigen::AngleAxisf(yawRad, Eigen::Vector3f::UnitY())
			* Eigen::AngleAxisf(pitchRad, Eigen::Vector3f::UnitX())
			* Eigen::AngleAxisf(rollRad, Eigen::Vector3f::UnitZ()));

		Eigen::Matrix4f rotMatrix = quat.matrix().transpose();

		m_ViewMatrix = Eigen::Matrix4f::Identity();
		m_ViewMatrix.block<3, 3>(0, 0) = rotMatrix.block<3, 3>(0, 0);
		m_ViewMatrix.block<3, 1>(0, 3) = -rotMatrix.block<3, 3>(0, 0) * m_Position;
		m_ViewMatrix(3, 3) = 1.0f;

		RecalculateVPMatrix();
	}

	void Camera::RecalculateVPMatrix()
	{
		m_VPMatrix = m_ProjectionMatrix * m_ViewMatrix;
	}
}
