#!/usr/bin/env python3
"""Run one finite canonical CNN packet trace through an isolated BookSim2 adapter."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import os
import platform
import shlex
import socket
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO = Path(__file__).resolve().parents[3]
PATCH = REPO / "experiments/aspdac/external_patches/booksim2_cnn_trace.patch"
BOOKSIM_COMMIT = "28f43299f1706a3160ffac721ca461d74eb6e618"
WSL_DISTRO = "Ubuntu-24.04"
WSL_BASE_REPO = "/opt/stage-baselines/tools/booksim2"
WSL_BUILD_PARENT = "/opt/stage-baselines/tools"
WSL_RUN_PARENT = "/opt/stage-baselines/results/aspdac_cnn_trace"
TRACE_HEADER = [
    "packet_id", "release_cycle", "source", "destination", "flits",
    "traffic_class", "layer_id", "tensor_role", "payload_bits",
]
DELIVERY_HEADER = [
    "packet_id", "pid", "release_cycle", "injection_cycle", "arrival_cycle",
    "source", "destination", "hops", "packet_latency", "network_latency",
    "layer_id", "tensor_role",
]


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_json(value: Any) -> str:
    return hashlib.sha256(canonical_json(value).encode("utf-8")).hexdigest()


def atomic_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def repo_path(value: str) -> Path:
    path = Path(value)
    return path.resolve() if path.is_absolute() else (REPO / path).resolve()


def relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO).as_posix()
    except ValueError:
        return str(path.resolve())


def run_wsl(command: str, timeout: int = 1800) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["wsl", "-d", WSL_DISTRO, "--", "bash", "-lc", command],
        cwd=REPO,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        timeout=timeout,
        check=False,
    )


def write_wsl_file(source: Path, destination: str, timeout: int = 120) -> None:
    """Stream overlay bytes through stdin; fresh UNC paths and /mnt/f are unreliable here."""
    parent = destination.rsplit("/", 1)[0]
    completed = subprocess.run(
        [
            "wsl", "-d", WSL_DISTRO, "--", "bash", "-lc",
            f"set -e; mkdir -p {shlex.quote(parent)}; cat > {shlex.quote(destination)}",
        ],
        cwd=REPO,
        input=source.read_bytes(),
        capture_output=True,
        timeout=timeout,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"Could not stream {source} to WSL {destination}: "
            + completed.stderr.decode("utf-8", errors="replace")
        )


def read_wsl_file(source: str, destination: Path, timeout: int = 120) -> None:
    completed = subprocess.run(
        ["wsl", "-d", WSL_DISTRO, "--", "bash", "-lc", f"cat {shlex.quote(source)}"],
        cwd=REPO,
        capture_output=True,
        timeout=timeout,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"Could not stream WSL {source} to {destination}: "
            + completed.stderr.decode("utf-8", errors="replace")
        )
    destination.write_bytes(completed.stdout)


def wsl_sha256(path: str) -> str:
    completed = run_wsl(f"sha256sum {shlex.quote(path)}")
    if completed.returncode != 0:
        raise RuntimeError(f"Could not hash WSL file {path}: {completed.stderr}")
    return completed.stdout.split()[0]


def validate_trace(path: Path, expected_sha256: str, expected_count: int) -> list[dict[str, str]]:
    if sha256_file(path) != expected_sha256:
        raise ValueError(f"Trace SHA-256 mismatch for {path}")
    with path.open(newline="", encoding="utf-8") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames != TRACE_HEADER:
            raise ValueError(f"Unexpected trace header: {reader.fieldnames}")
        rows = list(reader)
    if len(rows) != expected_count:
        raise ValueError(f"Trace packet count {len(rows)} != registered {expected_count}")
    seen: set[str] = set()
    previous: tuple[int, int, str] | None = None
    for row in rows:
        key = (int(row["release_cycle"]), int(row["source"]), row["packet_id"])
        if previous is not None and key < previous:
            raise ValueError("Trace must be sorted by release_cycle,source,packet_id")
        previous = key
        source, destination = int(row["source"]), int(row["destination"])
        if not (0 <= source < 16 and 0 <= destination < 16 and source != destination):
            raise ValueError(f"Endpoint contract violation: {row}")
        if row["flits"] != "1" or row["traffic_class"] != "0" or row["payload_bits"] != "128":
            raise ValueError(f"Frozen one-flit trace contract violation: {row}")
        if not row["packet_id"] or row["packet_id"] in seen:
            raise ValueError(f"Duplicate/empty packet_id: {row['packet_id']}")
        seen.add(row["packet_id"])
    return rows


def raw_directory(output: Path, candidate_id: str) -> Path:
    if output.parent.name == "runner_outputs" and output.parent.parent.name == "manifests":
        bundle = output.parent.parent.parent
        return bundle / "raw/external_booksim_cnn_trace" / candidate_id
    return output.parent / f"{output.stem}.evidence"


def ensure_build(raw: Path) -> tuple[str, dict[str, Any]]:
    patch_sha = sha256_file(PATCH)
    build_name = f"booksim2-cnn-trace-stream-{patch_sha[:12]}"
    build_dir = f"{WSL_BUILD_PARENT}/{build_name}"
    metadata_wsl = f"{build_dir}/adapter_build.json"
    binary_wsl = f"{build_dir}/src/booksim"
    local_metadata = raw / "adapter_build.json"
    cache_ready = run_wsl(
        f"test -f {shlex.quote(metadata_wsl)} -a -x {shlex.quote(binary_wsl)}"
    ).returncode == 0
    if cache_ready:
        read_wsl_file(metadata_wsl, local_metadata)
        metadata = json.loads(local_metadata.read_text(encoding="utf-8"))
        if metadata.get("base_commit") == BOOKSIM_COMMIT and metadata.get("patch_sha256") == patch_sha:
            metadata["binary_sha256"] = wsl_sha256(binary_wsl)
            metadata["reused"] = True
            return build_dir, metadata

    if run_wsl(f"test -e {shlex.quote(build_dir)}").returncode == 0:
        build_name += f"-retry-{int(time.time())}"
        build_dir = f"{WSL_BUILD_PARENT}/{build_name}"
        metadata_wsl = f"{build_dir}/adapter_build.json"
        binary_wsl = f"{build_dir}/src/booksim"

    clone = run_wsl(
        "set -e; git clone --quiet --no-hardlinks "
        f"{shlex.quote(WSL_BASE_REPO)} {shlex.quote(build_dir)}; "
        f"git -C {shlex.quote(build_dir)} checkout --quiet --detach {BOOKSIM_COMMIT}"
    )
    build_log = raw / "adapter_build.log"
    build_log.write_text(clone.stdout + "\n--- STDERR ---\n" + clone.stderr, encoding="utf-8")
    if clone.returncode != 0:
        raise RuntimeError(f"Isolated BookSim2 clone failed; see {build_log}")

    # /mnt/f does not expose the Codex overlay; stdin streaming is deterministic.
    patch_destination = f"{build_dir}/booksim2_cnn_trace.patch"
    write_wsl_file(PATCH, patch_destination)
    applied = run_wsl(
        f"set -e; git -C {shlex.quote(build_dir)} apply --check booksim2_cnn_trace.patch; "
        f"git -C {shlex.quote(build_dir)} apply booksim2_cnn_trace.patch; "
        f"make -C {shlex.quote(build_dir + '/src')} -j1"
    )
    with build_log.open("a", encoding="utf-8") as stream:
        stream.write("\n--- APPLY/BUILD STDOUT ---\n" + applied.stdout)
        stream.write("\n--- APPLY/BUILD STDERR ---\n" + applied.stderr)
    if applied.returncode != 0 or run_wsl(f"test -x {shlex.quote(binary_wsl)}").returncode != 0:
        raise RuntimeError(f"BookSim2 adapter build failed; see {build_log}")
    metadata = {
        "schema_version": "aspg-booksim2-trace-adapter-build-1.0",
        "created_utc": utc_now(),
        "base_commit": BOOKSIM_COMMIT,
        "base_repo": WSL_BASE_REPO,
        "isolated_build_dir": build_dir,
        "patch_sha256": patch_sha,
        "binary_sha256": wsl_sha256(binary_wsl),
        "build_log_sha256": sha256_file(build_log),
        "reused": False,
    }
    local_metadata.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    write_wsl_file(local_metadata, metadata_wsl)
    return build_dir, metadata


def write_config(path: Path, trace_wsl: str, delivery_wsl: str, drain_cycles: int, seed: int) -> None:
    path.write_text(
        "\n".join(
            [
                "// BookSim2 fixed-commit finite CNN trace adapter; network kernel is unchanged.",
                "topology = mesh;", "k = 4;", "n = 2;", "routing_function = dim_order;",
                "num_vcs = 1;", "vc_buf_size = 16;", "wait_for_tail_credit = 0;",
                "traffic = uniform;", "injection_rate = 0.001;", "packet_size = 1;",
                f"seed = {seed};", "classes = 1;", "sim_count = 1;", "sim_type = trace;",
                f"trace_file = {trace_wsl};", f"trace_results_file = {delivery_wsl};",
                f"trace_drain_cycles = {drain_cycles};", "include_queuing = 1;",
            ]
        )
        + "\n",
        encoding="utf-8",
        newline="\n",
    )


def nearest_rank_p95(values: list[int]) -> float:
    ordered = sorted(values)
    return float(ordered[max(0, math.ceil(0.95 * len(ordered)) - 1)])


def parse_delivery(path: Path) -> tuple[list[dict[str, str]], dict[str, Any]]:
    with path.open(newline="", encoding="utf-8") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames != DELIVERY_HEADER:
            raise ValueError(f"Unexpected delivery header: {reader.fieldnames}")
        rows = list(reader)
    if not rows:
        raise ValueError("BookSim2 emitted no retired packets")
    latencies = [int(row["packet_latency"]) for row in rows]
    first_release = min(int(row["release_cycle"]) for row in rows)
    last_arrival = max(int(row["arrival_cycle"]) for row in rows)
    makespan = last_arrival - first_release + 1
    canonical_rows = [
        {
            key: row[key]
            for key in (
                "packet_id", "release_cycle", "injection_cycle", "arrival_cycle",
                "source", "destination", "hops", "packet_latency", "network_latency",
            )
        }
        for row in sorted(rows, key=lambda item: item["packet_id"])
    ]
    return rows, {
        "network_makespan_cycles": makespan,
        "packet_latency_avg": sum(latencies) / len(latencies),
        "packet_latency_p95": nearest_rank_p95(latencies),
        "throughput_packets_per_cycle": len(rows) / makespan,
        "canonical_delivery_trace_hash": sha256_json(canonical_rows),
    }


def execute(input_path: Path, output_path: Path) -> dict[str, Any]:
    candidate = json.loads(input_path.read_text(encoding="utf-8"))
    if candidate.get("scenario") != "vbs_cnn_trace":
        raise ValueError(f"Unsupported scenario: {candidate.get('scenario')}")
    candidate_id = str(candidate["candidate_id"])
    parameters = candidate["resolved"]["parameters"]
    trace = repo_path(str(parameters["trace_csv_path"]))
    expected_sha = str(parameters["trace_sha256"])
    expected_count = int(parameters["packet_count"])
    drain_cycles = int(parameters["drain_cycles"])
    seed = int(parameters.get("seed", 40))
    trace_rows = validate_trace(trace, expected_sha, expected_count)

    raw = raw_directory(output_path, candidate_id)
    raw.mkdir(parents=True, exist_ok=True)
    build_dir, build = ensure_build(raw)
    run_dir = f"{WSL_RUN_PARENT}/{candidate_id}"
    prepared = run_wsl(f"mkdir -p {shlex.quote(run_dir)}")
    if prepared.returncode != 0:
        raise RuntimeError(f"Could not create WSL run directory: {prepared.stderr}")
    trace_wsl = f"{run_dir}/trace.csv"
    config_wsl = f"{run_dir}/booksim.cfg"
    delivery_wsl = f"{run_dir}/packet_delivery.csv"
    write_wsl_file(trace, trace_wsl)
    config_copy = raw / "booksim.cfg"
    write_config(config_copy, trace_wsl, delivery_wsl, drain_cycles, seed)
    write_wsl_file(config_copy, config_wsl)

    started = time.monotonic()
    completed = run_wsl(
        f"cd {shlex.quote(run_dir)} && {shlex.quote(build_dir + '/src/booksim')} booksim.cfg",
        timeout=1800,
    )
    elapsed = time.monotonic() - started
    log = raw / "booksim.log"
    log.write_text(completed.stdout + "\n--- STDERR ---\n" + completed.stderr, encoding="utf-8")
    if run_wsl(f"test -f {shlex.quote(delivery_wsl)}").returncode != 0:
        raise RuntimeError(f"BookSim2 produced no delivery CSV; see {log}")
    delivery_copy = raw / "packet_delivery.csv"
    read_wsl_file(delivery_wsl, delivery_copy)
    delivered_rows, derived = parse_delivery(delivery_copy)
    delivered = len(delivered_rows)
    # This fixed BookSim2 main returns -1 (Windows/WSL code 255) when Run()
    # succeeds and 0 when Run() reports unstable.  Completion is therefore
    # authenticated by the finite adapter's retired count and timeout marker,
    # while the legacy process code is retained as provenance only.
    timeout = delivered != expected_count or "TRACE_TIMEOUT" in completed.stderr
    total_cycles_match = None
    for line in completed.stdout.splitlines():
        if line.startswith("TRACE_TOTAL_CYCLES = "):
            total_cycles_match = int(line.rsplit(" ", 1)[-1])
    total_cycles = total_cycles_match if total_cycles_match is not None else derived["network_makespan_cycles"]
    shared_metrics = {
        "trace_sha256": expected_sha,
        "offered_packets": len(trace_rows),
        "injected_packets": delivered,
        "delivered_packets": delivered,
        "undrained_packets": len(trace_rows) - delivered,
        "total_cycles": total_cycles,
        **derived,
        "timeout": timeout,
    }
    shared_metrics["canonical_metrics_hash"] = sha256_json(shared_metrics)
    result = {
        "status": "completed" if not timeout else "failed",
        "completed_utc": utc_now(),
        "measurement_kind": "booksim2_trace_packet_cycle_runtime",
        "metrics": shared_metrics,
        "provenance": {
            "tool": "BookSim2",
            "tool_git_commit": BOOKSIM_COMMIT,
            "adapter_patch_sha256": build["patch_sha256"],
            "binary_sha256": build["binary_sha256"],
            "config_sha256": sha256_file(config_copy),
            "trace_sha256": expected_sha,
            "log_sha256": sha256_file(log),
            "packet_delivery_sha256": sha256_file(delivery_copy),
            "topology_contract": "4x4_mesh_16_router_16_endpoint_xy_1vc_16flits_128b_packet",
            "input_adapter": "finite_csv_trace_v1",
            "base_network_kernel_unchanged": True,
            "isolated_build_dir": build_dir,
            "wsl_run_dir": run_dir,
            "host": socket.gethostname(),
            "platform": platform.platform(),
            "python": sys.version.split()[0],
            "elapsed_seconds": elapsed,
            "process_return_code": completed.returncode,
            "process_return_code_contract": "fixed BookSim2 main returns 255 when TrafficManager::Run succeeds",
        },
        "raw_evidence": {
            "config": relative(config_copy),
            "log": relative(log),
            "packet_delivery": relative(delivery_copy),
            "build_log": relative(raw / "adapter_build.log") if (raw / "adapter_build.log").exists() else None,
        },
        "limitations": [
            "BookSim2 executes the matched packet transport only; it does not execute CNN arithmetic or accuracy.",
            "The finite CSV adapter changes packet injection only; BookSim2 router, VC, credit, arbitration, and routing kernels are reused.",
            "Absolute cycle equality with STAGE is not assumed because BookSim2 retains its compiled-default router pipeline.",
        ],
    }
    if timeout:
        raise RuntimeError(
            f"BookSim2 trace incomplete: delivered={delivered}/{expected_count}, return_code={completed.returncode}; see {log}"
        )
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    try:
        result = execute(args.input.resolve(), output)
        atomic_json(output, result)
        print(canonical_json({"status": result["status"], "metrics": result["metrics"]}))
        return 0
    except Exception as exception:
        failure = {
            "status": "failed",
            "completed_utc": utc_now(),
            "measurement_kind": "booksim2_trace_packet_cycle_runtime",
            "exception_type": type(exception).__name__,
            "message": str(exception),
        }
        atomic_json(output, failure)
        print(str(exception), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
