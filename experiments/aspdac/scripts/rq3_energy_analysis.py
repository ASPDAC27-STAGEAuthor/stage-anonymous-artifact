#!/usr/bin/env python3
"""Verify Accelergy evidence and build matched STAGE energy summaries."""

from __future__ import annotations

import csv
import json
from pathlib import Path
from typing import Any

import matplotlib.pyplot as plt
import yaml

import final_analysis as common


BUNDLE = common.BUNDLE
WORKLOADS = ["gemm_256", "mlp_l1", "mlp_l2", "attention_qk", "attention_pv"]
LABELS = ["GEMM", "MLP-L1", "MLP-L2", "Attn-QK", "Attn-PV"]


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def verify_accelergy(root: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for case in WORKLOADS:
        case_root = root / case
        ert = case_root / "timeloop-mapper.ERT.yaml"
        log = case_root / "timeloop-mapper.accelergy.log"
        runner_log = case_root / "timeloop_mapper.stdout.log"
        document = yaml.safe_load(ert.read_text(encoding="utf-8"))
        tables = document.get("ERT", {}).get("tables", [])
        combined = log.read_text(encoding="utf-8", errors="replace") + runner_log.read_text(encoding="utf-8", errors="replace")
        actions = [action.get("name", "") for table in tables for action in table.get("actions", [])]
        row = {
            "case_id": case,
            "status": "completed" if len(tables) == 5 else "failed",
            "ert_table_count": len(tables),
            "ert_non_empty": len(tables) > 0,
            "dummy_action_detected": any("dummy" in action.lower() for action in actions) or "dummy estimated" in combined.lower(),
            "schema_fallback_detected": "key not found: tables" in combined.lower(),
            "ert_sha256": common.sha256(ert),
            "raw_root": common.repo_path(case_root),
        }
        if row["status"] != "completed" or row["dummy_action_detected"] or row["schema_fallback_detected"]:
            raise RuntimeError(f"Accelergy verification failed for {case}: {row}")
        rows.append(row)
    repeat = json.loads((root / "repeat_manifest.json").read_text(encoding="utf-8"))
    if repeat.get("claim_boundary") is None:
        raise RuntimeError("Accelergy repeat manifest is missing claim boundary")
    return rows


def main() -> None:
    results, stage_inputs = common.load_plan_results("rq3_energy")
    indexed = {result["axes"]["case_id"]: result for result in results}
    if len(indexed) != 14:
        raise RuntimeError(f"Expected 14 energy cases, found {len(indexed)}")
    external_root = BUNDLE / "raw/external_accelergy_full"
    verification = verify_accelergy(external_root)
    action_reference = {row["component"] + ":" + row["action"]: row for row in read_csv(external_root / "action_reference.csv")}
    microbench: list[dict[str, Any]] = []
    action_bindings = {
        "mac_read": "system.MAC[0..15]:read",
        "register_read": "system.Registers[0..15]:read",
        "register_write": "system.Registers[0..15]:write",
        "local_sram_read": "system.LocalBuffer[0..15]:read",
        "local_sram_write": "system.LocalBuffer[0..15]:write",
        "global_sram_read": "system.GlobalBuffer:read",
        "global_sram_write": "system.GlobalBuffer:write",
        "dram_read": "system.DRAM:read",
        "dram_write": "system.DRAM:write",
    }
    for case, action_key in action_bindings.items():
        result, reference = indexed[case], action_reference[action_key]
        reference_energy = float(reference["energy_pj"])
        stage_energy = float(result["metrics"]["stage_energy_pj"])
        microbench.append({
            "case_id": case,
            "component": reference["component"],
            "action": reference["action"],
            "estimator": reference["estimator"],
            "action_count": result["metrics"]["action_count"],
            "accelergy_ert_energy_pj": reference_energy,
            "stage_energy_pj": stage_energy,
            "relative_error": (stage_energy - reference_energy) / reference_energy,
            "evidence_level": "Exact shared ERT action",
            "ert_sha256": result["provenance"]["ert_sha256"],
            "candidate_id": result["candidate_id"],
        })

    workload_rows: list[dict[str, Any]] = []
    for case in WORKLOADS:
        result = indexed[case]
        metrics, breakdown = result["metrics"], result["breakdown"]
        native = float(metrics["external_native_energy_uj"])
        matched = float(metrics["stage_matched_ert_energy_uj"])
        workload_rows.append({
            "case_id": case,
            "external_status": "completed",
            "ert_table_count": next(row["ert_table_count"] for row in verification if row["case_id"] == case),
            "dummy_action_detected": False,
            "schema_fallback_detected": False,
            "external_native_energy_uj": native,
            "stage_shared_ert_energy_uj": matched,
            "native_binding_gap_uj": matched - native,
            "native_binding_gap_fraction": (matched - native) / native,
            "compute_uj": breakdown["compute_uj"],
            "register_uj": breakdown["register_uj"],
            "local_sram_uj": breakdown["local_sram_uj"],
            "global_sram_uj": breakdown["global_sram_uj"],
            "dram_uj": breakdown["dram_uj"],
            "noc_energy_uj": "unknown",
            "conversion_energy_uj": "unknown",
            "optical_energy_uj": "unknown",
            "native_cross_tool_evidence": "Trend",
            "shared_ert_rebound_evidence": "Exact action-accounting replay",
            "claim_boundary": "45-nm CACTI/Aladdin shared reference model; not silicon calibrated",
            "binding_note": "Timeloop native arithmetic summary uses 1.0 pJ/MAC; generated MAC ERT is 3.275 pJ/action",
            "candidate_id": result["candidate_id"],
        })

    micro_path = BUNDLE / "summary/rq3_energy_microbench.csv"
    workload_path = BUNDLE / "summary/rq3_accelergy_stage_energy.csv"
    verify_path = BUNDLE / "summary/rq3_accelergy_verification.csv"
    common.write_csv(micro_path, microbench)
    common.write_csv(workload_path, workload_rows)
    common.write_csv(verify_path, verification)

    x = list(range(len(WORKLOADS)))
    fig, axes = plt.subplots(1, 2, figsize=(10.8, 4.2))
    bottom = [0.0] * len(x)
    categories = [
        ("compute_uj", "Compute", "#2563eb"),
        ("register_uj", "Register", "#60a5fa"),
        ("local_sram_uj", "Local SRAM", "#16a34a"),
        ("global_sram_uj", "Global SRAM", "#d97706"),
        ("dram_uj", "DRAM", "#7c3aed"),
    ]
    for key, label, color in categories:
        values = [float(row[key]) for row in workload_rows]
        axes[0].bar(x, values, bottom=bottom, color=color, label=label)
        bottom = [left + value for left, value in zip(bottom, values)]
    axes[0].scatter(x, [float(row["external_native_energy_uj"]) for row in workload_rows], color="black", marker="_", s=190, label="Native tool total")
    axes[0].set_xticks(x, LABELS, rotation=20)
    axes[0].set_ylabel("Energy (uJ)")
    axes[0].set_title("(a) Shared-ERT rebound breakdown")
    axes[0].grid(axis="y", alpha=0.22)
    axes[0].legend(frameon=False, fontsize=7, ncol=2)
    gaps = [100 * float(row["native_binding_gap_fraction"]) for row in workload_rows]
    axes[1].bar(x, gaps, color="#dc2626")
    axes[1].set_xticks(x, LABELS, rotation=20)
    axes[1].set_ylabel("ERT-bound vs native total gap (%)")
    axes[1].set_title("(b) Arithmetic binding mismatch")
    axes[1].grid(axis="y", alpha=0.22)
    fig.tight_layout()
    figure = BUNDLE / "figures/fig_rq3_energy_breakdown.pdf"
    fig.savefig(figure, bbox_inches="tight")
    fig.savefig(figure.with_suffix(".png"), dpi=220, bbox_inches="tight")
    plt.close(fig)

    external_files = sorted(path for path in external_root.rglob("*") if path.is_file())
    raw_manifest = BUNDLE / "manifests/rq3_energy_external_raw_manifest.json"
    raw_manifest.write_text(json.dumps({
        "schema_version": "aspg-external-raw-manifest-1.0",
        "source_policy": "immutable copy of completed five-workload Accelergy bundle",
        "files": [{"path": common.repo_path(path), "sha256": common.sha256(path), "bytes": path.stat().st_size} for path in external_files],
        "file_count": len(external_files),
        "verified_cases": 5,
        "nonempty_ert_cases": 5,
        "dummy_action_cases": 0,
        "schema_fallback_cases": 0,
    }, indent=2) + "\n", encoding="utf-8")
    outputs = [micro_path, workload_path, verify_path, figure, figure.with_suffix(".png")]
    manifest = BUNDLE / "manifests/rq3_energy_output_manifest.json"
    manifest.write_text(json.dumps({
        "schema_version": "aspg-derived-output-manifest-1.0",
        "rq": "RQ3-Energy",
        "inputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in [*stage_inputs, raw_manifest]],
        "outputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in outputs],
        "microbench_exact_cases": 9,
        "workload_cases": 5,
        "native_workload_evidence": "Trend",
        "matched_rebound_evidence": "Exact action-accounting replay",
        "binding_mismatch_retained": True,
    }, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"verified_accelergy_cases": 5, "microbench_exact": 9, "workload_cases": 5, "native_binding_gap_pct": [100 * row["native_binding_gap_fraction"] for row in workload_rows]}, indent=2))


if __name__ == "__main__":
    main()
