#!/usr/bin/env python3
"""Config-driven, resumable ASP-DAC reviewer-extension P0 manager and analyzer."""

from __future__ import annotations

import argparse
import csv
import ctypes
import hashlib
import itertools
import json
import math
import os
import platform
import re
import shlex
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from statistics import median
from typing import Any, Iterable

import yaml


REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_SMOKE_PLAN = REPO_ROOT / "experiments/aspdac/specs/reviewer_extension_20260717/p0_smoke.yaml"
DEFAULT_RUNNER_DLL = REPO_ROOT / "tools/HardwareSim.AspdacRunner/bin/Release/net8.0/HardwareSim.AspdacRunner.dll"
MANAGER_VERSION = "aspg-reviewer-p0-manager-1.1"
TERMINAL_STATUSES = {"completed", "timeout", "resource_limit", "skipped", "failed"}
BOOKSIM_WSL_DISTRO = "Ubuntu-24.04"
BOOKSIM_WSL_BINARY = "/opt/stage-baselines/tools/booksim2/src/booksim"
BOOKSIM_WSL_STAGING_ROOT = "/tmp/stage-reviewer-p0-booksim2"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def atomic_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    try:
        os.replace(temporary, path)
    except PermissionError:
        with path.open("w", encoding="utf-8") as stream:
            stream.write(temporary.read_text(encoding="utf-8"))
            stream.flush()
            os.fsync(stream.fileno())
        temporary.unlink(missing_ok=True)


def load_plan(path: Path) -> dict[str, Any]:
    value = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"P0 plan must be a mapping: {path}")
    if value.get("schema_version") != "aspg-reviewer-p0-plan-1.0":
        raise ValueError("P0 plan schema_version must be aspg-reviewer-p0-plan-1.0")
    if not isinstance(value.get("experiments"), list) or not value["experiments"]:
        raise ValueError("P0 plan requires a non-empty experiments list")
    return value


def resolve_path(value: str | Path) -> Path:
    path = Path(value)
    return path if path.is_absolute() else REPO_ROOT / path


def ensure_bundle(bundle: Path) -> None:
    for name in ("config", "raw", "summary", "figures", "manifests", "failures", "logs"):
        (bundle / name).mkdir(parents=True, exist_ok=True)


def current_git_commit() -> str | None:
    try:
        return subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, text=True, stderr=subprocess.DEVNULL
        ).strip() or None
    except (OSError, subprocess.CalledProcessError):
        return None


def total_physical_memory_bytes() -> int | None:
    if os.name == "nt":
        class MemoryStatusEx(ctypes.Structure):
            _fields_ = [
                ("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
            ]
        value = MemoryStatusEx()
        value.dwLength = ctypes.sizeof(value)
        return int(value.ullTotalPhys) if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(value)) else None
    if hasattr(os, "sysconf"):
        try:
            return int(os.sysconf("SC_PHYS_PAGES") * os.sysconf("SC_PAGE_SIZE"))
        except (ValueError, OSError):
            return None
    return None


def expand_experiment(experiment: dict[str, Any]) -> Iterable[dict[str, Any]]:
    fixed = dict(experiment.get("fixed") or {})
    axes = experiment.get("axes") or {}
    if not isinstance(axes, dict):
        raise ValueError(f"Experiment {experiment.get('name')} axes must be a mapping")
    axis_names = list(axes)
    axis_values = []
    for name in axis_names:
        values = axes[name]
        if not isinstance(values, list) or not values:
            raise ValueError(f"Experiment axis {name} must be a non-empty list")
        axis_values.append(values)
    cases = experiment.get("cases") or [{}]
    if not isinstance(cases, list) or not cases:
        raise ValueError(f"Experiment {experiment.get('name')} cases must be a non-empty list")
    products = itertools.product(*axis_values) if axis_names else [()]
    for case in cases:
        if not isinstance(case, dict):
            raise ValueError("P0 cases must be mappings")
        for values in products:
            parameters = {**fixed, **case, **dict(zip(axis_names, values))}
            yield parameters
        products = itertools.product(*axis_values) if axis_names else [()]


def expand_plan(plan: dict[str, Any]) -> list[dict[str, Any]]:
    candidates: list[dict[str, Any]] = []
    seen: set[str] = set()
    for experiment in plan["experiments"]:
        required = ("name", "provider", "scenario")
        if not isinstance(experiment, dict) or any(not experiment.get(name) for name in required):
            raise ValueError("Each P0 experiment requires name, provider, and scenario")
        for parameters in expand_experiment(experiment):
            config_identity = {
                "schema_version": plan["schema_version"],
                "provider": experiment["provider"],
                "scenario": experiment["scenario"],
                "parameters": parameters,
            }
            config_hash = sha256_text(canonical_json(config_identity))
            candidate_id = f"p0-{str(experiment['provider']).replace('_', '-')}-{config_hash[:16]}"
            if candidate_id in seen:
                raise ValueError(f"Duplicate stable candidate id {candidate_id}")
            seen.add(candidate_id)
            candidates.append({
                **config_identity,
                "plan_name": plan.get("name"),
                "experiment": experiment["name"],
                "candidate_id": candidate_id,
                "config_hash": config_hash,
                "policy": {**(plan.get("policy") or {}), **(experiment.get("policy") or {})},
            })
    return sorted(candidates, key=lambda item: item["candidate_id"])


