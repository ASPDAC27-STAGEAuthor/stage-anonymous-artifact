#!/usr/bin/env python3
"""Create exact/trend RQ3 size-scaling CSVs from actual references and STAGE raw runs."""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path
from typing import Any

import final_analysis as common
import matplotlib.pyplot as plt


BUNDLE = common.BUNDLE
MATRIX = common.REPO / "experiments/aspdac/specs/final_sweeps/rq3_size_matrix.yaml"
TIMELOOP_SUMMARY = BUNDLE / "raw/size_scaling_timeloop/timeloop_size_summary.csv"
SCALESIM_SUMMARY = BUNDLE / "raw/size_scaling_scalesim/size_scaling_scalesim_summary.csv"
ACCELERGY_SUMMARY = BUNDLE / "raw/size_scaling_accelergy/accelergy_size_summary.csv"
ACCESS_KEYS_TL = {
    "register": "register_accesses",
    "local_buffer": "local_buffer_accesses",
    "global_buffer": "global_buffer_accesses",
    "dram": "dram_accesses",
}
ACCESS_KEYS_SS = (
    "sram_ifmap_reads",
    "sram_filter_reads",
    "sram_ofmap_writes",
    "dram_ifmap_reads",
    "dram_filter_reads",
    "dram_ofmap_writes",
)
FAMILY_LABELS = {"gemm": "GEMM", "mlp_l1": "MLP-L1", "attention_qk": "Attention-QK"}
FAMILY_COLORS = {"gemm": "#2563eb", "mlp_l1": "#d97706", "attention_qk": "#7c3aed"}
SCALE_ORDER = {"small": 0, "medium": 1, "large": 2}


