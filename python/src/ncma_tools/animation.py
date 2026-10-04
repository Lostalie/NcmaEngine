"""Isolated tool-owned clock/policy/history over immutable numerical animation ABI 2."""
from __future__ import annotations

import copy
import ctypes
import json
import math
import threading
from pathlib import Path
from typing import Any


def identity() -> dict:
    return {"position": [0., 0., 0.], "rotation_xyzw": [0., 0., 0., 1.], "scale": [1., 1., 1.]}


def transform(values) -> dict:
    return {"position": list(values[:3]), "rotation_xyzw": list(values[3:7]), "scale": list(values[7:10])}


def combine(a: dict, b: dict) -> dict:
    x, y, z, w = a["rotation_xyzw"]
    u, v, t, s = b["rotation_xyzw"]
    q = [w*u+x*s+y*t-z*v, w*v-x*t+y*s+z*u, w*t+x*v-y*u+z*s, w*s-x*u-y*v-z*t]
    norm = math.sqrt(sum(i*i for i in q))
    p = [a["scale"][i]*b["position"][i] for i in range(3)]
    cross = [y*p[2]-z*p[1], z*p[0]-x*p[2], x*p[1]-y*p[0]]
    cross2 = [y*cross[2]-z*cross[1], z*cross[0]-x*cross[2], x*cross[1]-y*cross[0]]
    return {"position": [a["position"][i]+p[i]+2*(w*cross[i]+cross2[i]) for i in range(3)],
            "rotation_xyzw": [i/norm for i in q], "scale": [a["scale"][i]*b["scale"][i] for i in range(3)]}


