"""Offline Present-return statistics, never physical-display or target acceptance.

Usage: python -m ncma_tools.ecs_e5d_present_metrics STREAM REPORT [--player]
Reads only; emits one structured JSON result. No sampling, filtering or warmup trim.
"""
import argparse
from fractions import Fraction
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import sys

from ncma_tools.ecs_e5d_stream_evidence import normalize, unique, verify_bytes

STATISTICS_VERSION = "ncma.present-return.v1"
MAX_REPORT_BYTES = 4 * 1024**2
MAX_STREAM_BYTES = 1024**3


def interval_metrics(interval_ticks, frequency):
    """Exact whole-window arithmetic. At least 100 intervals, not a duration gate."""
    if type(frequency) is not int or not 1 <= frequency <= 2**63 - 1:
        raise ValueError("Invalid timestamp frequency")
    if type(interval_ticks) is not list or not 100 <= len(interval_ticks) <= 1_000_000:
        raise ValueError("Require 100..1000000 complete Present intervals")
    if any(type(t) is not int or not 1 <= t <= 2**63 - 1 for t in interval_ticks):
        raise ValueError("Every interval must be a positive bounded integer")
    count = len(interval_ticks)
    tail_count = (count + 99) // 100
    total = sum(interval_ticks)
    tail_total = sum(sorted(interval_ticks, reverse=True)[:tail_count])
    average = Fraction(count * frequency, total)
    low = Fraction(tail_count * frequency, tail_total)
    hitch = Fraction(max(interval_ticks) * 1000, frequency)

    def exact(value):
        return dict(numerator=value.numerator, denominator=value.denominator)

    return dict(statisticsVersion=STATISTICS_VERSION, intervalCount=count, tailCount=tail_count,
        totalTicks=total, tailTotalTicks=tail_total, timestampFrequency=frequency,
        averageFps=exact(average), onePercentLowFps=exact(low), maximumHitchMs=exact(hitch),
        thresholdComparisons=dict(averageAtLeast30=average >= 30, onePercentLowAtLeast27=low >= 27,
                                  noHitchOver80=hitch <= 80),
        thresholdsAreAcceptance=False,
        formula="N*f/sum(all); ceil(N/100)*f/sum(slowest ceil(N/100)); max(all)*1000/f",
        targetPerformanceAccepted=False)


def _reject_constant(value):
    raise ValueError("Non-finite JSON constant: " + value)


def _json(raw):
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique, parse_constant=_reject_constant)


def _hash(raw):
    return hashlib.sha256(raw).hexdigest().upper()


def analyze_bytes(report_bytes, stream_bytes, *, player=False):
    """Bind and validate the same immutable bytes that feed the arithmetic."""
    if type(player) is not bool:
        raise ValueError("Explicit boolean report shape required")
    if type(report_bytes) is not bytes or not 1 <= len(report_bytes) <= MAX_REPORT_BYTES:
        raise ValueError("Invalid bounded report bytes")
    if type(stream_bytes) is not bytes or not 1 <= len(stream_bytes) <= MAX_STREAM_BYTES:
        raise ValueError("Invalid bounded stream bytes")
    report = _json(report_bytes)
    if type(report) is not dict:
        raise ValueError("Expected report object")
    if player:
        if (type(report.get("schemaVersion")) is not int or report["schemaVersion"] != 1 or
                type(report.get("exitCode")) is not int or report["exitCode"] != 0 or
                report.get("reason") != "completed" or report.get("fault") is not None or
                report.get("reportError") is not None or report.get("shutdownErrors") != []):
            raise ValueError("Player report did not complete successfully")
        for key in ("validationErrors", "validationWarnings"):
            if type(report.get(key)) is not int or report[key] != 0:
                raise ValueError("Player validation diagnostics present")
        report = report.get("loopCosts")
    if type(report) is not dict:
        raise ValueError("Missing loop report")
    validated = verify_bytes(stream_bytes, report, camel_case=player)
    data = normalize(report) if player else report
    intervals, outcomes = [], [0, 0, 0, 0]
    count = without_present = 0
    first_start = first_present = last_present = last_end = None
    # BytesIO avoids materializing a second list of every line in a long stream.
    with io.BytesIO(stream_bytes) as source:
        for raw in source:
            entry = _json(raw)
            if entry["kind"] != "sample":
                continue
            row = entry["sample"]
            count += 1
            outcomes[row["Outcome"]] += 1
            if first_start is None:
                first_start = row["StartedTimestamp"]
            last_end = row["EndedTimestamp"]
            present = row["PresentReturnTimestamp"]
            if present is None:
                without_present += 1
            else:
                if first_present is None:
                    first_present = present
                last_present = present
            interval = row["PresentReturnIntervalTicks"]
            if interval is not None:
                intervals.append(interval)
    if count != validated["fullSamples"] or len(intervals) != max(0, validated["presentReturns"] - 1):
        raise ValueError("Complete parsed sample counts disagree")
    arithmetic = interval_metrics(intervals, data["TimestampFrequency"])
    return dict(schema=1, statisticsVersion=STATISTICS_VERSION,
        inputHashes=dict(reportSha256=_hash(report_bytes), streamSha256=_hash(stream_bytes)),
        reportShape="player" if player else "loop", frameRateLimit=data["FrameRateLimit"],
        fullStreamValidation=validated, declaredStreamCoverageVerified=True,
        workloadBoundaryVerified=False, inputOriginVerified=False, physicalDisplayVerified=False,
        targetPerformanceAccepted=False,
        outcomes=dict(zip(("completed", "stopped", "faulted", "recovered"), outcomes)),
        iterationsWithoutPresent=without_present,
        boundary=dict(firstLoopStart=first_start, lastLoopEnd=last_end,
            firstPresentReturn=first_present, lastPresentReturn=last_present,
            beforeFirstPresentTicks=first_present-first_start, afterLastPresentTicks=last_end-last_present),
        arithmetic=arithmetic,
        scope="entire supplied validated stream; fault/recovery retained; startup/shutdown and exterior gaps unknown; not display FPS")


def _read_bounded(path, limit):
    with Path(path).open("rb") as source:
        before = os.fstat(source.fileno())
        if not stat.S_ISREG(before.st_mode) or not 1 <= before.st_size <= limit:
            raise ValueError("Expected bounded regular input file")
        raw = source.read(limit + 1)
        after = os.fstat(source.fileno())
        if len(raw) != before.st_size or after.st_size != before.st_size or after.st_mtime_ns != before.st_mtime_ns:
            raise ValueError("Input changed while reading")
        return raw


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        raise ValueError(message)


def main(argv=None):
    parser = _Parser(description=__doc__)
    parser.add_argument("stream")
    parser.add_argument("report")
    parser.add_argument("--player", action="store_true")
    try:
        args = parser.parse_args(argv)
    except ValueError as error:
        print(json.dumps(dict(schema=1, ok=False, error=dict(code="invalid_arguments", message=str(error)))))
        return 2
    try:
        result = analyze_bytes(_read_bounded(args.report, MAX_REPORT_BYTES),
                               _read_bounded(args.stream, MAX_STREAM_BYTES), player=args.player)
    except (ValueError, OSError, TypeError, KeyError, OverflowError) as error:
        print(json.dumps(dict(schema=1, ok=False, error=dict(code="invalid_evidence", message=str(error)))))
        return 2
    print(json.dumps(dict(schema=1, ok=True, result=result), allow_nan=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
