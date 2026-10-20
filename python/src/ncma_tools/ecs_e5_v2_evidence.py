"""Independent E5 v2 CPU verifier. Invalid heap estimates stay visible; memory acceptance is never granted.

The original v1 verifier and rejected archives remain unchanged.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
import statistics
import sys

PRODUCTS = {f"Ncma.{name}.dll" for name in ("Runtime", "Scene", "Managed", "Gameplay", "Movement", "Editor.Core")}
DRIVER = {"Ncma.Gameplay.Tests.dll", "Ncma.Gameplay.Tests.deps.json", "Ncma.Gameplay.Tests.runtimeconfig.json"}
FILES = PRODUCTS | DRIVER


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def _pairs(items: list) -> dict:
    result = {}
    for key, value in items:
        require(key not in result, f"Duplicate JSON field: {key}")
        result[key] = value
    return result


def loads(text: str):
    def bad(value):
        raise ValueError(f"Non-finite JSON value: {value}")
    return json.loads(text, object_pairs_hook=_pairs, parse_constant=bad)


def read_text(path: Path, maximum: int = 16 * 1024 * 1024) -> str:
    require(path.is_file() and not path.is_symlink() and 0 < path.stat().st_size <= maximum, "Invalid/bounded evidence file")
    data = path.read_bytes()
    return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def digest(path: Path) -> str:
    require(path.is_file() and not path.is_symlink(), "Missing/reparse evidence input")
    with path.open("rb") as stream:
        result = hashlib.sha256()
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
        return result.hexdigest().upper()


def child(root: Path, name: str) -> Path:
    require(isinstance(name, str) and name and "\\" not in name and ":" not in name and
            all(part not in ("", ".", "..") for part in name.split("/")), "Unsafe relative evidence path")
    path = root.joinpath(*name.split("/"))
    require(path.resolve().is_relative_to(root.resolve()), "Escaping evidence path")
    for parent in (path, *path.parents):
        require(not parent.is_symlink() and not (hasattr(parent, "is_junction") and parent.is_junction()), "Reparse evidence path")
        if parent == root:
            break
    return path


def percentile(values: list[float], fraction: float) -> float:
    return sorted(values)[max(0, math.ceil(len(values) * fraction) - 1)]



MEMORY_CONTRACT = {
    "schema": 2,
    "rawSource": "GC.GetTotalMemory(false)",
    "validStatus": "valid-estimate",
    "invalidStatus": "invalid-runtime-reading",
    "lastCollectionScope": "last-completed-gc-not-current-heap",
    "memoryAcceptance": "pending",
    "negativeRawPolicy": "preserve-and-mark-invalid-never-clamp",
    "v1Disposition": "rejected-archives-remain-rejected"
}

def validate_contract(contract: dict) -> None:
    require(contract["schema"] == 2 and contract["id"] == "ecs-e5-c-v2" and
            contract["memoryDiagnostics"] == MEMORY_CONTRACT, "Wrong v2 memory contract")


def signed64(value) -> bool:
    return type(value) is int and -(1 << 63) <= value < (1 << 63)


def validate_memory(row: dict) -> None:
    for suffix in ("Before", "After"):
        raw = row[f"managed{suffix}Bytes"]
        require(signed64(raw), "Missing/invalid raw signed heap counter")
        memory = row[f"memory{suffix}"]
        require(type(memory) is dict and set(memory) == {"status", "lastCollection"}, "Incomplete memory telemetry")
        require(memory["status"] == ("invalid-runtime-reading" if raw < 0 else "valid-estimate"),
                "Memory status contradicts raw runtime reading")
        gc = memory["lastCollection"]
        require(type(gc) is dict and set(gc) == {"index", "generation", "heapBytes", "fragmentedBytes",
                "totalCommittedBytes", "compacted", "concurrent", "scope"}, "Incomplete last-GC telemetry")
        require(gc["scope"] == "last-completed-gc-not-current-heap", "Wrong last-GC scope")
        for field in ("index", "generation", "heapBytes", "fragmentedBytes", "totalCommittedBytes"):
            require(signed64(gc[field]) and gc[field] >= 0, "Invalid last-GC field")
        require(gc["generation"] <= 2 and gc["fragmentedBytes"] <= gc["heapBytes"] and
                type(gc["compacted"]) is bool and type(gc["concurrent"]) is bool, "Invalid last-GC bounds")
        if gc["index"] == 0:
            require(gc["heapBytes"] == gc["fragmentedBytes"] == 0, "Uncollected heap cannot claim last-GC size")
    require(row["memoryAfter"]["lastCollection"]["index"] >= row["memoryBefore"]["lastCollection"]["index"],
            "Last-GC index went backwards")


def memory_report(row: dict) -> dict:
    return {key: row[key] for key in ("managedBeforeBytes", "managedAfterBytes", "memoryBefore",
                                     "memoryAfter", "processWorkingSetBytes", "processLifetimePeakWorkingSetBytes")}


def invalid_memory_readings(results: dict) -> list:
    invalid = []
    for (variant, run), (_, samples, _) in results.items():
        for (workload, count), row in samples.items():
            for suffix in ("Before", "After"):
                raw = row[f"managed{suffix}Bytes"]
                if raw < 0:
                    invalid.append(dict(variant=variant, run=run, workload=workload, objects=count,
                                        phase=suffix.lower(), rawBytes=raw, status="invalid-runtime-reading"))
    return invalid


def validate_run(text: str, contract: dict, cohort: str, variant: str, measured: bool,
                 contract_hash: str, files: dict) -> tuple[dict, dict, dict | None]:
    validate_contract(contract)
    rows = []
    marker = "E5 v2 paired closure verified before and after workloads."
    require(text.splitlines().count(marker) == 1, "Missing/duplicate executed closure proof")
    for line in text.splitlines():
        if line.startswith("{"):
            rows.append(loads(line))
        elif line.strip():
            require(line == marker or line.startswith("E0 fixture checks:") or line.startswith("E5 read fixture checks:"), "Unexpected log output")
    allowed = {"ecs_e5_paired_environment", "ecs_e5_environment", "ecs_e5_sample", "ecs_e5_semantic"}
    require(all(row.get("kind") in allowed for row in rows), "Unknown event")
    paired = [r for r in rows if r["kind"] == "ecs_e5_paired_environment"]
    require(len(paired) == 1, "Missing/duplicate paired environment")
    p = paired[0]
    require(p["schema"] == 3 and p["contractId"] == "ecs-e5-c-v2" and p["variant"] == variant and p["cohort"] == cohort and
            p["measured"] is measured and p["contractSha256"] == contract_hash and
            p["tieredCompilation"] == contract["tieredCompilation"] and p["files"] == files,
            "Environment/input contract mismatch")
    env = [r for r in rows if r["kind"] == "ecs_e5_environment"]
    require(len(env) == (1 if measured else 0), "Missing/duplicate runtime environment")
    if measured:
        require(env[0]["schema"] == 2 and env[0]["configuration"] == "Release" and env[0]["stopwatchFrequency"] > 0 and
                env[0]["cpuCount"] > 0 and isinstance(env[0]["serverGc"], bool), "Wrong runtime configuration")
        for key in ("runtime", "os", "architecture", "assembly", "worldAssembly", "utc"):
            require(isinstance(env[0][key], str) and bool(env[0][key]), "Incomplete environment")
    names = {w["name"]: w for w in contract["cohorts"][cohort]}
    counts = contract["counts"] if measured else [contract["smokeCount"]]
    expected = {(name, count) for name in names for count in counts}
    semantics, samples = {}, {}
    for row in rows:
        if row["kind"] not in ("ecs_e5_sample", "ecs_e5_semantic"):
            continue
        key = (row["workload"], row["objects"])
        dest = semantics if row["kind"] == "ecs_e5_semantic" else samples
        require(key in expected and key not in dest, "Unknown/duplicate workload row")
        dest[key] = row
        if dest is semantics:
            require(isinstance(row["tick"], int) and row["tick"] >= 0 and row["sceneBytes"] > 0 and
                    len(row["sha256"]) == 64 and all(c in "0123456789ABCDEF" for c in row["sha256"]), "Invalid semantic output")
        else:
            require(measured and row["schema"] == 2 and row["warmup"] == contract["warmup"] and
                    row["samples"] == contract["samples"] and row["operationsPerSample"] == names[key[0]]["operations"],
                    "Sampling contract mismatch")
            for raw, summary in (("rawMs", "ms"), ("rawAllocatedThreadBytes", "allocatedThreadBytes")):
                values = row[raw]
                require(len(values) == contract["samples"] and all(type(v) in (int, float) and math.isfinite(v) and
                        (v > 0 if raw == "rawMs" else v >= 0) for v in values), "Missing/invalid raw samples")
                for label, quantile in (("p50", .5), ("p95", .95), ("p99", .99), ("max", 1)):
                    require(math.isclose(row[summary][label], percentile(values, quantile), rel_tol=1e-12, abs_tol=1e-12),
                            "Summary does not match raw samples")
            require(len(row["gcCollections"]) == 3 and all(type(v) is int and v >= 0 for v in row["gcCollections"]), "Invalid GC counts")
            validate_memory(row)
            for field in ("processWorkingSetBytes", "processLifetimePeakWorkingSetBytes", "sceneBytes"):
                require(type(row[field]) is int and row[field] >= 0, "Invalid memory metric")
            require(isinstance(row["layerMs"], dict), "Missing layer diagnostics")
    require(set(semantics) == expected and set(samples) == (expected if measured else set()), "Incomplete workload matrix")
    return semantics, samples, env[0] if measured else None


def paired_summary(ratios: list[float], threshold: float) -> dict:
    require(len(ratios) == 3 and all(math.isfinite(r) and r > 0 for r in ratios), "Three independent process pairs required")
    logs = [math.log(r) for r in ratios]
    mean = statistics.mean(logs)
    half = 4.302652729911275 * statistics.stdev(logs) / math.sqrt(3)
    lower, upper = math.exp(mean - half), math.exp(mean + half)
    return {"ratios": ratios, "geometricMean": math.exp(mean), "process95Interval": [lower, upper],
            "threshold": threshold, "status": "met" if max(ratios) <= threshold and upper <= threshold else
            "observed_regression" if min(ratios) > threshold and lower > threshold else "unproven"}


def verify(root: Path) -> dict:
    inputs_path, execution_path = child(root, "inputs.json"), child(root, "execution.json")
    inputs, execution = loads(read_text(inputs_path)), loads(read_text(execution_path))
    require(inputs["schema"] == execution["schema"] == 2 and inputs["generation"] == execution["generation"] and
            inputs["measured"] is execution["measured"] and type(inputs["measured"]) is bool and
            execution["inputsSha256"] == digest(inputs_path), "Execution identity mismatch")
    require(execution["fullPerformanceAccepted"] is False and execution["deployed"] is False, "Baseline cannot grant acceptance")
    contract_path = child(root, "contract.json")
    require(digest(contract_path) == inputs["contractSha256"], "Contract hash mismatch")
    contract = loads(read_text(contract_path))
    require(contract["schema"] == 2 and contract["id"] == "ecs-e5-c-v2" and contract["runs"] == 3 and
            contract["order"] == ["AB", "BA", "AB"] and contract["acceptance"] is False, "Wrong contract version")
    for name, field in (("source-freeze.json", "sourceFreezeSha256"), ("measure.ps1", "scriptSha256"), ("verifier.py", "verifierSha256")):
        require(digest(child(root, name)) == inputs[field], "Evidence tool/source hash mismatch")
    validate_contract(contract)
    reference, cohort, measured = inputs["reference"], inputs["cohort"], inputs["measured"]
    require(reference in ("e2b", "e4e") and cohort in ("common", "reads") and
            not (reference == "e2b" and cohort == "reads"), "Unsupported reference/cohort")
    variants = (reference, "candidate")
    expected = {(v, n) for v in variants for n in FILES}
    actual, hashes = set(), {v: {} for v in variants}
    for file in inputs["files"]:
        key = (file["variant"], file["name"])
        require(key in expected and key not in actual and file["archive"] == f"{key[0]}/{key[1]}", "Invalid/duplicate closure record")
        actual.add(key)
        path = child(root, file["archive"])
        require(path.stat().st_size == file["size"] and digest(path) == file["sha256"], "Input bytes changed")
        hashes[key[0]][key[1]] = file["sha256"]
    require(actual == expected, "Incomplete input closure")
    for name in DRIVER:
        require(hashes[reference][name] == hashes["candidate"][name], "Not the same driver")
    packages = {}
    for package in inputs["packages"]:
        variant = package["variant"]
        require(variant in variants and variant not in packages, "Duplicate/unknown package")
        path = child(root, package["archive"])
        require(digest(path) == package["sha256"], "Package manifest changed")
        manifest = loads(read_text(path))
        require(manifest["configuration"] == "Release" and manifest["tfm"] == "net8.0" and
                manifest["product"] == "NcmaEngine-editor-candidate", "Wrong product/configuration")
        if variant == reference:
            require(package["sha256"] == contract["references"][reference]["manifestSha256"], "Reference changed")
        for name in PRODUCTS:
            entry = [f for f in manifest["files"] if f["path"] == name]
            require(len(entry) == 1 and entry[0]["sha256"] == hashes[variant][name] and
                    entry[0]["size"] == child(root, f"{variant}/{name}").stat().st_size, "Package/product mismatch")
        packages[variant] = package
    require(set(packages) == set(variants), "Missing package")
    for variant in variants:
        directory = child(root, variant)
        require({p.name for p in directory.iterdir()} == FILES | {"paired-inputs.json"}, "Extra/missing closure input")
        manifest = loads(read_text(directory / "paired-inputs.json", 65536))
        require(manifest["schema"] == 3 and manifest["contractId"] == "ecs-e5-c-v2" and manifest["variant"] == variant and manifest["cohort"] == cohort and
                manifest["contractSha256"] == inputs["contractSha256"], "Driver manifest mismatch")
        require(len(manifest["files"]) == 9 and len({f["name"] for f in manifest["files"]}) == 9, "Duplicate/missing driver file")
        for f in manifest["files"]:
            require(f["name"] in FILES and hashes[variant][f["name"]] == f["sha256"] and
                    child(root, f"{variant}/{f['name']}").stat().st_size == f["size"], "Driver closure changed")
    runs = 3 if measured else 1
    require(execution["runsCompleted"] == runs, "Incomplete run schedule")
    schedule = [(v, run) for run in range(1, runs + 1) for v in (variants if run % 2 else variants[::-1])]
    require([(r["variant"], r["run"]) for r in execution["logs"]] == schedule, "Missing/duplicate/reordered process pairs")
    results, environments = {}, []
    for log in execution["logs"]:
        variant, run = log["variant"], log["run"]
        require(log["path"] == f"{variant}-{run}.log" and log["exitCode"] == 0, "Failed/wrong execution log")
        path = child(root, log["path"])
        require(digest(path) == log["sha256"], "Execution log changed")
        ep = child(root, log["environment"])
        require(digest(ep) == log["environmentSha256"], "Machine environment changed")
        machine = loads(read_text(ep))
        require(bool(machine["utc"]) and bool(machine["cpu"]) and isinstance(machine["processes"], list) and
                isinstance(machine["powerScheme"], str), "Incomplete machine environment")
        result = validate_run(read_text(path), contract, cohort, variant, measured, inputs["contractSha256"], hashes[variant])
        results[(variant, run)] = result
        environments.append({"variant": variant, "run": run, "runtime": result[2], "machine": log["environment"]})
    for run in range(1, runs + 1):
        require(results[(reference, run)][0] == results[("candidate", run)][0], "Paired semantic states differ")
        if measured:
            a, b = results[(reference, run)][2], results[("candidate", run)][2]
            for key in ("runtime", "os", "architecture", "cpuCount", "stopwatchFrequency", "serverGc", "configuration", "assembly"):
                require(a[key] == b[key], "Paired runtime settings differ")
    report = []
    if measured:
        for key in sorted(results[(reference, 1)][1]):
            per_run, ratios = [], []
            for run in range(1, 4):
                a, b = (results[(v, run)][1][key] for v in variants)
                ratios.append(b["ms"]["p95"] / a["ms"]["p95"])
                per_run.append({"run": run, "referenceMs": a["ms"], "candidateMs": b["ms"],
                                "referenceAllocatedBytes": a["allocatedThreadBytes"], "candidateAllocatedBytes": b["allocatedThreadBytes"],
                                "referenceGc": a["gcCollections"], "candidateGc": b["gcCollections"],
                                "referenceMemory": memory_report(a), "candidateMemory": memory_report(b)})
            targets = [t for t in contract["targets"] if t["reference"] == reference and t["cohort"] == cohort and
                       key[0] in t["workloads"] and key[1] in t["counts"]]
            report.append({"workload": key[0], "objects": key[1], "runs": per_run, "p95Ratios": ratios,
                           "targets": [{"id": t["id"], **paired_summary(ratios, t["maximumP95Ratio"])} for t in targets],
                           "warmAllocationTarget": all(results[("candidate", r)][1][key]["allocatedThreadBytes"]["max"] == 0 for r in range(1, 4))
                           if cohort == "reads" and key[0] in contract["allocationTargets"]["workloads"] else None})
    return {"schema": 2, "evidenceVerified": True, "memoryAcceptance": "pending",
            "invalidMemoryReadings": invalid_memory_readings(results), "generation": inputs["generation"], "reference": reference,
            "cohort": cohort, "measured": measured, "pairedSemanticsEqual": True, "runs": runs, "environments": environments,
            "rows": report, "fullPerformanceAccepted": False, "deployed": False,
            "warning": "Scoped CPU evidence, not final E5 acceptance. Process intervals assume independent pairs; hardware load observations are bounded snapshots."}


def main() -> int:
    try:
        require(len(sys.argv) == 2, "Usage: python -m ncma_tools.ecs_e5_v2_evidence ARCHIVE")
        print(json.dumps(verify(Path(sys.argv[1])), ensure_ascii=False, allow_nan=False, indent=2))
        return 0
    except (ValueError, KeyError, TypeError, OSError, IndexError, OverflowError) as error:
        print(f"E5 v2 evidence rejected: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
