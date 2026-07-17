#!/usr/bin/env python3
"""Run or stage ASP-DAC competitor baseline simulations.

This script intentionally uses only Python standard-library modules. It is safe
to run before external tools are installed: missing tools produce explicit
`not_run` manifests instead of fabricated measurements.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable


BOOKSIM_TRAFFIC = {
    "uniform": "uniform",
    "transpose": "transpose",
    "hotspot": "hotspot",
    "bit_complement": "bitcomp",
    "burst": "burst",
}


@dataclass(frozen=True)
class ToolLocation:
    name: str
    runner: str
    executable: str | None
    note: str


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")


def run_command(command: list[str], cwd: Path, timeout_s: int = 120) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=str(cwd),
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=timeout_s,
        check=False,
    )


def detect_native(name: str) -> ToolLocation:
    executable = shutil.which(name)
    if executable:
        return ToolLocation(name=name, runner="native", executable=executable, note="found on PATH")
    return ToolLocation(name=name, runner="native", executable=None, note="not found on PATH")


def detect_wsl(name: str, distro: str) -> ToolLocation:
    wsl = shutil.which("wsl")
    if not wsl:
        return ToolLocation(name=name, runner="wsl", executable=None, note="wsl.exe not found")
    command = [wsl, "-d", distro, "--", "bash", "-lc", f"command -v {name} || true"]
    try:
        completed = run_command(command, Path.cwd(), timeout_s=20)
    except Exception as exc:  # pragma: no cover - diagnostic path
        return ToolLocation(name=name, runner="wsl", executable=None, note=f"WSL detection failed: {exc}")
    executable = completed.stdout.strip().splitlines()[0] if completed.stdout.strip() else None
    if executable:
        return ToolLocation(name=name, runner="wsl", executable=executable, note=f"found in {distro}")
    return ToolLocation(name=name, runner="wsl", executable=None, note=f"not found in {distro}")


def choose_tool(name: str, distro: str) -> ToolLocation:
    native = detect_native(name)
    if native.executable:
        return native
    return detect_wsl(name, distro)


def to_wsl_path(path: Path, distro: str) -> str:
    wsl = shutil.which("wsl")
    if not wsl:
        raise RuntimeError("wsl.exe not found")
    completed = run_command(
        [wsl, "-d", distro, "--", "wslpath", "-a", str(path)],
        Path.cwd(),
        timeout_s=20,
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or "wslpath failed")
    return completed.stdout.strip()


def base_manifest(case_id: str, tool: str, raw_config: Path, repo_root: Path) -> dict:
    try:
        git_commit = run_command(["git", "rev-parse", "HEAD"], repo_root, timeout_s=20).stdout.strip()
    except Exception:
        git_commit = "unknown"
    return {
        "schema": "aspdac.result_manifest.v1",
        "case_id": case_id,
        "tool_or_system": tool,
        "evidence_label": "reference_trend",
        "status": "not_run",
        "skip_reason": None,
        "raw_config_path": str(raw_config.relative_to(repo_root)).replace("\\", "/"),
        "resolved_config_path": str(raw_config.relative_to(repo_root)).replace("\\", "/"),
        "config_hash": sha256_file(raw_config),
        "seed": 0,
        "software_version": {
            "git_commit": git_commit,
            "tool_version": "TBD",
            "schema_versions": ["aspdac.result_manifest.v1"],
        },
        "environment": {
            "host": sys.platform,
            "runner_generated_at_utc": datetime.now(timezone.utc).isoformat(),
        },
        "command": "not_run",
        "metrics_file": None,
        "trace_metadata_file": None,
        "canonical_trace_hash": None,
        "repeat_trace_hashes": [],
        "result_limitations": [
            "External baseline evidence only; not a VHaCSim result.",
            "Trend comparison unless all assumptions are normalized.",
        ],
    }


def write_manifest(path: Path, manifest: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")


def booksim_config(traffic_name: str, traffic_value: str, injection_rate: float) -> str:
    return f"""// ASP-DAC VHaCSim reference trend config.
// Generated by experiments/aspdac/scripts/run_competitor_baselines.py.
// This is external BookSim evidence, not a VHaCSim result.

topology = mesh;
k = 4;
n = 2;
routing_function = dim_order;

num_vcs = 1;
vc_buf_size = 16;
wait_for_tail_credit = 0;

traffic = {traffic_value};
injection_rate = {injection_rate:.3f};
packet_size = 1;

sim_type = latency;
sample_period = 1000;
warmup_periods = 3;
max_samples = 10;

