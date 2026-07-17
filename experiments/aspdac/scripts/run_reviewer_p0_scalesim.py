#!/usr/bin/env python3
"""Run the six frozen reviewer P0-B SCALE-Sim wall-clock candidates."""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import shlex
import shutil
import subprocess
import time
from pathlib import Path
from typing import Any


EXPECTED_PARAMETERS = {
    "tool", "mesh_dimension", "dataflow", "denominator_kind", "case", "repeat",
}
FROZEN_CASES = {
    "small_gemm": (128, 128, 128),
    "attention_qk": (128, 128, 64),
}
NATIVE_REPORT_NAMES = (
    "COMPUTE_REPORT.csv", "DETAILED_ACCESS_REPORT.csv", "BANDWIDTH_REPORT.csv", "TIME_REPORT.csv",
)
EXPECTED_TOOL_COMMIT = "9f98c4371055a54c75209c2e02b640b897550532"
WSL_STAGING_ROOT = "/opt/stage-baselines/reviewer_extension_runs/p0b_20260717"


def load_module(name: str, path: Path) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load module {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_candidate(candidate: dict[str, Any], input_payload: dict[str, Any]) -> dict[str, Any]:
    if candidate.get("provider") != "scalesim_reviewer_context":
        raise ValueError(f"Unexpected provider for {candidate.get('candidate_id')}")
    if candidate.get("scenario") != "reviewer_specialist_runtime":
        raise ValueError(f"Unexpected scenario for {candidate.get('candidate_id')}")
    parameters = candidate.get("parameters")
    if not isinstance(parameters, dict) or set(parameters) != EXPECTED_PARAMETERS:
        raise ValueError(f"P0-B parameter allowlist violation for {candidate.get('candidate_id')}")
    if parameters.get("tool") != "SCALE-Sim" or int(parameters.get("mesh_dimension", 0)) != 4:
        raise ValueError("P0-B requires SCALE-Sim with a 4x4 array")
    if str(parameters.get("dataflow", "")).upper() != "WS":
        raise ValueError("P0-B requires weight-stationary dataflow")
    if parameters.get("denominator_kind") != "external_native_workload":
        raise ValueError("P0-B denominator contract changed")
    if parameters.get("case") not in FROZEN_CASES or int(parameters.get("repeat", -1)) not in (0, 1, 2):
        raise ValueError("P0-B case/repeat contract changed")
    if input_payload.get("scenario") != candidate["scenario"]:
        raise ValueError("Input scenario mismatch")
    if input_payload.get("candidate_id") != candidate["candidate_id"]:
        raise ValueError("Input candidate identity mismatch")
    if input_payload.get("config_hash") != candidate["config_hash"]:
        raise ValueError("Input config hash mismatch")
    if input_payload.get("resolved") != {"parameters": parameters}:
        raise ValueError("Input must contain only the frozen high-level parameters")
    return dict(parameters)


def update_checkpoint(
    p0: Any,
    bundle: Path,
    candidate: dict[str, Any],
    status: str,
    reason: str = "",
    raw_path: Path | None = None,
) -> int:
    path = bundle / "manifests" / "p0_checkpoint.json"
    checkpoint = json.loads(path.read_text(encoding="utf-8"))
    candidate_id = candidate["candidate_id"]
    prior = checkpoint["candidates"].get(candidate_id, {})
    if status == "running":
        attempt = int(prior.get("attempt", 0)) + 1
        checkpoint["candidates"][candidate_id] = {
            "status": "running",
            "started_utc": p0.utc_now(),
            "attempt": attempt,
            "config_hash": candidate["config_hash"],
        }
    else:
        attempt = int(prior.get("attempt", 1))
        prior.update({
            "status": status,
            "reason": reason,
            "finished_utc": p0.utc_now(),
            "raw_path": str(raw_path) if raw_path else "",
        })
        checkpoint["candidates"][candidate_id] = prior
    checkpoint["updated_utc"] = p0.utc_now()
    p0.atomic_json(path, checkpoint)
    return attempt


def parse_native_time(p0: Any, text: str) -> tuple[float | None, int | None]:
    rss_match = re.search(r"Maximum resident set size \(kbytes\):\s*(\d+)", text)
    elapsed_match = re.search(r"Elapsed \(wall clock\) time \(h:mm:ss or m:ss\):\s*([^\r\n]+)", text)
    rss_bytes = int(rss_match.group(1)) * 1024 if rss_match else None
    wall_seconds = p0.parse_gnu_time_elapsed(elapsed_match.group(1)) if elapsed_match else None
    return wall_seconds, rss_bytes


def run_candidate(
    p0: Any,
    p1: Any,
    candidate: dict[str, Any],
    parameters: dict[str, Any],
    bundle: Path,
    attempt: int,
    template: str,
    template_path: Path,
    provenance: dict[str, Any],
    distro: str,
    tool_repo: str,
    tool_python: str,
) -> dict[str, Any]:
    candidate_id = candidate["candidate_id"]
    case_id = str(parameters["case"])
    repeat = int(parameters["repeat"])
    m, n, k = FROZEN_CASES[case_id]
    expected_macs = m * n * k
    policy = candidate.get("policy") or {}
    timeout_seconds = int(float(policy.get("timeout_seconds", 1800)))
    memory_limit_bytes = int(policy.get("max_peak_working_set_bytes", 0) or 0)
    run_name = f"aspdac_reviewer_p0b_{case_id}_r{repeat}"
    evidence_dir = bundle / "raw" / "scalesim_context" / candidate_id
    evidence_dir.mkdir(parents=True, exist_ok=True)
    config_path = evidence_dir / "aspdac_4x4_ws.cfg"
    topology_path = evidence_dir / f"{case_id}.csv"
    config_path.write_text(p1.resolved_config(template, run_name), encoding="utf-8")
    p1.write_topology(topology_path, case_id, m, n, k)
    for name in (*NATIVE_REPORT_NAMES, "stdout.txt", "stderr.txt", "time.txt", "exit_code.txt"):
        (evidence_dir / name).unlink(missing_ok=True)

    wsl_root = f"{WSL_STAGING_ROOT}/{candidate_id}/attempt-{attempt}"
    wsl_inputs = f"{wsl_root}/inputs"
    wsl_outputs = f"{wsl_root}/scale_outputs"
    prepared = p1.capture(distro, ["mkdir", "-p", wsl_inputs, wsl_outputs])
    if prepared.returncode != 0:
        raise RuntimeError(f"Cannot prepare WSL staging: {prepared.stderr.strip()}")
    wsl_config = f"{wsl_inputs}/aspdac_4x4_ws.cfg"
    wsl_topology = f"{wsl_inputs}/{case_id}.csv"
    shutil.copy2(config_path, p1.wsl_unc_path(distro, wsl_config))
    shutil.copy2(topology_path, p1.wsl_unc_path(distro, wsl_topology))
    wsl_time = f"{wsl_root}/time.txt"
    wsl_stdout = f"{wsl_root}/stdout.txt"
    wsl_stderr = f"{wsl_root}/stderr.txt"
    wsl_exit = f"{wsl_root}/exit_code.txt"
    linux_argv = [
        tool_python, "-m", "scalesim.scale", "-c", wsl_config, "-t", wsl_topology,
        "-p", wsl_outputs, "-i", "gemm", "-s", "N",
    ]
    native_command = (
        f"cd {shlex.quote(tool_repo)}; set +e; "
        f"timeout --signal=TERM --kill-after=5s {timeout_seconds}s "
        f"/usr/bin/time -v -o {shlex.quote(wsl_time)} {shlex.join(linux_argv)} "
        f"> {shlex.quote(wsl_stdout)} 2> {shlex.quote(wsl_stderr)}; "
        f"code=$?; printf '%s\\n' \"$code\" > {shlex.quote(wsl_exit)}; "
        f"printf 'SCALESIM_WRAPPER_EXIT=%s\\n' \"$code\""
    )
    host_argv = p1.wsl_command(distro, ["bash", "-lc", native_command])
    envelope_started = time.perf_counter()
    outer_timed_out = False
    try:
        wrapper = subprocess.run(
            host_argv,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=timeout_seconds + 30,
            check=False,
        )
        wrapper_return_code = wrapper.returncode
        wrapper_stdout = wrapper.stdout or ""
        wrapper_stderr = wrapper.stderr or ""
    except subprocess.TimeoutExpired as error:
        outer_timed_out = True
        wrapper_return_code = 124
        wrapper_stdout = error.stdout if isinstance(error.stdout, str) else ""
        wrapper_stderr = (error.stderr if isinstance(error.stderr, str) else "") + "\nOuter WSL wrapper timeout\n"
    envelope_wall = time.perf_counter() - envelope_started

    for name, wsl_path in {
        "stdout.txt": wsl_stdout,
        "stderr.txt": wsl_stderr,
        "time.txt": wsl_time,
        "exit_code.txt": wsl_exit,
    }.items():
        source = p1.wsl_unc_path(distro, wsl_path)
        if source.is_file():
            shutil.copy2(source, evidence_dir / name)
    wsl_report_root = p1.wsl_unc_path(distro, f"{wsl_outputs}/{run_name}")
    if wsl_report_root.is_dir():
        for name in NATIVE_REPORT_NAMES:
            source = wsl_report_root / name
            if source.is_file():
                shutil.copy2(source, evidence_dir / name)

    (evidence_dir / "wrapper_stdout.txt").write_text(wrapper_stdout, encoding="utf-8")
    (evidence_dir / "wrapper_stderr.txt").write_text(wrapper_stderr, encoding="utf-8")
    p1.atomic_json(evidence_dir / "command.json", {
        "host_argv": host_argv,
        "native_command": native_command,
        "linux_argv": linux_argv,
        "started_utc": p0.utc_now(),
        "wsl_envelope_wall_seconds": envelope_wall,
        "wrapper_return_code": wrapper_return_code,
        "outer_timed_out": outer_timed_out,
    })

    time_text = (evidence_dir / "time.txt").read_text(encoding="utf-8", errors="replace") if (evidence_dir / "time.txt").is_file() else ""
    child_wall_seconds, child_peak_bytes = parse_native_time(p0, time_text)
    try:
        native_return_code = int((evidence_dir / "exit_code.txt").read_text(encoding="ascii", errors="replace").strip())
    except (OSError, ValueError):
        native_return_code = None
    report_paths = {name: evidence_dir / name for name in NATIVE_REPORT_NAMES}
    missing_reports = [name for name, path in report_paths.items() if not path.is_file()]
    if native_return_code == 124 or outer_timed_out:
        status = "timeout"
        reason = f"scalesim_native_timeout_seconds:{timeout_seconds}"
    elif child_peak_bytes is not None and memory_limit_bytes and child_peak_bytes > memory_limit_bytes:
        status = "resource_limit"
        reason = f"scalesim_child_peak_rss_exceeded:{child_peak_bytes}>{memory_limit_bytes}"
    elif native_return_code != 0 or child_wall_seconds is None or child_peak_bytes is None or missing_reports:
        status = "failed"
        reason = f"scalesim_incomplete_evidence:return_code={native_return_code};missing={','.join(missing_reports)}"
    else:
        status = "completed"
        reason = "SCALE-Sim native workload completed with GNU time child wall/RSS and all four native reports."

    metrics: dict[str, Any] = {
        "M": m,
        "N": n,
        "K": k,
        "expected_macs": expected_macs,
        "denominator_kind": "exact_gemm_mac_operations",
        "denominator_value": expected_macs,
        "ratio_eligible": False,
        "scalesim_child_wall_seconds": child_wall_seconds,
        "scalesim_child_peak_rss_bytes": child_peak_bytes,
        "wsl_envelope_wall_seconds": envelope_wall,
        "native_termination": "completed_reports_present" if status == "completed" else status,
    }
    if not missing_reports:
        compute = p1.read_csv_first(report_paths["COMPUTE_REPORT.csv"])
        access = p1.read_csv_first(report_paths["DETAILED_ACCESS_REPORT.csv"])
        bandwidth = p1.read_csv_first(report_paths["BANDWIDTH_REPORT.csv"])
        metrics.update({
            "total_cycles": int(float(compute["Total Cycles (incl. prefetch)"])),
            "warm_cycles": int(float(compute["Total Cycles"])),
            "stall_cycles": int(float(compute["Stall Cycles"])),
            "utilization_percent": float(compute["Overall Util %"]),
            "accesses": {key.strip(): int(float(value)) for key, value in access.items() if key.strip() and key.strip() != "LayerID" and value.strip()},
            "bandwidth": {key.strip(): float(value) for key, value in bandwidth.items() if key.strip() and key.strip() != "LayerID" and value.strip()},
        })
        metrics["canonical_native_metrics_sha256"] = p1.canonical_hash({
            key: metrics[key] for key in ("M", "N", "K", "expected_macs", "total_cycles", "warm_cycles", "stall_cycles", "utilization_percent", "accesses", "bandwidth")
        })

    raw_files = p1.inventory(evidence_dir)
    manager_metrics = {
        "process_wall_seconds": child_wall_seconds,
        "wsl_envelope_wall_seconds": envelope_wall,
        "peak_working_set_bytes": child_peak_bytes,
        "timeout_seconds": timeout_seconds,
        "memory_limit_bytes": memory_limit_bytes or None,
        "return_code": native_return_code,
        "measurement_source": "wsl_native_gnu_time_v",
        "host": {
            "platform": p0.platform.platform(),
            "processor": p0.platform.processor(),
            "python": p0.sys.version.split()[0],
            "physical_memory_bytes": p0.total_physical_memory_bytes(),
            "git_commit": p0.current_git_commit(),
        },
    }
    return {
        "status": status,
        "reason": reason,
        "completed_utc": p0.utc_now(),
        "measurement_kind": "specialist_tool_runtime_context",
        "scenario": candidate["scenario"],
        "candidate_id": candidate_id,
        "config_hash": candidate["config_hash"],
        "parameters": parameters,
        "metrics": metrics,
        "manager_metrics": manager_metrics,
        "provenance": {
            **provenance,
            "manager_version": p0.MANAGER_VERSION,
            "provider": candidate["provider"],
            "tool": "SCALE-Sim",
            "wsl_native_staging_directory": wsl_root,
            "native_command": native_command,
            "native_return_code": native_return_code,
            "frozen_config_source": template_path.relative_to(bundle.parents[3]).as_posix(),
            "frozen_config_source_sha256": p1.sha256_file(template_path),
            "resolved_config_sha256": p1.sha256_file(config_path),
            "topology_sha256": p1.sha256_file(topology_path),
        },
        "raw_evidence": {row["path"]: str((evidence_dir / row["path"]).resolve()) for row in raw_files},
        "raw_evidence_sha256": {row["path"]: row["sha256"] for row in raw_files},
        "denominators": {
            "workload": "Exact M*N*K logical MAC operations",
            "wall_time": "GNU /usr/bin/time -v elapsed time for the native SCALE-Sim Python child",
            "peak_rss": "GNU /usr/bin/time -v maximum resident set size for the native SCALE-Sim Python child",
            "cycles": "COMPUTE_REPORT Total Cycles (incl. prefetch)",
        },
        "limitations": [
            "This is wall-clock/resource context, not evidence that one model is more accurate than another.",
            "No cross-tool wall-time ratio is eligible; each specialist retains its native workload/termination denominator.",
            "The adapter consumes only the frozen case/repeat contract and never reads STAGE schedules, counts, traces, or results.",
            "WSL launch, staging, and evidence retrieval are excluded from child wall/RSS and reported separately.",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--distro", default="Ubuntu-24.04")
    parser.add_argument("--tool-repo", default="/opt/stage-baselines/tools/SCALE-Sim")
    parser.add_argument("--tool-python", default="/opt/stage-baselines/venv/bin/python")
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--select-candidate", action="append", default=[])
    args = parser.parse_args()
    repo_root = args.repo_root.resolve()
    bundle = args.bundle.resolve()
    manifest_path = args.manifest.resolve() if args.manifest else bundle / "manifests" / "reviewer_p0_specialist_runtime_context-candidates.json"
    p0 = load_module("reviewer_extension_p0", repo_root / "experiments/aspdac/scripts/reviewer_extension_p0.py")
    p1 = load_module("run_reviewer_p1_scalesim", repo_root / "experiments/aspdac/scripts/run_reviewer_p1_scalesim.py")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    candidates = [row for row in manifest["candidates"] if row.get("provider") == "scalesim_reviewer_context"]
    if not args.select_candidate and len(candidates) != 6:
        raise ValueError(f"Expected exactly six P0-B SCALE-Sim candidates, found {len(candidates)}")
    if args.select_candidate:
        requested = set(args.select_candidate)
        candidates = [row for row in candidates if row["candidate_id"] in requested]
        missing = requested - {row["candidate_id"] for row in candidates}
        if missing:
            raise ValueError(f"Unknown P0-B SCALE-Sim candidates: {sorted(missing)}")
    template_path = repo_root / "experiments/aspdac/results/scalesim_wsl/core_mnk_v1/aspdac_4x4_ws.cfg"
    template = template_path.read_text(encoding="utf-8")
    provenance = p1.tool_provenance(args.distro, args.tool_repo, args.tool_python)
    if provenance["git_commit"] != EXPECTED_TOOL_COMMIT:
        raise RuntimeError(f"SCALE-Sim commit mismatch: {provenance['git_commit']}")

    failures = 0
    for index, candidate in enumerate(candidates, start=1):
        raw_path = bundle / "raw" / f"{candidate['candidate_id']}.json"
        if args.resume and raw_path.is_file():
            prior = json.loads(raw_path.read_text(encoding="utf-8"))
            if prior.get("status") == "completed":
                print(f"[{index}/{len(candidates)}] {candidate['candidate_id']}: resumed completed", flush=True)
                continue
        input_path = bundle / "manifests" / "inputs" / f"{candidate['candidate_id']}.json"
        input_payload = json.loads(input_path.read_text(encoding="utf-8"))
        parameters = validate_candidate(candidate, input_payload)
        attempt = update_checkpoint(p0, bundle, candidate, "running")
        print(f"[{index}/{len(candidates)}] {candidate['candidate_id']}: running {parameters['case']} repeat={parameters['repeat']}", flush=True)
        try:
            payload = run_candidate(
                p0, p1, candidate, parameters, bundle, attempt, template, template_path, provenance,
                args.distro, args.tool_repo, args.tool_python,
            )
        except Exception as error:
            payload = p0.failure_payload(candidate, "failed", f"scalesim_adapter_error:{type(error).__name__}:{error}", {
                "process_wall_seconds": None,
                "peak_working_set_bytes": None,
                "measurement_source": "adapter_failed_before_complete_evidence",
            })
        p0.atomic_json(raw_path, payload)
        update_checkpoint(p0, bundle, candidate, str(payload.get("status", "failed")), str(payload.get("reason", "")), raw_path)
        failures += payload.get("status") != "completed"
        print(f"[{index}/{len(candidates)}] {candidate['candidate_id']}: {payload.get('status')}", flush=True)
    p0.analyze_command(bundle)
    print(json.dumps({"selected": len(candidates), "failed": failures}, indent=2, sort_keys=True))
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
