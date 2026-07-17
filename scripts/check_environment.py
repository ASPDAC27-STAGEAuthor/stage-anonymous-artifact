#!/usr/bin/env python3
"""Check the portable default artifact environment without recording identity."""

from __future__ import annotations

import importlib.metadata
import json
import platform
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output"
REQUIRED_PACKAGES = ["numpy", "matplotlib", "PyYAML", "jsonschema", "Pillow"]
OPTIONAL_TOOLS = ["booksim", "timeloop-model", "timeloop-mapper", "accelergy", "scalesim"]


def command_output(*args: str) -> str | None:
    try:
        return subprocess.run(args, check=True, capture_output=True, text=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def main() -> int:
    dotnet_version = command_output("dotnet", "--version")
    python_ok = sys.version_info >= (3, 10)
    dotnet_ok = bool(dotnet_version and int(dotnet_version.split(".", 1)[0]) >= 8)
    packages: dict[str, str | None] = {}
    for name in REQUIRED_PACKAGES:
        try:
            packages[name] = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            packages[name] = None

    usage = shutil.disk_usage(ROOT)
    report = {
        "status": "pass" if python_ok and dotnet_ok and all(packages.values()) else "fail",
        "identity_fields_omitted": True,
        "platform": platform.system(),
        "machine": platform.machine(),
        "python": platform.python_version(),
        "python_ok": python_ok,
        "dotnet_sdk": dotnet_version,
        "dotnet_ok": dotnet_ok,
        "packages": packages,
        "optional_external_tools": {name: bool(shutil.which(name)) for name in OPTIONAL_TOOLS},
        "free_disk_gib": round(usage.free / (1024**3), 2),
        "default_path_requires_external_tools": False,
        "full_mnist_gpu_replay_in_default_path": False,
    }
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "environment.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
