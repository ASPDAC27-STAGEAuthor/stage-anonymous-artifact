#!/usr/bin/env python3
"""Register the approved MoT mapping sweep in the final paper bundle.

The source sweep predates the Phase 10 bundle.  This script verifies its
approved invariants, copies the immutable raw CSV/JSON into the bundle, writes
a narrow tidy CSV for plotting, and records content hashes for every output.
"""

from __future__ import annotations

import csv
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path


REPO = Path(__file__).resolve().parents[3]
BUNDLE = REPO / "experiments/aspdac/results/final_20260716"
SOURCE_CSV = REPO / "graph/phase8a-256-dc-sweep-static-weights-mot-inr-v4.csv"
SOURCE_JSON = REPO / "graph/phase8a-256-dc-sweep-static-weights-mot-inr-v4.json"
RAW_DIR = BUNDLE / "raw/stage_mot_mapping"
SUMMARY = BUNDLE / "summary/stage_mot_mapping.csv"
MANIFEST = BUNDLE / "manifests/stage_mot_mapping_output_manifest.json"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def repo_path(path: Path) -> str:
    return path.relative_to(REPO).as_posix()


def main() -> None:
    with SOURCE_CSV.open(newline="", encoding="utf-8-sig") as stream:
        source_rows = list(csv.DictReader(stream))

    expected_pairs = {(d, c) for d in (32, 64, 128, 256) for c in (2, 4, 8, 16, 32, 64)}
    observed_pairs = {(int(row["d"]), int(row["c"])) for row in source_rows}
    if len(source_rows) != 24 or observed_pairs != expected_pairs:
        raise RuntimeError("MoT mapping source is not the approved 4x6 D/C matrix")
    if not all(
        row["exact"].lower() == "true"
        and int(row["repeat_count"]) >= 2
        and row["trace_deterministic"].lower() == "true"
        for row in source_rows
    ):
        raise RuntimeError("MoT mapping source failed exact/repeat/determinism checks")

    RAW_DIR.mkdir(parents=True, exist_ok=True)
    raw_csv = RAW_DIR / SOURCE_CSV.name
    raw_json = RAW_DIR / SOURCE_JSON.name
    shutil.copyfile(SOURCE_CSV, raw_csv)
    shutil.copyfile(SOURCE_JSON, raw_json)
    source_hash = sha256(raw_csv)

    fields = [
        "workload_id",
        "M",
        "K",
        "N",
        "partition_width_d",
        "cluster_size_c",
        "mesh_rows",
        "mesh_columns",
        "used_pe_count",
        "cycles",
        "physical_packet_moves_1024b",
        "physical_bit_distance_bit_um",
        "component_stall_cycles",
        "link_congestion_cycles",
        "exact",
        "repeat_count",
        "trace_deterministic",
        "trace_hash",
        "layout_hash",
        "resolved_mapping_hash",
        "raw_csv_sha256",
        "evidence_level",
        "claim_boundary",
    ]
    rows = []
    for source in source_rows:
        rows.append(
            {
                "workload_id": "matmul_m1_k256_n256",
                "M": 1,
                "K": 256,
                "N": 256,
                "partition_width_d": int(source["d"]),
                "cluster_size_c": int(source["c"]),
                "mesh_rows": int(source["mesh_rows"]),
                "mesh_columns": int(source["mesh_columns"]),
                "used_pe_count": int(source["used_pe_count"]),
                "cycles": int(source["cycles"]),
                "physical_packet_moves_1024b": int(source["physical_packet_moves_measured"]),
                "physical_bit_distance_bit_um": int(source["physical_bit_distance_measured"]),
                "component_stall_cycles": int(source["component_stall_cycles"]),
                "link_congestion_cycles": int(source["link_congestion_cycles"]),
                "exact": source["exact"],
                "repeat_count": int(source["repeat_count"]),
                "trace_deterministic": source["trace_deterministic"],
                "trace_hash": source["trace_hash"],
                "layout_hash": source["layout_hash"],
                "resolved_mapping_hash": source["resolved_mapping_hash"],
                "raw_csv_sha256": source_hash,
                "evidence_level": "Measured deterministic full-cycle runtime",
                "claim_boundary": "D and C jointly change the resolved hierarchical mapping and mesh geometry; this is not an isolated topology-only experiment",
            }
        )

    SUMMARY.parent.mkdir(parents=True, exist_ok=True)
    with SUMMARY.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)

    manifest = {
        "schema_version": "aspdac-stage-mot-mapping-output-1.0",
        "generated_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "experiment": "same-workload hierarchical mapping and topology-geometry sweep",
        "workload": {"M": 1, "K": 256, "N": 256},
        "matrix": {"partition_width_d": [32, 64, 128, 256], "cluster_size_c": [2, 4, 8, 16, 32, 64]},
        "case_count": len(rows),
        "repeat_count_per_case": 2,
        "exact_cases": sum(row["exact"].lower() == "true" for row in rows),
        "deterministic_cases": sum(row["trace_deterministic"].lower() == "true" for row in rows),
        "claim_boundary": rows[0]["claim_boundary"],
        "files": [
            {"path": repo_path(raw_csv), "sha256": sha256(raw_csv), "bytes": raw_csv.stat().st_size, "role": "immutable raw CSV"},
            {"path": repo_path(raw_json), "sha256": sha256(raw_json), "bytes": raw_json.stat().st_size, "role": "immutable raw JSON"},
            {"path": repo_path(SUMMARY), "sha256": sha256(SUMMARY), "bytes": SUMMARY.stat().st_size, "role": "tidy paper CSV"},
        ],
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"cases": len(rows), "summary": repo_path(SUMMARY), "manifest": repo_path(MANIFEST)}, indent=2))


if __name__ == "__main__":
    main()