def read_csv(path: Path) -> list[dict[str, str]]:
    if not path.is_file():
        raise FileNotFoundError(f"Required measured input is missing: {path}")
    with path.open(newline="", encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    if not rows:
        raise ValueError(f"Measured input is empty: {path}")
    return rows


def int_value(row: dict[str, str], key: str) -> int:
    return int(float(row[key]))


def index_references(rows: list[dict[str, str]], label: str) -> dict[str, dict[str, str]]:
    indexed = {row["case_id"]: row for row in rows}
    if len(indexed) != len(rows):
        raise ValueError(f"{label} summary contains duplicate case_id values")
    incomplete = [case_id for case_id, row in indexed.items() if row.get("status", "").lower() != "completed"]
    if incomplete:
        raise ValueError(f"{label} summary contains incomplete cases: {incomplete}")
    return indexed


def index_stage(results: list[dict[str, Any]]) -> dict[tuple[str, str, str], dict[str, Any]]:
    indexed: dict[tuple[str, str, str], dict[str, Any]] = {}
    for row in results:
        key = (str(row["scenario"]), str(row["axes"]["case_id"]), str(row["axes"]["mode"]))
        if key in indexed:
            raise ValueError(f"Duplicate STAGE size result: {key}")
        if row.get("status") != "completed":
            raise ValueError(f"Incomplete STAGE size result: {key}")
        indexed[key] = row
    return indexed


def relative_error(actual: int | float, reference: int | float) -> float:
    if reference == 0:
        return 0.0 if actual == 0 else float("inf")
    return (float(actual) - float(reference)) / float(reference)


def build_rows(
    matrix_cases: list[dict[str, Any]],
    stage: dict[tuple[str, str, str], dict[str, Any]],
    timeloop: dict[str, dict[str, str]],
    scalesim: dict[str, dict[str, str]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]]]:
    timeloop_rows: list[dict[str, Any]] = []
    scalesim_rows: list[dict[str, Any]] = []
    access_rows: list[dict[str, Any]] = []
    for case in matrix_cases:
        case_id, family, scale = str(case["case_id"]), str(case["family"]), str(case["scale"])
        tl_ref, ss_ref = timeloop[case_id], scalesim[case_id]
        compute = stage[("vtl_workload", case_id, "compute_only")]
        full = stage[("vtl_workload", case_id, "full_system")]
        warm = stage[("vss_workload", case_id, "warm_array")]
        cold = stage[("vss_workload", case_id, "cold_start")]
        expected_macs = int(case["expected_macs"])
        tl_cycles = int_value(tl_ref, "cycles")
        compute_metrics, full_metrics = compute["metrics"], full["metrics"]
        exact_accesses = True
        for stage_key, reference_key in ACCESS_KEYS_TL.items():
            external_count = int_value(tl_ref, reference_key)
            stage_count = int(compute["accesses"][stage_key])
            exact_accesses = exact_accesses and external_count == stage_count
            access_rows.append({
                "tool": "timeloop-model",
                "case_id": case_id,
                "family": family,
                "scale": scale,
                "hierarchy_action": stage_key,
                "external_measured_accesses": external_count,
                "stage_replayed_accesses": stage_count,
                "relative_error": relative_error(stage_count, external_count),
                "evidence_level": "Exact schedule replay" if stage_count == external_count else "Mismatch",
                "mapping_hash": compute["provenance"]["mapping_hash"],
            })
        exact_cycles = int(compute_metrics["total_cycles"]) == tl_cycles
        exact_macs = int(compute_metrics["completed_macs"]) == expected_macs
        attribution_sum = sum(int(full_metrics[key]) for key in (
            "compute_cycles", "memory_cycles", "noc_cycles", "serialization_cycles",
            "conversion_cycles", "reduction_cycles", "softmax_cycles",
        ))
        timeloop_rows.append({
            "case_id": case_id,
            "family": family,
            "scale": scale,
            "M": int(case["M"]),
            "N": int(case["N"]),
            "K": int(case["K"]),
            "expected_macs": expected_macs,
            "timeloop_model_cycles": tl_cycles,
            "stage_compute_only_cycles": int(compute_metrics["total_cycles"]),
            "compute_cycle_relative_error": relative_error(int(compute_metrics["total_cycles"]), tl_cycles),
            "exact_cycle_match": exact_cycles,
            "exact_mac_match": exact_macs,
            "exact_access_match": exact_accesses,
            "evidence_level": "Exact" if exact_cycles and exact_macs and exact_accesses else "Mismatch",
            "timeloop_utilization_pct": float(tl_ref["utilization_pct"]),
            "stage_compute_utilization_pct": float(compute_metrics["utilization_pct"]),
            "stage_full_system_cycles": int(full_metrics["total_cycles"]),
            "stage_compute_cycles": int(full_metrics["compute_cycles"]),
            "stage_memory_cycles": int(full_metrics["memory_cycles"]),
            "stage_noc_cycles": int(full_metrics["noc_cycles"]),
            "stage_serialization_cycles": int(full_metrics["serialization_cycles"]),
            "stage_conversion_cycles": int(full_metrics["conversion_cycles"]),
            "stage_reduction_cycles": int(full_metrics["reduction_cycles"]),
            "stage_softmax_cycles": int(full_metrics["softmax_cycles"]),
            "full_system_attribution_sum": attribution_sum,
            "full_system_attribution_exact": attribution_sum == int(full_metrics["total_cycles"]),
            "compute_trace_hash": compute_metrics["canonical_trace_hash"],
            "full_trace_hash": full_metrics["canonical_trace_hash"],
            "mapping_hash": compute["provenance"]["mapping_hash"],
            "paired_config_hash": compute["config_hash"],
            "comparison_boundary": "Compute floor/accesses are Exact; full-system decomposition is STAGE-only and not Timeloop-equivalent.",
        })

        ss_cycles = int_value(ss_ref, "total_cycles")
        warm_metrics, cold_metrics = warm["metrics"], cold["metrics"]
        ss_access_exact = True
        for key in ACCESS_KEYS_SS:
            external_count = int_value(ss_ref, key)
            stage_count = int(warm["accesses"][key])
            ss_access_exact = ss_access_exact and external_count == stage_count
            access_rows.append({
                "tool": "SCALE-Sim",
                "case_id": case_id,
                "family": family,
                "scale": scale,
                "hierarchy_action": key,
                "external_measured_accesses": external_count,
                "stage_replayed_accesses": stage_count,
                "relative_error": relative_error(stage_count, external_count),
                "evidence_level": "Exact input-contract replay" if stage_count == external_count else "Mismatch",
                "mapping_hash": warm["provenance"]["mapping_hash"],
            })
        error_pct = 100.0 * abs(relative_error(int(warm_metrics["total_cycles"]), ss_cycles))
        scalesim_rows.append({
            "case_id": case_id,
            "family": family,
            "scale": scale,
            "M": int(case["M"]),
            "N": int(case["N"]),
            "K": int(case["K"]),
            "expected_macs": expected_macs,
            "scalesim_total_cycles": ss_cycles,
            "scalesim_cold_cycles": int_value(ss_ref, "total_cycles_incl_prefetch"),
            "scalesim_stall_cycles": int_value(ss_ref, "stall_cycles"),
            "scalesim_utilization_pct": float(ss_ref["overall_util_pct"]),
            "stage_warm_total_cycles": int(warm_metrics["total_cycles"]),
            "stage_cold_total_cycles": int(cold_metrics["total_cycles"]),
            "stage_prefetch_cycles": int(cold_metrics["prefetch_cycles"]),
            "stage_wavefront_cycles": int(warm_metrics["wavefront_cycles"]),
            "stage_memory_service_cycles": int(warm_metrics["memory_cycles"]),
            "stage_memory_stall_cycles": int(warm_metrics["memory_stall_cycles"]),
            "stage_utilization_pct": float(warm_metrics["utilization_pct"]),
            "warm_cycle_relative_error": relative_error(int(warm_metrics["total_cycles"]), ss_cycles),
            "absolute_warm_cycle_error_pct": error_pct,
            "trend_under_10pct": error_pct < 10.0,
            "exact_access_contract_match": ss_access_exact,
            "evidence_level": "Trend" if ss_access_exact else "Mismatch",
            "warm_trace_hash": warm_metrics["canonical_trace_hash"],
            "cold_trace_hash": cold_metrics["canonical_trace_hash"],
            "mapping_hash": warm["provenance"]["mapping_hash"],
            "paired_config_hash": warm["config_hash"],
            "comparison_boundary": "Matched 4x4 WS wavefront/access streams; SCALE-Sim internal bank arbitration is not replicated.",
        })

    for rows, tool_key, stage_key in (
        (timeloop_rows, "timeloop_model_cycles", "stage_compute_only_cycles"),
        (scalesim_rows, "scalesim_total_cycles", "stage_warm_total_cycles"),
    ):
        small_by_family = {str(row["family"]): row for row in rows if row["scale"] == "small"}
        for row in rows:
            small = small_by_family[str(row["family"])]
            row["external_cycle_scale_vs_small"] = float(row[tool_key]) / float(small[tool_key])
            row["stage_cycle_scale_vs_small"] = float(row[stage_key]) / float(small[stage_key])
            row["mac_scale_vs_small"] = float(row["expected_macs"]) / float(small["expected_macs"])
    timeloop_rows.sort(key=lambda row: (row["family"], SCALE_ORDER[str(row["scale"])]))
    scalesim_rows.sort(key=lambda row: (row["family"], SCALE_ORDER[str(row["scale"])]))
    access_rows.sort(key=lambda row: (row["tool"], row["family"], row["scale"], row["hierarchy_action"]))
    return timeloop_rows, scalesim_rows, access_rows


