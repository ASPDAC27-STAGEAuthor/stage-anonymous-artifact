#!/usr/bin/env python3
"""Validate and summarize the constrained-electrical to WDM-optical intervention."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
from typing import Any


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def rel(path: Path, repo: Path) -> str:
    return path.resolve().relative_to(repo.resolve()).as_posix()


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", required=True)
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[3]
    bundle = (repo / args.bundle).resolve() if not Path(args.bundle).is_absolute() else Path(args.bundle).resolve()
    raw_paths = sorted((bundle / "raw").glob("**/*.json"))
    if len(raw_paths) != 4:
        raise RuntimeError(f"Expected four intervention results, found {len(raw_paths)}")

    results = [json.loads(path.read_text(encoding="utf-8")) for path in raw_paths]
    if any(result.get("status") != "completed" for result in results):
        raise RuntimeError("Every intervention result must be completed")

    groups: dict[str, list[dict[str, Any]]] = {}
    for result in results:
        mode = result["metrics"]["TransportMode"]
        groups.setdefault(mode, []).append(result)
    if set(groups) != {"electrical_contended", "optical_contended"}:
        raise RuntimeError(f"Unexpected transport modes: {sorted(groups)}")
    if any(len(group) != 2 for group in groups.values()):
        raise RuntimeError("Each transport mode must have two repeats")

    repeat_fields = ("TotalCycles", "MeanPacketLatencyCycles", "BackpressureEvents", "TotalTransportEnergyPj", "CanonicalTraceSha256")
    for mode, group in groups.items():
        if any(len({item["metrics"][field] for item in group}) != 1 for field in repeat_fields):
            raise RuntimeError(f"Repeat mismatch in {mode}")

    paired_fields = ("WorkloadHash", "MappingHash", "ComputeHash", "MemoryHash", "EndpointHash")
    representatives = {mode: sorted(group, key=lambda item: int(item["resolved"]["parameters"]["repeat"]))[0] for mode, group in groups.items()}
    if any(len({item["metrics"][field] for item in representatives.values()}) != 1 for field in paired_fields):
        raise RuntimeError("A non-transport paired hash changed")
    if len({item["provenance"]["graph_hash"] for item in representatives.values()}) != 1:
        raise RuntimeError("The paired graph hash changed")

    electrical = representatives["electrical_contended"]["metrics"]
    optical = representatives["optical_contended"]["metrics"]
    if int(electrical["ChannelCapacity"]) != 1 or int(optical["ChannelCapacity"]) != 8:
        raise RuntimeError("Expected one electrical channel and eight optical wavelengths")
    if not int(optical["TotalCycles"]) < int(electrical["TotalCycles"]):
        raise RuntimeError("The WDM intervention did not reduce cycles")
    if not float(optical["TotalTransportEnergyPj"]) > float(electrical["TotalTransportEnergyPj"]):
        raise RuntimeError("The expected energy trade-off is absent")

    rows: list[dict[str, Any]] = []
    for label, metrics in (("Single-channel electrical", electrical), ("Eight-wavelength optical", optical)):
        rows.append({
            "configuration": label,
            "transport_mode": metrics["TransportMode"],
            "channel_capacity": metrics["ChannelCapacity"],
            "service_cycles_per_packet": metrics["ServiceCyclesPerPacket"],
            "effective_payload_bits_per_cycle": float(metrics["ChannelCapacity"]) * float(metrics["PayloadBits"]) / float(metrics["ServiceCyclesPerPacket"]),
            "packet_count": metrics["PacketCount"],
            "total_cycles": metrics["TotalCycles"],
            "mean_packet_latency_cycles": metrics["MeanPacketLatencyCycles"],
            "backpressure_events": metrics["BackpressureEvents"],
            "total_transport_energy_pj": metrics["TotalTransportEnergyPj"],
            "canonical_trace_sha256": metrics["CanonicalTraceSha256"],
            "repeat_count": 2,
            "workload_hash": metrics["WorkloadHash"],
            "mapping_hash": metrics["MappingHash"],
            "compute_hash": metrics["ComputeHash"],
            "memory_hash": metrics["MemoryHash"],
            "endpoint_hash": metrics["EndpointHash"],
            "evidence_level": "Measured paired transport intervention",
        })

    summary = {
        "cycle_reduction_pct": 100.0 * (1.0 - float(optical["TotalCycles"]) / float(electrical["TotalCycles"])),
        "latency_reduction_pct": 100.0 * (1.0 - float(optical["MeanPacketLatencyCycles"]) / float(electrical["MeanPacketLatencyCycles"])),
        "backpressure_reduction_pct": 100.0 * (1.0 - float(optical["BackpressureEvents"]) / float(electrical["BackpressureEvents"])),
        "energy_ratio": float(optical["TotalTransportEnergyPj"]) / float(electrical["TotalTransportEnergyPj"]),
        "effective_payload_rate_ratio": rows[1]["effective_payload_bits_per_cycle"] / rows[0]["effective_payload_bits_per_cycle"],
        "repeat_hashes_identical": True,
        "paired_non_transport_hashes_identical": True,
    }

    summary_dir = bundle / "summary"
    summary_dir.mkdir(parents=True, exist_ok=True)
    csv_path = summary_dir / "optical_intervention.csv"
    json_path = summary_dir / "optical_intervention_summary.json"
    claim_path = summary_dir / "optical_intervention_claim_status.md"
    write_csv(csv_path, rows)
    json_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    claim_path.write_text(
        "# Optical intervention claim status\n\n"
        "| Claim | Status | Result |\n"
        "| --- | --- | --- |\n"
        f"| WDM relieves the constrained link | measured | {int(electrical['TotalCycles']):,} to {int(optical['TotalCycles']):,} cycles ({summary['cycle_reduction_pct']:.1f}% reduction) |\n"
        f"| WDM lowers transport energy | not supported | {float(electrical['TotalTransportEnergyPj']) / 1000:.2f} to {float(optical['TotalTransportEnergyPj']) / 1000:.2f} nJ |\n"
        "| Optical is faster at equal channel count | not supported | The matched eight-channel comparison remains 1,025 electrical versus 2,049 optical cycles |\n\n"
        "The positive result comes from replacing one electrical channel with eight wavelength channels while preserving the workload, graph, mapping, compute, memory, and endpoints.\n",
        encoding="utf-8",
    )

    manifest_inputs = raw_paths + [csv_path, json_path, claim_path]
    figure_dir = bundle / "figures"
    if figure_dir.exists():
        manifest_inputs.extend(sorted(path for path in figure_dir.iterdir() if path.is_file()))
    manifest = {
        "schema_version": "aspg-optical-intervention-output-manifest-1.0",
        "claim_boundary": "Measured latency relief from WDM parallelism; no energy advantage and no equal-channel optical win.",
        "files": [
            {"path": rel(path, repo), "sha256": sha256_file(path), "bytes": path.stat().st_size}
            for path in manifest_inputs
        ],
    }
    manifest_path = bundle / "manifests" / "optical_intervention_output_manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"cases": len(results), "summary": summary, "manifest": rel(manifest_path, repo)}, indent=2))


if __name__ == "__main__":
    main()