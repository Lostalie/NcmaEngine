"""Validate bounded E5-D development pipe probes, never formal/domain acceptance."""
from __future__ import annotations

import json
import math
from pathlib import Path
import sys
from uuid import UUID

from .ecs_e5_v2_evidence import child, digest, loads, read_text, require

WORKLOADS = ("page_cold_revision", "page_warm_first", "page_warm_last", "object_warm")
COSTS = {"RoundtripMilliseconds", "OwnerThreadAllocatedBytes", "PumpMilliseconds",
         "PumpOwnerThreadAllocatedBytes", "Pumps", "ResponseBytes", "FullCaptures"}
HISTORY = ("approved_transaction", "undo", "redo")
HISTORY_COSTS = COSTS - {"FullCaptures"} | {"RequestId", "RevisionBefore", "RevisionAfter"}


def number(value):
    return type(value) in (int, float) and math.isfinite(value) and value >= 0


def integer(value, maximum=(1 << 63)-1):
    return type(value) is int and 0 <= value <= maximum


def validate_history(data: dict, contract: dict) -> dict:
    require(type(contract["schema"]) is int and contract["schema"] == 2 and contract["id"] == "ecs-e5-d-domain-v2-audit-inclusive", "Wrong history contract")
    history, sampling = contract["pipeHistory"], contract["sampling"]
    require(history["counts"] == [100, 1000, 4096] and history["smokeCount"] == 8 and history["workloads"] == list(HISTORY) and
            sampling["warmup"] == 12 and sampling["samples"] == 100 and sampling["processes"] == 3, "Changed history matrix")
    require(type(data["schema"]) is int and data["schema"] == 2 and data["contractId"] == contract["id"] and type(data["measured"]) is bool,
            "Wrong history identity")
    require(data["scope"] == history["scope"] and data["allocationScope"] == contract["pipeReads"]["allocationScope"], "Changed history scope")
    require(data["memoryAcceptance"] == "pending" and all(data[k] is False for k in
            ("formalClosureVerified", "targetBudgetAccepted", "fullPerformanceAccepted", "deployed")), "False history acceptance")
    require(isinstance(data["runtime"], str) and data["runtime"] and integer(data["processId"]) and data["processId"] > 0, "Missing history runtime")
    counts = history["counts"] if data["measured"] else [8]
    require(len(data["fixtures"]) == len(counts) and [f["objects"] for f in data["fixtures"]] == counts, "History fixture matrix")
    for fixture in data["fixtures"]:
        require(type(fixture["objects"]) is int and type(fixture["warmup"]) is int and fixture["warmup"] == (12 if data["measured"] else 1) and
                type(fixture["sampleCount"]) is int and fixture["sampleCount"] == (100 if data["measured"] else 2), "History sample counts")
        for k in ("semanticChecksPassed", "pairingRequired", "exactApprovalRequired", "retryIdempotent", "singleHistory",
                  "restoreInvalidatesHandles", "revokedGrantRejected", "revokedConnectionRejected"):
            require(fixture[k] is True, "History semantic/authority check missing")
        for k in ("startupHash", "finalHash"):
            require(isinstance(fixture[k], str) and len(fixture[k]) == 64 and all(c in "0123456789ABCDEF" for c in fixture[k]), "History fixture hash")
        audit = fixture["audit"]
        require(audit["ProductionAuditIncluded"] is True and type(audit["RequestsVerified"]) is int and
                audit["RequestsVerified"] == 5 * (fixture["warmup"] + fixture["sampleCount"]) + 2 and 1 <= len(audit["Files"]) <= 4,
                "History audit scope")
        require([r["workload"] for r in fixture["rows"]] == list(HISTORY), "History workload matrix")
        request_ids = set()
        for row in fixture["rows"]:
            require(len(row["raw"]) == fixture["sampleCount"], "Incomplete history samples")
            for cost in row["raw"]:
                require(set(cost) == HISTORY_COSTS, "History cost fields")
                request_id = str(UUID(cost["RequestId"]))
                require(request_id not in request_ids and UUID(request_id).int != 0, "Duplicate history request")
                request_ids.add(request_id)
                require(number(cost["RoundtripMilliseconds"]) and number(cost["PumpMilliseconds"]) and
                        cost["PumpMilliseconds"] <= cost["RoundtripMilliseconds"], "History nested timing")
                for k in HISTORY_COSTS - {"RequestId", "RoundtripMilliseconds", "PumpMilliseconds"}:
                    require(integer(cost[k]), "History invalid counter")
                require(cost["RevisionAfter"] == cost["RevisionBefore"] + 1 and cost["Pumps"] > 0 and
                        0 < cost["ResponseBytes"] <= 262144 and cost["PumpOwnerThreadAllocatedBytes"] <= cost["OwnerThreadAllocatedBytes"], "History commit/cost boundary")
        for i in range(fixture["sampleCount"]):
            a, b, c = (r["raw"][i] for r in fixture["rows"])
            require(a["RevisionAfter"] == b["RevisionBefore"] and b["RevisionAfter"] == c["RevisionBefore"], "History cycle continuity")
            if i:
                require(fixture["rows"][2]["raw"][i-1]["RevisionAfter"] == a["RevisionBefore"], "History sample continuity")
    return dict(probeValidated=True, measured=data["measured"], rows=len(counts)*3, samples=sum(f["sampleCount"]*3 for f in data["fixtures"]),
                formalClosureVerified=False, targetBudgetAccepted=False, memoryAcceptance="pending", fullPerformanceAccepted=False, deployed=False)