def build_accelergy_rows(matrix_cases: list[dict[str, Any]], accelergy: dict[str, dict[str, str]]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for case in matrix_cases:
        case_id = str(case["case_id"])
        reference = accelergy[case_id]
        checks = {
            "ert_exists": reference["ert_exists"].lower() == "true",
            "ert_nonempty": reference["ert_nonempty"].lower() == "true",
            "five_tables": int_value(reference, "ert_table_count") == 5,
            "all_tables_have_actions": reference["all_tables_have_actions"].lower() == "true",
            "no_dummy_action": reference["dummy_action_estimate"].lower() == "false",
            "no_schema_fallback": reference["schema_fallback"].lower() == "false",
        }
        if not all(checks.values()):
            raise ValueError(f"Accelergy case {case_id} failed ERT validation: {checks}")
        expected_macs = int(case["expected_macs"])
        if int_value(reference, "expected_macs") != expected_macs:
            raise ValueError(f"Accelergy case {case_id} MAC count does not match the frozen matrix")
        energy_uj = float(reference["energy_uj"])
        rows.append({
            "case_id": case_id,
            "family": str(case["family"]),
            "scale": str(case["scale"]),
            "M": int(case["M"]),
            "N": int(case["N"]),
            "K": int(case["K"]),
            "expected_macs": expected_macs,
            "accelergy_energy_uj": energy_uj,
            "accelergy_energy_pj_per_mac": energy_uj * 1_000_000.0 / expected_macs,
            "ert_exists": checks["ert_exists"],
            "ert_nonempty": checks["ert_nonempty"],
            "ert_table_count": 5,
            "all_tables_have_actions": checks["all_tables_have_actions"],
            "dummy_action_estimate": False,
            "schema_fallback": False,
            "ert_valid": True,
            "technology": reference["technology"],
            "mapping_hash": reference.get("mapping_sha256") or reference.get("mapping_hash"),
            "evidence_level": "Trend shared reference",
            "claim_boundary": reference["energy_claim_boundary"],
        })
    small_by_family = {str(row["family"]): row for row in rows if row["scale"] == "small"}
    for row in rows:
        row["energy_scale_vs_small"] = float(row["accelergy_energy_uj"]) / float(small_by_family[str(row["family"])]["accelergy_energy_uj"])
    rows.sort(key=lambda row: (row["family"], SCALE_ORDER[str(row["scale"])]))
    return rows


def make_figure(timeloop_rows: list[dict[str, Any]], scalesim_rows: list[dict[str, Any]], accelergy_rows: list[dict[str, Any]], output: Path) -> None:
    """Render a unit-safe three-panel scaling figure from the tidy measured rows."""
    fig, axes = plt.subplots(1, 3, figsize=(11.6, 3.75))
    sizes = ["small", "medium", "large"]
    x = list(range(len(sizes)))
    for family in FAMILY_LABELS:
        tl = sorted((row for row in timeloop_rows if row["family"] == family), key=lambda row: SCALE_ORDER[str(row["scale"])])
        ss = sorted((row for row in scalesim_rows if row["family"] == family), key=lambda row: SCALE_ORDER[str(row["scale"])])
        color, label = FAMILY_COLORS[family], FAMILY_LABELS[family]
        axes[0].plot(x, [float(row["external_cycle_scale_vs_small"]) for row in tl], color=color, marker="o", linewidth=1.8, label=f"{label} Timeloop")
        axes[0].plot(x, [float(row["stage_cycle_scale_vs_small"]) for row in tl], color=color, marker="s", markerfacecolor="white", linestyle="--", linewidth=1.2, label=f"{label} STAGE")
        axes[1].plot(x, [float(row["external_cycle_scale_vs_small"]) for row in ss], color=color, marker="o", linewidth=1.8, label=f"{label} SCALE-Sim")
        axes[1].plot(x, [float(row["stage_cycle_scale_vs_small"]) for row in ss], color=color, marker="s", markerfacecolor="white", linestyle="--", linewidth=1.2, label=f"{label} STAGE")
    for axis in axes[:2]:
        axis.set_xticks(x, ["S", "M", "L"])
        axis.set_yscale("log", base=2)
        axis.set_ylabel("Cycle scale vs. small (x)")
        axis.grid(alpha=0.22, which="both")
    axes[0].set_title("(a) Timeloop compute floor (Exact)")
    max_error = max(float(row["absolute_warm_cycle_error_pct"]) for row in scalesim_rows)
    axes[1].set_title(f"(b) SCALE-Sim timing (Trend, max |err| {max_error:.1f}%)")
    axes[0].legend(frameon=False, fontsize=6.4, ncol=2, loc="upper left")
    axes[1].legend(frameon=False, fontsize=6.4, ncol=2, loc="upper left")

    for family in FAMILY_LABELS:
        energy = sorted((row for row in accelergy_rows if row["family"] == family), key=lambda row: SCALE_ORDER[str(row["scale"])])
        axes[2].plot(x, [float(row["accelergy_energy_pj_per_mac"]) for row in energy], color=FAMILY_COLORS[family], marker="o", linewidth=1.8, label=FAMILY_LABELS[family])
    axes[2].set_xticks(x, ["S", "M", "L"])
    axes[2].set_ylabel("Accelergy energy (pJ/MAC)")
    axes[2].set_title("(c) Shared 45-nm energy (Trend)")
    axes[2].grid(alpha=0.22)
    axes[2].legend(frameon=False, fontsize=7)
    fig.text(0.5, 0.005, "S/M/L denote the frozen small, medium, and large shape in each workload family.", ha="center", fontsize=7.2)
    fig.tight_layout(rect=(0, 0.045, 1, 1))
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, bbox_inches="tight", metadata={"Creator": "STAGE RQ3 size analysis", "CreationDate": None, "ModDate": None})
    fig.savefig(output.with_suffix(".png"), dpi=240, bbox_inches="tight")
    plt.close(fig)


