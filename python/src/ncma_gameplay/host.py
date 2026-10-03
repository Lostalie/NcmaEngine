"""Trusted project-script host. Isolation of play state is not a security sandbox."""
from __future__ import annotations

import math
from pathlib import Path
import sys
import tokenize
import types
import uuid

from .api import Behaviour, Export, Native, Node, _WorldLease


class Host:
    def __init__(self, scripts: str, library: str):
        self._native = Native(Path(library))
        self._types: dict[str, tuple[type[Behaviour], dict[str, Export]]] = {}
        self._modules: list[str] = []
        self._instances: list[tuple[Behaviour, bool]] = []
        self._lease: _WorldLease | None = None
        root = Path(scripts).resolve(strict=True)
        prefix = "_ncma_gameplay_" + uuid.uuid4().hex + "_"
        try:
            for source in sorted(root.rglob("*.py")):
                if "__pycache__" in source.parts or source.name == "__init__.py":
                    continue
                if not source.resolve(strict=True).is_relative_to(root):
                    raise ValueError("Gameplay script must remain inside its script directory")
                parts = source.relative_to(root).with_suffix("").parts
                if not all(part.isidentifier() for part in parts):
                    raise ValueError("Gameplay module names must be Python identifiers")
                logical = ".".join(parts)
                name = prefix + logical
                module = types.ModuleType(name)
                module.__file__ = str(source)
                sys.modules[name] = module
                self._modules.append(name)
                # Compile source directly: reload never depends on timestamp-based .pyc caching.
                with tokenize.open(source) as stream:
                    code = compile(stream.read(), str(source), "exec")
                exec(code, module.__dict__)
                for value in list(module.__dict__.values()):
                    if not isinstance(value, type) or value is Behaviour or not issubclass(value, Behaviour):
                        continue
                    if value.__module__ != name:
                        continue
                    type_name = logical + "." + value.__name__
                    if type_name in self._types:
                        raise ValueError("Duplicate Python Behaviour: " + type_name)
                    fields = {}
                    for base in reversed(value.__mro__):
                        for field_name, field in vars(base).items():
                            if isinstance(field, Export):
                                if field.name != field_name:
                                    raise ValueError("Export descriptors cannot be reused under another name")
                                fields[field_name] = field
                            elif field_name in fields:
                                del fields[field_name]
                    self._types[type_name] = (value, fields)
        except BaseException:
            self.close()
            raise

    def describe(self) -> list:
        return [(name, [(key, int(field.kind), float(field.default), field.display_name or key,
                        field.category) for key, field in fields.items()])
                for name, (_, fields) in sorted(self._types.items())]

    def bind(self, world: int, bindings: list) -> int:
        self.end_scene()
        if not world:
            raise ValueError("Play world must not be null")
        # Validate type/property schema before constructing or activating any script.
        prepared = []
        for node_id, name, enabled, properties in bindings:
            if name not in self._types:
                raise ValueError("Missing Python Behaviour: " + name)
            script_type, fields = self._types[name]
            values, names = {}, set()
            for key, kind, number in properties:
                if key in names or key not in fields or int(fields[key].kind) != kind:
                    raise ValueError("Missing, duplicate or changed Python Export: " + key)
                names.add(key)
                values[key] = fields[key].from_native(number)
            prepared.append((node_id, script_type, bool(enabled), values))
        self._lease = _WorldLease(self._native, world)
        try:
            for node_id, script_type, enabled, values in prepared:
                instance = script_type()
                instance._node = Node(self._lease, node_id)
                for key, value in values.items():
                    setattr(instance, key, value)
                self._instances.append((instance, False))
                instance.on_create()
                if enabled:
                    # Mark active before callback so failed activation still receives cleanup.
                    self._instances[-1] = (instance, True)
                    instance.on_enable()
        except BaseException:
            try:
                self.end_scene()
            except BaseException:
                pass
            raise
        return len(self._instances)

    def tick(self, delta_seconds: float) -> None:
        if not math.isfinite(delta_seconds) or delta_seconds < 0:
            raise ValueError("Delta time must be finite and nonnegative")
        for instance, enabled in self._instances:
            if enabled:
                instance.on_update(delta_seconds)

    def end_scene(self) -> None:
        instances, self._instances = self._instances, []
        failures = []
        try:
            for instance, enabled in reversed(instances):
                if enabled:
                    try:
                        instance.on_disable()
                    except BaseException as error:
                        failures.append(type(error).__name__ + ": " + str(error))
                try:
                    instance.on_destroy()
                except BaseException as error:
                    failures.append(type(error).__name__ + ": " + str(error))
        finally:
            if self._lease:
                self._lease.handle = 0
            self._lease = None
        if failures:
            raise RuntimeError("Python lifecycle cleanup: " + "; ".join(failures))

    def close(self) -> None:
        try:
            self.end_scene()
        finally:
            self._types.clear()
            for name in self._modules:
                sys.modules.pop(name, None)
            self._modules.clear()

    def __del__(self):
        try:
            self.close()
        except BaseException:
            pass
