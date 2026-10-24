import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from ncma_tools.ecs_e5d_pipe_evidence import validate, verify, WORKLOADS

CONTRACT = json.loads((Path(__file__).resolve().parents[2] / "scripts/ecs-e5d-domain-contract.json").read_text(encoding="utf-8"))


class EcsPipeEvidenceTests(unittest.TestCase):
    def fixture(self, measured=False):
        c = CONTRACT["pipeReads"]
        rows = []
        for n in c["counts"] if measured else [8]:
            for w in WORKLOADS:
                sample = dict(RoundtripMilliseconds=2, OwnerThreadAllocatedBytes=80, PumpMilliseconds=.2,
                              PumpOwnerThreadAllocatedBytes=40, Pumps=2, ResponseBytes=1024,
                              FullCaptures=1 if w == "page_cold_revision" else 0)
                rows.append(dict(workload=w, objects=n, warmup=12 if measured else 1, sampleCount=100 if measured else 2,
                                 startupHash="A"*64, raw=[copy.deepcopy(sample) for _ in range(100 if measured else 2)],
                                 audit=dict(ProductionAuditIncluded=True, Instance="00000000-0000-0000-0000-000000000001",
                                            Session="00000000-0000-0000-0000-000000000002", Connection="00000000-0000-0000-0000-000000000003", RequestsVerified=113 if measured else 4,
                                            Files=[dict(path="synthetic-only", sha256="A"*64)]),
                                 semanticChecksPassed=True, pairingRequired=True, remoteWriteGrants=0, revokedReadRejected=True))
        return dict(schema=2, contractId=CONTRACT["id"], measured=measured,
                    scope="development actual-private-pipe probe; no formal package closure or complete E5-D acceptance",
                    roundtripScope=c["scope"], pumpScope=c["pumpScope"], allocationScope=c["allocationScope"],
                    runtime="synthetic validator fixture, not actual performance", processId=1, rows=rows,
                    memoryAcceptance="pending", targetBudgetAccepted=False, fullPerformanceAccepted=False, deployed=False)

    def test_complete_smoke_and_measurement_are_not_acceptance(self):
        for measured, rows, samples in ((False, 4, 8), (True, 12, 1200)):
            result = validate(self.fixture(measured), CONTRACT)
            self.assertEqual((result["rows"], result["samples"]), (rows, samples))
            self.assertFalse(result["formalClosureVerified"])
            self.assertFalse(result["fullPerformanceAccepted"])

    def test_matrix_and_authority_negatives(self):
        changes = (lambda x: x["rows"].pop(), lambda x: x["rows"].append(copy.deepcopy(x["rows"][0])),
                   lambda x: x["rows"][0].update(workload="fake_gpu"), lambda x: x.update(fullPerformanceAccepted=True),
                   lambda x: x.update(memoryAcceptance="accepted"), lambda x: x.update(targetBudgetAccepted=True),
                   lambda x: x.update(allocationScope="all process allocations"), lambda x: x.update(measured=True),
                   lambda x: x["rows"][0].update(pairingRequired=False), lambda x: x["rows"][0].update(remoteWriteGrants=1),
                   lambda x: x["rows"][0].update(revokedReadRejected=False), lambda x: x["rows"][0].update(semanticChecksPassed=False),
                   lambda x: x["rows"][0]["raw"].pop(), lambda x: x["rows"][0].update(startupHash="wrong"),
                   lambda x: x.update(schema=True), lambda x: x["rows"][0].update(warmup=True),
                   lambda x: x["rows"][0].update(sampleCount=2.0), lambda x: x.update(schema=1),
                   lambda x: x["rows"][0]["audit"].update(ProductionAuditIncluded=False),
                   lambda x: x["rows"][0]["audit"].update(RequestsVerified=3))
        for change in changes:
            item = self.fixture(); change(item)
            with self.assertRaises((ValueError, KeyError)):
                validate(item, CONTRACT)

    def test_raw_cost_negatives(self):
        changes = (lambda s: s.update(RoundtripMilliseconds=float("nan")), lambda s: s.update(PumpMilliseconds=-1),
                   lambda s: s.update(PumpMilliseconds=3), lambda s: s.update(OwnerThreadAllocatedBytes=-1),
                   lambda s: s.update(PumpOwnerThreadAllocatedBytes=81), lambda s: s.update(OwnerThreadAllocatedBytes=True),
                   lambda s: s.update(ResponseBytes=262145), lambda s: s.update(Pumps=0),
                   lambda s: s.update(FullCaptures=0), lambda s: s.update(extra=1), lambda s: s.pop("ResponseBytes"))
        for change in changes:
            item = self.fixture(); change(item["rows"][0]["raw"][0])
            with self.assertRaises((ValueError, KeyError)):
                validate(item, CONTRACT)

    def test_changed_predeclared_matrix_rejects(self):
        for key, value in (("pageLimit", 129), ("maxOutputBytes", 300000), ("counts", [8]), ("pollSleepMilliseconds", 0)):
            contract = copy.deepcopy(CONTRACT); contract["pipeReads"][key] = value
            with self.assertRaises(ValueError):
                validate(self.fixture(), contract)

    def test_development_files_are_bound_without_formal_acceptance(self):
        for mutation in ("none", "contract", "product", "missing", "duplicate", "escape", "wrong_name"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory(prefix="ncma-e5d-verifier-") as folder:
                root = Path(folder); archive = root/"out/verification/ecs-e5d/probe"; archive.mkdir(parents=True)
                products = root/"managed"; products.mkdir()
                contract_path = root/"contract.json"; contract_path.write_text(json.dumps(CONTRACT), encoding="utf-8")
                data = self.fixture(); data["contractSha256"] = hashlib.sha256(contract_path.read_bytes()).hexdigest().upper()
                for i, row in enumerate(data["rows"]):
                    audit = archive / (str(i)+".jsonl")
                    audit.write_text("\n".join(json.dumps(dict(requestId=str(j), code="ok", changed=False, approved=False,
                        instance=row["audit"]["Instance"], session=row["audit"]["Session"], connection=row["audit"]["Connection"])) for j in range(4)), encoding="utf-8")
                    row["audit"]["Files"] = [dict(path=str(audit), sha256=hashlib.sha256(audit.read_bytes()).hexdigest().upper())]
                data["products"] = []
                for name in ("Ncma.Editor.Transport.Tests", "Ncma.Editor.Transport", "Ncma.Editor.Protocol", "Ncma.Editor.Core", "Ncma.Scene", "Ncma.Runtime"):
                    path = products/(name+".dll"); path.write_bytes(b"synthetic input hash fixture; not executed")
                    data["products"].append(dict(name=name, path=str(path), sha256=hashlib.sha256(path.read_bytes()).hexdigest().upper()))
                if mutation == "contract": contract_path.write_text("{}", encoding="utf-8")
                if mutation == "product": path.write_bytes(b"changed")
                if mutation == "missing": data["products"].pop()
                if mutation == "duplicate": data["products"].append(data["products"][0])
                if mutation == "escape": data["products"][0]["path"] = str(root/"outside.dll")
                if mutation == "wrong_name": data["products"][0]["name"] = "Ncma.Mismatch"
                report = archive/"pipe-costs.json"; report.write_text(json.dumps(data), encoding="utf-8")
                if mutation == "none":
                    result = verify(report, contract_path, root)
                    self.assertEqual(result["developmentProductsChecked"], 6)
                    self.assertFalse(result["formalClosureVerified"])
                else:
                    with self.assertRaises((ValueError, KeyError, OSError)):
                        verify(report, contract_path, root)


if __name__ == "__main__":
    unittest.main()