def plan_command(plan_path: Path, bundle_override: Path | None = None) -> tuple[Path, list[dict[str, Any]]]:
    plan = load_plan(plan_path)
    bundle = bundle_override or resolve_path(plan["bundle_root"])
    ensure_bundle(bundle)
    candidates = expand_plan(plan)
    plan_hash = sha256_text(canonical_json(plan))
    snapshot = bundle / "config" / f"{plan.get('name', 'p0')}-{plan_hash[:12]}.yaml"
    if not snapshot.exists():
        shutil.copy2(plan_path, snapshot)
    manifest = {
        "schema_version": "aspg-reviewer-p0-candidate-manifest-1.0",
        "manager_version": MANAGER_VERSION,
        "generated_utc": utc_now(),
        "source_plan": str(plan_path.resolve()),
        "plan_hash": plan_hash,
        "candidate_count": len(candidates),
        "candidates": candidates,
    }
    atomic_json(bundle / "manifests" / f"{plan.get('name', 'p0')}-candidates.json", manifest)
    print(f"planned={len(candidates)} bundle={bundle}")
    return bundle, candidates


def read_process_memory(pid: int) -> tuple[int, int]:
    """Returns current RSS and OS-reported peak working set when available."""
    if os.name == "nt":
        from ctypes import wintypes

        class ProcessMemoryCountersEx(ctypes.Structure):
            _fields_ = [
                ("cb", ctypes.c_ulong), ("PageFaultCount", ctypes.c_ulong),
                ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t),
                ("PrivateUsage", ctypes.c_size_t),
            ]
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        psapi = ctypes.WinDLL("psapi", use_last_error=True)
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD]
        psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
        handle = kernel32.OpenProcess(0x1000 | 0x0010, False, pid)
        if not handle:
            return 0, 0
        try:
            counters = ProcessMemoryCountersEx()
            counters.cb = ctypes.sizeof(counters)
            ok = psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb)
            return (int(counters.WorkingSetSize), int(counters.PeakWorkingSetSize)) if ok else (0, 0)
        finally:
            kernel32.CloseHandle(handle)
    status = Path(f"/proc/{pid}/status")
    if status.exists():
        values: dict[str, int] = {}
        for line in status.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith(("VmRSS:", "VmHWM:")):
                key, raw = line.split(":", 1)
                values[key] = int(raw.strip().split()[0]) * 1024
        return values.get("VmRSS", 0), values.get("VmHWM", 0)
    return 0, 0


def run_wsl_script(script: str, *, input_bytes: bytes | None = None, timeout: float | None = None) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        ["wsl.exe", "-d", BOOKSIM_WSL_DISTRO, "--", "bash", "-lc", script],
        input=input_bytes,
        capture_output=True,
        timeout=timeout,
    )


def read_wsl_file(path: str, *, required: bool = True) -> bytes:
    completed = run_wsl_script(f"cat {shlex.quote(path)}")
    if completed.returncode != 0:
        if required:
            raise RuntimeError(
                f"Could not read WSL evidence {path}: {completed.stderr.decode('utf-8', errors='replace')}"
            )
        return b""
    return completed.stdout


def parse_gnu_time_elapsed(value: str) -> float:
    parts = value.strip().split(":")
    if len(parts) == 2:
        return int(parts[0]) * 60 + float(parts[1])
    if len(parts) == 3:
        return int(parts[0]) * 3600 + int(parts[1]) * 60 + float(parts[2])
    raise ValueError(f"Unsupported GNU time elapsed value: {value}")


def final_float(stdout: str, label: str) -> float | None:
    matches = re.findall(rf"^{re.escape(label)}\s*=\s*([-+0-9.eE]+)", stdout, flags=re.MULTILINE)
    return float(matches[-1]) if matches else None


def booksim_config(parameters: dict[str, Any]) -> tuple[str, int]:
    traffic = str(parameters["traffic"])
    if traffic == "uniform":
        native_traffic = "uniform"
    elif traffic == "hotspot":
        native_traffic = "hotspot({5})"
    else:
        raise ValueError(f"Unsupported reviewer BookSim2 traffic: {traffic}")
    target_packets = int(parameters["packet_count"])
    injection_rate = float(parameters["injection_rate"])
    # Latency mode normally needs three post-warmup converged samples. This
    # chooses a scale-aligned sample period, but native convergence remains in
    # control and exact equality is neither required nor implied.
    sample_period = max(1, math.ceil(target_packets / (16 * injection_rate * 3)))
    lines = [
        "topology = mesh;",
        "k = 4;",
        "n = 2;",
        "routing_function = dim_order;",
        "num_vcs = 1;",
        "vc_buf_size = 16;",
        "wait_for_tail_credit = 0;",
        f"traffic = {native_traffic};",
        f"injection_rate = {injection_rate:.17g};",
        "packet_size = 1;",
        f"seed = {int(parameters['seed'])};",
        "sim_type = latency;",
        f"sample_period = {sample_period};",
        "warmup_periods = 3;",
        "max_samples = 10;",
        "pair_stats = 1;",
        "stats_out = stats.m;",
    ]
    return "\n".join(lines) + "\n", sample_period


