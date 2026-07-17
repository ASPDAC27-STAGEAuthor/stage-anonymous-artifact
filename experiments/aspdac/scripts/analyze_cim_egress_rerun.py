#!/usr/bin/env python3
"""Validate and summarize the shared-egress digital/CIM rerun."""

from __future__ import annotations

import argparse
import json
import math
import statistics
from pathlib import Path
from typing import Any

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

import final_analysis as common


REPO = Path(__file__).resolve().parents[3]
DEFAULT_BUNDLE = REPO / "experiments/aspdac/results/cim_egress_rerun_20260717"
DEFAULT_BASELINE = REPO / "experiments/aspdac/results/final_20260716"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, default=DEFAULT_BUNDLE)
    parser.add_argument("--baseline", type=Path, default=DEFAULT_BASELINE)
    return parser.parse_args()


def absolute(path: Path) -> Path:
    return path if path.is_absolute() else REPO / path


def cim_rows(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for result in results:
        if result["scenario"] != "cim_template_comparison":
            continue
        metrics = result["metrics"]
        energy = metrics["energy"]
        provenance = result["provenance"]
        complete = energy["complete_energy_per_operation_pj"]
        if complete is None or energy["unknown_terms"]:
            raise RuntimeError(f"Incomplete energy remained in {result['candidate_id']}")
        rows.append(
            {
                "device": metrics["device"],
                "mode": metrics["mode"],
                "seed": metrics["seed"],
                "repeat": metrics["repeat"],
                "operation_count": metrics["operation_count"],
                "template_id": provenance["template_id"],
                "template_profile_hash": provenance["template_profile_hash"],
                "workload_hash": provenance["workload_hash"],
                "mapping_hash": provenance["mapping_hash"],
                "total_cycles": metrics["total_cycles"],
                "invocation_latency_cycles": metrics["invocation_latency_cycles"],
                "known_energy_per_operation_pj": energy["known_energy_per_operation_pj"],
                "egress_energy_per_operation_pj": energy["egress_energy_per_operation_pj"],
                "complete_energy_per_operation_pj": complete,
                "array_energy_pj": energy["array_pj"],
                "adc_energy_pj": energy["adc_pj"],
                "dac_energy_pj": energy["dac_pj"],
                "decoder_controller_energy_pj": energy["decoder_controller_pj"],
                "buffer_accumulator_energy_pj": energy["buffer_accumulator_pj"],
                "egress_energy_pj": energy["egress_pj"],
                "other_known_energy_pj": energy["other_known_pj"],
                "unknown_terms": ";".join(energy["unknown_terms"]),
                "egress_energy_model_id": provenance["egress_energy_model_id"],
                "egress_energy_pj_per_bit": provenance["egress_energy_pj_per_bit"],
                "egress_output_bits_per_invocation": provenance["egress_output_bits_per_invocation"],
                "output_rmse": metrics["output_rmse"],
                "output_error_result_hash": metrics["output_error_result_hash"],
                "canonical_trace_sha256": metrics["canonical_trace_sha256"],
                "production_kernel_trace_hash": provenance["production_kernel_trace_hash"],
                "candidate_id": result["candidate_id"],
                "evidence_level": "STAGE model comparison with complete shared-egress accounting",
            }
        )
    if len(rows) != 16:
        raise RuntimeError(f"Expected 16 CIM rows, found {len(rows)}")
    if {row["egress_energy_model_id"] for row in rows} != {"component-template-output-bit-v1"}:
        raise RuntimeError("Egress model identity changed across candidates")
    if {int(row["egress_output_bits_per_invocation"]) for row in rows} != {256}:
        raise RuntimeError("Matched output shell is not 256 bits")
    if any(not math.isclose(float(row["egress_energy_pj_per_bit"]), 0.0005, rel_tol=0, abs_tol=0) for row in rows):
        raise RuntimeError("Egress pJ/bit changed")
    if any(not math.isclose(float(row["egress_energy_per_operation_pj"]), 0.000125, rel_tol=0, abs_tol=1e-15) for row in rows):
        raise RuntimeError("Egress pJ/op changed")

    for device in ("digital", "cim"):
        repeats = [row for row in rows if row["device"] == device and row["mode"] == "functional_exact"]
        if len(repeats) != 2 or len({row["canonical_trace_sha256"] for row in repeats}) != 1:
            raise RuntimeError(f"Functional repeats differ for {device}")
    fixed = [row for row in rows if row["mode"] == "characterization_fixed_seed"]
    if len(fixed) != 2 or len({row["canonical_trace_sha256"] for row in fixed}) != 1:
        raise RuntimeError("Fixed-seed CIM repeats differ")
    multi = [row for row in rows if row["mode"] == "characterization_multi_seed"]
    if len(multi) != 10:
        raise RuntimeError("Expected ten multi-seed CIM rows")
    return sorted(rows, key=lambda row: (row["device"], row["mode"], int(row["seed"]), int(row["repeat"])))


def check_baseline(rows: list[dict[str, Any]], baseline_results: list[dict[str, Any]]) -> dict[str, Any]:
    old = {
        result["candidate_id"]: result
        for result in baseline_results
        if result["scenario"] == "cim_template_comparison"
    }
    if set(old) != {row["candidate_id"] for row in rows}:
        raise RuntimeError("Candidate identity differs from the frozen baseline")
    for row in rows:
        prior = old[row["candidate_id"]]
        old_metrics = prior["metrics"]
        old_energy = old_metrics["energy"]
        checks = (
            row["canonical_trace_sha256"] == old_metrics["canonical_trace_sha256"],
            int(row["total_cycles"]) == int(old_metrics["total_cycles"]),
            float(row["output_rmse"]) == float(old_metrics["output_rmse"]),
            float(row["known_energy_per_operation_pj"]) == float(old_energy["known_energy_per_operation_pj"]),
            old_energy["complete_energy_per_operation_pj"] is None,
        )
        if not all(checks):
            raise RuntimeError(f"Baseline invariant changed for {row['candidate_id']}")
    return {
        "candidate_ids_identical": True,
        "trace_hashes_identical": True,
        "cycles_identical": True,
        "rmse_identical": True,
        "known_energy_subtotals_identical": True,
        "baseline_complete_energy_was_null": True,
    }


def statistics_row(rows: list[dict[str, Any]]) -> dict[str, Any]:
    fixed = [row for row in rows if row["mode"] == "characterization_fixed_seed"]
    multi = sorted(
        (row for row in rows if row["mode"] == "characterization_multi_seed"),
        key=lambda row: int(row["seed"]),
    )
    errors = [float(row["output_rmse"]) for row in multi]
    mean = statistics.mean(errors)
    standard_deviation = statistics.stdev(errors)
    half_width = 2.262157 * standard_deviation / math.sqrt(len(errors))
    return {
        "mode": "characterization_multi_seed",
        "sample_count": len(errors),
        "mean_output_rmse": mean,
        "standard_deviation": standard_deviation,
        "student_t_95_low": mean - half_width,
        "student_t_95_high": mean + half_width,
        "fixed_seed_42_rmse": float(fixed[0]["output_rmse"]),
        "fixed_seed_repeat_hash_identical": True,
        "evidence_level": "Trend statistics",
    }


def energy_summary(rows: list[dict[str, Any]]) -> dict[str, Any]:
    digital = next(row for row in rows if row["device"] == "digital" and row["mode"] == "functional_exact")
    cim = next(row for row in rows if row["device"] == "cim" and row["mode"] == "functional_exact")
    operation_count = float(cim["operation_count"])
    components = {
        "array": float(cim["array_energy_pj"]) / operation_count,
        "adc": float(cim["adc_energy_pj"]) / operation_count,
        "dac": float(cim["dac_energy_pj"]) / operation_count,
        "decoder_controller": float(cim["decoder_controller_energy_pj"]) / operation_count,
        "buffer_accumulator": float(cim["buffer_accumulator_energy_pj"]) / operation_count,
        "egress": float(cim["egress_energy_pj"]) / operation_count,
        "other": float(cim["other_known_energy_pj"]) / operation_count,
    }
    cim_complete = float(cim["complete_energy_per_operation_pj"])
    if not math.isclose(sum(components.values()), cim_complete, rel_tol=1e-12, abs_tol=1e-12):
        raise RuntimeError("CIM energy components do not sum to complete energy")
    digital_complete = float(digital["complete_energy_per_operation_pj"])
    return {
        "digital_complete_energy_per_operation_pj": digital_complete,
        "cim_complete_energy_per_operation_pj": cim_complete,
        "cim_to_digital_energy_ratio": cim_complete / digital_complete,
        "cim_adc_fraction": components["adc"] / cim_complete,
        "egress_energy_per_operation_pj": float(cim["egress_energy_per_operation_pj"]),
        "egress_energy_model_id": cim["egress_energy_model_id"],
        "egress_energy_pj_per_bit": float(cim["egress_energy_pj_per_bit"]),
        "output_bits_per_invocation": int(cim["egress_output_bits_per_invocation"]),
        **{f"cim_{key}_energy_per_operation_pj": value for key, value in components.items()},
        "claim_result": "CIM energy advantage not observed in the configured model",
        "evidence_level": "STAGE model comparison",
    }


def plot_energy(summary: dict[str, Any], output: Path) -> None:
    fig, axes = plt.subplots(1, 2, figsize=(7.0, 3.1))
    totals = [
        float(summary["digital_complete_energy_per_operation_pj"]),
        float(summary["cim_complete_energy_per_operation_pj"]),
    ]
    axes[0].bar(["Digital", "CIM"], totals, color=["#333333", "#2f78b7"], width=0.62)
    axes[0].set_yscale("log")
    axes[0].set_ylabel("Modeled energy (pJ/op)")
    axes[0].set_title("(a) Complete modeled energy")
    axes[0].grid(axis="y", alpha=0.25)
    for index, value in enumerate(totals):
        axes[0].text(index, value * 1.18, f"{value:.6f}", ha="center", fontsize=8)

    component_keys = ["array", "adc", "dac", "decoder_controller", "buffer_accumulator", "egress"]
    labels = ["Array", "ADC", "DAC", "Ctrl.", "Buffer", "Egress"]
    values = [float(summary[f"cim_{key}_energy_per_operation_pj"]) for key in component_keys]
    axes[1].bar(labels, values, color=["#4e79a7", "#e15759", "#f28e2b", "#76b7b2", "#59a14f", "#af7aa1"])
    axes[1].set_yscale("log")
    axes[1].set_ylabel("CIM component energy (pJ/op)")
    axes[1].set_title("(b) CIM energy attribution")
    axes[1].tick_params(axis="x", rotation=28)
    axes[1].grid(axis="y", alpha=0.25)
    fig.tight_layout()
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, bbox_inches="tight")
    fig.savefig(output.with_suffix(".png"), dpi=300, bbox_inches="tight")
    plt.close(fig)


