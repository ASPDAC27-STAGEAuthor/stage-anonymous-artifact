#!/usr/bin/env python3
"""Validate frozen terminal evidence claim by claim, or as one complete replay."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import statistics
from pathlib import Path
from typing import Callable

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "experiments" / "aspdac" / "results"
OUT = ROOT / "output"
FINAL = RESULTS / "final_20260716" / "summary"
REVIEWER = RESULTS / "reviewer_extension_20260717" / "summary"
OPTICAL = RESULTS / "optical_intervention_20260717" / "summary"
PRECISION = RESULTS / "mnist_pe_precision_20260717" / "summary"
TOOL_IDENTITIES = json.loads((ROOT / "experiments" / "aspdac" / "external_inputs" / "tool_versions.json").read_text(encoding="utf-8"))

EXPECTED_RQ1_HASHES = {
    "backpressure": "b721530d43bf2ee66d3560b26e7092ba655a1e8b3683c2dc69941645d328c079",
    "compile_non_mutation": "2d77093f637dc2e14e76f5aaa548ff004a16a94d873c1651d7a38808c553ff8e",
    "current_next_visibility": "0e4db9fd07357145c9a7a589c005b79d612b4bf54a7be97e0270063d461dca36",
    "graph_round_trip": "e6d253c4f48a312e81c06c1fe001cd6a2df01b1e20393f3b23e55d4281065cef",
    "optical_loss_power_margin": "3549e5320b9e87ff088e36dadaead0a7ee530a1fa5840442d255120730d9d77f",
    "packet_serialization_132b": "b67b89ae6faade90c9bf972b22361e883cf94ba02bfcb4d7632ce183e0ce9245",
    "reduction": "77a7e00b1f934108990e8b32dd78275f63dda18719b5ed8cf7f668741858130c",
    "softmax": "d6b3637ede22b6f598dd5507f965b23b6f837781f67112ea4d1c3e44f8b5ea81",
    "wavelength_arbitration": "50620c37578e981bd53c80209baf26f380f2b345458c1f512b762f6324071321",
}

EXPECTED_NOC_HASHES = {
    "noc_n01_single_128": ("39b5935d8ef3f5b80bc28ba38fcca659ea4686ea990fca041e720b311214b625", "eb27dd9911b609414d5d4be795575d742237ca9dbd623481e29ef338b30a3cf0"),
    "noc_n02_single_256_vc1": ("775c258695f69a6bf801e0d5365ee21f77838a8c1e3a786127ae785e2505aeaf", "aa559a872cae7d6f7a513b86c42a3eb04e4b1c9d344f92cbc648f4e2aaa0142b"),
    "noc_n03_single_512_vc3": ("b444ceb9860f3675c23347ba7c30c7542c6e9bbe5f367f320e1aac0cf0f3d6aa", "80667b8b8db1f0d84a2a784d9615d9841e18310e2a51d1f4a80eb3fd49065041"),
    "noc_n04_single_1024": ("76745208393878939427057e208d749a3b04802a75e2ec44007b580d19a64b88", "cf97ce053ddc0539965cb41f764d848e44bf957773f033fc6566ff7ff277c1d8"),
    "noc_n05_contend_128": ("df0418aaf50ba81b05d8343818400f1ba33159a63016566aa84148eb8f6067ff", "fbbeb638ee913896a62eb40d75d09881ddb260c3f7c9dfffc231b72560860b21"),
    "noc_n06_contend_512": ("d031287b738b199add6a75685993b871ad028c8065c7d38df842a5862d66b2dc", "823dd9817b95c9eda5c28c5ccb2a2b175db545ab12896e3ec4f313e8d38240a1"),
    "noc_n09_atomic_depth_boundary": ("ef32c764fe3dc95b2332b9cbd16b3a890ec6df02f1132c333d0a8144d652b8e3", "19151ad92ba991bc2ac4721f26211f93e6ecc637e0503d29599c20107d0dcb74"),
}

EXPECTED_ERT_ENERGY_PJ = {
    "mac_read": 3.275,
    "register_read": 0.009,
    "register_write": 0.009,
    "local_sram_read": 1.45068,
    "local_sram_write": 1.71282,
    "global_sram_read": 124.61,
    "global_sram_write": 118.033,
    "dram_read": 2048.0,
    "dram_write": 2048.0,
}


def rows(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def validate_analytical() -> dict[str, object]:
    evidence = rows(FINAL / "rq1_exact_cases.csv")
    by_case = {row["case"]: row for row in evidence}
    require(set(by_case) == set(EXPECTED_RQ1_HASHES), "expected nine analytical contract cases")
    for case, expected_hash in EXPECTED_RQ1_HASHES.items():
        row = by_case[case]
        require(row["all_passed"].lower() == "true", f"analytical contract failed: {case}")
        require(row["byte_identical"].lower() == "true", f"repeat trace mismatch: {case}")
        require(int(row["repeats"]) == 2, f"repeat count changed: {case}")
        require(row["canonical_trace_hash"] == expected_hash, f"canonical hash changed: {case}")
    return {
        "status": "pass",
        "cases_passed": len(evidence),
        "expected_cases": 9,
        "repeat_runs": sum(int(row["repeats"]) for row in evidence),
        "all_repeats_byte_identical": True,
        "canonical_trace_hashes": {case: by_case[case]["canonical_trace_hash"] for case in sorted(by_case)},
    }


def validate_determinism() -> dict[str, object]:
    analytical = validate_analytical()
    return {
        "status": "pass",
        "cases": analytical["cases_passed"],
        "repeat_runs": analytical["repeat_runs"],
        "all_repeats_byte_identical": analytical["all_repeats_byte_identical"],
        "algorithm": "SHA-256",
        "canonical_trace_schema": "1.0",
    }


def validate_noc() -> dict[str, object]:
    evidence = rows(REVIEWER / "noc_contract_microbench.csv")
    supported = [
        row for row in evidence
        if row["checkpoint_status"] == "completed"
        and row["runtime_status"] in {"completed", "expected_boundary"}
        and row["oracle_matched"].lower() == "true"
    ]
    require(len(supported) == 14, "expected 14 supported NoC contract runs")
    case_ids = sorted({row["case_id"] for row in supported})
    require(len(case_ids) == 7, "expected seven supported NoC cases")
    require(all(row["stage_trace_sha256"] and row["oracle_timeline_sha256"] for row in supported), "NoC trace hash missing")
    for row in supported:
        expected_stage, expected_oracle = EXPECTED_NOC_HASHES[row["case_id"]]
        require(row["stage_trace_sha256"] == expected_stage, f"NoC STAGE trace hash changed: {row['case_id']}")
        require(row["oracle_timeline_sha256"] == expected_oracle, f"NoC oracle timeline hash changed: {row['case_id']}")
    return {
        "status": "pass",
        "supported_runs_passed": len(supported),
        "supported_cases_passed": len(case_ids),
        "case_ids": case_ids,
        "unsupported_cases": ["N07", "N08"],
    }


def validate_timeloop() -> dict[str, object]:
    cycle_rows = rows(FINAL / "rq3_timeloop_stage_cycles.csv")
    access_rows = rows(FINAL / "rq3_timeloop_stage_accesses.csv")
    require(len(cycle_rows) == 5, "expected five Timeloop cycle cases")
    require(len(access_rows) == 20, "expected twenty Timeloop access rows")
    require(all(int(row["stage_compute_only_cycles"]) == int(row["timeloop_model_cycles"]) for row in cycle_rows), "Timeloop cycle mismatch")
    require(all(float(row["compute_cycle_relative_error"]) == 0.0 for row in cycle_rows), "Timeloop cycle error changed")
    require(all(int(row["stage_replayed_accesses"]) == int(row["timeloop_model_accesses"]) for row in access_rows), "Timeloop access mismatch")
    require(all(float(row["relative_error"]) == 0.0 for row in access_rows), "Timeloop access error changed")
    identity = TOOL_IDENTITIES["timeloop"]
    require(identity["infrastructure_git_commit"] == "6e6186f9fe8f9a1f3990f78f57c7224d22cb8cfa", "Timeloop Git identity changed")
    require(identity["timeloop_model_sha256"] == "c3eb5d5f5717c701a46e57894a6ac5e24b581c27953cce483c2561913eb5194e", "Timeloop binary identity changed")
    return {
        "status": "pass",
        "cycle_cases_exact": len(cycle_rows),
        "access_rows_exact": len(access_rows),
        "maximum_relative_error": 0.0,
        "tool_identity": identity,
    }


def validate_scalesim() -> dict[str, object]:
    evidence = [row for row in rows(REVIEWER / "holdout_scalesim_stage_timing.csv") if row["status"] == "completed"]
    pairs: list[tuple[str, str, int, int]] = []
    for case_id, repeat in sorted({(row["case_id"], row["repeat"]) for row in evidence}):
        by_tool = {row["tool"]: row for row in evidence if row["case_id"] == case_id and row["repeat"] == repeat}
        if {"stage", "scalesim"}.issubset(by_tool):
            pairs.append((case_id, repeat, int(by_tool["scalesim"]["total_cycles"]), int(by_tool["stage"]["total_cycles"])))
    maximum = max(abs(stage - scale) / max(stage, scale) * 100 for _, _, scale, stage in pairs)
    require(len(pairs) == 16, "expected 16 SCALE-Sim hold-out pairs")
    require(maximum <= 10.0, "hold-out pair exceeded the predefined 10% engineering tolerance")
    require(round(maximum, 2) == 9.69, "unexpected hold-out maximum difference")
    identity = TOOL_IDENTITIES["scalesim"]
    require(identity["git_commit"] == "9f98c4371055a54c75209c2e02b640b897550532", "SCALE-Sim Git identity changed")
    require(identity["compatibility_diff_sha256"] == "ee0f1075ace7dc2b299e9cfaf4bc9f423b7fdfb9747ecff20f542dfa5f3dad1b", "SCALE-Sim compatibility diff identity changed")
    return {
        "status": "pass",
        "paired_runs": len(pairs),
        "maximum_relative_difference_percent": round(maximum, 6),
        "acceptance_envelope_percent": 10.0,
        "tool_identity": identity,
    }


def validate_accelergy() -> dict[str, object]:
    evidence = rows(FINAL / "rq3_energy_microbench.csv")
    by_case = {row["case_id"]: row for row in evidence}
    require(set(by_case) == set(EXPECTED_ERT_ENERGY_PJ), "expected nine Accelergy ERT actions")
    for case, expected in EXPECTED_ERT_ENERGY_PJ.items():
        row = by_case[case]
        require(float(row["relative_error"]) == 0.0, f"Accelergy relative error changed: {case}")
        require(float(row["stage_energy_pj"]) == expected, f"STAGE action energy changed: {case}")
        require(float(row["accelergy_ert_energy_pj"]) == expected, f"ERT action energy changed: {case}")
    ert_hashes = sorted({row["ert_sha256"] for row in evidence})
    require(ert_hashes == ["5f86d648b6e655d254a31dd3a619bd2476adf3dfae81ae7278a34feb2307d9da"], "ERT hash changed")
    identity = TOOL_IDENTITIES["accelergy"]
    require(identity["version"] == "0.4", "Accelergy version changed")
    require(identity["ert_sha256"] == ert_hashes[0], "Accelergy ERT identity changed")
    return {
        "status": "pass",
        "actions_exact": len(evidence),
        "maximum_relative_error": 0.0,
        "ert_sha256": ert_hashes[0],
        "energy_pj": EXPECTED_ERT_ENERGY_PJ,
        "tool_identity": identity,
    }


def saturation_value(value: str) -> float:
    return float(value.removeprefix(">"))


def validate_booksim() -> dict[str, object]:
    evidence = rows(FINAL / "rq2_saturation_summary.csv")
    require({row["traffic"] for row in evidence} == {"uniform", "transpose", "bit_complement", "hotspot_node5"}, "BookSim traffic set changed")
    booksim_order = [row["traffic"] for row in sorted(evidence, key=lambda row: float(row["booksim_saturation"]))]
    stage_order = [row["traffic"] for row in sorted(evidence, key=lambda row: saturation_value(row["stage_saturation"]))]
    hotspot = next(row for row in evidence if row["traffic"] == "hotspot_node5")
    require(booksim_order[0] == "hotspot_node5", "BookSim congestion ordering changed")
    require(stage_order[0] == "hotspot_node5", "STAGE congestion ordering changed")
    require(float(hotspot["booksim_saturation"]) == 0.04 and float(hotspot["stage_saturation"]) == 0.08, "hotspot saturation points changed")
    require(hotspot["ordering_match_eligible"].lower() == "true", "BookSim ordering claim is not eligible")
    identity = TOOL_IDENTITIES["booksim2"]
    require(identity["git_commit"] == "28f43299f1706a3160ffac721ca461d74eb6e618", "BookSim Git identity changed")
    require(identity["binary_sha256"] == "44b617ec81bcdb7496ee86acab011f5d7d00c0716ae65150a11777a2b84c4cbd", "BookSim binary identity changed")
    return {
        "status": "pass",
        "shared_first_congested_traffic": "hotspot_node5",
        "booksim_hotspot_saturation": 0.04,
        "stage_hotspot_saturation": 0.08,
        "claim_boundary": "congestion ordering only; no cycle or absolute-saturation equivalence",
        "tool_identity": identity,
    }


def validate_attribution() -> dict[str, object]:
    evidence = rows(REVIEWER / "trace_guided_interventions.csv")
    by_axis = {row["axis_name"]: row for row in evidence}
    require(set(by_axis) == {"link_width", "memory_ports"}, "expected two controlled interventions")
    link = by_axis["link_width"]
    memory = by_axis["memory_ports"]
    require(link["measured_baseline_dominant_set"] == "noc" and link["measured_selected_intervention"] == "memory", "link-width bottleneck migration changed")
    require(memory["measured_baseline_dominant_set"] == "memory" and memory["measured_selected_intervention"] == "noc", "memory-port bottleneck migration changed")
    require(link["intervention_trace_sha256"] == memory["baseline_trace_sha256"], "intervention chain is not trace-connected")
    for row in evidence:
        require(all(row[field].lower() == "true" for field in ["exact_repeat_pass", "all_delta_intervals_pass", "dominant_set_pass", "pair_acceptance_pass"]), f"attribution acceptance failed: {row['pair_id']}")
    return {
        "status": "pass",
        "interventions_passed": len(evidence),
        "diagnosed_migration": ["noc", "memory", "noc"],
        "claim_boundary": "deterministic within-model controlled attribution",
    }


def validate_mapping() -> dict[str, object]:
    evidence = rows(FINAL / "stage_mot_mapping.csv")
    cycles = [int(row["cycles"]) for row in evidence]
    packets = [int(row["physical_packet_moves_1024b"]) for row in evidence]
    distance = [int(row["physical_bit_distance_bit_um"]) for row in evidence]
    require(len(evidence) == 24, "expected 24 mapping candidates")
    require((min(cycles), max(cycles)) == (174, 1741), "mapping cycle range changed")
    require((min(packets), max(packets)) == (316, 600), "mapping packet-move range changed")
    require(all(row["exact"].lower() == "true" and row["trace_deterministic"].lower() == "true" for row in evidence), "mapping exactness/determinism failed")
    return {
        "status": "pass",
        "candidates": len(evidence),
        "cycle_range": [min(cycles), max(cycles)],
        "packet_move_range": [min(packets), max(packets)],
        "physical_bit_distance_range_bit_um": [min(distance), max(distance)],
    }


def validate_precision() -> dict[str, object]:
    evidence = rows(PRECISION / "mnist_pe_precision_paired.csv")
    by_profile = {row["profile_id"]: row for row in evidence}
    expected_accuracy = {"fp32_a32": 98.39, "fp16_a16": 98.40, "fp8_a16": 98.28, "fp8_a8": 97.35}
    require(set(by_profile) == set(expected_accuracy), "MNIST precision profile set changed")
    for profile, expected in expected_accuracy.items():
        require(abs(float(by_profile[profile]["accuracy_percent"]) - expected) < 1e-9, f"accuracy changed for {profile}")
    baseline_bits = int(by_profile["fp32_a32"]["packetized_bits"])
    traffic = {profile: 100 * int(row["packetized_bits"]) / baseline_bits for profile, row in by_profile.items()}
    require([round(traffic[p], 5) for p in ["fp32_a32", "fp16_a16", "fp8_a16", "fp8_a8"]] == [100.0, 50.0, 25.0, 25.0], "precision traffic ratios changed")
    checkpoint = ROOT / "data" / "mnist" / "checkpoint" / "small_mnist_cnn_state.pt"
    predictions = ROOT / "data" / "mnist" / "checkpoint" / "test_predictions.csv"
    checkpoint_hash = sha256(checkpoint)
    predictions_hash = sha256(predictions)
    require(checkpoint_hash == "0aa004a4bd1bbbdaf550a115ad0ed5cda16b6269b8753b97bd076cd1fc55d662", "checkpoint hash mismatch")
    require(predictions_hash == "22a6341fd32a5468ddafd217cb2eff746a2f07d5e2578e117085e2c7f9403970", "prediction hash mismatch")
    return {
        "status": "pass",
        "profiles": len(evidence),
        "accuracy_percent": expected_accuracy,
        "traffic_percent_of_fp32": traffic,
        "checkpoint_sha256": checkpoint_hash,
        "predictions_sha256": predictions_hash,
    }


def validate_optical() -> dict[str, object]:
    evidence = rows(OPTICAL / "optical_intervention.csv")
    electrical = next(row for row in evidence if row["transport_mode"] == "electrical_contended")
    wdm = next(row for row in evidence if row["transport_mode"] == "optical_contended")
    payload_ratio = float(wdm["effective_payload_bits_per_cycle"]) / float(electrical["effective_payload_bits_per_cycle"])
    cycle_reduction = 100 * (1 - int(wdm["total_cycles"]) / int(electrical["total_cycles"]))
    require(payload_ratio == 4.0, "optical effective payload ratio changed")
    require((int(electrical["total_cycles"]), int(wdm["total_cycles"])) == (8193, 2049), "optical intervention cycles changed")
    return {
        "status": "pass",
        "effective_payload_ratio": payload_ratio,
        "electrical_cycles": int(electrical["total_cycles"]),
        "optical_cycles": int(wdm["total_cycles"]),
        "cycle_reduction_percent": round(cycle_reduction, 6),
    }


def validate_trace() -> dict[str, object]:
    evidence = rows(REVIEWER / "stage_scalability.csv")
    largest = [row for row in evidence if row["mesh_dimension"] == "32" and row["packet_count"] == "1000000" and row["seed"] == "40"]
    metrics_runs = [row for row in largest if row["trace_mode"] == "metrics_only"]
    full_runs = [row for row in largest if row["trace_mode"] == "full"]
    require(len(metrics_runs) == 3 and len(full_runs) == 3, "largest-case repeat set is incomplete")
    metrics_kernel = statistics.median(float(row["simulation_wall_seconds"]) for row in metrics_runs)
    metrics_e2e = statistics.median(float(row["process_wall_seconds"]) for row in metrics_runs)
    full_e2e = statistics.median(float(row["process_wall_seconds"]) for row in full_runs)
    full_median = sorted(full_runs, key=lambda row: float(row["process_wall_seconds"]))[1]
    require(round(metrics_kernel, 2) == 2.35, "metrics-only kernel median changed")
    require(round(metrics_e2e, 2) == 8.32, "metrics-only end-to-end median changed")
    require(round(full_e2e, 2) == 63.08, "full-trace end-to-end median changed")
    require(int(full_median["event_count"]) == 46852084, "full-trace event count changed")
    return {
        "status": "pass",
        "mesh": "32x32",
        "packets": 1_000_000,
        "cycles": int(full_median["simulated_cycles"]),
        "metrics_kernel_median_seconds": metrics_kernel,
        "metrics_end_to_end_median_seconds": metrics_e2e,
        "full_trace_end_to_end_median_seconds": full_e2e,
        "full_trace_event_count": int(full_median["event_count"]),
        "raw_trace_bytes": int(full_median["raw_trace_bytes"]),
        "compressed_trace_bytes": int(full_median["compressed_trace_bytes"]),
        "raw_trace_included": False,
    }


VALIDATORS: dict[str, Callable[[], dict[str, object]]] = {
    "analytical": validate_analytical,
    "determinism": validate_determinism,
    "noc": validate_noc,
    "timeloop": validate_timeloop,
    "scalesim": validate_scalesim,
    "accelergy": validate_accelergy,
    "booksim": validate_booksim,
    "attribution": validate_attribution,
    "mapping": validate_mapping,
    "precision": validate_precision,
    "optical": validate_optical,
    "trace": validate_trace,
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--claim", choices=["all", *VALIDATORS], default="all", help="validate one claim or the complete frozen evidence set")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    selected = list(VALIDATORS) if args.claim == "all" else [args.claim]
    report = {
        "status": "pass",
        "selected_claim": args.claim,
        "claims": {name: VALIDATORS[name]() for name in selected},
    }
    OUT.mkdir(parents=True, exist_ok=True)
    output_name = "frozen_validation.json" if args.claim == "all" else f"claim_{args.claim}.json"
    (OUT / output_name).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
