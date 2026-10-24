"""Strict v2 loop/pacing evidence; historical v1 contract is not modified."""
import copy
import json
from pathlib import Path
from ncma_tools.ecs_e5d_loop_evidence import verify as verify_v1


def verify(report, *, camel_case=False):
    def key(name):
        return name[0].lower()+name[1:] if camel_case else name
    def get(obj, name):
        if not isinstance(obj, dict) or key(name) not in obj:
            raise ValueError("Missing " + name)
        return obj[key(name)]
    if type(get(report, "Version")) is not int or get(report, "Version") != 2:
        raise ValueError("Expected loop report v2")
    cap = get(report, "FrameRateLimit")
    if cap is not None and (type(cap) is not int or not 1 <= cap <= 240):
        raise ValueError("Invalid cap")
    frequency = get(report, "TimestampFrequency")
    if type(frequency) is not int or frequency <= 0:
        raise ValueError("Invalid frequency")
    rows = get(report, "Samples")
    if not isinstance(rows, list):
        raise ValueError("Expected raw samples")
    previous = None
    minimum = None if cap is None else 1+(frequency-1)//cap
    admitted = 0
    # In-memory reuse of unchanged v1 generic raw/phase/Present checks.
    # This is not emitted as a v1 result or used to relabel historical evidence.
    common = copy.deepcopy(report)
    common[key("Version")] = 1
    for index, row in enumerate(rows):
        admission = get(row, "PacingAdmissionTimestamp")
        phases = get(row, "Phases")
        pacing = get(phases, "Pacing")
        if type(pacing) is not int or pacing < 0:
            raise ValueError("Invalid pacing duration")
        outcome = get(row, "Outcome")
        if cap is None:
            if admission is not None or pacing != 0:
                raise ValueError("Uncapped loop fabricates pacing")
        elif admission is None:
            if outcome not in ("Stopped", "Faulted", 1, 2) or type(outcome) is bool:
                raise ValueError("Completed capped loop missing admission")
            if get(row, "PresentReturnTimestamp") is not None:
                raise ValueError("Present without paced admission")
        else:
            start, end = get(row, "StartedTimestamp"), get(row, "EndedTimestamp")
            if type(admission) is not int or type(start) is not int or type(end) is not int or not start <= admission <= end:
                raise ValueError("Admission outside loop")
            if previous is not None and admission-previous < minimum:
                raise ValueError("Admission exceeds configured rate")
            previous = admission
            admitted += 1
        control = get(phases, "Control")
        if type(control) is not int or control < 0:
            raise ValueError("Invalid control duration")
        common[key("Samples")][index][key("Phases")][key("Control")] = control+pacing
    result = verify_v1(common, camel_case=camel_case)
    return dict(result, frameRateLimit=cap, retainedAdmissions=admitted, minimumAdmissionTicks=minimum)


def main():
    import argparse
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    parser.add_argument("--player", action="store_true")
    args=parser.parse_args()
    data=json.loads(Path(args.report).read_text(encoding="utf-8-sig"))
    print(json.dumps(verify(data["loopCosts"] if args.player else data, camel_case=args.player)))


if __name__ == "__main__":
    main()
