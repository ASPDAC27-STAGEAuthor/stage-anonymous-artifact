#!/usr/bin/env python3
"""Run only the 16 frozen reviewer P1 SCALE-Sim hold-out candidates.

The adapter consumes the candidate's high-level M/N/K contract, materializes a
fresh 4x4 weight-stationary SCALE-Sim config/topology, and writes only the
candidate's external raw directory.  It never reads a STAGE result.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import importlib.util
import json
import os
import shlex
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


EXPECTED_TOOL_COMMIT = "9f98c4371055a54c75209c2e02b640b897550532"
ALLOWED_PARAMETERS = {"case_id", "M", "N", "K", "precision_bits", "repeat", "seed"}
NATIVE_REPORT_NAMES = ("COMPUTE_REPORT.csv", "DETAILED_ACCESS_REPORT.csv", "BANDWIDTH_REPORT.csv", "TIME_REPORT.csv")
FROZEN_CASES = {
    "holdout_gemm_096": (96, 96, 96),
    "holdout_gemm_192": (192, 192, 192),
    "holdout_gemm_384": (384, 384, 384),
    "holdout_rect_128x256x64": (128, 256, 64),
    "holdout_rect_256x64x192": (256, 64, 192),
    "holdout_rect_64x384x128": (64, 384, 128),
    "holdout_attn_qk_s096_d064": (96, 96, 64),
    "holdout_attn_qk_s192_d064": (192, 192, 64),
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_hash(value: Any) -> str:
    return sha256_bytes(json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("utf-8"))


def atomic_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + f".{os.getpid()}.tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def read_csv_first(path: Path) -> dict[str, str]:
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        row = next(csv.DictReader(stream))
    return {str(key).strip(): str(value).strip() for key, value in row.items() if key is not None and value is not None}


def wsl_unc_path(distro: str, linux_path: str) -> Path:
    if not linux_path.startswith("/") or ".." in Path(linux_path).parts:
        raise ValueError(f"Expected an absolute normalized WSL path: {linux_path}")
    tail = linux_path.lstrip("/").replace("/", "\\")
    return Path(f"\\\\wsl.localhost\\{distro}\\{tail}")


def wsl_command(distro: str, argv: list[str], cwd: str | None = None) -> list[str]:
    command = ["wsl.exe", "-d", distro]
    if cwd:
        command.extend(["--cd", cwd])
    command.extend(["-e", *argv])
    return command


def capture(distro: str, argv: list[str], cwd: str | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        wsl_command(distro, argv, cwd),
        text=True,
        encoding="utf-8",
        errors="replace",
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def load_p1_module(repo_root: Path) -> Any:
    path = repo_root / "experiments" / "aspdac" / "scripts" / "reviewer_extension_p1.py"
    spec = importlib.util.spec_from_file_location("reviewer_extension_p1", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load P1 orchestrator: {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_candidate(candidate: dict[str, Any], input_payload: dict[str, Any]) -> dict[str, Any]:
    if candidate.get("kind") != "holdout" or candidate.get("provider") != "scalesim":
        raise ValueError(f"Not a SCALE-Sim hold-out candidate: {candidate.get('candidate_id')}")
    if input_payload.get("scenario") != "reviewer_holdout_scalesim":
        raise ValueError(f"Unexpected scenario for {candidate['candidate_id']}")
    if input_payload.get("candidate_id") != candidate["candidate_id"] or input_payload.get("config_hash") != candidate["config_hash"]:
        raise ValueError(f"Candidate identity mismatch for {candidate['candidate_id']}")
    resolved = input_payload.get("resolved")
    if not isinstance(resolved, dict) or set(resolved) != {"parameters"} or not isinstance(resolved["parameters"], dict):
        raise ValueError(f"Resolved input must contain only parameters for {candidate['candidate_id']}")
    parameters = dict(resolved["parameters"])
    if set(parameters) != ALLOWED_PARAMETERS:
        raise ValueError(f"Parameter allowlist violation for {candidate['candidate_id']}: {sorted(set(parameters) - ALLOWED_PARAMETERS)}")
    case_id = str(parameters["case_id"])
    shape = (int(parameters["M"]), int(parameters["N"]), int(parameters["K"]))
    if FROZEN_CASES.get(case_id) != shape:
        raise ValueError(f"Frozen shape mismatch for {case_id}: {shape}")
    if int(parameters["precision_bits"]) != 16 or int(parameters["seed"]) != 40 or int(parameters["repeat"]) not in (0, 1):
        raise ValueError(f"Frozen precision/seed/repeat mismatch for {case_id}")
    return parameters


def resolved_config(template: str, run_name: str) -> str:
    original = "run_name = aspdac_4x4_ws_core_mnk"
    if template.count(original) != 1:
        raise ValueError("Frozen config must contain exactly one canonical run_name")
    result = template.replace(original, f"run_name = {run_name}")
    required = (
        "ArrayHeight:    4",
        "ArrayWidth:     4",
        "Bandwidth : 8,8,8",
        "Dataflow : ws",
        "MemoryBanks:   1",
        "SparsitySupport : false",
        "RandomNumberGeneratorSeed : 40",
        "InterfaceBandwidth: USER",
    )
    missing = [line for line in required if line not in result]
    if missing:
        raise ValueError(f"Frozen 4x4 WS contract is incomplete: {missing}")
    return result


def write_topology(path: Path, case_id: str, m: int, n: int, k: int) -> None:
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(["Layer Name", " M", " N", " K", ""])
        writer.writerow([case_id, m, n, k, ""])


def inventory(root: Path, excluded: set[Path] | None = None) -> list[dict[str, Any]]:
    excluded = {path.resolve() for path in (excluded or set())}
    rows: list[dict[str, Any]] = []
    for path in sorted(root.rglob("*")):
        if path.is_file() and path.resolve() not in excluded:
            rows.append({"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": sha256_file(path)})
    return rows


def tool_provenance(distro: str, tool_repo: str, tool_python: str) -> dict[str, Any]:
    commit = capture(distro, ["git", "-C", tool_repo, "rev-parse", "HEAD"])
    status = capture(distro, ["git", "-C", tool_repo, "status", "--short"])
    diff = capture(distro, ["git", "-C", tool_repo, "diff", "--", "scalesim/memory/double_buffered_scratchpad_mem.py", "scalesim/memory/read_buffer.py"])
    runtime = capture(distro, [tool_python, "-c", "import scalesim,numpy,yaml,sys; print(sys.version.replace(chr(10),' ')); print(scalesim.__file__); print(numpy.__version__); print(yaml.__version__)"], tool_repo)
    if commit.returncode != 0 or runtime.returncode != 0:
        raise RuntimeError("SCALE-Sim provenance probe failed: " + commit.stderr + runtime.stderr)
    lines = runtime.stdout.splitlines()
    return {
        "tool": "SCALE-Sim",
        "git_commit": commit.stdout.strip(),
        "expected_git_commit": EXPECTED_TOOL_COMMIT,
        "git_commit_matches_expected": commit.stdout.strip() == EXPECTED_TOOL_COMMIT,
        "git_status_short": status.stdout.splitlines(),
        "known_local_compatibility_diff_sha256": sha256_bytes(diff.stdout.encode("utf-8")),
        "python": tool_python,
        "python_version": lines[0] if lines else "unavailable",
        "module_path": lines[1] if len(lines) > 1 else "unavailable",
        "numpy_version": lines[2] if len(lines) > 2 else "unavailable",
        "pyyaml_version": lines[3] if len(lines) > 3 else "unavailable",
        "wsl_distribution": distro,
        "tool_repo": tool_repo,
    }


def mark_running(p1: Any, run_root: Path, candidate_id: str) -> None:
    checkpoint_path = run_root / "manifests" / "p1_checkpoint.json"
    with p1.checkpoint_lock(run_root):
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
        state = checkpoint["candidates"][candidate_id]
        state.update({"status": "running", "worker": "reviewer-p1-scalesim", "attempts": int(state.get("attempts", 0)) + 1,
                      "updated_utc": utc_now(), "reason": ""})
        checkpoint["updated_utc"] = utc_now()
        p1.atomic_json(checkpoint_path, checkpoint)


def run_candidate(
    candidate: dict[str, Any],
    parameters: dict[str, Any],
    run_root: Path,
    template: str,
    template_path: Path,
    provenance: dict[str, Any],
    distro: str,
    tool_repo: str,
    tool_python: str,
    timeout_seconds: int,
) -> tuple[str, Path, str]:
    raw_result = run_root / candidate["raw_relpath"]
    candidate_root = raw_result.parent
    inputs = candidate_root / "inputs"
    outputs = candidate_root / "scale_outputs"
    inputs.mkdir(parents=True, exist_ok=True)
    outputs.mkdir(parents=True, exist_ok=True)
    case_id = str(parameters["case_id"])
    repeat = int(parameters["repeat"])
    run_name = f"aspdac_reviewer_p1_{case_id}_r{repeat}"
    report_root = outputs / run_name
    for report_name in NATIVE_REPORT_NAMES:
        (report_root / report_name).unlink(missing_ok=True)
    config_path = inputs / "aspdac_4x4_ws.cfg"
    topology_path = inputs / f"{case_id}.csv"
    config_path.write_text(resolved_config(template, run_name), encoding="utf-8")
    write_topology(topology_path, case_id, int(parameters["M"]), int(parameters["N"]), int(parameters["K"]))
    attempt_token = f"{os.getpid()}-{time.time_ns()}"
    wsl_candidate_root = f"/opt/stage-baselines/reviewer_extension_runs/20260717/{candidate['candidate_id']}/{attempt_token}"
    wsl_inputs = f"{wsl_candidate_root}/inputs"
    wsl_outputs = f"{wsl_candidate_root}/scale_outputs"
    prepared = capture(distro, ["mkdir", "-p", wsl_inputs, wsl_outputs])
    if prepared.returncode != 0:
        raise RuntimeError(f"Cannot prepare WSL candidate directory: {prepared.stderr.strip()}")
    wsl_config = f"{wsl_inputs}/aspdac_4x4_ws.cfg"
    wsl_topology = f"{wsl_inputs}/{case_id}.csv"
    shutil.copy2(config_path, wsl_unc_path(distro, wsl_config))
    shutil.copy2(topology_path, wsl_unc_path(distro, wsl_topology))
    linux_argv = [
        tool_python, "-m", "scalesim.scale",
        "-c", wsl_config,
        "-t", wsl_topology,
        "-p", wsl_outputs,
        "-i", "gemm", "-s", "N",
    ]
    host_argv = wsl_command(distro, linux_argv, tool_repo)
    started_utc = utc_now()
    started = time.monotonic()
    timed_out = False
    try:
        completed = subprocess.run(
            host_argv,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=timeout_seconds,
            check=False,
        )
        return_code = completed.returncode
        stdout = completed.stdout or ""
        stderr = completed.stderr or ""
    except subprocess.TimeoutExpired as error:
        timed_out = True
        return_code = 124
        stdout = error.stdout or ""
        stderr = (error.stderr or "") + f"\nTimed out after {timeout_seconds}s\n"
    elapsed = time.monotonic() - started
    (candidate_root / "stdout.log").write_text(stdout, encoding="utf-8")
    (candidate_root / "stderr.log").write_text(stderr, encoding="utf-8")
    atomic_json(candidate_root / "command.json", {
        "host_argv": host_argv,
        "linux_argv": linux_argv,
        "linux_command": shlex.join(linux_argv),
        "cwd": tool_repo,
        "started_utc": started_utc,
        "completed_utc": utc_now(),
        "wall_time_seconds": round(elapsed, 6),
        "return_code": return_code,
        "timed_out": timed_out,
    })
    wsl_report_root = wsl_unc_path(distro, f"{wsl_outputs}/{run_name}")
    if wsl_report_root.is_dir():
        report_root.mkdir(parents=True, exist_ok=True)
        for report_name in NATIVE_REPORT_NAMES:
            source_report = wsl_report_root / report_name
            if source_report.is_file():
                shutil.copy2(source_report, report_root / report_name)
    report_paths = {
        "compute": report_root / "COMPUTE_REPORT.csv",
        "access": report_root / "DETAILED_ACCESS_REPORT.csv",
        "bandwidth": report_root / "BANDWIDTH_REPORT.csv",
        "time": report_root / "TIME_REPORT.csv",
    }
    missing = [name for name, path in report_paths.items() if not path.exists()]
    common = {
        "scenario": "reviewer_holdout_scalesim",
        "candidate_id": candidate["candidate_id"],
        "config_hash": candidate["config_hash"],
        "completed_utc": utc_now(),
        "resolved": {"parameters": parameters, "architecture": {
            "array_rows": 4, "array_columns": 4, "macs_per_pe_per_cycle": 1,
            "dataflow": "weight_stationary", "interface_bandwidth": [8, 8, 8], "seed": 40,
        }},
        "provenance": {
            **provenance,
            "adapter": "run_reviewer_p1_scalesim.py",
            "adapter_input_allowlist": sorted(ALLOWED_PARAMETERS),
            "frozen_config_source": template_path.relative_to(run_root.parents[3]).as_posix(),
            "frozen_config_source_sha256": sha256_file(template_path),
            "resolved_config_sha256": sha256_file(config_path),
            "topology_sha256": sha256_file(topology_path),
            "command": shlex.join(linux_argv),
            "wall_time_seconds": round(elapsed, 6),
            "raw_path": candidate["raw_relpath"],
            "wsl_staging_root": wsl_candidate_root,
        },
    }
    if return_code != 0 or missing:
        payload = {
            **common,
            "status": "failed",
            "failure": {"return_code": return_code, "timed_out": timed_out, "missing_reports": missing,
                        "stdout": "stdout.log", "stderr": "stderr.log"},
        }
        atomic_json(raw_result, payload)
        return "failed", raw_result, f"return_code={return_code};missing_reports={','.join(missing)}"

    compute = read_csv_first(report_paths["compute"])
    access = read_csv_first(report_paths["access"])
    bandwidth = read_csv_first(report_paths["bandwidth"])
    total_with_prefetch = int(float(compute["Total Cycles (incl. prefetch)"]))
    warm_cycles = int(float(compute["Total Cycles"]))
    accesses = {
        "sram_ifmap_reads": int(float(access["SRAM IFMAP Reads"])),
        "sram_filter_reads": int(float(access["SRAM Filter Reads"])),
        "sram_ofmap_writes": int(float(access["SRAM OFMAP Writes"])),
        "dram_ifmap_reads": int(float(access["DRAM IFMAP Reads"])),
        "dram_filter_reads": int(float(access["DRAM Filter Reads"])),
        "dram_ofmap_writes": int(float(access["DRAM OFMAP Writes"])),
    }
    bandwidths = {
        "avg_ifmap_sram_bw": float(bandwidth["Avg IFMAP SRAM BW"]),
        "avg_filter_sram_bw": float(bandwidth["Avg FILTER SRAM BW"]),
        "avg_ofmap_sram_bw": float(bandwidth["Avg OFMAP SRAM BW"]),
        "avg_ifmap_dram_bw": float(bandwidth["Avg IFMAP DRAM BW"]),
        "avg_filter_dram_bw": float(bandwidth["Avg FILTER DRAM BW"]),
        "avg_ofmap_dram_bw": float(bandwidth["Avg OFMAP DRAM BW"]),
    }
    metrics_identity = {
        "case_id": case_id,
        "M": int(parameters["M"]), "N": int(parameters["N"]), "K": int(parameters["K"]),
        "total_cycles": total_with_prefetch, "warm_cycles": warm_cycles,
        "stall_cycles": int(float(compute["Stall Cycles"])),
        "overall_util_percent": float(compute["Overall Util %"]),
        "accesses": accesses, "bandwidth": bandwidths,
    }
    raw_files = inventory(candidate_root, {raw_result})
    payload = {
        **common,
        "status": "completed",
        "evidence": {"shape_and_tool_config": "Exact", "cross_tool_timing": "Trend",
                     "accesses": "Numerical only under a shared counter definition; otherwise Trend"},
        "metrics": {
            "expected_macs": int(parameters["M"]) * int(parameters["N"]) * int(parameters["K"]),
            "total_cycles": total_with_prefetch,
            "warm_cycles": warm_cycles,
            "prefetch_cycles": total_with_prefetch - warm_cycles,
            "memory_stall_cycles": int(float(compute["Stall Cycles"])),
            "utilization_percent": float(compute["Overall Util %"]),
            "mapping_efficiency_percent": float(compute["Mapping Efficiency %"]),
            "compute_utilization_percent": float(compute["Compute Util %"]),
            "accesses": accesses,
            "bandwidth": bandwidths,
            "canonical_metrics_sha256": canonical_hash(metrics_identity),
            "raw_report_bundle_sha256": canonical_hash(raw_files),
        },
        "denominators": {
            "total_cycles": "COMPUTE_REPORT: Total Cycles (incl. prefetch)",
            "warm_cycles": "COMPUTE_REPORT: Total Cycles",
            "prefetch_cycles": "Total Cycles (incl. prefetch) minus Total Cycles",
            "stall_cycles": "COMPUTE_REPORT: Stall Cycles",
            "utilization_percent": "COMPUTE_REPORT: Overall Util %",
            "average_bandwidth": "BANDWIDTH_REPORT native per-stream average over the SCALE-Sim-defined trace window",
        },
        "raw_files": raw_files,
        "limitations": [
            "External SCALE-Sim native schedule and memory arbitration; compare timing to STAGE only as Trend evidence.",
            "The adapter consumes only frozen high-level M/N/K/precision/repeat/seed fields and never reads STAGE output.",
        ],
    }
    atomic_json(raw_result, payload)
    return "completed", raw_result, ""


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--run-root", type=Path, required=True)
    parser.add_argument("--distro", default="Ubuntu-24.04")
    parser.add_argument("--tool-repo", default="/opt/stage-baselines/tools/SCALE-Sim")
    parser.add_argument("--tool-python", default="/opt/stage-baselines/venv/bin/python")
    parser.add_argument("--timeout-seconds", type=int, default=14400)
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--select-candidate", action="append", default=[])
    args = parser.parse_args()
    repo_root = args.repo_root.resolve()
    run_root = args.run_root.resolve()
    p1 = load_p1_module(repo_root)
    candidates = json.loads((run_root / "manifests" / "p1_candidates.json").read_text(encoding="utf-8"))
    selected = [item for item in candidates if item.get("kind") == "holdout" and item.get("provider") == "scalesim"]
    if args.select_candidate:
        requested = set(args.select_candidate)
        selected = [item for item in selected if item["candidate_id"] in requested]
        missing = requested - {item["candidate_id"] for item in selected}
        if missing:
            raise ValueError(f"Unknown or non-SCALE-Sim candidates: {sorted(missing)}")
    if not args.select_candidate and len(selected) != 16:
        raise ValueError(f"Expected exactly 16 SCALE-Sim hold-out candidates, found {len(selected)}")
    template_path = repo_root / "experiments" / "aspdac" / "results" / "scalesim_wsl" / "core_mnk_v1" / "aspdac_4x4_ws.cfg"
    template = template_path.read_text(encoding="utf-8")
    provenance = tool_provenance(args.distro, args.tool_repo, args.tool_python)
    if not provenance["git_commit_matches_expected"]:
        raise RuntimeError(f"SCALE-Sim commit mismatch: {provenance['git_commit']}")
    checkpoint_path = run_root / "manifests" / "p1_checkpoint.json"
    failures = 0
    for index, candidate in enumerate(selected, start=1):
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))["candidates"]
        raw_result = run_root / candidate["raw_relpath"]
        if args.resume and checkpoint[candidate["candidate_id"]]["status"] == "completed" and raw_result.exists():
            print(f"[{index}/{len(selected)}] {candidate['candidate_id']}: resumed completed", flush=True)
            continue
        input_path = run_root / "inputs" / f"{candidate['candidate_id']}.json"
        input_payload = json.loads(input_path.read_text(encoding="utf-8"))
        parameters = validate_candidate(candidate, input_payload)
        mark_running(p1, run_root, candidate["candidate_id"])
        print(f"[{index}/{len(selected)}] {candidate['candidate_id']}: running", flush=True)
        try:
            status, result_path, reason = run_candidate(
                candidate, parameters, run_root, template, template_path, provenance,
                args.distro, args.tool_repo, args.tool_python, args.timeout_seconds)
        except Exception as error:  # preserve the single candidate failure and continue
            status = "failed"
            reason = f"adapter_exception={type(error).__name__}:{error}"
            result_path = raw_result
            atomic_json(result_path, {"status": status, "scenario": "reviewer_holdout_scalesim",
                                      "candidate_id": candidate["candidate_id"], "config_hash": candidate["config_hash"],
                                      "completed_utc": utc_now(), "failure": {"reason": reason}})
        p1.record_status(run_root, candidate["candidate_id"], status, reason, result_path)
        failures += status != "completed"
        print(f"[{index}/{len(selected)}] {candidate['candidate_id']}: {status}", flush=True)
    summary = p1.summarize(run_root)
    print(json.dumps({
        "selected": len(selected),
        "failed": failures,
        "comparison_gate_ready_count": summary["comparison_gate_ready_count"],
        "comparison_gate_blocked_count": summary["comparison_gate_blocked_count"],
    }, indent=2, sort_keys=True))
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
