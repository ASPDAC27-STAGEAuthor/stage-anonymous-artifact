#!/usr/bin/env python3
"""Create the final claim audit and content-addressed bundle index."""

from __future__ import annotations

import csv
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import final_analysis as common


BUNDLE = common.BUNDLE


CLAIMS: list[dict[str, str]] = [
    {"claim_id": "C-RQ1-EXACT", "scope": "RQ1", "claim": "Nine exact/oracle cases pass and each repeated canonical trace is byte-identical.", "status": "measured", "evidence_level": "Exact", "evidence": "summary/rq1_exact_cases.csv; summary/rq1_repeat_hashes.csv", "paper_action": "state as measured"},
    {"claim_id": "C-RQ2-HOTSPOT", "scope": "RQ2", "claim": "BookSim2 and STAGE both identify hotspot node 5 as the earliest severe congestion case.", "status": "measured", "evidence_level": "Trend", "evidence": "summary/rq2_saturation_summary.csv", "paper_action": "state with trend boundary"},
    {"claim_id": "C-RQ2-SATURATION-EQUIVALENCE", "scope": "RQ2", "claim": "STAGE numerically matches BookSim2 saturation for every traffic pattern.", "status": "not_supported", "evidence_level": "Numerical", "evidence": "summary/rq2_saturation_summary.csv", "paper_action": "explicitly reject; only hotspot ordering is eligible"},
    {"claim_id": "C-RQ2-UNIFORM-SATURATION", "scope": "RQ2", "claim": "The exact STAGE uniform-traffic saturation point is identified within the tested range.", "status": "pending", "evidence_level": "Numerical", "evidence": "summary/rq2_saturation_summary.csv", "paper_action": "report saturation > tested bound 0.60"},
    {"claim_id": "C-RQ3-TL-CYCLES", "scope": "RQ3-A", "claim": "V-TL compute-only exactly reproduces all five Timeloop 16-MAC cycle floors and access counts.", "status": "measured", "evidence_level": "Exact", "evidence": "summary/rq3_timeloop_stage_cycles.csv; summary/rq3_timeloop_stage_accesses.csv", "paper_action": "state as exact schedule replay"},
    {"claim_id": "C-RQ3-SIZE-TL", "scope": "RQ3-A size extension", "claim": "V-TL exactly reproduces Timeloop cycles, MAC counts, and four-level access counts at all nine registered GEMM, MLP-L1, and attention-QK sizes.", "status": "measured", "evidence_level": "Exact", "evidence": "summary/rq3_size_timeloop_stage_scaling.csv; summary/rq3_size_access_scaling.csv", "paper_action": "state as a nine-point exact size extension"},
    {"claim_id": "C-RQ3-TL-FULLSYSTEM-EQUIVALENCE", "scope": "RQ3-A", "claim": "STAGE full-system total cycles are directly equivalent to Timeloop analytical cycles.", "status": "not_supported", "evidence_level": "Trend", "evidence": "summary/rq3_timeloop_stage_cycles.csv", "paper_action": "compare compute floor only; show STAGE-only excess decomposition"},
    {"claim_id": "C-RQ3-SS-TREND", "scope": "RQ3-B", "claim": "Matched V-SS preserves SCALE-Sim timing trends and the MLP-L1 bandwidth outlier.", "status": "measured", "evidence_level": "Trend", "evidence": "summary/rq3_scalesim_stage_timing.csv", "paper_action": "state as trend; max absolute cycle difference 6.41 percent"},
    {"claim_id": "C-RQ3-SIZE-SS", "scope": "RQ3-B size extension", "claim": "All nine registered V-SS size points stay within the pre-registered 10-percent Trend envelope of SCALE-Sim warm cycles.", "status": "measured", "evidence_level": "Trend", "evidence": "summary/rq3_size_scalesim_stage_scaling.csv", "paper_action": "state as Trend only; maximum absolute cycle difference 5.4004 percent"},
    {"claim_id": "C-ENERGY-ERT", "scope": "Energy", "claim": "All five workload and nine size-extension Accelergy bundles have non-empty five-table ERTs with no dummy action or schema fallback; nine one-action checks are exact.", "status": "measured", "evidence_level": "Exact shared reference", "evidence": "summary/rq3_accelergy_verification.csv; summary/rq3_energy_microbench.csv; summary/rq3_size_accelergy_scaling.csv", "paper_action": "replace obsolete Accelergy-failed text"},
    {"claim_id": "C-ENERGY-NATIVE-EQUALITY", "scope": "Energy", "claim": "Native Timeloop energy totals numerically equal the shared-ERT rebound totals.", "status": "not_supported", "evidence_level": "Trend", "evidence": "summary/rq3_accelergy_stage_energy.csv", "paper_action": "retain 3.81--3.92 percent arithmetic-binding gap"},
    {"claim_id": "C-MNIST-FUNCTIONAL", "scope": "CNN feasibility", "claim": "The deterministic LeNet-style oracle reaches 98.39 percent on all 10,000 MNIST test images, with identical repeated prediction hashes.", "status": "measured", "evidence_level": "Exact functional oracle", "evidence": "summary/mnist_cnn_functional.csv; summary/mnist_cnn_network_summary.csv", "paper_action": "state as PyTorch functional evidence, not STAGE accuracy"},
    {"claim_id": "C-MNIST-NOC-TRACE", "scope": "CNN NoC trace", "claim": "BookSim2 and STAGE consume byte-identical registered materialized-im2col cold-start traces, including the 18,337-packet sequential-network trace, and all 12 same-seed repeat groups reproduce exact delivery and metrics hashes.", "status": "measured", "evidence_level": "Exact", "evidence": "summary/mnist_cnn_noc_trace_summary.csv; summary/mnist_cnn_noc_repeat_hashes.csv", "paper_action": "state as an exact finite transport-trace contract; do not call it native CNN execution"},
    {"claim_id": "C-MNIST-NOC-TREND", "scope": "CNN NoC trace", "claim": "BookSim2 and STAGE preserve the five isolated-layer network-makespan ranking (Spearman rho approximately 1.0) and both identify FC1 as dominant.", "status": "measured", "evidence_level": "Trend", "evidence": "summary/mnist_cnn_noc_cross_tool.csv; manifests/mnist_cnn_noc_output_manifest.json", "paper_action": "state as Trend only; report 51,524 versus 18,537 sequential-network cycles descriptively, not as cycle equivalence"},
    {"claim_id": "C-MNIST-STAGE-ENDTOEND", "scope": "CNN feasibility", "claim": "STAGE natively executes the complete CNN numerical graph, including ReLU and average pooling, and reproduces MNIST accuracy.", "status": "not_supported", "evidence_level": "Out of scope", "evidence": "summary/mnist_cnn_stage_layers.csv", "paper_action": "claim only exact Conv/im2col and FC cycle/data-movement feasibility; post-ops are synthetic services"},
    {"claim_id": "C-RQ4-ORACLE", "scope": "RQ4", "claim": "The 4.11/-4.11/-1.11 optical oracle and 132-bit/two-cycle serialization are exact.", "status": "measured", "evidence_level": "Exact", "evidence": "summary/rq4_optical_oracle.csv", "paper_action": "state as exact"},
    {"claim_id": "C-RQ4-CAPACITY", "scope": "RQ4", "claim": "Increasing wavelength capacity from 1 to 8 monotonically reduces cycles, conflicts, and backpressure.", "status": "measured", "evidence_level": "Measured paired runtime", "evidence": "summary/rq4_matched_transport.csv", "paper_action": "state as measured"},
    {"claim_id": "C-RQ4-OPTICAL-WIN", "scope": "RQ4", "claim": "The tested optical transport is faster or lower-energy than the electrical reference.", "status": "not_supported", "evidence_level": "Measured paired runtime", "evidence": "summary/rq4_matched_transport.csv", "paper_action": "state the measured loss: 2049 vs 1025 cycles and 140.52 vs 10.49 nJ for Attention"},
    {"claim_id": "C-RQ4-BER", "scope": "RQ4", "claim": "The optical model predicts BER accuracy.", "status": "pending", "evidence_level": "Out of scope", "evidence": "summary/rq4_optical_oracle.csv", "paper_action": "state BER not modeled"},
    {"claim_id": "C-CODESIGN-MIGRATION", "scope": "STAGE-only", "claim": "One-axis sweeps expose compute-to-memory/NoC bottleneck migration and plateaus.", "status": "measured", "evidence_level": "Measured STAGE-only", "evidence": "summary/stage_codesign_bottleneck.csv", "paper_action": "state as STAGE-only diagnostic evidence"},
    {"claim_id": "C-CODESIGN-CAUSAL", "scope": "STAGE-only", "claim": "Doubling memory ports reduces the diagnosed memory critical stall and exposes NoC as predicted.", "status": "measured", "evidence_level": "Measured causal intervention", "evidence": "summary/stage_causal_intervention.csv", "paper_action": "state as measured"},
    {"claim_id": "C-MOT-MAPPING", "scope": "STAGE-only topology/mapping", "claim": "The same M=1, K=N=256 workload produces distinct cycle and physical-communication costs across 24 resolved hierarchical mappings and mesh geometries, with exact outputs and deterministic repeated traces.", "status": "measured", "evidence_level": "Measured deterministic full-cycle runtime", "evidence": "summary/stage_mot_mapping.csv; manifests/stage_mot_mapping_output_manifest.json", "paper_action": "state as a joint mapping/topology-geometry sensitivity result, not an isolated topology-only comparison"},
    {"claim_id": "C-PRECISION", "scope": "STAGE-only", "claim": "INT8 and INT4 reduce packetized bits while conversion cost remains explicit.", "status": "measured", "evidence_level": "Measured STAGE-only", "evidence": "summary/stage_precision.csv", "paper_action": "state as measured"},
    {"claim_id": "C-CIM-REPRO", "scope": "STAGE-only CIM", "claim": "Functional and fixed-seed CIM runs reproduce exact hashes; ten-seed error statistics retain evidence and uncertainty.", "status": "measured", "evidence_level": "Exact plus Trend statistics", "evidence": "summary/stage_cim_comparison.csv; summary/stage_cim_statistics.csv", "paper_action": "state with characterization boundary"},
    {"claim_id": "C-CIM-ENERGY-WIN", "scope": "STAGE-only CIM", "claim": "CIM has lower complete energy/op than the digital PE.", "status": "pending", "evidence_level": "Trend only", "evidence": "summary/stage_cim_comparison.csv", "paper_action": "do not claim; egress energy is unknown and digital template is synthetic"},
    {"claim_id": "C-SILICON-CALIBRATION", "scope": "All energy", "claim": "Reported energies are silicon-calibrated power measurements.", "status": "pending", "evidence_level": "Out of scope", "evidence": "summary/rq3_accelergy_stage_energy.csv; summary/rq4_matched_transport.csv", "paper_action": "state shared reference/synthetic functional boundaries"},
]


