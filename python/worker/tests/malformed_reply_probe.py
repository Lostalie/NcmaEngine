"""TEST ONLY: corrupt real authenticated RPC replies AFTER production request validation.

No product import or command-line switch enables this entry. Finite named corruptions only;
tests independently verify the C# transport and ModuleSession refuse each result.
"""
import asyncio
import base64
import importlib.util
import json
from pathlib import Path

source = Path(__file__).resolve().parents[1] / "ncma_module_worker.py"
spec = importlib.util.spec_from_file_location("tested_ncma_worker", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class MalformedWorker(module.Worker):
    async def invoke(self, raw, context):
        good = await super().invoke(raw, context)
        value = json.loads(good)
        kind = json.loads(base64.b64decode(json.loads(raw)["payload"]))["text"]
        if kind == "unknown":
            value["extra"] = 1
        elif kind == "missing":
            del value["epoch"]
        elif kind == "duplicate":
            return b'{"version":2,' + good[1:]
        elif kind == "escaped-duplicate":
            return b'{"ver\\u0073ion":2,' + good[1:]
        elif kind == "utf8":
            return b"\xff\xfe"
        elif kind == "oversize":
            return b"x" * 131073
        elif kind == "version":
            value["version"] = 1  # Removed v1 must not be accepted by the v2 client.
        elif kind == "boolean":
            value["version"] = True
        elif kind == "instance":
            value["instance"] = "abc00000-1001-4001-8001-000000000001"
        elif kind in ("requestId", "sessionId", "outputSchema"):
            value[kind] = "abc00000-1001-4001-8001-000000000001"
        elif kind == "epoch":
            value["epoch"] += 1
        elif kind == "fingerprint":
            value["fingerprint"] = "A" * 64
        elif kind == "noncanonical-uuid":
            value["requestId"] = "ABC00000-1001-4001-8001-000000000001"
        elif kind == "lowercase-fingerprint":
            value["fingerprint"] = "a" * 64
        elif kind == "bad-status":
            value["status"] = 4
        elif kind == "unavailable-with-payload":
            value["status"] = 2
        elif kind == "bad-base64":
            value["payload"] = "!"
        elif kind == "noncanonical-base64":
            value["payload"] += "\n"
        elif kind == "empty-proposal":
            value["payload"] = ""
        elif kind == "duplicate-proposal":
            value["payload"] = base64.b64encode(b'{"a":1,"a":2}').decode("ascii")
        elif kind == "utf8-proposal":
            value["payload"] = base64.b64encode(b'{"a":"\xff"}').decode("ascii")
        elif kind == "oversize-proposal":
            value["payload"] = base64.b64encode(b'{"a":"' + b"x" * 65536 + b'"}').decode("ascii")
        elif kind == "deep-proposal":
            value["payload"] = base64.b64encode(b'{"a":' + b"[" * 17 + b"0" + b"]" * 17 + b"}").decode("ascii")
        elif kind == "unavailable":
            value["status"] = 2
            value["payload"] = ""
        elif kind == "rejected":
            value["status"] = 3
            value["payload"] = ""
        elif kind != "healthy":
            raise ValueError("unknown test corruption")
        return module.encode(value)


module.Worker = MalformedWorker
asyncio.run(module.main())
