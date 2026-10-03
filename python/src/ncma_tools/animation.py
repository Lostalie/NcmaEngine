"""Versioned C ABI adapter: all evaluation and commands remain in the C++ engine."""
from __future__ import annotations

import ctypes
import json
from pathlib import Path
from typing import Any


class AnimationSession:
    def __init__(self, root: Path):
        root = root.resolve(strict=True)
        library = (root / "out" / "managed" / "NcmaNative.dll").resolve(strict=True)
        if not library.is_relative_to(root):
            raise ValueError("Native library must remain inside the selected engine project")
        self._native = ctypes.CDLL(str(library))
        self._bind("ncma_animation_abi_version", [], ctypes.c_uint32)
        self._bind("ncma_animation_create", [ctypes.c_uint32], ctypes.c_uint64)
        self._bind("ncma_animation_destroy", [ctypes.c_uint64], None)
        self._bind("ncma_animation_command", [ctypes.c_uint64, ctypes.c_uint32, ctypes.c_double, ctypes.c_char_p], ctypes.c_uint8)
        self._bind("ncma_animation_inspect", [ctypes.c_uint64], ctypes.c_char_p)
        self._bind("ncma_animation_last_error", [], ctypes.c_char_p)
        self._handle = 0
        if self._native.ncma_animation_abi_version() != 1:
            raise RuntimeError("Unsupported native animation ABI; rebuild NcmaEngine")
        self._handle = self._native.ncma_animation_create(1)
        if not self._handle:
            raise RuntimeError(self._error())

    def _bind(self, name: str, args: list[Any], result: Any) -> None:
        function = getattr(self._native, name)
        function.argtypes, function.restype = args, result

    def _error(self) -> str:
        return self._native.ncma_animation_last_error().decode("utf-8")

    def _require_open(self) -> None:
        if not self._handle:
            raise RuntimeError("Animation session is closed")

    def inspect(self) -> dict[str, Any]:
        self._require_open()
        result = self._native.ncma_animation_inspect(self._handle)
        if result is None:
            raise RuntimeError(self._error())
        return json.loads(result)

    def command(self, command: int, value: float = 0, text: str = "") -> dict[str, Any]:
        self._require_open()
        if not self._native.ncma_animation_command(self._handle, command, value, text.encode("utf-8")):
            raise RuntimeError(self._error())
        return self.inspect()

    def close(self) -> None:
        if self._handle:
            self._native.ncma_animation_destroy(self._handle)
            self._handle = 0

    def __enter__(self) -> AnimationSession:
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()