def execute_booksim2_candidate(candidate: dict[str, Any], bundle: Path) -> dict[str, Any]:
    candidate_id = candidate["candidate_id"]
    parameters = candidate["parameters"]
    policy = candidate.get("policy") or {}
    timeout_seconds = int(float(policy.get("timeout_seconds", 1800)))
    memory_limit_bytes = int(policy.get("max_peak_working_set_bytes", 0) or 0)
    config, sample_period = booksim_config(parameters)
    native_dir = f"{BOOKSIM_WSL_STAGING_ROOT}/{candidate_id}"
    prepare = run_wsl_script(
        f"umask 077; mkdir -p {shlex.quote(native_dir)}; cat > {shlex.quote(native_dir + '/booksim.cfg')}",
        input_bytes=config.encode("utf-8"),
        timeout=30,
    )
    if prepare.returncode != 0:
        raise RuntimeError(f"WSL native staging failed: {prepare.stderr.decode('utf-8', errors='replace')}")

    binary_check = run_wsl_script(
        f"test -x {shlex.quote(BOOKSIM_WSL_BINARY)} && sha256sum {shlex.quote(BOOKSIM_WSL_BINARY)}; "
        f"git -C {shlex.quote(str(Path(BOOKSIM_WSL_BINARY).parent.parent))} rev-parse HEAD 2>/dev/null || true",
        timeout=30,
    )
    binary_lines = binary_check.stdout.decode("utf-8", errors="replace").splitlines()
    binary_sha256 = binary_lines[0].split()[0] if binary_lines else None
    tool_git_commit = binary_lines[1].strip() if len(binary_lines) > 1 else None

    native_command = (
        f"cd {shlex.quote(native_dir)}; set +e; "
        f"timeout --signal=TERM --kill-after=5s {timeout_seconds}s "
        f"/usr/bin/time -v -o time.txt {shlex.quote(BOOKSIM_WSL_BINARY)} booksim.cfg "
        f"> stdout.txt 2> stderr.txt; code=$?; printf '%s\n' \"$code\" > exit_code.txt; "
        f"printf 'BOOKSIM_WRAPPER_EXIT=%s\n' \"$code\""
    )
    envelope_start = time.perf_counter()
    try:
        wrapper = run_wsl_script(native_command, timeout=timeout_seconds + 30)
    except subprocess.TimeoutExpired:
        manager_metrics = {
            "process_wall_seconds": time.perf_counter() - envelope_start,
            "peak_working_set_bytes": None,
            "timeout_seconds": timeout_seconds,
            "measurement_source": "wsl_wrapper_timeout_no_gnu_time",
        }
        return failure_payload(candidate, "timeout", f"wsl_wrapper_timeout_seconds:{timeout_seconds + 30}", manager_metrics)
    envelope_wall = time.perf_counter() - envelope_start

    stdout_bytes = read_wsl_file(native_dir + "/stdout.txt", required=False)
    stderr_bytes = read_wsl_file(native_dir + "/stderr.txt", required=False)
    time_bytes = read_wsl_file(native_dir + "/time.txt", required=False)
    stats_bytes = read_wsl_file(native_dir + "/stats.m", required=False)
    exit_bytes = read_wsl_file(native_dir + "/exit_code.txt", required=False)
    stdout = stdout_bytes.decode("utf-8", errors="replace")
    time_text = time_bytes.decode("utf-8", errors="replace")
    stats_text = stats_bytes.decode("utf-8", errors="replace")
    try:
        native_return_code = int(exit_bytes.decode("ascii", errors="replace").strip())
    except ValueError:
        native_return_code = None
    if native_return_code is None:
        time_status_match = re.search(r"^\s*Exit status:\s*(\d+)\s*$", time_text, flags=re.MULTILINE)
        if time_status_match:
            native_return_code = int(time_status_match.group(1))

    evidence_dir = bundle / "raw" / "booksim2_context" / candidate_id
    evidence_dir.mkdir(parents=True, exist_ok=True)
    evidence = {
        "booksim.cfg": config.encode("utf-8"),
        "stdout.txt": stdout_bytes,
        "stderr.txt": stderr_bytes,
        "time.txt": time_bytes,
        "stats.m": stats_bytes,
        "exit_code.txt": exit_bytes,
    }
    for name, content in evidence.items():
        (evidence_dir / name).write_bytes(content)

    rss_match = re.search(r"Maximum resident set size \(kbytes\):\s*(\d+)", time_text)
    elapsed_match = re.search(r"Elapsed \(wall clock\) time \(h:mm:ss or m:ss\):\s*([^\r\n]+)", time_text)
    child_peak_bytes = int(rss_match.group(1)) * 1024 if rss_match else None
    child_wall_seconds = parse_gnu_time_elapsed(elapsed_match.group(1)) if elapsed_match else None
    pair_matches = re.findall(r"pair_sent\(1,:\)\s*=\s*\[([^\]]*)\]", stats_text)
    native_accepted_packets = sum(int(value) for value in pair_matches[-1].split()) if pair_matches else None
    target_packets = int(parameters["packet_count"])
    if native_return_code == 124:
        status = "timeout"
        reason = f"booksim_native_timeout_seconds:{timeout_seconds}"
    elif child_peak_bytes is not None and memory_limit_bytes > 0 and child_peak_bytes > memory_limit_bytes:
        status = "resource_limit"
        reason = f"booksim_child_peak_rss_exceeded:{child_peak_bytes}>{memory_limit_bytes}"
    elif native_return_code not in (0, 255) or native_accepted_packets is None or child_wall_seconds is None:
        status = "failed"
        reason = f"booksim_incomplete_evidence:return_code={native_return_code}"
    else:
        status = "completed"
        reason = "BookSim2 latency-mode native termination completed with an exact accepted-packet denominator."
    if "Too many sample periods needed to converge" in stdout:
        native_termination = "unstable_max_samples"
    elif "Draining all recorded packets" in stdout:
        native_termination = "converged_and_drained"
    else:
        native_termination = "native_completed_without_convergence_marker"

    manager_metrics = {
        "process_wall_seconds": child_wall_seconds,
        "wsl_envelope_wall_seconds": envelope_wall,
        "peak_working_set_bytes": child_peak_bytes,
        "timeout_seconds": timeout_seconds,
        "memory_limit_bytes": memory_limit_bytes or None,
        "return_code": native_return_code,
        "measurement_source": "wsl_native_gnu_time_v",
        "host": {
            "platform": platform.platform(),
            "processor": platform.processor(),
            "python": sys.version.split()[0],
            "physical_memory_bytes": total_physical_memory_bytes(),
            "git_commit": current_git_commit(),
        },
    }
    metrics = {
        "target_packet_count": target_packets,
        "native_accepted_packets": native_accepted_packets,
        "target_minus_native_accepted_packets": (
            target_packets - native_accepted_packets if native_accepted_packets is not None else None
        ),
        "target_exact_match": native_accepted_packets == target_packets if native_accepted_packets is not None else None,
        "denominator_kind": "native_accepted_packets_exact_pair_stats",
        "denominator_value": native_accepted_packets,
        "ratio_eligible": False,
        "sample_period_cycles": sample_period,
        "packet_latency_average": final_float(stdout, "Packet latency average"),
        "injected_packet_rate_average": final_float(stdout, "Injected packet rate average"),
        "accepted_packet_rate_average": final_float(stdout, "Accepted packet rate average"),
        "booksim_child_wall_seconds": child_wall_seconds,
        "booksim_child_peak_rss_bytes": child_peak_bytes,
        "wsl_envelope_wall_seconds": envelope_wall,
        "native_termination": native_termination,
    }
    return {
        "status": status,
        "reason": reason,
        "completed_utc": utc_now(),
        "measurement_kind": "specialist_tool_runtime_context",
        "scenario": candidate["scenario"],
        "candidate_id": candidate_id,
        "config_hash": candidate["config_hash"],
        "parameters": parameters,
        "metrics": metrics,
        "manager_metrics": manager_metrics,
        "provenance": {
            "manager_version": MANAGER_VERSION,
            "provider": candidate["provider"],
            "tool": "BookSim2",
            "tool_git_commit": tool_git_commit,
            "binary_sha256": binary_sha256,
            "wsl_distro": BOOKSIM_WSL_DISTRO,
            "wsl_native_binary": BOOKSIM_WSL_BINARY,
            "wsl_native_staging_directory": native_dir,
            "config_sha256": sha256_text(config),
            "native_command": native_command,
            "native_return_code": native_return_code,
            "native_return_code_contract": "255=converged Run success; 0=unstable native termination; 124=timeout",
        },
        "raw_evidence": {
            name: str((evidence_dir / name).resolve()) for name in evidence
        },
        "raw_evidence_sha256": {
            name: sha256_file(evidence_dir / name) for name in evidence
        },
        "limitations": [
            "BookSim2 latency-mode native termination controls the measured duration and accepted packet count.",
            "The requested packet_count only scales sample_period; exact target equality is not assumed.",
            "The exact denominator is the sum of native pair_sent accepted-packet samples; no STAGE/BookSim wall-time ratio is eligible.",
            "GNU /usr/bin/time -v measures the native BookSim2 child; WSL launch and evidence retrieval are reported separately.",
        ],
    }