def main() -> None:
    args = parse_args()
    bundle = absolute(args.bundle)
    baseline = absolute(args.baseline)
    results, inputs = common.load_plan_results("cim_egress_rerun", bundle)
    baseline_results, baseline_inputs = common.load_plan_results("stage_codesign", baseline)
    rows = cim_rows(results)
    baseline_check = check_baseline(rows, baseline_results)
    stats = statistics_row(rows)
    summary = energy_summary(rows)

    comparison_path = bundle / "summary/stage_cim_comparison.csv"
    statistics_path = bundle / "summary/stage_cim_statistics.csv"
    energy_path = bundle / "summary/stage_cim_energy_summary.csv"
    common.write_csv(comparison_path, rows)
    common.write_csv(statistics_path, [stats])
    common.write_csv(energy_path, [summary])

    figure_path = bundle / "figures/fig_cim_egress_energy.pdf"
    plot_energy(summary, figure_path)

    claim_rows = [
        {
            "claim_id": "C-CIM-REPRO",
            "status": "measured",
            "result": "Fixed-seed hashes remain identical; ten-seed RMSE statistics are unchanged.",
            "evidence": "summary/stage_cim_comparison.csv; summary/stage_cim_statistics.csv",
        },
        {
            "claim_id": "C-CIM-COMPLETE-ENERGY",
            "status": "measured_model",
            "result": f"Shared-egress complete energy is {summary['digital_complete_energy_per_operation_pj']:.9f} pJ/op digital and {summary['cim_complete_energy_per_operation_pj']:.9f} pJ/op CIM.",
            "evidence": "summary/stage_cim_energy_summary.csv",
        },
        {
            "claim_id": "C-CIM-ENERGY-WIN",
            "status": "not_supported",
            "result": "The configured CIM model has higher complete modeled energy than the digital template.",
            "evidence": "summary/stage_cim_energy_summary.csv",
        },
    ]
    claim_csv = bundle / "summary/cim_egress_claim_status.csv"
    common.write_csv(claim_csv, claim_rows)
    claim_md = bundle / "summary/cim_egress_claim_status.md"
    claim_md.write_text(
        "# CIM shared-egress claim status\n\n"
        "| Claim | Status | Result | Evidence |\n"
        "| --- | --- | --- | --- |\n"
        + "\n".join(
            f"| {row['claim_id']} | **{row['status']}** | {row['result']} | {row['evidence']} |"
            for row in claim_rows
        )
        + "\n\n"
        "The same component-template-output-bit-v1 model (0.0005 pJ/bit, 256 output bits per invocation) is applied to both templates. "
        "The digital baseline remains synthetic; the CIM array/ADC retain their declared literature and modeled sources.\n",
        encoding="utf-8",
    )

    outputs = [
        comparison_path,
        statistics_path,
        energy_path,
        figure_path,
        figure_path.with_suffix(".png"),
        claim_csv,
        claim_md,
    ]
    manifest = {
        "schema_version": "aspg-derived-output-manifest-1.0",
        "experiment": "cim_egress_rerun",
        "inputs": [
            {"path": common.repo_path(path), "sha256": common.sha256(path)}
            for path in inputs + baseline_inputs
        ],
        "outputs": [
            {"path": common.repo_path(path), "sha256": common.sha256(path)}
            for path in outputs
        ],
        "case_count": len(rows),
        "failure_count": 0,
        "egress_model_id": summary["egress_energy_model_id"],
        "egress_energy_pj_per_bit": summary["egress_energy_pj_per_bit"],
        "output_bits_per_invocation": summary["output_bits_per_invocation"],
        "baseline_invariants": baseline_check,
        "complete_energy_available": True,
        "energy_win_supported": False,
    }
    manifest_path = bundle / "manifests/cim_egress_output_manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    print(
        json.dumps(
            {
                "cases": len(rows),
                "baseline_invariants": baseline_check,
                "rmse_statistics": stats,
                "energy_summary": summary,
                "manifest": common.repo_path(manifest_path),
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
