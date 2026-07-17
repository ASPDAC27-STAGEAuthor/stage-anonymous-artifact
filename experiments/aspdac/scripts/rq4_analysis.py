#!/usr/bin/env python3
"""Validate RQ4 paired transport evidence and build tidy outputs/figures."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import matplotlib.pyplot as plt

import final_analysis as common


BUNDLE = common.BUNDLE
WORKLOADS = ["attention_128_64", "gemm_256"]
LABELS = {"attention_128_64": "Attention", "gemm_256": "GEMM"}


def load_rq4() -> tuple[list[dict[str, Any]], list[Path]]:
    results, inputs = common.load_plan_results("rq4_optical")
    if len(results) != 14:
        raise RuntimeError(f"Expected 14 RQ4 results, found {len(results)}")
    return results, inputs


def oracle_rows(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for result in results:
        if result["scenario"] != "optical_oracle":
            continue
        metrics = result["metrics"]
        rows.append({
            "repeat": result["axes"]["repeat"],
            "evidence_level": "Exact",
            "route_loss_db": metrics["RouteLossDb"],
            "received_power_dbm": metrics["ReceivedPowerDbm"],
            "exact_receiver_sensitivity_dbm": -3.0,
            "exact_case_margin_db": metrics["ExactCaseMarginDb"],
            "matched_receiver_sensitivity_dbm": -18.0,
            "matched_system_margin_db": metrics["MatchedSystemMarginDb"],
            "logical_bits": metrics["LogicalBits"],
            "encoded_bits": metrics["EncodedBits"],
            "service_cycles": metrics["ServiceCycles"],
            "channels": ";".join(metrics["ChannelIds"]),
            "wavelengths_nm": ";".join(str(value) for value in metrics["WavelengthsNanometers"]),
            "capacity_sweep": ";".join(str(value) for value in metrics["CapacitySweep"]),
            "ber_status": metrics["BerStatus"],
            "canonical_trace_sha256": metrics["CanonicalTraceSha256"],
            "candidate_id": result["candidate_id"],
            "raw_path": common.repo_path(next(path for path in (BUNDLE / "raw/rq4_optical_oracle").glob(f"{result['candidate_id']}.json"))),
        })
    if len(rows) != 2:
        raise RuntimeError(f"Expected two oracle repeats, found {len(rows)}")
    if len({row["canonical_trace_sha256"] for row in rows}) != 1:
        raise RuntimeError("RQ4 oracle repeat hashes differ")
    expected = (("route_loss_db", 4.11), ("received_power_dbm", -4.11), ("exact_case_margin_db", -1.11), ("matched_system_margin_db", 13.89))
    for row in rows:
        for key, value in expected:
            if abs(float(row[key]) - value) > 1e-12:
                raise RuntimeError(f"RQ4 oracle mismatch for {key}: {row[key]}")
        if int(row["encoded_bits"]) != 132 or int(row["service_cycles"]) != 2:
            raise RuntimeError("RQ4 64b/66b serialization oracle failed")
    return sorted(rows, key=lambda row: int(row["repeat"]))


def transport_rows(results: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for result in results:
        if result["scenario"] != "matched_transport":
            continue
        metrics, provenance = result["metrics"], result["provenance"]
        rows.append({
            "workload": metrics["WorkloadId"],
            "transport_mode": metrics["TransportMode"],
            "channel_capacity": metrics["ChannelCapacity"],
            "packet_count": metrics["PacketCount"],
            "flow_count": metrics["FlowCount"],
            "payload_bits_per_packet": metrics["PayloadBits"],
            "encoded_bits_per_packet": metrics["EncodedBitsPerPacket"],
            "service_cycles_per_packet": metrics["ServiceCyclesPerPacket"],
            "total_cycles": metrics["TotalCycles"],
            "mean_packet_latency_cycles": metrics["MeanPacketLatencyCycles"],
            "conflict_count": metrics["ConflictCount"],
            "backpressure_events": metrics["BackpressureEvents"],
            "logical_bits": metrics["LogicalBits"],
            "encoded_bits": metrics["EncodedBits"],
            "route_loss_db": metrics["RouteLossDb"],
            "received_power_dbm": metrics["ReceivedPowerDbm"],
            "receiver_margin_db": metrics["ReceiverMarginDb"],
            "serdes_energy_pj": metrics["SerdesEnergyPj"],
            "conversion_energy_pj": metrics["ConversionEnergyPj"],
            "tuning_energy_pj": metrics["TuningEnergyPj"],
            "laser_energy_pj": metrics["LaserEnergyPj"],
            "link_energy_pj": metrics["LinkEnergyPj"],
            "endpoint_energy_pj": metrics["EndpointEnergyPj"],
            "total_transport_energy_pj": metrics["TotalTransportEnergyPj"],
            "ber_status": metrics["BerStatus"],
            "graph_hash": provenance["graph_hash"],
            "workload_hash": metrics["WorkloadHash"],
            "mapping_hash": metrics["MappingHash"],
            "compute_hash": metrics["ComputeHash"],
            "memory_hash": metrics["MemoryHash"],
            "endpoint_hash": metrics["EndpointHash"],
            "transport_hash": metrics["TransportHash"],
            "model_profile_hash": provenance["model_profile_hash"],
            "canonical_trace_sha256": metrics["CanonicalTraceSha256"],
            "evidence_level": "Measured paired transport runtime",
            "candidate_id": result["candidate_id"],
            "experiment_name": result["experiment_name"],
        })
    if len(rows) != 12:
        raise RuntimeError(f"Expected 12 matched transport rows, found {len(rows)}")
    invariant_keys = ("graph_hash", "workload_hash", "mapping_hash", "compute_hash", "memory_hash", "endpoint_hash", "packet_count", "flow_count")
    for workload in WORKLOADS:
        group = [row for row in rows if row["workload"] == workload]
        if len(group) != 6:
            raise RuntimeError(f"Expected six paired rows for {workload}, found {len(group)}")
        for key in invariant_keys:
            if len({row[key] for row in group}) != 1:
                raise RuntimeError(f"Paired invariant {key} differs for {workload}")
        contended = sorted((row for row in group if row["transport_mode"] == "optical_contended"), key=lambda row: int(row["channel_capacity"]))
        cycles = [int(row["total_cycles"]) for row in contended]
        conflicts = [int(row["conflict_count"]) for row in contended]
        backpressure = [int(row["backpressure_events"]) for row in contended]
        if not all(left >= right for left, right in zip(cycles, cycles[1:])):
            raise RuntimeError(f"Capacity/cycle relation failed for {workload}: {cycles}")
        if not all(left >= right for left, right in zip(conflicts, conflicts[1:])):
            raise RuntimeError(f"Capacity/conflict relation failed for {workload}: {conflicts}")
        if not all(left >= right for left, right in zip(backpressure, backpressure[1:])):
            raise RuntimeError(f"Capacity/backpressure relation failed for {workload}: {backpressure}")
        conflict_free = next(row for row in group if row["transport_mode"] == "optical_conflict_free")
        cap8 = next(row for row in contended if int(row["channel_capacity"]) == 8)
        if int(cap8["total_cycles"]) < int(conflict_free["total_cycles"]):
            raise RuntimeError(f"Contended cap8 beats conflict-free for {workload}")
    return sorted(rows, key=lambda row: (WORKLOADS.index(row["workload"]), row["transport_mode"], int(row["channel_capacity"])))


def build_figure(oracle: list[dict[str, Any]], transport: list[dict[str, Any]], output: Path) -> None:
    attention = [row for row in transport if row["workload"] == "attention_128_64"]
    contended = sorted((row for row in attention if row["transport_mode"] == "optical_contended"), key=lambda row: int(row["channel_capacity"]))
    capacities = [int(row["channel_capacity"]) for row in contended]
    fig, axes = plt.subplots(1, 3, figsize=(13.2, 3.8))

    oracle_loss = float(oracle[0]["route_loss_db"])
    axes[0].plot([0, 5], [0, 5], color="#64748b", linestyle="--", linewidth=1, label="y=x")
    axes[0].scatter([4.11], [oracle_loss], color="#2563eb", s=60, label="Exact route")
    axes[0].set_xlim(3.9, 4.3)
    axes[0].set_ylim(3.9, 4.3)
    axes[0].set_xlabel("Analytical loss (dB)")
    axes[0].set_ylabel("STAGE loss (dB)")
    axes[0].set_title("(a) Exact optical oracle")
    axes[0].grid(alpha=0.22)
    axes[0].legend(frameon=False, fontsize=8)

    axes[1].plot(capacities, [float(row["total_cycles"]) for row in contended], marker="o", color="#2563eb", label="Cycles")
    axes[1].set_xscale("log", base=2)
    axes[1].set_yscale("log", base=2)
    axes[1].set_xticks(capacities, [str(value) for value in capacities])
    axes[1].set_xlabel("Shared wavelengths")
    axes[1].set_ylabel("Transport cycles (log2)", color="#2563eb")
    twin = axes[1].twinx()
    twin.plot(capacities, [float(row["conflict_count"]) for row in contended], marker="s", color="#dc2626", label="Conflicts")
    twin.set_yscale("symlog", linthresh=1)
    twin.set_ylabel("Wavelength conflicts", color="#dc2626")
    axes[1].set_title("(b) Attention capacity sweep")
    axes[1].grid(alpha=0.22)

    selected = [
        next(row for row in attention if row["transport_mode"] == "electrical"),
        next(row for row in attention if row["transport_mode"] == "optical_conflict_free"),
        next(row for row in attention if row["transport_mode"] == "optical_contended" and int(row["channel_capacity"]) == 1),
    ]
    labels = ["Electrical", "Optical CF", "Optical cap1"]
    categories = [
        ("serdes_energy_pj", "SerDes", "#2563eb"),
        ("conversion_energy_pj", "Conversion", "#7c3aed"),
        ("tuning_energy_pj", "Tuning", "#94a3b8"),
        ("laser_energy_pj", "Laser", "#d97706"),
        ("link_energy_pj", "Link", "#16a34a"),
        ("endpoint_energy_pj", "Endpoint", "#dc2626"),
    ]
    bottom = [0.0] * len(selected)
    x = list(range(len(selected)))
    for key, label, color in categories:
        values = [float(row[key]) / 1000.0 for row in selected]
        axes[2].bar(x, values, bottom=bottom, color=color, label=label)
        bottom = [left + value for left, value in zip(bottom, values)]
    axes[2].set_xticks(x, labels, rotation=18)
    axes[2].set_ylabel("Transport energy (nJ)")
    axes[2].set_title("(c) Measured energy categories")
    axes[2].grid(axis="y", alpha=0.22)
    axes[2].legend(frameon=False, fontsize=7, ncol=2)
    fig.tight_layout()
    fig.savefig(output, bbox_inches="tight")
    fig.savefig(output.with_suffix(".png"), dpi=220, bbox_inches="tight")
    plt.close(fig)


def main() -> None:
    results, inputs = load_rq4()
    oracle = oracle_rows(results)
    transport = transport_rows(results)
    oracle_path = BUNDLE / "summary/rq4_optical_oracle.csv"
    transport_path = BUNDLE / "summary/rq4_matched_transport.csv"
    common.write_csv(oracle_path, oracle)
    common.write_csv(transport_path, transport)
    figure = BUNDLE / "figures/fig_rq4_electrical_optical.pdf"
    build_figure(oracle, transport, figure)

    outputs = [oracle_path, transport_path, figure, figure.with_suffix(".png")]
    manifest = BUNDLE / "manifests/rq4_output_manifest.json"
    manifest.write_text(json.dumps({
        "schema_version": "aspg-derived-output-manifest-1.0",
        "rq": "RQ4",
        "inputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in inputs],
        "outputs": [{"path": common.repo_path(path), "sha256": common.sha256(path)} for path in outputs],
        "oracle_repeat_count": 2,
        "oracle_hashes_identical": True,
        "matched_transport_cases": 12,
        "paired_invariants_verified": ["graph", "workload", "mapping", "compute", "memory", "endpoint", "packet_count", "flow_count"],
        "capacity_relation_verified": "cycles/conflicts/backpressure non-increasing for capacities 1,2,4,8",
        "ber_scope": "BER not modeled",
        "energy_scope": "Phase 8 shared functional reference values; not silicon calibrated",
    }, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "oracle_repeats": len(oracle),
        "matched_transport_cases": len(transport),
        "failures": 0,
        "attention_cycles": {f"{row['transport_mode']}_cap{row['channel_capacity']}": row["total_cycles"] for row in transport if row["workload"] == "attention_128_64"},
    }, indent=2))


if __name__ == "__main__":
    main()
