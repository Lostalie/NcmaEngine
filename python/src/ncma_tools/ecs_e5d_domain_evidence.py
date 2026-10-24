"""Independent development-window validation; never grants performance acceptance."""
import hashlib
import json
import math
from pathlib import Path
import uuid

CONTRACT = "ecs-e5-d-running-development-v2-native-counters"
NATIVE = {"Dimension", "State", "LiveWorlds", "LiveBodies", "Sequence", "Batches", "CopiedBytes", "Errors", "LiveJobs", "LastStepMilliseconds"}


def require(ok, label):
    if not ok:
        raise ValueError(label)


def number(value, integer=False, signed=False):
    require(type(value) is int if integer else type(value) in (int, float), "numeric type")
    require(math.isfinite(value) and (signed or value >= 0), "finite metric")
    return value


def validate(data):
    require(data["schema"] == 2 and data["contractId"] == CONTRACT, "contract")
    require(data["developmentOnly"] is True and data["formalCohort"] is False and data["targetPerformanceAccepted"] is False and data["memoryAcceptance"] == "pending", "acceptance scope")
    prefix = {"Editor": "editor-running-mode-", "Player": "player-trusted-sdk-running-mode-"}[data["host"]]
    require([w["label"] for w in data["windows"]] == [prefix + str(i) for i in range(3)], "window matrix")
    unique_total = pending_total = duplicate_total = 0
    for w in data["windows"]:
        require(w["warmup"] == 12 and w["samples"] == 100 and len(w["rows"]) == 100 and w["fixedInputDeltaSeconds"] == 1/60, "sample matrix")
        require(w["cpuScopesAreNested"] is True and w["gpuDomainsAreNotAdditive"] is True and w["syntheticInputClock"] is True and w["framePacingMeasured"] is False and w["targetPerformanceAccepted"] is False and w["memoryAcceptance"] == "pending", "window scope")
        require(w["resourcesBefore"] == w["resourcesAfter"], "resource lifetime")
        require(set(w["resourcesBefore"]) == {"Groups", "Meshes", "SkinMeshes", "UiTargets", "Environments", "Textures", "Materials", "Targets", "PostPrograms", "PhysicsWorlds"}, "resource coverage")
        for value in w["resourcesBefore"].values():
            number(value, integer=True)
        for memory in (w["memoryBefore"], w["memoryAfter"]):
            for name, value in memory.items():
                if name != "RawManagedHeapValid":
                    number(value, integer=True, signed=name == "RawManagedHeapBytes")
            require(memory["RawManagedHeapValid"] is (memory["RawManagedHeapBytes"] >= 0), "signed heap validity")
            for required in ("WorkingSetBytes", "PrivateBytes", "LastGcIndex", "LastGcHeapSizeBytes", "LastGcFragmentedBytes", "Gen0", "Gen1", "Gen2"):
                number(memory[required], integer=True)
        frames = {r["Presentation"]["HostFrame"] for r in w["rows"]}
        require(len(frames) == 100, "duplicate host frames")
        previous = None
        unique = {}
        invalid = duplicate = 0
        for row in w["rows"]:
            s, p = row["Simulation"], row["Presentation"]
            a, c = s["Advance"], s["LastCharacterStep"]
            for name in ("State", "Kind", "CommittedSteps", "TickBefore", "TickAfter", "AdvanceSequence"):
                number(a[name], integer=True)
            number(p["Generation"], integer=True); number(p["HostFrame"], integer=True)
            require(s["FullPerformanceAccepted"] is False and a["FullPerformanceAccepted"] is False and p["FullPerformanceAccepted"] is False, "nested acceptance")
            require(a["Completed"] is True and a["IncludesUpdate"] is True and a["State"] == 2 and a["Kind"] == 0 and a["CommittedSteps"] == 1 and a["TickAfter"] == a["TickBefore"] + 1 and a["RequestedDeltaSeconds"] == 1/60, "real running step")
            require(uuid.UUID(a["SessionId"]).int != 0 and uuid.UUID(a["WorldId"]).int != 0, "empty identity")
            require(c["PlaySessionId"] == a["SessionId"] and c["WorldId"] == a["WorldId"] and c["Tick"] == a["TickAfter"], "character stamp")
            require(set(c["Native"]) == NATIVE, "native counters missing or interop layout exposed")
            for name, value in c["Native"].items():
                number(value, integer=name != "LastStepMilliseconds")
            require(c["Native"]["Sequence"] == a["TickAfter"] and c["Native"]["LiveJobs"] == 0 and c["Native"]["Errors"] == 0 and c["Native"]["LiveBodies"] > 0, "native commit/lifecycle")
            for name, value in c.items():
                if name.endswith("Milliseconds"):
                    number(value)
            require(p["Completed"] is True and p["IncludesDiagnosticCapture"] is False, "presentation scope")
            number(row["HostMilliseconds"]); number(row["HostOwnerAllocatedBytes"], integer=True)
            for item in (a, p):
                number(item["CpuWallMilliseconds"]); number(item["OwnerThreadAllocatedBytes"], integer=True)
            current = (a["SessionId"], a["WorldId"], p["Generation"], a["TickAfter"], a["AdvanceSequence"], p["HostFrame"])
            require(current[2] > 0, "device identity")
            if previous:
                require(current[:3] == previous[:3] and current[3] == previous[3]+1 and current[4] == previous[4]+1 and current[5] > previous[5], "window identity/sequence")
            previous = current
            counters = p["Counters"]
            require(counters["MainSubmissions"] == counters["Presents"] == 1, "submission count")
            for name in ("MeshUploadedBytes", "ResourceUploadedBytes", "EnvironmentUploadedBytes", "UiUploadedBytes"):
                require(counters[name] == 0, "immutable warm upload")
            for value in counters.values():
                number(value, integer=True)
            for name, domain in (("Main", 1), ("Skin", 2), ("Ui", 3)):
                observation = p[name]; sample = observation["Sample"]; t = sample["Timing"]
                for field in ("Generation", "Sequence", "OriginFrame", "ObservedFrame", "SkippedSamples", "State", "Domain"):
                    number(t[field], integer=True)
                number(sample["Resource"], integer=True); number(sample["ContentRevision"], integer=True)
                valid = t["State"] == 3
                require(t["State"] in range(8) and t["IsValid"] is valid and t["Generation"] == p["Generation"] and t["Domain"] == domain, "GPU identity/state")
                number(t["Milliseconds"])
                require(t["OriginFrame"] <= p["HostFrame"] and t["ObservedFrame"] <= p["HostFrame"] and (valid or t["Milliseconds"] == 0), "GPU time/source")
                require((t["Sequence"] == 0) == (t["OriginFrame"] == 0) and (t["State"] not in range(1, 6) or t["Sequence"] > 0) and (t["State"] != 0 or t["Sequence"] == 0), "GPU sequence")
                require(not valid or t["ObservedFrame"] >= t["OriginFrame"], "future GPU origin")
                require(observation["Attribution"] == (0 if not valid else 1 if t["OriginFrame"] == p["HostFrame"] else 2), "GPU attribution")
                require((sample["Resource"] == sample["ContentRevision"] == 0) if domain != 3 else (t["Sequence"] == 0 or sample["Resource"] > 0 and sample["ContentRevision"] > 0), "GPU resource metadata")
                if not valid:
                    invalid += 1
                    continue
                key = (t["Generation"], domain, t["Sequence"])
                entry = dict(Generation=t["Generation"], Domain=domain, Sequence=t["Sequence"], OriginFrame=t["OriginFrame"], Resource=sample["Resource"], ContentRevision=sample["ContentRevision"], Milliseconds=t["Milliseconds"], InMeasuredWindow=t["OriginFrame"] in frames)
                if key in unique:
                    require(unique[key] == entry, "conflicting repeated GPU source")
                    duplicate += 1
                else:
                    unique[key] = entry
        gpu = w["gpu"]
        require(gpu["completeGpuCoverage"] is False and gpu["duplicateObservations"] == duplicate and gpu["invalidOrPendingObservations"] == invalid, "GPU summary count/scope")
        require(gpu["unique"] == list(unique.values()), "GPU dedup summary")
        expected_domains = [dict(domain=d, uniqueMeasuredSources=sum(e["Domain"] == d and e["InMeasuredWindow"] for e in unique.values()), uniqueOutsideWindowSources=sum(e["Domain"] == d and not e["InMeasuredWindow"] for e in unique.values())) for d in (1, 2, 3)]
        require(gpu["domains"] == expected_domains, "GPU coverage summary")
        unique_total += len(unique); pending_total += invalid; duplicate_total += duplicate
    return dict(windows=3, samples=300, uniqueGpuSources=unique_total, pendingOrInvalid=pending_total, repeatedGpuObservations=duplicate_total, targetPerformanceAccepted=False, memoryAcceptance="pending")


