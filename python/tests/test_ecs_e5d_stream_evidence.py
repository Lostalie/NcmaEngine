import copy
import json
from pathlib import Path
import tempfile
import unittest
from ncma_tools.ecs_e5d_stream_evidence import verify, verify_bytes, SCOPE, PHASES


def fixture():
    rows = []
    for i in range(600):
        phases = dict.fromkeys(PHASES, 0)
        phases["Control"] = 10
        rows.append(dict(Sequence=i+1, FrameId=i//2, Outcome=i % 4, StartedTimestamp=100+i*20,
                         EndedTimestamp=110+i*20, OwnerAllocatedBytes=0, Phases=phases,
                         PresentReturnTimestamp=109+i*20, PresentReturnIntervalTicks=None if i == 0 else 20,
                         PacingAdmissionTimestamp=None))
    stream = dict(Version=1, RunId="11111111-1111-1111-1111-111111111111", Status="completed",
                  QueueCapacity=4096, MaximumRecords=1000000, Produced=600, Accepted=600, Written=600,
                  QueueDrops=0, BudgetDrops=0, FailureDrops=0, Unwritten=0, PublishTicks=3,
                  PublishAllocatedBytes=0, WriterWallTicks=7, WriterAllocatedBytes=100, Bytes=0,
                  Failure=None, CompleteRawCoverage=True, TargetPerformanceAccepted=False, Scope=SCOPE)
    report = dict(Version=2, TimestampFrequency=1000, Capacity=512, TotalIterations=600,
                  OmittedIterations=88, PresentReturns=600, PresentIntervals=599,
                  MaximumPresentIntervalTicks=20, Samples=copy.deepcopy(rows[:512]), FrameRateLimit=None,
                  Scope="owner-loop-wall-including-observation; present-return-not-display; startup/shutdown excluded",
                  TargetPerformanceAccepted=False, CompleteRawCoverage=False, Stream=stream)
    header = dict(kind="header", version=1, runId=stream["RunId"], frequency=1000, queueCapacity=4096,
                  maximumRecords=1000000, frameRateLimit=None,
                  scope="owner-loop-not-display; retain gaps/faults; observation perturbs timings; startup/shutdown excluded")
    footer = dict(stream, Status="pending", CompleteRawCoverage=False, WriterWallTicks=5, WriterAllocatedBytes=0)
    return report, [header]+[dict(kind="sample", sample=r) for r in rows]+[dict(kind="sealed", report=footer)]


def encoded(report, lines):
    parts = [json.dumps(line, separators=(",", ":")).encode()+b"\n" for line in lines[:-1]]
    if isinstance(lines[-1], dict) and lines[-1].get("kind") == "sealed":
        lines[-1]["report"]["Bytes"] = sum(map(len, parts))
    parts.append(json.dumps(lines[-1], separators=(",", ":")).encode()+b"\n")
    data = b"".join(parts)
    report["Stream"]["Bytes"] = len(data)
    return data


class StreamEvidenceTests(unittest.TestCase):
    def test_immutable_bytes_entry_matches_file(self):
        report, lines = fixture()
        raw = encoded(report, lines)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "full.ncloop"
            path.write_bytes(raw)
            self.assertEqual(verify_bytes(raw, report), verify(path, report))
        for bad in (bytearray(raw), b"", raw[:-1]):
            with self.assertRaises(ValueError):
                verify_bytes(bad, report)

    def test_suffix_beyond_512_and_player_shape(self):
        report, lines = fixture()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/"full.ncloop"
            path.write_bytes(encoded(report, lines))
            result = verify(path, report)
            self.assertEqual(result["fullSamples"], 600)
            self.assertEqual(result["presentReturns"], 600)
            self.assertFalse(result["targetPerformanceAccepted"])
            def camel(value):
                if isinstance(value, dict): return {k[0].lower()+k[1:]: camel(v) for k,v in value.items()}
                return [camel(v) for v in value] if isinstance(value,list) else value
            player = camel(report)
            for row in player["samples"]:
                row["outcome"] = ("Completed","Stopped","Faulted","Recovered")[row["outcome"]]
            self.assertEqual(verify(path,player,camel_case=True),result)
            ambiguous=copy.deepcopy(player)
            ambiguous["Stream"]=ambiguous["stream"]
            with self.assertRaises(ValueError):
                verify(path,ambiguous,camel_case=True)
            ambiguous=copy.deepcopy(player)
            ambiguous["samples"][0]["phases"]["Control"]=10
            with self.assertRaises(ValueError):
                verify(path,ambiguous,camel_case=True)

    def test_corrupt_suffix_and_host_rejected(self):
        mutations = [
            lambda r,l: r["Stream"].update(Status="pending"),
            lambda r,l: r["Stream"].update(Status="close_timeout"),
            lambda r,l: r["Stream"].update(QueueDrops=1),
            lambda r,l: r["Stream"].update(BudgetDrops=1),
            lambda r,l: r["Stream"].update(FailureDrops=1),
            lambda r,l: r["Stream"].update(Unwritten=1),
            lambda r,l: r["Stream"].update(Written=True),
            lambda r,l: r["Stream"].update(Failure="write_failed"),
            lambda r,l: r["Stream"].update(TargetPerformanceAccepted=True),
            lambda r,l: l[0].update(frequency=True),
            lambda r,l: l[0].update(runId="22222222-2222-2222-2222-222222222222"),
            lambda r,l: l[513]["sample"].update(Sequence=512),
            lambda r,l: l[513]["sample"].update(Outcome=True),
            lambda r,l: l[513]["sample"].update(OwnerAllocatedBytes=-1),
            lambda r,l: l[513]["sample"].update(StartedTimestamp=0),
            lambda r,l: l[513]["sample"].update(PresentReturnIntervalTicks=21),
            lambda r,l: l[513]["sample"].update(PresentReturnTimestamp=None),
            lambda r,l: l[513]["sample"]["Phases"].update(Recovery=1),
            lambda r,l: l[513]["sample"].update(PacingAdmissionTimestamp=10360),
            lambda r,l: l[513]["sample"].update(Unknown=0),
            lambda r,l: l[-1]["report"].update(Status="completed"),
            lambda r,l: l[-1]["report"].update(CompleteRawCoverage=True),
            lambda r,l: l[-1]["report"].update(Produced=599),
            lambda r,l: l[-1]["report"].update(WriterWallTicks=8),
            lambda r,l: l[-1]["report"].update(WriterAllocatedBytes=2),
            lambda r,l: l.pop(513),
            lambda r,l: l.pop(),
            lambda r,l: l.append(dict(kind="extra")),
        ]
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"bad.ncloop"
            for index, mutation in enumerate(mutations):
                report,lines=fixture()
                mutation(report,lines)
                path.write_bytes(encoded(report,lines))
                with self.subTest(index=index), self.assertRaises(ValueError):
                    verify(path,report)

    def test_duplicate_truncated_and_oversized_lines(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"bad.ncloop"
            for change in (
                lambda data: data.replace(b'"kind":"sample"',b'"kind":"sample","kind":"sample"',1),
                lambda data: data[:-1],
                lambda data: data+b" "*8193+b"\n",
                lambda data: b"[]\n"+data,
            ):
                report,lines=fixture()
                data=change(encoded(report,lines))
                report["Stream"]["Bytes"]=len(data)
                path.write_bytes(data)
                with self.assertRaises(ValueError):
                    verify(path,report)
