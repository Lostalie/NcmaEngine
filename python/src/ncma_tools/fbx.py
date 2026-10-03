from __future__ import annotations

import ctypes
import json
from pathlib import Path
from typing import Any


def inspect_fbx(root: Path, source: Path, sample_rate: float = 30) -> dict[str, Any]:
    root = root.resolve(strict=True)
    library = (root / "out" / "managed" / "NcmaNative.dll").resolve(strict=True)
    if not library.is_relative_to(root):
        raise ValueError("Native library must remain inside the engine project")
    native = ctypes.CDLL(str(library))
    native.ncma_character_abi_version.argtypes = []
    native.ncma_character_abi_version.restype = ctypes.c_uint32
    native.ncma_character_inspect_fbx.argtypes = [ctypes.c_uint32, ctypes.c_char_p, ctypes.c_double]
    native.ncma_character_inspect_fbx.restype = ctypes.c_char_p
    native.ncma_character_last_error.argtypes = []
    native.ncma_character_last_error.restype = ctypes.c_char_p
    if native.ncma_character_abi_version() != 1:
        raise RuntimeError("Unsupported native character ABI; rebuild the engine")
    result = native.ncma_character_inspect_fbx(1, str(source.resolve(strict=True)).encode("utf-8"), sample_rate)
    if result is None:
        raise RuntimeError(native.ncma_character_last_error().decode("utf-8"))
    return json.loads(result)
