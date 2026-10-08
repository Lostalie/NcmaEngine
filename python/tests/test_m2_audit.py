"""Audit safety/negative tests; no production mutations or H8 approval."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from ncma_tools.m2_audit import safe_path, read_json, project_closure, check_project_boundary, validate_package, kernel_fixture_evidence, read_profile, PENDING

ROOT = Path(__file__).resolve().parents[2]


class M2AuditTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / "out/verification/m2-8/unit"
        parent.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=parent)
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def package(self):
        base = self.root / "package"
        base.mkdir()
        (base / "NcmaPlayer.exe").write_bytes(b"test-owned not an actual apphost")
        (base / "NcmaPlayer.deps.json").write_text(json.dumps({"libraries": {"Ncma.Runtime/1.0.0": {}}}), encoding="utf-8")
        files = [{"path": p.name, "size": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
                 for p in base.iterdir()]
        data = {"schemaVersion": 1, "product": "NcmaPlayer-null-candidate", "configuration": "Debug",
                "files": files, "resourceKernels": [], "modules": [], "publishMode": "framework-dependent",
                "production": False, "manualAcceptance": False, "selfContainedVerified": False}
        (base / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
        return base, data

    def validate(self):
        return validate_package(self.root, "package", "NcmaPlayer-null-candidate", "Debug")

    def test_project_relative_targets_only(self):
        for path in ("../other", "/absolute", "C:/absolute", "x\\y", "./dot", "a/../b", "a//b", ""):
            with self.assertRaises(ValueError):
                safe_path(self.root, path)
        self.assertEqual(safe_path(self.root, "out/report.json"), self.root / "out/report.json")

    def test_closed_json_duplicate_nonfinite_and_size_rejection(self):
        path = self.root / "invalid.json"
        for text in ('{"version":1,"version":2}', '{"number":NaN}', '{"number":Infinity}',
                     '{"number":1e999}', '{"number":-1e999}'):
            path.write_text(text, encoding="utf-8")
            with self.assertRaises(ValueError):
                read_json(path)
        path.write_bytes(b" " * (4 * 1024 * 1024 + 1))
        with self.assertRaises(ValueError):
            read_json(path)

    def test_player_and_editor_dependency_boundaries(self):
        host = ["managed/Ncma.Managed.Host/Ncma.Managed.Host.csproj"]
        self.assertEqual(check_project_boundary(host, False), host)
        editor = ["managed/Ncma.Editor.Core/Ncma.Editor.Core.csproj", "managed/Ncma.Gui/Ncma.Gui.csproj"]
        self.assertEqual(check_project_boundary(editor, True), editor)
        self.assertEqual(check_project_boundary(editor, False), [])

    def test_external_reference_rejected_and_shared_closure_deduplicated(self):
        (self.root / "A.csproj").write_text('<Project><ItemGroup><ProjectReference Include="B.csproj"/><ProjectReference Include="B.csproj"/></ItemGroup></Project>', encoding="utf-8")
        (self.root / "B.csproj").write_text("<Project/>", encoding="utf-8")
        self.assertEqual(project_closure(self.root, "A.csproj"), ["A.csproj", "B.csproj"])
        (self.root / "A.csproj").write_text('<Project><ProjectReference Include="../outside.csproj"/></Project>', encoding="utf-8")
        with self.assertRaises(ValueError):
            project_closure(self.root, "A.csproj")

    def test_package_hash_and_unlisted_file_rejection(self):
        base, _ = self.package()
        self.assertEqual(self.validate()["files_verified"], 2)
        (base / "unlisted.dll").write_bytes(b"unknown")
        with self.assertRaises(ValueError):
            self.validate()
        (base / "unlisted.dll").unlink()  # Exact test-owned file, never a project cleanup target.
        (base / "NcmaPlayer.exe").write_bytes(b"tampered")
        with self.assertRaises(ValueError):
            self.validate()

    def test_package_configuration_and_case_collision_rejected(self):
        base, data = self.package()
        data["configuration"] = "Release"
        (base / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaises(ValueError):
            self.validate()
        data["configuration"] = "Debug"
        data["files"].append(data["files"][0] | {"path": data["files"][0]["path"].upper()})
        (base / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaises(ValueError):
            self.validate()

    def test_player_cannot_deploy_editor_resource_kernel(self):
        base, data = self.package()
        data["resourceKernels"] = [{"id": "ncma.animation", "abiVersion": 2, "path": "plugins/NcmaNative.dll"}]
        (base / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaises(ValueError):
            self.validate()

    def test_editor_runtime_log_exception_is_narrow_and_bounded(self):
        base, data = self.package()
        (base / "NcmaPlayer.deps.json").rename(base / "NcmaEngine.deps.json")
        deps = base / "NcmaEngine.deps.json"
        data["files"] = [({"path": deps.name, "size": deps.stat().st_size,
                           "sha256": hashlib.sha256(deps.read_bytes()).hexdigest()}
                          if item["path"] == "NcmaPlayer.deps.json" else item) for item in data["files"]]
        data["product"] = "NcmaEngine-editor-candidate"
        data["resourceKernels"] = [{"id": name, "abiVersion": 2, "path": "plugins/NcmaNative.dll"}
                                  for name in ("ncma.animation", "ncma.character")]
        text = base / "plugins/NcmaText.dll"
        text.parent.mkdir()
        text.write_bytes(b"test-owned UI text module")
        data["files"].append({"path": "plugins/NcmaText.dll", "size": text.stat().st_size,
                              "sha256": hashlib.sha256(text.read_bytes()).hexdigest()})
        data["resourceKernels"].append({"id": "ncma.text", "abiVersion": 1,
                                       "path": "plugins/NcmaText.dll", "lazy": True})
        pose = base / "plugins/NcmaAnimationKernel.dll"
        pose.write_bytes(b"test-owned pose kernel")
        data["files"].append({"path": "plugins/NcmaAnimationKernel.dll", "size": pose.stat().st_size,
                              "sha256": hashlib.sha256(pose.read_bytes()).hexdigest()})
        data["resourceKernels"].append({"id": "ncma.pose", "abiVersion": 1,
                                       "path": "plugins/NcmaAnimationKernel.dll", "lazy": True})
        data["modules"] = [{"id": name, "abiMajor": 1, "abiMinor": minor}
                            for name, minor in (("ncma.platform", 0), ("ncma.renderer", 1), ("ncma.gui", 7), ("ncma.physics", 1))]
        (base / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
        log = base / "out/user/logs/editor-candidate.jsonl"
        log.parent.mkdir(parents=True)
        log.write_bytes(b"test-owned runtime data")
        validate = lambda: validate_package(self.root, "package", data["product"], "Debug")
        self.assertEqual(validate()["runtime_data"][0]["path"], "out/user/logs/editor-candidate.jsonl")
        log.write_bytes(b" " * (1048576 + 1))
        with self.assertRaises(ValueError):
            validate()
        log.write_bytes(b"test-owned runtime data")
        data["resourceKernels"] = data["resourceKernels"][:-1]
        (base / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaises(ValueError):
            validate()  # A new candidate cannot omit pose metadata.
        installed = self.root / "out/bin"
        installed.parent.mkdir(exist_ok=True)
        base.rename(installed)  # Exact test-owned package, not a workspace deployment.
        data["product"] = "NcmaEngine-editor"
        def validate_installed():
            (installed / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
            return validate_package(self.root, "out/bin", data["product"], "Debug")
        with self.assertRaises(ValueError):
            validate_installed()  # A present pose DLL must still be declared.
        (installed / "plugins/NcmaAnimationKernel.dll").unlink()  # Exact test-owned file.
        data["files"] = [f for f in data["files"] if f["path"] != "plugins/NcmaAnimationKernel.dll"]
        self.assertEqual(validate_installed()["product"], "NcmaEngine-editor")

    def test_dx11_pose_kernel_is_lazy_versioned_and_not_in_null_player(self):
        base, data = self.package()
        pose = base / "plugins/NcmaAnimationKernel.dll"
        pose.parent.mkdir()
        pose.write_bytes(b"test-owned pose kernel")
        data["files"].append({"path": "plugins/NcmaAnimationKernel.dll", "size": pose.stat().st_size,
                              "sha256": hashlib.sha256(pose.read_bytes()).hexdigest()})
        data["resourceKernels"] = [{"id": "ncma.pose", "abiVersion": 1,
                                   "path": "plugins/NcmaAnimationKernel.dll", "lazy": True}]
        def validate():
            (base / "deployment-manifest.json").write_text(json.dumps(data), encoding="utf-8")
            return validate_package(self.root, "package", data["product"], "Debug")
        with self.assertRaises(ValueError):
            validate()  # Null cannot deploy a 3D pose contract.
        pose_metadata = data["resourceKernels"]
        data["resourceKernels"] = []
        with self.assertRaises(ValueError):
            validate()  # Omitting metadata cannot hide an unnecessary DLL.
        data["resourceKernels"] = pose_metadata
        data["product"] = "NcmaPlayer-dx11-candidate"
        self.assertEqual(validate()["files_verified"], 3)
        data["resourceKernels"] = pose_metadata[0]
        with self.assertRaises(ValueError):
            validate()  # Reject a singleton record instead of an array.
        data["resourceKernels"] = pose_metadata
        for field, wrong in (("abiVersion", 2), ("lazy", False), ("path", "plugins/other.dll")):
            original = data["resourceKernels"][0][field]
            data["resourceKernels"][0][field] = wrong
            with self.assertRaises(ValueError):
                validate()
            data["resourceKernels"][0][field] = original
        data["resourceKernels"].append(dict(data["resourceKernels"][0]))
        with self.assertRaises(ValueError):
            validate()

    def test_kernel_reference_evidence_rejects_wrong_scope_or_pixel_budget(self):
        directory = self.root / "out/verification/m2-8/kernel-Debug"
        directory.mkdir(parents=True)
        image = directory / "reference.rgba"
        image.write_bytes(bytes(256 * 256 * 4))
        report = {"schemaVersion": 1, "kind": "kernel_only_reference", "width": 256, "height": 256,
                  "sharedNumericalShaders": True, "independentAlgorithmOracle": False, "legacyHostUsed": False,
                  "validationErrors": 0, "validationWarnings": 0, "liveResources": 0}
        path = directory / "fixture.json"
        path.write_text(json.dumps(report), encoding="utf-8")
        render = self.root / "out/verification/m2/render-Debug/render-results.json"
        render.parent.mkdir(parents=True)
        comparison = {"kernelReference": True, "legacyComparison": True, "cycles": 32, "validation": True,
                      "maxChannelError": 0, "meanChannelError": 0, "legacyMaxChannelError": 0, "legacyMeanChannelError": 0}
        render.write_text(json.dumps(comparison), encoding="utf-8")
        self.assertEqual(len(kernel_fixture_evidence(self.root, "Debug")), 3)
        for key, value in (("legacyHostUsed", True), ("independentAlgorithmOracle", True), ("validationWarnings", 1)):
            path.write_text(json.dumps(report | {key: value}), encoding="utf-8")
            with self.assertRaises(ValueError):
                kernel_fixture_evidence(self.root, "Debug")
        path.write_text(json.dumps(report), encoding="utf-8")
        render.write_text(json.dumps(comparison | {"legacyMaxChannelError": 5}), encoding="utf-8")
        with self.assertRaises(ValueError):
            kernel_fixture_evidence(self.root, "Debug")
        render.write_text(json.dumps(comparison), encoding="utf-8")
        image.write_bytes(b"truncated")
        with self.assertRaises(ValueError):
            kernel_fixture_evidence(self.root, "Debug")

    def test_profile_parser_rejects_duplicates_nonfinite_and_wrong_samples(self):
        path = self.root / "profile.log"
        row = {"steps": 1, "commandsPerSuccessfulStep": 64, "samples": 32, "medianMs": 1}
        path.write_text("PASS fixture\n" + json.dumps(row) + "\n", encoding="utf-8-sig")
        self.assertEqual(set(read_profile(path)), {"commands:1:64"})
        for content in (json.dumps(row) + "\n" + json.dumps(row),
                        '{"steps":1,"steps":2,"commandsPerSuccessfulStep":64,"samples":32}',
                        '{"steps":1,"commandsPerSuccessfulStep":64,"samples":32,"medianMs":NaN}',
                        '{"steps":1,"commandsPerSuccessfulStep":64,"samples":32,"medianMs":1e999}',
                        json.dumps(row | {"samples": 31})):
            path.write_text(content, encoding="utf-8")
            with self.assertRaises(ValueError):
                read_profile(path)

    def test_audit_cannot_approve_manual_gates(self):
        self.assertIn("H7_self_contained_target_environment", PENDING)
        self.assertIn("post_M2_cleanup_and_regression", PENDING)


if __name__ == "__main__":
    unittest.main()