def validate(data: dict, contract: dict) -> dict:
    require(type(contract["schema"]) is int and contract["schema"] == 2 and contract["id"] == "ecs-e5-d-domain-v2-audit-inclusive", "Wrong contract")
    reads, sampling = contract["pipeReads"], contract["sampling"]
    require(reads["counts"] == [100, 1000, 4096] and reads["workloads"] == list(WORKLOADS) and
            sampling["warmup"] == 12 and sampling["samples"] == 100 and sampling["processes"] == 3 and
            reads["smokeCount"] == 8 and reads["pageLimit"] == 128 and reads["detailLimit"] == 64 and
            reads["maxOutputBytes"] == 262144 and reads["pollSleepMilliseconds"] == 1 and
            reads["maxRequestSeconds"] == 10, "Changed probe matrix/bounds")
    require(type(data["schema"]) is int and data["schema"] == 2 and data["contractId"] == contract["id"] and type(data["measured"]) is bool,
            "Wrong development probe identity")
    require(data["scope"] == "development actual-private-pipe probe; no formal package closure or complete E5-D acceptance",
            "Scope promotion")
    for field, key in (("roundtripScope", "scope"), ("pumpScope", "pumpScope"), ("allocationScope", "allocationScope")):
        require(data[field] == reads[key], "Misrepresented timing/allocation scope")
    require(data["memoryAcceptance"] == "pending" and data["targetBudgetAccepted"] is False and
            data["fullPerformanceAccepted"] is False and data["deployed"] is False, "False acceptance")
    require(isinstance(data["runtime"], str) and data["runtime"] and integer(data["processId"]) and data["processId"] > 0,
            "Missing runtime/process identity")
    expected = {(w, n) for w in WORKLOADS for n in (reads["counts"] if data["measured"] else [8])}
    seen = set()
    for row in data["rows"]:
        key = (row["workload"], row["objects"])
        require(type(row["objects"]) is int and key in expected and key not in seen, "Missing/extra/duplicate matrix row")
        seen.add(key)
        require(type(row["warmup"]) is int and type(row["sampleCount"]) is int and row["warmup"] == (12 if data["measured"] else 1) and
                row["sampleCount"] == (100 if data["measured"] else 2) and
                len(row["raw"]) == row["sampleCount"], "Wrong samples/warmup")
        require(row["semanticChecksPassed"] is True and row["pairingRequired"] is True and
                type(row["remoteWriteGrants"]) is int and row["remoteWriteGrants"] == 0 and
                row["revokedReadRejected"] is True, "Read permission/semantic proof missing")
        audit = row["audit"]
        require(audit["ProductionAuditIncluded"] is True and type(audit["RequestsVerified"]) is int and
                audit["RequestsVerified"] == row["warmup"] + row["sampleCount"] + 1 and 1 <= len(audit["Files"]) <= 4,
                "Missing production audit proof")
        require(isinstance(row["startupHash"], str) and len(row["startupHash"]) == 64 and
                all(c in "0123456789ABCDEF" for c in row["startupHash"]), "Missing fixture identity")
        for sample in row["raw"]:
            require(set(sample) == COSTS, "Missing/unknown cost field")
            require(number(sample["RoundtripMilliseconds"]) and number(sample["PumpMilliseconds"]) and
                    sample["PumpMilliseconds"] <= sample["RoundtripMilliseconds"], "Invalid nested time")
            for field in COSTS - {"RoundtripMilliseconds", "PumpMilliseconds"}:
                require(integer(sample[field]), "Invalid integer counter")
            require(sample["PumpOwnerThreadAllocatedBytes"] <= sample["OwnerThreadAllocatedBytes"] and
                    0 < sample["ResponseBytes"] <= reads["maxOutputBytes"] and sample["Pumps"] > 0 and
                    sample["FullCaptures"] == (1 if row["workload"] == "page_cold_revision" else 0),
                    "Invalid allocation/page/proof boundary")
    require(seen == expected, "Incomplete workload matrix")
    return dict(probeValidated=True, measured=data["measured"], rows=len(seen),
                samples=sum(len(r["raw"]) for r in data["rows"]),
                formalClosureVerified=False, targetBudgetAccepted=False,
                memoryAcceptance="pending", fullPerformanceAccepted=False, deployed=False)


