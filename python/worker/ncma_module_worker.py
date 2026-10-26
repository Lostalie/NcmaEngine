"""Authenticated loopback gRPC deterministic tool. No gameplay, eval, filesystem or model IO.

Bootstrap is a single bounded JSON line on inherited stdin. EOF requests shutdown.
gRPC message bodies use strict UTF-8 JSON custom marshalling (not protobuf).
This trusted process is NOT an operating-system security sandbox.
"""
import asyncio
import base64
import hashlib
import hmac
import json
import math
import os
import sys
import uuid

try:
    import grpc
except ImportError:
    sys.stderr.write("module worker dependency unavailable\n")
    raise SystemExit(2)

MAX_WIRE = 131072
MAX_PAYLOAD = 65536
VERSION = 2
SERVICE = "ncma.modules.v2.Worker"
OPERATION = {
    "moduleId": "cde08200-1001-4001-8001-000000000001",
    "moduleVersion": "1.0",
    "configurationId": "cde08200-1001-4001-8001-000000000004",
    "name": "ncma.tools.text_stats",
    "inputSchema": "cde08200-1001-4001-8001-000000000002",
    "outputSchema": "cde08200-1001-4001-8001-000000000003",
    "maxInputBytes": MAX_PAYLOAD,
    "maxOutputBytes": MAX_PAYLOAD,
    "timeoutMilliseconds": 10000,
}


def closed(value, fields):
    if type(value) is not dict or set(value) != set(fields):
        raise ValueError("closed object required")
    return value


def pairs(items):
    value = {}
    for key, item in items:
        if key in value:
            raise ValueError("duplicate member")
        key.encode("utf-8", "strict")
        value[key] = item
    return value


def reject_number(_):
    raise ValueError("unsupported number")


def decode(data, maximum=MAX_WIRE):
    if not 2 <= len(data) <= maximum:
        raise ValueError("message budget")
    text = data.decode("utf-8", "strict")
    depth = 0
    quoted = False
    escaped = False
    for char in text:
        if quoted:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                quoted = False
        elif char == '"':
            quoted = True
        elif char in "{[":
            depth += 1
            if depth > 8:
                raise ValueError("depth budget")
        elif char in "}]":
            depth -= 1
    return json.loads(text, object_pairs_hook=pairs, parse_constant=reject_number,
                      parse_float=reject_number)


def encode(value):
    data = json.dumps(value, ensure_ascii=True, allow_nan=False, separators=(",", ":")).encode("utf-8")
    if len(data) > MAX_WIRE:
        raise ValueError("message budget")
    return data


def identity(value, empty=False):
    if type(value) is not str or len(value) != 36:
        raise ValueError("identity")
    parsed = uuid.UUID(value)
    if str(parsed) != value or (not empty and parsed.int == 0):
        raise ValueError("identity")
    return value


def integer(value, minimum=0, maximum=(1 << 63) - 1):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError("integer")
    return value


def authoring_context(value):
    # V2 has no runtime World/tick aliases or compatibility defaults. All eleven fields
    # are required, including explicit null selection and the empty local-only endpoint.
    closed(value, ("projectId", "hostSessionId", "editSessionId", "documentSessionId",
                   "documentGeneration", "revision", "behaviourCatalogGeneration", "reviewEpoch",
                   "selection", "endpointId", "audienceRevision"))
    for field in ("projectId", "hostSessionId", "editSessionId", "documentSessionId"):
        identity(value[field])
    maximum = (1 << 64) - 1
    integer(value["documentGeneration"], 1, maximum)
    integer(value["revision"], 0, maximum)
    integer(value["reviewEpoch"], 1, maximum)
    integer(value["audienceRevision"], 0, maximum)
    identity(value["behaviourCatalogGeneration"], empty=True)
    identity(value["endpointId"], empty=True)
    if value["selection"] is not None:
        identity(value["selection"])
    if uuid.UUID(value["endpointId"]).int == 0 and value["audienceRevision"] != 0:
        raise ValueError("audience without endpoint")
    return value


