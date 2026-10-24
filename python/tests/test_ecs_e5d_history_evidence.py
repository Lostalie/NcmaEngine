import copy
import json
from pathlib import Path
import unittest
from uuid import UUID

from ncma_tools.ecs_e5d_pipe_evidence import validate_history, verify_history_audit, HISTORY

CONTRACT = json.loads((Path(__file__).resolve().parents[2]/"scripts/ecs-e5d-domain-contract.json").read_text(encoding="utf-8"))


class EcsHistoryEvidenceTests(unittest.TestCase):
    def fixture(self, measured=False):
        fixtures, audits = [], []
        warmup, samples = (12, 100) if measured else (1, 2)
        for count in [100, 1000, 4096] if measured else [8]:
            rows = [dict(workload=w, raw=[]) for w in HISTORY]
            audit = []
            for i in range(warmup + samples):
                events = []
                for j, capability in enumerate(("ncma.scene.transaction", "ncma.history.undo", "ncma.history.redo")):
                    request = str(UUID(int=i*3+j+1)); before = i*3+j+1
                    events.append(dict(requestId=request, capability=capability, code="ok", changed=True, replayed=False, approved=True,
                                       approvingIdentity="trusted_editor_ui", expectedRevision=before, revision=before+1, executionRevision=before+1))
                    if i >= warmup:
                        rows[j]["raw"].append(dict(RequestId=request, RevisionBefore=before, RevisionAfter=before+1,
                            RoundtripMilliseconds=2, OwnerThreadAllocatedBytes=80, PumpMilliseconds=.2,
                            PumpOwnerThreadAllocatedBytes=40, Pumps=2, ResponseBytes=1024))
                denied = dict(events[0], code="permission_denied", changed=False, approved=False)
                retry = dict(events[0], replayed=True)
                audit.extend([denied, events[0], retry, events[1], events[2]])
            audit.extend([dict(denied), dict(denied)])
            fixtures.append(dict(objects=count, warmup=warmup, sampleCount=samples, startupHash="A"*64, finalHash="B"*64,
                semanticChecksPassed=True, pairingRequired=True, exactApprovalRequired=True, retryIdempotent=True,
                singleHistory=True, restoreInvalidatesHandles=True, revokedGrantRejected=True, revokedConnectionRejected=True,
                audit=dict(ProductionAuditIncluded=True, RequestsVerified=len(audit), Files=[{}]), rows=rows))
            audits.append(audit)
        return dict(schema=2, contractId=CONTRACT["id"], measured=measured, scope=CONTRACT["pipeHistory"]["scope"],
            allocationScope=CONTRACT["pipeReads"]["allocationScope"], runtime="synthetic validator fixture only", processId=1,
            fixtures=fixtures, formalClosureVerified=False, targetBudgetAccepted=False, memoryAcceptance="pending",
            fullPerformanceAccepted=False, deployed=False), audits

    def test_full_history_matrix_is_development_only(self):
        for measured, expected in ((False, (3, 6)), (True, (9, 900))):
            data, audits = self.fixture(measured)
            result = validate_history(data, CONTRACT)
            self.assertEqual((result["rows"], result["samples"]), expected)
            self.assertFalse(result["fullPerformanceAccepted"])
            for fixture, audit in zip(data["fixtures"], audits):
                verify_history_audit(fixture, audit)

    def test_history_matrix_authority_and_counter_negatives(self):
        changes = [lambda x: x.update(fullPerformanceAccepted=True), lambda x: x.update(formalClosureVerified=True),
            lambda x: x.update(memoryAcceptance="accepted"), lambda x: x.update(schema=1), lambda x: x["fixtures"].pop(),
            lambda x: x["fixtures"][0].update(exactApprovalRequired=False), lambda x: x["fixtures"][0].update(restoreInvalidatesHandles=False),
            lambda x: x["fixtures"][0].update(revokedGrantRejected=False), lambda x: x["fixtures"][0].update(retryIdempotent=False),
            lambda x: x["fixtures"][0]["audit"].update(ProductionAuditIncluded=False), lambda x: x["fixtures"][0]["rows"].reverse()]
        for key, value in (("RoundtripMilliseconds", float("inf")), ("PumpMilliseconds", 3), ("OwnerThreadAllocatedBytes", -1),
                           ("Pumps", False), ("ResponseBytes", 262145), ("RevisionAfter", 500), ("RequestId", str(UUID(int=0)))):
            changes.append(lambda x, k=key, v=value: x["fixtures"][0]["rows"][0]["raw"][0].update({k: v}))
        for change in changes:
            data, _ = self.fixture(); change(data)
            with self.assertRaises((ValueError, KeyError)):
                validate_history(data, CONTRACT)

    def test_persisted_history_audit_negatives(self):
        changes = (lambda r: r[0].update(code="ok"), lambda r: r[1].update(approved=False),
            lambda r: r[1].update(approvingIdentity="agent"), lambda r: r[1].update(replayed=True),
            lambda r: r[2].update(replayed=False), lambda r: r[2].update(executionRevision=999),
            lambda r: r[3].update(capability="ncma.scene.transaction"), lambda r: r[6].update(requestId=r[1]["requestId"]),
            lambda r: r[8].update(expectedRevision=999), lambda r: r[-1].update(approved=True))
        for change in changes:
            data, audits = self.fixture(); change(audits[0])
            with self.assertRaises(ValueError):
                verify_history_audit(data["fixtures"][0], audits[0])


if __name__ == "__main__":
    unittest.main()
