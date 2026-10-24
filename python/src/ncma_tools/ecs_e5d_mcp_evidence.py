"""Development-only actual stdio observations. Not a target or formal package gate."""
from datetime import datetime
import json
from pathlib import Path
import sys
from uuid import UUID

from .ecs_e5_v2_evidence import child, digest, loads, read_text, require
from .ecs_e5d_pipe_evidence import number, integer, verify_history_audit

WORKLOADS = ("page_cold_revision", "page_warm_first", "page_warm_last", "object_warm", "approved_transaction", "undo", "redo")
COSTS = {"RequestId", "RevisionBefore", "RevisionAfter", "FullCaptures", "RoundtripMilliseconds", "OwnerThreadAllocatedBytes",
         "PumpMilliseconds", "PumpOwnerThreadAllocatedBytes", "Pumps", "ResponseBytes", "StructuredBytes"}
PRODUCTS = {"Ncma.Editor.Transport.Tests", "Ncma.Editor.Transport", "Ncma.Editor.Protocol", "Ncma.Editor.Core", "Ncma.Scene", "Ncma.Runtime"}
HELPERS = {"Ncma.Editor.Mcp.dll", "Ncma.Editor.Mcp.deps.json", "Ncma.Editor.Mcp.runtimeconfig.json", "Ncma.Editor.Protocol.dll"}


def validate(data, contract):
    require(type(contract["schema"]) is int and contract["schema"] == 1 and contract["id"] == "ecs-e5-d-mcp-stdio-v1", "MCP contract identity")
    require(contract["counts"] == [100, 1000, 4096] and all(type(n) is int for n in contract["counts"]) and contract["smokeCount"] == 8 and
            contract["workloads"] == list(WORKLOADS), "MCP contract matrix")
    require(all(type(contract["sampling"][k]) is int and contract["sampling"][k] == v for k, v in (("processes", 3), ("warmup", 12), ("samples", 100))), "MCP sample contract")
    require(contract["limits"] == dict(page=128, detail=64, structuredOutputBytes=262144, stdioFrameBytes=1048576,
            pollSleepMilliseconds=1, requestTimeoutSeconds=15), "MCP limits")
    require(type(data["schema"]) is int and data["schema"] == 1 and data["contractId"] == contract["id"] and type(data["measured"]) is bool, "MCP report identity")
    require(all(data[k] == contract[k] for k in ("scope", "allocationScope", "pumpScope")), "MCP scope changed")
    require(all(data[k] is False for k in ("formalClosureVerified", "targetBudgetAccepted", "fullPerformanceAccepted", "deployed")) and data["memoryAcceptance"] == "pending", "False MCP acceptance")
    require(integer(data["processId"]) and data["processId"] > 0 and isinstance(data["runtime"], str) and data["runtime"], "MCP parent identity")
    expected = [100, 1000, 4096] if data["measured"] else [8]
    require([f["objects"] for f in data["fixtures"]] == expected, "MCP complete fixture matrix")
    for fixture in data["fixtures"]:
        require(type(fixture["objects"]) is int and type(fixture["warmup"]) is int and type(fixture["sampleCount"]) is int and
                fixture["warmup"] == (12 if data["measured"] else 1) and fixture["sampleCount"] == (100 if data["measured"] else 2), "MCP samples")
        require(integer(fixture["childProcessId"]) and fixture["childProcessId"] > 0 and fixture["childProcessId"] != data["processId"] and
                type(fixture["childExitCode"]) is int and fixture["childExitCode"] == 0 and fixture["childStartedUtc"].endswith("Z"), "Actual separate child/exit")
        datetime.fromisoformat(fixture["childStartedUtc"])
        for k in ("pairingRequired", "exactApprovalRequired", "semanticChecksPassed", "schemaValidated", "retryIdempotent", "singleHistory",
                  "restoreInvalidatesHandles", "revokedGrantRejected", "revokedConnectionRejected", "cleanShutdown"):
            require(fixture[k] is True, "MCP authority/semantic/lifecycle missing")
        for k in ("startupHash", "finalHash"):
            require(isinstance(fixture[k], str) and len(fixture[k]) == 64 and all(c in "0123456789ABCDEF" for c in fixture[k]), "MCP fixture hash")
        audit = fixture["audit"]
        require(audit["ProductionAuditIncluded"] is True and type(audit["RequestsVerified"]) is int and
                audit["RequestsVerified"] == 9 * (fixture["warmup"] + fixture["sampleCount"]) + 4 and 1 <= len(audit["Files"]) <= 4, "MCP audit cardinality")
        require([r["workload"] for r in fixture["rows"]] == list(WORKLOADS), "MCP full workload matrix")
        ids = set()
        for row in fixture["rows"]:
            require(len(row["raw"]) == fixture["sampleCount"], "MCP raw count")
            for cost in row["raw"]:
                require(set(cost) == COSTS, "MCP raw fields")
                key = str(UUID(cost["RequestId"]))
                require(UUID(key).int != 0 and key not in ids, "MCP duplicated measured request")
                ids.add(key)
                require(number(cost["RoundtripMilliseconds"]) and number(cost["PumpMilliseconds"]) and cost["PumpMilliseconds"] <= cost["RoundtripMilliseconds"], "MCP nested time")
                for k in COSTS - {"RequestId", "RoundtripMilliseconds", "PumpMilliseconds"}:
                    require(integer(cost[k]), "MCP counter type/range")
                write = row["workload"] in WORKLOADS[4:]
                require(cost["RevisionAfter"] == cost["RevisionBefore"] + int(write) and cost["Pumps"] > 0 and
                        cost["PumpOwnerThreadAllocatedBytes"] <= cost["OwnerThreadAllocatedBytes"] and
                        0 < cost["StructuredBytes"] <= 262144 and cost["StructuredBytes"] < cost["ResponseBytes"] <= 1048576, "MCP revision/output/allocation boundary")
                if not write:
                    require(cost["FullCaptures"] == (1 if row["workload"] == "page_cold_revision" else 0), "MCP cold/warm proof count")
        for i in range(fixture["sampleCount"]):
            a, b, c = (r["raw"][i] for r in fixture["rows"][4:])
            require(a["RevisionAfter"] == b["RevisionBefore"] and b["RevisionAfter"] == c["RevisionBefore"], "MCP history sequence")
    return dict(probeValidated=True, measured=data["measured"], rows=len(expected)*7,
        samples=sum(f["sampleCount"]*7 for f in data["fixtures"]), formalClosureVerified=False,
        targetBudgetAccepted=False, memoryAcceptance="pending", fullPerformanceAccepted=False, deployed=False)


