#!/usr/bin/env python3
"""Analyze the matched BookSim2/STAGE MNIST CNN transport trace replay."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np


REPO_ROOT = Path(__file__).resolve().parents[3]
BUNDLE_DEFAULT = REPO_ROOT / "experiments/aspdac/results/final_20260716"
CASE_ORDER = ("conv1", "conv2", "fc1", "fc2", "fc3", "sequential_network")
ISOLATED_LAYERS = CASE_ORDER[:5]
TOOL_ORDER = ("BookSim2", "STAGE")
EXPECTED_CASES = 24
EXPECTED_SINGLE_IMAGE_PACKETS = 18_337
FIXED_PDF_DATE = datetime(2026, 7, 16, tzinfo=timezone.utc)
SHARED_METRICS = (
    "trace_sha256",
    "offered_packets",
    "injected_packets",
    "delivered_packets",
    "undrained_packets",
    "total_cycles",
    "network_makespan_cycles",
    "packet_latency_avg",
    "packet_latency_p95",
    "throughput_packets_per_cycle",
    "timeout",
    "canonical_delivery_trace_hash",
)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def repo_path(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return str(path.resolve())


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_csv(path: Path, fieldnames: Iterable[str], rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(fieldnames), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def _tool(record: dict[str, Any]) -> str:
    provider = str(record["provider"])
    if provider == "stage":
        return "STAGE"
    if provider == "booksim2_trace":
        return "BookSim2"
    raise RuntimeError(f"unexpected CNN NoC provider: {provider}")


def _required(metrics: dict[str, Any], name: str) -> Any:
    if name not in metrics:
        raise RuntimeError(f"missing shared metric {name}")
    return metrics[name]


def result_row(record: dict[str, Any], raw_path: Path) -> dict[str, Any]:
    metrics = record.get("metrics") or {}
    provenance = record.get("provenance") or {}
    parameters = record["resolved"]["parameters"]
    shared = {key: _required(metrics, key) for key in SHARED_METRICS}
    computed_metrics_hash = sha256_text(canonical_json(shared))
    return {
        "candidate_id": record["candidate_id"],
        "tool": _tool(record),
        "case_id": record["axes"]["case_id"],
        "seed": int(record["axes"]["seed"]),
        "repeat": int(record["axes"]["repeat"]),
        "status": record["status"],
        "evidence_level": record["evidence_level"],
        "measurement_kind": record["measurement_kind"],
        "trace_csv_path": parameters["trace_csv_path"],
        "trace_sha256": str(shared["trace_sha256"]),
        "registered_trace_sha256": parameters["trace_sha256"],
        "registered_packet_count": int(parameters["packet_count"]),
        "logical_bits": int(parameters["logical_bits"]),
        "wire_bits": int(parameters["wire_bits"]),
        "padding_bits": int(parameters["padding_bits"]),
        "drain_cycles": int(parameters["drain_cycles"]),
        "offered_packets": int(shared["offered_packets"]),
        "injected_packets": int(shared["injected_packets"]),
        "delivered_packets": int(shared["delivered_packets"]),
        "undrained_packets": int(shared["undrained_packets"]),
        "total_cycles": int(shared["total_cycles"]),
        "network_makespan_cycles": int(shared["network_makespan_cycles"]),
        "packet_latency_avg": float(shared["packet_latency_avg"]),
        "packet_latency_p95": float(shared["packet_latency_p95"]),
        "throughput_packets_per_cycle": float(shared["throughput_packets_per_cycle"]),
        "timeout": bool(shared["timeout"]),
        "canonical_delivery_trace_hash": shared["canonical_delivery_trace_hash"],
        "canonical_metrics_hash": metrics.get("canonical_metrics_hash", computed_metrics_hash),
        "computed_shared_metrics_hash": computed_metrics_hash,
        "queue_occupancy_avg_flits": metrics.get("queue_occupancy_avg_flits", ""),
        "queue_occupancy_max_flits": metrics.get("queue_occupancy_max_flits", ""),
        "congestion_cycles": metrics.get("congestion_cycles", ""),
        "router_conflict_stalls": metrics.get("router_conflict_stalls", ""),
        "backpressure_events": metrics.get("backpressure_events", metrics.get("backpressure_cycles", "")),
        "injection_queue_stalls": metrics.get("injection_queue_stalls", ""),
        "config_hash": record["config_hash"],
        "model_hash": parameters["model_hash"],
        "dataset_hash": parameters["dataset_hash"],
        "prediction_hash": parameters["prediction_hash"],
        "lowering_hash": parameters["lowering_hash"],
        "runtime_or_tool": provenance.get("runtime", provenance.get("tool", "")),
        "tool_git_commit": provenance.get("tool_git_commit", ""),
        "adapter_patch_sha256": provenance.get("adapter_patch_sha256", ""),
        "binary_sha256": provenance.get("binary_sha256", ""),
        "raw_path": repo_path(raw_path),
        "claim_boundary": "CNN-shape-derived NoC trace; no CNN arithmetic, accuracy, or cycle-exact cross-tool claim",
    }


def load_rows(bundle: Path) -> tuple[list[dict[str, Any]], list[Path]]:
    raw_paths = sorted((bundle / "raw" / "mnist_cnn_noc_stage").glob("c-*.json"))
    raw_paths += sorted((bundle / "raw" / "mnist_cnn_noc_booksim2").glob("c-*.json"))
    rows = [result_row(read_json(path), path) for path in raw_paths]
    rows.sort(key=lambda row: (TOOL_ORDER.index(row["tool"]), CASE_ORDER.index(row["case_id"]), row["seed"], row["repeat"]))
    return rows, raw_paths


def validate_rows(rows: list[dict[str, Any]], trace_manifest: dict[str, Any]) -> None:
    if len(rows) != EXPECTED_CASES:
        raise RuntimeError(f"expected {EXPECTED_CASES} completed candidate records, found {len(rows)}")
    expected_groups = {(tool, case_id, 40) for tool in TOOL_ORDER for case_id in CASE_ORDER}
    groups: dict[tuple[str, str, int], list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        groups[(row["tool"], row["case_id"], row["seed"])].append(row)
        if row["status"] != "completed":
            raise RuntimeError(f"candidate {row['candidate_id']} is not completed")
        if row["trace_sha256"] != row["registered_trace_sha256"]:
            raise RuntimeError(f"candidate {row['candidate_id']} consumed a different trace")
        if row["registered_packet_count"] != row["offered_packets"]:
            raise RuntimeError(f"candidate {row['candidate_id']} offered count differs from the trace")
    if set(groups) != expected_groups:
        raise RuntimeError(f"unexpected tool/case/seed coverage: {sorted(groups)}")
    for key, members in groups.items():
        if sorted(row["repeat"] for row in members) != [0, 1]:
            raise RuntimeError(f"{key} does not have repeats 0 and 1")
    manifest_cases = {entry["case_id"]: entry for entry in trace_manifest["cases"]}
    if set(manifest_cases) != set(CASE_ORDER):
        raise RuntimeError("trace manifest does not contain the registered six cases")
    if int(manifest_cases["sequential_network"]["packet_count"]) != EXPECTED_SINGLE_IMAGE_PACKETS:
        raise RuntimeError("sequential network trace is not the registered 18,337 packets")
    for row in rows:
        manifest_case = manifest_cases[row["case_id"]]
        if row["trace_sha256"] != manifest_case["trace_sha256"]:
            raise RuntimeError(f"{row['candidate_id']} trace hash differs from trace manifest")


def make_repeat_rows(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    groups: dict[tuple[str, str, int], list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        groups[(row["tool"], row["case_id"], row["seed"])].append(row)
    result = []
    for (tool, case_id, seed), members in sorted(
        groups.items(), key=lambda item: (TOOL_ORDER.index(item[0][0]), CASE_ORDER.index(item[0][1]), item[0][2])
    ):
        indexed = {row["repeat"]: row for row in members}
        first, second = indexed[0], indexed[1]
        result.append(
            {
                "tool": tool,
                "case_id": case_id,
                "seed": seed,
                "trace_sha256": first["trace_sha256"],
                "input_trace_hash_identical": first["trace_sha256"] == second["trace_sha256"],
                "repeat_0_delivery_hash": first["canonical_delivery_trace_hash"],
                "repeat_1_delivery_hash": second["canonical_delivery_trace_hash"],
                "delivery_trace_hash_identical": first["canonical_delivery_trace_hash"] == second["canonical_delivery_trace_hash"],
                "repeat_0_metrics_hash": first["canonical_metrics_hash"],
                "repeat_1_metrics_hash": second["canonical_metrics_hash"],
                "metrics_hash_identical": first["canonical_metrics_hash"] == second["canonical_metrics_hash"],
                "repeat_exact": (
                    first["trace_sha256"] == second["trace_sha256"]
                    and first["canonical_delivery_trace_hash"] == second["canonical_delivery_trace_hash"]
                    and first["canonical_metrics_hash"] == second["canonical_metrics_hash"]
                ),
            }
        )
    return result


def make_cross_tool_rows(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    representatives = {(row["tool"], row["case_id"]): row for row in rows if row["repeat"] == 0}
    result = []
    for case_id in CASE_ORDER:
        book = representatives[("BookSim2", case_id)]
        stage = representatives[("STAGE", case_id)]
        book_latency, stage_latency = float(book["packet_latency_avg"]), float(stage["packet_latency_avg"])
        book_makespan, stage_makespan = int(book["network_makespan_cycles"]), int(stage["network_makespan_cycles"])
        result.append(
            {
                "case_id": case_id,
                "packet_count": book["registered_packet_count"],
                "trace_sha256": book["trace_sha256"],
                "exact_input_trace_match": book["trace_sha256"] == stage["trace_sha256"],
                "booksim_delivered_packets": book["delivered_packets"],
                "stage_delivered_packets": stage["delivered_packets"],
                "both_fully_drained": (
                    not book["timeout"] and not stage["timeout"]
                    and book["undrained_packets"] == 0 and stage["undrained_packets"] == 0
                    and book["delivered_packets"] == book["registered_packet_count"]
                    and stage["delivered_packets"] == stage["registered_packet_count"]
                ),
                "booksim_packet_latency_avg": book_latency,
                "stage_packet_latency_avg": stage_latency,
                "latency_delta_pct_of_booksim": 100.0 * (stage_latency - book_latency) / book_latency if book_latency else math.nan,
                "booksim_packet_latency_p95": book["packet_latency_p95"],
                "stage_packet_latency_p95": stage["packet_latency_p95"],
                "booksim_network_makespan_cycles": book_makespan,
                "stage_network_makespan_cycles": stage_makespan,
                "makespan_delta_pct_of_booksim": 100.0 * (stage_makespan - book_makespan) / book_makespan if book_makespan else math.nan,
                "booksim_throughput_packets_per_cycle": book["throughput_packets_per_cycle"],
                "stage_throughput_packets_per_cycle": stage["throughput_packets_per_cycle"],
                "runtime_evidence_level": "Numerical descriptive",
                "claim_boundary": "same finite trace; no cycle-exact equivalence claim",
            }
        )
    return result


def _rank(values: list[float]) -> list[float]:
    order = sorted(range(len(values)), key=lambda index: values[index])
    ranks = [0.0] * len(values)
    cursor = 0
    while cursor < len(order):
        end = cursor + 1
        while end < len(order) and values[order[end]] == values[order[cursor]]:
            end += 1
        average_rank = (cursor + 1 + end) / 2.0
        for position in range(cursor, end):
            ranks[order[position]] = average_rank
        cursor = end
    return ranks


def spearman_rho(left: list[float], right: list[float]) -> float:
    a, b = np.asarray(_rank(left), dtype=float), np.asarray(_rank(right), dtype=float)
    if np.std(a) == 0 or np.std(b) == 0:
        return 1.0 if np.array_equal(a, b) else 0.0
    return float(np.corrcoef(a, b)[0, 1])


def make_trace_summary(trace_manifest: dict[str, Any], cross_rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    cross_by_case = {row["case_id"]: row for row in cross_rows}
    result = []
    for entry in trace_manifest["cases"]:
        case_id = entry["case_id"]
        result.append(
            {
                "case_id": case_id,
                "packet_count": entry["packet_count"],
                "logical_bits": entry["logical_bits"],
                "wire_bits": entry["wire_bits"],
                "padding_bits": entry["padding_bits"],
                "first_release_cycle": entry["first_release_cycle"],
                "last_release_cycle": entry["last_release_cycle"],
                "drain_cycles": entry["drain_cycles"],
                "trace_csv_path": entry["trace_csv_path"],
                "trace_sha256": entry["trace_sha256"],
                "exact_cross_tool_input_match": cross_by_case[case_id]["exact_input_trace_match"],
                "both_fully_drained": cross_by_case[case_id]["both_fully_drained"],
                "evidence_level": "Exact trace contract",
            }
        )
    result.sort(key=lambda row: CASE_ORDER.index(row["case_id"]))
    return result


def create_figure(cross_rows: list[dict[str, Any]], pdf_path: Path, png_path: Path) -> None:
    rows = {row["case_id"]: row for row in cross_rows}
    labels = ["conv1", "conv2", "fc1", "fc2", "fc3", "network"]
    x = np.arange(len(CASE_ORDER))
    width = 0.37
    figure, axes = plt.subplots(1, 3, figsize=(11.5, 3.8), constrained_layout=True)

    axes[0].bar(x, [rows[case]["packet_count"] for case in CASE_ORDER], color="#4E79A7")
    axes[0].set_yscale("log")
    axes[0].set_xticks(x, labels, rotation=30, ha="right")
    axes[0].set_ylabel("128-bit packets (log scale)")
    axes[0].set_title("(a) Registered trace size")
    axes[0].grid(axis="y", which="both", alpha=0.25)

    axes[1].bar(x - width / 2, [rows[case]["booksim_packet_latency_avg"] for case in CASE_ORDER], width, label="BookSim2", color="#F28E2B")
    axes[1].bar(x + width / 2, [rows[case]["stage_packet_latency_avg"] for case in CASE_ORDER], width, label="STAGE", color="#4E79A7")
    axes[1].set_xticks(x, labels, rotation=30, ha="right")
    axes[1].set_ylabel("Mean packet latency (cycles)")
    axes[1].set_title("(b) Same-trace latency")
    axes[1].legend(frameon=False, fontsize=8)
    axes[1].grid(axis="y", alpha=0.25)

    axes[2].bar(x - width / 2, [rows[case]["booksim_network_makespan_cycles"] for case in CASE_ORDER], width, label="BookSim2", color="#F28E2B")
    axes[2].bar(x + width / 2, [rows[case]["stage_network_makespan_cycles"] for case in CASE_ORDER], width, label="STAGE", color="#4E79A7")
    axes[2].set_yscale("log")
    axes[2].set_xticks(x, labels, rotation=30, ha="right")
    axes[2].set_ylabel("Network makespan (cycles, log)")
    axes[2].set_title("(c) Same-trace completion")
    axes[2].grid(axis="y", which="both", alpha=0.25)

    figure.suptitle("MNIST CNN-shaped NoC trace: BookSim2 vs. STAGE V-BS-CNN", fontsize=12)
    pdf_path.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(
        pdf_path,
        format="pdf",
        bbox_inches="tight",
        metadata={
            "Title": "MNIST CNN NoC trace comparison",
            "Author": "STAGE experiment pipeline",
            "Subject": "BookSim2 and STAGE same-trace numerical comparison",
            "CreationDate": FIXED_PDF_DATE,
            "ModDate": FIXED_PDF_DATE,
        },
    )
    figure.savefig(png_path, format="png", dpi=220, bbox_inches="tight", metadata={"Software": "STAGE deterministic analysis"})
    plt.close(figure)


def analyze(bundle: Path) -> dict[str, Any]:
    bundle = bundle.resolve()
    trace_manifest_path = bundle / "manifests" / "mnist_cnn_noc_trace_manifest.json"
    trace_manifest = read_json(trace_manifest_path)
    rows, raw_paths = load_rows(bundle)
    validate_rows(rows, trace_manifest)
    repeat_rows = make_repeat_rows(rows)
    cross_rows = make_cross_tool_rows(rows)
    trace_rows = make_trace_summary(trace_manifest, cross_rows)

    representatives = {row["case_id"]: row for row in cross_rows if row["case_id"] in ISOLATED_LAYERS}
    booksim_rank_values = [float(representatives[layer]["booksim_network_makespan_cycles"]) for layer in ISOLATED_LAYERS]
    stage_rank_values = [float(representatives[layer]["stage_network_makespan_cycles"]) for layer in ISOLATED_LAYERS]
    rho = spearman_rho(booksim_rank_values, stage_rank_values)
    booksim_dominant = ISOLATED_LAYERS[int(np.argmax(booksim_rank_values))]
    stage_dominant = ISOLATED_LAYERS[int(np.argmax(stage_rank_values))]
    exact_repeats = all(bool(row["repeat_exact"]) for row in repeat_rows)
    all_drained = all(bool(row["both_fully_drained"]) for row in cross_rows)
    trend_pass = rho >= 0.8 and booksim_dominant == stage_dominant
    if not all_drained:
        raise RuntimeError("CNN NoC trace validation failed: at least one matched case did not fully drain")
    if not exact_repeats:
        raise RuntimeError("CNN NoC trace validation failed: at least one deterministic repeat hash differs")

    summary_dir = bundle / "summary"
    run_csv = summary_dir / "mnist_cnn_noc_runs.csv"
    repeat_csv = summary_dir / "mnist_cnn_noc_repeat_hashes.csv"
    cross_csv = summary_dir / "mnist_cnn_noc_cross_tool.csv"
    trace_csv = summary_dir / "mnist_cnn_noc_trace_summary.csv"
    write_csv(run_csv, rows[0].keys(), rows)
    write_csv(repeat_csv, repeat_rows[0].keys(), repeat_rows)
    write_csv(cross_csv, cross_rows[0].keys(), cross_rows)
    write_csv(trace_csv, trace_rows[0].keys(), trace_rows)

    figure_dir = bundle / "figures"
    pdf_path = figure_dir / "fig_mnist_cnn_noc_booksim_stage.pdf"
    png_path = figure_dir / "fig_mnist_cnn_noc_booksim_stage.png"
    create_figure(cross_rows, pdf_path, png_path)

    manifest_path = bundle / "manifests" / "mnist_cnn_noc_output_manifest.json"
    indexed = [
        Path(__file__).resolve(),
        trace_manifest_path,
        *[Path(entry["trace_csv_path"]) if Path(entry["trace_csv_path"]).is_absolute() else REPO_ROOT / entry["trace_csv_path"] for entry in trace_manifest["cases"]],
        *raw_paths,
        run_csv,
        repeat_csv,
        cross_csv,
        trace_csv,
        pdf_path,
        png_path,
    ]
    manifest = {
        "schema_version": "aspg-mnist-cnn-noc-output-manifest-1.0",
        "status": "completed",
        "validation": {
            "candidate_count": len(rows),
            "expected_candidate_count": EXPECTED_CASES,
            "trace_case_count": len(trace_rows),
            "sequential_network_packets": EXPECTED_SINGLE_IMAGE_PACKETS,
            "all_exact_input_trace_matches": all(row["exact_input_trace_match"] for row in cross_rows),
            "all_fully_drained": all_drained,
            "repeat_groups": len(repeat_rows),
            "all_repeat_delivery_and_metrics_hashes_exact": exact_repeats,
            "isolated_layer_makespan_spearman_rho": rho,
            "preregistered_rank_threshold": 0.8,
            "booksim_dominant_layer": booksim_dominant,
            "stage_dominant_layer": stage_dominant,
            "trend_relationship_pass": trend_pass,
        },
        "evidence_boundaries": {
            "trace_contract": "Exact: byte-identical registered trace hash and packet count across tools",
            "runtime": "Numerical descriptive: latency and makespan; no cycle-exact equivalence threshold",
            "trend": "Five isolated-layer makespan rank rho >= 0.8 and the same dominant layer",
            "excluded": "CNN arithmetic, MNIST accuracy, native STAGE tensor trace, and end-to-end system cycles",
        },
        "files": [
            {"path": repo_path(path), "bytes": path.stat().st_size, "sha256": sha256_file(path)}
            for path in indexed
        ],
    }
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, default=BUNDLE_DEFAULT)
    args = parser.parse_args()
    manifest = analyze(args.bundle)
    print(json.dumps({"status": manifest["status"], **manifest["validation"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
