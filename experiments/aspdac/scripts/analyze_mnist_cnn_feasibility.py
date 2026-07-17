#!/usr/bin/env python3
"""Validate and summarize the MNIST functional oracle and STAGE layer replay.

This analysis deliberately keeps two claim domains separate:

* PyTorch establishes end-to-end numerical MNIST accuracy.
* STAGE establishes deterministic cycle/data-movement feasibility for exact
  Conv/im2col and fully connected layer shapes.

It fails closed if the expected 20 STAGE cases, repeat determinism, exact MAC
counts, or the registered 98.39% functional result are absent.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import yaml


EXPECTED_ACCURACY = 0.9839
EXPECTED_MACS_PER_IMAGE = 281_640
EXPECTED_TEST_SAMPLES = 10_000
EXPECTED_STAGE_CASES = 20
LAYER_ORDER = ("conv1", "conv2", "fc1", "fc2", "fc3")
MODE_ORDER = ("compute_only", "full_system")
FIXED_PDF_DATE = datetime(2026, 7, 16, tzinfo=timezone.utc)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_csv(path: Path, fieldnames: Iterable[str], rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(fieldnames), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def load_confusion_matrix(path: Path) -> np.ndarray:
    with path.open(newline="", encoding="utf-8") as handle:
        reader = csv.DictReader(handle)
        rows = [[int(row[f"pred_{index}"]) for index in range(10)] for row in reader]
    matrix = np.asarray(rows, dtype=np.int64)
    if matrix.shape != (10, 10):
        raise RuntimeError(f"expected a 10x10 confusion matrix, found {matrix.shape}")
    return matrix


def stage_row(record: dict[str, Any]) -> dict[str, Any]:
    metrics = record["metrics"]
    parameters = record["resolved"]["parameters"]
    provenance = record["provenance"]
    axes = record["axes"]
    return {
        "candidate_id": record["candidate_id"],
        "status": record["status"],
        "evidence_level": record["evidence_level"],
        "measurement_kind": record["measurement_kind"],
        "layer_id": metrics["layer_id"],
        "layer_kind": metrics["layer_kind"],
        "mode": metrics["mode"],
        "repeat": int(axes["repeat"]),
        "M": int(parameters["M"]),
        "N": int(parameters["N"]),
        "K": int(parameters["K"]),
        "image_count": int(metrics["image_count"]),
        "completed_macs": int(metrics["completed_macs"]),
        "input_elements": int(metrics["input_elements"]),
        "weight_elements": int(metrics["weight_elements"]),
        "output_elements": int(metrics["output_elements"]),
        "logical_bits": int(metrics["logical_bits"]),
        "compute_cycles": int(metrics["compute_cycles"]),
        "memory_cycles": int(metrics["memory_cycles"]),
        "noc_cycles": int(metrics["noc_cycles"]),
        "post_op_cycles": int(metrics["post_op_cycles"]),
        "total_cycles": int(metrics["total_cycles"]),
        "compute_utilization_pct": float(metrics["compute_utilization_pct"]),
        "dominant_service": metrics["dominant_service"],
        "canonical_trace_hash": metrics["canonical_trace_hash"],
        "config_hash": record["config_hash"],
        "mapping_hash": provenance["mapping_hash"],
        "model_hash": provenance["model_hash"],
        "dataset_hash": provenance["dataset_hash"],
        "prediction_hash": provenance["prediction_hash"],
        "lowering_hash": provenance["lowering_hash"],
        "claim_boundary": "STAGE cycle/data-movement feasibility; not end-to-end numerical CNN accuracy",
    }


def validate_stage_rows(rows: list[dict[str, Any]], layer_macs: dict[str, int]) -> None:
    if len(rows) != EXPECTED_STAGE_CASES:
        raise RuntimeError(f"expected {EXPECTED_STAGE_CASES} STAGE cases, found {len(rows)}")
    if any(row["status"] != "completed" for row in rows):
        incomplete = [row["candidate_id"] for row in rows if row["status"] != "completed"]
        raise RuntimeError(f"non-completed STAGE cases: {incomplete}")

    groups: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        key = (str(row["layer_id"]), str(row["mode"]))
        groups[key].append(row)

    expected_groups = {(layer, mode) for layer in LAYER_ORDER for mode in MODE_ORDER}
    if set(groups) != expected_groups:
        raise RuntimeError(f"unexpected layer/mode coverage: {sorted(groups)}")

    compared_fields = (
        "completed_macs",
        "input_elements",
        "weight_elements",
        "output_elements",
        "logical_bits",
        "compute_cycles",
        "memory_cycles",
        "noc_cycles",
        "post_op_cycles",
        "total_cycles",
        "compute_utilization_pct",
        "dominant_service",
        "canonical_trace_hash",
        "mapping_hash",
    )
    for key, members in groups.items():
        if len(members) != 2 or sorted(int(row["repeat"]) for row in members) != [0, 1]:
            raise RuntimeError(f"{key} does not contain exactly repeats 0 and 1")
        baseline = members[0]
        for member in members[1:]:
            differing = [field for field in compared_fields if member[field] != baseline[field]]
            if differing:
                raise RuntimeError(f"repeat mismatch for {key}: {differing}")
        expected_macs = layer_macs[key[0]]
        if any(int(member["completed_macs"]) != expected_macs for member in members):
            raise RuntimeError(f"exact MAC mismatch for {key[0]}")

    for mode in MODE_ORDER:
        unique_total = sum(
            int(groups[(layer, mode)][0]["completed_macs"]) for layer in LAYER_ORDER
        )
        if unique_total != EXPECTED_MACS_PER_IMAGE:
            raise RuntimeError(f"{mode} MAC total is {unique_total}, expected {EXPECTED_MACS_PER_IMAGE}")


def make_network_rows(stage_rows: list[dict[str, Any]], macs_full_test: int) -> list[dict[str, Any]]:
    rows_by_key = {(row["layer_id"], row["mode"], row["repeat"]): row for row in stage_rows}
    result: list[dict[str, Any]] = []
    for mode in MODE_ORDER:
        representative = [rows_by_key[(layer, mode, 0)] for layer in LAYER_ORDER]
        sums = {
            field: sum(int(row[field]) for row in representative)
            for field in (
                "completed_macs",
                "logical_bits",
                "compute_cycles",
                "memory_cycles",
                "noc_cycles",
                "post_op_cycles",
                "total_cycles",
            )
        }
        service_totals = {
            "compute": sums["compute_cycles"],
            "memory": sums["memory_cycles"],
            "noc": sums["noc_cycles"],
            "post_op": sums["post_op_cycles"],
        }
        dominant = max(service_totals, key=service_totals.get)
        result.append(
            {
                "mode": mode,
                "evidence_level": "trend",
                "repeat_count": 2,
                "layer_count": len(LAYER_ORDER),
                "completed_macs_per_image": sums["completed_macs"],
                "functional_macs_full_test_set": macs_full_test,
                "logical_bits_per_image": sums["logical_bits"],
                "compute_cycles_per_image": sums["compute_cycles"],
                "memory_cycles_per_image": sums["memory_cycles"],
                "noc_cycles_per_image": sums["noc_cycles"],
                "post_op_cycles_per_image": sums["post_op_cycles"],
                "total_cycles_per_image": sums["total_cycles"],
                "dominant_service": dominant,
                "trace_hash_repeat_identical": True,
                "interpretation": (
                    "Exact-shape STAGE cycle/data-movement feasibility per image; "
                    "no end-to-end STAGE numerical accuracy claim"
                ),
            }
        )
    return result


def create_figure(
    confusion: np.ndarray,
    stage_rows: list[dict[str, Any]],
    accuracy_pct: float,
    pdf_path: Path,
    png_path: Path,
) -> None:
    rows = {(row["layer_id"], row["mode"], row["repeat"]): row for row in stage_rows}
    figure, axes = plt.subplots(2, 2, figsize=(10.6, 7.2), constrained_layout=True)

    ax = axes[0, 0]
    image = ax.imshow(confusion, cmap="Blues", aspect="equal")
    ax.set_xticks(range(10))
    ax.set_yticks(range(10))
    ax.set_xlabel("Predicted digit")
    ax.set_ylabel("True digit")
    ax.set_title(f"(a) PyTorch oracle confusion matrix — {accuracy_pct:.2f}%")
    figure.colorbar(image, ax=ax, fraction=0.046, pad=0.04, label="Images")

    ax = axes[0, 1]
    class_totals = confusion.sum(axis=1)
    class_accuracy = 100.0 * np.diag(confusion) / class_totals
    bars = ax.bar(np.arange(10), class_accuracy, color="#4C78A8")
    ax.axhline(accuracy_pct, color="#E45756", linewidth=1.5, linestyle="--", label="Overall")
    ax.set_ylim(94.0, 100.0)
    ax.set_xticks(range(10))
    ax.set_xlabel("Digit")
    ax.set_ylabel("Accuracy (%)")
    ax.set_title("(b) Functional accuracy by class")
    ax.legend(frameon=False, loc="lower left")
    for bar, value in zip(bars, class_accuracy):
        ax.text(bar.get_x() + bar.get_width() / 2, value + 0.12, f"{value:.1f}", ha="center", va="bottom", fontsize=7)
    ax.grid(axis="y", alpha=0.25)

    ax = axes[1, 0]
    x = np.arange(len(LAYER_ORDER))
    width = 0.36
    compute_only = [int(rows[(layer, "compute_only", 0)]["total_cycles"]) for layer in LAYER_ORDER]
    full_system = [int(rows[(layer, "full_system", 0)]["total_cycles"]) for layer in LAYER_ORDER]
    ax.bar(x - width / 2, compute_only, width, label="Compute-only", color="#59A14F")
    ax.bar(x + width / 2, full_system, width, label="Full-system", color="#F28E2B")
    ax.set_yscale("log")
    ax.set_xticks(x, LAYER_ORDER)
    ax.set_ylabel("Cycles per image (log scale)")
    ax.set_title("(c) STAGE exact-shape layer runtime")
    ax.legend(frameon=False)
    ax.grid(axis="y", which="both", alpha=0.25)

    ax = axes[1, 1]
    services = (
        ("compute_cycles", "Compute", "#59A14F"),
        ("memory_cycles", "Memory", "#4E79A7"),
        ("noc_cycles", "NoC", "#E15759"),
        ("post_op_cycles", "Post-op", "#B07AA1"),
    )
    left = np.zeros(len(LAYER_ORDER), dtype=float)
    for field, label, color in services:
        values = np.asarray([int(rows[(layer, "full_system", 0)][field]) for layer in LAYER_ORDER])
        ax.barh(np.arange(len(LAYER_ORDER)), values, left=left, label=label, color=color)
        left += values
    ax.set_yticks(np.arange(len(LAYER_ORDER)), LAYER_ORDER)
    ax.invert_yaxis()
    ax.set_xlabel("Service cycles per image")
    ax.set_title("(d) STAGE full-system service breakdown")
    ax.legend(frameon=False, ncol=2, fontsize=8)
    ax.grid(axis="x", alpha=0.25)

    figure.suptitle(
        "MNIST feasibility: PyTorch numerical oracle + STAGE cycle/data-movement replay",
        fontsize=12,
    )
    pdf_path.parent.mkdir(parents=True, exist_ok=True)
    pdf_metadata = {
        "Title": "MNIST CNN feasibility",
        "Author": "STAGE experiment pipeline",
        "Subject": "PyTorch functional oracle and STAGE cycle/data-movement feasibility",
        "CreationDate": FIXED_PDF_DATE,
        "ModDate": FIXED_PDF_DATE,
    }
    figure.savefig(pdf_path, format="pdf", bbox_inches="tight", metadata=pdf_metadata)
    figure.savefig(
        png_path,
        format="png",
        dpi=220,
        bbox_inches="tight",
        metadata={"Software": "STAGE deterministic MNIST analysis"},
    )
    plt.close(figure)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--bundle",
        type=Path,
        default=Path("experiments/aspdac/results/final_20260716"),
    )
    args = parser.parse_args()
    bundle = args.bundle.resolve()
    functional_dir = bundle / "raw" / "mnist_cnn"
    stage_dir = bundle / "raw" / "mnist_cnn_stage_layers"
    summary_dir = bundle / "summary"
    figure_dir = bundle / "figures"
    manifest_dir = bundle / "manifests"

    functional = read_json(functional_dir / "summary.json")
    if functional["status"] != "completed":
        raise RuntimeError("functional MNIST oracle is not completed")
    if int(functional["test_samples"]) != EXPECTED_TEST_SAMPLES:
        raise RuntimeError("functional oracle did not evaluate the full 10,000-image test set")
    if float(functional["test_repeat_1"]["accuracy"]) != EXPECTED_ACCURACY:
        raise RuntimeError("functional accuracy differs from the registered 98.39% result")
    if float(functional["test_repeat_2"]["accuracy"]) != EXPECTED_ACCURACY:
        raise RuntimeError("repeat functional accuracy differs from 98.39%")
    if not functional["repeat_prediction_hash_identical"]:
        raise RuntimeError("functional prediction hashes are not identical")
    if int(functional["macs_per_image"]) != EXPECTED_MACS_PER_IMAGE:
        raise RuntimeError("functional MAC total is not 281,640 per image")

    with (functional_dir / "layer_profiles.csv").open(newline="", encoding="utf-8") as handle:
        profiles = list(csv.DictReader(handle))
    layer_macs = {row["layer"]: int(row["macs_per_image"]) for row in profiles}
    if tuple(row["layer"] for row in profiles) != LAYER_ORDER:
        raise RuntimeError("unexpected MNIST layer order")
    if sum(layer_macs.values()) != EXPECTED_MACS_PER_IMAGE:
        raise RuntimeError("layer profile MAC sum is not 281,640")

    stage_files = sorted(stage_dir.glob("c-*.json"))
    stage_rows = [stage_row(read_json(path)) for path in stage_files]
    stage_rows.sort(key=lambda row: (LAYER_ORDER.index(row["layer_id"]), MODE_ORDER.index(row["mode"]), row["repeat"]))
    validate_stage_rows(stage_rows, layer_macs)

    functional_rows = []
    for repeat_index in (1, 2):
        repeat = functional[f"test_repeat_{repeat_index}"]
        functional_rows.append(
            {
                "oracle": "PyTorch functional",
                "repeat": repeat_index - 1,
                "samples": int(repeat["samples"]),
                "correct": int(repeat["correct"]),
                "accuracy": float(repeat["accuracy"]),
                "accuracy_pct": 100.0 * float(repeat["accuracy"]),
                "prediction_sha256": repeat["prediction_sha256"],
                "repeat_prediction_hash_identical": functional["repeat_prediction_hash_identical"],
                "model": functional["model"],
                "architecture": functional["architecture"],
                "model_parameters": int(functional["model_parameters"]),
                "model_sha256": functional["model_sha256"],
                "seed": int(functional["seed"]),
                "train_samples": int(functional["train_samples"]),
                "test_samples": int(functional["test_samples"]),
                "macs_per_image": int(functional["macs_per_image"]),
                "macs_full_test_set": int(functional["macs_full_test_set"]),
                "torch_version": functional["torch_version"],
                "git_commit": functional["git_commit"],
                "host": functional["host"],
                "command_json": json.dumps(functional["command"], separators=(",", ":")),
                "claim_boundary": functional["claim_boundary"],
            }
        )

    network_rows = make_network_rows(stage_rows, int(functional["macs_full_test_set"]))
    resolved_config = {
        "schema_version": "aspg-mnist-cnn-resolved-config-1.0",
        "config_id": "SmallMNISTCNN-LeNet-style",
        "purpose": "Complete MNIST functional oracle plus exact-shape STAGE layer feasibility",
        "functional_oracle": {
            "runtime": "PyTorch",
            "architecture": functional["architecture"],
            "seed": int(functional["seed"]),
            "epochs": int(functional["epochs"]),
            "train_samples": int(functional["train_samples"]),
            "test_samples": int(functional["test_samples"]),
            "model_sha256": functional["model_sha256"],
            "prediction_sha256": functional["test_repeat_1"]["prediction_sha256"],
            "dataset_sha256": stage_rows[0]["dataset_hash"],
        },
        "stage_layer_replay": {
            "base_config": "S-Native",
            "base_config_sha256": stage_rows[0]["config_hash"],
            "precision_bits": 32,
            "modes": list(MODE_ORDER),
            "repeats": [0, 1],
            "aggregate_macs_per_cycle": 4096,
            "link_bits_per_cycle": 128,
            "memory_ports": 1,
            "memory_latency_cycles": 5,
            "post_op_elements_per_cycle": 16,
            "lowering_sha256": stage_rows[0]["lowering_hash"],
            "layers": [
                {
                    "layer_id": row["layer"],
                    "kind": row["kind"],
                    "M": int(row["im2col_M"]),
                    "N": int(row["im2col_N"]),
                    "K": int(row["im2col_K"]),
                    "macs_per_image": int(row["macs_per_image"]),
                }
                for row in profiles
            ],
        },
        "claim_boundary": functional["claim_boundary"],
    }
    config_dir = bundle / "config"
    config_dir.mkdir(parents=True, exist_ok=True)
    config_json = config_dir / "mnist_cnn.resolved.json"
    config_yaml = config_dir / "mnist_cnn.resolved.yaml"
    config_json.write_text(json.dumps(resolved_config, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    config_yaml.write_text(yaml.safe_dump(resolved_config, sort_keys=False, allow_unicode=True), encoding="utf-8")
    functional_csv = summary_dir / "mnist_cnn_functional.csv"
    stage_csv = summary_dir / "mnist_cnn_stage_layers.csv"
    network_csv = summary_dir / "mnist_cnn_network_summary.csv"
    write_csv(functional_csv, functional_rows[0].keys(), functional_rows)
    write_csv(stage_csv, stage_rows[0].keys(), stage_rows)
    write_csv(network_csv, network_rows[0].keys(), network_rows)

    confusion = load_confusion_matrix(functional_dir / "confusion_matrix.csv")
    if int(confusion.sum()) != EXPECTED_TEST_SAMPLES or int(np.trace(confusion)) != 9_839:
        raise RuntimeError("confusion matrix does not match 9,839/10,000 functional accuracy")
    pdf_path = figure_dir / "fig_mnist_cnn_feasibility.pdf"
    png_path = figure_dir / "fig_mnist_cnn_feasibility.png"
    create_figure(confusion, stage_rows, 100.0 * EXPECTED_ACCURACY, pdf_path, png_path)

    script_path = Path(__file__).resolve()
    manifest_path = manifest_dir / "mnist_cnn_feasibility_manifest.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    indexed_paths = [
        script_path,
        config_json,
        config_yaml,
        functional_dir / "summary.json",
        functional_dir / "manifest.json",
        functional_dir / "confusion_matrix.csv",
        functional_dir / "layer_profiles.csv",
        *stage_files,
        functional_csv,
        stage_csv,
        network_csv,
        pdf_path,
        png_path,
    ]
    manifest = {
        "schema_version": "aspg-mnist-cnn-analysis-manifest-1.0",
        "status": "completed",
        "claim_boundary": (
            "98.39% is the PyTorch functional-oracle accuracy. STAGE evidence covers "
            "deterministic exact-shape cycle/data movement, not end-to-end numerical CNN accuracy."
        ),
        "validation": {
            "functional_full_test_samples": EXPECTED_TEST_SAMPLES,
            "functional_correct": 9_839,
            "functional_accuracy_pct": 98.39,
            "functional_prediction_hash_repeat_identical": True,
            "stage_completed_cases": len(stage_rows),
            "stage_expected_cases": EXPECTED_STAGE_CASES,
            "stage_repeat_groups": len(LAYER_ORDER) * len(MODE_ORDER),
            "stage_trace_hash_repeat_identical": True,
            "exact_macs_per_image": EXPECTED_MACS_PER_IMAGE,
            "functional_macs_full_test_set": int(functional["macs_full_test_set"]),
            "stage_compute_only_cycles_per_image": network_rows[0]["total_cycles_per_image"],
            "stage_full_system_cycles_per_image": network_rows[1]["total_cycles_per_image"],
            "stage_full_system_dominant_service": network_rows[1]["dominant_service"],
        },
        "files": [
            {
                "path": path.relative_to(bundle.parent.parent.parent.parent).as_posix()
                if bundle.parent.parent.parent.parent in path.parents
                else str(path),
                "bytes": path.stat().st_size,
                "sha256": sha256_file(path),
            }
            for path in indexed_paths
        ],
    }
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(
        json.dumps(
            {
                "status": "completed",
                "functional_accuracy_pct": 98.39,
                "stage_cases": len(stage_rows),
                "exact_macs_per_image": EXPECTED_MACS_PER_IMAGE,
                "compute_only_cycles_per_image": network_rows[0]["total_cycles_per_image"],
                "full_system_cycles_per_image": network_rows[1]["total_cycles_per_image"],
                "manifest": str(manifest_path),
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
