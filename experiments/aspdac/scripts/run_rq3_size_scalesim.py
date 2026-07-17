#!/usr/bin/env python3
"""Run the real installed SCALE-Sim for the Phase 10 shared size matrix.

This runner is deliberately external-tool only.  It invokes SCALE-Sim once per
matrix point so a completed point can be resumed without rerunning the others.
All topology/config inputs, logs, reports, traces, hashes, and failures are
retained for artifact review.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import platform
import shutil
import socket
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import yaml


DEFAULT_SCALESIM_REPO = Path("/opt/stage-baselines/tools/SCALE-Sim")
DEFAULT_SCALESIM_PYTHON = Path("/opt/stage-baselines/venv/bin/python")
EXPECTED_COMMIT = "9f98c4371055a54c75209c2e02b640b897550532"
FINAL_RAW_PREFIX = (
    "experiments/aspdac/results/final_20260716/raw/size_scaling_scalesim"
)
SUMMARY_FIELDS = [
    "case_id",
    "family",
    "scale",
    "M",
    "N",
    "K",
    "expected_macs",
    "status",
    "return_code",
    "elapsed_seconds",
    "total_cycles_incl_prefetch",
    "total_cycles",
    "cold_cycles",
    "warm_cycles",
    "prefetch_cycles",
    "stall_cycles",
    "overall_util_pct",
    "mapping_efficiency_pct",
    "compute_util_pct",
    "sram_ifmap_reads",
    "sram_filter_reads",
    "sram_ofmap_writes",
    "dram_ifmap_reads",
    "dram_filter_reads",
    "dram_ofmap_writes",
    "avg_ifmap_sram_bw",
    "avg_filter_sram_bw",
    "avg_ofmap_sram_bw",
    "avg_ifmap_dram_bw",
    "avg_filter_dram_bw",
    "avg_ofmap_dram_bw",
    "config_hash",
    "topology_hash",
    "mapping_hash",
    "raw_output_hash",
    "scalesim_git_commit",
    "scalesim_python",
    "command",
    "external_raw_path",
    "evidence_level",
    "comparison_boundary",
]


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_hash(value: Any) -> str:
    payload = json.dumps(
        value, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def read_csv_rows(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return [
            {
                str(key).strip(): str(value).strip()
                for key, value in row.items()
                if key is not None and value is not None
            }
            for row in csv.DictReader(handle)
        ]


def run_capture(command: list[str], cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=cwd,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def git_value(repo: Path, *args: str) -> str:
    completed = run_capture(["git", "-C", str(repo), *args])
    return completed.stdout.strip() if completed.returncode == 0 else "unavailable"


def load_matrix(path: Path) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    document = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or not isinstance(document.get("cases"), list):
        raise ValueError("matrix must contain a cases list")
    contract = document.get("tool_contracts", {}).get("scalesim")
    expected = "4x4 single-MAC weight-stationary; dense; seed 40"
    if contract != expected:
        raise ValueError(f"unexpected SCALE-Sim contract: {contract!r}")
    cases: list[dict[str, Any]] = []
    seen: set[str] = set()
    for value in document["cases"]:
        if not isinstance(value, dict):
            raise ValueError("every matrix case must be an object")
        case = dict(value)
        case_id = str(case["case_id"])
        if case_id in seen:
            raise ValueError(f"duplicate case_id: {case_id}")
        seen.add(case_id)
        for field in ("M", "N", "K", "expected_macs"):
            case[field] = int(case[field])
        if case["M"] * case["N"] * case["K"] != case["expected_macs"]:
            raise ValueError(f"expected_macs mismatch for {case_id}")
        cases.append(case)
    if len(cases) != 9:
        raise ValueError(f"expected 9 shared cases, found {len(cases)}")
    return document, cases


def resolved_config(source: Path, case_id: str) -> str:
    text = source.read_text(encoding="utf-8")
    old = "run_name = aspdac_4x4_ws_core_mnk"
    new = f"run_name = aspdac_4x4_ws_size_scaling_{case_id}"
    if old not in text:
        raise ValueError(f"frozen config does not contain {old!r}")
    result = text.replace(old, new, 1)
    required = [
        "ArrayHeight:    4",
        "ArrayWidth:     4",
        "Bandwidth : 8,8,8",
        "Dataflow : ws",
        "SparsitySupport : false",
        "RandomNumberGeneratorSeed : 40",
        "InterfaceBandwidth: USER",
    ]
    missing = [line for line in required if line not in result]
    if missing:
        raise ValueError(f"frozen config contract missing: {missing}")
    return result


def write_topology(path: Path, case: dict[str, Any]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(["Layer Name", " M", " N", " K", ""])
        writer.writerow(
            [case["case_id"], case["M"], case["N"], case["K"], ""]
        )


def report_paths(raw_output: Path, run_name: str) -> dict[str, Path]:
    report_root = raw_output / run_name
    return {
        "compute": report_root / "COMPUTE_REPORT.csv",
        "access": report_root / "DETAILED_ACCESS_REPORT.csv",
        "bandwidth": report_root / "BANDWIDTH_REPORT.csv",
        "time": report_root / "TIME_REPORT.csv",
    }


def inventory(paths: list[Path], base: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for path in sorted(paths):
        if not path.is_file():
            continue
        rows.append(
            {
                "path": path.relative_to(base).as_posix(),
                "size_bytes": path.stat().st_size,
                "sha256": sha256_file(path),
            }
        )
    return rows


def parse_metric_row(
    case: dict[str, Any],
    reports: dict[str, Path],
    provenance: dict[str, Any],
    command: list[str],
    elapsed: float,
    config_hash: str,
    topology_hash: str,
    raw_hash: str,
) -> dict[str, Any]:
    compute = read_csv_rows(reports["compute"])[0]
    access = read_csv_rows(reports["access"])[0]
    bandwidth = read_csv_rows(reports["bandwidth"])[0]
    cold = int(float(compute["Total Cycles (incl. prefetch)"]))
    warm = int(float(compute["Total Cycles"]))
    mapping_hash = canonical_hash(
        {
            "schema": "aspdac.scalesim.mapping.v1",
            "case": {
                key: case[key]
                for key in ("case_id", "M", "N", "K", "expected_macs")
            },
            "config_hash": config_hash,
            "topology_hash": topology_hash,
            "scalesim_git_commit": provenance["scalesim_git_commit"],
        }
    )
    return {
        "case_id": case["case_id"],
        "family": case["family"],
        "scale": case["scale"],
        "M": case["M"],
        "N": case["N"],
        "K": case["K"],
        "expected_macs": case["expected_macs"],
        "status": "completed",
        "return_code": 0,
        "elapsed_seconds": round(elapsed, 6),
        "total_cycles_incl_prefetch": cold,
        "total_cycles": warm,
        "cold_cycles": cold,
        "warm_cycles": warm,
        "prefetch_cycles": cold - warm,
        "stall_cycles": int(float(compute["Stall Cycles"])),
        "overall_util_pct": float(compute["Overall Util %"]),
        "mapping_efficiency_pct": float(compute["Mapping Efficiency %"]),
        "compute_util_pct": float(compute["Compute Util %"]),
        "sram_ifmap_reads": int(float(access["SRAM IFMAP Reads"])),
        "sram_filter_reads": int(float(access["SRAM Filter Reads"])),
        "sram_ofmap_writes": int(float(access["SRAM OFMAP Writes"])),
        "dram_ifmap_reads": int(float(access["DRAM IFMAP Reads"])),
        "dram_filter_reads": int(float(access["DRAM Filter Reads"])),
        "dram_ofmap_writes": int(float(access["DRAM OFMAP Writes"])),
        "avg_ifmap_sram_bw": float(bandwidth["Avg IFMAP SRAM BW"]),
        "avg_filter_sram_bw": float(bandwidth["Avg FILTER SRAM BW"]),
        "avg_ofmap_sram_bw": float(bandwidth["Avg OFMAP SRAM BW"]),
        "avg_ifmap_dram_bw": float(bandwidth["Avg IFMAP DRAM BW"]),
        "avg_filter_dram_bw": float(bandwidth["Avg FILTER DRAM BW"]),
        "avg_ofmap_dram_bw": float(bandwidth["Avg OFMAP DRAM BW"]),
        "config_hash": config_hash,
        "topology_hash": topology_hash,
        "mapping_hash": mapping_hash,
        "raw_output_hash": raw_hash,
        "scalesim_git_commit": provenance["scalesim_git_commit"],
        "scalesim_python": provenance["scalesim_python"],
        "command": " ".join(command),
        "external_raw_path": f"{FINAL_RAW_PREFIX}/{case['case_id']}",
        "evidence_level": "Trend",
        "comparison_boundary": (
            "Real SCALE-Sim 4x4 single-MAC WS dense timing/access evidence; "
            "seed 40; internal bank arbitration is SCALE-Sim-native"
        ),
    }


def write_summary(path: Path, rows: list[dict[str, Any]]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=SUMMARY_FIELDS, lineterminator="\n")
        writer.writeheader()
        for row in rows:
            writer.writerow({field: row.get(field, "") for field in SUMMARY_FIELDS})


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--frozen-config", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--failure-root", type=Path, required=True)
    parser.add_argument("--scalesim-repo", type=Path, default=DEFAULT_SCALESIM_REPO)
    parser.add_argument("--scalesim-python", type=Path, default=DEFAULT_SCALESIM_PYTHON)
    parser.add_argument("--timeout-seconds", type=int, default=14400)
    parser.add_argument("--resume", action="store_true")
    parser.add_argument(
        "--select-case",
        action="append",
        default=[],
        help="Run only the named case(s), while retaining prior completed checkpoints.",
    )
    args = parser.parse_args()

    started = utc_now()
    output_root = args.output_root.resolve()
    failure_root = args.failure_root.resolve()
    output_root.mkdir(parents=True, exist_ok=True)
    failure_root.mkdir(parents=True, exist_ok=True)
    matrix_document, all_cases = load_matrix(args.matrix.resolve())
    known_case_ids = {str(case["case_id"]) for case in all_cases}
    selected_case_ids = set(args.select_case) if args.select_case else known_case_ids
    unknown_case_ids = selected_case_ids - known_case_ids
    if unknown_case_ids:
        raise ValueError(f"unknown --select-case values: {sorted(unknown_case_ids)}")
    matrix_hash = sha256_file(args.matrix.resolve())
    frozen_config_hash = sha256_file(args.frozen_config.resolve())

    git_commit = git_value(args.scalesim_repo, "rev-parse", "HEAD")
    git_status = git_value(args.scalesim_repo, "status", "--porcelain")
    python_version = run_capture(
        [str(args.scalesim_python), "-c", "import sys; print(sys.version)"]
    ).stdout.strip()
    module_path = run_capture(
        [
            str(args.scalesim_python),
            "-c",
            "import scalesim; print(scalesim.__file__)",
        ],
        args.scalesim_repo,
    ).stdout.strip()
    provenance = {
        "schema": "aspdac.scalesim.size_scaling.provenance.v1",
        "host": socket.gethostname(),
        "platform": platform.platform(),
        "python_runtime": sys.version,
        "scalesim_python": str(args.scalesim_python),
        "scalesim_python_version": python_version,
        "scalesim_module": module_path,
        "scalesim_repo": str(args.scalesim_repo),
        "scalesim_git_commit": git_commit,
        "scalesim_git_expected_commit": EXPECTED_COMMIT,
        "scalesim_git_matches_expected": git_commit == EXPECTED_COMMIT,
        "scalesim_git_status_porcelain": git_status.splitlines(),
        "source_patch": "existing local NumPy compatibility patch",
        "matrix_hash": matrix_hash,
        "frozen_config_hash": frozen_config_hash,
        "matrix_id": matrix_document.get("matrix_id"),
        "tool_contract": matrix_document["tool_contracts"]["scalesim"],
        "started_at_utc": started,
    }
    (output_root / "run_provenance.json").write_text(
        json.dumps(provenance, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

    rows: list[dict[str, Any]] = []
    case_manifests: list[dict[str, Any]] = []
    for index, case in enumerate(all_cases, start=1):
        case_id = str(case["case_id"])
        case_root = output_root / case_id
        manifest_path = case_root / "case_manifest.json"
        if manifest_path.exists():
            prior = json.loads(manifest_path.read_text(encoding="utf-8"))
            if prior.get("status") == "completed" and isinstance(prior.get("summary"), dict):
                rows.append(prior["summary"])
                case_manifests.append(prior)
                print(f"[{index}/{len(all_cases)}] {case_id}: resumed completed", flush=True)
                continue
        if case_id not in selected_case_ids:
            print(f"[{index}/{len(all_cases)}] {case_id}: deferred by selection", flush=True)
            continue

        case_root.mkdir(parents=True, exist_ok=True)
        inputs = case_root / "inputs"
        raw_output = case_root / "raw_outputs"
        inputs.mkdir(parents=True, exist_ok=True)
        raw_output.mkdir(parents=True, exist_ok=True)
        config = inputs / "aspdac_4x4_ws.cfg"
        topology = inputs / f"{case_id}.csv"
        config.write_text(resolved_config(args.frozen_config, case_id), encoding="utf-8")
        write_topology(topology, case)
        config_hash = sha256_file(config)
        topology_hash = sha256_file(topology)
        run_name = f"aspdac_4x4_ws_size_scaling_{case_id}"
        command = [
            str(args.scalesim_python),
            "-m",
            "scalesim.scale",
            "-c",
            str(config),
            "-t",
            str(topology),
            "-p",
            str(raw_output),
            "-i",
            "gemm",
            "-s",
            "N",
        ]
        print(f"[{index}/{len(all_cases)}] {case_id}: running", flush=True)
        case_started = utc_now()
        started_clock = time.monotonic()
        timed_out = False
        try:
            completed = subprocess.run(
                command,
                cwd=args.scalesim_repo,
                text=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                timeout=args.timeout_seconds,
                check=False,
            )
            return_code = completed.returncode
            stdout = completed.stdout
            stderr = completed.stderr
        except subprocess.TimeoutExpired as exc:
            timed_out = True
            return_code = 124
            stdout = exc.stdout or ""
            stderr = (exc.stderr or "") + f"\nTimed out after {args.timeout_seconds}s\n"
        elapsed = time.monotonic() - started_clock
        (case_root / "stdout.log").write_text(stdout, encoding="utf-8")
        (case_root / "stderr.log").write_text(stderr, encoding="utf-8")
        (case_root / "command.json").write_text(
            json.dumps(
                {
                    "argv": command,
                    "cwd": str(args.scalesim_repo),
                    "started_at_utc": case_started,
                    "completed_at_utc": utc_now(),
                    "elapsed_seconds": elapsed,
                    "return_code": return_code,
                    "timed_out": timed_out,
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )
        reports = report_paths(raw_output, run_name)
        missing_reports = [name for name, path in reports.items() if not path.exists()]
        status = "completed" if return_code == 0 and not missing_reports else "failed"
        raw_files = inventory(list(raw_output.rglob("*")), case_root)
        raw_hash = canonical_hash(
            [{key: row[key] for key in ("path", "size_bytes", "sha256")} for row in raw_files]
        )
        manifest: dict[str, Any] = {
            "schema": "aspdac.scalesim.size_scaling.case.v1",
            "case": case,
            "status": status,
            "return_code": return_code,
            "timed_out": timed_out,
            "elapsed_seconds": round(elapsed, 6),
            "command": command,
            "cwd": str(args.scalesim_repo),
            "config_hash": config_hash,
            "topology_hash": topology_hash,
            "raw_output_hash": raw_hash,
            "raw_files": raw_files,
            "missing_reports": missing_reports,
            "scalesim_git_commit": git_commit,
            "started_at_utc": case_started,
            "completed_at_utc": utc_now(),
        }
        if status == "completed":
            summary = parse_metric_row(
                case,
                reports,
                provenance,
                command,
                elapsed,
                config_hash,
                topology_hash,
                raw_hash,
            )
            manifest["summary"] = summary
            rows.append(summary)
        else:
            failure = {
                "schema": "aspdac.scalesim.size_scaling.failure.v1",
                "case_id": case_id,
                "status": status,
                "return_code": return_code,
                "timed_out": timed_out,
                "missing_reports": missing_reports,
                "command": command,
                "stdout": f"{FINAL_RAW_PREFIX}/{case_id}/stdout.log",
                "stderr": f"{FINAL_RAW_PREFIX}/{case_id}/stderr.log",
                "recorded_at_utc": utc_now(),
            }
            (failure_root / f"size_scaling_scalesim_{case_id}.json").write_text(
                json.dumps(failure, indent=2) + "\n", encoding="utf-8"
            )
            rows.append(
                {
                    "case_id": case_id,
                    "family": case["family"],
                    "scale": case["scale"],
                    "M": case["M"],
                    "N": case["N"],
                    "K": case["K"],
                    "expected_macs": case["expected_macs"],
                    "status": "failed",
                    "return_code": return_code,
                    "elapsed_seconds": round(elapsed, 6),
                    "config_hash": config_hash,
                    "topology_hash": topology_hash,
                    "raw_output_hash": raw_hash,
                    "scalesim_git_commit": git_commit,
                    "scalesim_python": str(args.scalesim_python),
                    "command": " ".join(command),
                    "external_raw_path": f"{FINAL_RAW_PREFIX}/{case_id}",
                    "evidence_level": "failed",
                    "comparison_boundary": "; ".join(missing_reports),
                }
            )
        manifest_path.write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
        )
        case_manifests.append(manifest)
        write_summary(output_root / "size_scaling_scalesim_summary.csv", rows)
        print(
            f"[{index}/{len(all_cases)}] {case_id}: {status} ({elapsed:.1f}s)", flush=True
        )

    ordered_rows = sorted(rows, key=lambda row: [c["case_id"] for c in all_cases].index(row["case_id"]))
    summary_path = output_root / "size_scaling_scalesim_summary.csv"
    write_summary(summary_path, ordered_rows)
    completed_count = sum(row.get("status") == "completed" for row in ordered_rows)
    failed_count = sum(row.get("status") == "failed" for row in ordered_rows)
    run_manifest = {
        "schema": "aspdac.scalesim.size_scaling.run.v1",
        "matrix_id": matrix_document.get("matrix_id"),
        "matrix_hash": matrix_hash,
        "tool_contract": matrix_document["tool_contracts"]["scalesim"],
        "source": "real installed SCALE-Sim; no analytical substitute",
        "evidence_level": "Trend",
        "started_at_utc": started,
        "completed_at_utc": utc_now(),
        "requested_cases": len(all_cases),
        "selected_case_ids_this_invocation": sorted(selected_case_ids),
        "completed_cases": completed_count,
        "failed_cases": failed_count,
        "not_completed_case_ids": [
            str(case["case_id"])
            for case in all_cases
            if str(case["case_id"]) not in {
                str(row["case_id"])
                for row in ordered_rows
                if row.get("status") == "completed"
            }
        ],
        "scalesim_git_commit": git_commit,
        "scalesim_git_expected_commit": EXPECTED_COMMIT,
        "scalesim_git_matches_expected": git_commit == EXPECTED_COMMIT,
        "scalesim_git_status_porcelain": git_status.splitlines(),
        "summary_path": f"{FINAL_RAW_PREFIX}/{summary_path.name}",
        "summary_sha256": sha256_file(summary_path),
        "failure_records": [
            path.name for path in sorted(failure_root.glob("size_scaling_scalesim_*.json"))
        ],
        "case_manifests": [
            {
                "case_id": manifest["case"]["case_id"],
                "status": manifest["status"],
                "elapsed_seconds": manifest["elapsed_seconds"],
                "raw_output_hash": manifest["raw_output_hash"],
                "path": (
                    f"{FINAL_RAW_PREFIX}/{manifest['case']['case_id']}/case_manifest.json"
                ),
            }
            for manifest in case_manifests
        ],
        "limitations": [
            "Trend evidence: STAGE does not reproduce SCALE-Sim internal bank arbitration.",
            "Seed 40 is frozen even though the dense cases are deterministic.",
            "Generated trace files are retained and may be large.",
        ],
    }
    (output_root / "size_scaling_scalesim_run_manifest.json").write_text(
        json.dumps(run_manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(
        f"complete: {completed_count}/{len(all_cases)} completed, {failed_count} failed",
        flush=True,
    )
    return 0 if failed_count == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
