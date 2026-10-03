"""
NcmaEngine Python Package

This package provides Python bindings for the NcmaEngine game engine.
Scripts should inherit from `Component` to create custom game logic.

Example:
    from NcmaEngine import Component

    class PlayerController(Component):
        speed: float = 5.0

        def start(self):
            print("Player started!")

        def update(self, delta_time: float):
            print(f"Updating player with speed {self.speed}")
"""

__version__ = "0.1.0"

from NcmaEngine.component import Component, serialized_field
from NcmaEngine.vector3 import Vector3
from NcmaEngine.transform import Transform
from NcmaEngine.input import Input, KeyCode, MouseCode

__all__ = [
    "Component",
    "serialized_field",
    "Vector3",
    "Transform",
    "Input",
    "KeyCode",
    "MouseCode",
]