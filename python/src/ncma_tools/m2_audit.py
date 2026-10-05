"""Read-only M2.8 preflight. Evidence generation cannot approve H8 or delete anything."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import os
import platform
import re
import stat
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath

MAX_JSON = 4 * 1024 * 1024
MAX_EVIDENCE = 32 * 1024 * 1024
MAX_SOURCE_FILES = 8192
EXCLUDED = {"bin", "obj", "out", ".git", ".vs", "sdk", "__pycache__"}
LEGACY = {
    "editor": ("EditorApplication", "EditorMain", "EditorAnimation", "EditorFbxCharacter"),
    "host": ("ManagedHost", "ManagedSceneClient", "DotNetGameplayRuntime", "Ncma.Managed.Host", "NativeEntry", "SceneEntry", "NcmaGameplayBridge"),
    "policy": ("ActionAnimationWorkspace", "AgentCapabilityRegistry", "ScriptRuntimeRegistry"),
    "comparison": ("NcmaLegacySceneReference", "NcmaLegacyReferenceCapture"),
}
PENDING = ["H3_H4_H5_real_UI_DPI_and_third_party_MCP", "H7_self_contained_target_environment",
           "H7_production_promotion_and_recovery", "H8_full_old_new_performance_and_long_run",
           "H8_signed_cleanup_inventory", "post_M2_cleanup_and_regression"]


def safe_path(root: Path, relative: str) -> Path:
    parts = PurePosixPath(relative).parts
    if not parts or "\\" in relative or ":" in relative or relative.startswith("/") or any(p in ("", ".", "..") for p in relative.split("/")):
        raise ValueError("Project-relative non-traversing path required.")
    root = root.resolve(strict=True)
    path = root
    for part in parts:
        path = path / part
        if path.exists() or path.is_symlink():
            info = path.lstat()
            if path.is_symlink() or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError("Reparse/link target rejected.")
    if not path.resolve().is_relative_to(root):
        raise ValueError("Path escapes project.")
    return path


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def strict_json(text: str):
    def finite_float(value):
        result = float(value)
        if not math.isfinite(result):
            raise ValueError("Nonfinite JSON number.")
        return result

    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate JSON key.")
            result[key] = value
        return result
    return json.loads(text, object_pairs_hook=pairs, parse_float=finite_float,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite JSON.")))


def read_json(path: Path):
    if not path.is_file() or path.stat().st_size > MAX_JSON:
        raise ValueError("Missing/oversized JSON.")
    return strict_json(path.read_text(encoding="utf-8-sig"))


def read_profile(path: Path) -> dict:
    if not path.is_file() or path.stat().st_size > MAX_JSON:
        raise ValueError("Missing/oversized profile log.")
    rows = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line.startswith("{"):
            continue
        item = strict_json(line)
        if "steps" in item:
            key = f"commands:{item['steps']}:{item['commandsPerSuccessfulStep']}"
        elif item.get("fixture") == "runtime_pressure_one_fixed_step":
            key = f"pressure:{item['objects']}:{item['componentsPerObject']}:{item['bindingsPerObject']}"
        elif "environment" in item and "runtime" in item:
            key = "environment"
        else:
            continue
        if key in rows:
            raise ValueError("Duplicate profile fixture.")
        if key != "environment" and item["samples"] != 32:
            raise ValueError("Expected 32 measured samples.")
        rows[key] = item
    return rows


def runtime_profile_evidence(root: Path, configuration: str) -> tuple[dict, list[dict]]:
    frozen = safe_path(root, f"tests/assets/m2/runtime-historical-reference-{configuration}.json")
    historical = read_json(frozen)
    if historical["schemaVersion"] != 1 or historical["configuration"] != configuration or historical["scope"] != "historical_M1_retained_runtime_fixtures":
        raise ValueError("Historical profile identity mismatch.")
    paths = [frozen]
    baseline = {}
    for value in historical["rows"]:
        if "steps" in value:
            key = f"commands:{value['steps']}:{value['commandsPerSuccessfulStep']}"
        elif value.get("fixture") == "runtime_pressure_one_fixed_step":
            key = f"pressure:{value['objects']}:{value['componentsPerObject']}:{value['bindingsPerObject']}"
        elif "environment" in value and "runtime" in value:
            key = "environment"
        else:
            raise ValueError("Unknown historical fixture.")
        if key in baseline:
            raise ValueError("Duplicate historical fixture.")
        baseline[key] = value
    expected = {f"commands:{steps}:{commands}" for steps in (0, 1, 8) for commands in (0, 64, 1024)}
    expected |= {f"pressure:{a}:{b}:{c}" for a, b, c in ((128, 9, 4), (32, 9, 64), (1024, 1, 1), (4096, 1, 1))}
    if set(baseline) != expected | {"environment"}:
        raise ValueError("Incomplete historical runtime profile.")
    rounds = []
    for number in range(1, 4):
        path = safe_path(root, f"out/verification/m2-8/profiles-{configuration}/runtime-round-{number}.log")
        paths.append(path)
        rows = read_profile(path)
        if set(rows) != expected | {"environment"}:
            raise ValueError("Incomplete current runtime profile.")
        rounds.append(rows)
    comparisons = []
    for key in sorted(expected):
        old = baseline[key]
        median = lambda row: row["medianMs"] if key.startswith("commands:") else row["frameMs"]["median"]
        allocation = lambda row: row["medianFrameAllocationBytes"] if key.startswith("commands:") else row["allocationBytes"]["median"]
        for row in [old] + [r[key] for r in rounds]:
            if median(row) < 0 or allocation(row) < 0:
                raise ValueError("Negative runtime measurement.")
        times = [median(r[key]) for r in rounds]
        allocated = [allocation(r[key]) for r in rounds]
        # Desktop sub-20us timings are treated as noise, not an absolute performance gate.
        time_review = median(old) >= .02 and all(value > median(old) * 1.1 for value in times)
        allocation_review = all(value > allocation(old) * 1.1 for value in allocated)
        comparisons.append({"fixture": key, "historical": old, "current": [r[key] for r in rounds],
                            "three_round_time_review": time_review, "three_round_allocation_review": allocation_review})
    result = {"schemaVersion": 1, "scope": "retained_managed_runtime_fixtures_vs_historical_M1_logs",
              "rounds": 3, "warmupPerRound": 8, "samplesPerRound": 32,
              "simultaneousOldNewEntryComparison": False, "historicalHardwareIdentityVerified": False,
              "historicalFixtureSourceHashRecorded": False, "frameRateAcceptance": False,
              "environmentFieldsMatch": all(r["environment"] == baseline["environment"] for r in rounds),
              "historicalSourceLogs": historical["sourceLogs"],
              "historicalEnvironment": baseline["environment"], "currentEnvironments": [r["environment"] for r in rounds],
              "reviewRequired": any(r["three_round_time_review"] or r["three_round_allocation_review"] for r in comparisons),
              "comparisons": comparisons}
    evidence = [{"path": path.relative_to(root).as_posix(), "sha256": digest(path)} for path in paths]
    return result, evidence


def project_closure(root: Path, entry: str) -> list[str]:
    pending, found = [entry], set()
    while pending:
        relative = pending.pop()
        if relative in found:
            continue
        path = safe_path(root, relative)
        found.add(relative)
        for node in ET.parse(path).getroot().iter():
            if node.tag.rsplit("}", 1)[-1] != "ProjectReference":
                continue
            include = node.attrib["Include"].replace("\\", "/")
            original = Path(os.path.abspath(path.parent / include))
            if not original.is_relative_to(root):
                raise ValueError("External ProjectReference rejected.")
            target = safe_path(root, original.relative_to(root).as_posix()).resolve(strict=True)
            if not target.is_relative_to(root):
                raise ValueError("External ProjectReference rejected.")
            pending.append(target.relative_to(root).as_posix())
    return sorted(found)


def check_project_boundary(projects: list[str], player: bool) -> list[str]:
    forbidden = ("Ncma.Managed.Host",) + (("Ncma.Editor", "Ncma.Gui") if player else ())
    return [p for p in projects if any(Path(p).parent.name.startswith(word) for word in forbidden)]


def source_files(root: Path) -> list[Path]:
    result = []
    for directory in ("managed", "engine/source", "tests", "python", "scripts"):
        base = safe_path(root, directory)
        for folder, dirs, files in os.walk(base, followlinks=False):
            dirs[:] = sorted(d for d in dirs if d not in EXCLUDED)
            for name in dirs:
                safe_path(root, (Path(folder) / name).relative_to(root).as_posix())
            for name in sorted(files):
                path = Path(folder) / name
                if path.suffix in (".cs", ".csproj", ".cpp", ".h", ".py", ".ps1", ".json"):
                    checked = safe_path(root, path.relative_to(root).as_posix())
                    if checked.stat().st_size > MAX_EVIDENCE or len(result) >= MAX_SOURCE_FILES:
                        raise ValueError("Source inventory budget exceeded.")
                    result.append(checked)
    for name in ("CMakeLists.txt", "NcmaEngine.sln", "NcmaEngine.vcxproj", "Build.bat", "LaunchEditor.cmd", "AGENTS.md"):
        result.append(safe_path(root, name))
    return sorted(set(result))


def validate_package(root: Path, directory: str, product: str, configuration: str) -> dict:
    base = safe_path(root, directory)
    manifest_path = safe_path(root, directory + "/deployment-manifest.json")
    data = read_json(manifest_path)
    if data["schemaVersion"] != 1 or data["product"] != product or data["configuration"] != configuration:
        raise ValueError("Package identity/configuration mismatch.")
    seen = set()
    for item in data["files"]:
        relative = item["path"]
        if relative.casefold() in seen:
            raise ValueError("Duplicate package path.")
        seen.add(relative.casefold())
        path = safe_path(root, directory + "/" + relative)
        if not path.is_file() or path.stat().st_size != item["size"] or digest(path) != item["sha256"].lower():
            raise ValueError("Package file/hash mismatch: " + relative)
    actual = set()
    for folder, dirs, names in os.walk(base, followlinks=False):
        for name in dirs + names:
            safe_path(root, (Path(folder) / name).relative_to(root).as_posix())
        actual.update((Path(folder) / name).relative_to(base).as_posix().casefold() for name in names)
    extra = actual - seen - {"deployment-manifest.json"}
    runtime_data = []
    allowed = {"out/user/logs/editor-candidate.jsonl": 1048576,
               "out/user/logs/editor-candidate.jsonl.1": 1048576,
               "out/user/editor/preferences.json": 1048576,
               "sample/out/user/logs/editor-candidate.jsonl": 1048576,
               "sample/out/user/logs/editor-candidate.jsonl.1": 1048576,
               "sample/out/user/editor/preferences.json": 1048576}
    if product.startswith("NcmaEngine"):
        for relative in sorted(extra):
            path = safe_path(root, directory + "/" + relative)
            if relative not in allowed or path.stat().st_size > allowed[relative]:
                raise ValueError("Unlisted package file: " + relative)
            runtime_data.append({"path": relative, "bytes": path.stat().st_size, "sha256": digest(path)})
    elif extra:
        raise ValueError("Unlisted package files.")
    if not seen.issubset(actual):
        raise ValueError("Missing package files.")
    deps = read_json(base / ("NcmaEngine.deps.json" if product.startswith("NcmaEngine") else "NcmaPlayer.deps.json"))
    libraries = sorted(deps["libraries"])
    bad = ("Ncma.Managed.Host/",) + (("Ncma.Editor", "Ncma.Gui/") if product.startswith("NcmaPlayer") else ())
    if any(key.startswith(bad) for key in libraries):
        raise ValueError("Legacy/editor dependency in package.")
    kernels = data.get("resourceKernels", [])
    if not isinstance(kernels, list) or any(not isinstance(k, dict) for k in kernels):
        raise ValueError("Resource-kernel metadata must be an array of records.")
    expected_pose = ("ncma.pose", 1, "plugins/NcmaAnimationKernel.dll")
    if product == "NcmaPlayer-null-candidate" and any("ncmaanimationkernel" in p for p in seen):
        raise ValueError("Pose kernel deployed in Null Player.")
    kernel_ids = [(k["id"], k["abiVersion"], k["path"]) for k in kernels]
    if len(kernel_ids) != len(set(kernel_ids)):
        raise ValueError("Duplicate resource-kernel metadata.")
    if expected_pose in kernel_ids and (expected_pose[2].casefold() not in seen or
            next(k for k in kernels if k["id"] == "ncma.pose").get("lazy") is not True):
        raise ValueError("Pose kernel must be listed and lazily initialized.")
    if product.startswith("NcmaEngine"):
        required_kernels = {("ncma.animation", 2, "plugins/NcmaNative.dll"),
                            ("ncma.character", 2, "plugins/NcmaNative.dll")}
        # Read-only preflight verifies the PREVIOUS installation before checked replacement.
        # A current candidate always requires pose; an installed prior package may lack that
        # additive capability, but cannot carry an undeclared pose DLL. No runtime fallback.
        previous_without_pose = (directory == "out/bin" and product == "NcmaEngine-editor" and
                                 expected_pose[2].casefold() not in seen)
        if not previous_without_pose:
            required_kernels.add(expected_pose)
        if set(kernel_ids) != required_kernels:
            raise ValueError("Resource-kernel ABI metadata mismatch.")
        modules = {m["id"]: (m["abiMajor"], m["abiMinor"]) for m in data["modules"]}
        previous_gui = directory == "out/bin" and product == "NcmaEngine-editor" and modules.get("ncma.gui") == (1, 2)
        if modules != {"ncma.platform": (1, 0), "ncma.renderer": (1, 1), "ncma.gui": (1, 2 if previous_gui else 3), "ncma.physics": (1, 1)}:
            raise ValueError("Editor module ABI metadata mismatch.")
    elif set(kernel_ids) != ({expected_pose} if product == "NcmaPlayer-dx11-candidate" else set()) or any("ncmanative" in p or "ncmagui" in p or "ncmaimportkernel" in p or "ncma.asset.import" in p or p.startswith("tools/import-worker/") for p in seen):
        raise ValueError("Editor resource/GUI deployed in Player.")
    return {"product": product, "directory": directory, "files_verified": len(seen),
            "manifest_sha256": digest(manifest_path), "publish_mode": data["publishMode"],
            "production": data["production"], "manual_acceptance": data["manualAcceptance"],
            "self_contained_verified": data["selfContainedVerified"], "libraries": libraries, "runtime_data": runtime_data}


def kernel_fixture_evidence(root: Path, configuration: str) -> list[dict]:
    directory = f"out/verification/m2-8/kernel-{configuration}"
    report = safe_path(root, directory + "/fixture.json")
    image = safe_path(root, directory + "/reference.rgba")
    metadata = read_json(report)
    expected = {"schemaVersion": 1, "kind": "kernel_only_reference", "width": 256, "height": 256,
                "sharedNumericalShaders": True, "independentAlgorithmOracle": False, "legacyHostUsed": False,
                "validationErrors": 0, "validationWarnings": 0, "liveResources": 0}
    if metadata != expected or image.stat().st_size != 256 * 256 * 4:
        raise ValueError("Kernel reference scope/validation/dimensions mismatch.")
    render = safe_path(root, f"out/verification/m2/render-{configuration}/render-results.json")
    comparison = read_json(render)
    if (comparison.get("kernelReference") is not True or comparison.get("legacyComparison") is not True or
            comparison.get("cycles") != 32 or comparison.get("validation") is not True or
            not 0 <= comparison["maxChannelError"] <= 4 or not 0 <= comparison["meanChannelError"] <= .1 or
            not 0 <= comparison["legacyMaxChannelError"] <= 4 or not 0 <= comparison["legacyMeanChannelError"] <= .1):
        raise ValueError("Missing/failed kernel-managed-legacy comparison.")
    # Preserve exact image and both structured reports, not just paths to mutable test outputs.
    return [{"path": path.relative_to(root).as_posix(), "sha256": digest(path)}
            for path in (report, image, render)]


def audit(root: Path, configuration: str) -> dict:
    root = root.resolve(strict=True)
    if configuration not in ("Debug", "Release"):
        raise ValueError("Unsupported configuration.")
    findings, applications = [], {}
    for name, player in (("Ncma.Editor.App", False), ("Ncma.Player.App", True)):
        projects = project_closure(root, f"managed/{name}/{name}.csproj")
        violations = check_project_boundary(projects, player)
        if violations:
            findings.append({"code": "project_boundary", "application": name, "paths": violations})
        applications[name] = projects
    files = source_files(root)
    inventory = [{"path": p.relative_to(root).as_posix(), "sha256": digest(p), "bytes": p.stat().st_size} for p in files]
    consumers, encoding_warnings = [], []
    for path in files:
        if path.relative_to(root).as_posix() == "python/src/ncma_tools/m2_audit.py":
            continue
        raw = path.read_bytes()
        try:
            text = raw.decode("utf-8-sig")
        except UnicodeDecodeError:
            # Legacy source encoding is not rewritten. Inventory scans ASCII identifier tokens only.
            text = raw.decode("ascii", errors="replace")
            encoding_warnings.append(path.relative_to(root).as_posix())
        for group, names in LEGACY.items():
            matched = [n for n in names if n in text or n in path.name]
            if matched:
                consumers.append({"path": path.relative_to(root).as_posix(), "group": group, "tokens": matched,
                                  "decision": "retain_until_reviewed_post_M2_cleanup"})
    index = read_json(safe_path(root, f"out/verification/m2-7/{configuration}/packages.json"))
    packages = []
    for key, product in (("editor", "NcmaEngine-editor-candidate"), ("playerNull", "NcmaPlayer-null-candidate"),
                         ("playerDx11", "NcmaPlayer-dx11-candidate")):
        selected = Path(index[key]).resolve(strict=True)
        if not selected.is_relative_to(root / "out/package/m2-7" / configuration):
            raise ValueError("Unexpected package index target.")
        packages.append(validate_package(root, selected.relative_to(root).as_posix(), product, configuration))
    commit = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True, timeout=10)
    dirty = subprocess.run(["git", "status", "--porcelain"], cwd=root, capture_output=True, text=True, timeout=10)
    ctest = safe_path(root, f"out/build/windows-ninja-{configuration.lower()}/Testing/Temporary/LastTest.log")
    if ctest.stat().st_size > MAX_EVIDENCE:
        raise ValueError("CTest evidence budget exceeded.")
    evidence = [{"path": ctest.relative_to(root).as_posix(), "sha256": digest(ctest)}]
    text = ctest.read_bytes().decode("utf-8", errors="replace")
    # CTest locale header may be ACP while managed stdout is UTF-8. Preserve/hash raw bytes;
    # extract only the fixed test-owned output path, never reinterpret scene or package JSON.
    for pattern in (r"M2\.8 measurements: ([^\r\n]+)",):
        for match in re.finditer(pattern, text):
            path = Path(match.group(1)).resolve(strict=True)
            if not path.is_relative_to(root / "out/verification"):
                raise ValueError("Unexpected measurement target.")
            relative = path.relative_to(root).as_posix()
            path = safe_path(root, relative)
            measurements = read_json(path)
            if measurements["schemaVersion"] != 1 or measurements["oldBaselineComparison"] or measurements["frameRateAcceptance"]:
                raise ValueError("Measurement scope mismatch.")
            evidence.append({"path": relative, "sha256": digest(path), "measurements": measurements})
    evidence.extend(kernel_fixture_evidence(root, configuration))
    runtime_profiles, runtime_evidence = runtime_profile_evidence(root, configuration)
    evidence.extend(runtime_evidence)
    entry_identity = {"path": "out/bin/NcmaEngine.exe", "kind": "not_installed", "retired_entry_built": False}
    if (root / "out/bin/NcmaEngine.exe").exists():
        default_entry = safe_path(root, "out/bin/NcmaEngine.exe")
        entry_identity.update(sha256=digest(default_entry), kind="previous_installation")
        if (root / "out/bin/deployment-manifest.json").exists():
            installed = read_json(safe_path(root, "out/bin/deployment-manifest.json"))
            validate_package(root, "out/bin", "NcmaEngine-editor", installed["configuration"])
            entry_identity.update(kind="managed_apphost", configuration=installed["configuration"], production=installed["production"])
    source_hash = hashlib.sha256(json.dumps(inventory, sort_keys=True).encode("utf-8")).hexdigest()
    return {"schemaVersion": 1, "kind": "M2.8_read_only_preflight", "configuration": configuration,
            "source_revision": commit.stdout.strip() if commit.returncode == 0 else None,
            "source_dirty": bool(dirty.stdout.strip()) if dirty.returncode == 0 else None,
            "source_content_sha256": source_hash, "environment": {"os": platform.platform(), "python": platform.python_version()},
            "default_entry_identity": entry_identity, "findings": findings, "audit_passed": not findings, "h8_accepted": False,
            "production_promoted": entry_identity.get("kind") == "managed_apphost" and entry_identity.get("production") is True,
            "cleanup_authorized_by_this_report": False,
            "pending_gates": [gate for gate in PENDING if gate != "H7_production_promotion_and_recovery" or entry_identity.get("kind") != "managed_apphost"],
            "applications": applications, "packages": packages,
            "source_inventory": inventory, "legacy_consumers": consumers, "non_utf8_source_paths": encoding_warnings,
            "inventory_scope": "lexical_ASCII_identifiers_not_semantic_dead_code_proof",
            "ctest_raw_bytes_preserved": True, "runtime_profiles": runtime_profiles, "evidence": evidence}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--configuration", choices=("Debug", "Release"), required=True)
    args = parser.parse_args(argv)
    try:
        result = audit(args.root, args.configuration)
        root = args.root.resolve(strict=True)
        relative = f"out/verification/m2-8/{args.configuration}/{uuid.uuid4().hex}"
        directory = safe_path(root, relative)
        directory.mkdir(parents=True, exist_ok=False)
        # Own new evidence only; no overwrite/promotion/delete and no arbitrary --output path.
        payload = json.dumps(result, ensure_ascii=False, indent=2).encode("utf-8")
        if len(payload) > MAX_JSON:
            raise ValueError("Audit output budget exceeded.")
        with (directory / "audit.json").open("xb") as stream:
            stream.write(payload)
        for entry in result["evidence"]:
            source = safe_path(root, entry["path"])
            with (directory / source.name).open("xb") as stream:
                stream.write(source.read_bytes())
        print(json.dumps({"schemaVersion": 1, "audit_passed": result["audit_passed"], "h8_accepted": False,
                          "report": (directory / "audit.json").as_posix(), "pending_gates": result["pending_gates"]}))
        return 0 if result["audit_passed"] else 2
    except (OSError, ValueError, KeyError, ET.ParseError, subprocess.SubprocessError) as error:
        print(json.dumps({"schemaVersion": 1, "audit_passed": False, "h8_accepted": False,
                          "code": "audit_failed", "message": str(error)}))
        return 2


if __name__ == "__main__":
    sys.exit(main())
