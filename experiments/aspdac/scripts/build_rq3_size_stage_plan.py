#!/usr/bin/env python3
"""Build the STAGE RQ3 size-scaling plan from completed external-tool summaries.

The builder deliberately refuses to infer any reference timing or access count.  A
plan is emitted only after every matrix case has one successful Timeloop row and
one successful SCALE-Sim row with auditable raw-output provenance.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
from typing import Any, Iterable

import yaml


REPO = Path(__file__).resolve().parents[3]
DEFAULT_MATRIX = REPO / "experiments/aspdac/specs/final_sweeps/rq3_size_matrix.yaml"
DEFAULT_TIMELOOP = REPO / "experiments/aspdac/results/final_20260716/raw/size_scaling_timeloop/timeloop_size_summary.csv"
DEFAULT_SCALESIM = REPO / "experiments/aspdac/results/final_20260716/raw/size_scaling_scalesim/size_scaling_scalesim_summary.csv"
DEFAULT_OUTPUT = REPO / "experiments/aspdac/specs/final_sweeps/rq3_size_stage.yaml"
DEFAULT_BUNDLE_ROOT = REPO / "experiments/aspdac/results/final_20260716"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def row_sha256(row: dict[str, str]) -> str:
    return hashlib.sha256(canonical_json(row).encode("utf-8")).hexdigest()


def read_yaml(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise FileNotFoundError(f"Required size matrix is missing: {path}")
    value = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a YAML mapping: {path}")
    return value


def read_summary(path: Path, tool: str) -> list[dict[str, str]]:
    if not path.is_file():
        raise FileNotFoundError(
            f"Required completed {tool} size summary is missing: {path}. "
            "No STAGE plan was generated; run the external tool first."
        )
    with path.open(newline="", encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    if not rows:
        raise ValueError(f"{tool} size summary is empty: {path}")
    return rows


def required(row: dict[str, str], name: str, tool: str, case_id: str) -> str:
    value = row.get(name)
    if value is None or not str(value).strip():
        raise ValueError(f"{tool} case {case_id} is missing required column/value '{name}'")
    return str(value).strip()


def required_alias(row: dict[str, str], names: Iterable[str], tool: str, case_id: str) -> str:
    for name in names:
        value = row.get(name)
        if value is not None and str(value).strip():
            return str(value).strip()
    raise ValueError(f"{tool} case {case_id} is missing required value; accepted columns: {', '.join(names)}")


def as_int(value: str, label: str) -> int:
    try:
        number = float(value)
    except ValueError as exc:
        raise ValueError(f"{label} must be numeric, got {value!r}") from exc
    integer = int(number)
    if number != integer or integer < 0:
        raise ValueError(f"{label} must be a non-negative integer, got {value!r}")
    return integer


def as_float(value: str, label: str) -> float:
    try:
        number = float(value)
    except ValueError as exc:
        raise ValueError(f"{label} must be numeric, got {value!r}") from exc
    if not (number >= 0):
        raise ValueError(f"{label} must be non-negative, got {value!r}")
    return number


def repo_path(value: str, summary_path: Path, tool: str, case_id: str) -> str:
    raw = Path(value)
    candidates = [raw] if raw.is_absolute() else [REPO / raw, summary_path.parent / raw]
    existing = next((candidate.resolve() for candidate in candidates if candidate.exists()), None)
    if existing is None:
        raise FileNotFoundError(f"{tool} case {case_id} external_raw_path does not exist: {value}")
    try:
        return existing.relative_to(REPO).as_posix()
    except ValueError:
        return str(existing)


def index_completed(rows: list[dict[str, str]], tool: str, expected_ids: set[str]) -> dict[str, dict[str, str]]:
    indexed: dict[str, dict[str, str]] = {}
    for row in rows:
        case_id = required(row, "case_id", tool, "<unknown>")
        if case_id in indexed:
            raise ValueError(f"{tool} summary has duplicate case_id {case_id}")
        status = required(row, "status", tool, case_id).lower()
        if status != "completed":
            raise ValueError(f"{tool} case {case_id} is not completed (status={status})")
        if row.get("return_code") not in (None, "", "0", "0.0"):
            raise ValueError(f"{tool} case {case_id} has nonzero return_code={row['return_code']}")
        indexed[case_id] = row
    missing = expected_ids - set(indexed)
    extra = set(indexed) - expected_ids
    if missing or extra:
        raise ValueError(f"{tool} case-set mismatch: missing={sorted(missing)}, extra={sorted(extra)}")
    return indexed


def validate_shape(row: dict[str, str], matrix_case: dict[str, Any], tool: str) -> tuple[int, int, int, int]:
    case_id = str(matrix_case["case_id"])
    values = tuple(as_int(required(row, key, tool, case_id), f"{tool} {case_id} {key}") for key in ("M", "N", "K", "expected_macs"))
    expected = tuple(int(matrix_case[key]) for key in ("M", "N", "K", "expected_macs"))
    if values != expected:
        raise ValueError(f"{tool} case {case_id} shape/MAC mismatch: summary={values}, matrix={expected}")
    if values[0] * values[1] * values[2] != values[3]:
        raise ValueError(f"{tool} case {case_id} expected_macs is not M*N*K")
    return values


def build_timeloop_case(matrix_case: dict[str, Any], row: dict[str, str], source: Path, source_hash: str) -> dict[str, Any]:
    case_id = str(matrix_case["case_id"])
    M, N, K, expected_macs = validate_shape(row, matrix_case, "Timeloop")
    computes = as_int(required(row, "computes", "Timeloop", case_id), f"Timeloop {case_id} computes")
    if computes != expected_macs:
        raise ValueError(f"Timeloop case {case_id} computes={computes} does not equal expected_macs={expected_macs}")
    return {
        "case_id": case_id,
        "family": str(matrix_case["family"]),
        "scale": str(matrix_case["scale"]),
        "M": M,
        "N": N,
        "K": K,
        "expected_macs": expected_macs,
        "register_accesses": as_int(required(row, "register_accesses", "Timeloop", case_id), f"Timeloop {case_id} register_accesses"),
        "local_buffer_accesses": as_int(required(row, "local_buffer_accesses", "Timeloop", case_id), f"Timeloop {case_id} local_buffer_accesses"),
        "global_buffer_accesses": as_int(required(row, "global_buffer_accesses", "Timeloop", case_id), f"Timeloop {case_id} global_buffer_accesses"),
        "dram_accesses": as_int(required(row, "dram_accesses", "Timeloop", case_id), f"Timeloop {case_id} dram_accesses"),
        "reference_cycles": as_int(required(row, "cycles", "Timeloop", case_id), f"Timeloop {case_id} cycles"),
        "reference_utilization_pct": as_float(required(row, "utilization_pct", "Timeloop", case_id), f"Timeloop {case_id} utilization_pct"),
        "mapping_hash": required_alias(row, ("mapping_hash", "mapping_sha256"), "Timeloop", case_id),
        "external_raw_path": repo_path(required(row, "external_raw_path", "Timeloop", case_id), source, "Timeloop", case_id),
        "external_summary_sha256": source_hash,
        "external_row_sha256": row_sha256(row),
    }


def build_scalesim_case(matrix_case: dict[str, Any], row: dict[str, str], source: Path, source_hash: str) -> dict[str, Any]:
    case_id = str(matrix_case["case_id"])
    M, N, K, expected_macs = validate_shape(row, matrix_case, "SCALE-Sim")
    fields = (
        "sram_ifmap_reads",
        "sram_filter_reads",
        "sram_ofmap_writes",
        "dram_ifmap_reads",
        "dram_filter_reads",
        "dram_ofmap_writes",
    )
    result: dict[str, Any] = {
        "case_id": case_id,
        "family": str(matrix_case["family"]),
        "scale": str(matrix_case["scale"]),
        "M": M,
        "N": N,
        "K": K,
        "expected_macs": expected_macs,
    }
    result.update({name: as_int(required(row, name, "SCALE-Sim", case_id), f"SCALE-Sim {case_id} {name}") for name in fields})
    result.update({
        "reference_cycles": as_int(required(row, "total_cycles", "SCALE-Sim", case_id), f"SCALE-Sim {case_id} total_cycles"),
        "reference_cold_cycles": as_int(required(row, "total_cycles_incl_prefetch", "SCALE-Sim", case_id), f"SCALE-Sim {case_id} total_cycles_incl_prefetch"),
        "reference_stall_cycles": as_int(required(row, "stall_cycles", "SCALE-Sim", case_id), f"SCALE-Sim {case_id} stall_cycles"),
        "reference_utilization_pct": as_float(required(row, "overall_util_pct", "SCALE-Sim", case_id), f"SCALE-Sim {case_id} overall_util_pct"),
        "mapping_hash": required_alias(row, ("mapping_hash", "mapping_sha256"), "SCALE-Sim", case_id),
        "external_raw_path": repo_path(required(row, "external_raw_path", "SCALE-Sim", case_id), source, "SCALE-Sim", case_id),
        "external_summary_sha256": source_hash,
        "external_row_sha256": row_sha256(row),
    })
    return result


def bundle_root_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return resolved.relative_to(REPO).as_posix()
    except ValueError as exc:
        raise ValueError(f"Bundle root must be inside the repository: {resolved}") from exc


def build_plan(
    matrix_path: Path,
    timeloop_path: Path,
    scalesim_path: Path,
    bundle_root: Path = DEFAULT_BUNDLE_ROOT,
) -> dict[str, Any]:
    matrix = read_yaml(matrix_path)
    matrix_cases = matrix.get("cases")
    if not isinstance(matrix_cases, list) or len(matrix_cases) != 9:
        raise ValueError("RQ3 size matrix must contain exactly nine cases")
    case_ids = [str(case["case_id"]) for case in matrix_cases]
    if len(case_ids) != len(set(case_ids)):
        raise ValueError("RQ3 size matrix contains duplicate case_id values")
    expected_ids = set(case_ids)
    timeloop_rows = index_completed(read_summary(timeloop_path, "Timeloop"), "Timeloop", expected_ids)
    scalesim_rows = index_completed(read_summary(scalesim_path, "SCALE-Sim"), "SCALE-Sim", expected_ids)
    timeloop_hash, scalesim_hash = sha256_file(timeloop_path), sha256_file(scalesim_path)
    timeloop_cases = [build_timeloop_case(case, timeloop_rows[str(case["case_id"])], timeloop_path, timeloop_hash) for case in matrix_cases]
    scalesim_cases = [build_scalesim_case(case, scalesim_rows[str(case["case_id"])], scalesim_path, scalesim_hash) for case in matrix_cases]
    return {
        "schema_version": "aspg-experiment-plan-1.0",
        "name": "rq3_size_stage",
        "bundle_root": bundle_root_path(bundle_root),
        "configs": [
            "experiments/aspdac/specs/final_configs/v_tl.yaml",
            "experiments/aspdac/specs/final_configs/v_ss.yaml",
        ],
        "experiments": [
            {
                "name": "rq3_size_stage_vtl",
                "provider": "stage",
                "scenario": "vtl_workload",
                "evidence_level": "exact",
                "base_config": "V-TL",
                "fixed": {
                    "sram_ifmap_reads": 0,
                    "sram_filter_reads": 0,
                    "sram_ofmap_writes": 0,
                    "dram_ifmap_reads": 0,
                    "dram_filter_reads": 0,
                    "dram_ofmap_writes": 0,
                    "reference_cold_cycles": 0,
                    "reference_stall_cycles": 0,
                },
                "cases": timeloop_cases,
                "axes": {"mode": ["compute_only", "full_system"]},
            },
            {
                "name": "rq3_size_stage_vss",
                "provider": "stage",
                "scenario": "vss_workload",
                "evidence_level": "trend",
                "base_config": "V-SS",
                "fixed": {
                    "register_accesses": 0,
                    "local_buffer_accesses": 0,
                    "global_buffer_accesses": 0,
                    "dram_accesses": 0,
                    "seed": 40,
                },
                "cases": scalesim_cases,
                "axes": {"mode": ["warm_array", "cold_start"]},
            },
        ],
    }


def write_plan(path: Path, plan: dict[str, Any]) -> None:
    content = yaml.safe_dump(plan, sort_keys=False, allow_unicode=True, width=240)
    if path.exists() and path.read_text(encoding="utf-8") != content:
        raise FileExistsError(f"Refusing to change an existing frozen plan in place: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix", type=Path, default=DEFAULT_MATRIX)
    parser.add_argument("--timeloop-summary", type=Path, default=DEFAULT_TIMELOOP)
    parser.add_argument("--scalesim-summary", type=Path, default=DEFAULT_SCALESIM)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--bundle-root", type=Path, default=DEFAULT_BUNDLE_ROOT)
    args = parser.parse_args()
    plan = build_plan(
        args.matrix.resolve(),
        args.timeloop_summary.resolve(),
        args.scalesim_summary.resolve(),
        args.bundle_root.resolve(),
    )
    write_plan(args.output.resolve(), plan)
    print(json.dumps({"output": str(args.output.resolve()), "experiments": 2, "matrix_cases": 9, "stage_candidates": 36}, indent=2))


if __name__ == "__main__":
    main()
