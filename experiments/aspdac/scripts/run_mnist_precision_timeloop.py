#!/usr/bin/env python3
"""Run MNIST precision profiles through an external Timeloop installation.

Two comparison modes are retained:

* frozen_fp32_mapping: find one mapping with FP32, then reuse that exact mapping
  for every precision profile;
* precision_optimized_mapping: let Timeloop search independently at each
  precision while physical storage capacity and bit bandwidth remain fixed.

The workload is the same five lowered MNIST GEMMs used by the STAGE precision
bundle. M is multiplied by 10,000 so weights are loaded once for the complete
test set, matching the aggregate STAGE accounting contract.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import shutil
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PROFILES = (
    {"profile_id": "fp32_a32", "payload_bits": 32, "accumulator_bits": 32},
    {"profile_id": "fp16_a16", "payload_bits": 16, "accumulator_bits": 16},
    {"profile_id": "fp8_a16", "payload_bits": 8, "accumulator_bits": 16},
    {"profile_id": "fp8_a8", "payload_bits": 8, "accumulator_bits": 8},
)

LAYERS = (
    {"layer_id": "conv1", "M": 576, "N": 6, "K": 25, "spatial": "M4 N2 K1"},
    {"layer_id": "conv2", "M": 64, "N": 16, "K": 150, "spatial": "M4 N4 K1"},
    {"layer_id": "fc1", "M": 1, "N": 120, "K": 256, "spatial": "M1 N4 K4"},
    {"layer_id": "fc2", "M": 1, "N": 84, "K": 120, "spatial": "M1 N4 K4"},
    {"layer_id": "fc3", "M": 1, "N": 10, "K": 84, "spatial": "M1 N2 K4"},
)

IMAGE_COUNT = 10_000
PACKET_BITS = 128
LOCAL_BUFFER_CAPACITY_BITS_PER_PE = 8_192
GLOBAL_BUFFER_CAPACITY_KIB = 256
GLOBAL_BUFFER_BANDWIDTH_BITS_PER_CYCLE = 512
DRAM_BANDWIDTH_BITS_PER_CYCLE = 256


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def run_capture(command: list[str], cwd: Path) -> dict[str, Any]:
    started = time.perf_counter()
    completed = subprocess.run(
        command,
        cwd=cwd,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    return {
        "command": command,
        "return_code": completed.returncode,
        "elapsed_seconds": round(time.perf_counter() - started, 6),
        "stdout": completed.stdout,
        "stderr": completed.stderr,
    }


def deck_text(layer: dict[str, Any], profile: dict[str, Any]) -> str:
    payload_bits = int(profile["payload_bits"])
    accumulator_bits = int(profile["accumulator_bits"])
    local_entries = LOCAL_BUFFER_CAPACITY_BITS_PER_PE // payload_bits
    register_entries = 32 // accumulator_bits
    block_size = 256 // payload_bits
    global_bandwidth = GLOBAL_BUFFER_BANDWIDTH_BITS_PER_CYCLE // payload_bits
    dram_bandwidth = DRAM_BANDWIDTH_BITS_PER_CYCLE // payload_bits
    m_total = int(layer["M"]) * IMAGE_COUNT
    return f"""mapper:
  algorithm: random-pruned
  optimization_metrics: [energy, delay]
  search_size: 256
  victory_condition: 32
  timeout: 120
  num_threads: 4

arch:
  arithmetic:
    instances: 16
    meshX: 4
    word_bits: {accumulator_bits}
  storage:
  - name: Registers
    entries: {register_entries}
    instances: 16
    meshX: 4
    word_bits: {accumulator_bits}
    cluster_size: 1
  - name: LocalBuffer
    entries: {local_entries}
    instances: 16
    meshX: 4
    word_bits: {payload_bits}
    block_size: 1
    cluster_size: 1
  - name: GlobalBuffer
    sizeKB: {GLOBAL_BUFFER_CAPACITY_KIB}
    instances: 1
    word_bits: {payload_bits}
    block_size: {block_size}
    bandwidth: {global_bandwidth}.0
  - name: DRAM
    technology: DRAM
    instances: 1
    word_bits: {payload_bits}
    block_size: {block_size}
    bandwidth: {dram_bandwidth}.0

mapspace:
  constraints:
  - target: Registers
    type: datatype
    keep: [Z]
    bypass: [A, B]
  - target: LocalBuffer
    type: datatype
    keep: [A, B, Z]
    bypass: []
  - target: GlobalBuffer
    type: datatype
    keep: [A, B, Z]
    bypass: []
  - target: GlobalBuffer
    type: spatial
    factors: {layer['spatial']}
    permutation: MNK
  - target: Registers
    type: utilization
    min: 0.01

problem:
  shape: gemm_ABZ
  M: {m_total}
  N: {layer['N']}
  K: {layer['K']}
