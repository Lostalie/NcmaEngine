from __future__ import annotations

import ctypes
from enum import IntEnum
import math
from pathlib import Path
import threading


class Vector3(ctypes.Structure):
    _fields_ = [("x", ctypes.c_float), ("y", ctypes.c_float), ("z", ctypes.c_float)]


class Transform(ctypes.Structure):
    _fields_ = [("position", Vector3), ("rotation_x", ctypes.c_float),
                ("rotation_y", ctypes.c_float), ("rotation_z", ctypes.c_float),
                ("rotation_w", ctypes.c_float), ("scale", Vector3)]


class ExportKind(IntEnum):
    FLOAT = 1
    DOUBLE = 2
    INTEGER = 3
    BOOLEAN = 4


class Export:
    def __init__(self, default: float | int | bool, *, kind: ExportKind | None = None,
                 display_name: str = "", category: str = ""):
        self.kind = ExportKind(kind) if kind is not None else (
            ExportKind.BOOLEAN if type(default) is bool else
            ExportKind.INTEGER if type(default) is int else ExportKind.DOUBLE)
        self.default = self.validate(default)
        self.display_name, self.category = display_name, category
        self.name = ""

    def validate(self, value: object) -> float | int | bool:
        if self.kind == ExportKind.BOOLEAN:
            if type(value) is not bool:
                raise ValueError("Boolean Export requires bool")
            return value
        if self.kind == ExportKind.INTEGER:
            if type(value) is not int or not -2147483648 <= value <= 2147483647:
                raise ValueError("Integer Export requires int32")
            return value
        if type(value) not in (float, int) or not math.isfinite(value):
            raise ValueError("Numeric Export requires a finite number")
        number = float(value)
        if self.kind == ExportKind.FLOAT and not math.isfinite(ctypes.c_float(number).value):
            raise ValueError("Float Export exceeds float32")
        return number

    def from_native(self, value: float) -> float | int | bool:
        if not math.isfinite(value):
            raise ValueError("Export value must be finite")
        if self.kind == ExportKind.BOOLEAN:
            if value not in (0, 1):
                raise ValueError("Boolean Export must be 0 or 1")
            return bool(value)
        if self.kind == ExportKind.INTEGER:
            if value != math.trunc(value):
                raise ValueError("Integer Export must be integral")
            return self.validate(int(value))
        return self.validate(value)

    def __set_name__(self, owner: type, name: str) -> None:
        if name.startswith("_") or name == "node":
            raise ValueError("Export must have a public name other than node")
        self.name = name

    def __get__(self, instance: object, owner: type | None = None):
        if instance is None:
            return self
        return instance.__dict__.get(self.name, self.default)

    def __set__(self, instance: object, value: object) -> None:
        instance.__dict__[self.name] = self.validate(value)


def export(default: float | int | bool, **metadata) -> Export:
    return Export(default, **metadata)


class _WorldLease:
    def __init__(self, native: Native, handle: int):
        self.native, self.handle = native, handle
        self.thread = threading.get_ident()

    def require(self) -> int:
        if threading.get_ident() != self.thread:
            raise RuntimeError("Scene operations must run on the play-session thread")
        if not self.handle:
            raise RuntimeError("Node belongs to an ended play session")
        return self.handle


class Native:
    def __init__(self, library: Path):
        self.library = ctypes.CDLL(str(library.resolve(strict=True)))
        bindings = {
            "ncma_get_abi_version": ([], ctypes.c_uint32),
            "ncma_get_last_error": ([], ctypes.c_char_p),
            "ncma_world_create_node": ([ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint64], ctypes.c_uint64),
            "ncma_world_get_local_transform": ([ctypes.c_void_p, ctypes.c_uint64, ctypes.POINTER(Transform)], ctypes.c_uint8),
            "ncma_world_set_local_transform": ([ctypes.c_void_p, ctypes.c_uint64, ctypes.POINTER(Transform)], ctypes.c_uint8),
            "ncma_world_set_parent": ([ctypes.c_void_p, ctypes.c_uint64, ctypes.c_uint64], ctypes.c_uint8),
            "ncma_world_destroy_node": ([ctypes.c_void_p, ctypes.c_uint64, ctypes.c_uint8], ctypes.c_uint8),
        }
        for name, (arguments, result) in bindings.items():
            function = getattr(self.library, name)
            function.argtypes, function.restype = arguments, result
        if self.library.ncma_get_abi_version() != 1:
            raise RuntimeError("Unsupported scene ABI; rebuild NcmaEngine")

    def check(self, result: int) -> int:
        if not result:
            message = self.library.ncma_get_last_error()
            raise RuntimeError(message.decode("utf-8", errors="replace") if message else "Native scene operation failed")
        return result


class Node:
    def __init__(self, lease: _WorldLease, node_id: int):
        self._lease, self.id = lease, node_id

    @property
    def local_transform(self) -> Transform:
        result = Transform()
        native = self._lease.native
        native.check(native.library.ncma_world_get_local_transform(self._lease.require(), self.id, ctypes.byref(result)))
        return result

    @local_transform.setter
    def local_transform(self, value: Transform) -> None:
        if not isinstance(value, Transform):
            raise TypeError("local_transform requires Transform")
        numbers = [value.position.x, value.position.y, value.position.z,
                   value.rotation_x, value.rotation_y, value.rotation_z, value.rotation_w,
                   value.scale.x, value.scale.y, value.scale.z]
        if not all(math.isfinite(number) for number in numbers) or sum(number * number for number in numbers[3:7]) < 1e-12:
            raise ValueError("Transform must be finite with a nonzero rotation quaternion")
        native = self._lease.native
        native.check(native.library.ncma_world_set_local_transform(self._lease.require(), self.id, ctypes.byref(value)))

    def create_child(self, name: str) -> Node:
        if not isinstance(name, str) or not name or "\0" in name:
            raise ValueError("Node name must be a nonempty string without NUL")
        native = self._lease.native
        node_id = native.check(native.library.ncma_world_create_node(self._lease.require(), name.encode("utf-8"), self.id))
        return Node(self._lease, node_id)

    def set_parent(self, parent: Node | None) -> None:
        if parent is not None and parent._lease is not self._lease:
            raise ValueError("Parent must belong to the same play session")
        native = self._lease.native
        native.check(native.library.ncma_world_set_parent(self._lease.require(), self.id, parent.id if parent else 0))

    def destroy(self, recursive: bool = True) -> None:
        native = self._lease.native
        native.check(native.library.ncma_world_destroy_node(self._lease.require(), self.id, int(recursive)))


class Behaviour:
    @property
    def node(self) -> Node:
        if "_node" not in self.__dict__:
            raise RuntimeError("Node is assigned after construction, before on_create")
        return self._node

    def on_create(self) -> None: pass
    def on_enable(self) -> None: pass
    def on_update(self, delta_seconds: float) -> None: pass
    def on_fixed_update(self, fixed_delta_seconds: float) -> None: pass
    def on_disable(self) -> None: pass
    def on_destroy(self) -> None: pass
