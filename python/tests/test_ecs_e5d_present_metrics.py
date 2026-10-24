import copy
from fractions import Fraction
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from ncma_tools.ecs_e5d_present_metrics import interval_metrics, analyze_bytes
from test_ecs_e5d_stream_evidence import fixture, encoded


def fraction(result, key):
    value = result[key]
    return Fraction(value["numerator"], value["denominator"])


def capture(report, lines, player=False):
    raw = encoded(report, lines)
    if player:
        def camel(value):
            if isinstance(value, dict):
                return {key[0].lower()+key[1:]: camel(item) for key,item in value.items()}
            return [camel(item) for item in value] if isinstance(value, list) else value
        report = dict(schemaVersion=1, exitCode=0, reason="completed", fault=None, reportError=None,
                      shutdownErrors=[], validationErrors=0, validationWarnings=0, loopCosts=camel(report))
    return json.dumps(report).encode(), raw


class PresentMetricsTests(unittest.TestCase):
    def test_exact_budget_boundaries_without_tolerance(self):
        result = interval_metrics([100] * 100, 3000)
        self.assertEqual(fraction(result, "averageFps"), 30)
        self.assertTrue(all(result["thresholdComparisons"].values()))
        self.assertFalse(result["thresholdsAreAcceptance"])
        self.assertFalse(result["targetPerformanceAccepted"])
        self.assertFalse(interval_metrics([100]*99+[101],3000)["thresholdComparisons"]["averageAtLeast30"])
        self.assertTrue(interval_metrics([900]*99+[1000],27000)["thresholdComparisons"]["onePercentLowAtLeast27"])
        self.assertFalse(interval_metrics([900]*99+[1001],27000)["thresholdComparisons"]["onePercentLowAtLeast27"])
        self.assertTrue(interval_metrics([30]*99+[80],1000)["thresholdComparisons"]["noHitchOver80"])
        self.assertFalse(interval_metrics([30]*99+[81],1000)["thresholdComparisons"]["noHitchOver80"])

    def test_tail_ceil_duration_average_and_exact_large_integers(self):
        result = interval_metrics([10]*199+[20,30],1000)
        self.assertEqual((result["tailCount"], result["tailTotalTicks"]),(3,60))
        self.assertEqual(fraction(result,"onePercentLowFps"),50)
        self.assertEqual(fraction(interval_metrics([10]*50+[20]*50,1000),"averageFps"),Fraction(200,3))
        limit = 2**63 - 1
        self.assertEqual(fraction(interval_metrics([limit]*100,limit),"maximumHitchMs"),1000)

    def test_invalid_arithmetic_rejected_and_input_unchanged(self):
        rows=list(range(1,202)); before=rows[:]
        self.assertEqual(interval_metrics(rows,1000),interval_metrics(rows[::-1],1000))
        self.assertEqual(rows,before)
        for invalid in (0,-1,True,1.0,None,2**63):
            with self.assertRaises(ValueError): interval_metrics([1]*100,invalid)
            with self.assertRaises(ValueError): interval_metrics([1]*99+[invalid],1000)
        for rows in ([],[1]*99,[1]*1_000_001,(1,)*100):
            with self.assertRaises(ValueError): interval_metrics(rows,1000)

    def test_full_suffix_both_shapes_hashes_and_nonacceptance(self):
        for player in (False,True):
            report,stream=capture(*fixture(),player=player)
            result=analyze_bytes(report,stream,player=player)
            self.assertEqual(result["fullStreamValidation"]["fullSamples"],600)
            self.assertEqual(result["arithmetic"]["intervalCount"],599)
            self.assertEqual(result["inputHashes"]["reportSha256"],hashlib.sha256(report).hexdigest().upper())
            self.assertEqual(result["inputHashes"]["streamSha256"],hashlib.sha256(stream).hexdigest().upper())
            self.assertEqual(result["outcomes"],dict(completed=150,stopped=150,faulted=150,recovered=150))
            self.assertIsNone(result["frameRateLimit"])
            for key in ("workloadBoundaryVerified","inputOriginVerified","physicalDisplayVerified","targetPerformanceAccepted"):
                self.assertFalse(result[key])

    def test_fault_recovery_gap_retained(self):
        report,lines=fixture(); rows=[line["sample"] for line in lines[1:-1]]
        rows[512].update(Outcome=2,PresentReturnTimestamp=None,PresentReturnIntervalTicks=None)
        for row in rows[513:]:
            for key in ("StartedTimestamp","EndedTimestamp","PresentReturnTimestamp"): row[key]+=100
        rows[513].update(Outcome=3,PresentReturnIntervalTicks=140)
        report.update(PresentReturns=599,PresentIntervals=598,MaximumPresentIntervalTicks=140)
        result=analyze_bytes(*capture(report,lines))
        self.assertEqual(result["iterationsWithoutPresent"],1)
        self.assertEqual(result["arithmetic"]["totalTicks"],12080)
        self.assertEqual(fraction(result["arithmetic"],"maximumHitchMs"),140)
        self.assertFalse(result["arithmetic"]["thresholdComparisons"]["noHitchOver80"])

    def test_bad_streams_rejected(self):
        mutations=[lambda r,l:r["Stream"].update(QueueDrops=1),lambda r,l:r["Stream"].update(Status="pending"),
            lambda r,l:l.pop(513),lambda r,l:l.pop(),lambda r,l:l[513]["sample"].update(Sequence=512),
            lambda r,l:l[513]["sample"].update(PresentReturnIntervalTicks=1),
            lambda r,l:l[513]["sample"].update(OwnerAllocatedBytes=-1)]
        for mutation in mutations:
            report,lines=fixture(); mutation(report,lines)
            with self.assertRaises(ValueError): analyze_bytes(*capture(report,lines))
        report,stream=capture(*fixture())
        with self.assertRaises(ValueError): analyze_bytes(report,stream[:-1])
        with self.assertRaises(ValueError): analyze_bytes(report,bytearray(stream))
        with self.assertRaises(ValueError): analyze_bytes(report,stream,player=1)

    def test_duplicate_nonfinite_and_failed_player_rejected(self):
        report,stream=capture(*fixture())
        for raw in (report.replace(b'"Version": 2',b'"Version": 2,"Version": 2',1), b'{"x":NaN}',b'[]'):
            with self.assertRaises(ValueError): analyze_bytes(raw,stream)
        raw,stream=capture(*fixture(),player=True); original=json.loads(raw)
        for key,value in (("exitCode",1),("exitCode",True),("fault","fault"),("reason","timed_out"),
                          ("reportError","failed"),("shutdownErrors",["failed"]),("validationWarnings",1)):
            changed=copy.deepcopy(original); changed[key]=value
            with self.assertRaises(ValueError): analyze_bytes(json.dumps(changed).encode(),stream,player=True)

    def test_null_stream_never_generates_fps(self):
        report,lines=fixture()
        for line in lines[1:-1]: line["sample"].update(PresentReturnTimestamp=None,PresentReturnIntervalTicks=None)
        report.update(PresentReturns=0,PresentIntervals=0,MaximumPresentIntervalTicks=0)
        report["Samples"]=[copy.deepcopy(line["sample"]) for line in lines[1:513]]
        with self.assertRaisesRegex(ValueError,"Require 100"):
            analyze_bytes(*capture(report,lines))

    def test_real_cli_success_and_structured_failures_no_input_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); report,stream=capture(*fixture(),player=True)
            rp,sp=root/"report.json",root/"full.ncloop"; rp.write_bytes(report);sp.write_bytes(stream)
            command=[sys.executable,"-B","-m","ncma_tools.ecs_e5d_present_metrics"]
            env=dict(os.environ,PYTHONPATH=str(Path(__file__).resolve().parents[1]/"src"))
            cases=[([str(sp),str(rp),"--player"],0,None),([],2,"invalid_arguments"),
                   ([str(sp),str(rp),"--unknown"],2,"invalid_arguments"),
                   ([str(root/"missing"),str(rp),"--player"],2,"invalid_evidence"),
                   ([str(sp),str(rp)],2,"invalid_evidence")]
            for args,expected,code in cases:
                completed=subprocess.run(command+args,env=env,capture_output=True,timeout=20)
                self.assertEqual(completed.returncode,expected,completed.stderr)
                self.assertEqual(completed.stderr,b"")
                result=json.loads(completed.stdout)
                self.assertEqual(result["ok"],expected==0)
                if code:self.assertEqual(result["error"]["code"],code)
            self.assertEqual(rp.read_bytes(),report);self.assertEqual(sp.read_bytes(),stream)