"""


def last_float(pattern: str, text: str) -> float | None:
    values = re.findall(pattern, text, flags=re.MULTILINE)
    return float(values[-1]) if values else None


def parse_stats(path: Path) -> dict[str, float | None]:
    text = path.read_text(encoding="utf-8", errors="replace")
    summary = text.split("Summary Stats", 1)[-1]
    metrics: dict[str, float | None] = {
        "cycles": last_float(r"^Cycles:\s+([0-9.eE+-]+)", summary),
        "utilization_pct": last_float(r"^Utilization:\s+([0-9.eE+-]+)%", summary),
        "energy_uj": last_float(r"^Energy:\s+([0-9.eE+-]+) uJ", summary),
        "computes": last_float(r"^Computes\s*=\s*([0-9.eE+-]+)", summary),
    }
    for level, key in (
        ("Registers", "register_accesses"),
        ("LocalBuffer", "local_buffer_accesses"),
        ("GlobalBuffer", "global_buffer_accesses"),
        ("DRAM", "dram_accesses"),
    ):
        match = re.search(
            rf"=== {level} ===\s+Total scalar accesses\s+:\s+([0-9.eE+-]+)",
            text,
            flags=re.MULTILINE,
        )
        metrics[key] = float(match.group(1)) if match else None
    return metrics


def case_result(
    *,
    case_dir: Path,
    layer: dict[str, Any],
    profile: dict[str, Any],
    mode: str,
    executable: Path,
    mapping_source: Path | None,
) -> dict[str, Any]:
    case_dir.mkdir(parents=True, exist_ok=True)
    deck = case_dir / "input.yaml"
    deck.write_text(deck_text(layer, profile), encoding="utf-8", newline="\n")
    if mapping_source is None:
        command = [str(executable), str(deck)]
        prefix = "timeloop-mapper"
    else:
        frozen_mapping = case_dir / "mapping.yaml"
        shutil.copy2(mapping_source, frozen_mapping)
        command = [str(executable), str(deck), str(frozen_mapping)]
        prefix = "timeloop-model"
    run = run_capture(command, case_dir)
    (case_dir / "tool.stdout.log").write_text(
        run["stdout"] + "\n--- STDERR ---\n" + run["stderr"],
        encoding="utf-8",
        newline="\n",
    )
    stats_path = case_dir / f"{prefix}.stats.txt"
    generated_mapping = case_dir / f"{prefix}.map.yaml"
    metrics = parse_stats(stats_path) if stats_path.exists() else {}
    payload_bits = int(profile["payload_bits"])
    accumulator_bits = int(profile["accumulator_bits"])
    accesses = {
        "register": metrics.get("register_accesses"),
        "local_buffer": metrics.get("local_buffer_accesses"),
        "global_buffer": metrics.get("global_buffer_accesses"),
        "dram": metrics.get("dram_accesses"),
    }
    register_traffic_bits = None if accesses["register"] is None else accesses["register"] * accumulator_bits
    local_traffic_bits = None if accesses["local_buffer"] is None else accesses["local_buffer"] * payload_bits
    global_traffic_bits = None if accesses["global_buffer"] is None else accesses["global_buffer"] * payload_bits
    dram_traffic_bits = None if accesses["dram"] is None else accesses["dram"] * payload_bits
    hierarchy_values = [register_traffic_bits, local_traffic_bits, global_traffic_bits, dram_traffic_bits]
    completed = run["return_code"] == 0 and stats_path.exists() and all(value is not None for value in hierarchy_values)
    result = {
        "status": "completed" if completed else "failed",
        "mode": mode,
        "layer_id": layer["layer_id"],
        "profile_id": profile["profile_id"],
        "payload_bits": payload_bits,
        "accumulator_bits": accumulator_bits,
        "M": int(layer["M"]) * IMAGE_COUNT,
        "N": layer["N"],
        "K": layer["K"],
        "image_count": IMAGE_COUNT,
        "spatial_constraint": layer["spatial"],
        "return_code": run["return_code"],
        "elapsed_seconds": run["elapsed_seconds"],
        "deck_sha256": sha256(deck),
        "mapping_sha256": sha256(generated_mapping) if generated_mapping.exists() else (
            sha256(case_dir / "mapping.yaml") if (case_dir / "mapping.yaml").exists() else ""
        ),
        **metrics,
        "register_traffic_bits": register_traffic_bits,
        "local_buffer_traffic_bits": local_traffic_bits,
        "global_buffer_traffic_bits": global_traffic_bits,
        "dram_traffic_bits": dram_traffic_bits,
        "hierarchy_traffic_bits": sum(value for value in hierarchy_values if value is not None),
        "raw_path": case_dir.as_posix(),
    }
    (case_dir / "case_result.json").write_text(
        json.dumps(result, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    if not completed:
        raise RuntimeError(f"Timeloop failed for {mode}/{layer['layer_id']}/{profile['profile_id']}: {run['stderr'][-1000:]}")
    return result


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


def aggregate(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for mode in ("frozen_fp32_mapping", "precision_optimized_mapping"):
        mode_rows = [row for row in rows if row["mode"] == mode]
        baseline = [row for row in mode_rows if row["profile_id"] == "fp32_a32"]
        baseline_values = {
            key: sum(float(row[key]) for row in baseline)
            for key in ("cycles", "energy_uj", "dram_traffic_bits", "hierarchy_traffic_bits")
        }
        for profile in PROFILES:
            profile_rows = [row for row in mode_rows if row["profile_id"] == profile["profile_id"]]
            values = {
                key: sum(float(row[key]) for row in profile_rows)
                for key in ("cycles", "energy_uj", "dram_traffic_bits", "hierarchy_traffic_bits")
            }
            result.append({
                "mode": mode,
                "profile_id": profile["profile_id"],
                "payload_bits": profile["payload_bits"],
                "accumulator_bits": profile["accumulator_bits"],
                **values,
                "cycle_reduction_vs_fp32_percent": 100.0 * (1.0 - values["cycles"] / baseline_values["cycles"]),
                "energy_reduction_vs_fp32_percent": 100.0 * (1.0 - values["energy_uj"] / baseline_values["energy_uj"]),
                "dram_traffic_reduction_vs_fp32_percent": 100.0 * (1.0 - values["dram_traffic_bits"] / baseline_values["dram_traffic_bits"]),
                "hierarchy_traffic_reduction_vs_fp32_percent": 100.0 * (1.0 - values["hierarchy_traffic_bits"] / baseline_values["hierarchy_traffic_bits"]),
                "mapping_hashes": ";".join(row["mapping_sha256"] for row in profile_rows),
            })
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--timeloop-mapper", type=Path, default=Path("/opt/stage-tools/bin/timeloop-mapper"))
    parser.add_argument("--timeloop-model", type=Path, default=Path("/opt/stage-tools/bin/timeloop-model"))
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        raise SystemExit(f"Refusing non-empty output directory: {output}")
    output.mkdir(parents=True, exist_ok=True)
    started = utc_now()
    rows: list[dict[str, Any]] = []
    fp32_mappings: dict[str, Path] = {}

    for layer in LAYERS:
        for profile in PROFILES:
            case_dir = output / "raw" / "precision_optimized_mapping" / layer["layer_id"] / profile["profile_id"]
            row = case_result(
                case_dir=case_dir,
                layer=layer,
                profile=profile,
                mode="precision_optimized_mapping",
                executable=args.timeloop_mapper,
                mapping_source=None,
            )
            rows.append(row)
            if profile["profile_id"] == "fp32_a32":
                fp32_mappings[str(layer["layer_id"])] = case_dir / "timeloop-mapper.map.yaml"

    for layer in LAYERS:
        mapping = fp32_mappings[str(layer["layer_id"])]
        for profile in PROFILES:
            case_dir = output / "raw" / "frozen_fp32_mapping" / layer["layer_id"] / profile["profile_id"]
            rows.append(case_result(
                case_dir=case_dir,
                layer=layer,
                profile=profile,
                mode="frozen_fp32_mapping",
                executable=args.timeloop_model,
                mapping_source=mapping,
            ))

    summary = aggregate(rows)
    write_csv(output / "summary" / "timeloop_layer_cases.csv", rows)
    write_csv(output / "summary" / "timeloop_precision_trend.csv", summary)
    manifest = {
        "schema": "aspg.mnist_precision_timeloop.v1",
        "started_at_utc": started,
        "completed_at_utc": utc_now(),
        "tool": "Timeloop",
        "timeloop_mapper": str(args.timeloop_mapper),
        "timeloop_mapper_sha256": sha256(args.timeloop_mapper),
        "timeloop_model": str(args.timeloop_model),
        "timeloop_model_sha256": sha256(args.timeloop_model),
        "image_count": IMAGE_COUNT,
        "physical_contract": {
            "processing_elements": 16,
            "mesh_x": 4,
            "local_buffer_capacity_bits_per_pe": LOCAL_BUFFER_CAPACITY_BITS_PER_PE,
            "global_buffer_capacity_kib": GLOBAL_BUFFER_CAPACITY_KIB,
            "global_buffer_bandwidth_bits_per_cycle": GLOBAL_BUFFER_BANDWIDTH_BITS_PER_CYCLE,
            "dram_bandwidth_bits_per_cycle": DRAM_BANDWIDTH_BITS_PER_CYCLE,
        },
        "comparison_modes": {
            "frozen_fp32_mapping": "Exact FP32 mapping reused; isolates word-width and component-energy effects.",
            "precision_optimized_mapping": "Independent Timeloop map search per profile under fixed physical bit capacity/bandwidth.",
        },
        "mixed_precision_boundary": "Memory levels use payload width; Registers and arithmetic use accumulator width. FP8/A16 MAC energy is an accumulator-width surrogate, not a transistor-level mixed FP8xFP8-to-FP16 model.",
        "case_count": len(rows),
        "all_completed": all(row["status"] == "completed" for row in rows),
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"manifest": manifest, "summary": summary}, indent=2))


if __name__ == "__main__":
    main()
