#!/usr/bin/env python3
"""Audit and summarize the Phase 10 MNIST PE precision bundle."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import os
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

import numpy as np

REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from run_mnist_pe_precision import canonical_json, quantize_scalar  # noqa: E402

PROFILES = ("fp32_a32", "fp16_a16", "fp8_a16", "fp8_a8")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def scalar_vmm_codes(payload: dict[str, Any]) -> list[str]:
    arithmetic = payload["arithmetic"]
    rows = int(payload["metrics"]["rows"])
    columns = int(payload["metrics"]["columns"])
    activation = arithmetic["activation_values"]
    weights = arithmetic["weight_values"]
    width = {"fp8": 2, "fp16": 4, "fp32": 8}[arithmetic["output_dtype"]]
    codes: list[str] = []
    for column in range(columns):
        accumulator = quantize_scalar(0.0, arithmetic["accumulate_dtype"])[0]
        for row in range(rows):
            input_value = quantize_scalar(float(activation[row]), arithmetic["input_dtype"])[0]
            weight_value = quantize_scalar(float(weights[row * columns + column]), arithmetic["weight_dtype"])[0]
            accumulator = quantize_scalar(
                accumulator + input_value * weight_value, arithmetic["accumulate_dtype"])[0]
        code = quantize_scalar(accumulator, arithmetic["output_dtype"])[1]
        codes.append(f"{code:0{width}x}")
    return codes


def kernel_gate(bundle: Path) -> dict[str, Any]:
    raw_dir = bundle / "raw/mnist_pe_kernel_conformance_r1"
    paths = sorted(raw_dir.glob("*.json"))
    rows: list[dict[str, Any]] = []
    repeat_groups: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for path in paths:
        payload = json.loads(path.read_text(encoding="utf-8"))
        metrics = payload["metrics"]
        python_codes = scalar_vmm_codes(payload)
        actual_codes = payload["arithmetic"]["actual_encoded_hex"]
        row = {
            "candidate_id": payload["candidate_id"],
            "case_id": metrics["case_id"],
            "profile_id": metrics["profile_id"],
            "repeat": metrics["repeat"],
            "rows": metrics["rows"],
            "columns": metrics["columns"],
            "kernel_reference_exact": bool(metrics["exact_encoded_bits"]),
            "python_reference_exact": python_codes == actual_codes,
            "expected_output_hash": metrics["expected_output_hash"],
            "actual_output_hash": metrics["actual_output_hash"],
            "trace_hash": payload["provenance"]["trace_hash"],
            "raw_path": path.relative_to(REPO_ROOT).as_posix(),
            "raw_sha256": sha256_file(path),
        }
        rows.append(row)
        repeat_groups[(row["case_id"], row["profile_id"])].append(row)
    repeat_exact = all(
        len(group) == 2
        and len({item["actual_output_hash"] for item in group}) == 1
        and len({item["trace_hash"] for item in group}) == 1
        for group in repeat_groups.values())
    profile_counts = {profile: sum(row["profile_id"] == profile for row in rows) for profile in PROFILES}
    checks = {
        "candidate_count_96": len(rows) == 96,
        "profile_counts_24_each": all(profile_counts[profile] == 24 for profile in PROFILES),
        "case_profile_repeat_groups_48": len(repeat_groups) == 48,
        "kernel_reference_exact": all(row["kernel_reference_exact"] for row in rows),
        "python_backend_reference_exact": all(row["python_reference_exact"] for row in rows),
        "repeat_output_and_trace_exact": repeat_exact,
    }
    summary_dir = bundle / "summary"
    summary_dir.mkdir(parents=True, exist_ok=True)
    csv_path = summary_dir / "mnist_pe_precision_kernel_conformance.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    result = {
        "schema_version": "aspg-mnist-pe-kernel-gate-1.0",
        "status": "passed" if all(checks.values()) else "failed",
        "checks": checks,
        "profile_counts": profile_counts,
        "kernel_csv": csv_path.relative_to(REPO_ROOT).as_posix(),
        "kernel_csv_sha256": sha256_file(csv_path),
    }
    write_json(bundle / "manifests/mnist_pe_kernel_gate.json", result)
    if result["status"] != "passed":
        raise RuntimeError(f"Kernel gate failed: {checks}")
    return result


def smoke_gate(bundle: Path) -> dict[str, Any] | None:
    paths = sorted((bundle / "raw/mnist_pe_smoke_r1").glob("*.json"))
    if not paths:
        return None
    rows = [json.loads(path.read_text(encoding="utf-8")) for path in paths]
    profiles = {row["axes"]["profile_id"] for row in rows}
    checks = {
        "candidate_count_4": len(rows) == 4,
        "all_profiles": profiles == set(PROFILES),
        "all_completed": all(row["status"] == "completed" for row in rows),
        "all_gate_checks": all(all(row["gate_checks"].values()) for row in rows),
        "all_repeat_exact": all(row["metrics"]["repeat_exact"] is True for row in rows),
    }
    result = {
        "schema_version": "aspg-mnist-pe-smoke-gate-1.0",
        "status": "passed" if all(checks.values()) else "failed",
        "checks": checks,
        "candidate_ids": [row["candidate_id"] for row in rows],
    }
    write_json(bundle / "manifests/mnist_pe_smoke_gate.json", result)
    if result["status"] != "passed":
        raise RuntimeError(f"Smoke gate failed: {checks}")
    return result


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    if not rows:
        raise RuntimeError(f"No rows for {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    fieldnames = list(rows[0])
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def digest_json(value: Any) -> str:
    return hashlib.sha256(canonical_json(value).encode("utf-8")).hexdigest()


def load_raw(bundle: Path, experiment: str) -> list[dict[str, Any]]:
    return [json.loads(path.read_text(encoding="utf-8")) for path in sorted((bundle / "raw" / experiment).glob("*.json"))]


def paired_bootstrap_ci(delta: np.ndarray, seed: int, resamples: int) -> tuple[float, float]:
    values, counts = np.unique(delta.astype(np.int8), return_counts=True)
    rng = np.random.default_rng(seed)
    sampled_counts = rng.multinomial(len(delta), counts / len(delta), size=resamples)
    estimates = (sampled_counts * values[None, :]).sum(axis=1) / len(delta)
    low, high = np.quantile(estimates, [0.025, 0.975])
    return float(low * 100.0), float(high * 100.0)


def functional_analysis(bundle: Path) -> dict[str, Any]:
    rows = load_raw(bundle, "mnist_pe_functional_r1")
    if len(rows) != 8:
        raise RuntimeError(f"Expected 8 functional candidates, received {len(rows)}")
    indexed = {(row["axes"]["profile_id"], int(row["axes"]["repeat"])): row for row in rows}
    if set(indexed) != {(profile, repeat) for profile in PROFILES for repeat in (0, 1)}:
        raise RuntimeError("Functional profile/repeat matrix is incomplete")
    artifacts: dict[str, dict[str, Any]] = {}
    for profile in PROFILES:
        path = REPO_ROOT / indexed[(profile, 0)]["runs"][0]["prediction_artifact"]
        artifacts[profile] = json.loads(path.read_text(encoding="utf-8"))
    baseline = indexed[("fp32_a32", 0)]
    labels = np.asarray(artifacts["fp32_a32"]["labels"], dtype=np.int16)
    baseline_predictions = np.asarray(artifacts["fp32_a32"]["predictions"], dtype=np.int16)
    if len(labels) != 10000 or any(artifacts[profile]["labels"] != artifacts["fp32_a32"]["labels"] for profile in PROFILES):
        raise RuntimeError("Functional prediction artifacts do not preserve the frozen 10,000-image order")
    config = baseline["resolved"]["base_config"]
    if baseline["runs"][0]["prediction_hash"] != config["model"]["original_prediction_semantic_sha256"]:
        raise RuntimeError("FP32 bridge prediction hash does not match the frozen functional oracle")
    accuracy_rows: list[dict[str, Any]] = []
    prediction_rows: list[dict[str, Any]] = []
    layer_rows: list[dict[str, Any]] = []
    for index, label in enumerate(labels):
        row: dict[str, Any] = {"index": index, "label": int(label)}
        for profile in PROFILES:
            prediction = int(artifacts[profile]["predictions"][index])
            row[f"prediction_{profile}"] = prediction
            row[f"correct_{profile}"] = int(prediction == label)
        prediction_rows.append(row)
    baseline_correct = baseline_predictions == labels
    for profile_index, profile in enumerate(PROFILES):
        first = indexed[(profile, 0)]
        repeated = indexed[(profile, 1)]
        first_run = first["runs"][0]
        repeated_run = repeated["runs"][0]
        if any(first_run[key] != repeated_run[key] for key in ("prediction_hash", "layer_hashes", "summary_hash")):
            raise RuntimeError(f"Functional repeat mismatch for {profile}")
        predictions = np.asarray(artifacts[profile]["predictions"], dtype=np.int16)
        correct = predictions == labels
        delta = correct.astype(np.int8) - baseline_correct.astype(np.int8)
        ci_low, ci_high = paired_bootstrap_ci(
            delta, int(config["functional_bridge"]["bootstrap_seed"]),
            int(config["functional_bridge"]["bootstrap_resamples"]))
        accuracy_rows.append({
            "profile_id": profile,
            "profile_hash": first["profile"]["profile_hash"],
            "correct": int(correct.sum()),
            "images": len(labels),
            "accuracy": round(float(correct.mean()), 8),
            "accuracy_percent": round(float(correct.mean() * 100.0), 6),
            "delta_vs_fp32_percentage_points": round(float((correct.mean() - baseline_correct.mean()) * 100.0), 6),
            "paired_ci95_low_percentage_points": round(ci_low, 6),
            "paired_ci95_high_percentage_points": round(ci_high, 6),
            "prediction_disagreement": int((predictions != baseline_predictions).sum()),
            "baseline_correct_to_profile_wrong": int((baseline_correct & ~correct).sum()),
            "baseline_wrong_to_profile_correct": int((~baseline_correct & correct).sum()),
            "repeat_0_candidate_id": first["candidate_id"],
            "repeat_1_candidate_id": repeated["candidate_id"],
            "prediction_hash": first_run["prediction_hash"],
            "layer_hash_set": digest_json(first_run["layer_hashes"]),
            "summary_hash": first_run["summary_hash"],
            "repeat_exact": True,
            "evidence_level": "Measured functional bridge",
        })
        for layer in ("conv1", "conv2", "fc1", "fc2", "fc3"):
            metrics = first_run["layer_metrics_vs_fp32"][layer]
            layer_rows.append({
                "profile_id": profile,
                "layer_id": layer,
                "elements": metrics["elements"],
                "rmse_vs_fp32": metrics["rmse"],
                "max_abs_error_vs_fp32": metrics["max_abs_error"],
                "cosine_similarity_vs_fp32": metrics["cosine_similarity"],
                "encoded_output_hash": first_run["layer_hashes"][layer],
                "repeat_encoded_output_hash": repeated_run["layer_hashes"][layer],
                "repeat_exact": first_run["layer_hashes"][layer] == repeated_run["layer_hashes"][layer],
            })
    summary = bundle / "summary"
    write_csv(summary / "mnist_pe_precision_accuracy.csv", accuracy_rows)
    write_csv(summary / "mnist_pe_precision_predictions.csv", prediction_rows)
    write_csv(summary / "mnist_pe_precision_layer_error.csv", layer_rows)
    return {
        "raw": rows,
        "indexed": indexed,
        "artifacts": artifacts,
        "accuracy_rows": accuracy_rows,
        "prediction_rows": prediction_rows,
        "layer_rows": layer_rows,
        "config": config,
    }

def system_and_pair_analysis(bundle: Path, functional: dict[str, Any]) -> dict[str, Any]:
    layer_raw = load_raw(bundle, "mnist_pe_stage_layers_r1")
    network_raw = load_raw(bundle, "mnist_pe_stage_network_r1")
    if len(layer_raw) != 80 or len(network_raw) != 8:
        raise RuntimeError(f"Incomplete STAGE matrix: layers={len(layer_raw)}, network={len(network_raw)}")
    system_rows: list[dict[str, Any]] = []
    for payload in layer_raw + network_raw:
        metrics = payload["metrics"]
        provenance = payload["provenance"]
        layer_scope = "layer" if "case_id" in payload["axes"] else "network"
        system_rows.append({
            "scope": layer_scope,
            "candidate_id": payload["candidate_id"],
            "profile_id": payload["axes"]["profile_id"],
            "repeat": int(payload["axes"]["repeat"]),
            "layer_id": payload["axes"].get("case_id", "sequential_network"),
            "mode": payload["axes"].get("mode", "full_system_sequential"),
            "image_count": metrics["image_count"],
            "logical_bits": metrics["logical_bits"],
            "padding_bits": metrics["padding_bits"],
            "packetized_bits": metrics["packetized_bits"],
            "packet_count": metrics["packet_count"],
            "compute_cycles": metrics["compute_cycles"],
            "memory_cycles": metrics["memory_cycles"],
            "noc_cycles": metrics["noc_cycles"],
            "post_op_cycles": metrics["post_op_cycles"],
            "conversion_cycles": metrics["conversion_cycles"],
            "conversion_energy_pj": metrics["conversion_energy_pj"],
            "total_or_sequential_cycles": metrics.get("total_cycles", metrics.get("sequential_cycles")),
            "canonical_trace_hash": metrics["canonical_trace_hash"],
            "compute_timing_distinguishes_arithmetic_profile": metrics["compute_timing_distinguishes_arithmetic_profile"],
            "arithmetic_profile_hash": provenance["arithmetic_profile_hash"],
            "model_hash": provenance["model_hash"],
            "dataset_hash": provenance["dataset_hash"],
            "workload_hash": provenance["workload_hash"],
            "mapping_hash": provenance["mapping_hash"],
            "transport_hash": provenance["transport_hash"],
            "lowering_hash": provenance["lowering_hash"],
        })
    write_csv(bundle / "summary/mnist_pe_precision_system_cost.csv", system_rows)
    network_indexed = {(row["axes"]["profile_id"], int(row["axes"]["repeat"])): row for row in network_raw}
    baseline_network = network_indexed[("fp32_a32", 0)]
    accuracy_by_profile = {row["profile_id"]: row for row in functional["accuracy_rows"]}
    paired_rows: list[dict[str, Any]] = []
    for profile in PROFILES:
        accuracy = accuracy_by_profile[profile]
        network = network_indexed[(profile, 0)]
        metrics = network["metrics"]
        provenance = network["provenance"]
        row = {
            "profile_id": profile,
            "profile_hash": accuracy["profile_hash"],
            "accuracy_candidate_id": accuracy["repeat_0_candidate_id"],
            "system_candidate_id": network["candidate_id"],
            "accuracy_percent": accuracy["accuracy_percent"],
            "delta_vs_fp32_percentage_points": accuracy["delta_vs_fp32_percentage_points"],
            "paired_ci95_low_percentage_points": accuracy["paired_ci95_low_percentage_points"],
            "paired_ci95_high_percentage_points": accuracy["paired_ci95_high_percentage_points"],
            "prediction_disagreement": accuracy["prediction_disagreement"],
            "prediction_hash": accuracy["prediction_hash"],
            "packetized_bits": metrics["packetized_bits"],
            "packet_count": metrics["packet_count"],
            "traffic_reduction_vs_fp32_percent": round(100.0 * (1.0 - metrics["packetized_bits"] / baseline_network["metrics"]["packetized_bits"]), 6),
            "sequential_cycles": metrics["sequential_cycles"],
            "cycle_reduction_vs_fp32_percent": round(100.0 * (1.0 - metrics["sequential_cycles"] / baseline_network["metrics"]["sequential_cycles"]), 6),
            "conversion_cycles": metrics["conversion_cycles"],
            "conversion_energy_pj": metrics["conversion_energy_pj"],
            "compute_timing_distinguishes_arithmetic_profile": metrics["compute_timing_distinguishes_arithmetic_profile"],
            "model_hash": provenance["model_hash"],
            "dataset_hash": provenance["dataset_hash"],
            "sample_order_hash": functional["config"]["dataset"]["sample_order_sha256"],
            "workload_hash": provenance["workload_hash"],
            "mapping_hash": provenance["mapping_hash"],
            "transport_hash": provenance["transport_hash"],
            "lowering_hash": provenance["lowering_hash"],
            "post_op_hash": functional["config"]["contracts"]["post_op_sha256"],
        }
        row["paired_hash"] = digest_json(row)
        paired_rows.append(row)
    write_csv(bundle / "summary/mnist_pe_precision_paired.csv", paired_rows)
    return {
        "layer_raw": layer_raw,
        "network_raw": network_raw,
        "network_indexed": network_indexed,
        "system_rows": system_rows,
        "paired_rows": paired_rows,
    }


def repeat_hash_analysis(bundle: Path, functional: dict[str, Any], system: dict[str, Any]) -> list[dict[str, Any]]:
    repeat_rows: list[dict[str, Any]] = []
    def add(kind: str, key: str, first_id: str, second_id: str, first_hash: str, second_hash: str) -> None:
        repeat_rows.append({
            "evidence_kind": kind, "identity": key,
            "repeat_0_candidate_id": first_id, "repeat_1_candidate_id": second_id,
            "repeat_0_hash": first_hash, "repeat_1_hash": second_hash,
            "exact": first_hash == second_hash,
        })
    for profile in PROFILES:
        first = functional["indexed"][(profile, 0)]
        second = functional["indexed"][(profile, 1)]
        add("functional", profile, first["candidate_id"], second["candidate_id"],
            digest_json({k: first["runs"][0][k] for k in ("prediction_hash", "layer_hashes", "summary_hash")}),
            digest_json({k: second["runs"][0][k] for k in ("prediction_hash", "layer_hashes", "summary_hash")}))
    for payload in load_raw(bundle, "mnist_pe_smoke_r1"):
        profile = payload["axes"]["profile_id"]
        add("smoke_internal", profile, payload["candidate_id"] + ":pass0", payload["candidate_id"] + ":pass1",
            digest_json({k: payload["runs"][0][k] for k in ("prediction_hash", "layer_hashes", "summary_hash")}),
            digest_json({k: payload["runs"][1][k] for k in ("prediction_hash", "layer_hashes", "summary_hash")}))
    kernel_groups: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for payload in load_raw(bundle, "mnist_pe_kernel_conformance_r1"):
        kernel_groups[(payload["axes"]["case_id"], payload["axes"]["profile_id"])].append(payload)
    for key, group in sorted(kernel_groups.items()):
        group.sort(key=lambda item: int(item["axes"]["repeat"]))
        add("kernel", "/".join(key), group[0]["candidate_id"], group[1]["candidate_id"],
            digest_json([group[0]["metrics"]["actual_output_hash"], group[0]["provenance"]["trace_hash"]]),
            digest_json([group[1]["metrics"]["actual_output_hash"], group[1]["provenance"]["trace_hash"]]))
    layer_groups: dict[tuple[str, str, str], list[dict[str, Any]]] = defaultdict(list)
    for payload in system["layer_raw"]:
        layer_groups[(payload["axes"]["profile_id"], payload["axes"]["case_id"], payload["axes"]["mode"])].append(payload)
    for key, group in sorted(layer_groups.items()):
        group.sort(key=lambda item: int(item["axes"]["repeat"]))
        add("stage_layer", "/".join(key), group[0]["candidate_id"], group[1]["candidate_id"],
            group[0]["metrics"]["canonical_trace_hash"], group[1]["metrics"]["canonical_trace_hash"])
    for profile in PROFILES:
        first = system["network_indexed"][(profile, 0)]
        second = system["network_indexed"][(profile, 1)]
        add("stage_network", profile, first["candidate_id"], second["candidate_id"],
            first["metrics"]["canonical_trace_hash"], second["metrics"]["canonical_trace_hash"])
    if len(repeat_rows) != 100 or not all(row["exact"] for row in repeat_rows):
        raise RuntimeError("Repeat-hash matrix is incomplete or non-deterministic")
    write_csv(bundle / "summary/mnist_pe_precision_repeat_hashes.csv", repeat_rows)
    return repeat_rows

def generate_figure(bundle: Path, accuracy_rows: list[dict[str, Any]], paired_rows: list[dict[str, Any]]) -> list[Path]:
    os.environ.setdefault("MPLCONFIGDIR", str(bundle / "runtime/matplotlib"))
    import matplotlib.pyplot as plt
    labels = ["FP32/A32", "FP16/A16", "FP8/A16", "FP8/A8"]
    colors = ["#355C7D", "#4C956C", "#F2A65A", "#C44536"]
    accuracy = [row["accuracy_percent"] for row in accuracy_rows]
    packetized = [row["packetized_bits"] / 1e9 for row in paired_rows]
    deltas = [row["delta_vs_fp32_percentage_points"] for row in accuracy_rows]
    reductions = [row["traffic_reduction_vs_fp32_percent"] for row in paired_rows]
    fig, axes = plt.subplots(1, 2, figsize=(8.2, 3.25), constrained_layout=True)
    x = np.arange(len(labels))
    axes[0].bar(x, accuracy, color=colors, edgecolor="black", linewidth=0.45)
    axes[0].set_xticks(x, labels, rotation=18, ha="right")
    axes[0].set_ylabel("Top-1 accuracy (%)")
    axes[0].set_title("(a) Functional-bridge accuracy", loc="left", fontsize=10)
    axes[0].set_ylim(max(0.0, min(accuracy) - 0.8), 100.0)
    axes[0].grid(axis="y", alpha=0.25, linewidth=0.6)
    for index, (value, delta) in enumerate(zip(accuracy, deltas)):
        axes[0].text(index, value + 0.06, f"{value:.2f}%\n({delta:+.2f} pp)", ha="center", va="bottom", fontsize=7.5)
    axes[1].bar(x, packetized, color=colors, edgecolor="black", linewidth=0.45)
    axes[1].set_xticks(x, labels, rotation=18, ha="right")
    axes[1].set_ylabel("Packetized traffic (Gbit)")
    axes[1].set_title("(b) Paired STAGE traffic", loc="left", fontsize=10)
    axes[1].grid(axis="y", alpha=0.25, linewidth=0.6)
    for index, (value, reduction) in enumerate(zip(packetized, reductions)):
        axes[1].text(index, value + max(packetized) * 0.015, f"{value:.2f}\n(-{reduction:.1f}%)", ha="center", va="bottom", fontsize=7.5)
    fig.suptitle("MNIST sensitivity to STAGE digital-PE arithmetic", fontsize=11)
    figures = bundle / "figures"
    figures.mkdir(parents=True, exist_ok=True)
    png = figures / "fig_mnist_precision_accuracy_cost.png"
    pdf = figures / "fig_mnist_precision_accuracy_cost.pdf"
    fig.savefig(png, dpi=300, bbox_inches="tight")
    fig.savefig(pdf, bbox_inches="tight")
    plt.close(fig)
    return [png, pdf]


def write_claim_documents(bundle: Path, functional: dict[str, Any], system: dict[str, Any], failures: list[Path]) -> list[Path]:
    accuracy = {row["profile_id"]: row for row in functional["accuracy_rows"]}
    paired = {row["profile_id"]: row for row in system["paired_rows"]}
    claim_path = bundle / "summary/mnist_pe_precision_claim_matrix.md"
    claim_lines = [
        "# MNIST PE precision claim matrix", "",
        "| Claim | Status | Evidence | Boundary |", "|---|---|---|---|",
        "| `C-MNIST-PE-ARITH-CONFORMANCE` | `measured/exact` | 96/96 real `CoreDigitalVmmKernel` cases; C# and Python encoded bits exact | Digital PE Conv/FC VMM arithmetic only |",
        "| `C-MNIST-PE-PRECISION-ACCURACY` | `measured/functional-bridge` | 10,000 images × 4 profiles × 2 exact repeats | Bias, ReLU, AvgPool, sequencing, and argmax are the deterministic functional harness |",
        "| `C-MNIST-PE-ACCURACY-COST` | `measured` | 8 accuracy records paired with 88 STAGE layer/network cost records | Compute timing profile does not distinguish accumulator dtype |",
        "| `C-MNIST-STAGE-ENDTOEND` | `not_supported` | No native full-CNN STAGE runtime was introduced | Must remain not supported |",
        "", "## Measured accuracy", "",
        "| Profile | Correct | Accuracy | Delta vs FP32 | 95% paired CI | Disagreements |", "|---|---:|---:|---:|---:|---:|",
    ]
    for profile in PROFILES:
        row = accuracy[profile]
        claim_lines.append(
            f"| `{profile}` | {row['correct']}/10000 | {row['accuracy_percent']:.2f}% | "
            f"{row['delta_vs_fp32_percentage_points']:+.2f} pp | "
            f"[{row['paired_ci95_low_percentage_points']:+.2f}, {row['paired_ci95_high_percentage_points']:+.2f}] pp | "
            f"{row['prediction_disagreement']} |")
    claim_lines += [
        "", "## Evidence discipline", "",
        "- Final R1 matrix: 192/192 terminal candidates plus 4/4 smoke candidates.",
        f"- Retained non-final failure records: {len(failures)}; none is silently removed or admitted into final R1 statistics.",
        "- FP32 bridge reproduces the frozen 98.39% oracle prediction hash exactly.",
        "- FP8/A16 and FP8/A8 share 8-bit transport width; their transport totals match where the frozen system model does not distinguish accumulator dtype.",
        "- No changes were made to `latex/current_overleaf/`.",
    ]
    claim_path.write_text("\n".join(claim_lines) + "\n", encoding="utf-8")
    paper_path = bundle / "summary/mnist_pe_precision_paper_suggestions.md"
    fp8a16 = accuracy["fp8_a16"]
    fp8a8 = accuracy["fp8_a8"]
    pair16 = paired["fp8_a16"]
    paper_lines = [
        "# MNIST PE precision paper suggestions", "", "## Candidate result sentence", "",
        f"We replayed convolutional and fully connected layers with the finite-precision arithmetic implemented by the STAGE digital PE, while a deterministic functional harness retained bias, ReLU, pooling, layer sequencing, and argmax. Relative to FP32/A32 ({accuracy['fp32_a32']['accuracy_percent']:.2f}%), FP8/A16 reached {fp8a16['accuracy_percent']:.2f}% ({fp8a16['delta_vs_fp32_percentage_points']:+.2f} pp) while reducing packetized traffic by {pair16['traffic_reduction_vs_fp32_percent']:.1f}%. The more aggressive FP8/A8 profile reached {fp8a8['accuracy_percent']:.2f}% ({fp8a8['delta_vs_fp32_percentage_points']:+.2f} pp). This is an application-level sensitivity study of the PE arithmetic contract, not a native end-to-end STAGE CNN execution.",
        "", "## Placement recommendation", "",
        "- Admit the compact 1×2 figure only after user review.",
        "- Keep the functional-harness limitation in the method, caption, and result sentence.",
        "- Do not infer PE compute latency or accumulator-energy savings: the frozen compute profile does not distinguish these arithmetic profiles.",
        "- Do not replace the current CNN layer-ordering panel automatically.",
    ]
    paper_path.write_text("\n".join(paper_lines) + "\n", encoding="utf-8")
    return [claim_path, paper_path]


def manifest_index(bundle: Path, kernel: dict[str, Any], smoke: dict[str, Any], outputs: list[Path]) -> Path:
    final_experiments = {
        "mnist_pe_kernel_conformance_r1": 96, "mnist_pe_smoke_r1": 4,
        "mnist_pe_functional_r1": 8, "mnist_pe_stage_layers_r1": 80,
        "mnist_pe_stage_network_r1": 8,
    }
    candidates: dict[str, list[str]] = {}
    for experiment, expected in final_experiments.items():
        rows = load_raw(bundle, experiment)
        if len(rows) != expected:
            raise RuntimeError(f"Manifest count mismatch for {experiment}: {len(rows)} != {expected}")
        candidates[experiment] = sorted(row["candidate_id"] for row in rows)
    failure_paths = sorted((bundle / "failures").glob("*.json"))
    entries = [{"path": path.relative_to(REPO_ROOT).as_posix(), "bytes": path.stat().st_size, "sha256": sha256_file(path)} for path in sorted(outputs)]
    index = {
        "schema_version": "aspg-mnist-pe-precision-manifest-index-1.0",
        "bundle_root": bundle.relative_to(REPO_ROOT).as_posix(),
        "final_plan": "mnist_pe_precision_r1", "final_config": "MNIST-PE-PRECISION-20260717-R1",
        "formal_candidate_count": 192, "smoke_candidate_count": 4,
        "candidate_ids": candidates, "gates": {"kernel": kernel["checks"], "smoke": smoke["checks"]},
        "outputs": entries,
        "retained_failure_records": [{"path": path.relative_to(REPO_ROOT).as_posix(), "sha256": sha256_file(path)} for path in failure_paths],
        "retained_failed_attempt_artifacts": "experiments/aspdac/results/mnist_pe_precision_20260717/runtime/failed_attempts",
        "old_bundles_read_only": True,
        "claim_boundary": {"accuracy_evidence": "Measured functional bridge", "native_stage_full_cnn": False, "C-MNIST-STAGE-ENDTOEND": "not_supported"},
    }
    path = bundle / "manifests/mnist_pe_precision_manifest_index.json"
    write_json(path, index)
    return path


def final_analysis(bundle: Path, kernel: dict[str, Any], smoke: dict[str, Any]) -> dict[str, Any]:
    functional = functional_analysis(bundle)
    system = system_and_pair_analysis(bundle, functional)
    repeat_rows = repeat_hash_analysis(bundle, functional, system)
    figures = generate_figure(bundle, functional["accuracy_rows"], system["paired_rows"])
    failures = sorted((bundle / "failures").glob("*.json"))
    documents = write_claim_documents(bundle, functional, system, failures)
    required = [
        bundle / "summary/mnist_pe_precision_accuracy.csv", bundle / "summary/mnist_pe_precision_predictions.csv",
        bundle / "summary/mnist_pe_precision_layer_error.csv", bundle / "summary/mnist_pe_precision_kernel_conformance.csv",
        bundle / "summary/mnist_pe_precision_system_cost.csv", bundle / "summary/mnist_pe_precision_paired.csv",
        bundle / "summary/mnist_pe_precision_repeat_hashes.csv", *documents, *figures,
    ]
    manifest = manifest_index(bundle, kernel, smoke, required)
    return {
        "status": "passed", "formal_candidates": 192, "smoke_candidates": 4,
        "repeat_groups_exact": len(repeat_rows), "retained_failures": len(failures),
        "manifest": manifest.relative_to(REPO_ROOT).as_posix(),
        "accuracy": {row["profile_id"]: row["accuracy"] for row in functional["accuracy_rows"]},
    }

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--gate-only", action="store_true")
    args = parser.parse_args()
    bundle = args.bundle.resolve()
    kernel = kernel_gate(bundle)
    smoke = smoke_gate(bundle)
    if smoke is None:
        result = {"kernel": kernel, "smoke": None}
    elif args.gate_only:
        result = {"kernel": kernel, "smoke": smoke}
    else:
        result = {"kernel": kernel, "smoke": smoke, "final": final_analysis(bundle, kernel, smoke)}
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())