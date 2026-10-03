"""
Transform - Position, rotation, and scale component for scripts.

Provides access to an entity's transform in Python scripts.
"""

from NcmaEngine.vector3 import Vector3


class Transform:
    """
    Provides access to an entity's position, rotation, and scale.

    Note: This is a Python-side wrapper that provides a convenient interface.
    The actual transform data is stored in C++ and accessed via the engine's
    TransformComponent.
    """

    def __init__(self, position: Vector3 = None, rotation: Vector3 = None, scale: Vector3 = None):
        """
        Initialize transform with given values.

        All parameters are optional and default to zero/one values.
        """
        self._position = position or Vector3.zero()
        self._rotation = rotation or Vector3.zero()  # Euler angles in degrees
        self._scale = scale or Vector3.one()

    # ---- Position ----

    @property
    def position(self) -> Vector3:
        """Get the world position."""
        return self._position

    @position.setter
    def position(self, value: Vector3):
        """Set the world position."""
        self._position = value

    def set_position(self, x: float, y: float, z: float):
        """Set position using x, y, z components."""
        self._position = Vector3(x, y, z)

    def translate(self, delta: Vector3):
        """Move the transform by delta."""
        self._position = self._position + delta

    # ---- Rotation ----

    @property
    def rotation(self) -> Vector3:
        """Get the rotation as Euler angles in degrees."""
        return self._rotation

    @rotation.setter
    def rotation(self, value: Vector3):
        """Set rotation using Euler angles in degrees."""
        self._rotation = value

    def set_rotation(self, x: float, y: float, z: float):
        """Set rotation using x, y, z Euler angles in degrees."""
        self._rotation = Vector3(x, y, z)

    def rotate(self, delta: Vector3):
        """Rotate by delta (in degrees)."""
        self._rotation = self._rotation + delta

    # ---- Scale ----

    @property
    def scale(self) -> Vector3:
        """Get the local scale."""
        return self._scale

    @scale.setter
    def scale(self, value: Vector3):
        """Set the local scale."""
        self._scale = value

    def set_scale(self, x: float, y: float, z: float):
        """Set scale using x, y, z components."""
        self._scale = Vector3(x, y, z)

    # ---- Direction vectors ----

    @property
    def forward(self) -> Vector3:
        """Get the forward direction vector."""
        # Simplified - in real implementation this would use rotation matrix
        return Vector3.forward()

    @property
    def right(self) -> Vector3:
        """Get the right direction vector."""
        return Vector3.right()

    @property
    def up(self) -> Vector3:
        """Get the up direction vector."""
        return Vector3.up()

    # ---- Methods ----

    def look_at(self, target: Vector3):
        """Rotate to face a target position."""
        # Simplified - in real implementation this would compute rotation from direction
        direction = target - self._position
        if direction.magnitude > 0.001:
            # Compute yaw and pitch from direction
            import math
            yaw = math.atan2(-direction.x, -direction.z) * (180.0 / math.pi)
            pitch = math.asin(direction.y / direction.magnitude) * (180.0 / math.pi)
            self._rotation = Vector3(pitch, yaw, 0)

    def __repr__(self) -> str:
        return f"Transform(pos={self._position}, rot={self._rotation}, scale={self._scale})"