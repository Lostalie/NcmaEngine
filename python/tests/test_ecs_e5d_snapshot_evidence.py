import copy
import unittest
import hashlib
import json
from pathlib import Path
import tempfile
import test_ecs_e5d_domain_evidence as old_tests
from ncma_tools.ecs_e5d_snapshot_evidence import validate, verify, CONTRACT


class SnapshotEvidenceTests(unittest.TestCase):
    def fixture(self, pending=False):
        data = old_tests.EcsDomainEvidenceTests().fixture()
        data.update(schema=3, contractId=CONTRACT)
        for mode, w in enumerate(data["windows"]):
            for i, row in enumerate(w["rows"]):
                p = row["Presentation"]; p.update(Version=2, HostFrame=i+11)
                for name in ("Main", "Skin", "Ui"):
                    a = p[name]; t = a["Sample"]["Timing"]
                    t["ObservedFrame"] = p["HostFrame"]
                    if t["Sequence"]:
                        t["OriginFrame"] += 10
                    p[name+"Resolved"] = copy.deepcopy(a)
                if pending:
                    current, resolved = p["Main"], p["MainResolved"]
                    current["Sample"]["Timing"].update(State=2, IsValid=False, Milliseconds=0, Sequence=i+2)
                    current["Attribution"] = 0
                    resolved["Sample"]["Timing"]["OriginFrame"] -= 1; resolved["Attribution"] = 2
            baseline = copy.deepcopy(w["rows"][0]["Presentation"])
            baseline["HostFrame"] = 10
            for name in ("Main", "Skin", "Ui"):
                a = baseline[name]; t = a["Sample"]["Timing"]
                t.update(Sequence=0, OriginFrame=0, ObservedFrame=10, State=0, IsValid=False, Milliseconds=0)
                a["Attribution"] = 0; baseline[name+"Resolved"] = copy.deepcopy(a)
            w["baselinePresentation"] = baseline
            resolved = [dict(Generation=mode+1, Domain=1, Sequence=i+1, OriginFrame=i+(10 if pending else 11),
                            State=3, Resource=0, ContentRevision=0, Milliseconds=.3, InMeasuredWindow=not pending or i != 0) for i in range(100)]
            domains = []
            for domain in (1, 2, 3):
                states = [0]*8; states[(2 if pending else 3) if domain == 1 else 0] = 100
                resolved_states = [0]*8; resolved_states[3 if domain == 1 else 0] = 100
                domains.append(dict(domain=domain, uniqueMeasuredValidSources=(99 if pending else 100) if domain == 1 else 0,
                    uniqueOutsideWindowValidSources=int(domain == 1 and pending), uniqueInvalidResolvedSources=0,
                    currentStates=states, resolvedStates=resolved_states, admissionSequenceGaps=int(domain == 1 and pending),
                    unobservedResolutionSequenceSlots=0, unobservedTailSequenceSlots=int(domain == 1 and pending)))
            w["gpu"] = dict(resolved=resolved, duplicateResolvedObservations=0, domains=domains, completeGpuCoverage=False)
        return data

    def test_valid_current_and_pending_completed_separate(self):
        for pending in (False, True):
            with self.subTest(pending=pending):
                result = validate(self.fixture(pending))
                self.assertEqual(result["uniqueResolvedSources"], 300)
                self.assertFalse(result["completeGpuCoverage"])

    def test_invalid_resolution_and_sequence_gaps_not_hidden(self):
        data = self.fixture(True); w = data["windows"][0]
        for i in range(50, 100):
            for name in ("Main", "MainResolved"):
                w["rows"][i]["Presentation"][name]["Sample"]["Timing"]["Sequence"] += 2
            w["gpu"]["resolved"][i]["Sequence"] += 2
        a = w["rows"][50]["Presentation"]["MainResolved"]
        a["Sample"]["Timing"].update(State=5, IsValid=False, Milliseconds=0); a["Attribution"] = 0
        w["gpu"]["resolved"][50].update(State=5, Milliseconds=0)
        d = w["gpu"]["domains"][0]
        d.update(admissionSequenceGaps=3, unobservedResolutionSequenceSlots=2, uniqueInvalidResolvedSources=1, uniqueMeasuredValidSources=98)
        d["resolvedStates"][3] -= 1; d["resolvedStates"][5] += 1
        self.assertEqual(validate(data)["samples"], 300)

    def test_mutations_reject(self):
        base = self.fixture(True)
        def p(d): return d["windows"][0]["rows"][0]["Presentation"]
        def rt(d): return p(d)["MainResolved"]["Sample"]["Timing"]
        mutations = [
            lambda d: d.update(schema=2), lambda d: d.update(contractId="old"),
            lambda d: p(d).update(Version=1), lambda d: p(d).pop("MainResolved"),
            lambda d: p(d).update(Completed=False), lambda d: rt(d).update(State=2, IsValid=False, Milliseconds=0),
            lambda d: rt(d).update(Generation=9), lambda d: rt(d).update(Sequence=2),
            lambda d: rt(d).update(Sequence=2**64), lambda d: rt(d).update(Sequence=True),
            lambda d: rt(d).update(OriginFrame=12), lambda d: rt(d).update(ObservedFrame=12),
            lambda d: rt(d).update(Milliseconds=float("nan")), lambda d: rt(d).update(Milliseconds=-1),
            lambda d: rt(d).update(SkippedSamples=1), lambda d: p(d)["MainResolved"].update(Attribution=1),
            lambda d: p(d)["UiResolved"]["Sample"].update(Resource=1),
            lambda d: d["windows"][0]["baselinePresentation"].update(HostFrame=11),
            lambda d: d["windows"][0]["baselinePresentation"].update(Generation=9),
            lambda d: d["windows"][0]["gpu"]["domains"][0].update(unobservedTailSequenceSlots=0),
            lambda d: d["windows"][0]["gpu"]["domains"][0].update(admissionSequenceGaps=0),
            lambda d: d["windows"][0]["gpu"]["domains"][0].update(unobservedTailSequenceSlots=True),
            lambda d: d["windows"][0]["gpu"].update(completeGpuCoverage=True),
            lambda d: d["windows"][0]["gpu"]["resolved"].pop(),
        ]
        for i, mutate in enumerate(mutations):
            with self.subTest(i=i):
                data = copy.deepcopy(base); mutate(data)
                with self.assertRaises((ValueError, KeyError)):
                    validate(data)

    def test_actual_file_binding_rejects_raw_boolean_and_changed_bytes(self):
        # Files are synthetic verifier fixtures, not executable engine products.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); evidence = root/"relocated-editor"; product = root/"product-editor"
            evidence.mkdir(); product.mkdir()
            data = self.fixture(True)
            def save(path, value): path.write_text(json.dumps(value), encoding="utf-8")
            report = evidence/"ecs-running-domain-costs.json"; save(report, data)
            raws = []
            for i, w in enumerate(data["windows"]):
                raw = dict(schema=3, contractId=CONTRACT, label=w["label"], completedSamples=100, failure=None,
                           baselinePresentation=w["baselinePresentation"], rows=w["rows"], memoryBefore=w["memoryBefore"], memoryAfter=w["memoryAfter"])
                raws.append(copy.deepcopy(raw)); save(evidence/f"ecs-running-raw-{i}.json", raw)
            save(evidence/"request.json", dict(kind="editor", productRoot=str(product)))
            entries, loaded = [], []
            for i in range(5):
                path = product/f"fixture-{i}.dll"; content = f"nonexecutable-{i}".encode(); path.write_bytes(content)
                sha = hashlib.sha256(content).hexdigest().upper()
                entries.append(dict(path=path.name, sha256=sha, size=len(content))); loaded.append(dict(path=str(path), sha256=sha))
            save(product/"deployment-manifest.json", dict(files=entries)); save(evidence/"loaded-product-assemblies.json", loaded)
            self.assertEqual(verify(report)["actualLoadedAssemblies"], 5)
            raw = copy.deepcopy(raws[0]); raw["rows"][0]["Presentation"]["Main"]["Sample"]["Timing"]["SkippedSamples"] = False
            save(evidence/"ecs-running-raw-0.json", raw)
            with self.assertRaises(ValueError): verify(report)
            save(evidence/"ecs-running-raw-0.json", raws[0])
            (product/"fixture-0.dll").write_bytes(b"changed")
            with self.assertRaises(ValueError): verify(report)
