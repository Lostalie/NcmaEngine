"""
Input module for NcmaEngine scripts.

Provides a Python interface to the engine's input system.
"""

import math

# Key codes - must match C++ KeyCode enum
class KeyCode:
    # Printable keys
    SPACE = 32
    A = 65
    B = 66
    C = 67
    D = 68
    E = 69
    F = 70
    G = 71
    H = 72
    I = 73
    J = 74
    K = 75
    L = 76
    M = 77
    N = 78
    O = 79
    P = 80
    Q = 81
    R = 82
    S = 83
    T = 84
    U = 85
    V = 86
    W = 87
    X = 88
    Y = 89
    Z = 90

    # Function keys
    ESCAPE = 256
    ENTER = 257
    TAB = 258
    BACKSPACE = 259
    UP = 262
    DOWN = 263
    LEFT = 264
    RIGHT = 265

    # Modifiers
    LEFT_SHIFT = 340
    LEFT_CONTROL = 341
    LEFT_ALT = 342
    RIGHT_SHIFT = 344
    RIGHT_CONTROL = 345
    RIGHT_ALT = 346


# Mouse buttons
class MouseCode:
    LEFT = 0
    RIGHT = 1
    MIDDLE = 2


class Input:
    """
    Provides access to keyboard and mouse input.

    Note: This is a static wrapper around the engine's input system.
    The actual implementation is in C++ and called via the engine's Python bindings.

    Example:
        from NcmaEngine.input import Input, KeyCode

        if Input.is_key_pressed(KeyCode.W):
            print("W key pressed!")
    """

    # Internal reference to C++ InputSystem (set by engine bindings)
    _cpp_input = None

    @classmethod
    def _set_cpp_input(cls, cpp_input):
        """Internal: Set the C++ input system reference."""
        cls._cpp_input = cpp_input

    @classmethod
    def is_key_pressed(cls, key_code: int) -> bool:
        """
        Check if a key was just pressed this frame.

        Args:
            key_code: The key code to check

        Returns:
            True if the key was just pressed
        """
        if cls._cpp_input is not None:
            return cls._cpp_input.IsKeyPressed(key_code)
        return False

    @classmethod
    def is_key_held(cls, key_code: int) -> bool:
        """
        Check if a key is currently held down.

        Args:
            key_code: The key code to check

        Returns:
            True if the key is held
        """
        if cls._cpp_input is not None:
            return cls._cpp_input.IsKeyHeld(key_code)
        return False

    @classmethod
    def is_key_released(cls, key_code: int) -> bool:
        """
        Check if a key was just released this frame.

        Args:
            key_code: The key code to check

        Returns:
            True if the key was just released
        """
        if cls._cpp_input is not None:
            return cls._cpp_input.IsKeyReleased(key_code)
        return False

    @classmethod
    def is_mouse_button_pressed(cls, button: int) -> bool:
        """
        Check if a mouse button was just pressed.

        Args:
            button: The mouse button code (0=left, 1=right, 2=middle)

        Returns:
            True if the button was just pressed
        """
        if cls._cpp_input is not None:
            return cls._cpp_input.IsMouseButtonPressed(button)
        return False

    @classmethod
    def is_mouse_button_held(cls, button: int) -> bool:
        """
        Check if a mouse button is currently held.

        Args:
            button: The mouse button code

        Returns:
            True if the button is held
        """
        if cls._cpp_input is not None:
            return cls._cpp_input.IsMouseButtonHeld(button)
        return False

    @classmethod
    def get_mouse_position(cls) -> tuple:
        """
        Get the current mouse position.

        Returns:
            Tuple of (x, y) coordinates
        """
        if cls._cpp_input is not None:
            return cls._cpp_input.GetMousePosition()
        return (0.0, 0.0)

    @classmethod
    def get_mouse_x(cls) -> float:
        """Get the mouse X position."""
        if cls._cpp_input is not None:
            return cls._cpp_input.GetMouseX()
        return 0.0

    @classmethod
    def get_mouse_y(cls) -> float:
        """Get the mouse Y position."""
        if cls._cpp_input is not None:
            return cls._cpp_input.GetMouseY()
        return 0.0

    @classmethod
    def get_mouse_delta(cls) -> tuple:
        """
        Get the mouse delta since last frame.

        Returns:
            Tuple of (delta_x, delta_y)
        """
        if cls._cpp_input is not None:
            return (cls._cpp_input.GetMouseDeltaX(), cls._cpp_input.GetMouseDeltaY())
        return (0.0, 0.0)

    # ---- Convenience methods for common inputs ----

    @classmethod
    def get_wasd(cls) -> dict:
        """
        Get the state of WASD movement keys.

        Returns:
            Dict with 'w', 'a', 's', 'd' boolean values
        """
        return {
            'w': cls.is_key_held(KeyCode.W),
            'a': cls.is_key_held(KeyCode.A),
            's': cls.is_key_held(KeyCode.S),
            'd': cls.is_key_held(KeyCode.D),
        }

    @classmethod
    def get_arrow_keys(cls) -> dict:
        """
        Get the state of arrow keys.

        Returns:
            Dict with 'up', 'down', 'left', 'right' boolean values
        """
        return {
            'up': cls.is_key_held(KeyCode.UP),
            'down': cls.is_key_held(KeyCode.DOWN),
            'left': cls.is_key_held(KeyCode.LEFT),
            'right': cls.is_key_held(KeyCode.RIGHT),
        }

    @classmethod
    def is_shift_held(cls) -> bool:
        """Check if either shift key is held."""
        return cls.is_key_held(KeyCode.LEFT_SHIFT) or cls.is_key_held(KeyCode.RIGHT_SHIFT)

    @classmethod
    def is_ctrl_held(cls) -> bool:
        """Check if either control key is held."""
        return cls.is_key_held(KeyCode.LEFT_CONTROL) or cls.is_key_held(KeyCode.RIGHT_CONTROL)

    @classmethod
    def is_alt_held(cls) -> bool:
        """Check if either alt key is held."""
        return cls.is_key_held(KeyCode.LEFT_ALT) or cls.is_key_held(KeyCode.RIGHT_ALT)


# Convenience instance
input = Input()