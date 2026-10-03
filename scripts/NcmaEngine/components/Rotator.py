"""
Rotator - Example script component for NcmaEngine.

This script rotates its parent entity continuously.
"""

from NcmaEngine import Component, serialized_field
from NcmaEngine.vector3 import Vector3


class Rotator(Component):
    """
    A simple component that rotates the entity each frame.

    Attributes:
        rotation_speed: Rotation speed in degrees per second
    """

    rotation_speed: float = serialized_field(default=90.0)

    def start(self):
        """Called when the component first becomes active."""
        print(f"Rotator started on entity")

    def update(self, delta_time: float):
        """Called every frame."""
        if self.transform:
            # Get current rotation
            current_rot = self.transform.rotation

            # Apply rotation
            self.transform.rotation = Vector3(
                current_rot.x,
                current_rot.y + self.rotation_speed * delta_time,
                current_rot.z
            )

    def on_destroy(self):
        """Called when the component is removed."""
        print("Rotator destroyed")