// Normalization notes:
// - VHaCSim paper spec uses 128-bit packets over a 128-bit/cycle baseline link.
// - This config treats one BookSim packet as one normalized 128-bit packet.
// - Traffic case label: {traffic_name}.
"""


def run_booksim_case(repo_root: Path, tool: ToolLocation, distro: str, traffic_name: str, traffic_value: str) -> None:
    case_id = f"booksim_4x4_mesh_{traffic_name}_inj001"
    result_dir = repo_root / "experiments" / "aspdac" / "results" / "booksim" / case_id
    raw_config = result_dir / "raw" / "booksim.cfg"
    manifest_path = result_dir / "manifest.json"
    log_path = result_dir / "logs" / "booksim.log"
    write_text(raw_config, booksim_config(traffic_name, traffic_value, injection_rate=0.01))
    manifest = base_manifest(case_id, "BookSim", raw_config, repo_root)

    if not tool.executable:
        manifest["status"] = "not_run"
        manifest["skip_reason"] = "BookSim executable not found in native PATH or WSL."
        write_manifest(manifest_path, manifest)
        return

    if tool.runner == "native":
        command = [tool.executable, str(raw_config)]
        completed = run_command(command, raw_config.parent, timeout_s=180)
    else:
        wsl = shutil.which("wsl")
        raw_config_wsl = to_wsl_path(raw_config, distro)
        workdir_wsl = to_wsl_path(raw_config.parent, distro)
        shell_command = f"cd {shell_quote(workdir_wsl)} && {shell_quote(tool.executable)} {shell_quote(raw_config_wsl)}"
        command = [wsl, "-d", distro, "--", "bash", "-lc", shell_command]
        completed = run_command(command, raw_config.parent, timeout_s=180)

    write_text(log_path, completed.stdout + "\n--- STDERR ---\n" + completed.stderr)
    manifest["command"] = " ".join(command)
    manifest["software_version"]["tool_version"] = tool.note
    manifest["metrics_file"] = str(log_path.relative_to(repo_root)).replace("\\", "/")
    if completed.returncode == 0:
        manifest["status"] = "completed"
        manifest["skip_reason"] = None
    else:
        manifest["status"] = "failed"
        manifest["skip_reason"] = f"BookSim returned exit code {completed.returncode}; see log."
    write_manifest(manifest_path, manifest)


def shell_quote(value: str) -> str:
    return "'" + value.replace("'", "'\\''") + "'"


def stage_timeloop_manifest(repo_root: Path, timeloop: ToolLocation, accelergy: ToolLocation) -> None:
    case_id = "timeloop_accelergy_core_workloads"
    result_dir = repo_root / "experiments" / "aspdac" / "results" / "timeloop_accelergy" / "core_workloads"
    raw_config = repo_root / "experiments" / "aspdac" / "baseline_tools" / "timeloop_accelergy" / "timeloop_accelergy_core_workloads.yaml"
    manifest_path = result_dir / "manifest.json"
    manifest = base_manifest(case_id, "Timeloop_Accelergy", raw_config, repo_root)

    missing: list[str] = []
    if not timeloop.executable:
        missing.append("timeloop-model")
    if not accelergy.executable:
        missing.append("accelergy")
    if missing:
        manifest["status"] = "not_run"
        manifest["skip_reason"] = "Missing executable(s): " + ", ".join(missing)
    else:
        manifest["status"] = "not_run"
        manifest["skip_reason"] = (
            "Executables detected, but native Timeloop/Accelergy architecture/problem/action "
            "decks have not yet been finalized from the normalized ASP-DAC spec."
        )
        manifest["software_version"]["tool_version"] = f"{timeloop.note}; {accelergy.note}"
    write_manifest(manifest_path, manifest)


def write_discovery(repo_root: Path, tools: Iterable[ToolLocation]) -> None:
    payload = {
        "schema": "aspdac.tool_discovery.v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tools": [tool.__dict__ for tool in tools],
    }
    path = repo_root / "experiments" / "aspdac" / "results" / "tool_discovery.json"
    write_manifest(path, payload)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=".", help="Repository root path")
    parser.add_argument("--wsl-distro", default="Ubuntu-24.04", help="WSL distro to inspect")
    args = parser.parse_args()

    repo_root = Path(args.repo_root).resolve()
    distro = args.wsl_distro

    booksim = choose_tool("booksim", distro)
    timeloop = choose_tool("timeloop-model", distro)
    accelergy = choose_tool("accelergy", distro)
    write_discovery(repo_root, [booksim, timeloop, accelergy])

    for traffic_name, traffic_value in BOOKSIM_TRAFFIC.items():
        run_booksim_case(repo_root, booksim, distro, traffic_name, traffic_value)

    stage_timeloop_manifest(repo_root, timeloop, accelergy)
    print("ASP-DAC competitor baseline staging complete.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