def operation(value):
    closed(value, OPERATION)
    # Registered code/schema identities cannot be selected by arbitrary input.
    for key in OPERATION:
        if key not in ("timeoutMilliseconds", "maxInputBytes", "maxOutputBytes") and value[key] != OPERATION[key]:
            raise ValueError("operation identity")
    integer(value["timeoutMilliseconds"], 1, 60000)
    integer(value["maxInputBytes"], 2, MAX_PAYLOAD)
    integer(value["maxOutputBytes"], 2, MAX_PAYLOAD)
    return value


class Worker:
    def __init__(self, bootstrap):
        closed(bootstrap, ("version", "instance", "secret", "keyPem", "certificatePem", "operation"))
        integer(bootstrap["version"], VERSION, VERSION)
        self.instance = identity(bootstrap["instance"])
        secret = bootstrap["secret"]
        if type(secret) is not str or len(secret) != 64 or secret != bytes.fromhex(secret).hex().upper():
            raise ValueError("bootstrap credential")
        self.authorization = "Bearer " + secret
        self.operation = operation(bootstrap["operation"])
        self.session = None
        self.epoch = 0
        self.seen = set()
        self.ready = False
        self.work = set()
        self.stopping = False

    def completed(self, task):
        self.work.discard(task)
        if not task.cancelled():
            task.exception()  # Observe even a disconnected caller's failure.

    async def calculate(self, text, utf8):
        # Only this registered bounded deterministic operation exists. No executor/thread spawn.
        await asyncio.sleep(0)
        return {"codePoints": len(text), "utf8Bytes": len(utf8),
                "sha256": hashlib.sha256(utf8).hexdigest().upper()}

    async def execute(self, text, utf8, context, deadline):
        await self.check_deadline(context, deadline)
        result = encode(await self.calculate(text, utf8))
        if len(result) > self.operation["maxOutputBytes"]:
            raise ValueError("output budget")
        await self.check_deadline(context, deadline)
        return result

    async def stop(self):
        self.stopping = True
        tasks = tuple(self.work)
        for task in tasks:
            task.cancel()
        if tasks:
            _, pending = await asyncio.wait(tasks, timeout=2)
            return not pending
        return True

    async def authenticate(self, context):
        values = [item.value for item in context.invocation_metadata() if item.key == "authorization"]
        if len(values) != 1 or type(values[0]) is not str or len(values[0]) != 71 or not values[0].isascii() or not hmac.compare_digest(values[0], self.authorization):
            await context.abort(grpc.StatusCode.UNAUTHENTICATED, "module authentication failed")

    async def deadline(self, context):
        remaining = context.time_remaining()
        if remaining is None or not math.isfinite(remaining):
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "module deadline required")
        if remaining <= 0:
            await context.abort(grpc.StatusCode.DEADLINE_EXCEEDED, "module deadline expired")
        return asyncio.get_running_loop().time() + min(remaining, self.operation["timeoutMilliseconds"] / 1000)

    @staticmethod
    async def check_deadline(context, deadline):
        if context.cancelled() or asyncio.get_running_loop().time() >= deadline:
            await context.abort(grpc.StatusCode.DEADLINE_EXCEEDED, "module deadline expired")

    async def handshake(self, raw, context):
        await self.authenticate(context)
        deadline = await self.deadline(context)
        try:
            value = closed(decode(raw), ("version", "instance", "nonce", "operation"))
            integer(value["version"], VERSION, VERSION)
            if identity(value["instance"]) != self.instance or operation(value["operation"]) != self.operation or self.ready:
                raise ValueError("handshake identity")
            nonce = identity(value["nonce"])
            await self.check_deadline(context, deadline)
            self.ready = True
            return encode({"version": VERSION, "instance": self.instance, "nonce": nonce,
                           "processId": os.getpid(), "maxWireBytes": MAX_WIRE})
        except (ValueError, TypeError, KeyError, UnicodeError, RecursionError):
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "invalid module handshake")

    async def invoke(self, raw, context):
        await self.authenticate(context)
        deadline = await self.deadline(context)
        try:
            if not self.ready or self.stopping:
                raise ValueError("handshake required")
            value = closed(decode(raw), ("version", "instance", "requestId", "sessionId", "epoch", "fingerprint", "operation", "context", "payload"))
            integer(value["version"], VERSION, VERSION)
            if identity(value["instance"]) != self.instance or operation(value["operation"]) != self.operation:
                raise ValueError("worker identity")
            request = identity(value["requestId"])
            session = identity(value["sessionId"])
            epoch = integer(value["epoch"], 1)
            if (self.session is not None and self.session != session) or epoch < self.epoch or request in self.seen or len(self.seen) >= 4096:
                raise ValueError("replay/session budget")
            fingerprint = value["fingerprint"]
            if type(fingerprint) is not str or len(fingerprint) != 64 or fingerprint != bytes.fromhex(fingerprint).hex().upper():
                raise ValueError("fingerprint")
            authoring_context(value["context"])
            if type(value["payload"]) is not str or len(value["payload"]) > 87384:
                raise ValueError("payload budget")
            payload = base64.b64decode(value["payload"], validate=True)
            if len(payload) > self.operation["maxInputBytes"] or base64.b64encode(payload).decode("ascii") != value["payload"]:
                raise ValueError("payload budget")
            text = closed(decode(payload, MAX_PAYLOAD), ("text",))["text"]
            if type(text) is not str:
                raise ValueError("text required")
            utf8 = text.encode("utf-8", "strict")
            await self.check_deadline(context, deadline)
            # An RPC cancellation is not proof that its work stopped. Own the task separately;
            # a cancellation-resistant operation retains a slot until its actual terminal state.
            if len(self.work) >= 4:
                await context.abort(grpc.StatusCode.RESOURCE_EXHAUSTED, "module work budget")
            self.seen.add(request)
            self.session = session
            self.epoch = epoch
            task = asyncio.create_task(self.execute(text, utf8, context, deadline))
            self.work.add(task)
            task.add_done_callback(self.completed)
            try:
                result = await asyncio.shield(task)
            except asyncio.CancelledError:
                task.cancel()
                raise
            return encode({"version": VERSION, "instance": self.instance, "requestId": request,
                           "sessionId": session, "epoch": epoch, "fingerprint": fingerprint,
                           "outputSchema": self.operation["outputSchema"], "status": 1,
                           "payload": base64.b64encode(result).decode("ascii")})
        except (ValueError, TypeError, KeyError, UnicodeError, RecursionError):
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "invalid module request")


