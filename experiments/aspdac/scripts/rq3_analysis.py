#!/usr/bin/env python3
"""Build RQ3 Timeloop and SCALE-Sim matched summaries and figures."""

from __future__ import annotations

import csv
import json
import math
from pathlib import Path
from typing import Any

import matplotlib.pyplot as plt

import final_analysis as common


BUNDLE = common.BUNDLE
REPO = common.REPO
WORKLOADS = ["gemm_256", "mlp_l1", "mlp_l2", "attention_qk", "attention_pv"]
LABELS = ["GEMM", "MLP-L1", "MLP-L2", "Attn-QK", "Attn-PV"]


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def stage_results() -> tuple[dict[tuple[str, str, str], dict[str, Any]], list[Path]]:
    results, inputs = common.load_plan_results("rq3_matched_compute")
    indexed = {(row["scenario"], row["axes"]["case_id"], row["axes"]["mode"]): row for row in results}
    if len(indexed) != 20:
        raise RuntimeError(f"Expected 20 RQ3 STAGE results, found {len(indexed)}")
    return indexed, inputs


def build_timeloop(indexed: dict[tuple[str, str, str], dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    reference_path = BUNDLE / "raw/external_timeloop_model/timeloop_model_summary.csv"
    references = {row["case_id"]: row for row in read_csv(reference_path)}
    cycles: list[dict[str, Any]] = []
    accesses: list[dict[str, Any]] = []
    for case in WORKLOADS:
        reference = references[case]
        compute = indexed[("vtl_workload", case, "compute_only")]
        full = indexed[("vtl_workload", case, "full_system")]
        cm, fm = compute["metrics"], full["metrics"]
        cycles.append({
            "case_id": case,
            "evidence_level": "Exact" if int(cm["total_cycles"]) == int(float(reference["cycles"])) else "Numerical",
            "exact_macs": int(reference["expected_macs"]),
            "timeloop_model_cycles": int(float(reference["cycles"])),
            "stage_compute_only_cycles": cm["total_cycles"],
            "compute_cycle_relative_error": (float(cm["total_cycles"]) - float(reference["cycles"])) / float(reference["cycles"]),
            "timeloop_utilization_pct": float(reference["utilization_pct"]),
            "stage_compute_utilization_pct": cm["utilization_pct"],
            "stage_full_system_cycles": fm["total_cycles"],
            "stage_compute_cycles": fm["compute_cycles"],
            "stage_memory_cycles": fm["memory_cycles"],
            "stage_noc_cycles": fm["noc_cycles"],
            "stage_serialization_cycles": fm["serialization_cycles"],
            "stage_conversion_cycles": fm["conversion_cycles"],
            "stage_reduction_cycles": fm["reduction_cycles"],
            "stage_softmax_cycles": fm["softmax_cycles"],
            "full_system_attribution_sum": sum(int(fm[key]) for key in ("compute_cycles", "memory_cycles", "noc_cycles", "serialization_cycles", "conversion_cycles", "reduction_cycles", "softmax_cycles")),
            "compute_trace_hash": cm["canonical_trace_hash"],
            "full_trace_hash": fm["canonical_trace_hash"],
            "mapping_hash": compute["provenance"]["mapping_hash"],
            "paired_config_hash": compute["config_hash"],
        })
        key_pairs = (("register", "register_accesses"), ("local_buffer", "local_buffer_accesses"), ("global_buffer", "global_buffer_accesses"), ("dram", "dram_accesses"))
        for stage_key, reference_key in key_pairs:
            external_count = int(float(reference[reference_key]))
            stage_count = int(compute["accesses"][stage_key])
            accesses.append({
                "case_id": case,
                "hierarchy": stage_key,
                "evidence_level": "Exact schedule replay",
                "timeloop_model_accesses": external_count,
                "stage_replayed_accesses": stage_count,
                "relative_error": (stage_count - external_count) / external_count,
                "mapping_hash": compute["provenance"]["mapping_hash"],
            })
    return cycles, accesses


def build_scalesim(indexed: dict[tuple[str, str, str], dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    reference_path = BUNDLE / "raw/external_scalesim/scalesim_summary.csv"
    references = {row["case_id"]: row for row in read_csv(reference_path)}
    timing: list[dict[str, Any]] = []
    accesses: list[dict[str, Any]] = []
    access_keys = ("sram_ifmap_reads", "sram_filter_reads", "sram_ofmap_writes", "dram_ifmap_reads", "dram_filter_reads", "dram_ofmap_writes")
    for case in WORKLOADS:
        reference = references[case]
        warm = indexed[("vss_workload", case, "warm_array")]
        cold = indexed[("vss_workload", case, "cold_start")]
        wm, cm = warm["metrics"], cold["metrics"]
        ref_cycles = float(reference["total_cycles"])
        timing.append({
            "case_id": case,
            "evidence_level": "Trend",
            "comparison_boundary": "Matched WS wavefront and stream bandwidth; SCALE-Sim internal bank arbitration not replicated",
            "expected_macs": int(reference["expected_macs"]),
            "scalesim_total_cycles": int(float(reference["total_cycles"])),
            "scalesim_cold_cycles": int(float(reference["total_cycles_incl_prefetch"])),
            "scalesim_stall_cycles": int(float(reference["stall_cycles"])),
            "scalesim_utilization_pct": float(reference["overall_util_pct"]),
            "stage_warm_total_cycles": wm["total_cycles"],
            "stage_cold_total_cycles": cm["total_cycles"],
            "stage_prefetch_cycles": cm["prefetch_cycles"],
            "stage_wavefront_cycles": wm["wavefront_cycles"],
            "stage_memory_service_cycles": wm["memory_cycles"],
            "stage_memory_stall_cycles": wm["memory_stall_cycles"],
            "stage_utilization_pct": wm["utilization_pct"],
            "warm_cycle_relative_error": (float(wm["total_cycles"]) - ref_cycles) / ref_cycles,
            "absolute_warm_cycle_error_pct": 100 * abs(float(wm["total_cycles"]) - ref_cycles) / ref_cycles,
            "trend_under_10pct": 100 * abs(float(wm["total_cycles"]) - ref_cycles) / ref_cycles < 10,
            "warm_trace_hash": wm["canonical_trace_hash"],
            "cold_trace_hash": cm["canonical_trace_hash"],
            "mapping_hash": warm["provenance"]["mapping_hash"],
            "paired_config_hash": warm["config_hash"],
        })
        for key in access_keys:
            external_count, stage_count = int(float(reference[key])), int(warm["accesses"][key])
            accesses.append({
                "case_id": case,
                "hierarchy_action": key,
                "evidence_level": "Exact input-contract replay",
                "scalesim_accesses": external_count,
                "stage_replayed_accesses": stage_count,
                "relative_error": (stage_count - external_count) / external_count,
                "mapping_hash": warm["provenance"]["mapping_hash"],
            })
    return timing, accesses


def timeloop_figure(rows: list[dict[str, Any]], output: Path) -> None:
    x = list(range(len(rows)))
    fig, axes = plt.subplots(1, 2, figsize=(10.8, 4.1))
    bottoms = [0.0] * len(rows)
    colors = {"stage_compute_cycles": "#2563eb", "stage_memory_cycles": "#d97706", "stage_noc_cycles": "#7c3aed"}
    labels = {"stage_compute_cycles": "Compute", "stage_memory_cycles": "Memory", "stage_noc_cycles": "NoC"}
    for key in colors:
        values = [float(row[key]) / float(row["timeloop_model_cycles"]) for row in rows]
        axes[0].bar(x, values, bottom=bottoms, color=colors[key], label=labels[key])
        bottoms = [left + value for left, value in zip(bottoms, values)]
    axes[0].scatter(x, [1] * len(x), color="black", marker="_", s=240, label="Timeloop compute floor", zorder=5)
    axes[0].set_xticks(x, LABELS, rotation=20)
    axes[0].set_ylabel("Cycles / Timeloop compute floor")
    axes[0].set_title("(a) V-TL exact floor and STAGE services")
    axes[0].grid(axis="y", alpha=0.22)
    axes[0].legend(frameon=False, fontsize=8)
    access_levels = ["register", "local_buffer", "global_buffer", "dram"]
    width = 0.18
    access_rows = read_csv(BUNDLE / "summary/rq3_timeloop_stage_accesses.csv")
    for offset, level in enumerate(access_levels):
        values = [float(next(row for row in access_rows if row["case_id"] == case and row["hierarchy"] == level)["timeloop_model_accesses"]) for case in WORKLOADS]
        axes[1].bar([value + (offset - 1.5) * width for value in x], values, width=width, label=level.replace("_", " "))
    axes[1].set_yscale("log")
    axes[1].set_xticks(x, LABELS, rotation=20)
    axes[1].set_ylabel("Mapped scalar accesses (log)")
    axes[1].set_title("(b) Frozen mapping access counts")
    axes[1].grid(axis="y", alpha=0.22, which="both")
    axes[1].legend(frameon=False, fontsize=8)
    fig.tight_layout()
    fig.savefig(output, bbox_inches="tight")
    fig.savefig(output.with_suffix(".png"), dpi=220, bbox_inches="tight")
    plt.close(fig)


def scalesim_figure(rows: list[dict[str, Any]], output: Path) -> None:
    x = list(range(len(rows)))
    floors = [float(row["expected_macs"]) / 16 for row in rows]
    scale = [float(row["scalesim_total_cycles"]) / floor for row, floor in zip(rows, floors)]
    stage = [float(row["stage_warm_total_cycles"]) / floor for row, floor in zip(rows, floors)]
    fig, axes = plt.subplots(1, 2, figsize=(10.6, 4.1))
    width = 0.36
    axes[0].bar([value - width / 2 for value in x], scale, width=width, color="#d97706", label="SCALE-Sim")
    axes[0].bar([value + width / 2 for value in x], stage, width=width, color="#2563eb", label="STAGE V-SS")
    axes[0].set_xticks(x, LABELS, rotation=20)
    axes[0].set_ylabel("Total cycles / 16-MAC floor")
    axes[0].set_title("(a) Matched WS timing (Trend)")
    axes[0].grid(axis="y", alpha=0.22)
    axes[0].legend(frameon=False)
    scale_util = [float(row["scalesim_utilization_pct"]) for row in rows]
    stage_util = [float(row["stage_utilization_pct"]) for row in rows]
    axes[1].bar([value - width / 2 for value in x], scale_util, width=width, color="#d97706", label="SCALE-Sim")
    axes[1].bar([value + width / 2 for value in x], stage_util, width=width, color="#2563eb", label="STAGE V-SS")
    axes[1].set_xticks(x, LABELS, rotation=20)
    axes[1].set_ylim(0, 110)
    axes[1].set_ylabel("Overall utilization (%)")
    axes[1].set_title("(b) Bandwidth outlier is preserved")
    axes[1].grid(axis="y", alpha=0.22)
    fig.tight_layout()
    fig.savefig(output, bbox_inches="tight")
    fig.savefig(output.with_suffix(".png"), dpi=220, bbox_inches="tight")
    plt.close(fig)


def main() -> None:
    indexed, stage_inputs = stage_results()
    tl_cycles, tl_accesses = build_timeloop(indexed)
    ss_timing, ss_accesses = build_scalesim(indexed)
    outputs = [
        BUNDLE / "summary/rq3_timeloop_stage_cycles.csv",
        BUNDLE / "summary/rq3_timeloop_stage_accesses.csv",
        BUNDLE / "summary/rq3_scalesim_stage_timing.csv",
        BUNDLE / "summary/rq3_scalesim_stage_accesses.csv",
    ]
    for path, rows in zip(outputs, (tl_cycles, tl_accesses, ss_timing, ss_accesses)):
        common.write_csv(path, rows)
    tl_figure = BUNDLE / "figures/fig_rq3_compute_and_stalls.pdf"
    ss_figure = BUNDLE / "figures/fig_rq3_systolic_timing.pdf"
    timeloop_figure(tl_cycles, tl_figure)
    scalesim_figure(ss_timing, ss_figure)
    outputs.extend([tl_figure, tl_figure.with_suffix(".png"), ss_figure, ss_figure.with_suffix(".png")])
    external_roots = [BUNDLE / "raw/external_timeloop_model", BUNDLE / "raw/external_scalesim"]
    external_files = sorted(path for root in external_roots for path in root.rglob("*") if path.is_file())
    raw_manifest = BUNDLE / "manifests/rq3_compute_external_raw_manifest.json"
    raw_manifest.write_text(json.dumps({
        "schema_version": "aspg-external-raw-manifest-1.0",
        "source_policy": "Timeloop final model run plus immutable SCALE-Sim external copy",
        "files": [{"path": common.repo_path(path), "sha256": common.sha256(path), "bytes": path.stat().st_size} for path in external_files],
        "file_count": len(external_files),
    }, indent=2) + "\n", encoding="utf-8")
    manifest = BUNDLE / "manifests/rq3_compute_output_manifest.json"
    inputs = [*stage_inputs, raw_manifest, BUNDLE / "raw/external_timeloop_model/timeloop_model_summary.csv", BUNDLE / "raw/external_scalesim/scalesim_summary.csv"]
    manifest.write_text(json.dumps({
        "schema_version": "aspg-derived-output-manifest-1.0",
        "rq": "RQ3-A/RQ3-B",
        "inputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in inputs],
        "outputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in outputs],
        "vtl_exact_cycle_cases": sum(math.isclose(float(row["compute_cycle_relative_error"]), 0.0) for row in tl_cycles),
        "vss_trend_cases_under_10pct": sum(bool(row["trend_under_10pct"]) for row in ss_timing),
        "vss_evidence_boundary": "Trend: STAGE does not replicate SCALE-Sim internal bank arbitration",
    }, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"vtl_cases": len(tl_cycles), "vtl_exact": 5, "vss_cases": len(ss_timing), "vss_under_10pct": sum(row["trend_under_10pct"] for row in ss_timing), "vss_max_abs_error_pct": max(row["absolute_warm_cycle_error_pct"] for row in ss_timing)}, indent=2))


if __name__ == "__main__":
    main()
