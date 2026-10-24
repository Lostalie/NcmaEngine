"""V3 development evidence. V2 validator is immutable and used only for common CPU/native/lifetime checks."""
import copy
import hashlib
import json
from pathlib import Path
from . import ecs_e5d_domain_evidence as v2

CONTRACT = "ecs-e5-d-running-development-v3-gpu-snapshots"
require, number = v2.require, v2.number
DOMAINS = (("Main", 1), ("Skin", 2), ("Ui", 3))


def u64(value):
    number(value, integer=True)
    require(value <= 2**64-1, "GPU uint64 range")


def source(p, field, domain):
    a = p[field]; s = a["Sample"]; t = s["Timing"]
    for key in ("Generation", "Sequence", "OriginFrame", "ObservedFrame", "SkippedSamples", "State", "Domain"):
        u64(t[key])
    u64(s["Resource"]); u64(s["ContentRevision"]); number(t["Milliseconds"])
    valid = t["State"] == 3
    require(t["State"] in range(8) and t["IsValid"] is valid and t["Generation"] == p["Generation"] and t["Domain"] == domain, "snapshot identity/state")
    require(t["OriginFrame"] <= p["HostFrame"] and t["ObservedFrame"] <= p["HostFrame"] and (valid or t["Milliseconds"] == 0), "snapshot duration/horizon")
    require((t["Sequence"] == 0) == (t["OriginFrame"] == 0) and (t["State"] not in range(1, 6) or t["Sequence"] > 0) and (t["State"] != 0 or t["Sequence"] == 0), "snapshot sequence")
    require(t["State"] not in range(2, 6) or t["ObservedFrame"] >= t["OriginFrame"], "snapshot future source")
    number(a["Attribution"], integer=True)
    require(a["Attribution"] == (0 if not valid else 1 if t["OriginFrame"] == p["HostFrame"] else 2), "snapshot attribution")
    require((s["Resource"] == s["ContentRevision"] == 0) if domain != 3 or t["Sequence"] == 0 else (s["Resource"] > 0 and s["ContentRevision"] > 0), "snapshot resource")
    return s


def pair(p, name, domain):
    require(type(p["Version"]) is int and p["Version"] == 2 and p["Completed"] is True, "snapshot report version/completion")
    u64(p["Generation"]); u64(p["HostFrame"])
    require(p["Generation"] > 0 and p["HostFrame"] > 0, "snapshot report identity")
    c, r = source(p, name, domain), source(p, name+"Resolved", domain)
    ct, rt = c["Timing"], r["Timing"]
    require(rt["State"] in (0, 3, 4, 5) and rt["Sequence"] <= ct["Sequence"] and rt["ObservedFrame"] == ct["ObservedFrame"] and rt["SkippedSamples"] <= ct["SkippedSamples"], "snapshot pair")
    require(ct["State"] not in (1, 2) or rt["Sequence"] != ct["Sequence"], "unfinished same-sequence resolution")
    require(ct["State"] not in (3, 4, 5) or c == r, "resolved current mismatch")
    return c, r


def entry(s, frames):
    t = s["Timing"]
    return dict(Generation=t["Generation"], Domain=t["Domain"], Sequence=t["Sequence"], OriginFrame=t["OriginFrame"],
                State=t["State"], Resource=s["Resource"], ContentRevision=s["ContentRevision"],
                Milliseconds=t["Milliseconds"], InMeasuredWindow=t["OriginFrame"] in frames)


