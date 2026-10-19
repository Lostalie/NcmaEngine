from __future__ import annotations

import copy
import json
from pathlib import Path
import tempfile
import unittest

from ncma_tools.ecs_e5_evidence import DRIVER, FILES, PRODUCTS, digest, loads, paired_summary, validate_run, verify


CONTRACT = json.loads((Path(__file__).resolve().parents[2] / "scripts/ecs-e5-contract.json").read_text(encoding="utf-8"))


class EcsE5EvidenceTests(unittest.TestCase):
    def fixture(self, cohort="common", measured=True):
        files = {"test.dll": "F" * 64}
        rows = [{"kind": "ecs_e5_paired_environment", "schema": 2, "variant": "candidate", "cohort": cohort,
                 "measured": measured, "contractSha256": "A" * 64, "tieredCompilation": "0", "files": files}]
        if measured:
            rows.append({"kind": "ecs_e5_environment", "configuration": "Release", "stopwatchFrequency": 10000000,
                         "cpuCount": 8, "serverGc": False, "runtime": "test", "os": "test", "architecture": "X64",
                         "assembly": "test", "worldAssembly": "test", "utc": "2026-10-19T00:00:00Z"})
        for workload in CONTRACT["cohorts"][cohort]:
            for count in CONTRACT["counts"] if measured else [8]:
                rows.append({"kind": "ecs_e5_semantic", "workload": workload["name"], "objects": count,
                             "tick": 112, "sceneBytes": 100, "sha256": "B" * 64})
                if measured:
                    rows.append({"kind": "ecs_e5_sample", "schema": 1, "workload": workload["name"], "objects": count,
                                 "warmup": 12, "samples": 100, "operationsPerSample": workload["operations"],
                                 "ms": dict.fromkeys(("p50", "p95", "p99", "max"), 1),
                                 "allocatedThreadBytes": dict.fromkeys(("p50", "p95", "p99", "max"), 0),
                                 "rawMs": [1] * 100, "rawAllocatedThreadBytes": [0] * 100, "gcCollections": [0, 0, 0],
                                 "managedBeforeBytes": 1, "managedAfterBytes": 1, "processWorkingSetBytes": 1,
                                 "processLifetimePeakWorkingSetBytes": 1, "sceneBytes": 100, "layerMs": {}})
        return rows, files

    def check_rows(self, rows, files, cohort="common", measured=True):
        text = "\n".join(json.dumps(row) for row in rows) + "\nE5 paired closure verified before and after workloads.\n"
        return validate_run(text, CONTRACT, cohort, "candidate", measured, "A" * 64, files)

    def test_complete_common_and_read_matrices(self):
        for cohort, expected in (("common", 33), ("reads", 27)):
            rows, files = self.fixture(cohort)
            semantic, samples, _ = self.check_rows(rows, files, cohort)
            self.assertEqual(len(semantic), expected)
            self.assertEqual(len(samples), expected)

    def test_smoke_is_not_measurement(self):
        rows, files = self.fixture(measured=False)
        self.assertEqual(len(self.check_rows(rows, files, measured=False)[1]), 0)
        with self.assertRaises(ValueError):
            self.check_rows(rows, files)

    def test_missing_duplicate_unknown_rows(self):
        original, files = self.fixture()
        for action in (lambda r: r.pop(), lambda r: r.append(copy.deepcopy(r[-1])),
                       lambda r: r[-1].update(workload="substituted_query"), lambda r: r[-1].update(objects=999),
                       lambda r: r.append(copy.deepcopy(r[0])), lambda r: r[0].update(kind="ecs_e3_paired_environment")):
            rows = copy.deepcopy(original); action(rows)
            with self.assertRaises(ValueError):
                self.check_rows(rows, files)

    def test_raw_summary_and_sampling_tampering(self):
        original, files = self.fixture()
        mutations = [lambda r: r.update(warmup=11), lambda r: r.update(samples=99),
                     lambda r: r.update(operationsPerSample=16), lambda r: r["rawMs"].pop(),
                     lambda r: r["rawMs"].__setitem__(0, -1), lambda r: r["rawAllocatedThreadBytes"].__setitem__(0, -1),
                     lambda r: r["ms"].update(p95=2), lambda r: r["gcCollections"].append(0),
                     lambda r: r.pop("layerMs"), lambda r: r.update(managedAfterBytes=-1)]
        for mutate in mutations:
            rows = copy.deepcopy(original); mutate(rows[-1])
            with self.assertRaises((ValueError, KeyError)):
                self.check_rows(rows, files)

    def test_contract_variant_and_configuration(self):
        original, files = self.fixture()
        for field, value in (("variant", "e2b"), ("cohort", "reads"), ("contractSha256", "C" * 64),
                             ("tieredCompilation", "1"), ("files", {}), ("schema", 1)):
            rows = copy.deepcopy(original); rows[0][field] = value
            with self.assertRaises(ValueError):
                self.check_rows(rows, files)
        rows = copy.deepcopy(original); rows[1]["configuration"] = "Debug"
        with self.assertRaises(ValueError):
            self.check_rows(rows, files)

    def test_strict_json(self):
        for invalid in ('{"x":1,"x":2}', '{"x":NaN}', '{"x":Infinity}'):
            with self.assertRaises(ValueError):
                loads(invalid)

    def test_process_interval_not_best_run(self):
        self.assertEqual(paired_summary([.75, .75, .75], .8)["status"], "met")
        self.assertEqual(paired_summary([1.2, 1.2, 1.2], 1)["status"], "observed_regression")
        self.assertEqual(paired_summary([.7, .9, 1.1], 1)["status"], "unproven")
        with self.assertRaises(ValueError):
            paired_summary([.75, .75], .8)

    def archive_fixture(self, root):
        def save(path, value):
            path.write_text(json.dumps(value), encoding="utf-8")
        contract = copy.deepcopy(CONTRACT)
        packages = []
        records = []
        for variant in ("e2b", "candidate"):
            directory = root / variant; directory.mkdir()
            for name in sorted(FILES):
                (directory / name).write_text(name, encoding="utf-8")
                records.append(dict(variant=variant, name=name, archive=f"{variant}/{name}", source="unused",
                                    sha256=digest(directory / name), size=(directory / name).stat().st_size))
            package_name = f"{variant}-package.json"
            save(root / package_name, dict(configuration="Release", tfm="net8.0", product="NcmaEngine-editor-candidate",
                 files=[dict(path=f["name"], sha256=f["sha256"], size=f["size"]) for f in records if f["variant"] == variant and f["name"] in PRODUCTS]))
            packages.append(dict(variant=variant, archive=package_name, sha256=digest(root / package_name)))
        contract["references"]["e2b"]["manifestSha256"] = packages[0]["sha256"]
        save(root / "contract.json", contract)
        contract_hash = digest(root / "contract.json")
        for name in ("source-freeze.json", "measure.ps1", "verifier.py"):
            (root / name).write_text("fixture", encoding="utf-8")
        logs = []
        for variant in ("e2b", "candidate"):
            files = {f["name"]: f["sha256"] for f in records if f["variant"] == variant}
            save(root / variant / "paired-inputs.json", dict(schema=2, variant=variant, cohort="common", contractSha256=contract_hash,
                 files=[dict(name=f["name"], sha256=f["sha256"], size=f["size"]) for f in records if f["variant"] == variant]))
            rows, _ = self.fixture(measured=False)
            rows[0].update(variant=variant, files=files, contractSha256=contract_hash)
            log_name = f"{variant}-1.log"
            (root / log_name).write_text("\n".join(json.dumps(r) for r in rows) + "\nE5 paired closure verified before and after workloads.\n", encoding="utf-8")
            env_name = f"{variant}-1-environment.json"
            save(root / env_name, dict(utc="test", cpu=[dict(name="test")], processes=[], powerScheme="test"))
            logs.append(dict(variant=variant, run=1, path=log_name, sha256=digest(root / log_name), exitCode=0,
                             environment=env_name, environmentSha256=digest(root / env_name)))
        inputs = dict(schema=1, generation="fixture", measured=False, reference="e2b", cohort="common",
                      contractSha256=contract_hash, sourceFreezeSha256=digest(root / "source-freeze.json"),
                      scriptSha256=digest(root / "measure.ps1"), verifierSha256=digest(root / "verifier.py"),
                      packages=packages, files=records)
        save(root / "inputs.json", inputs)
        execution = dict(schema=1, generation="fixture", measured=False, inputsSha256=digest(root / "inputs.json"),
                         runsCompleted=1, logs=logs, fullPerformanceAccepted=False, deployed=False)
        save(root / "execution.json", execution)
        return inputs, execution

    def test_archive_chain_and_negative_closure(self):
        cases = ("valid", "missing", "bytes", "extra", "directory", "input_duplicate", "input_missing", "escape",
                 "different_driver", "contract", "freeze", "script", "verifier", "log", "environment", "manifest",
                 "manifest_duplicate", "log_duplicate", "log_missing", "log_order", "semantic", "acceptance")
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory(prefix="ncma-e5-evidence-") as temporary:
                root = Path(temporary); inputs, execution = self.archive_fixture(root)
                if case == "valid":
                    self.assertTrue(verify(root)["evidenceVerified"])
                    continue
                if case == "missing": (root / "candidate/Ncma.Runtime.dll").unlink()
                if case == "bytes": (root / "candidate/Ncma.Runtime.dll").write_text("changed")
                if case == "extra": (root / "candidate/extra").write_text("extra")
                if case == "directory": (root / "candidate/extra").mkdir()
                if case == "input_duplicate": inputs["files"].append(copy.deepcopy(inputs["files"][0]))
                if case == "input_missing": inputs["files"].pop()
                if case == "escape": inputs["files"][0]["archive"] = "../outside"
                if case == "different_driver":
                    name = sorted(DRIVER)[0]; path = root / "candidate" / name; path.write_text("different")
                    record = next(f for f in inputs["files"] if f["variant"] == "candidate" and f["name"] == name)
                    record.update(sha256=digest(path), size=path.stat().st_size)
                paths = {"contract": "contract.json", "freeze": "source-freeze.json", "script": "measure.ps1", "verifier": "verifier.py",
                         "log": "candidate-1.log", "environment": "candidate-1-environment.json", "manifest": "candidate-package.json"}
                if case in paths: (root / paths[case]).write_text("altered")
                if case == "manifest_duplicate":
                    path = root / "candidate/paired-inputs.json"; data = json.loads(path.read_text()); data["files"].append(data["files"][0]); path.write_text(json.dumps(data))
                if case == "log_duplicate": execution["logs"].append(copy.deepcopy(execution["logs"][0]))
                if case == "log_missing": execution["logs"].pop()
                if case == "log_order": execution["logs"].reverse()
                if case == "semantic":
                    path = root / "candidate-1.log"; path.write_text(path.read_text().replace('"tick": 112', '"tick": 113'))
                    execution["logs"][1]["sha256"] = digest(path)
                if case == "acceptance": execution["fullPerformanceAccepted"] = True
                (root / "inputs.json").write_text(json.dumps(inputs), encoding="utf-8")
                execution["inputsSha256"] = digest(root / "inputs.json")
                (root / "execution.json").write_text(json.dumps(execution), encoding="utf-8")
                with self.assertRaises((ValueError, OSError, KeyError)):
                    verify(root)


if __name__ == "__main__":
    unittest.main()