class AnimationSession:
    HISTORY_LIMIT = 128

    def __init__(self, root: Path):
        root = root.resolve(strict=True)
        library = (root / "out" / "managed" / "NcmaNative.dll").resolve(strict=True)
        if not library.is_relative_to(root):
            raise ValueError("Native library must remain inside the selected engine project")
        self._owner = threading.get_ident()
        self._native = ctypes.CDLL(str(library))
        u32, u64, f32, f64 = ctypes.c_uint32, ctypes.c_uint64, ctypes.c_float, ctypes.c_double
        ptr, counts, floats = ctypes.c_void_p, ctypes.POINTER(u32), ctypes.POINTER(f32)
        self._bind("ncma_animation_abi_version", [], u32)
        self._bind("ncma_animation_create", [u32], u64)
        self._bind("ncma_animation_destroy", [u64], None)
        self._bind("ncma_animation_read_library", [u64, ptr, u32, counts], ctypes.c_uint8)
        self._bind("ncma_animation_read_error", [ptr, u32, counts], ctypes.c_uint8)
        self._bind("ncma_animation_sample", [u64, u32, f64, floats, u32, f32, floats, u32, counts], ctypes.c_uint8)
        self._bind("ncma_animation_motion", [u64, u32, f64, f64, floats, u32, counts], ctypes.c_uint8)
        self._bind("ncma_animation_notifies", [u64, u32, f64, f64, ptr, u32, counts], ctypes.c_uint8)
        self._handle = 0
        if self._native.ncma_animation_abi_version() != 2:
            raise RuntimeError("Animation numerical ABI 2 required; rebuild NcmaEngine")
        self._handle = self._native.ncma_animation_create(2)
        if not self._handle:
            raise RuntimeError(self._error())
        try:
            self._metadata = self._read(self._native.ncma_animation_read_library, self._handle)
            self._bones = len(self._metadata["bones"])
            self._clips = self._metadata["clips"]
            self._undo, self._redo = [], []
            self._revision = 0
            self._state = self._initial()
        except BaseException:
            self.close()
            raise

    def _bind(self, name: str, args: list[Any], result: Any) -> None:
        function = getattr(self._native, name)
        function.argtypes, function.restype = args, result

    def _error(self) -> str:
        function = self._native.ncma_animation_read_error
        count = ctypes.c_uint32()
        if function(None, 0, ctypes.byref(count)) != 2 or not 1 <= count.value <= 4096:
            return "Animation kernel error"
        buffer = ctypes.create_string_buffer(count.value)
        if function(buffer, len(buffer), ctypes.byref(count)) != 1:
            return "Error copy failed"
        return buffer.value.decode("utf-8", errors="strict")

    def _read(self, function, *arguments):
        count = ctypes.c_uint32()
        if function(*arguments, None, 0, ctypes.byref(count)) != 2 or not 1 <= count.value <= 1048576:
            raise RuntimeError(self._error())
        buffer = ctypes.create_string_buffer(count.value)
        if function(*arguments, buffer, len(buffer), ctypes.byref(count)) != 1:
            raise RuntimeError(self._error())
        return json.loads(buffer.value)

    def _require_open(self) -> None:
        if threading.get_ident() != self._owner:
            raise RuntimeError("Animation owner thread required")
        if not self._handle:
            raise RuntimeError("Animation session is closed")

    def _sample(self, state):
        count = self._bones*26
        output = (ctypes.c_float*count)()
        source = (ctypes.c_float*len(state["from"]))(*state["from"]) if state["blend_duration"] else None
        required = ctypes.c_uint32()
        weight = min(1., state["blend_time"]/state["blend_duration"]) if state["blend_duration"] else 1.
        if self._native.ncma_animation_sample(self._handle, state["clip"], state["time"], source,
                                             len(source) if source is not None else 0, weight, output, count, ctypes.byref(required)) != 1:
            raise RuntimeError(self._error())
        if required.value != count:
            raise RuntimeError("Invalid sample count")
        state["pose"] = list(output)

    def _initial(self):
        state = {"clip": 0, "time": 0., "speed": 0., "paused": True, "action_active": False,
                 "hit_window": False, "combo_window": False, "invulnerable": False,
                 "blend_time": 0., "blend_duration": 0., "from": [], "events": [],
                 "actor": identity(), "root_delta": identity()}
        self._sample(state)
        return state

    def _play(self, state, name, blend):
        state["from"] = state["pose"][:self._bones*10]
        state["clip"] = next(i for i, c in enumerate(self._clips) if c["name"] == name)
        state["time"] = state["blend_time"] = 0.
        state["blend_duration"] = blend
        state["root_delta"], state["events"] = identity(), []
        self._sample(state)

    def _advance(self, state, seconds):
        if isinstance(seconds, bool) or not isinstance(seconds, (int, float)) or not math.isfinite(seconds) or not 0 <= seconds <= 1:
            raise ValueError("Step seconds must be finite within 0..1")
        root, events, elapsed = identity(), [], 0.
        while seconds > 0:
            clip = self._clips[state["clip"]]
            rate = 1. if state["action_active"] or state["speed"] == 0 else .5+state["speed"]*.5
            segment = min(seconds, max(0., clip["duration"]-state["time"])/rate) if state["action_active"] else seconds
            start = state["time"]
            end = start+segment*rate if clip["loop"] else min(start+segment*rate, clip["duration"])
            motion, count = (ctypes.c_float*10)(), ctypes.c_uint32()
            if self._native.ncma_animation_motion(self._handle, state["clip"], start, end, motion, 10, ctypes.byref(count)) != 1:
                raise RuntimeError(self._error())
            delta = transform(motion)
            state["actor"], root = combine(state["actor"], delta), combine(root, delta)
            for notify in self._read(self._native.ncma_animation_notifies, self._handle, state["clip"], start, end):
                changes = {"Hit.Start": ("hit_window", True), "Hit.End": ("hit_window", False),
                           "Combo.Open": ("combo_window", True), "Combo.Close": ("combo_window", False),
                           "Invulnerability.Start": ("invulnerable", True), "Invulnerability.End": ("invulnerable", False)}
                if notify["name"] in changes:
                    key, flag = changes[notify["name"]]
                    state[key] = flag
                notify["offset"] = elapsed+notify["offset"]/rate
                events.append(notify)
            state["time"] = end % clip["duration"] if clip["loop"] else end
            state["blend_time"] = min(state["blend_time"]+segment*rate, state["blend_duration"])
            self._sample(state)
            seconds, elapsed = max(0., seconds-segment), elapsed+segment
            if state["action_active"] and state["time"] >= clip["duration"]:
                state["action_active"] = state["hit_window"] = state["combo_window"] = state["invulnerable"] = False
                self._play(state, "Run" if state["speed"] > .1 else "Idle", .15)
            else:
                break
        state["root_delta"], state["events"] = root, events

    def inspect(self) -> dict[str, Any]:
        self._require_open()
        state = self._state
        clip = self._clips[state["clip"]]
        bones = [{"name": b["name"], "parent": b["parent"],
                  "position": state["pose"][self._bones*10+i*16+12:self._bones*10+i*16+15],
                  "local": transform(state["pose"][i*10:i*10+10])}
                 for i, b in enumerate(self._metadata["bones"])]
        return copy.deepcopy({"schema_version": 1, "session_kind": "action_animation_preview", "revision": self._revision,
                "state": clip["name"], "time": state["time"], "duration": clip["duration"],
                "blend_weight": min(1., state["blend_time"]/state["blend_duration"]) if state["blend_duration"] else 1.,
                **{key: state[key] for key in ("speed", "paused", "action_active", "hit_window", "combo_window",
                                               "invulnerable", "actor", "root_delta", "events")},
                "can_undo": bool(self._undo), "can_redo": bool(self._redo), "skeleton_uuid": self._metadata["skeleton_uuid"],
                "bones": bones, "clips": self._clips})

    def command(self, command: int, value: float = 0, text: str = "") -> dict[str, Any]:
        self._require_open()
        if isinstance(value, bool) or not math.isfinite(value):
            raise ValueError("Command value must be finite")
        if command in (6, 7):
            source, target = (self._undo, self._redo) if command == 6 else (self._redo, self._undo)
            if not source:
                raise RuntimeError("Animation history is empty")
            target.append(self._state)
            self._state = source.pop()
        else:
            state = copy.deepcopy(self._state)
            if command == 1:
                if not 0 <= value <= 1:
                    raise ValueError("Speed must be within 0..1")
                state["speed"] = value
                name = "Run" if value > .1 else "Idle"
                if not state["action_active"] and self._clips[state["clip"]]["name"] != name:
                    self._play(state, name, .15)
            elif command == 2:
                if text not in ("Attack", "Dodge"):
                    raise ValueError("Action must be Attack or Dodge")
                if state["action_active"] and not (text == "Attack" and state["combo_window"]):
                    raise RuntimeError("Action locked; Attack chains only in combo window")
                self._play(state, text, .08 if text == "Attack" else .05)
                state["action_active"] = True
                state["hit_window"] = state["combo_window"] = state["invulnerable"] = False
            elif command == 3:
                if value not in (0, 1):
                    raise ValueError("Paused must be 0 or 1")
                state["paused"] = value == 1
            elif command == 4:
                self._advance(state, value)
            elif command == 5:
                state = self._initial()
            else:
                raise ValueError("Unknown animation command")
            if command in (1, 2, 3):
                state["events"], state["root_delta"] = [], identity()
            self._undo.append(self._state)
            self._undo = self._undo[-self.HISTORY_LIMIT:]
            self._redo.clear()
            self._state = state
        self._revision += 1
        return self.inspect()

    def close(self) -> None:
        if threading.get_ident() != self._owner:
            raise RuntimeError("Animation owner thread required")
        if self._handle:
            self._native.ncma_animation_destroy(self._handle)
            self._handle = 0

    def __enter__(self) -> AnimationSession:
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()