def rebuild(window):
    frames = {row["Presentation"]["HostFrame"] for row in window["rows"]}
    baseline = window["baselinePresentation"]
    require(frames and baseline["HostFrame"] < min(frames) and baseline["IncludesDiagnosticCapture"] is False, "snapshot warm baseline")
    tracks, seen, resolved = {}, {}, {}
    duplicates = 0
    for name, domain in DOMAINS:
        c, r = pair(baseline, name, domain)
        tracks[domain] = dict(current=c, resolved=r, admission=0, resolution=0, currentStates=[0]*8, resolvedStates=[0]*8)
        if r["Timing"]["Sequence"]:
            seen[(domain, r["Timing"]["Sequence"])] = entry(r, frames)
    previous_frame = baseline["HostFrame"]
    for row in window["rows"]:
        p = row["Presentation"]
        require(p["Generation"] == baseline["Generation"] and p["HostFrame"] > previous_frame, "snapshot report chronology")
        previous_frame = p["HostFrame"]
        for name, domain in DOMAINS:
            c, r = pair(p, name, domain)
            ct, rt = c["Timing"], r["Timing"]
            old = tracks[domain]; oc, ores = old["current"], old["resolved"]
            require(ct["Sequence"] >= oc["Timing"]["Sequence"] and rt["Sequence"] >= ores["Timing"]["Sequence"] and
                    ct["SkippedSamples"] >= oc["Timing"]["SkippedSamples"] and ct["ObservedFrame"] >= oc["Timing"]["ObservedFrame"], "snapshot regression")
            require(ct["Sequence"] != oc["Timing"]["Sequence"] or
                    (ct["OriginFrame"], c["Resource"], c["ContentRevision"]) == (oc["Timing"]["OriginFrame"], oc["Resource"], oc["ContentRevision"]), "current source conflict")
            old["admission"] += max(0, ct["Sequence"]-oc["Timing"]["Sequence"]-1)
            old["resolution"] += max(0, rt["Sequence"]-ores["Timing"]["Sequence"]-1)
            old["currentStates"][ct["State"]] += 1; old["resolvedStates"][rt["State"]] += 1
            if rt["Sequence"]:
                key = (domain, rt["Sequence"]); value = entry(r, frames)
                require(key not in seen or seen[key] == value, "resolved source conflict")
                duplicates += int(key in resolved)
                seen[key] = value; resolved[key] = value
            old["current"], old["resolved"] = c, r
    domains = []
    for _, d in DOMAINS:
        own = [e for e in resolved.values() if e["Domain"] == d]; t = tracks[d]
        domains.append(dict(domain=d, uniqueMeasuredValidSources=sum(e["State"] == 3 and e["InMeasuredWindow"] for e in own),
                            uniqueOutsideWindowValidSources=sum(e["State"] == 3 and not e["InMeasuredWindow"] for e in own),
                            uniqueInvalidResolvedSources=sum(e["State"] != 3 for e in own),
                            currentStates=t["currentStates"], resolvedStates=t["resolvedStates"],
                            admissionSequenceGaps=t["admission"], unobservedResolutionSequenceSlots=t["resolution"],
                            unobservedTailSequenceSlots=t["current"]["Timing"]["Sequence"]-t["resolved"]["Timing"]["Sequence"]))
    return dict(resolved=list(resolved.values()), duplicateResolvedObservations=duplicates, domains=domains, completeGpuCoverage=False)


def current_only_summary(rows):
    """In-memory adapter for unchanged V2 CPU/native checks, NEVER an emitted V2 evidence record."""
    frames = {row["Presentation"]["HostFrame"] for row in rows}
    unique = {}; invalid = duplicate = 0
    for row in rows:
        for name, domain in DOMAINS:
            s = row["Presentation"][name]["Sample"]; t = s["Timing"]
            if t["State"] != 3:
                invalid += 1
                continue
            e = entry(s, frames); e.pop("State")
            key = (domain, t["Sequence"])
            require(key not in unique or unique[key] == e, "current repeated source conflict")
            duplicate += int(key in unique); unique[key] = e
    return dict(unique=list(unique.values()), duplicateObservations=duplicate, invalidOrPendingObservations=invalid,
                completeGpuCoverage=False, domains=[dict(domain=d, uniqueMeasuredSources=sum(e["Domain"] == d and e["InMeasuredWindow"] for e in unique.values()),
                uniqueOutsideWindowSources=sum(e["Domain"] == d and not e["InMeasuredWindow"] for e in unique.values())) for _, d in DOMAINS])


