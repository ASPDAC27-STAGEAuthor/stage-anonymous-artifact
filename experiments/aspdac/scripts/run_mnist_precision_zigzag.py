#!/usr/bin/env python3
"""Run the four MNIST precision profiles with an independent ZigZag DSE model."""

from __future__ import annotations

import argparse
import csv
import hashlib
import importlib.metadata
import json
import pickle
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from zigzag import api


PROFILES = (
    {"profile_id": "fp32_a32", "payload_bits": 32, "accumulator_bits": 32},
    {"profile_id": "fp16_a16", "payload_bits": 16, "accumulator_bits": 16},
    {"profile_id": "fp8_a16", "payload_bits": 8, "accumulator_bits": 16},
    {"profile_id": "fp8_a8", "payload_bits": 8, "accumulator_bits": 8},
)

LAYERS = (
    {"id": 0, "layer_id": "conv1", "M": 576, "N": 6, "K": 25},
    {"id": 1, "layer_id": "conv2", "M": 64, "N": 16, "K": 150},
    {"id": 2, "layer_id": "fc1", "M": 1, "N": 120, "K": 256},
    {"id": 3, "layer_id": "fc2", "M": 1, "N": 84, "K": 120},
    {"id": 4, "layer_id": "fc3", "M": 1, "N": 10, "K": 84},
)

IMAGE_COUNT = 10_000
L1_SIZE_BITS = 2_097_152
L1_BANDWIDTH_BITS_PER_CYCLE = 512
L3_BANDWIDTH_BITS_PER_CYCLE = 256


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def workload_text(profile: dict[str, Any], layers: tuple[dict[str, Any], ...]) -> str:
    payload = int(profile["payload_bits"])
    accumulator = int(profile["accumulator_bits"])
    blocks: list[str] = []
    for layer in layers:
        m_total = int(layer["M"]) * IMAGE_COUNT
        blocks.append(f"""- id: {layer['id']}
  name: {layer['layer_id']}
  operator_type: Gemm
  equation: O[d0][d1]+=I[d0][d2]*W[d2][d1]
  dimension_relations: []
  loop_dims: [D0, D1, D2]
  loop_sizes: [{m_total}, {layer['N']}, {layer['K']}]
  operand_precision:
    W: {payload}
    I: {payload}
    O: {accumulator}
    O_final: {payload}
  operand_source:
    I: {layer['id']}
    W: {layer['id']}
""")
    return "\n".join(blocks)


def hardware_text(profile: dict[str, Any]) -> str:
    payload = int(profile["payload_bits"])
    return f"""name: stage_like_4x4_{profile['profile_id']}

memories:
  reg_O:
    size: 32
    r_cost: 0.02
    w_cost: 0.02
    area: 0
    latency: 1
    auto_cost_extraction: False
    operands: [O]
    ports:
      - name: r_port_1
        type: read
        bandwidth_min: 32
        bandwidth_max: 32
        allocation: [O, tl]
      - name: r_port_2
        type: read
        bandwidth_min: 32
        bandwidth_max: 32
        allocation: [O, th]
      - name: w_port_1
        type: write
        bandwidth_min: 32
        bandwidth_max: 32
        allocation: [O, fh]
      - name: w_port_2
        type: write
        bandwidth_min: 32
        bandwidth_max: 32
        allocation: [O, fl]
    served_dimensions: [D2]

  l1:
    size: {L1_SIZE_BITS}
    r_cost: 22.9
    w_cost: 52.01
    area: 0
    latency: 1
    auto_cost_extraction: False
    operands: [I1, I2, O]
    ports:
      - name: rw_port_1
        type: read_write
        bandwidth_min: 32
        bandwidth_max: {L1_BANDWIDTH_BITS_PER_CYCLE}
        allocation: [I1, tl]
      - name: rw_port_2
        type: read_write
        bandwidth_min: 32
        bandwidth_max: {L1_BANDWIDTH_BITS_PER_CYCLE}
        allocation: [I2, tl]
      - name: rw_port_3
        type: read_write
        bandwidth_min: 32
        bandwidth_max: {L1_BANDWIDTH_BITS_PER_CYCLE}
        allocation: [O, tl, O, fl]
      - name: rw_port_4
        type: read_write
        bandwidth_min: 32
        bandwidth_max: {L1_BANDWIDTH_BITS_PER_CYCLE}
        allocation: [I1, fh, I2, fh, O, fh, O, th]
    served_dimensions: [D1, D2]
    force_double_buffering: True

  l3:
    size: 10000000000
    r_cost: 16000
    w_cost: 24000
    area: 0
    latency: 1
    auto_cost_extraction: False
    operands: [I1, I2, O]
    ports:
      - name: rw_port_1
        type: read_write
        bandwidth_min: {L3_BANDWIDTH_BITS_PER_CYCLE}
        bandwidth_max: {L3_BANDWIDTH_BITS_PER_CYCLE}
        allocation: [I1, fh, I1, tl, I2, fh, I2, tl, O, fh, O, tl, O, fl, O, th]
    served_dimensions: [D1, D2]

operational_array:
  input_precision: [{payload}, {payload}]
  unit_energy: 0.04
  unit_area: 1
  dimensions: [D1, D2]
  sizes: [4, 4]
"""


def mapping_text() -> str:
    return """- name: default
  spatial_mapping:
    D1:
      - D0, 4
    D2:
      - D1, 4
  temporal_ordering:
    - [D2, "*"]
    - [D1, "*"]
    - [D0, "*"]
  memory_operand_links:
    O: O
    W: I2
    I: I1
"""


def sum_accesses(accesses: Any) -> int:
    return int(sum(int(value) for value in accesses.data.values()))