def verify_audit(fixture, records):
    cycles = fixture["warmup"] + fixture["sampleCount"]
    require(len(records) == 9*cycles+4, "MCP actual audit count")
    require(records[0]["capability"] == "ncma.capabilities.list" and records[1]["capability"] == "ncma.scene.inspect", "MCP real startup catalog/prime")
    read_count = 2+4*cycles
    read_ids = set()
    for record in records[:read_count]:
        require(record["code"] == "ok" and record["changed"] is False and record["approved"] is False and record["requestId"] not in read_ids, "MCP read audit")
        read_ids.add(record["requestId"])
    for j, row in enumerate(fixture["rows"][:4]):
        for i, cost in enumerate(row["raw"]):
            event = records[2+j*cycles+fixture["warmup"]+i]
            require(event["requestId"] == cost["RequestId"] and event["expectedRevision"] == cost["RevisionBefore"] and event["revision"] == cost["RevisionAfter"] and
                    event["capability"] == ("ncma.scene.object.inspect" if j == 3 else "ncma.scene.inspect"), "MCP read costs/audit differ")
    verify_history_audit(dict(warmup=fixture["warmup"], sampleCount=fixture["sampleCount"], rows=fixture["rows"][4:]), records[read_count:])


def verify(path: Path, contract_path: Path, repository: Path):
    repository = repository.resolve(); path = path.absolute()
    require(path.is_relative_to(repository/"out/verification/ecs-e5d"), "MCP development archive root")
    path = child(repository, path.relative_to(repository).as_posix())
    data, contract = loads(read_text(path)), loads(read_text(contract_path))
    require(data["contractSha256"] == digest(contract_path) and contract["domainContractSha256"] == digest(contract_path.parent/"ecs-e5d-domain-contract.json"), "MCP contract bytes changed")
    result = validate(data, contract)
    for fixture in data["fixtures"]:
        records = []; files = set()
        for item in fixture["audit"]["Files"]:
            declared = Path(item["path"])
            require(declared.is_absolute() and declared.is_relative_to(path.parent) and declared not in files, "MCP escaped/duplicate audit")
            files.add(declared); actual = child(repository, declared.relative_to(repository).as_posix())
            require(digest(actual) == item["sha256"], "MCP audit file changed")
            records.extend(loads(line) for line in read_text(actual).splitlines())
        for record in records:
            for key in ("instance", "session", "connection"):
                require(str(UUID(record[key])) == str(UUID(fixture["audit"][key.title()])), "MCP audit identity")
        verify_audit(fixture, records)
    products = {}
    for item in data["products"]:
        declared = Path(item["path"])
        require(item["name"] in PRODUCTS and item["name"] not in products and declared.is_absolute() and declared.is_relative_to(repository/"managed") and
                declared.name == item["name"]+".dll", "MCP parent product path/name")
        actual = child(repository, declared.relative_to(repository).as_posix()); require(digest(actual) == item["sha256"], "MCP parent product changed")
        products[item["name"]] = item
    require(set(products) == PRODUCTS, "MCP parent product closure")
    helpers = {}
    base = Path(products["Ncma.Editor.Transport.Tests"]["path"]).parent
    for item in data["helperFiles"]:
        declared = Path(item["path"])
        require(declared.parent == base and declared.name in HELPERS and declared.name not in helpers, "MCP helper file path/name")
        actual = child(repository, declared.relative_to(repository).as_posix()); require(digest(actual) == item["sha256"], "MCP helper changed")
        helpers[declared.name] = actual
    require(set(helpers) == HELPERS and digest(helpers["Ncma.Editor.Protocol.dll"]) == products["Ncma.Editor.Protocol"]["sha256"], "MCP helper closure/protocol")
    deps = loads(read_text(helpers["Ncma.Editor.Mcp.deps.json"]))
    require(set(deps["libraries"]) == {"Ncma.Editor.Mcp/1.0.0", "Ncma.Editor.Protocol/1.0.0"}, "MCP helper declared dependencies")
    return dict(result, archiveSha256=digest(path), contractSha256=digest(contract_path), developmentProductsChecked=len(products), helperFilesChecked=len(helpers))


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("Usage: ecs_e5d_mcp_evidence <mcp-costs.json> <contract.json> <repository>")
    print(json.dumps(verify(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])), indent=2))