def failure_payload(candidate: dict[str, Any], status: str, reason: str, manager_metrics: dict[str, Any]) -> dict[str, Any]:
    kind = "stage_reviewer_scaling_runtime" if candidate["provider"] == "stage_reviewer_scaling" else "specialist_tool_runtime_context"
    return {
        "status": status,
        "reason": reason,
        "completed_utc": utc_now(),
        "measurement_kind": kind,
        "scenario": candidate["scenario"],
        "candidate_id": candidate["candidate_id"],
        "config_hash": candidate["config_hash"],
        "parameters": candidate["parameters"],
        "manager_metrics": manager_metrics,
        "provenance": {"manager_version": MANAGER_VERSION, "provider": candidate["provider"]},
    }


def execute_candidate(candidate: dict[str, Any], bundle: Path, runner_dll: Path) -> dict[str, Any]:
    candidate_id = candidate["candidate_id"]
    raw_path = bundle / "raw" / f"{candidate_id}.json"
    input_path = bundle / "manifests" / "inputs" / f"{candidate_id}.json"
    log_path = bundle / "logs" / f"{candidate_id}.log"
    input_payload = {
        "scenario": candidate["scenario"],
        "candidate_id": candidate_id,
        "config_hash": candidate["config_hash"],
        "git_commit": current_git_commit(),
        "resolved": {"parameters": candidate["parameters"]},
    }
    atomic_json(input_path, input_payload)

    if candidate["provider"] == "booksim2_reviewer_context":
        try:
            payload = execute_booksim2_candidate(candidate, bundle)
        except (OSError, RuntimeError, ValueError, subprocess.SubprocessError) as exc:
            payload = failure_payload(candidate, "failed", f"booksim_adapter_error:{type(exc).__name__}:{exc}", {
                "process_wall_seconds": None,
                "peak_working_set_bytes": None,
                "measurement_source": "adapter_failed_before_complete_evidence",
            })
        atomic_json(raw_path, payload)
        return payload
    if candidate["provider"] != "stage_reviewer_scaling":
        reason = f"provider_not_implemented:{candidate['provider']}"
        payload = failure_payload(candidate, "skipped", reason, {
            "process_wall_seconds": None,
            "peak_working_set_bytes": None,
            "measurement_source": "none",
        })
        atomic_json(raw_path, payload)
        return payload
    if not runner_dll.is_file():
        reason = f"runner_dll_missing:{runner_dll}"
        payload = failure_payload(candidate, "skipped", reason, {
            "process_wall_seconds": None,
            "peak_working_set_bytes": None,
            "measurement_source": "none",
        })
        atomic_json(raw_path, payload)
        return payload

    policy = candidate.get("policy") or {}
    timeout_seconds = float(policy.get("timeout_seconds", 1800))
    memory_limit_bytes = int(policy.get("max_peak_working_set_bytes", 0) or 0)
    command = ["dotnet", str(runner_dll), "--input", str(input_path), "--output", str(raw_path)]
    start = time.perf_counter()
    peak_working_set = 0
    status_override: str | None = None
    reason_override: str | None = None
    with log_path.open("w", encoding="utf-8") as log:
        log.write("command=" + canonical_json(command) + "\n")
        log.flush()
        process = subprocess.Popen(command, cwd=REPO_ROOT, stdout=log, stderr=subprocess.STDOUT, text=True)
        while process.poll() is None:
            current, os_peak = read_process_memory(process.pid)
            peak_working_set = max(peak_working_set, current, os_peak)
            elapsed = time.perf_counter() - start
            if memory_limit_bytes > 0 and peak_working_set > memory_limit_bytes:
                status_override = "resource_limit"
                reason_override = f"peak_working_set_exceeded:{peak_working_set}>{memory_limit_bytes}"
                process.kill()
                break
            if elapsed > timeout_seconds:
                status_override = "timeout"
                reason_override = f"process_timeout_seconds:{timeout_seconds:g}"
                process.kill()
                break
            time.sleep(0.05)
        return_code = process.wait()
        current, os_peak = read_process_memory(process.pid)
        peak_working_set = max(peak_working_set, current, os_peak)
    process_wall = time.perf_counter() - start
    manager_metrics = {
        "process_wall_seconds": process_wall,
        "peak_working_set_bytes": peak_working_set or None,
        "parent_poll_interval_seconds": 0.05,
        "timeout_seconds": timeout_seconds,
        "memory_limit_bytes": memory_limit_bytes or None,
        "return_code": return_code,
        "measurement_source": "parent_process_poll",
        "host": {
            "platform": platform.platform(),
            "processor": platform.processor(),
            "python": sys.version.split()[0],
            "physical_memory_bytes": total_physical_memory_bytes(),
            "git_commit": current_git_commit(),
        },
    }
    if status_override is not None:
        if raw_path.exists():
            shutil.move(raw_path, bundle / "failures" / f"{candidate_id}.{time.time_ns()}.partial.json")
        payload = failure_payload(candidate, status_override, reason_override or status_override, manager_metrics)
        atomic_json(raw_path, payload)
        return payload
    if return_code != 0 or not raw_path.exists():
        reason = f"runner_exit_code:{return_code}" if return_code else "runner_output_missing"
        payload = failure_payload(candidate, "failed", reason, manager_metrics)
        atomic_json(raw_path, payload)
        return payload

    try:
        payload = json.loads(raw_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        partial = bundle / "failures" / f"{candidate_id}.{time.time_ns()}.invalid.json"
        shutil.move(raw_path, partial)
        payload = failure_payload(candidate, "failed", f"invalid_runner_json:{exc}", manager_metrics)
        atomic_json(raw_path, payload)
        return payload
    payload["manager_metrics"] = manager_metrics
    metrics = payload.setdefault("metrics", {})
    metrics["process_wall_seconds"] = process_wall
    metrics["peak_working_set_bytes"] = peak_working_set or None
    scenario_wall = metrics.get("scenario_wall_seconds")
    metrics["process_envelope_overhead_seconds"] = (
        max(0.0, process_wall - float(scenario_wall)) if scenario_wall is not None else None
    )
    atomic_json(raw_path, payload)
    if payload.get("status") not in TERMINAL_STATUSES:
        invalid_status = payload.get("status")
        payload["status"] = "failed"
        payload["reason"] = f"invalid_terminal_status:{invalid_status}"
        atomic_json(raw_path, payload)
    return payload


def run_command(plan_path: Path, bundle_override: Path | None, runner_dll: Path, resume: bool) -> int:
    bundle, candidates = plan_command(plan_path, bundle_override)
    checkpoint_path = bundle / "manifests" / "p0_checkpoint.json"
    if checkpoint_path.exists():
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
    else:
        checkpoint = {
            "schema_version": "aspg-reviewer-p0-checkpoint-1.0",
            "manager_version": MANAGER_VERSION,
            "created_utc": utc_now(),
            "candidates": {},
        }
    for candidate in candidates:
        candidate_id = candidate["candidate_id"]
        prior = checkpoint["candidates"].get(candidate_id, {})
        raw_path = bundle / "raw" / f"{candidate_id}.json"
        if resume and prior.get("status") in TERMINAL_STATUSES and raw_path.exists():
            print(f"resume-skip {candidate_id} status={prior['status']}")
            continue
        checkpoint["candidates"][candidate_id] = {
            "status": "running",
            "started_utc": utc_now(),
            "attempt": int(prior.get("attempt", 0)) + 1,
            "config_hash": candidate["config_hash"],
        }
        checkpoint["updated_utc"] = utc_now()
        atomic_json(checkpoint_path, checkpoint)
        payload = execute_candidate(candidate, bundle, runner_dll)
        checkpoint["candidates"][candidate_id].update({
            "status": payload.get("status", "failed"),
            "reason": payload.get("reason"),
            "finished_utc": utc_now(),
            "raw_path": str(raw_path),
        })
        checkpoint["updated_utc"] = utc_now()
        atomic_json(checkpoint_path, checkpoint)
        print(f"{candidate_id} status={payload.get('status')} reason={payload.get('reason', '')}")
    counts: dict[str, int] = {}
    for row in checkpoint["candidates"].values():
        counts[row.get("status", "unknown")] = counts.get(row.get("status", "unknown"), 0) + 1
    print("status_counts=" + canonical_json(counts))
    return 0 if not any(name in counts for name in ("failed",)) else 2


def read_raw(bundle: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for path in sorted((bundle / "raw").glob("p0-*.json")):
        try:
            value = json.loads(path.read_text(encoding="utf-8"))
            if isinstance(value, dict):
                value["_raw_path"] = str(path)
                rows.append(value)
        except (json.JSONDecodeError, OSError):
            rows.append({"status": "failed", "reason": "analysis_invalid_json", "_raw_path": str(path)})
    return rows


def nested(row: dict[str, Any], group: str, name: str) -> Any:
    value = row.get(group)
    return value.get(name) if isinstance(value, dict) else None


def write_csv(path: Path, fieldnames: list[str], rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def analyze_command(bundle: Path) -> int:
    ensure_bundle(bundle)
    raw = read_raw(bundle)
    stage_raw = [row for row in raw if row.get("measurement_kind") == "stage_reviewer_scaling_runtime"]
    scalability_fields = [
        "candidate_id", "status", "reason", "mesh_dimension", "packet_count", "seed", "repeat", "trace_mode",
        "process_wall_seconds", "process_startup_seconds", "graph_build_seconds", "graph_validation_seconds",
        "compile_seconds", "simulation_wall_seconds", "simulation_core_seconds", "trace_persist_seconds",
        "trace_compression_seconds", "simulated_cycles", "simulated_cycles_per_second",
        "completed_packets_per_second", "events_per_second", "peak_working_set_bytes", "peak_managed_bytes",
        "raw_trace_bytes", "compressed_trace_bytes", "requested_packets", "injected_packets", "completed_packets",
        "event_count", "canonical_delivery_sha256", "raw_trace_sha256", "topology_canonical_sha256", "raw_path",
    ]
    scalability_rows: list[dict[str, Any]] = []
    for row in stage_raw:
        parameters = row.get("parameters") if isinstance(row.get("parameters"), dict) else {}
        metrics = row.get("metrics") if isinstance(row.get("metrics"), dict) else {}
        hashes = row.get("hashes") if isinstance(row.get("hashes"), dict) else {}
        scalability_rows.append({
            "candidate_id": row.get("candidate_id"), "status": row.get("status"), "reason": row.get("reason"),
            "mesh_dimension": parameters.get("mesh_dimension"), "packet_count": parameters.get("packet_count"),
            "seed": parameters.get("seed"), "repeat": parameters.get("repeat"), "trace_mode": parameters.get("trace_mode"),
            **{name: metrics.get(name) for name in scalability_fields if name in metrics},
            "canonical_delivery_sha256": hashes.get("canonical_delivery_sha256"),
            "raw_trace_sha256": hashes.get("raw_trace_sha256"),
            "topology_canonical_sha256": hashes.get("topology_canonical_sha256"),
            "raw_path": row.get("_raw_path"),
        })
    scalability_rows.sort(key=lambda item: (
        item.get("mesh_dimension") or 0, item.get("packet_count") or 0, item.get("seed") or 0,
        item.get("repeat") or 0, str(item.get("trace_mode"))))
    write_csv(bundle / "summary" / "stage_scalability.csv", scalability_fields, scalability_rows)

    paired: dict[tuple[Any, ...], dict[str, dict[str, Any]]] = {}
    for row in scalability_rows:
        key = (row.get("mesh_dimension"), row.get("packet_count"), row.get("seed"), row.get("repeat"))
        paired.setdefault(key, {})[str(row.get("trace_mode"))] = row
    overhead_fields = [
        "mesh_dimension", "packet_count", "seed", "repeat", "metrics_status", "full_status",
        "metrics_simulation_wall_seconds", "full_simulation_wall_seconds", "trace_on_slowdown",
        "event_count", "raw_trace_bytes", "compressed_trace_bytes", "bytes_per_event", "compression_ratio",
        "delivery_hash_equal", "metrics_candidate_id", "full_candidate_id",
    ]
    overhead_rows: list[dict[str, Any]] = []
    for key, modes in sorted(paired.items(), key=lambda item: tuple(value or 0 for value in item[0])):
        off = modes.get("metrics_only") or modes.get("metrics-only")
        full = modes.get("full") or modes.get("full_provenance") or modes.get("full-provenance")
        off_seconds = off.get("simulation_wall_seconds") if off else None
        full_seconds = full.get("simulation_wall_seconds") if full else None
        events = full.get("event_count") if full else None
        raw_bytes = full.get("raw_trace_bytes") if full else None
        compressed = full.get("compressed_trace_bytes") if full else None
        overhead_rows.append({
            "mesh_dimension": key[0], "packet_count": key[1], "seed": key[2], "repeat": key[3],
            "metrics_status": off.get("status") if off else "missing", "full_status": full.get("status") if full else "missing",
            "metrics_simulation_wall_seconds": off_seconds, "full_simulation_wall_seconds": full_seconds,
            "trace_on_slowdown": (full_seconds / off_seconds) if off_seconds and full_seconds else None,
            "event_count": events, "raw_trace_bytes": raw_bytes, "compressed_trace_bytes": compressed,
            "bytes_per_event": (raw_bytes / events) if raw_bytes is not None and events else None,
            "compression_ratio": (compressed / raw_bytes) if raw_bytes and compressed is not None else None,
            "delivery_hash_equal": (
                off.get("canonical_delivery_sha256") == full.get("canonical_delivery_sha256")
                if off and full and off.get("canonical_delivery_sha256") and full.get("canonical_delivery_sha256") else None
            ),
            "metrics_candidate_id": off.get("candidate_id") if off else None,
            "full_candidate_id": full.get("candidate_id") if full else None,
        })
    write_csv(bundle / "summary" / "stage_trace_overhead.csv", overhead_fields, overhead_rows)

    compile_fields = [
        "candidate_id", "status", "mesh_dimension", "packet_count", "seed", "repeat", "trace_mode",
        "graph_build_seconds", "graph_validation_seconds", "compile_seconds", "peak_working_set_bytes",
        "topology_canonical_sha256",
    ]
    write_csv(bundle / "summary" / "stage_compile_scaling.csv", compile_fields, scalability_rows)

    specialist_fields = [
        "candidate_id", "tool", "case", "traffic", "injection_rate", "packet_count", "repeat", "status", "reason",
        "process_wall_seconds", "wsl_envelope_wall_seconds", "peak_working_set_bytes",
        "denominator_kind", "denominator_value", "target_minus_native_accepted_packets", "target_exact_match",
        "ratio_eligible", "sample_period_cycles", "packet_latency_average", "injected_packet_rate_average",
        "accepted_packet_rate_average", "native_termination", "native_return_code", "raw_path",
    ]
    specialist_rows: list[dict[str, Any]] = []
    for row in raw:
        if row.get("measurement_kind") != "specialist_tool_runtime_context":
            continue
        parameters = row.get("parameters") if isinstance(row.get("parameters"), dict) else {}
        manager = row.get("manager_metrics") if isinstance(row.get("manager_metrics"), dict) else {}
        metrics = row.get("metrics") if isinstance(row.get("metrics"), dict) else {}
        provenance = row.get("provenance") if isinstance(row.get("provenance"), dict) else {}
        specialist_rows.append({
            "candidate_id": row.get("candidate_id"), "tool": parameters.get("tool"), "case": parameters.get("case"),
            "traffic": parameters.get("traffic"), "injection_rate": parameters.get("injection_rate"),
            "packet_count": parameters.get("packet_count"), "repeat": parameters.get("repeat"),
            "status": row.get("status"), "reason": row.get("reason"),
            "process_wall_seconds": manager.get("process_wall_seconds"),
            "wsl_envelope_wall_seconds": manager.get("wsl_envelope_wall_seconds"),
            "peak_working_set_bytes": manager.get("peak_working_set_bytes"),
            "denominator_kind": metrics.get("denominator_kind") or parameters.get("denominator_kind"),
            "denominator_value": metrics.get("denominator_value"),
            "target_minus_native_accepted_packets": metrics.get("target_minus_native_accepted_packets"),
            "target_exact_match": metrics.get("target_exact_match"),
            "ratio_eligible": metrics.get("ratio_eligible"),
            "sample_period_cycles": metrics.get("sample_period_cycles"),
            "packet_latency_average": metrics.get("packet_latency_average"),
            "injected_packet_rate_average": metrics.get("injected_packet_rate_average"),
            "accepted_packet_rate_average": metrics.get("accepted_packet_rate_average"),
            "native_termination": metrics.get("native_termination"),
            "native_return_code": provenance.get("native_return_code"),
            "raw_path": row.get("_raw_path"),
        })
    if not specialist_rows:
        specialist_rows = [
            {"tool": "BookSim2", "status": "not_run", "reason": "no_specialist_raw_records; no numeric context fabricated"},
            {"tool": "SCALE-Sim", "status": "not_run", "reason": "no_specialist_raw_records; no numeric context fabricated"},
        ]
    write_csv(bundle / "summary" / "specialist_tool_runtime_context.csv", specialist_fields, specialist_rows)

    write_compact_table(bundle, overhead_rows)
    write_scalability_figure(bundle, scalability_rows)
    analysis_manifest = {
        "schema_version": "aspg-reviewer-p0-analysis-1.0",
        "manager_version": MANAGER_VERSION,
        "generated_utc": utc_now(),
        "raw_record_count": len(raw),
        "stage_record_count": len(stage_raw),
        "specialist_record_count": len(specialist_rows),
        "outputs": [
            "summary/stage_scalability.csv", "summary/stage_trace_overhead.csv",
            "summary/stage_compile_scaling.csv", "summary/specialist_tool_runtime_context.csv",
            "summary/table_reviewer_scalability.md",
        ],
    }
    atomic_json(bundle / "manifests" / "p0_analysis_manifest.json", analysis_manifest)
    print(f"analyzed raw={len(raw)} stage={len(stage_raw)} bundle={bundle}")
    return 0


def write_compact_table(bundle: Path, overhead_rows: list[dict[str, Any]]) -> None:
    selected = [row for row in overhead_rows if row.get("mesh_dimension") in (4, 8, 16)]
    lines = [
        "# Reviewer scalability compact table", "",
        "Only completed paired runs are summarized. Missing, timeout, resource-limit, skipped, or failed points remain in the CSV.", "",
        "| Mesh | Packets | Off wall (s) | Full wall (s) | Slowdown | Raw MB | Bytes/event | Hash equal |",
        "|---:|---:|---:|---:|---:|---:|---:|:---:|",
    ]
    grouped: dict[tuple[Any, Any], list[dict[str, Any]]] = {}
    for row in selected:
        if row.get("metrics_status") == "completed" and row.get("full_status") == "completed":
            grouped.setdefault((row.get("mesh_dimension"), row.get("packet_count")), []).append(row)
    for key in sorted(grouped):
        rows = grouped[key]
        med = lambda name: median(float(row[name]) for row in rows if row.get(name) is not None)
        lines.append(
            f"| {key[0]}x{key[0]} | {key[1]} | {med('metrics_simulation_wall_seconds'):.4g} | "
            f"{med('full_simulation_wall_seconds'):.4g} | {med('trace_on_slowdown'):.3g}x | "
            f"{med('raw_trace_bytes') / 1_000_000:.3g} | {med('bytes_per_event'):.3g} | "
            f"{'yes' if all(row.get('delivery_hash_equal') for row in rows) else 'no'} |"
        )
    if not grouped:
        lines.append("| - | - | - | - | - | - | - | pending measured pairs |")
    (bundle / "summary" / "table_reviewer_scalability.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_scalability_figure(bundle: Path, rows: list[dict[str, Any]]) -> None:
    completed = [row for row in rows if row.get("status") == "completed" and row.get("simulation_wall_seconds")]
    pending = bundle / "figures" / "fig_reviewer_scalability.pending.md"
    if not completed:
        pending.write_text("No completed measured STAGE P0 points; figure generation remains pending and no values were fabricated.\n", encoding="utf-8")
        return
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        pending.write_text("Completed measurements exist, but matplotlib is unavailable; install it before evidence figure generation.\n", encoding="utf-8")
        return
    figure, axis = plt.subplots(figsize=(6.2, 3.8))
    for trace_mode, marker in (("metrics_only", "o"), ("full", "s")):
        mode_rows = [row for row in completed if row.get("trace_mode") == trace_mode]
        for dimension in sorted({row.get("mesh_dimension") for row in mode_rows}):
            points = []
            for packet_count in sorted({row.get("packet_count") for row in mode_rows if row.get("mesh_dimension") == dimension}):
                values = [float(row["simulation_wall_seconds"]) for row in mode_rows
                          if row.get("mesh_dimension") == dimension and row.get("packet_count") == packet_count]
                if values:
                    points.append((packet_count, median(values)))
            if points:
                axis.plot([point[0] for point in points], [point[1] for point in points], marker=marker,
                          label=f"{dimension}x{dimension} {trace_mode}")
    axis.set_xscale("log")
    axis.set_yscale("log")
    axis.set_xlabel("Exact packet count")
    axis.set_ylabel("Simulation wall time (s)")
    axis.grid(True, which="both", alpha=0.25)
    axis.legend(fontsize=7, ncol=2)
    figure.tight_layout()
    figure.savefig(bundle / "figures" / "fig_reviewer_scalability.pdf")
    figure.savefig(bundle / "figures" / "fig_reviewer_scalability.png", dpi=180)
    plt.close(figure)
    pending.unlink(missing_ok=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)
    plan_parser = subcommands.add_parser("plan", help="expand config and freeze stable candidate ids")
    run_parser = subcommands.add_parser("run", help="execute each candidate in an independently measured process")
    smoke_parser = subcommands.add_parser("smoke", help="run the preregistered 4x4 x 1e4 smoke plan")
    analyze_parser = subcommands.add_parser("analyze", help="create tidy CSV, compact table, and measured figure")
    for item in (plan_parser, run_parser):
        item.add_argument("--plan", type=Path, required=True)
        item.add_argument("--bundle", type=Path)
    run_parser.add_argument("--runner-dll", type=Path, default=DEFAULT_RUNNER_DLL)
    run_parser.add_argument("--no-resume", action="store_true")
    smoke_parser.add_argument("--plan", type=Path, default=DEFAULT_SMOKE_PLAN)
    smoke_parser.add_argument("--bundle", type=Path)
    smoke_parser.add_argument("--runner-dll", type=Path, default=DEFAULT_RUNNER_DLL)
    smoke_parser.add_argument("--no-resume", action="store_true")
    analyze_parser.add_argument("--bundle", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.command == "plan":
        plan_command(args.plan.resolve(), args.bundle.resolve() if args.bundle else None)
        return 0
    if args.command in ("run", "smoke"):
        return run_command(
            args.plan.resolve(), args.bundle.resolve() if args.bundle else None,
            args.runner_dll.resolve(), resume=not args.no_resume)
    if args.command == "analyze":
        return analyze_command(args.bundle.resolve())
    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