def cme_metrics(cme: Any) -> dict[str, Any]:
    total_word_accesses = 0
    l3_word_accesses = 0
    access_breakdown: dict[str, list[dict[str, Any]]] = {}
    for operand, levels in cme.memory_word_access.items():
        level_rows: list[dict[str, Any]] = []
        for level_index, accesses in enumerate(levels):
            count = sum_accesses(accesses)
            total_word_accesses += count
            if level_index == len(levels) - 1:
                l3_word_accesses += count
            level_rows.append({
                "level_index": level_index,
                "word_accesses": count,
                "directions": {str(direction.value): int(value) for direction, value in accesses.data.items()},
            })
        access_breakdown[str(operand)] = level_rows
    return {
        "energy_pj": float(cme.energy_total),
        "mac_energy_pj": float(cme.mac_energy),
        "memory_energy_pj": float(cme.mem_energy),
        "latency_cycles": float(cme.latency_total2),
        "ideal_cycles": float(cme.ideal_cycle),
        "mac_utilization": float(cme.mac_utilization2),
        "total_word_accesses": total_word_accesses,
        "l3_word_accesses": l3_word_accesses,
        "l3_transaction_bits": l3_word_accesses * L3_BANDWIDTH_BITS_PER_CYCLE,
        "memory_word_access_breakdown": access_breakdown,
    }


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    columns: list[str] = []
    for row in rows:
        for key in row:
            if key not in columns:
                columns.append(key)
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns)
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--smoke", action="store_true")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        raise SystemExit(f"Refusing non-empty output directory: {output}")
    output.mkdir(parents=True, exist_ok=True)
    profiles = PROFILES[:1] if args.smoke else PROFILES
    layers = LAYERS[:1] if args.smoke else LAYERS
    started = utc_now()
    rows: list[dict[str, Any]] = []

    for profile in profiles:
        case_dir = output / "raw" / profile["profile_id"]
        config_dir = case_dir / "config"
        config_dir.mkdir(parents=True)
        workload = config_dir / "workload.yaml"
        hardware = config_dir / "hardware.yaml"
        mapping = config_dir / "mapping.yaml"
        workload.write_text(workload_text(profile, layers), encoding="utf-8", newline="\n")
        hardware.write_text(hardware_text(profile), encoding="utf-8", newline="\n")
        mapping.write_text(mapping_text(), encoding="utf-8", newline="\n")
        dump_folder = case_dir / "zigzag_output"
        pickle_path = case_dir / "list_of_cmes.pickle"
        try:
            energy, latency, cmes = api.get_hardware_performance_zigzag(
                workload=str(workload),
                accelerator=str(hardware),
                mapping=str(mapping),
                opt="energy",
                dump_folder=str(dump_folder),
                pickle_filename=str(pickle_path),
                lpf_limit=6,
                nb_spatial_mappings_generated=1,
                loma_show_progress_bar=False,
            )
            root = cmes[0][0] if isinstance(cmes[0], tuple) else cmes[0]
            metrics = cme_metrics(root)
            status = "completed"
            failure = ""
        except Exception as exception:
            metrics = {}
            status = "failed"
            failure = f"{type(exception).__name__}: {exception}"
        result = {
            "status": status,
            "profile_id": profile["profile_id"],
            "payload_bits": profile["payload_bits"],
            "accumulator_bits": profile["accumulator_bits"],
            "layer_count": len(layers),
            "workload_sha256": sha256(workload),
            "hardware_sha256": sha256(hardware),
            "mapping_sha256": sha256(mapping),
            **metrics,
            "failure": failure,
            "raw_path": case_dir.as_posix(),
        }
        (case_dir / "case_result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        rows.append(result)
        if status != "completed":
            raise RuntimeError(f"ZigZag failed for {profile['profile_id']}: {failure}")

    baseline = rows[0]
    for row in rows:
        for metric in ("energy_pj", "memory_energy_pj", "latency_cycles", "total_word_accesses", "l3_transaction_bits"):
            row[f"{metric}_reduction_vs_fp32_percent"] = 100.0 * (1.0 - float(row[metric]) / float(baseline[metric]))
    write_csv(output / "summary" / "zigzag_precision_trend.csv", [
        {key: value for key, value in row.items() if key != "memory_word_access_breakdown"}
        for row in rows
    ])
    source_commit = subprocess.run(
        ["git", "-C", str(args.source), "rev-parse", "HEAD"],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    ).stdout.strip()
    manifest = {
        "schema": "aspg.mnist_precision_zigzag.v1",
        "started_at_utc": started,
        "completed_at_utc": utc_now(),
        "tool": "ZigZag",
        "zigzag_version": importlib.metadata.version("zigzag-dse"),
        "zigzag_source_commit": source_commit,
        "source_path": str(args.source),
        "profiles": list(profiles),
        "layer_count": len(layers),
        "image_count": IMAGE_COUNT,
        "physical_contract": {
            "array": "4x4",
            "l1_size_bits": L1_SIZE_BITS,
            "l1_bandwidth_bits_per_cycle": L1_BANDWIDTH_BITS_PER_CYCLE,
            "l3_bandwidth_bits_per_cycle": L3_BANDWIDTH_BITS_PER_CYCLE,
            "spatial_mapping": "D0x4 by D1x4",
            "temporal_mapping": "LOMA optimized independently per precision",
        },
        "energy_boundary": "Memory costs are fixed declared ZigZag reference values and operational unit energy is fixed at 0.04 pJ/op; precision-dependent compute-circuit energy is not claimed.",
        "traffic_boundary": "l3_transaction_bits is the number of modeled L3 word accesses multiplied by the fixed 256-bit L3 port width, including transfer granularity effects.",
        "all_completed": all(row["status"] == "completed" for row in rows),
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"manifest": manifest, "summary": rows}, indent=2))


if __name__ == "__main__":
    main()
