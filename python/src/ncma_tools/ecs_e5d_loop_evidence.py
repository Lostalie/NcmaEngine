"""Validate the actual host loop's bounded raw prefix, never infer target FPS."""
import json
from pathlib import Path


def verify(report, *, camel_case=False):
    def read(obj, name):
        if not isinstance(obj, dict):
            raise ValueError("Expected object")
        key = name[0].lower() + name[1:] if camel_case else name
        if key not in obj:
            raise ValueError("Missing " + key)
        return obj[key]

    def integer(value, minimum=0):
        if type(value) is not int or value < minimum or value > 2**63 - 1:
            raise ValueError("Invalid integer")
        return value

    if read(report, "Version") != 1 or type(read(report, "Version")) is not int:
        raise ValueError("Unsupported version")
    if read(report, "TargetPerformanceAccepted") is not False:
        raise ValueError("Not a target acceptance report")
    if read(report, "Scope") != "owner-loop-wall-including-observation; present-return-not-display; startup/shutdown excluded":
        raise ValueError("Wrong measurement scope")
    frequency = integer(read(report, "TimestampFrequency"), 1)
    capacity = integer(read(report, "Capacity"), 1)
    total = integer(read(report, "TotalIterations"))
    omitted = integer(read(report, "OmittedIterations"))
    rows = read(report, "Samples")
    if capacity > 512 or not isinstance(rows, list) or len(rows) != min(total, capacity) or omitted != total - len(rows):
        raise ValueError("Incomplete prefix or incorrect omission count")
    if read(report, "CompleteRawCoverage") is not (omitted == 0):
        raise ValueError("False raw coverage claim")
    presents = integer(read(report, "PresentReturns"))
    intervals = integer(read(report, "PresentIntervals"))
    maximum = integer(read(report, "MaximumPresentIntervalTicks"))
    if presents > total or intervals != max(0, presents - 1):
        raise ValueError("Invalid aggregate Present counters")
    previous_end = last_present = None
    seen_presents = seen_maximum = 0
    for index, row in enumerate(rows):
        if integer(read(row, "Sequence"), 1) != index + 1:
            raise ValueError("Non-prefix sequence")
        integer(read(row, "FrameId"))
        outcome = read(row, "Outcome")
        if not (type(outcome) is int and outcome in range(4) or
                type(outcome) is str and outcome in ("Completed", "Stopped", "Faulted", "Recovered")):
            raise ValueError("Invalid outcome")
        start = integer(read(row, "StartedTimestamp"))
        end = integer(read(row, "EndedTimestamp"))
        integer(read(row, "OwnerAllocatedBytes"))
        if end < start or previous_end is not None and start < previous_end:
            raise ValueError("Nonmonotonic loop timestamps")
        phases = read(row, "Phases")
        phase_sum = sum(integer(read(phases, key)) for key in (
            "Control", "Platform", "Intents", "Agent", "BeginPresentation",
            "Input", "Simulation", "Presentation", "Recovery"))
        if phase_sum != end - start:
            raise ValueError("Phases do not partition wall interval")
        present = read(row, "PresentReturnTimestamp")
        interval = read(row, "PresentReturnIntervalTicks")
        if present is None:
            if interval is not None:
                raise ValueError("Interval without Present")
        else:
            integer(present)
            if not start <= present <= end:
                raise ValueError("Present outside observed loop")
            expected = None if last_present is None else present - last_present
            if interval != expected or interval is not None and type(interval) is not int:
                raise ValueError("Invalid Present return interval")
            if interval is not None:
                integer(interval)
                seen_maximum = max(seen_maximum, interval)
            seen_presents += 1
            last_present = present
        previous_end = end
    if presents < seen_presents or maximum < seen_maximum or intervals == 0 and maximum != 0:
        raise ValueError("Aggregate counters contradict prefix")
    if not omitted and (presents != seen_presents or maximum != seen_maximum):
        raise ValueError("Complete raw data differs from aggregate")
    return dict(iterations=total, retained=len(rows), omitted=omitted, presentReturns=presents,
                timestampFrequency=frequency, targetPerformanceAccepted=False)


def main():
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    parser.add_argument("--player", action="store_true")
    args = parser.parse_args()
    data = json.loads(Path(args.report).read_text(encoding="utf-8-sig"))
    print(json.dumps(verify(data["loopCosts"] if args.player else data, camel_case=args.player)))


if __name__ == "__main__":
    main()