def verify(path: Path, contract_path: Path, repository: Path) -> dict:
    repository = repository.resolve()
    path = path.absolute()
    require(path.is_relative_to(repository / "out/verification/ecs-e5d"), "Not a development archive")
    path = child(repository, path.relative_to(repository).as_posix())
    for parent in (path, *path.parents):
        require(not parent.is_symlink() and not (hasattr(parent, "is_junction") and parent.is_junction()), "Reparse probe")
        if parent == repository:
            break
    data, contract = loads(read_text(path)), loads(read_text(contract_path))
    require(data["contractSha256"] == digest(contract_path), "Contract bytes changed")
    history = "fixtures" in data
    result = validate_history(data, contract) if history else validate(data, contract)
    for row in data["fixtures"] if history else data["rows"]:
        records = []
        files = set()
        for item in row["audit"]["Files"]:
            declared = Path(item["path"])
            require(declared.is_absolute() and declared.is_relative_to(path.parent) and declared not in files, "Audit escaped/duplicated")
            files.add(declared)
            actual = child(repository, declared.relative_to(repository).as_posix())
            require(digest(actual) == item["sha256"], "Audit bytes changed")
            records.extend(loads(line) for line in read_text(actual).splitlines())
        require(len(records) == row["audit"]["RequestsVerified"], "Audit cardinality")
        for record in records:
            for key in ("instance", "session", "connection"):
                require(str(UUID(record[key])) == str(UUID(row["audit"][key.title()])), "Audit identity changed")
        if history:
            verify_history_audit(row, records)
        else:
            ids = [r["requestId"] for r in records]
            require(len(set(ids)) == len(ids) and all(r["code"] == "ok" and r["changed"] is False and r["approved"] is False for r in records), "Read audit changed/duplicated")
    products = set()
    for item in data["products"]:
        require(item["name"] not in products and isinstance(item["name"], str) and item["name"].startswith("Ncma."), "Duplicate/unknown product")
        products.add(item["name"])
        declared = Path(item["path"])
        require(declared.is_absolute() and declared.is_relative_to(repository / "managed") and declared.name == item["name"] + ".dll", "Unexpected development product")
        actual = child(repository, declared.relative_to(repository).as_posix())
        require(digest(actual) == item["sha256"], "Loaded product changed after probe")
    require({"Ncma.Editor.Transport.Tests", "Ncma.Editor.Transport", "Ncma.Editor.Protocol", "Ncma.Editor.Core", "Ncma.Scene", "Ncma.Runtime"} <= products,
            "Missing actual pipe/scene products")
    return dict(result, archiveSha256=digest(path), contractSha256=digest(contract_path), developmentProductsChecked=len(products))


def verify_history_audit(fixture: dict, records: list) -> None:
    """Check actual persisted permission/commit/retry/history records, not just flags."""
    cycles = fixture["warmup"] + fixture["sampleCount"]
    seen = set()
    for i in range(cycles):
        denied, write, retry, undo, redo = records[i*5:i*5+5]
        require(denied["code"] == "permission_denied" and denied["changed"] is False and denied["approved"] is False and
                denied["requestId"] == write["requestId"] == retry["requestId"], "Audit exact default-deny request")
        for event, capability in ((write, "ncma.scene.transaction"), (undo, "ncma.history.undo"), (redo, "ncma.history.redo")):
            require(event["capability"] == capability and event["code"] == "ok" and event["changed"] is True and event["replayed"] is False and
                    event["approved"] is True and event["approvingIdentity"] == "trusted_editor_ui" and event["requestId"] not in seen,
                    "Audit authorized single commit")
            seen.add(event["requestId"])
        require(retry["replayed"] is True and retry["approved"] is True and retry["revision"] == write["revision"] and
                retry["executionRevision"] == write["executionRevision"], "Audit idempotent retry")
        if i >= fixture["warmup"]:
            for row, event in zip(fixture["rows"], (write, undo, redo)):
                cost = row["raw"][i - fixture["warmup"]]
                require(cost["RequestId"] == event["requestId"] and cost["RevisionBefore"] == event["expectedRevision"] and
                        cost["RevisionAfter"] == event["revision"], "Raw history cost differs from audit")
    require(all(r["code"] == "permission_denied" and r["changed"] is False and r["approved"] is False for r in records[-2:]), "Audit revoked grant bypass")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("Usage: ecs_e5d_pipe_evidence <probe.json> <contract.json> <repository>")
    print(json.dumps(verify(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])), indent=2))
