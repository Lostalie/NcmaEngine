"""Fixed offline preparation recipe, not a worker, test runner, or installer grant.

Executed ONLY from the physically retained input copy by the trusted PS preparer.
No user-selectable pip arguments, downloads, source builds, or global installation.
"""
import hashlib
import importlib.metadata
import importlib.util
import json
import os
from pathlib import Path
import re
import runpy
import sys
from urllib.parse import urlsplit
from urllib.request import url2pathname
import venv


def require(value, code):
    if not value:
        raise RuntimeError(code)


def same(left, right):
    return os.path.normcase(os.path.abspath(left)) == os.path.normcase(os.path.abspath(right))


def locked_wheels(inputs):
    result = {}
    text = (inputs / "worker/requirements-win-py312.lock").read_text(encoding="utf-8")
    require(len(text) <= 16384 and "\0" not in text, "python_lock_budget")
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        match = re.fullmatch(r"(grpcio|typing-extensions)==([0-9]+\.[0-9]+\.[0-9]+) --hash=sha256:([0-9a-f]{64})", line)
        require(match is not None, "python_lock_entry")
        name, version, digest = match.groups()
        require(name not in result, "python_lock_duplicate")
        filename = (f"grpcio-{version}-cp312-cp312-win_amd64.whl" if name == "grpcio"
                    else f"typing_extensions-{version}-py3-none-any.whl")
        wheel = inputs / "wheels" / filename
        require(wheel.stat().st_size <= 67108864, "python_wheel_budget")
        with wheel.open("rb") as stream:
            require(hashlib.file_digest(stream, "sha256").hexdigest() == digest, "python_wheel_hash")
        result[name] = (version, digest, wheel)
    require(set(result) == {"grpcio", "typing-extensions"}, "python_lock_closure")
    return result


def main():
    require(len(sys.argv) == 4 and sys.argv[1] in ("create", "install"), "python_prepare_arguments")
    mode = sys.argv[1]
    inputs, output = map(Path, sys.argv[2:])
    require(inputs.is_absolute() and output.is_absolute(), "python_prepare_absolute")
    require(re.fullmatch(r"[0-9a-f]{32}", output.name) is not None
            and output.parent.name == "python-environments", "python_prepare_output_scope")
    require(inputs.name == "inputs" and inputs.parent.parent.name == "python-inputs"
            and re.fullmatch(r"[0-9a-f]{32}", inputs.parent.name) is not None
            and same(inputs.parent.parent.parent, output.parent.parent), "python_prepare_input_scope")
    require(same(__file__, inputs / "recipe/PythonWorkerEnvironment.py"), "python_prepare_copied_recipe")
    require(sys.version_info[:2] == (3, 12) and sys.maxsize > 2**32
            and sys.flags.isolated == 1 and sys.dont_write_bytecode, "python_prepare_runtime_flags")
    runtime, environment = inputs / "runtime", output / "venv"
    require(same(sys.base_prefix, runtime), "python_prepare_base_runtime")
    require(output.is_dir(), "python_prepare_output_missing")
    wheels = locked_wheels(inputs)
    if mode == "create":
        require(same(sys.prefix, runtime) and same(sys.executable, runtime / "python.exe"), "python_prepare_base_executable")
        require(not environment.exists(), "python_prepare_environment_exists")
        # No ensurepip subprocess, upgrade, global site-packages, or symlinks.
        venv.EnvBuilder(system_site_packages=False, clear=False, symlinks=False,
                        upgrade=False, with_pip=False, upgrade_deps=False).create(environment)
        return

    require(same(sys.prefix, environment)
            and same(sys.executable, environment / "Scripts/python.exe"), "python_prepare_venv_executable")
    site = environment / "Lib/site-packages"
    require(site.is_dir() and not any(site.iterdir()), "python_prepare_environment_not_empty")
    pip_wheels = list((runtime / "Lib/ensurepip/_bundled").glob("pip-*-py3-none-any.whl"))
    require(len(pip_wheels) == 1, "python_prepare_pip_closure")
    report = output / "pip-report.json"
    observation = output / "observation.json"
    require(not report.exists() and not observation.exists(), "python_prepare_receipt_exists")
    # --isolated alone does NOT disable global/site pip.ini. os.devnull does.
    for key in list(os.environ):
        if key.upper().startswith(("PIP_", "PYTHON")):
            del os.environ[key]
    os.environ["PIP_CONFIG_FILE"] = os.devnull
    sys.path.insert(0, str(pip_wheels[0]))
    sys.argv = ["pip", "--isolated", "--disable-pip-version-check", "--no-input", "--require-virtualenv",
                "install", "--no-index", "--find-links", str(inputs / "wheels"), "--require-hashes",
                "--only-binary=:all:", "--no-deps", "--no-cache-dir", "--no-compile",
                "--report", str(report), "-r", str(inputs / "worker/requirements-win-py312.lock")]
    try:
        runpy.run_module("pip", run_name="__main__", alter_sys=True)
    except SystemExit as error:
        require(error.code in (None, 0), "python_prepare_pip_failed")

    require(report.is_file() and report.stat().st_size <= 1048576, "python_prepare_report_budget")
    document = json.loads(report.read_text(encoding="utf-8"))
    require(document.get("version") == "1" and len(document.get("install", [])) == 2, "python_prepare_report_closure")
    reported = set()
    for entry in document["install"]:
        name = entry["metadata"]["name"].lower().replace("_", "-")
        require(name in wheels and name not in reported, "python_prepare_report_package")
        version, digest, wheel = wheels[name]
        info = entry["download_info"]
        uri = urlsplit(info["url"])
        require(uri.scheme == "file" and uri.netloc == "" and not uri.query and not uri.fragment
                and same(url2pathname(uri.path), wheel), "python_prepare_report_source")
        require(entry["metadata"]["version"] == version
                and info["archive_info"]["hashes"]["sha256"] == digest, "python_prepare_report_hash")
        reported.add(name)
    distributions = list(importlib.metadata.distributions(path=[str(site)]))
    installed = {d.metadata["Name"].lower().replace("_", "-"): d.version for d in distributions}
    require(len(distributions) == 2 and installed == {k: v[0] for k, v in wheels.items()}, "python_prepare_installed_closure")
    # Inspect resolution only: do not import grpc native code or start any worker.
    for module in ("grpc", "typing_extensions"):
        spec = importlib.util.find_spec(module)
        require(spec is not None and spec.origin is not None
                and Path(spec.origin).is_relative_to(site), "python_prepare_module_origin")
    with observation.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump({"kind": "ncma.python-worker.environment-observation.v1", "inputs": str(inputs),
                   "environment": str(environment), "baseRuntime": str(runtime), "installed": installed,
                   "isolated": True, "bytecodeDisabled": True, "pipConfigurationDisabled": True,
                   "pipInstalled": False, "workerExecuted": False, "testsExecuted": False}, stream, indent=2)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # No arbitrary exception text/paths/configuration in the status line.
        code = str(error) if type(error) is RuntimeError and re.fullmatch(r"python_[a-z_]+", str(error)) else "python_prepare_failed"
        print(f"{code}:{type(error).__name__}", file=sys.stderr)
        sys.exit(1)
