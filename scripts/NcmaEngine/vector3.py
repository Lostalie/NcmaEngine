"""
Vector3 - 3D vector math class for scripts.

Provides a simple 3D vector type for positions, directions, and velocities.
This is a Python-side wrapper - actual math is done in C++.
"""

import math


class Vector3:
    """
    A 3D vector with x, y, z components.

    Attributes:
        x: X component
        y: Y component
        z: Z component
    """

    def __init__(self, x: float = 0.0, y: float = 0.0, z: float = 0.0):
        self.x = x
        self.y = y
        self.z = z

    # ---- Static properties ----

    @staticmethod
    def zero():
        """Returns a zero vector (0, 0, 0)."""
        return Vector3(0, 0, 0)

    @staticmethod
    def one():
        """Returns a one vector (1, 1, 1)."""
        return Vector3(1, 1, 1)

    @staticmethod
    def up():
        """Returns an up vector (0, 1, 0)."""
        return Vector3(0, 1, 0)

    @staticmethod
    def down():
        """Returns a down vector (0, -1, 0)."""
        return Vector3(0, -1, 0)

    @staticmethod
    def left():
        """Returns a left vector (-1, 0, 0)."""
        return Vector3(-1, 0, 0)

    @staticmethod
    def right():
        """Returns a right vector (1, 0, 0)."""
        return Vector3(1, 0, 0)

    @staticmethod
    def forward():
        """Returns a forward vector (0, 0, -1)."""
        return Vector3(0, 0, -1)

    @staticmethod
    def back():
        """Returns a back vector (0, 0, 1)."""
        return Vector3(0, 0, 1)

    # ---- Properties ----

    @property
    def magnitude(self) -> float:
        """Returns the length of the vector."""
        return math.sqrt(self.x * self.x + self.y * self.y + self.z * self.z)

    @property
    def normalized(self):
        """Returns a copy of the vector with magnitude 1."""
        mag = self.magnitude
        if mag > 0:
            return Vector3(self.x / mag, self.y / mag, self.z / mag)
        return Vector3.zero()

    # ---- Instance methods ----

    def normalize(self):
        """Normalizes the vector in place."""
        mag = self.magnitude
        if mag > 0:
            self.x /= mag
            self.y /= mag
            self.z /= mag

    def dot(self, other: 'Vector3') -> float:
        """Returns the dot product of two vectors."""
        return self.x * other.x + self.y * other.y + self.z * other.z

    def cross(self, other: 'Vector3') -> 'Vector3':
        """Returns the cross product of two vectors."""
        return Vector3(
            self.y * other.z - self.z * other.y,
            self.z * other.x - self.x * other.z,
            self.x * other.y - self.y * other.x
        )

    def distance(self, other: 'Vector3') -> float:
        """Returns the distance between two vectors."""
        return (self - other).magnitude

    def lerp(self, other: 'Vector3', t: float) -> 'Vector3':
        """Linearly interpolates between two vectors."""
        t = max(0.0, min(1.0, t))
        return Vector3(
            self.x + (other.x - self.x) * t,
            self.y + (other.y - self.y) * t,
            self.z + (other.z - self.z) * t
        )

    # ---- Operators ----

    def __add__(self, other: 'Vector3') -> 'Vector3':
        return Vector3(self.x + other.x, self.y + other.y, self.z + other.z)

    def __sub__(self, other: 'Vector3') -> 'Vector3':
        return Vector3(self.x - other.x, self.y - other.y, self.z - other.z)

    def __mul__(self, scalar: float) -> 'Vector3':
        return Vector3(self.x * scalar, self.y * scalar, self.z * scalar)

    def __rmul__(self, scalar: float) -> 'Vector3':
        return self * scalar

    def __truediv__(self, scalar: float) -> 'Vector3':
        return Vector3(self.x / scalar, self.y / scalar, self.z / scalar)

    def __neg__(self) -> 'Vector3':
        return Vector3(-self.x, -self.y, -self.z)

    def __eq__(self, other: 'Vector3') -> bool:
        return self.x == other.x and self.y == other.y and self.z == other.z

    def __repr__(self) -> str:
        return f"Vector3({self.x}, {self.y}, {self.z})"

    def __str__(self) -> str:
        return self.__repr__()

    def to_list(self) -> list:
        """Convert to a Python list [x, y, z]."""
        return [self.x, self.y, self.z]

    @classmethod
    def from_list(cls, lst: list) -> 'Vector3':
        """Create a Vector3 from a list [x, y, z]."""
        if len(lst) >= 3:
            return cls(lst[0], lst[1], lst[2])
        return cls.zero()