def main() -> None:
    import yaml

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, default=BUNDLE)
    args = parser.parse_args()
    bundle = args.bundle.resolve()
    timeloop_summary = bundle / "raw/size_scaling_timeloop/timeloop_size_summary.csv"
    scalesim_summary = bundle / "raw/size_scaling_scalesim/size_scaling_scalesim_summary.csv"
    accelergy_summary = bundle / "raw/size_scaling_accelergy/accelergy_size_summary.csv"
    matrix = yaml.safe_load(MATRIX.read_text(encoding="utf-8"))
    matrix_cases = matrix["cases"]
    timeloop = index_references(read_csv(timeloop_summary), "Timeloop")
    scalesim = index_references(read_csv(scalesim_summary), "SCALE-Sim")
    accelergy = index_references(read_csv(accelergy_summary), "Accelergy")
    expected = {str(case["case_id"]) for case in matrix_cases}
    if set(timeloop) != expected or set(scalesim) != expected or set(accelergy) != expected:
        raise RuntimeError("Actual-tool size summaries do not exactly cover the frozen nine-case matrix")
    results, stage_inputs = common.load_plan_results("rq3_size_stage", bundle)
    if len(results) != 36:
        raise RuntimeError(f"Expected 36 STAGE size results (9x2x2), found {len(results)}")
    tl_rows, ss_rows, access_rows = build_rows(matrix_cases, index_stage(results), timeloop, scalesim)
    energy_rows = build_accelergy_rows(matrix_cases, accelergy)
    outputs = [
        bundle / "summary/rq3_size_timeloop_stage_scaling.csv",
        bundle / "summary/rq3_size_scalesim_stage_scaling.csv",
        bundle / "summary/rq3_size_access_scaling.csv",
        bundle / "summary/rq3_size_accelergy_scaling.csv",
    ]
    for path, rows in zip(outputs, (tl_rows, ss_rows, access_rows, energy_rows)):
        common.write_csv(path, rows)
    analysis_manifest = bundle / "manifests/rq3_size_scaling_output_manifest.json"
    analysis_inputs = [MATRIX, timeloop_summary, scalesim_summary, accelergy_summary, *stage_inputs]
    analysis_manifest.write_text(json.dumps({
        "schema_version": "aspg-derived-output-manifest-1.0",
        "rq": "RQ3 shared size scaling tidy data",
        "inputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in analysis_inputs],
        "outputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in outputs],
        "timeloop_exact_cases": sum(row["evidence_level"] == "Exact" for row in tl_rows),
        "scalesim_trend_cases_under_10pct": sum(bool(row["trend_under_10pct"]) for row in ss_rows),
        "scalesim_max_absolute_cycle_error_pct": max(float(row["absolute_warm_cycle_error_pct"]) for row in ss_rows),
        "accelergy_valid_ert_cases": sum(bool(row["ert_valid"]) for row in energy_rows),
        "evidence_boundary": "Timeloop compute/access replay Exact; SCALE-Sim timing Trend; Accelergy uses a shared 45-nm reference and is not silicon calibrated.",
    }, indent=2) + "\n", encoding="utf-8")

    figure = bundle / "figures/fig_rq3_size_scaling.pdf"
    make_figure(read_csv(outputs[0]), read_csv(outputs[1]), read_csv(outputs[3]), figure)
    figure_outputs = [figure, figure.with_suffix(".png")]
    figure_manifest = bundle / "manifests/rq3_size_scaling_figure_manifest.json"
    figure_manifest.write_text(json.dumps({
        "schema_version": "aspg-derived-output-manifest-1.0",
        "rq": "RQ3 shared size scaling figure",
        "input_policy": "Figure is generated only from tidy CSVs, never directly from raw summaries.",
        "inputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in outputs],
        "outputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in figure_outputs],
    }, indent=2) + "\n", encoding="utf-8")
    delivered = [*outputs, *figure_outputs]
    print(json.dumps({
        "timeloop_exact": f"{sum(row['evidence_level'] == 'Exact' for row in tl_rows)}/9",
        "scalesim_under_10pct": f"{sum(bool(row['trend_under_10pct']) for row in ss_rows)}/9",
        "scalesim_max_abs_error_pct": max(float(row["absolute_warm_cycle_error_pct"]) for row in ss_rows),
        "accelergy_valid_ert": f"{sum(bool(row['ert_valid']) for row in energy_rows)}/9",
        "outputs": [common.repo_path(path) for path in delivered],
    }, indent=2))


if __name__ == "__main__":
    main()
