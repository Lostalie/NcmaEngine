"""Bounded offline full-loop stream validation; no FPS or target acceptance."""
from contextlib import nullcontext
import io
import json
import os
from pathlib import Path
from uuid import UUID
from ncma_tools.ecs_e5d_pacing_evidence import verify as verify_prefix

SCOPE = "full owner-loop samples; publish cost outside sample end, inside successive present intervals; writer wall overlaps owner; startup/shutdown excluded; not display FPS"
PHASES = ("Control", "Platform", "Intents", "Agent", "BeginPresentation", "Input",
          "Simulation", "Presentation", "Recovery", "Pacing")
OUTCOMES = ("Completed", "Stopped", "Faulted", "Recovered")


def integer(value, minimum=0, maximum=2**63-1):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError("Invalid bounded integer")
    return value


def normalize(value):
    if isinstance(value, dict):
        result = {}
        for key, entry in value.items():
            if not key: raise ValueError("Empty report field")
            normalized = key[0].upper()+key[1:]
            if normalized in result: raise ValueError("Case-colliding report fields")
            result[normalized] = normalize(entry)
        return result
    return [normalize(v) for v in value] if isinstance(value, list) else value


def closed(value, keys):
    if type(value) is not dict or set(value) != set(keys.split()):
        raise ValueError("Unexpected object fields")


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON field")
        result[key] = value
    return result


def verify(path, report, *, camel_case=False):
    with Path(path).open("rb") as source:
        return _verify_opened(source, os.fstat(source.fileno()).st_size, report, camel_case=camel_case)


def verify_bytes(raw, report, *, camel_case=False):
    """Validate an immutable captured stream without reopening a mutable path."""
    if type(raw) is not bytes or not 1 <= len(raw) <= 1024**3:
        raise ValueError("Invalid bounded stream bytes")
    with io.BytesIO(raw) as source:
        return _verify_opened(source, len(raw), report, camel_case=camel_case)


