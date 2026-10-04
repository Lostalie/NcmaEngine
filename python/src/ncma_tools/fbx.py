from __future__ import annotations

import ctypes
import json
from pathlib import Path
from typing import Any


class ImportedCharacter:
    """Read-only tooling lease, not Python gameplay or Editor scene/history ownership."""

    def __init__(self, root: Path, source: Path, sample_rate: float = 30) -> None:
        root = root.resolve(strict=True)
        library = (root / "out" / "managed" / "NcmaNative.dll").resolve(strict=True)
        if not library.is_relative_to(root):
            raise ValueError("Native library must remain inside the engine project")
        self._native = ctypes.CDLL(str(library))
        self._handle = 0
        self._bind("ncma_character_abi_version", [], ctypes.c_uint32)
        self._bind("ncma_character_import", [ctypes.c_uint32, ctypes.c_char_p, ctypes.c_double, ctypes.c_char_p], ctypes.c_uint64)
        self._bind("ncma_character_release", [ctypes.c_uint64], ctypes.c_uint8)
        self._bind("ncma_character_read_report", [ctypes.c_uint64, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)], ctypes.c_uint8)
        self._bind("ncma_character_read_error", [ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)], ctypes.c_uint8)
        if self._native.ncma_character_abi_version() != 2:
            raise RuntimeError("Unsupported native character ABI; ABI 2 required")
        self._handle = self._native.ncma_character_import(2, str(source.resolve(strict=True)).encode("utf-8"), sample_rate, None)
        if not self._handle:
            raise RuntimeError(self._error())

    def _bind(self, name: str, args: list[Any], result: Any) -> None:
        function = getattr(self._native, name)
        function.argtypes = args
        function.restype = result

    def _error(self) -> str:
        count = ctypes.c_uint32()
        if self._native.ncma_character_read_error(None, 0, ctypes.byref(count)) != 2 or not 0 < count.value <= 4194304:
            return "Character kernel failure"
        output = ctypes.create_string_buffer(count.value)
        if self._native.ncma_character_read_error(output, len(output), ctypes.byref(count)) != 1:
            return "Character error copy failed"
        return output.value.decode("utf-8")

    def inspect(self) -> dict[str, Any]:
        if not self._handle:
            raise RuntimeError("Character resource disposed")
        count = ctypes.c_uint32()
        if self._native.ncma_character_read_report(self._handle, None, 0, ctypes.byref(count)) != 2:
            raise RuntimeError(self._error())
        if not 1 < count.value <= 4194304:
            raise RuntimeError("Invalid character report size")
        size = count.value
        output = ctypes.create_string_buffer(size)
        if self._native.ncma_character_read_report(self._handle, output, size, ctypes.byref(count)) != 1:
            raise RuntimeError(self._error())
        if count.value != size or output.raw[-1] != 0:
            raise RuntimeError("Character report copy mismatch")
        return json.loads(output.value)

    def close(self) -> None:
        if self._handle:
            if self._native.ncma_character_release(self._handle) != 1:
                raise RuntimeError(self._error())
            self._handle = 0

    def __enter__(self) -> ImportedCharacter:
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()


def inspect_fbx(root: Path, source: Path, sample_rate: float = 30) -> dict[str, Any]:
    with ImportedCharacter(root, source, sample_rate) as character:
        return character.inspect()