def validate(data):
    require(type(data["schema"]) is int and data["schema"] == 3 and data["contractId"] == CONTRACT, "v3 contract")
    # Original V2 file/semantics stay untouched; validate all its CPU, physics,
    # memory, lifetime and current-slot invariants on an in-memory projection.
    common = copy.deepcopy(data); common["schema"] = 2; common["contractId"] = v2.CONTRACT
    for w in common["windows"]:
        w["gpu"] = current_only_summary(w["rows"])
    v2.validate(common)
    summaries = []
    for w in data["windows"]:
        expected = rebuild(w); actual = {k: v for k, v in w["gpu"].items() if k != "scope"}
        require(json_equal(actual, expected), "v3 GPU reconstruction mismatch")
        summaries.append(expected)
    return dict(windows=3, samples=300, snapshotDomains=[s["domains"] for s in summaries],
                uniqueResolvedSources=sum(len(s["resolved"]) for s in summaries), targetPerformanceAccepted=False,
                completeGpuCoverage=False, memoryAcceptance="pending")


def json_equal(actual, expected):
    """JSON booleans must never pass as numeric 0/1. Numeric encodings may use 0.0."""
    if type(expected) in (int, float):
        return type(actual) in (int, float) and actual == expected
    if type(actual) is not type(expected):
        return False
    if isinstance(expected, dict):
        return actual.keys() == expected.keys() and all(json_equal(actual[k], v) for k, v in expected.items())
    if isinstance(expected, list):
        return len(actual) == len(expected) and all(json_equal(a, e) for a, e in zip(actual, expected))
    return actual == expected


def verify(path):
    path = Path(path).resolve(strict=True); pins = []
    def read(file):
        file = Path(file).resolve(strict=True)
        require(file.stat().st_size <= 32*1024*1024, "evidence size")
        raw = file.read_bytes(); pins.append(dict(path=str(file), sha256=hashlib.sha256(raw).hexdigest().upper()))
        return json.loads(raw)
    data = read(path); result = validate(data)
    for i, w in enumerate(data["windows"]):
        raw = read(path.parent / f"ecs-running-raw-{i}.json")
        require(raw["schema"] == 3 and raw["contractId"] == CONTRACT and raw["label"] == w["label"] and raw["completedSamples"] == 100 and raw["failure"] is None and
                json_equal(raw["baselinePresentation"], w["baselinePresentation"]) and json_equal(raw["rows"], w["rows"]) and
                json_equal(raw["memoryBefore"], w["memoryBefore"]) and json_equal(raw["memoryAfter"], w["memoryAfter"]), "v3 raw binding")
    request = read(path.parent / "request.json")
    require(request["kind"] == {"Editor": "editor", "Player": "player"}[data["host"]], "host binding")
    root = Path(request["productRoot"]).resolve(strict=True)
    require(root == (path.parent.parent / ("product-"+request["kind"])).resolve(strict=True), "product root")
    manifest = read(root / "deployment-manifest.json"); entries = {e["path"]: e for e in manifest["files"]}
    loaded = read(path.parent / "loaded-product-assemblies.json")
    require(len(loaded) >= 5, "loaded closure")
    for assembly in loaded:
        source_path = Path(assembly["path"]).resolve(strict=True); rel = source_path.relative_to(root).as_posix()
        sha = hashlib.sha256(source_path.read_bytes()).hexdigest().upper()
        require(sha == assembly["sha256"] == entries[rel]["sha256"] and source_path.stat().st_size == entries[rel]["size"], "actual loaded hash")
        pins.append(dict(path=str(source_path), sha256=sha))
    return dict(**result, actualLoadedAssemblies=len(loaded), files=pins)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(); parser.add_argument("report", type=Path)
    print(json.dumps(verify(parser.parse_args().report), indent=2))
