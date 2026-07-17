#!/usr/bin/env python3
"""Build canonical, materialized-im2col MNIST CNN packet traces.

The trace is a transport-only workload derived from the registered layer
shapes.  It is deliberately not described as a native STAGE numerical CNN
trace: endpoint 0 is the single memory endpoint, endpoints 1..15 are compute
endpoints, input/weight packets are striped from memory to compute, and output
packets return to memory.  Every wire packet is one 128-bit flit.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
from typing import Any, Iterable


REPO_ROOT = Path(__file__).resolve().parents[3]
BUNDLE_DEFAULT = REPO_ROOT / "experiments/aspdac/results/final_20260716"
TRACE_HEADER = (
    "packet_id",
    "release_cycle",
    "source",
    "destination",
    "flits",
    "traffic_class",
    "layer_id",
    "tensor_role",
    "payload_bits",
)
LAYER_ORDER = ("conv1", "conv2", "fc1", "fc2", "fc3")
COMPUTE_ENDPOINTS = tuple(range(1, 16))
ELEMENT_BITS = 32
PACKET_BITS = 128
ELEMENTS_PER_PACKET = PACKET_BITS // ELEMENT_BITS
MAX_MESH_HOPS = 6
VC_DEPTH_FLITS = 16
MIN_DRAIN_CYCLES = 4096
DRAIN_CYCLES_PER_PACKET = 8


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


def load_profiles(path: Path) -> list[dict[str, Any]]:
    with path.open(newline="", encoding="utf-8") as handle:
        source = list(csv.DictReader(handle))
    if tuple(row["layer"] for row in source) != LAYER_ORDER:
        raise RuntimeError(f"expected layer order {LAYER_ORDER}, found {[row['layer'] for row in source]}")
    profiles: list[dict[str, Any]] = []
    for row in source:
        profile = {
            "layer_id": row["layer"],
            "layer_kind": row["kind"],
            "M": int(row["im2col_M"]),
            "N": int(row["im2col_N"]),
            "K": int(row["im2col_K"]),
            "macs_per_image": int(row["macs_per_image"]),
        }
        expected_macs = profile["M"] * profile["N"] * profile["K"]
        if profile["macs_per_image"] != expected_macs:
            raise RuntimeError(f"{profile['layer_id']} MAC count is not M*N*K")
        profiles.append(profile)
    return profiles


def packet_count(elements: int) -> int:
    if elements < 0:
        raise ValueError("element count cannot be negative")
    return math.ceil(elements / ELEMENTS_PER_PACKET)


def build_layer_rows(
    profile: dict[str, Any],
    case_id: str,
    release_offset: int = 0,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    layer_id = str(profile["layer_id"])
    m, n, k = (int(profile[key]) for key in ("M", "N", "K"))
    elements = {"input": m * k, "weight": n * k, "output": m * n}
    counts = {role: packet_count(value) for role, value in elements.items()}
    rows: list[dict[str, Any]] = []
    release = release_offset

    # A single memory endpoint offers at most one ingress packet per cycle.
    for role in ("input", "weight"):
        for index in range(counts[role]):
            rows.append(
                {
                    "packet_id": f"{case_id}-{layer_id}-{role}-{index:06d}",
                    "release_cycle": release,
                    "source": 0,
                    "destination": COMPUTE_ENDPOINTS[index % len(COMPUTE_ENDPOINTS)],
                    "flits": 1,
                    "traffic_class": 0,
                    "layer_id": layer_id,
                    "tensor_role": role,
                    "payload_bits": PACKET_BITS,
                }
            )
            release += 1

    # Leave a registered mesh-flight guard before compute endpoints offer the
    # output packets. This is a fixed trace schedule, not a modeled compute time.
    output_start = release + MAX_MESH_HOPS
    for index in range(counts["output"]):
        rows.append(
            {
                "packet_id": f"{case_id}-{layer_id}-output-{index:06d}",
                "release_cycle": output_start + index // len(COMPUTE_ENDPOINTS),
                "source": COMPUTE_ENDPOINTS[index % len(COMPUTE_ENDPOINTS)],
                "destination": 0,
                "flits": 1,
                "traffic_class": 0,
                "layer_id": layer_id,
                "tensor_role": "output",
                "payload_bits": PACKET_BITS,
            }
        )

    logical_bits = sum(elements.values()) * ELEMENT_BITS
    wire_bits = len(rows) * PACKET_BITS
    last_release = max(int(row["release_cycle"]) for row in rows)
    metadata = {
        "case_id": case_id,
        "layer_id": layer_id,
        "layer_kind": profile["layer_kind"],
        "M": m,
        "N": n,
        "K": k,
        "macs_per_image": int(profile["macs_per_image"]),
        "element_counts": elements,
        "packet_counts": counts,
        "packet_count": len(rows),
        "logical_bits": logical_bits,
        "wire_bits": wire_bits,
        "padding_bits": wire_bits - logical_bits,
        "first_release_cycle": release_offset,
        "last_release_cycle": last_release,
        "drain_cycles": max(MIN_DRAIN_CYCLES, len(rows) * DRAIN_CYCLES_PER_PACKET + 1024),
    }
    validate_rows(rows)
    return rows, metadata


def validate_rows(rows: list[dict[str, Any]]) -> None:
    packet_ids = [str(row["packet_id"]) for row in rows]
    if len(packet_ids) != len(set(packet_ids)):
        raise RuntimeError("trace packet ids are not unique")
    releases: dict[tuple[int, int], int] = {}
    for row in rows:
        source = int(row["source"])
        destination = int(row["destination"])
        cycle = int(row["release_cycle"])
        if not (0 <= source < 16 and 0 <= destination < 16) or source == destination:
            raise RuntimeError(f"invalid endpoint pair in {row['packet_id']}")
        if int(row["flits"]) != 1 or int(row["payload_bits"]) != PACKET_BITS:
            raise RuntimeError(f"non-one-flit packet in {row['packet_id']}")
        if int(row["traffic_class"]) != 0 or cycle < 0:
            raise RuntimeError(f"invalid class or release cycle in {row['packet_id']}")
        key = (cycle, source)
        releases[key] = releases.get(key, 0) + 1
        if releases[key] > 1:
            raise RuntimeError(f"endpoint {source} offers more than one packet at cycle {cycle}")


def write_trace(path: Path, rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=TRACE_HEADER, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def build_traces(bundle: Path) -> dict[str, Any]:
    bundle = bundle.resolve()
    source_dir = bundle / "raw" / "mnist_cnn"
    profile_path = source_dir / "layer_profiles.csv"
    summary_path = source_dir / "summary.json"
    profiles = load_profiles(profile_path)
    functional = json.loads(summary_path.read_text(encoding="utf-8"))
    trace_dir = bundle / "raw" / "mnist_cnn_noc_traces"

    case_entries: list[dict[str, Any]] = []
    layer_rows: dict[str, list[dict[str, Any]]] = {}
    layer_metadata: dict[str, dict[str, Any]] = {}
    for profile in profiles:
        layer_id = str(profile["layer_id"])
        rows, metadata = build_layer_rows(profile, layer_id)
        path = trace_dir / f"{layer_id}.csv"
        write_trace(path, rows)
        metadata.update({"trace_csv_path": repo_path(path), "trace_sha256": sha256_file(path)})
        layer_rows[layer_id] = rows
        layer_metadata[layer_id] = metadata
        case_entries.append(metadata)

    sequential_rows: list[dict[str, Any]] = []
    offset = 0
    sequential_layers: list[dict[str, Any]] = []
    for profile in profiles:
        layer_id = str(profile["layer_id"])
        rows, metadata = build_layer_rows(profile, "sequential_network", offset)
        sequential_rows.extend(rows)
        sequential_layers.append(metadata)
        # A conservative fixed dependency barrier prevents the next layer from
        # overlapping a many-to-one output drain in either simulator.
        offset = (
            int(metadata["last_release_cycle"])
            + 1
            + int(metadata["packet_counts"]["output"])
            + MAX_MESH_HOPS
            + VC_DEPTH_FLITS
        )
    validate_rows(sequential_rows)
    sequential_path = trace_dir / "sequential_network.csv"
    write_trace(sequential_path, sequential_rows)
    logical_bits = sum(int(item["logical_bits"]) for item in sequential_layers)
    wire_bits = len(sequential_rows) * PACKET_BITS
    sequential_entry = {
        "case_id": "sequential_network",
        "layer_id": "all",
        "layer_kind": "sequential_cold_start_network",
        "packet_count": len(sequential_rows),
        "logical_bits": logical_bits,
        "wire_bits": wire_bits,
        "padding_bits": wire_bits - logical_bits,
        "first_release_cycle": 0,
        "last_release_cycle": max(int(row["release_cycle"]) for row in sequential_rows),
        "drain_cycles": max(
            MIN_DRAIN_CYCLES,
            len(sequential_rows) * DRAIN_CYCLES_PER_PACKET + 1024,
        ),
        "layers": sequential_layers,
        "trace_csv_path": repo_path(sequential_path),
        "trace_sha256": sha256_file(sequential_path),
    }
    case_entries.append(sequential_entry)

    manifest_path = bundle / "manifests" / "mnist_cnn_noc_trace_manifest.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest = {
        "schema_version": "aspg-mnist-cnn-noc-trace-manifest-1.0",
        "status": "completed",
        "trace_contract": {
            "topology": "V-BS-CNN 4x4 mesh, XY, endpoint 0 memory, endpoints 1..15 compute",
            "mapping": "materialized-im2col cold-start; one-copy input/weight striping and output gather",
            "packet_bits": PACKET_BITS,
            "flit_bits": PACKET_BITS,
            "flits_per_packet": 1,
            "traffic_class": 0,
            "csv_header": list(TRACE_HEADER),
            "release_rule": "one packet per source per cycle; fixed mesh-flight and inter-layer guards",
        },
        "source": {
            "layer_profiles_path": repo_path(profile_path),
            "layer_profiles_sha256": sha256_file(profile_path),
            "functional_summary_path": repo_path(summary_path),
            "functional_summary_sha256": sha256_file(summary_path),
            "model_sha256": functional["model_sha256"],
            "prediction_sha256": functional["test_repeat_1"]["prediction_sha256"],
            "lowering_sha256": functional["layer_profile_sha256"],
            "dataset_set_sha256": sha256_text(canonical_json(functional["dataset_files"])),
        },
        "cases": case_entries,
        "validation": {
            "case_count": len(case_entries),
            "isolated_layer_packet_count": sum(int(layer_metadata[layer]["packet_count"]) for layer in LAYER_ORDER),
            "sequential_network_packet_count": len(sequential_rows),
            "expected_packet_count": 18_337,
            "all_one_flit_128_bit": True,
            "endpoint_range": [0, 15],
            "same_source_release_collisions": 0,
        },
        "claim_boundary": (
            "CNN-shape-derived transport trace only; BookSim2 does not execute CNN arithmetic or accuracy, "
            "and this is not a native STAGE numerical tensor trace."
        ),
    }
    if manifest["validation"]["isolated_layer_packet_count"] != 18_337 or len(sequential_rows) != 18_337:
        raise RuntimeError("registered single-image trace must contain exactly 18,337 packets")
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, default=BUNDLE_DEFAULT)
    args = parser.parse_args()
    manifest = build_traces(args.bundle)
    print(
        json.dumps(
            {
                "status": manifest["status"],
                "case_count": manifest["validation"]["case_count"],
                "packet_count": manifest["validation"]["sequential_network_packet_count"],
                "manifest": repo_path(args.bundle.resolve() / "manifests" / "mnist_cnn_noc_trace_manifest.json"),
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