def write_claims() -> tuple[Path, Path]:
    csv_path = BUNDLE / "summary/claim_status.csv"
    common.write_csv(csv_path, CLAIMS)
    md_path = BUNDLE / "summary/CLAIM_STATUS.md"
    lines = [
        "# Final paper claim status",
        "",
        "Generated from the manifest-indexed final bundle. `pending` and `not_supported` claims must not be phrased as measured successes.",
        "",
        "| Claim | Scope | Status | Evidence | Paper action |",
        "|---|---|---|---|---|",
    ]
    for row in CLAIMS:
        lines.append(f"| `{row['claim_id']}` {row['claim']} | {row['scope']} | **{row['status']}** ({row['evidence_level']}) | {row['evidence']} | {row['paper_action']} |")
    md_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return csv_path, md_path


def write_rq2_correction() -> Path:
    raw = sorted((BUNDLE / "raw/rq2_stage_vbs").glob("*.json"))
    affected = []
    for path in raw:
        document = json.loads(path.read_text(encoding="utf-8"))
        if document.get("provenance", {}).get("runtime") == "HardwareSim.Core.RouterRuntime":
            affected.append(path)
    correction = BUNDLE / "manifests/rq2_runtime_label_correction.json"
    correction.write_text(json.dumps({
        "schema_version": "aspg-metadata-correction-1.0",
        "scope": "metadata label only; raw measurements are immutable and unchanged",
        "affected_result_count": len(affected),
        "recorded_label": "HardwareSim.Core.RouterRuntime",
        "correct_label": "HardwareSim.Core.AspdacVbsRuntime.FastRouter",
        "reason": "The final V-BS provider used the purpose-built nested FastRouter, but the first runner build emitted the generic design label.",
        "measurement_impact": "none",
        "source_evidence": [
            {"path": "src/HardwareSim.Core/AspdacVbsRuntime.cs", "sha256": common.sha256(common.REPO / "src/HardwareSim.Core/AspdacVbsRuntime.cs")},
            {"path": "tools/HardwareSim.AspdacRunner/Program.cs", "sha256": common.sha256(common.REPO / "tools/HardwareSim.AspdacRunner/Program.cs")},
        ],
        "affected_paths_sha256": hashlib.sha256("\n".join(common.repo_path(path) + ":" + common.sha256(path) for path in affected).encode("utf-8")).hexdigest(),
    }, indent=2) + "\n", encoding="utf-8")
    return correction


