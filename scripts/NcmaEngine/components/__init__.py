"""
Example script components for NcmaEngine.

This directory contains example Python scripts that can be used
as templates for creating custom game logic.
"""

from NcmaEngine import Component, serialized_field, Vector3


class Rotator(Component):
    """
    A simple component that rotates the entity each frame.

    Usage:
        entity.AddComponent(ScriptComponent).LoadScript("Rotator.py")
    """

    rotation_speed: float = serialized_field(default=90.0)  # degrees per second

    def start(self):
        print(f"Rotator started on {self.game_object}")

    def update(self, delta_time: float):
        if self.transform:
            current_rot = self.transform.rotation
            self.transform.rotation = Vector3(
                current_rot.x,
                current_rot.y + self.rotation_speed * delta_time,
                current_rot.z
            )


class Mover(Component):
    """
    A simple component that moves the entity based on input.

    This is a placeholder - actual input handling would require
    integration with the engine's input system.
    """

    speed: float = serialized_field(default=5.0)
    direction: Vector3 = serialized_field(default=Vector3.right())

    def start(self):
        print(f"Mover started")

    def update(self, delta_time: float):
        if self.transform:
            delta = self.direction * (self.speed * delta_time)
            self.transform.translate(delta)


class Billboard(Component):
    """
    Makes the entity always face the camera.
    Useful for sprites, UI elements, etc.
    """

    def update(self, delta_time: float):
        # In a real implementation, this would rotate to face the main camera
        pass