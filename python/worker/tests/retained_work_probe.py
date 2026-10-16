"""TEST ONLY: cancellation-resistant calculation under the REAL Worker admission/transport.

Not a product entry or registered operation. Adds a credential-protected test observation/release
RPC; it never executes supplied code. Production worker does not import this module.
"""
import asyncio
import importlib.util
from pathlib import Path

source = Path(__file__).resolve().parents[1] / "ncma_module_worker.py"
spec = importlib.util.spec_from_file_location("tested_ncma_worker", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
worker = None


class RetainedWorker(module.Worker):
    def __init__(self, bootstrap):
        super().__init__(bootstrap)
        global worker
        worker = self
        self.release = asyncio.Event()
        self.started = 0
        self.cancelled_count = 0
        self.finished = 0

    async def calculate(self, text, utf8):
        if text == "test-hold":
            self.started += 1
            try:
                while not self.release.is_set():
                    try:
                        await self.release.wait()
                    except asyncio.CancelledError:
                        self.cancelled_count += 1
            finally:
                self.finished += 1
        return await super().calculate(text, utf8)


async def probe(raw, context):
    await worker.authenticate(context)
    if raw == b"release":
        worker.release.set()
    elif raw != b"status":
        await context.abort(module.grpc.StatusCode.INVALID_ARGUMENT, "invalid test probe")
    return module.encode({"started": worker.started, "cancelled": worker.cancelled_count,
                          "finished": worker.finished, "retained": len(worker.work)})


original_handlers = module.grpc.method_handlers_generic_handler


def test_handlers(service, handlers):
    copy = dict(handlers)
    copy["TestProbe"] = module.grpc.unary_unary_rpc_method_handler(
        probe, request_deserializer=lambda raw: raw, response_serializer=lambda raw: raw)
    return original_handlers(service, copy)


module.Worker = RetainedWorker
module.grpc.method_handlers_generic_handler = test_handlers
asyncio.run(module.main())