def main() -> None:
    claim_csv, claim_md = write_claims()
    correction = write_rq2_correction()
    failure_disposition = list(csv.DictReader((BUNDLE / "summary/rq2_failure_disposition.csv").open(newline="", encoding="utf-8")))
    unresolved_failures = [row for row in failure_disposition if row["failure_disposition"] != "superseded_by_completed_result"]
    if unresolved_failures:
        raise RuntimeError(f"Unresolved failures: {unresolved_failures}")
    cnn_failure_disposition = json.loads(
        (BUNDLE / "manifests/mnist_cnn_noc_failure_disposition.json").read_text(encoding="utf-8")
    )
    if (
        cnn_failure_disposition.get("status") != "completed_with_preserved_smoke_failures"
        or cnn_failure_disposition.get("final_validation", {}).get("failed_final_candidates") != 0
        or not cnn_failure_disposition.get("final_validation", {}).get("all_packets_drained")
    ):
        raise RuntimeError("MNIST CNN NoC smoke failures do not have a completed final disposition")
    cnn_failure_records = cnn_failure_disposition.get("records", [])

    index_path = BUNDLE / "manifests/final_manifest_index.json"
    files = sorted(path for path in BUNDLE.rglob("*") if path.is_file() and path != index_path)
    entries: list[dict[str, Any]] = []
    for path in files:
        entries.append({
            "path": common.repo_path(path),
            "bundle_section": path.relative_to(BUNDLE).parts[0],
            "sha256": common.sha256(path),
            "bytes": path.stat().st_size,
        })
    counts: dict[str, int] = {}
    for entry in entries:
        counts[entry["bundle_section"]] = counts.get(entry["bundle_section"], 0) + 1
    index_path.write_text(json.dumps({
        "schema_version": "aspg-final-manifest-index-1.0",
        "generated_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "bundle_root": common.repo_path(BUNDLE),
        "file_count": len(entries),
        "section_counts": counts,
        "planned_stage_cases": {"p0": 5, "rq1": 18, "rq2": 590, "rq3_compute": 20, "rq3_energy": 14, "rq3_size": 36, "mnist_cnn_layers": 20, "mnist_cnn_noc_trace": 12, "rq4": 14, "stage_codesign": 42},
        "external_reference_cases": {"booksim_registered": 520, "booksim_uniform_extension": 10, "booksim_cnn_noc_trace": 12, "timeloop_model": 5, "scalesim": 5, "accelergy": 5, "timeloop_size": 9, "scalesim_size": 9, "accelergy_size": 9, "mnist_functional": 1},
        "unresolved_failures": 0,
        "retained_superseded_failure_records": len(failure_disposition) + len(cnn_failure_records),
        "retained_cnn_noc_smoke_failure_records": len(cnn_failure_records),
        "retained_failure_files": len(list((BUNDLE / "failures").glob("*"))),
        "claim_status_counts": {status: sum(row["status"] == status for row in CLAIMS) for status in ("measured", "not_supported", "pending")},
        "files": entries,
    }, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "indexed_files": len(entries),
        "section_counts": counts,
        "claims": {status: sum(row["status"] == status for row in CLAIMS) for status in ("measured", "not_supported", "pending")},
        "unresolved_failures": 0,
        "rq2_metadata_correction_count": json.loads(correction.read_text(encoding="utf-8"))["affected_result_count"],
        "cnn_noc_preserved_smoke_failures": len(cnn_failure_records),
        "claim_outputs": [common.repo_path(claim_csv), common.repo_path(claim_md)],
    }, indent=2))


if __name__ == "__main__":
    main()