def verify(path):
    path = Path(path).resolve(strict=True)
    pinned = []
    def read(p):
        p = Path(p).resolve(strict=True)
        require(p.stat().st_size <= 32*1024*1024, "evidence size")
        raw = p.read_bytes(); pinned.append(dict(path=str(p), sha256=hashlib.sha256(raw).hexdigest().upper()))
        return json.loads(raw)
    data = read(path); result = validate(data)
    for i, w in enumerate(data["windows"]):
        raw = read(path.parent / f"ecs-running-raw-{i}.json")
        require(raw["schema"] == 2 and raw["contractId"] == CONTRACT and raw["label"] == w["label"] and raw["completedSamples"] == 100 and raw["failure"] is None and raw["rows"] == w["rows"] and raw["memoryBefore"] == w["memoryBefore"] and raw["memoryAfter"] == w["memoryAfter"], "raw window binding")
    request = read(path.parent / "request.json")
    require(request["kind"] == {"Editor": "editor", "Player": "player"}[data["host"]], "host binding")
    root = Path(request["productRoot"]).resolve(strict=True)
    require(root == (path.parent.parent / ("product-"+request["kind"])).resolve(strict=True), "product root")
    manifest = read(root / "deployment-manifest.json")
    entries = {x["path"]: x for x in manifest["files"]}
    loaded = read(path.parent / "loaded-product-assemblies.json")
    require(len(loaded) >= 5, "loaded product closure")
    for assembly in loaded:
        source = Path(assembly["path"]).resolve(strict=True)
        relative = source.relative_to(root).as_posix()
        sha = hashlib.sha256(source.read_bytes()).hexdigest().upper()
        require(sha == assembly["sha256"] == entries[relative]["sha256"] and source.stat().st_size == entries[relative]["size"], "actual loaded assembly hash")
        pinned.append(dict(path=str(source), sha256=sha))
    return dict(**result, actualLoadedAssemblies=len(loaded), files=pinned)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.report), indent=2))