def _verify_opened(source, actual_bytes, report, *, camel_case=False):
    keys = "Version TimestampFrequency Capacity TotalIterations OmittedIterations PresentReturns PresentIntervals MaximumPresentIntervalTicks Samples FrameRateLimit Scope TargetPerformanceAccepted CompleteRawCoverage Stream".split()
    closed(report, " ".join(key[0].lower()+key[1:] if camel_case else key for key in keys))
    verify_prefix(report, camel_case=camel_case)
    report = normalize(report) if camel_case else report
    stream = report.get("Stream")
    closed(stream, "Version RunId Status QueueCapacity MaximumRecords Produced Accepted Written QueueDrops BudgetDrops FailureDrops Unwritten PublishTicks PublishAllocatedBytes WriterWallTicks WriterAllocatedBytes Bytes Failure CompleteRawCoverage TargetPerformanceAccepted Scope")
    if stream["Status"] != "completed" or stream["CompleteRawCoverage"] is not True:
        raise ValueError("Incomplete stream is not full evidence")
    if stream["Scope"] != SCOPE or stream["TargetPerformanceAccepted"] is not False or stream["Failure"] is not None:
        raise ValueError("Invalid stream scope/failure")
    if integer(stream["Version"]) != 1 or str(UUID(stream["RunId"])) != stream["RunId"]:
        raise ValueError("Stream version/identity")
    integer(stream["QueueCapacity"], 1, 4096)
    integer(stream["MaximumRecords"], 1, 1_000_000)
    count = integer(stream["Produced"], 0, stream["MaximumRecords"])
    if stream["Accepted"] != count or stream["Written"] != count or report["TotalIterations"] != count:
        raise ValueError("Stream/host count mismatch")
    for name in ("Accepted", "Written", "QueueDrops", "BudgetDrops", "FailureDrops", "Unwritten",
                 "PublishTicks", "PublishAllocatedBytes", "WriterWallTicks", "WriterAllocatedBytes", "Bytes"):
        integer(stream[name])
    if any(stream[k] for k in ("QueueDrops", "BudgetDrops", "FailureDrops", "Unwritten")):
        raise ValueError("Lost samples")
    total_bytes = integer(stream["Bytes"], 1, 1024**3)
    if actual_bytes != total_bytes:
        raise ValueError("Stream byte count mismatch")
    frequency, cap = report["TimestampFrequency"], report["FrameRateLimit"]
    rows = presents = maximum = consumed = 0
    last_end = last_present = last_admission = None
    sealed = False
    with nullcontext(source):
        while True:
            raw = source.readline(8193)
            if not raw:
                break
            if len(raw) > 8192 or not raw.endswith(b"\n") or consumed+len(raw) > total_bytes:
                raise ValueError("Unbounded/truncated line")
            value = json.loads(raw.decode("utf-8"), object_pairs_hook=unique)
            if type(value) is not dict:
                raise ValueError("Expected line object")
            if consumed == 0:
                closed(value, "kind version runId frequency queueCapacity maximumRecords frameRateLimit scope")
                if value != dict(kind="header", version=1, runId=stream["RunId"], frequency=frequency,
                                 queueCapacity=stream["QueueCapacity"], maximumRecords=stream["MaximumRecords"],
                                 frameRateLimit=cap, scope="owner-loop-not-display; retain gaps/faults; observation perturbs timings; startup/shutdown excluded"):
                    raise ValueError("Header mismatch")
                for key in ("version", "frequency", "queueCapacity", "maximumRecords"):
                    integer(value[key], 1)
                if value["frameRateLimit"] is not None:
                    integer(value["frameRateLimit"], 1, 240)
            elif sealed:
                raise ValueError("Data after seal")
            elif value.get("kind") == "sample":
                closed(value, "kind sample")
                row = value["sample"]
                closed(row, "Sequence FrameId Outcome StartedTimestamp EndedTimestamp OwnerAllocatedBytes Phases PresentReturnTimestamp PresentReturnIntervalTicks PacingAdmissionTimestamp")
                rows += 1
                if rows > count or integer(row["Sequence"], 1) != rows:
                    raise ValueError("Missing/reordered sample")
                integer(row["FrameId"], 0, 2**64-1)
                outcome = integer(row["Outcome"], 0, 3)
                start, end = integer(row["StartedTimestamp"]), integer(row["EndedTimestamp"])
                integer(row["OwnerAllocatedBytes"])
                if end < start or last_end is not None and start < last_end:
                    raise ValueError("Nonmonotonic sample")
                closed(row["Phases"], " ".join(PHASES))
                if sum(integer(row["Phases"][key]) for key in PHASES) != end-start:
                    raise ValueError("Invalid phase partition")
                present, interval = row["PresentReturnTimestamp"], row["PresentReturnIntervalTicks"]
                if present is None:
                    if interval is not None: raise ValueError("Interval without present")
                else:
                    integer(present, start, end)
                    expected = None if last_present is None else present-last_present
                    if interval != expected: raise ValueError("Wrong present interval")
                    if interval is not None: maximum = max(maximum, integer(interval))
                    last_present = present
                    presents += 1
                admission = row["PacingAdmissionTimestamp"]
                if cap is None:
                    if admission is not None or row["Phases"]["Pacing"]: raise ValueError("Fabricated pacing")
                elif admission is None:
                    if outcome not in (1,2) or present is not None: raise ValueError("Missing admission")
                else:
                    integer(admission, start, end)
                    if last_admission is not None and admission-last_admission < 1+(frequency-1)//cap:
                        raise ValueError("Rate violation")
                    last_admission = admission
                if rows <= len(report["Samples"]):
                    expected = dict(report["Samples"][rows-1])
                    if type(expected["Outcome"]) is str: expected["Outcome"] = OUTCOMES.index(expected["Outcome"])
                    if row != expected: raise ValueError("Prefix/stream mismatch")
                last_end = end
            elif value.get("kind") == "sealed":
                closed(value, "kind report")
                footer = value["report"]
                closed(footer, " ".join(stream))
                for key in stream:
                    if key not in ("Status","CompleteRawCoverage","Bytes","WriterWallTicks","WriterAllocatedBytes"):
                        if footer[key] != stream[key] or type(footer[key]) is not type(stream[key]):
                            raise ValueError("Seal/host mismatch")
                if footer["Status"] != "pending" or footer["CompleteRawCoverage"] is not False:
                    raise ValueError("Seal pretends flush complete")
                if integer(footer["Bytes"]) != consumed or integer(footer["WriterAllocatedBytes"]) != 0:
                    raise ValueError("Seal byte/allocation boundary")
                if integer(footer["WriterWallTicks"]) > stream["WriterWallTicks"]:
                    raise ValueError("Contradictory writer duration")
                sealed = True
            else:
                raise ValueError("Unknown line kind")
            consumed += len(raw)
    if not sealed or rows != count or consumed != total_bytes:
        raise ValueError("Missing final records/seal")
    if (presents != report["PresentReturns"] or max(0,presents-1) != report["PresentIntervals"] or
            maximum != report["MaximumPresentIntervalTicks"]):
        raise ValueError("Whole-stream aggregate mismatch")
    return dict(version=1, fullSamples=rows, prefixSamples=len(report["Samples"]), presentReturns=presents,
                maximumPresentIntervalTicks=maximum, bytes=total_bytes, targetPerformanceAccepted=False)


def main():
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stream")
    parser.add_argument("report")
    parser.add_argument("--player", action="store_true")
    args = parser.parse_args()
    report = json.loads(Path(args.report).read_text(encoding="utf-8-sig"), object_pairs_hook=unique)
    print(json.dumps(verify(args.stream, report["loopCosts"] if args.player else report, camel_case=args.player)))


if __name__ == "__main__":
    main()