async def main():
    if grpc.__version__ != "1.84.0":
        raise ValueError("worker dependency version")
    bootstrap = decode(sys.stdin.buffer.readline(16385), 16384)
    worker = Worker(bootstrap)
    for field in ("keyPem", "certificatePem"):
        if type(bootstrap[field]) is not str or not 1 <= len(bootstrap[field]) <= 8192:
            raise ValueError("credential budget")
    credentials = grpc.ssl_server_credentials(((bootstrap["keyPem"].encode("ascii"),
                                                 bootstrap["certificatePem"].encode("ascii")),))
    server = grpc.aio.server(options=(("grpc.max_receive_message_length", MAX_WIRE),
                                     ("grpc.max_send_message_length", MAX_WIRE),
                                     ("grpc.max_metadata_size", 2048)), maximum_concurrent_rpcs=4)
    server.add_generic_rpc_handlers((grpc.method_handlers_generic_handler(SERVICE, {
        "Handshake": grpc.unary_unary_rpc_method_handler(worker.handshake, request_deserializer=lambda data: data, response_serializer=lambda data: data),
        "Invoke": grpc.unary_unary_rpc_method_handler(worker.invoke, request_deserializer=lambda data: data, response_serializer=lambda data: data),
    }),))
    port = server.add_secure_port("127.0.0.1:0", credentials)
    if not port:
        raise ValueError("loopback listener")
    try:
        await server.start()
        print(encode({"version": VERSION, "instance": worker.instance, "port": port,
                      "processId": os.getpid()}).decode("ascii"), flush=True)
        # The launcher owns this pipe. No command text is evaluated; EOF/one byte means stop.
        await asyncio.to_thread(sys.stdin.buffer.read, 1)
    finally:
        await server.stop(2)
        if not await worker.stop():
            # Do not claim successful unload. The process owner retains a non-exiting worker;
            # only that owner can explicitly approve termination of its exact process handle.
            raise RuntimeError("module work did not drain")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception:
        sys.stderr.write("module worker failed\n")
        raise SystemExit(2)
