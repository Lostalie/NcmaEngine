"""
Component - Base class for Python scripts in NcmaEngine.

Inherit from this class to create custom game logic components.
Supports Unity-style lifecycle methods and serialized fields.

Example:
    from NcmaEngine import Component, serialized_field

    class PlayerController(Component):
        speed: float = serialized_field(default=5.0)
        jump_force: float = serialized_field(default=10.0)

        def start(self):
            print("Player started!")
            self._started = True

        def update(self, delta_time: float):
            if hasattr(self, '_started'):
                print(f"Moving at speed {self.speed}")
"""

import inspect
from typing import Optional, Dict, Any, Type, List


class SerializedField:
    """Metadata for a serialized field."""
    def __init__(self, default=None, name: str = "", field_type: str = ""):
        self.default = default
        self.name = name
        self.field_type = field_type or self._infer_type(default)


    def _infer_type(self, value) -> str:
        if value is None:
            return "unknown"
        return type(value).__name__


def serialized_field(default=None, name: str = ""):
    """
    Decorator to mark a class attribute as a serialized field.

    Usage:
        class MyComponent(Component):
            speed: float = serialized_field(default=5.0)
            name: str = serialized_field(default="Player")
    """
    return SerializedField(default=default, name=name)


class Component:
    """
    Base class for Python-defined components.

    Lifecycle methods (override as needed):
        - start(): Called before first update
        - update(delta_time): Called every frame
        - late_update(delta_time): Called after all update()
        - on_destroy(): Called when component is removed
        - on_enable(): Called when component becomes enabled
        - on_disable(): Called when component becomes disabled

    Properties (injected by engine):
        - game_object: The GameObject this component is attached to
        - transform: Shortcut to game_object.transform
        - enabled: Whether this component is active
    """

    # Class-level storage for serialized fields
    _serialized_fields_: Dict[str, SerializedField] = {}

    def __init__(self):
        """Internal initialization - do not override."""
        self._game_object = None
        self._enabled = True
        self._started = False

        # Process class annotations for serialized fields
        self._process_serialized_fields()

    def _process_serialized_fields(self):
        """Scan class annotations and create serialized field metadata."""
        cls = type(self)

        # Only process once per class (not inherited)
        if hasattr(cls, '_fields_processed_') and cls._fields_processed_:
            return

        # Get annotations
        annotations = cls.__dict__.get('__annotations__', {})

        for field_name, field_type in annotations.items():
            if field_name.startswith('_'):
                continue
            if field_name in self._serialized_fields_:
                continue

            # Get default value from class dict
            default = cls.__dict__.get(field_name, None)

            # Check if it's a SerializedField already
            if isinstance(default, SerializedField):
                self._serialized_fields_[field_name] = default
            else:
                self._serialized_fields_[field_name] = SerializedField(
                    default=default,
                    name=field_name
                )

        cls._fields_processed_ = True

    # ---- Engine-injected properties ----

    @property
    def game_object(self):
        """The GameObject this component is attached to."""
        return self._game_object

    @property
    def transform(self):
        """Shortcut to game_object.transform."""
        if self._game_object is not None:
            return self._game_object.transform
        return None

    @property
    def enabled(self) -> bool:
        """Whether this component is active."""
        return self._enabled

    @enabled.setter
    def enabled(self, value: bool):
        """Set component enabled state."""
        if self._enabled != value:
            self._enabled = value
            if value:
                self.on_enable()
            else:
                self.on_disable()

    # ---- Lifecycle methods (override in subclasses) ----

    def start(self):
        """
        Called before the first update.
        Use this for initialization that depends on other components.
        """
        pass

    def update(self, delta_time: float):
        """
        Called every frame.

        Args:
            delta_time: Time elapsed since last frame in seconds
        """
        pass

    def late_update(self, delta_time: float):
        """
        Called after all update() calls in the frame.

        Args:
            delta_time: Time elapsed since last frame in seconds
        """
        pass

    def on_destroy(self):
        """Called when the component is removed from its GameObject."""
        pass

    def on_enable(self):
        """Called when the component becomes enabled."""
        pass

    def on_disable(self):
        """Called when the component becomes disabled."""
        pass

    def awake(self):
        """
        Called when the component is first created/attached.
        Note: This is called automatically by the engine.
        """
        pass

    # ---- Internal methods (called by engine) ----

    def _set_game_object(self, go):
        """Internal: Set the parent GameObject."""
        self._game_object = go

    def _call_start(self):
        """Internal: Call start() if not already called."""
        if not self._started:
            self.start()
            self._started = True

    def _call_awake(self):
        """Internal: Call awake()."""
        self.awake()

    def _call_update(self, delta_time: float):
        """Internal: Call update()."""
        self.update(delta_time)

    def _call_late_update(self, delta_time: float):
        """Internal: Call late_update()."""
        self.late_update(delta_time)

    def _call_on_destroy(self):
        """Internal: Call on_destroy()."""
        self.on_destroy()

    def _call_on_enable(self):
        """Internal: Call on_enable()."""
        self.on_enable()

    def _call_on_disable(self):
        """Internal: Call on_disable()."""
        self.on_disable()

    # ---- Serialization support ----

    def get_serialized_fields(self) -> Dict[str, Any]:
        """Get all serialized field values."""
        result = {}
        for name in self._serialized_fields_:
            if hasattr(self, name):
                result[name] = getattr(self, name)
        return result

    def set_serialized_field(self, name: str, value: Any):
        """Set a serialized field value."""
        if name in self._serialized_fields_:
            setattr(self, name, value)

    # ---- Reflection support ----

    @classmethod
    def get_field_names(cls) -> List[str]:
        """Get list of serialized field names."""
        return list(cls._serialized_fields_.keys())

    @classmethod
    def get_field_metadata(cls, name: str) -> Optional[SerializedField]:
        """Get metadata for a specific field."""
        return cls._serialized_fields_.get(name)


# Decorator for creating serialized fields
def field(default=None, name: str = ""):
    """Alias for serialized_field() for cleaner syntax."""
    return serialized_field(default=default, name=name)


class Behaviour(Component):
    """
    Base class for components that need more control over lifecycle.
    Inherits from Component - provided for Unity compatibility.
    """
    pass