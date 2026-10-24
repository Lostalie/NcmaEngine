import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from uuid import UUID

from ncma_tools.ecs_e5d_mcp_evidence import validate, verify, verify_audit, WORKLOADS, PRODUCTS, HELPERS
import test_ecs_e5d_history_evidence as history_fixture

REPO = Path(__file__).resolve().parents[2]
CONTRACT = json.loads((REPO/"scripts/ecs-e5d-mcp-contract.json").read_text(encoding="utf-8"))


class EcsMcpEvidenceTests(unittest.TestCase):
    def fixture(self, measured=False):
        data, histories = history_fixture.EcsHistoryEvidenceTests().fixture(measured)
        data.update(schema=1, contractId=CONTRACT["id"], scope=CONTRACT["scope"], allocationScope=CONTRACT["allocationScope"], pumpScope=CONTRACT["pumpScope"])
        audits = []
        for index, (fixture, history) in enumerate(zip(data["fixtures"], histories)):
            cycles = fixture["warmup"] + fixture["sampleCount"]
            fixture.update(childProcessId=100+index, childStartedUtc="2026-10-20T00:00:00Z", childExitCode=0,
                           schemaValidated=True, cleanShutdown=True)
            reads = []
            def event(request, capability, revision):
                return dict(requestId=str(UUID(int=request)), capability=capability, expectedRevision=revision, revision=revision,
                            code="ok", changed=False, approved=False)
            records = [event(9000, "ncma.capabilities.list", 1), event(9001, "ncma.scene.inspect", 1)]
            for j, workload in enumerate(WORKLOADS[:4]):
                raw = []
                for i in range(cycles):
                    e = event(10000+j*cycles+i, "ncma.scene.object.inspect" if j == 3 else "ncma.scene.inspect", 2+i if j == 0 else 1+cycles)
                    records.append(e)
                    if i >= fixture["warmup"]:
                        raw.append(dict(RequestId=e["requestId"], RevisionBefore=e["revision"], RevisionAfter=e["revision"],
                            FullCaptures=1 if j == 0 else 0, RoundtripMilliseconds=2, OwnerThreadAllocatedBytes=80,
                            PumpMilliseconds=.2, PumpOwnerThreadAllocatedBytes=40, Pumps=2, ResponseBytes=1024, StructuredBytes=512))
                reads.append(dict(workload=workload, raw=raw))
            for row in fixture["rows"]:
                for cost in row["raw"]:
                    cost.update(FullCaptures=3, StructuredBytes=512)
                    cost["RevisionBefore"] += cycles; cost["RevisionAfter"] += cycles
            for e in history:
                for key in ("expectedRevision", "revision", "executionRevision"):
                    e[key] += cycles
            records.extend(history); fixture["rows"] = reads + fixture["rows"]
            fixture["audit"].update(RequestsVerified=len(records), Instance=str(UUID(int=1)), Session=str(UUID(int=2)), Connection=str(UUID(int=3)))
            for e in records:
                e.update(instance=str(UUID(int=1)), session=str(UUID(int=2)), connection=str(UUID(int=3)))
            audits.append(records)
        return data, audits

    def test_complete_matrix_never_accepts_performance(self):
        for measured, expected in ((False, (7, 14)), (True, (21, 2100))):
            data, audits = self.fixture(measured); result = validate(data, CONTRACT)
            self.assertEqual((result["rows"], result["samples"]), expected)
            self.assertFalse(result["formalClosureVerified"])
            for fixture, records in zip(data["fixtures"], audits): verify_audit(fixture, records)

    def test_mcp_scope_authority_child_and_sample_negatives(self):
        changes = [lambda x: x.update(fullPerformanceAccepted=True), lambda x: x.update(scope="in-process shortcut"),
            lambda x: x.update(memoryAcceptance="accepted"), lambda x: x.update(schema=True), lambda x: x["fixtures"].pop(),
            lambda x: x["fixtures"][0].update(childProcessId=x["processId"]), lambda x: x["fixtures"][0].update(childExitCode=1),
            lambda x: x["fixtures"][0].update(childStartedUtc="not-UTC"), lambda x: x["fixtures"][0].update(cleanShutdown=False),
            lambda x: x["fixtures"][0].update(exactApprovalRequired=False), lambda x: x["fixtures"][0].update(revokedConnectionRejected=False),
            lambda x: x["fixtures"][0]["audit"].update(ProductionAuditIncluded=False), lambda x: x["fixtures"][0]["rows"].reverse()]
        for key, value in (("RoundtripMilliseconds", float("nan")), ("PumpMilliseconds", 3), ("OwnerThreadAllocatedBytes", -1),
                           ("Pumps", False), ("StructuredBytes", 262145), ("ResponseBytes", 1048577), ("FullCaptures", 0), ("RevisionAfter", 500)):
            changes.append(lambda x, k=key, v=value: x["fixtures"][0]["rows"][0]["raw"][0].update({k: v}))
        for change in changes:
            data, _ = self.fixture(); change(data)
            with self.assertRaises((ValueError, KeyError)): validate(data, CONTRACT)

    def test_actual_file_chain_and_semantic_audit_negatives(self):
        # Synthetic file fixtures validate rejection logic; they never execute/measure a helper.
        for mutation in ("none", "helper", "missing_helper", "product", "audit", "audit_semantic", "domain", "escape"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory(prefix="ncma-mcp-probe-") as folder:
                root = Path(folder); archive = root/"out/verification/ecs-e5d/probe"; archive.mkdir(parents=True)
                managed = root/"managed"; managed.mkdir(); scripts = root/"scripts"; scripts.mkdir()
                contract_path = scripts/"ecs-e5d-mcp-contract.json"; contract_path.write_text(json.dumps(CONTRACT), encoding="utf-8")
                domain = scripts/"ecs-e5d-domain-contract.json"; domain.write_bytes((REPO/"scripts/ecs-e5d-domain-contract.json").read_bytes())
                def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()
                data, audits = self.fixture(); data["contractSha256"] = sha(contract_path); data["products"] = []
                for name in sorted(PRODUCTS):
                    p = managed/(name+".dll"); p.write_bytes(b"synthetic, not executed")
                    data["products"].append(dict(name=name, path=str(p), sha256=sha(p)))
                data["helperFiles"] = []
                for name in sorted(HELPERS):
                    p = managed/name
                    if not p.exists(): p.write_bytes(b"synthetic, not executed")
                    if name.endswith(".deps.json"): p.write_text(json.dumps(dict(libraries={"Ncma.Editor.Mcp/1.0.0": {}, "Ncma.Editor.Protocol/1.0.0": {}})), encoding="utf-8")
                    data["helperFiles"].append(dict(path=str(p), sha256=sha(p)))
                audit = archive/"audit.jsonl"
                if mutation == "audit_semantic": audits[0][0]["capability"] = "fake_catalog"
                audit.write_text("\n".join(json.dumps(e) for e in audits[0]), encoding="utf-8")
                data["fixtures"][0]["audit"]["Files"] = [dict(path=str(audit), sha256=sha(audit))]
                if mutation == "helper": (managed/"Ncma.Editor.Mcp.dll").write_bytes(b"changed")
                if mutation == "missing_helper": data["helperFiles"].pop()
                if mutation == "product": (managed/"Ncma.Scene.dll").write_bytes(b"changed")
                if mutation == "audit": audit.write_text("{}", encoding="utf-8")
                if mutation == "domain": domain.write_text("{}", encoding="utf-8")
                if mutation == "escape": data["helperFiles"][0]["path"] = str(root/"outside.dll")
                report = archive/"mcp-costs.json"; report.write_text(json.dumps(data), encoding="utf-8")
                if mutation == "none":
                    result = verify(report, contract_path, root); self.assertEqual(result["helperFilesChecked"], 4)
                    self.assertFalse(result["fullPerformanceAccepted"])
                else:
                    with self.assertRaises((ValueError, KeyError, OSError)): verify(report, contract_path, root)


if __name__ == "__main__": unittest.main()
