#!/usr/bin/env python3
"""Validate and summarize STAGE-only bottleneck, precision, causal, and CIM studies."""

from __future__ import annotations

import json
import math
import statistics
from pathlib import Path
from typing import Any

import matplotlib.pyplot as plt

import final_analysis as common


BUNDLE = common.BUNDLE
WORKLOADS = ["attention_128_64", "gemm_256"]


def load() -> tuple[list[dict[str, Any]], list[Path]]:
    results, inputs = common.load_plan_results("stage_codesign")
    if len(results) != 42:
        raise RuntimeError(f"Expected 42 STAGE co-design results, found {len(results)}")
    return results, inputs


def bottleneck_rows(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for result in results:
        if result["scenario"] != "codesign_bottleneck":
            continue
        m, p = result["metrics"], result["provenance"]
        row = {
            "workload": m["WorkloadId"],
            "axis": p["paired_axis"],
            "axis_value": p["paired_axis_value"],
            "macs_per_pe_per_cycle": m["MacsPerPePerCycle"],
            "link_bits_per_cycle": m["LinkBitsPerCycle"],
            "memory_ports": m["MemoryPorts"],
            "queue_depth": m["QueueDepth"],
            "total_cycles": m["TotalCycles"],
            "compute_service_demand_cycles": m["ComputeServiceDemandCycles"],
            "memory_service_demand_cycles": m["MemoryServiceDemandCycles"],
            "noc_service_demand_cycles": m["NocServiceDemandCycles"],
            "compute_critical_cycles": m["ComputeCriticalCycles"],
            "memory_critical_cycles": m["MemoryCriticalCycles"],
            "noc_critical_cycles": m["NocCriticalCycles"],
            "reduction_critical_cycles": m["ReductionCriticalCycles"],
            "softmax_critical_cycles": m["SoftmaxCriticalCycles"],
            "conversion_critical_cycles": m["ConversionCriticalCycles"],
            "dominant_bottleneck": m["DominantBottleneck"],
            "dominant_stall_cycles": m["DominantStallCycles"],
            "dominant_component_id": m["DominantComponentId"],
            "dominant_link_id": m["DominantLinkId"],
            "dominant_packet_evidence": m["DominantPacketEvidence"],
            "dominant_reason": m["DominantReason"],
            "graph_hash": m["GraphHash"],
            "workload_hash": m["WorkloadHash"],
            "mapping_hash": m["MappingHash"],
            "invariant_non_axis_model_hash": m["ModelHash"],
            "candidate_config_hash": result["config_hash"],
            "canonical_trace_sha256": m["CanonicalTraceSha256"],
            "candidate_id": result["candidate_id"],
            "evidence_level": "Measured STAGE-only cycle runtime",
        }
        if sum(int(row[key]) for key in ("compute_critical_cycles", "memory_critical_cycles", "noc_critical_cycles", "reduction_critical_cycles", "softmax_critical_cycles", "conversion_critical_cycles")) != int(row["total_cycles"]):
            raise RuntimeError(f"Critical attribution does not sum for {result['candidate_id']}")
        rows.append(row)
    if len(rows) != 22:
        raise RuntimeError(f"Expected 22 bottleneck rows, found {len(rows)}")
    for workload in WORKLOADS:
        group = [row for row in rows if row["workload"] == workload]
        if len(group) != 11:
            raise RuntimeError(f"Expected 11 bottleneck rows for {workload}")
        for axis, expected in (("pe_rate", 4), ("link_width", 4), ("memory_ports", 3)):
            axis_rows = [row for row in group if row["axis"] == axis]
            if len(axis_rows) != expected:
                raise RuntimeError(f"Expected {expected} {axis} rows for {workload}")
            for key in ("graph_hash", "workload_hash", "mapping_hash", "invariant_non_axis_model_hash"):
                if len({row[key] for row in axis_rows}) != 1:
                    raise RuntimeError(f"Invariant {key} changed in {workload}/{axis}")
        baselines = [
            next(row for row in group if row["axis"] == "pe_rate" and int(row["axis_value"]) == 256),
            next(row for row in group if row["axis"] == "link_width" and int(row["axis_value"]) == 128),
            next(row for row in group if row["axis"] == "memory_ports" and int(row["axis_value"]) == 1),
        ]
        if len({row["canonical_trace_sha256"] for row in baselines}) != 1:
            raise RuntimeError(f"Duplicate baseline traces differ for {workload}")
    return sorted(rows, key=lambda row: (WORKLOADS.index(row["workload"]), row["axis"], int(row["axis_value"])))


def precision_rows(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    order = ["FP16", "BF16", "INT8", "INT4"]
    rows: list[dict[str, Any]] = []
    for result in results:
        if result["scenario"] != "codesign_precision":
            continue
        m, p = result["metrics"], result["provenance"]
        rows.append({
            "precision": m["Precision"],
            "bits_per_element": m["BitsPerElement"],
            "element_count": m["ElementCount"],
            "logical_bits": m["LogicalBits"],
            "padding_bits": m["PaddingBits"],
            "packetized_bits": m["PacketizedBits"],
            "packet_count": m["PacketCount"],
            "total_cycles": m["TotalCycles"],
            "conversion_cycles": m["ConversionCycles"],
            "conversion_energy_pj": m["ConversionEnergyPj"],
            "dominant_bottleneck": m["DominantBottleneck"],
            "workload_hash": p["workload_hash"],
            "mapping_hash": p["mapping_hash"],
            "base_model_hash": p["base_model_hash"],
            "precision_model_hash": p["precision_model_hash"],
            "canonical_trace_sha256": m["CanonicalTraceSha256"],
            "candidate_id": result["candidate_id"],
            "evidence_level": "Measured STAGE-only packet/cycle runtime",
        })
    if len(rows) != 4:
        raise RuntimeError(f"Expected four precision rows, found {len(rows)}")
    rows.sort(key=lambda row: order.index(row["precision"]))
    if len({row["element_count"] for row in rows}) != 1 or len({row["mapping_hash"] for row in rows}) != 1:
        raise RuntimeError("Precision element count or mapping changed")
    expected_bits = {"FP16": 1_048_576, "BF16": 1_048_576, "INT8": 524_288, "INT4": 262_144}
    for row in rows:
        if int(row["logical_bits"]) != expected_bits[row["precision"]]:
            raise RuntimeError(f"Precision logical-bit mismatch: {row}")
    return rows


def causal_rows(bottleneck: list[dict[str, Any]]) -> list[dict[str, Any]]:
    baseline = next(row for row in bottleneck if row["workload"] == "attention_128_64" and row["axis"] == "memory_ports" and int(row["axis_value"]) == 1)
    intervention = next(row for row in bottleneck if row["workload"] == "attention_128_64" and row["axis"] == "memory_ports" and int(row["axis_value"]) == 2)
    if baseline["dominant_bottleneck"] != "memory" or int(baseline["memory_critical_cycles"]) <= 0:
        raise RuntimeError("Causal baseline is not memory-bound")
    if int(intervention["memory_critical_cycles"]) >= int(baseline["memory_critical_cycles"]):
        raise RuntimeError("Memory-port intervention did not reduce the target stall")
    if intervention["dominant_bottleneck"] == "memory":
        raise RuntimeError("Predicted next bottleneck did not appear")
    return [{
        "intervention": "memory_ports_1_to_2",
        "target_stall": "memory",
        "before_total_cycles": baseline["total_cycles"],
        "after_total_cycles": intervention["total_cycles"],
        "before_target_critical_cycles": baseline["memory_critical_cycles"],
        "after_target_critical_cycles": intervention["memory_critical_cycles"],
        "predicted_next_bottleneck": "noc",
        "observed_next_bottleneck": intervention["dominant_bottleneck"],
        "before_component_id": baseline["dominant_component_id"],
        "before_link_id": baseline["dominant_link_id"],
        "before_packet_evidence": baseline["dominant_packet_evidence"],
        "before_reason": baseline["dominant_reason"],
        "after_component_id": intervention["dominant_component_id"],
        "after_link_id": intervention["dominant_link_id"],
        "after_packet_evidence": intervention["dominant_packet_evidence"],
        "after_reason": intervention["dominant_reason"],
        "workload_hash": baseline["workload_hash"],
        "mapping_hash": baseline["mapping_hash"],
        "before_trace_sha256": baseline["canonical_trace_sha256"],
        "after_trace_sha256": intervention["canonical_trace_sha256"],
        "evidence_level": "Measured causal one-parameter intervention",
    }]


def cim_rows(results: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    rows: list[dict[str, Any]] = []
    for result in results:
        if result["scenario"] != "cim_template_comparison":
            continue
        m, p, e = result["metrics"], result["provenance"], result["metrics"]["energy"]
        rows.append({
            "device": m["device"],
            "mode": m["mode"],
            "seed": m["seed"],
            "repeat": m["repeat"],
            "operation_count": m["operation_count"],
            "template_id": p["template_id"],
            "template_profile_hash": p["template_profile_hash"],
            "workload_hash": p["workload_hash"],
            "mapping_hash": p["mapping_hash"],
            "total_cycles": m["total_cycles"],
            "invocation_latency_cycles": m["invocation_latency_cycles"],
            "known_energy_per_operation_pj": e["known_energy_per_operation_pj"],
            "complete_energy_per_operation_pj": "unknown" if e["complete_energy_per_operation_pj"] is None else e["complete_energy_per_operation_pj"],
            "array_energy_pj": e["array_pj"],
            "adc_energy_pj": e["adc_pj"],
            "dac_energy_pj": e["dac_pj"],
            "decoder_controller_energy_pj": e["decoder_controller_pj"],
            "buffer_accumulator_energy_pj": e["buffer_accumulator_pj"],
            "other_known_energy_pj": e["other_known_pj"],
            "unknown_terms": ";".join(e["unknown_terms"]),
            "output_rmse": m["output_rmse"],
            "output_error_result_hash": m["output_error_result_hash"],
            "footprint_area_um2": m["footprint_area_um2"],
            "footprint_complete": m["footprint_complete"],
            "known_area_subtotal_um2": m["known_area_subtotal_um2"],
            "array_profile_hash": p["array_profile_hash"],
            "adc_profile_hash": p["adc_profile_hash"],
            "nonideal_evidence": p["nonideal_evidence"],
            "canonical_trace_sha256": m["canonical_trace_sha256"],
            "production_kernel_trace_hash": p["production_kernel_trace_hash"],
            "candidate_id": result["candidate_id"],
            "evidence_level": "Trend: STAGE template comparison with incomplete energy totals",
        })
    if len(rows) != 16:
        raise RuntimeError(f"Expected 16 CIM comparison rows, found {len(rows)}")
    for device in ("digital", "cim"):
        repeats = [row for row in rows if row["device"] == device and row["mode"] == "functional_exact"]
        if len(repeats) != 2 or len({row["canonical_trace_sha256"] for row in repeats}) != 1 or any(float(row["output_rmse"]) != 0 for row in repeats):
            raise RuntimeError(f"Functional repeats failed for {device}")
    fixed = [row for row in rows if row["mode"] == "characterization_fixed_seed"]
    if len(fixed) != 2 or len({row["canonical_trace_sha256"] for row in fixed}) != 1 or len({row["output_error_result_hash"] for row in fixed}) != 1:
        raise RuntimeError("Fixed-seed CIM repeats differ")
    multi = sorted((row for row in rows if row["mode"] == "characterization_multi_seed"), key=lambda row: int(row["seed"]))
    if len(multi) != 10:
        raise RuntimeError("Expected 10 CIM multi-seed rows")
    errors = [float(row["output_rmse"]) for row in multi]
    mean = statistics.mean(errors)
    std = statistics.stdev(errors)
    half = 2.262157 * std / math.sqrt(len(errors))
    stats = [{
        "mode": "characterization_multi_seed",
        "sample_count": len(errors),
        "mean_output_rmse": mean,
        "standard_deviation": std,
        "student_t_95_low": mean - half,
        "student_t_95_high": mean + half,
        "fixed_seed_42_rmse": float(fixed[0]["output_rmse"]),
        "fixed_seed_repeat_hash_identical": True,
        "effect_evidence": multi[0]["nonideal_evidence"],
        "uncertainty_boundary": "ADC-resolution-equivalent derived sigma; not measured ReRAM device noise",
        "evidence_level": "Trend statistics",
    }]
    return sorted(rows, key=lambda row: (row["device"], row["mode"], int(row["seed"]), int(row["repeat"]))), stats


def codesign_figure(bottleneck: list[dict[str, Any]], precision: list[dict[str, Any]], causal: list[dict[str, Any]], output: Path) -> None:
    fig, axes = plt.subplots(2, 2, figsize=(10.8, 7.2))
    for workload, color, label in (("attention_128_64", "#2563eb", "Attention"), ("gemm_256", "#d97706", "GEMM")):
        rows = sorted((row for row in bottleneck if row["workload"] == workload and row["axis"] == "pe_rate"), key=lambda row: int(row["axis_value"]))
        axes[0, 0].plot([row["axis_value"] for row in rows], [row["total_cycles"] for row in rows], marker="o", color=color, label=label)
    axes[0, 0].set_xscale("log", base=2)
    axes[0, 0].set_yscale("log", base=2)
    axes[0, 0].set_xlabel("MAC/PE/cycle")
    axes[0, 0].set_ylabel("Total cycles (log2)")
    axes[0, 0].set_title("(a) Compute scaling and plateau")
    axes[0, 0].grid(alpha=0.22)
    axes[0, 0].legend(frameon=False)

    attention = sorted((row for row in bottleneck if row["workload"] == "attention_128_64" and row["axis"] == "link_width"), key=lambda row: int(row["axis_value"]))
    axes[0, 1].plot([row["axis_value"] for row in attention], [row["total_cycles"] for row in attention], marker="o", color="#7c3aed")
    for row in attention:
        axes[0, 1].annotate(row["dominant_bottleneck"], (row["axis_value"], row["total_cycles"]), xytext=(0, 7), textcoords="offset points", ha="center", fontsize=7)
    axes[0, 1].set_xscale("log", base=2)
    axes[0, 1].set_xlabel("Link bits/cycle")
    axes[0, 1].set_ylabel("Attention cycles")
    axes[0, 1].set_title("(b) NoC-to-memory migration")
    axes[0, 1].grid(alpha=0.22)

    labels = [row["precision"] for row in precision]
    x = list(range(len(labels)))
    axes[1, 0].bar(x, [float(row["packetized_bits"]) / 1_000_000 for row in precision], color="#16a34a", label="Packetized Mbit")
    twin = axes[1, 0].twinx()
    twin.plot(x, [row["total_cycles"] for row in precision], color="#dc2626", marker="o", label="Cycles")
    axes[1, 0].set_xticks(x, labels)
    axes[1, 0].set_ylabel("Packetized traffic (Mbit)", color="#16a34a")
    twin.set_ylabel("Total cycles", color="#dc2626")
    axes[1, 0].set_title("(c) Precision traffic and conversion")

    event = causal[0]
    before = next(row for row in bottleneck if row["workload"] == "attention_128_64" and row["axis"] == "memory_ports" and int(row["axis_value"]) == 1)
    after = next(row for row in bottleneck if row["workload"] == "attention_128_64" and row["axis"] == "memory_ports" and int(row["axis_value"]) == 2)
    categories = [("compute_critical_cycles", "Compute", "#2563eb"), ("memory_critical_cycles", "Memory", "#d97706"), ("noc_critical_cycles", "NoC", "#7c3aed"), ("reduction_critical_cycles", "Reduction", "#16a34a"), ("softmax_critical_cycles", "Softmax", "#dc2626")]
    bottom = [0.0, 0.0]
    for key, label, color in categories:
        values = [float(before[key]), float(after[key])]
        axes[1, 1].bar([0, 1], values, bottom=bottom, color=color, label=label)
        bottom = [left + value for left, value in zip(bottom, values)]
    axes[1, 1].set_xticks([0, 1], ["1 mem port", "2 mem ports"])
    axes[1, 1].set_ylabel("Critical-path cycles")
    axes[1, 1].set_title(f"(d) Causal intervention: {event['observed_next_bottleneck']} next")
    axes[1, 1].legend(frameon=False, fontsize=7, ncol=2)
    fig.tight_layout()
    fig.savefig(output, bbox_inches="tight")
    fig.savefig(output.with_suffix(".png"), dpi=220, bbox_inches="tight")
    plt.close(fig)


def cim_figure(rows: list[dict[str, Any]], stats: list[dict[str, Any]], output: Path) -> None:
    digital = next(row for row in rows if row["device"] == "digital" and row["mode"] == "functional_exact")
    cim = next(row for row in rows if row["device"] == "cim" and row["mode"] == "functional_exact")
    multi = sorted((row for row in rows if row["mode"] == "characterization_multi_seed"), key=lambda row: int(row["seed"]))
    fig, axes = plt.subplots(1, 2, figsize=(9.8, 3.9))
    x = [0, 1]
    axes[0].bar([value - 0.18 for value in x], [float(digital["total_cycles"]), float(cim["total_cycles"])], width=0.36, color="#2563eb", label="Cycles")
    twin = axes[0].twinx()
    twin.bar([value + 0.18 for value in x], [float(digital["known_energy_per_operation_pj"]), float(cim["known_energy_per_operation_pj"])], width=0.36, color="#d97706", label="Known pJ/op")
    axes[0].set_xticks(x, ["Digital", "CIM"])
    axes[0].set_ylabel("Template runtime cycles", color="#2563eb")
    twin.set_ylabel("Known energy/op (pJ)", color="#d97706")
    axes[0].set_title("(a) Functional template comparison (Trend)")
    axes[0].grid(axis="y", alpha=0.22)

    seeds = [int(row["seed"]) for row in multi]
    errors = [float(row["output_rmse"]) for row in multi]
    axes[1].scatter(seeds, errors, color="#7c3aed")
    axes[1].axhline(float(stats[0]["mean_output_rmse"]), color="#dc2626", label="Mean")
    axes[1].fill_between([min(seeds), max(seeds)], float(stats[0]["student_t_95_low"]), float(stats[0]["student_t_95_high"]), color="#dc2626", alpha=0.15, label="95% CI")
    axes[1].set_xlabel("Seed")
    axes[1].set_ylabel("Output RMSE")
    axes[1].set_title("(b) Characterization-driven variation")
    axes[1].grid(alpha=0.22)
    axes[1].legend(frameon=False)
    fig.tight_layout()
    fig.savefig(output, bbox_inches="tight")
    fig.savefig(output.with_suffix(".png"), dpi=220, bbox_inches="tight")
    plt.close(fig)


def main() -> None:
    results, inputs = load()
    bottleneck = bottleneck_rows(results)
    precision = precision_rows(results)
    causal = causal_rows(bottleneck)
    cim, cim_stats = cim_rows(results)
    outputs = [
        BUNDLE / "summary/stage_codesign_bottleneck.csv",
        BUNDLE / "summary/stage_precision.csv",
        BUNDLE / "summary/stage_causal_intervention.csv",
        BUNDLE / "summary/stage_cim_comparison.csv",
        BUNDLE / "summary/stage_cim_statistics.csv",
    ]
    for path, rows in zip(outputs, (bottleneck, precision, causal, cim, cim_stats)):
        common.write_csv(path, rows)
    codesign_fig = BUNDLE / "figures/fig_stage_codesign.pdf"
    cim_fig = BUNDLE / "figures/fig_stage_cim.pdf"
    codesign_figure(bottleneck, precision, causal, codesign_fig)
    cim_figure(cim, cim_stats, cim_fig)
    outputs.extend([codesign_fig, codesign_fig.with_suffix(".png"), cim_fig, cim_fig.with_suffix(".png")])
    manifest = BUNDLE / "manifests/stage_codesign_output_manifest.json"
    manifest.write_text(json.dumps({
        "schema_version": "aspg-derived-output-manifest-1.0",
        "rq": "STAGE-only co-design and digital/CIM",
        "inputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in inputs],
        "outputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in outputs],
        "bottleneck_cases": 22,
        "precision_cases": 4,
        "causal_interventions": 1,
        "cim_cases": 16,
        "fixed_seed_reproducible": True,
        "multi_seed_count": 10,
        "energy_boundary": "Complete digital and CIM totals remain unknown due to egress energy; known subtotals are Trend only",
        "profile_boundary": "Phase7C digital is synthetic; Phase9 CIM uses literature exact-point array/ADC plus model-derived peripherals",
    }, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "bottleneck_cases": len(bottleneck),
        "precision_cases": len(precision),
        "causal": causal[0],
        "cim_cases": len(cim),
        "cim_rmse_statistics": cim_stats[0],
    }, indent=2))


if __name__ == "__main__":
    main()
