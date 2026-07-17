#!/usr/bin/env python3
"""Build the external trace-visualization and cross-tool alignment bundle.

The builder is deliberately read-only with respect to every source evidence
bundle.  It materializes a new bundle from terminal, manifest-indexed results
and keeps shared-input projections separate from simulator-runtime metrics.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import platform
import statistics
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.collections import LineCollection
from matplotlib.colors import Normalize
from matplotlib.patches import FancyArrowPatch, Rectangle


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[2]
DEFAULT_OUTPUT = SCRIPT_DIR.parent / "results" / "trace_visualization_20260717"

FINAL = Path("experiments/aspdac/results/final_20260716")
REVIEWER = Path("experiments/aspdac/results/reviewer_extension_20260717")
EXTERNAL = Path("experiments/aspdac/results/external_rerun_20260717")
OVERNIGHT = Path("experiments/aspdac/results/overnight_consistency_20260717")

INK = "#1F2A33"
MUTED = "#63707A"
GRID = "#D9E0E5"
BLUE = "#2878A8"
TEAL = "#168B83"
ORANGE = "#D78324"
PURPLE = "#765AA6"
RED = "#B44747"
GREEN = "#3E8A57"
PALE = "#F4F7F8"

TRACE_WINDOW_CYCLES = 512
TEMPORAL_BINS = 64
MESH_DIMENSION = 4
LINK_BITS_PER_PACKET = 128
CAUSAL_CASE = "noc_n06_contend_512"
CAUSAL_REPEAT = "0"


def configure_style() -> None:
    plt.rcParams.update(
        {
            "font.family": "DejaVu Sans",
            "font.size": 7.0,
            "axes.titlesize": 7.8,
            "axes.labelsize": 7.0,
            "xtick.labelsize": 6.2,
            "ytick.labelsize": 6.2,
            "legend.fontsize": 6.1,
            "axes.linewidth": 0.65,
            "lines.linewidth": 1.25,
            "pdf.fonttype": 42,
            "ps.fonttype": 42,
            "savefig.dpi": 400,
        }
    )


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise ValueError(f"Expected JSON object: {path}")
    return value


def write_csv(path: Path, rows: Iterable[dict[str, Any]], fields: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields, lineterminator="\n", extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def fieldnames_for(rows: list[dict[str, Any]]) -> list[str]:
    fields: list[str] = []
    for row in rows:
        for key in row:
            if key not in fields:
                fields.append(key)
    if not fields:
        raise ValueError("Cannot write a header for an empty row collection")
    return fields


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value.rstrip() + "\n", encoding="utf-8", newline="\n")


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_sha256(value: Any) -> str:
    payload = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def as_int(value: Any, default: int = 0) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return default


def as_float(value: Any, default: float = math.nan) -> float:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return default
    return result if math.isfinite(result) else default


def truthy(value: Any) -> bool:
    return str(value).strip().lower() in {"true", "1", "yes", "pass", "passed"}


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return math.nan
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, math.ceil(fraction * len(ordered)) - 1))
    return ordered[index]


def router_xy(endpoint: int) -> tuple[int, int]:
    if endpoint < 0 or endpoint >= MESH_DIMENSION * MESH_DIMENSION:
        raise ValueError(f"Endpoint {endpoint} is outside the frozen 4x4 mesh")
    return endpoint % MESH_DIMENSION, endpoint // MESH_DIMENSION


def router_id(x: int, y: int) -> str:
    return f"router[{x},{y}]"


def directed_link(a: tuple[int, int], b: tuple[int, int]) -> str:
    return f"link[{router_id(*a)}->{router_id(*b)}]"


def xy_route(source: int, destination: int) -> list[str]:
    x, y = router_xy(source)
    target_x, target_y = router_xy(destination)
    links: list[str] = []
    while x != target_x:
        next_x = x + (1 if target_x > x else -1)
        links.append(directed_link((x, y), (next_x, y)))
        x = next_x
    while y != target_y:
        next_y = y + (1 if target_y > y else -1)
        links.append(directed_link((x, y), (x, next_y)))
        y = next_y
    return links


def source_spec(repo: Path) -> list[dict[str, str]]:
    specs = [
        ("final_manifest", FINAL / "manifests/final_manifest_index.json", "Final bundle authority", "Manifest"),
        ("reviewer_manifest", REVIEWER / "manifests/reviewer_extension_manifest_index.json", "Reviewer extension authority", "Manifest"),
        ("external_manifest", EXTERNAL / "manifests/bundle_manifest.json", "Terminal CNN cross-tool rerun authority", "Manifest"),
        ("cnn_trace", FINAL / "raw/mnist_cnn_noc_traces/sequential_network.csv", "Shared finite CNN packet input", "Exact input"),
        ("cnn_config", EXTERNAL / "config/v_bs_cnn.resolved.json", "Frozen 4x4 mesh and packet contract", "Exact configuration"),
        ("vbs_runtime_source", Path("src/HardwareSim.Core/AspdacVbsRuntime.cs"), "Registered stochastic traffic generator and packet runtime", "Source authority"),
        ("rq2_sweep", FINAL / "summary/rq2_booksim_stage_sweep.csv", "Registered STAGE/BookSim2 stochastic NoC results", "Numerical descriptive"),
        ("cnn_candidates", EXTERNAL / "summary/candidate_results.csv", "Terminal STAGE and BookSim2 run index", "Measured native metrics"),
        ("cnn_cross_tool", EXTERNAL / "summary/mnist_cnn_noc_cross_tool.csv", "CNN matched-input comparison", "Numerical descriptive"),
        ("noc_stage_timeline", REVIEWER / "summary/noc_cycle_timeline.csv", "STAGE per-cycle NoC events", "Exact STAGE trace"),
        ("noc_oracle_timeline", REVIEWER / "summary/noc_oracle_timeline.csv", "Independent NoC oracle events", "Independent oracle"),
        ("noc_microbench", REVIEWER / "summary/noc_contract_microbench.csv", "NoC case/repeat acceptance", "Exact where supported"),
        ("interventions", REVIEWER / "summary/trace_guided_interventions.csv", "One-axis bottleneck interventions", "Within-model deterministic"),
        ("holdout_timing", REVIEWER / "summary/holdout_scalesim_stage_timing.csv", "Independent STAGE/SCALE-Sim timing", "Trend"),
        ("holdout_status", REVIEWER / "summary/holdout_pair_status.csv", "Hold-out pair gate", "Exact input/provenance"),
        ("holdout_audit", REVIEWER / "manifests/holdout_independence_audit.md", "Hold-out independence audit", "Audit"),
        ("runtime_summary", OVERNIGHT / "summary/simulator_runtime_summary.csv", "Simulator wall-clock context", "Measured wall clock"),
        ("stage_scalability", REVIEWER / "summary/stage_scalability.csv", "Registered 10k to 1M packet scalability records", "Measured wall clock and storage"),
        ("claim_register_template", Path("experiments/aspdac/specs/reporting/experiment_claim_register_20260717.md"), "Consolidated claim definitions and reproduction recipes", "Reporting contract"),
    ]
    rows: list[dict[str, str]] = []
    for source_id, relative_path, role, evidence in specs:
        path = repo / relative_path
        if not path.is_file():
            raise FileNotFoundError(path)
        rows.append(
            {
                "source_id": source_id,
                "path": relative_path.as_posix(),
                "role": role,
                "evidence_level": evidence,
                "bytes": str(path.stat().st_size),
                "sha256": sha256_path(path),
            }
        )
    return rows


def build_entity_map(
    packets: list[dict[str, Any]] | None = None,
    *,
    config_path: str = "",
    config_sha256: str = "",
    trace_path: str = "",
    trace_sha256: str = "",
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []

    def add(
        entity_type: str,
        canonical_id: str,
        native_id: str,
        x: int | str,
        y: int | str,
        source_entity: str,
        destination_entity: str,
        mapping_rule: str,
        source_path: str,
        source_hash: str,
        status: str = "mapped",
    ) -> None:
        identity = {
            "entity_type": entity_type,
            "canonical_id": canonical_id,
            "native_id": native_id,
            "source_entity": source_entity,
            "destination_entity": destination_entity,
            "mapping_rule": mapping_rule,
            "source_path": source_path,
            "source_sha256": source_hash,
            "status": status,
        }
        rows.append({**identity, "x": x, "y": y, "mapping_sha256": canonical_sha256(identity)})

    for endpoint in range(MESH_DIMENSION * MESH_DIMENSION):
        x, y = router_xy(endpoint)
        add("endpoint", f"endpoint[{endpoint}]", str(endpoint), x, y, "", "", "endpoint n is attached one-to-one to router[n]", config_path, config_sha256)
        add("component", f"component[endpoint-{endpoint:02d}]", f"endpoint-{endpoint:02d}", x, y, "", "", "stable STAGE endpoint component ID mapped to canonical endpoint n", config_path, config_sha256)
        add("router_coordinate", router_id(x, y), str(endpoint), x, y, "", "", "row-major router index; x=n mod 4, y=floor(n/4)", config_path, config_sha256)
        add("router_index", f"router[{endpoint}]", str(endpoint), x, y, "", router_id(x, y), "index alias for the same row-major router", config_path, config_sha256)
        for port in ("east", "local", "north", "south", "west"):
            add("router_input_vc", f"router[{endpoint}].input[{port}/vc0]", f"router-{endpoint:02d}:{port}:vc0", x, y, f"endpoint[{endpoint}]" if port == "local" else "", f"router[{endpoint}]", "one frozen VC per physical input port", config_path, config_sha256)
            add("router_output_vc", f"router[{endpoint}].output[{port}/vc0]", f"router-{endpoint:02d}:{port}:vc0", x, y, f"router[{endpoint}]", f"endpoint[{endpoint}]" if port == "local" else "", "one frozen VC per physical output port", config_path, config_sha256)
    for y in range(MESH_DIMENSION):
        for x in range(MESH_DIMENSION):
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < MESH_DIMENSION and 0 <= ny < MESH_DIMENSION:
                    source = (x, y)
                    destination = (nx, ny)
                    add("directed_link", directed_link(source, destination), "", x, y, router_id(*source), router_id(*destination), "frozen directed XY mesh edge", config_path, config_sha256)
    for packet in packets or []:
        packet_id = str(packet["packet_id"])
        add("packet", f"packet[{packet_id}]", packet_id, "", "", str(packet["source_endpoint"]), str(packet["destination_endpoint"]), "canonical ID is the authenticated shared CSV packet_id", trace_path, trace_sha256)
        add("flit", f"flit[{packet_id}:0]", f"{packet_id}-f0", "", "", f"packet[{packet_id}]", "", "the frozen 128-bit packet is exactly one 128-bit flit", trace_path, trace_sha256)
    return sorted(rows, key=lambda row: (row["entity_type"], row["canonical_id"]))


def find_cnn_run(candidates: list[dict[str, str]], provider: str, repeat: str) -> dict[str, str]:
    matches = [
        row
        for row in candidates
        if row.get("provider") == provider
        and row.get("case_id") == "sequential_network"
        and row.get("repeat") == repeat
        and row.get("status") == "completed"
    ]
    if len(matches) != 1:
        raise ValueError(f"Expected one terminal {provider} sequential_network repeat {repeat}; found {len(matches)}")
    return matches[0]


def build_aligned_packets(repo: Path, candidates: list[dict[str, str]]) -> tuple[list[dict[str, Any]], Path]:
    trace_path = repo / FINAL / "raw/mnist_cnn_noc_traces/sequential_network.csv"
    trace_rows = read_csv(trace_path)
    booksim = find_cnn_run(candidates, "booksim2_trace", "0")
    booksim_record = read_json(repo / Path(booksim["raw_path"]))
    packet_delivery_rel = booksim_record.get("raw_evidence", {}).get("packet_delivery")
    if not packet_delivery_rel:
        raise ValueError("Terminal BookSim2 record has no packet_delivery artifact")
    packet_delivery_path = repo / Path(packet_delivery_rel)
    delivery_sha256 = sha256_path(packet_delivery_path)
    deliveries = {row["packet_id"]: row for row in read_csv(packet_delivery_path)}
    if len(deliveries) != len(trace_rows):
        raise ValueError(f"BookSim2 delivered {len(deliveries)} of {len(trace_rows)} shared packets")

    trace_hash = sha256_path(trace_path)
    if trace_hash != booksim.get("trace_sha256"):
        raise ValueError("BookSim2 terminal run trace hash differs from shared input")
    rows: list[dict[str, Any]] = []
    for packet in trace_rows:
        packet_id = packet["packet_id"]
        delivery = deliveries.get(packet_id)
        if delivery is None:
            raise ValueError(f"Missing BookSim2 packet delivery: {packet_id}")
        source = as_int(packet["source"])
        destination = as_int(packet["destination"])
        route = xy_route(source, destination)
        sx, sy = router_xy(source)
        dx, dy = router_xy(destination)
        rows.append(
            {
                "packet_id": packet_id,
                "case_id": "sequential_network",
                "layer_id": packet["layer_id"],
                "tensor_role": packet["tensor_role"],
                "release_cycle": as_int(packet["release_cycle"]),
                "source_endpoint": f"endpoint[{source}]",
                "destination_endpoint": f"endpoint[{destination}]",
                "payload_bits": as_int(packet["payload_bits"]),
                "wire_bits": as_int(packet["flits"]) * LINK_BITS_PER_PACKET,
                "x_hops": abs(dx - sx),
                "y_hops": abs(dy - sy),
                "route_hops": len(route),
                "xy_route": ";".join(route),
                "booksim_injection_cycle": as_int(delivery["injection_cycle"]),
                "booksim_arrival_cycle": as_int(delivery["arrival_cycle"]),
                "booksim_packet_latency": as_int(delivery["packet_latency"]),
                "booksim_candidate_id": booksim["candidate_id"],
                "stage_per_packet_delivery": "not_available_in_terminal_bundle",
                "shared_input_trace_sha256": trace_hash,
                "source_path": trace_path.relative_to(repo).as_posix(),
                "source_sha256": trace_hash,
                "runtime_source_path": packet_delivery_path.relative_to(repo).as_posix(),
                "runtime_source_sha256": delivery_sha256,
                "source_row_key": packet_id,
                "metric_definition": "release/source/destination/payload are Exact input; BookSim2 arrival/latency use native cycles",
                "evidence_level": "Exact input; BookSim2 packet runtime is Numerical descriptive",
            }
        )
    return rows, packet_delivery_path


def build_spatial_tables(packets: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    link_windows: dict[tuple[int, str], dict[str, Any]] = {}
    component_windows: dict[tuple[int, str], dict[str, Any]] = {}
    for packet in packets:
        window = as_int(packet["release_cycle"]) // TRACE_WINDOW_CYCLES
        bits = as_int(packet["wire_bits"])
        layer = str(packet["layer_id"])
        route = [item for item in str(packet["xy_route"]).split(";") if item]
        for link in route:
            key = (window, link)
            if key not in link_windows:
                link_windows[key] = {
                    "domain": "shared_cnn_input",
                    "window_index": window,
                    "window_start_cycle": window * TRACE_WINDOW_CYCLES,
                    "window_end_cycle_exclusive": (window + 1) * TRACE_WINDOW_CYCLES,
                    "canonical_link_id": link,
                    "packet_count": 0,
                    "wire_bits": 0,
                    "layer_counts": Counter(),
                    "tool": "Shared STAGE/BookSim2 input",
                    "traffic": "mnist_cnn_materialized_im2col",
                    "injection_rate": "finite_trace",
                    "seed": 40,
                    "queue_occupancy_mean": "",
                    "queue_occupancy_p95": "",
                    "queue_occupancy_max": "",
                    "congestion_cycle_ratio": "",
                    "stall_count": "",
                    "backpressure_count": "",
                    "dominant_stall_reason": "",
                    "source_path": packet["source_path"],
                    "source_sha256": packet["source_sha256"],
                    "source_selector": f"{window * TRACE_WINDOW_CYCLES} <= release_cycle < {(window + 1) * TRACE_WINDOW_CYCLES}",
                    "metric_semantics": "Exact shared-input bits projected over frozen XY routes; not observed queue occupancy",
                    "evidence_level": "Exact input projection",
                }
            entry = link_windows[key]
            entry["packet_count"] += 1
            entry["wire_bits"] += bits
            entry["layer_counts"][layer] += 1
        for direction, endpoint in (("sent", packet["source_endpoint"]), ("received", packet["destination_endpoint"])):
            key = (window, str(endpoint))
            if key not in component_windows:
                component_windows[key] = {
                    "domain": "shared_cnn_input",
                    "window_index": window,
                    "window_start_cycle": window * TRACE_WINDOW_CYCLES,
                    "window_end_cycle_exclusive": (window + 1) * TRACE_WINDOW_CYCLES,
                    "canonical_component_id": endpoint,
                    "sent_packets": 0,
                    "received_packets": 0,
                    "sent_wire_bits": 0,
                    "received_wire_bits": 0,
                    "tool": "Shared STAGE/BookSim2 input",
                    "traffic": "mnist_cnn_materialized_im2col",
                    "injection_rate": "finite_trace",
                    "seed": 40,
                    "queue_occupancy_mean": "",
                    "queue_occupancy_p95": "",
                    "queue_occupancy_max": "",
                    "stall_count": "",
                    "backpressure_count": "",
                    "source_path": packet["source_path"],
                    "source_sha256": packet["source_sha256"],
                    "source_selector": f"{window * TRACE_WINDOW_CYCLES} <= release_cycle < {(window + 1) * TRACE_WINDOW_CYCLES}",
                    "metric_semantics": "Exact endpoint demand from the shared packet input",
                    "evidence_level": "Exact input",
                }
            entry = component_windows[key]
            entry[f"{direction}_packets"] += 1
            entry[f"{direction}_wire_bits"] += bits

    link_rows: list[dict[str, Any]] = []
    for entry in link_windows.values():
        row = dict(entry)
        row["layer_counts"] = ";".join(f"{key}:{value}" for key, value in sorted(entry["layer_counts"].items()))
        link_rows.append(row)
    return (
        sorted(link_rows, key=lambda row: (row["window_index"], row["canonical_link_id"])),
        sorted(component_windows.values(), key=lambda row: (row["window_index"], row["canonical_component_id"])),
    )


def build_timeline_bins(packets: list[dict[str, Any]], stage_timeline: list[dict[str, str]], stage_source_path: str = "", stage_source_sha256: str = "") -> list[dict[str, Any]]:
    max_cycle = max(max(as_int(row["release_cycle"]), as_int(row["booksim_arrival_cycle"])) for row in packets)
    width = max(1, math.ceil((max_cycle + 1) / TEMPORAL_BINS))
    releases: dict[int, list[dict[str, Any]]] = defaultdict(list)
    deliveries: dict[int, list[dict[str, Any]]] = defaultdict(list)
    for packet in packets:
        releases[as_int(packet["release_cycle"]) // width].append(packet)
        deliveries[as_int(packet["booksim_arrival_cycle"]) // width].append(packet)
    rows: list[dict[str, Any]] = []
    cumulative_release = 0
    cumulative_delivery = 0
    for bin_index in range(TEMPORAL_BINS):
        released = releases.get(bin_index, [])
        delivered = deliveries.get(bin_index, [])
        cumulative_release += len(released)
        cumulative_delivery += len(delivered)
        latencies = [as_float(row["booksim_packet_latency"]) for row in delivered]
        layers = Counter(str(row["layer_id"]) for row in released)
        rows.append(
            {
                "domain": "cnn_matched_input_booksim_runtime",
                "resolution_label": "auto_64_bins",
                "resolution_cycles": width,
                "bin_index": bin_index,
                "cycle_start": bin_index * width,
                "cycle_end_exclusive": min(max_cycle + 1, (bin_index + 1) * width),
                "released_packets": len(released),
                "delivered_packets": len(delivered),
                "outstanding_packets_end": cumulative_release - cumulative_delivery,
                "mean_delivery_latency": statistics.fmean(latencies) if latencies else "",
                "p95_delivery_latency": percentile(latencies, 0.95) if latencies else "",
                "stall_events": "",
                "buffer_occupancy_events": "",
                "layer_release_counts": ";".join(f"{key}:{value}" for key, value in sorted(layers.items())),
                "source_path": f"{packets[0]['source_path']};{packets[0]['runtime_source_path']}",
                "source_sha256": f"{packets[0]['source_sha256']};{packets[0]['runtime_source_sha256']}",
                "source_selector": f"release/arrival cycle bin {bin_index} at width {width}",
                "metric_semantics": "Release is Exact shared input; delivery and latency are BookSim2 native packet runtime",
                "evidence_level": "Exact input; Numerical descriptive runtime",
            }
        )

    for resolution in (10, 100, 1000):
        resolution_releases: dict[int, list[dict[str, Any]]] = defaultdict(list)
        resolution_deliveries: dict[int, list[dict[str, Any]]] = defaultdict(list)
        for packet in packets:
            resolution_releases[as_int(packet["release_cycle"]) // resolution].append(packet)
            resolution_deliveries[as_int(packet["booksim_arrival_cycle"]) // resolution].append(packet)
        cumulative_release = 0
        cumulative_delivery = 0
        bin_count = math.ceil((max_cycle + 1) / resolution)
        for bin_index in range(bin_count):
            released = resolution_releases.get(bin_index, [])
            delivered = resolution_deliveries.get(bin_index, [])
            cumulative_release += len(released)
            cumulative_delivery += len(delivered)
            latencies = [as_float(row["booksim_packet_latency"]) for row in delivered]
            layers = Counter(str(row["layer_id"]) for row in released)
            rows.append(
                {
                    "domain": "cnn_matched_input_booksim_runtime",
                    "resolution_label": f"fixed_{resolution}_cycles",
                    "resolution_cycles": resolution,
                    "bin_index": bin_index,
                    "cycle_start": bin_index * resolution,
                    "cycle_end_exclusive": min(max_cycle + 1, (bin_index + 1) * resolution),
                    "released_packets": len(released),
                    "delivered_packets": len(delivered),
                    "outstanding_packets_end": cumulative_release - cumulative_delivery,
                    "mean_delivery_latency": statistics.fmean(latencies) if latencies else "",
                    "p95_delivery_latency": percentile(latencies, 0.95) if latencies else "",
                    "stall_events": "",
                    "buffer_occupancy_events": "",
                    "layer_release_counts": ";".join(f"{key}:{value}" for key, value in sorted(layers.items())),
                    "source_path": f"{packets[0]['source_path']};{packets[0]['runtime_source_path']}",
                    "source_sha256": f"{packets[0]['source_sha256']};{packets[0]['runtime_source_sha256']}",
                    "source_selector": f"release/arrival cycle bin {bin_index} at width {resolution}",
                    "metric_semantics": "Release is Exact shared input; delivery and latency are BookSim2 native packet runtime",
                    "evidence_level": "Exact input; Numerical descriptive runtime",
                }
            )

    causal = [
        row
        for row in stage_timeline
        if row.get("case_id") == CAUSAL_CASE and row.get("repeat") == CAUSAL_REPEAT
    ]
    by_cycle: dict[int, list[dict[str, str]]] = defaultdict(list)
    for row in causal:
        by_cycle[as_int(row.get("Cycle"))].append(row)
    max_causal_cycle = max(by_cycle, default=-1)
    for cycle in range(max_causal_cycle + 1):
        events = by_cycle.get(cycle, [])
        rows.append(
            {
                "domain": "stage_exact_noc_microbench",
                "resolution_label": "exact_cycle",
                "resolution_cycles": 1,
                "bin_index": cycle,
                "cycle_start": cycle,
                "cycle_end_exclusive": cycle + 1,
                "released_packets": sum(row.get("event_type") == "PacketInjection" for row in events),
                "delivered_packets": sum(truthy(row.get("Delivered")) for row in events),
                "outstanding_packets_end": "",
                "mean_delivery_latency": "",
                "p95_delivery_latency": "",
                "stall_events": sum(row.get("event_type") == "Stall" for row in events),
                "buffer_occupancy_events": sum(row.get("event_type") == "BufferOccupancy" for row in events),
                "layer_release_counts": "",
                "source_path": stage_source_path,
                "source_sha256": stage_source_sha256,
                "source_selector": f"case_id={CAUSAL_CASE};repeat={CAUSAL_REPEAT};Cycle={cycle}",
                "metric_semantics": f"Actual STAGE events for supported exact case {CAUSAL_CASE}",
                "evidence_level": "Exact STAGE trace",
            }
        )
    return rows


def extract_reason(reason: str) -> str:
    for token in reason.split(";"):
        if token.startswith("stall_reason="):
            return token.split("=", 1)[1]
    return ""


def build_stall_paths(stage_timeline: list[dict[str, str]], source_path: str = "", source_sha256: str = "") -> list[dict[str, Any]]:
    selected = [
        row
        for row in stage_timeline
        if row.get("case_id") == CAUSAL_CASE and row.get("repeat") == CAUSAL_REPEAT
    ]
    rows: list[dict[str, Any]] = []
    for row in sorted(selected, key=lambda item: (as_int(item.get("Sequence")), as_int(item.get("Cycle")))):
        rows.append(
            {
                "case_id": CAUSAL_CASE,
                "repeat": CAUSAL_REPEAT,
                "sequence": as_int(row.get("Sequence")),
                "cycle": as_int(row.get("Cycle")),
                "phase": as_int(row.get("Phase")),
                "packet_id": row.get("packet_id", ""),
                "event_type": row.get("event_type", ""),
                "component_id": row.get("component_id", ""),
                "link_id": row.get("link_id", ""),
                "occupancy_before": row.get("occupancy_before", ""),
                "occupancy_after": row.get("occupancy_after", ""),
                "granted": row.get("Granted", ""),
                "delivered": row.get("Delivered", ""),
                "stall_reason": extract_reason(row.get("Reason", "")),
                "reason_detail": row.get("Reason", ""),
                "source_path": source_path,
                "source_sha256": source_sha256,
                "source_row_key": f"{CAUSAL_CASE}/r{CAUSAL_REPEAT}/sequence={row.get('Sequence', '')}",
                "metric_definition": "typed committed STAGE event; stall_reason is parsed only from the recorded Reason field",
                "evidence_level": "Exact STAGE trace",
            }
        )
    if not any(row["stall_reason"] == "RouterConflict" for row in rows):
        raise ValueError("Selected causal trace does not contain the registered RouterConflict")
    if not any(row["stall_reason"] == "LinkBusy" for row in rows):
        raise ValueError("Selected causal trace does not contain the registered LinkBusy sequence")
    return rows


def build_aligned_runs(
    candidates: list[dict[str, str]],
    holdout: list[dict[str, str]],
    noc: list[dict[str, str]],
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for row in candidates:
        if row.get("case_id") != "sequential_network" or row.get("provider") not in {"stage", "booksim2_trace"}:
            continue
        provider = "STAGE" if row["provider"] == "stage" else "BookSim2"
        rows.append(
            {
                "alignment_family": "CNN finite packet trace",
                "case_id": row["case_id"],
                "tool": provider,
                "repeat": row["repeat"],
                "seed": row["seed"],
                "status": row["status"],
                "shared_input_hash": row["trace_sha256"],
                "config_hash": row["config_hash"],
                "native_total_cycles": row["network_makespan_cycles"],
                "native_mean_latency": row["packet_latency_avg"],
                "native_p95_latency": row["packet_latency_p95"],
                "native_utilization_percent": "",
                "runtime_wall_seconds": "",
                "evidence_level": "Exact input; Numerical descriptive runtime",
                "comparison_boundary": "Same finite packet input; native cycle models are not cycle-equivalent",
                "raw_path": row["raw_path"],
            }
        )
    for row in holdout:
        if row.get("status") != "completed":
            continue
        rows.append(
            {
                "alignment_family": "4x4 WS hold-out",
                "case_id": row["case_id"],
                "tool": "STAGE" if row["tool"].lower() == "stage" else "SCALE-Sim",
                "repeat": row["repeat"],
                "seed": row["seed"],
                "status": row["status"],
                "shared_input_hash": "shape_and_frozen_mapping_contract",
                "config_hash": row["config_hash"],
                "native_total_cycles": row["total_cycles"],
                "native_mean_latency": "",
                "native_p95_latency": "",
                "native_utilization_percent": row["utilization_percent"],
                "runtime_wall_seconds": "",
                "evidence_level": "Trend",
                "comparison_boundary": "Independent schedulers; cross-tool timing is Trend evidence",
                "raw_path": row["raw_path"],
            }
        )
    for row in noc:
        if row.get("checkpoint_status") != "completed":
            continue
        rows.append(
            {
                "alignment_family": "NoC independent oracle",
                "case_id": row["case_id"],
                "tool": "STAGE",
                "repeat": row["repeat"],
                "seed": "",
                "status": row["runtime_status"],
                "shared_input_hash": row["oracle_timeline_sha256"],
                "config_hash": "",
                "native_total_cycles": "",
                "native_mean_latency": "",
                "native_p95_latency": "",
                "native_utilization_percent": "",
                "runtime_wall_seconds": "",
                "evidence_level": "Exact" if truthy(row.get("oracle_matched")) else "Not supported",
                "comparison_boundary": "Exact only for supported registered oracle cases",
                "raw_path": row["raw_path"],
            }
        )
    return sorted(rows, key=lambda row: (row["alignment_family"], row["case_id"], row["tool"], str(row["repeat"])))


def build_cross_tool_alignment(
    cnn: list[dict[str, str]],
    holdout: list[dict[str, str]],
    noc: list[dict[str, str]],
    interventions: list[dict[str, str]],
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for row in cnn:
        rows.append(
            {
                "comparison_family": "CNN transport",
                "case_id": row["case_id"],
                "left_tool": "BookSim2",
                "right_tool": "STAGE",
                "shared_condition": f"packet_trace_sha256={row['trace_sha256']}",
                "left_metric": "network_makespan_cycles",
                "left_value": row["booksim_network_makespan_cycles"],
                "right_metric": "network_makespan_cycles",
                "right_value": row["stage_network_makespan_cycles"],
                "unit": "native cycles",
                "relation": "same-input descriptive; compare rank and workload response, not absolute equality",
                "evidence_level": row["runtime_evidence_level"],
                "claim_boundary": row["claim_boundary"],
            }
        )
    grouped: dict[tuple[str, str], dict[str, dict[str, str]]] = defaultdict(dict)
    for row in holdout:
        if row.get("status") == "completed":
            grouped[(row["case_id"], row["repeat"])][row["tool"].lower()] = row
    for (case_id, repeat), tools in sorted(grouped.items()):
        if {"stage", "scalesim"} <= tools.keys():
            rows.append(
                {
                    "comparison_family": "4x4 WS hold-out timing",
                    "case_id": f"{case_id}/r{repeat}",
                    "left_tool": "SCALE-Sim",
                    "right_tool": "STAGE",
                    "shared_condition": "M,N,K, precision, 4x4 WS architecture and public mapping contract",
                    "left_metric": "total_cycles",
                    "left_value": tools["scalesim"]["total_cycles"],
                    "right_metric": "total_cycles",
                    "right_value": tools["stage"]["total_cycles"],
                    "unit": "native cycles",
                    "relation": "independent-scheduler trend",
                    "evidence_level": "Trend",
                    "claim_boundary": "No cycle-exact equivalence; arbitration and prefetch definitions differ",
                }
            )
    for row in noc:
        rows.append(
            {
                "comparison_family": "NoC independent oracle",
                "case_id": f"{row['case_id']}/r{row['repeat']}",
                "left_tool": "Independent oracle",
                "right_tool": "STAGE",
                "shared_condition": f"oracle_timeline_sha256={row.get('oracle_timeline_sha256', '')}",
                "left_metric": "event timeline",
                "left_value": row.get("oracle_timeline_sha256", ""),
                "right_metric": "canonical trace",
                "right_value": row.get("stage_trace_sha256", ""),
                "unit": "canonical bytes/hash",
                "relation": "exact supported contract" if truthy(row.get("oracle_matched")) else "unsupported contract",
                "evidence_level": "Exact" if truthy(row.get("oracle_matched")) else "Not supported",
                "claim_boundary": row.get("stage_support_reason", ""),
            }
        )
    for row in interventions:
        rows.append(
            {
                "comparison_family": "Trace-guided intervention",
                "case_id": row["pair_id"],
                "left_tool": "STAGE baseline",
                "right_tool": "STAGE intervention",
                "shared_condition": f"invariant_pair_hash={row['invariant_pair_hash']}",
                "left_metric": "total_cycles",
                "left_value": row["before_total_cycles"],
                "right_metric": "total_cycles",
                "right_value": row["after_total_cycles"],
                "unit": "STAGE cycles",
                "relation": f"one-axis {row['axis_parameter']} intervention",
                "evidence_level": "Deterministic within-model",
                "claim_boundary": row["evidence_boundary"],
            }
        )
    return rows


class SplitMix64:
    """Byte-independent replay of the registered unsigned 64-bit generator."""

    MASK = (1 << 64) - 1

    def __init__(self, state: int) -> None:
        self.state = state & self.MASK

    def next_uint64(self) -> int:
        self.state = (self.state + 0x9E3779B97F4A7C15) & self.MASK
        value = self.state
        value = ((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9) & self.MASK
        value = ((value ^ (value >> 27)) * 0x94D049BB133111EB) & self.MASK
        return (value ^ (value >> 31)) & self.MASK

    def next_double(self) -> float:
        return (self.next_uint64() >> 11) * (1.0 / (1 << 53))


def build_hotspot_projection(
    repo: Path,
    rq2_rows: list[dict[str, str]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, Any]]:
    matches = [
        row
        for row in rq2_rows
        if row.get("tool") == "STAGE"
        and row.get("traffic") == "hotspot_node5"
        and math.isclose(as_float(row.get("injection_rate")), 0.06)
        and row.get("seed") == "0"
        and row.get("status") == "completed"
        and not truthy(row.get("timeout"))
        and not truthy(row.get("unstable"))
    ]
    if len(matches) != 1:
        raise ValueError(f"Expected one stable registered STAGE hotspot-node-5 point; found {len(matches)}")
    registered = matches[0]
    raw_relative = Path(registered["raw_path"])
    raw_path = repo / raw_relative
    raw_hash = sha256_path(raw_path)
    if raw_hash != registered["raw_sha256"]:
        raise ValueError("Registered STAGE hotspot raw hash does not match the raw artifact")
    record = read_json(raw_path)
    parameters = record["resolved"]["parameters"]
    metrics = record["metrics"]
    warmup = as_int(parameters["warmup_cycles"])
    measurement = as_int(parameters["measurement_cycles"])
    generation_cycles = warmup + measurement
    rate = as_float(parameters["injection_rate"])
    seed = as_int(parameters["seed"])
    if parameters.get("traffic") != "hotspot_node5":
        raise ValueError("Selected spatial case is not the registered hotspot-node-5 traffic")
    rng = SplitMix64(((seed & 0xFFFFFFFF) + 0x9E3779B97F4A7C15) & SplitMix64.MASK)
    link_counts: Counter[str] = Counter()
    endpoint_sent: Counter[int] = Counter()
    endpoint_received: Counter[int] = Counter()
    offered = 0
    for _cycle in range(generation_cycles):
        for source in range(16):
            if rng.next_double() >= rate:
                continue
            destination = 5
            offered += 1
            endpoint_sent[source] += 1
            endpoint_received[destination] += 1
            for link in xy_route(source, destination):
                link_counts[link] += 1
    if offered != as_int(metrics["offered_packets"]):
        raise ValueError(f"Deterministic hotspot replay offered {offered}, raw runtime recorded {metrics['offered_packets']}")

    runtime_source = Path("src/HardwareSim.Core/AspdacVbsRuntime.cs")
    runtime_source_hash = sha256_path(repo / runtime_source)
    common = {
        "domain": "stage_hotspot_node5_input",
        "window_index": 0,
        "window_start_cycle": 0,
        "window_end_cycle_exclusive": generation_cycles,
        "tool": "STAGE",
        "traffic": "hotspot_node5",
        "injection_rate": rate,
        "seed": seed,
        "queue_occupancy_mean": metrics["queue_occupancy_avg_flits"],
        "queue_occupancy_p95": "not_captured",
        "queue_occupancy_max": metrics["queue_occupancy_max_flits"],
        "congestion_cycle_ratio": as_float(metrics["congestion_cycles"]) / as_float(metrics["total_cycles"]),
        "stall_count": as_int(metrics["router_conflict_stalls"]) + as_int(metrics["injection_queue_stalls"]),
        "backpressure_count": metrics["backpressure_cycles"],
        "dominant_stall_reason": "router_conflict",
        "source_path": raw_relative.as_posix(),
        "source_sha256": raw_hash,
        "source_selector": f"candidate_id={registered['candidate_id']};traffic=hotspot_node5;rate=0.06;seed=0",
        "metric_semantics": "Packet/link counts are a deterministic replay of the registered offer generator; queue and stall fields are aggregate STAGE runtime metrics from the same terminal case.",
        "evidence_level": "Exact input projection validated by offered_packets; Numerical STAGE runtime aggregates",
    }
    link_rows = [
        {
            **common,
            "canonical_link_id": link,
            "packet_count": count,
            "wire_bits": count * LINK_BITS_PER_PACKET,
            "layer_counts": "",
        }
        for link, count in sorted(link_counts.items())
    ]
    component_rows = [
        {
            **common,
            "canonical_component_id": f"endpoint[{endpoint}]",
            "sent_packets": endpoint_sent[endpoint],
            "received_packets": endpoint_received[endpoint],
            "sent_wire_bits": endpoint_sent[endpoint] * LINK_BITS_PER_PACKET,
            "received_wire_bits": endpoint_received[endpoint] * LINK_BITS_PER_PACKET,
        }
        for endpoint in range(16)
    ]
    summary = {
        "candidate_id": registered["candidate_id"],
        "raw_path": raw_relative.as_posix(),
        "raw_sha256": raw_hash,
        "runtime_source_path": runtime_source.as_posix(),
        "runtime_source_sha256": runtime_source_hash,
        "traffic": "hotspot_node5",
        "injection_rate": rate,
        "seed": seed,
        "generation_cycles": generation_cycles,
        "projected_offered_packets": offered,
        "recorded_offered_packets": as_int(metrics["offered_packets"]),
        "delivered_packets": as_int(metrics["delivered_packets"]),
        "timeout": bool(metrics["timeout"]),
        "unstable": bool(metrics["unstable"]),
        "packet_latency_avg": metrics["packet_latency_avg"],
        "packet_latency_p95": metrics["packet_latency_p95"],
        "queue_occupancy_avg_flits": metrics["queue_occupancy_avg_flits"],
        "queue_occupancy_max_flits": metrics["queue_occupancy_max_flits"],
        "congestion_cycles": metrics["congestion_cycles"],
        "router_conflict_stalls": metrics["router_conflict_stalls"],
        "backpressure_cycles": metrics["backpressure_cycles"],
        "injection_queue_stalls": metrics["injection_queue_stalls"],
        "projection_validation": "exact offered-packet count match",
        "evidence_level": "Exact input projection; Numerical STAGE runtime aggregates",
    }
    return link_rows, component_rows, summary


def build_large_trace_discipline(
    scalability_rows: list[dict[str, str]],
    output: Path,
) -> list[dict[str, Any]]:
    selected = [
        row
        for row in scalability_rows
        if row.get("status") == "completed"
        and row.get("mesh_dimension") == "32"
        and row.get("packet_count") == "1000000"
        and row.get("trace_mode") in {"full", "metrics_only"}
    ]
    rows: list[dict[str, Any]] = []
    current_output_bytes = sum(path.stat().st_size for path in output.rglob("*") if path.is_file() and path.relative_to(output).parts[0] != "manifests" and path.name != "large_trace_discipline.csv")
    source_path = (REVIEWER / "summary/stage_scalability.csv").as_posix()
    for trace_mode in ("metrics_only", "full"):
        mode_rows = [row for row in selected if row["trace_mode"] == trace_mode]
        if not mode_rows:
            continue
        def median(field: str) -> float:
            return statistics.median(as_float(row[field]) for row in mode_rows)
        rows.append(
            {
                "mesh": "32x32",
                "packet_count": 1000000,
                "trace_mode": trace_mode,
                "terminal_run_count": len(mode_rows),
                "simulation_core_seconds_median": median("simulation_core_seconds"),
                "process_wall_seconds_median": median("process_wall_seconds"),
                "peak_working_set_bytes_median": median("peak_working_set_bytes"),
                "raw_trace_bytes_median": median("raw_trace_bytes"),
                "compressed_trace_bytes_median": median("compressed_trace_bytes"),
                "external_analysis_output_bytes_before_manifest": current_output_bytes,
                "raw_trace_copied_into_visualization_bundle": False,
                "analysis_input_policy": "manifested summary/index only; the 3.92-GiB-class raw trace is not copied",
                "source_path": source_path,
                "source_sha256": sha256_path(REPO_ROOT / REVIEWER / "summary/stage_scalability.csv"),
                "evidence_level": "Measured host runtime/storage context",
            }
        )
    return rows


def metric_definitions() -> list[dict[str, str]]:
    return [
        {
            "metric": "shared input route load",
            "source": "CNN packet CSV",
            "unit": "packets or wire bits per directed XY edge",
            "definition": "Each packet is projected over the frozen dimension-order XY path.",
            "evidence_level": "Exact input projection",
            "allowed_use": "Visualize offered spatial demand shared by STAGE and BookSim2.",
            "forbidden_use": "Do not call it observed queue occupancy, congestion, or simulator latency.",
        },
        {
            "metric": "BookSim2 packet latency",
            "source": "terminal packet_delivery.csv",
            "unit": "BookSim2 native cycles",
            "definition": "arrival_cycle minus release/injection cycle from the finite-trace adapter.",
            "evidence_level": "Numerical descriptive",
            "allowed_use": "Show BookSim2 temporal backlog and native latency distribution.",
            "forbidden_use": "Do not subtract it from STAGE packet cycles as an accuracy error.",
        },
        {
            "metric": "STAGE microbench event timeline",
            "source": "noc_cycle_timeline.csv",
            "unit": "STAGE cycles and typed events",
            "definition": "Committed PacketInjection, BufferOccupancy, Arbitration, Stall, LinkTransfer, and delivery events.",
            "evidence_level": "Exact STAGE trace",
            "allowed_use": "Trace packet/stall causal paths for supported registered cases.",
            "forbidden_use": "Do not generalize unsupported capacity-release cases.",
        },
        {
            "metric": "STAGE/SCALE-Sim timing",
            "source": "independent 4x4 WS hold-out runs",
            "unit": "tool-native cycles",
            "definition": "Total cycles produced independently from shared high-level dimensions and mapping contract.",
            "evidence_level": "Trend",
            "allowed_use": "Compare scaling direction and bounded trend on registered hold-outs.",
            "forbidden_use": "Do not claim exact schedule or per-cycle equivalence.",
        },
        {
            "metric": "simulation wall time",
            "source": "runtime summary and scalability raw results",
            "unit": "wall-clock seconds",
            "definition": "Measured host execution duration; separate from simulated cycles.",
            "evidence_level": "Measured wall clock",
            "allowed_use": "Report tool/runtime cost under the recorded host and trace mode.",
            "forbidden_use": "Do not mix wall-clock seconds with modeled hardware cycles.",
        },
    ]


def missing_field_ledger() -> list[dict[str, str]]:
    return [
        {
            "field": "STAGE per-packet CNN delivery cycle",
            "scope": "terminal sequential_network CNN result",
            "status": "not captured",
            "impact": "No packet-by-packet STAGE/BookSim2 latency overlay is produced.",
            "disposition": "Use STAGE aggregate latency/makespan only; retain Numerical descriptive boundary.",
        },
        {
            "field": "BookSim2 per-router queue occupancy and stall reason",
            "scope": "finite CNN trace adapter output",
            "status": "not captured",
            "impact": "Spatial map cannot claim observed BookSim2 congestion.",
            "disposition": "Plot exact shared-input route demand and BookSim2 delivery backlog separately.",
        },
        {
            "field": "Cross-tool common cycle semantics",
            "scope": "BookSim2 versus STAGE CNN runtime",
            "status": "not defined",
            "impact": "Absolute cycle difference is not an accuracy percentage.",
            "disposition": "Report native metrics side by side and compare layer ranking only.",
        },
        {
            "field": "SCALE-Sim packet/event identity",
            "scope": "4x4 WS hold-out",
            "status": "not available",
            "impact": "No exact event trace join with STAGE is possible.",
            "disposition": "Keep total-cycle and utilization comparisons at Trend level.",
        },
        {
            "field": "Unity trace visualization",
            "scope": "current external-visualization goal",
            "status": "out of scope",
            "impact": "No Unity production/schema/UI files are modified.",
            "disposition": "Deliver PDF/PNG and standalone offline HTML only.",
        },
    ]


def save_figure(fig: plt.Figure, output_dir: Path, stem: str) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    metadata = {"Creator": "STAGE external trace visualization", "CreationDate": None, "ModDate": None}
    fig.savefig(
        output_dir / f"{stem}.pdf",
        bbox_inches="tight",
        pad_inches=0.025,
        facecolor="white",
        metadata=metadata,
    )
    fig.savefig(
        output_dir / f"{stem}.png",
        bbox_inches="tight",
        pad_inches=0.025,
        facecolor="white",
        dpi=400,
        metadata={"Software": "STAGE external trace visualization"},
    )
    plt.close(fig)


def clean_axis(ax: plt.Axes, grid: str = "both") -> None:
    ax.spines[["top", "right"]].set_visible(False)
    if grid == "y":
        ax.grid(axis="y", color=GRID, linewidth=0.45, zorder=0)
    else:
        ax.grid(True, color=GRID, linewidth=0.45, zorder=0)
    ax.set_axisbelow(True)
    ax.tick_params(length=2.5, width=0.55, color=MUTED)


def parse_link(link: str) -> tuple[tuple[int, int], tuple[int, int]]:
    payload = link.removeprefix("link[router[").removesuffix("]]")
    left, right = payload.split("]->router[")
    x1, y1 = (int(value) for value in left.split(","))
    x2, y2 = (int(value) for value in right.split(","))
    return (x1, y1), (x2, y2)


def draw_registered_hotspot(
    ax: plt.Axes,
    link_rows: list[dict[str, Any]],
    component_rows: list[dict[str, Any]],
    summary: dict[str, Any],
    title: str = "(a) STAGE hotspot-node-5 demand",
) -> None:
    link_counts = {str(row["canonical_link_id"]): as_int(row["packet_count"]) for row in link_rows}
    node_counts = {
        as_int(str(row["canonical_component_id"]).removeprefix("endpoint[").removesuffix("]")):
        as_int(row["sent_packets"]) + as_int(row["received_packets"])
        for row in component_rows
    }
    max_link = max(link_counts.values())
    norm = Normalize(vmin=0, vmax=max_link)
    cmap = plt.get_cmap("viridis")
    for link, count in sorted(link_counts.items()):
        (x1, y1), (x2, y2) = parse_link(link)
        dx, dy = x2 - x1, y2 - y1
        offset_x, offset_y = (-0.055 * dy, 0.055 * dx)
        ax.add_patch(
            FancyArrowPatch(
                (x1 + offset_x, y1 + offset_y),
                (x2 + offset_x, y2 + offset_y),
                arrowstyle="-|>",
                mutation_scale=5.3,
                linewidth=0.55 + 2.2 * count / max_link,
                color=cmap(norm(count)),
                alpha=0.9,
                shrinkA=8,
                shrinkB=8,
                zorder=2,
            )
        )
    max_node = max(node_counts.values())
    for endpoint in range(16):
        x, y = router_xy(endpoint)
        demand = node_counts.get(endpoint, 0)
        size = 55 + 260 * demand / max_node
        color = ORANGE if endpoint == 5 else "white"
        ax.scatter([x], [y], s=size, facecolor=color, edgecolor=INK, linewidth=0.75, zorder=4)
        ax.text(x, y, str(endpoint), ha="center", va="center", fontsize=6.1, fontweight="bold", zorder=5)
    ax.set_title(title, loc="left", y=1.08, fontweight="bold", color=INK)
    ax.text(
        0.0,
        1.01,
        f"STAGE | rate={summary['injection_rate']:.2f} pkt/node/cycle | seed={summary['seed']} | "
        f"{summary['projected_offered_packets']:,} offers | {summary['congestion_cycles']:,} congestion cycles",
        transform=ax.transAxes,
        ha="left",
        va="bottom",
        fontsize=5.1,
        color=MUTED,
    )
    ax.set_xlim(-0.45, 3.45)
    ax.set_ylim(3.45, -0.45)
    ax.set_xticks(range(4), [f"x={x}" for x in range(4)])
    ax.set_yticks(range(4), [f"y={y}" for y in range(4)])
    ax.set_aspect("equal")
    ax.grid(color=GRID, linewidth=0.45)



def draw_hotspot(ax: plt.Axes, packets: list[dict[str, Any]], title: str = "(a) Shared-input spatial demand") -> None:
    link_counts: Counter[str] = Counter()
    node_counts: Counter[int] = Counter()
    for packet in packets:
        for link in str(packet["xy_route"]).split(";"):
            if link:
                link_counts[link] += 1
        node_counts[as_int(str(packet["source_endpoint"]).removeprefix("endpoint[").removesuffix("]"))] += 1
        node_counts[as_int(str(packet["destination_endpoint"]).removeprefix("endpoint[").removesuffix("]"))] += 1
    max_link = max(link_counts.values())
    norm = Normalize(vmin=0, vmax=max_link)
    cmap = plt.get_cmap("viridis")
    for link, count in sorted(link_counts.items()):
        (x1, y1), (x2, y2) = parse_link(link)
        dx, dy = x2 - x1, y2 - y1
        offset_x, offset_y = (-0.055 * dy, 0.055 * dx)
        arrow = FancyArrowPatch(
            (x1 + offset_x, y1 + offset_y),
            (x2 + offset_x, y2 + offset_y),
            arrowstyle="-|>",
            mutation_scale=5.3,
            linewidth=0.55 + 2.2 * count / max_link,
            color=cmap(norm(count)),
            alpha=0.9,
            shrinkA=8,
            shrinkB=8,
            zorder=2,
        )
        ax.add_patch(arrow)
    max_node = max(node_counts.values())
    for endpoint in range(16):
        x, y = router_xy(endpoint)
        demand = node_counts[endpoint]
        size = 55 + 260 * demand / max_node
        color = ORANGE if endpoint == max(node_counts, key=node_counts.get) else "white"
        ax.scatter([x], [y], s=size, facecolor=color, edgecolor=INK, linewidth=0.75, zorder=4)
        ax.text(x, y, str(endpoint), ha="center", va="center", fontsize=6.1, fontweight="bold", zorder=5)

    ax.set_title(title, loc="left", fontweight="bold", color=INK)
    ax.set_xlim(-0.45, 3.45)
    ax.set_ylim(3.45, -0.45)
    ax.set_xticks(range(4), [f"x={x}" for x in range(4)])
    ax.set_yticks(range(4), [f"y={y}" for y in range(4)])
    ax.set_aspect("equal")
    ax.grid(color=GRID, linewidth=0.45)
    ax.text(
        0.98,
        0.98,
        "Exact input projection",
        transform=ax.transAxes,
        ha="right",
        va="top",
        fontsize=5.3,
        color=MUTED,
        bbox={"boxstyle": "round,pad=0.2", "fc": "white", "ec": GRID, "lw": 0.5},
    )


def draw_temporal(ax: plt.Axes, timeline: list[dict[str, Any]], title: str = "(b) BookSim2 backlog over the shared trace") -> None:
    rows = [row for row in timeline if row["domain"] == "cnn_matched_input_booksim_runtime" and row.get("resolution_label") == "auto_64_bins"]
    x = np.array([as_int(row["cycle_start"]) for row in rows], dtype=float)
    released = np.array([as_int(row["released_packets"]) for row in rows], dtype=float)
    delivered = np.array([as_int(row["delivered_packets"]) for row in rows], dtype=float)
    outstanding = np.array([as_int(row["outstanding_packets_end"]) for row in rows], dtype=float)
    width = max(1, as_int(rows[0]["cycle_end_exclusive"]) - as_int(rows[0]["cycle_start"]))
    ax.step(x, released, where="post", color=BLUE, label="released / bin", linewidth=1.15)
    ax.step(x, delivered, where="post", color=TEAL, label="delivered / bin", linewidth=1.15)
    ax.fill_between(x, 0, outstanding, step="post", color=ORANGE, alpha=0.18, label="outstanding")
    peak_index = int(np.argmax(outstanding))
    ax.scatter([x[peak_index]], [outstanding[peak_index]], s=16, color=ORANGE, zorder=4)
    ax.annotate(
        f"peak backlog {int(outstanding[peak_index]):,}",
        xy=(x[peak_index], outstanding[peak_index]),
        xytext=(0.57, 0.88),
        textcoords="axes fraction",
        fontsize=5.7,
        color=INK,
        arrowprops={"arrowstyle": "->", "lw": 0.65, "color": MUTED},
    )
    ax.set_title(title, loc="left", fontweight="bold", color=INK)
    ax.set_xlabel(f"BookSim2 native cycle (bin width {width})")
    ax.set_ylabel("Packets per bin / outstanding")
    ax.legend(frameon=True, framealpha=0.9, edgecolor=GRID, ncol=1, loc="upper left")
    clean_axis(ax, grid="y")



def draw_causal(ax: plt.Axes, paths: list[dict[str, Any]], title: str = "(c) STAGE packet/stall causal path") -> None:
    packet_order = sorted({str(row["packet_id"]) for row in paths if row["packet_id"]})
    y_map = {packet: index for index, packet in enumerate(packet_order)}
    style = {
        "PacketInjection": ("o", BLUE),
        "BufferOccupancy": ("s", PURPLE),
        "Arbitration": ("^", TEAL),
        "LinkTransfer": (">", GREEN),
        "PacketMove": ("D", INK),
        "Stall": ("x", RED),
    }
    for packet in packet_order:
        packet_rows = [row for row in paths if row["packet_id"] == packet]
        cycles = [as_int(row["cycle"]) for row in packet_rows]
        ax.plot(cycles, [y_map[packet]] * len(cycles), color=GRID, linewidth=1.0, zorder=1)
        for row in packet_rows:
            event = str(row["event_type"])
            marker, color = style.get(event, (".", MUTED))
            ax.scatter([as_int(row["cycle"])], [y_map[packet]], marker=marker, color=color, s=25, linewidths=0.85, zorder=3)
            if row["stall_reason"]:
                ax.text(
                    as_int(row["cycle"]),
                    y_map[packet] + 0.16,
                    str(row["stall_reason"]),
                    rotation=45,
                    fontsize=5.0,
                    color=RED,
                    ha="left",
                )
    handles = []
    for event in ("PacketInjection", "Arbitration", "LinkTransfer", "Stall", "PacketMove"):
        marker, color = style[event]
        handles.append(plt.Line2D([], [], marker=marker, color=color, linestyle="none", markersize=4, label=event))
    ax.legend(handles=handles, frameon=True, framealpha=0.92, edgecolor=GRID, ncol=2, loc="center")
    ax.set_yticks(range(len(packet_order)), [packet.rsplit("-", 1)[-1] for packet in packet_order])
    ax.set_xlabel("STAGE cycle")
    ax.set_ylabel("Packet")
    ax.set_title(title, loc="left", fontweight="bold", color=INK)
    clean_axis(ax, grid="x")



def draw_alignment(
    ax: plt.Axes,
    holdout: list[dict[str, str]],
    title: str = "(d) Independent 4x4 WS timing trend",
) -> None:
    grouped: dict[tuple[str, str], dict[str, float]] = defaultdict(dict)
    for row in holdout:
        if row.get("status") == "completed":
            grouped[(row["case_id"], row["repeat"])][row["tool"].lower()] = as_float(row["total_cycles"])
    pairs = [
        (case_id, repeat, tools["scalesim"], tools["stage"])
        for (case_id, repeat), tools in sorted(grouped.items())
        if {"scalesim", "stage"} <= tools.keys()
    ]
    x = np.array([item[2] for item in pairs])
    y = np.array([item[3] for item in pairs])
    low = min(float(x.min()), float(y.min()))
    high = max(float(x.max()), float(y.max()))
    ax.fill_between([low, high], [0.9 * low, 0.9 * high], [1.1 * low, 1.1 * high], color=TEAL, alpha=0.10, label="±10% guide")
    ax.plot([low, high], [low, high], linestyle="--", color=MUTED, linewidth=0.8, label="equal native count")
    case_names = sorted({item[0] for item in pairs})
    colors = {case: plt.get_cmap("tab10")(index % 10) for index, case in enumerate(case_names)}
    for case_id, repeat, scale_cycles, stage_cycles in pairs:
        ax.scatter(
            [scale_cycles],
            [stage_cycles],
            color=colors[case_id],
            marker="o" if repeat == "0" else "x",
            s=20,
            linewidths=0.8,
            zorder=3,
        )
    relative = [abs(stage - scale) / scale for _, _, scale, stage in pairs]
    ax.text(
        0.04,
        0.94,
        f"16 independent pairs\nmax |difference| = {100 * max(relative):.2f}%",
        transform=ax.transAxes,
        va="top",
        fontsize=5.6,
        bbox={"boxstyle": "round,pad=0.22", "fc": "white", "ec": GRID, "lw": 0.6},
    )
    ax.set_xscale("log")
    ax.set_yscale("log")
    ax.set_xlabel("SCALE-Sim native cycles")
    ax.set_ylabel("STAGE native cycles")
    ax.set_title(title, loc="left", fontweight="bold", color=INK)
    ax.legend(frameon=False, loc="lower right")
    clean_axis(ax)



def render_figures(
    output: Path,
    packets: list[dict[str, Any]],
    hotspot_link_rows: list[dict[str, Any]],
    hotspot_component_rows: list[dict[str, Any]],
    hotspot_summary: dict[str, Any],
    timeline: list[dict[str, Any]],
    paths: list[dict[str, Any]],
    holdout: list[dict[str, str]],
) -> None:
    figures = output / "figures"
    configure_style()

    fig, ax = plt.subplots(figsize=(3.35, 3.15))
    fig.subplots_adjust(left=0.17, right=0.97, bottom=0.19, top=0.93)
    draw_registered_hotspot(ax, hotspot_link_rows, hotspot_component_rows, hotspot_summary, "STAGE hotspot-node-5 spatial demand")
    save_figure(fig, figures, "fig_trace_hotspot_spatial")

    fig, ax = plt.subplots(figsize=(3.35, 2.25))
    fig.subplots_adjust(left=0.16, right=0.97, bottom=0.2, top=0.9)
    draw_temporal(ax, timeline, "BookSim2 temporal backlog on the shared CNN trace")
    save_figure(fig, figures, "fig_trace_temporal_congestion")

    fig, ax = plt.subplots(figsize=(3.35, 2.3))
    fig.subplots_adjust(left=0.16, right=0.97, bottom=0.2, top=0.9)
    draw_causal(ax, paths, "STAGE exact packet/stall causal path")
    save_figure(fig, figures, "fig_trace_packet_causal_path")

    fig, ax = plt.subplots(figsize=(3.35, 2.55))
    fig.subplots_adjust(left=0.18, right=0.97, bottom=0.19, top=0.91)
    draw_alignment(ax, holdout, "Independent STAGE / SCALE-Sim trend")
    save_figure(fig, figures, "fig_trace_cross_tool_alignment")

    fig, axes = plt.subplots(2, 2, figsize=(7.0, 5.05))
    fig.subplots_adjust(left=0.08, right=0.985, bottom=0.085, top=0.95, wspace=0.27, hspace=0.34)
    draw_registered_hotspot(axes[0, 0], hotspot_link_rows, hotspot_component_rows, hotspot_summary)
    draw_temporal(axes[0, 1], timeline)
    draw_causal(axes[1, 0], paths)
    draw_alignment(axes[1, 1], holdout)
    fig.text(
        0.5,
        0.012,
        "Evidence boundaries are panel-specific: shared input projection, native BookSim2 runtime, exact STAGE trace, and cross-tool Trend.",
        ha="center",
        fontsize=5.8,
        color=MUTED,
    )
    save_figure(fig, figures, "fig_trace_analysis_2x2")


def render_html(output: Path, packets: list[dict[str, Any]], timeline: list[dict[str, Any]], holdout: list[dict[str, str]]) -> None:
    endpoint_counts: Counter[int] = Counter()
    link_counts: Counter[str] = Counter()
    layer_counts: Counter[str] = Counter()
    for packet in packets:
        source = as_int(str(packet["source_endpoint"]).removeprefix("endpoint[").removesuffix("]"))
        destination = as_int(str(packet["destination_endpoint"]).removeprefix("endpoint[").removesuffix("]"))
        endpoint_counts[source] += 1
        endpoint_counts[destination] += 1
        layer_counts[str(packet["layer_id"])] += 1
        for link in str(packet["xy_route"]).split(";"):
            if link:
                link_counts[link] += 1
    temporal = [row for row in timeline if row["domain"] == "cnn_matched_input_booksim_runtime" and row.get("resolution_label") == "auto_64_bins"]
    grouped: dict[tuple[str, str], dict[str, float]] = defaultdict(dict)
    for row in holdout:
        if row.get("status") == "completed":
            grouped[(row["case_id"], row["repeat"])][row["tool"].lower()] = as_float(row["total_cycles"])
    payload = {
        "endpoint_counts": {str(key): value for key, value in sorted(endpoint_counts.items())},
        "link_counts": dict(sorted(link_counts.items())),
        "layer_counts": dict(sorted(layer_counts.items())),
        "timeline": [
            {
                "cycle": row["cycle_start"],
                "released": row["released_packets"],
                "delivered": row["delivered_packets"],
                "outstanding": row["outstanding_packets_end"],
            }
            for row in temporal
        ],
        "holdout": [
            {"case": case, "repeat": repeat, "stage": tools["stage"], "scalesim": tools["scalesim"]}
            for (case, repeat), tools in sorted(grouped.items())
            if {"stage", "scalesim"} <= tools.keys()
        ],
    }
    template = r"""<!doctype html>
<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">
<title>STAGE Trace Evidence Explorer</title><style>
:root{color-scheme:light dark;--bg:#f4f7f8;--panel:#fff;--ink:#1f2a33;--muted:#63707a;--line:#d9e0e5;--blue:#2878a8;--orange:#d78324;--teal:#168b83;--red:#b44747}
@media(prefers-color-scheme:dark){:root{--bg:#10161b;--panel:#172027;--ink:#edf3f6;--muted:#a7b3bb;--line:#34434d}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 system-ui,sans-serif}.wrap{max-width:1180px;margin:auto;padding:24px}.head{display:flex;gap:18px;justify-content:space-between;align-items:end}.eyebrow{color:var(--teal);font-weight:700;letter-spacing:.08em;text-transform:uppercase;font-size:12px}h1{margin:.2rem 0;font-size:clamp(24px,4vw,40px)}p{color:var(--muted);max-width:78ch}.grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-top:18px}.card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:16px;box-shadow:0 8px 24px #0001}.card h2{margin:0 0 4px;font-size:16px}.note{font-size:12px;color:var(--muted)}svg{display:block;width:100%;height:auto;margin-top:10px}.controls{display:flex;gap:8px;align-items:center}.badge{border:1px solid var(--line);border-radius:999px;padding:5px 9px;font-size:12px}.legend{display:flex;gap:12px;flex-wrap:wrap;font-size:12px;color:var(--muted)}.sw{display:inline-block;width:10px;height:10px;border-radius:3px;margin-right:4px}.footer{margin-top:16px;font-size:12px;color:var(--muted)}@media(max-width:760px){.grid{grid-template-columns:1fr}.head{align-items:start;flex-direction:column}}
</style></head><body><main class=\"wrap\"><div class=\"head\"><div><div class=\"eyebrow\">Manifest-indexed evidence</div><h1>Trace alignment explorer</h1><p>Shared-input demand, native runtime behavior, and cross-tool trends remain separate. Hover marks for exact values.</p></div><div class=\"controls\"><span class=\"badge\">Exact input</span><span class=\"badge\">Native runtime</span><span class=\"badge\">Trend</span></div></div>
<section class=\"grid\"><article class=\"card\"><h2>4x4 CNN route demand</h2><div class=\"note\">Exact shared packet input projected over frozen XY paths; not observed queue occupancy.</div><svg id=\"mesh\" viewBox=\"0 0 480 360\" role=\"img\" aria-label=\"4 by 4 mesh route demand\"></svg></article>
<article class=\"card\"><h2>BookSim2 temporal backlog</h2><div class=\"note\">Release is Exact input. Delivery and backlog use BookSim2 native cycles.</div><svg id=\"timeline\" viewBox=\"0 0 520 300\" role=\"img\" aria-label=\"BookSim2 release delivery and backlog timeline\"></svg><div class=\"legend\"><span><i class=\"sw\" style=\"background:var(--blue)\"></i>released</span><span><i class=\"sw\" style=\"background:var(--teal)\"></i>delivered</span><span><i class=\"sw\" style=\"background:var(--orange)\"></i>outstanding</span></div></article>
<article class=\"card\"><h2>Layer traffic composition</h2><div class=\"note\">Packet counts from the materialized-im2col transport trace; no CNN numerical inference claim.</div><svg id=\"layers\" viewBox=\"0 0 520 300\" role=\"img\" aria-label=\"CNN layer packet counts\"></svg></article>
<article class=\"card\"><h2>Independent 4x4 WS trend</h2><div class=\"note\">STAGE and SCALE-Sim use independent schedulers. Native cycles are Trend evidence.</div><svg id=\"holdout\" viewBox=\"0 0 520 300\" role=\"img\" aria-label=\"STAGE and SCALE-Sim timing trend\"></svg></article></section>
<div class=\"footer\">Generated from terminal, hashed source artifacts. Missing fields and evidence boundaries are recorded in the companion bundle.</div></main><script>
const D=__DATA__;const NS='http://www.w3.org/2000/svg';function el(n,a={},t=''){const e=document.createElementNS(NS,n);Object.entries(a).forEach(([k,v])=>e.setAttribute(k,v));if(t)e.textContent=t;return e}function line(s,x1,y1,x2,y2,c,w=2){s.append(el('line',{x1,y1,x2,y2,stroke:c,'stroke-width':w,'stroke-linecap':'round'}))}function txt(s,x,y,t,a='middle'){s.append(el('text',{x,y,fill:'currentColor','font-size':11,'text-anchor':a},t))}
const css=n=>getComputedStyle(document.documentElement).getPropertyValue(n).trim();
(function(){const s=document.querySelector('#mesh'),lc=D.link_counts,ec=D.endpoint_counts,maxL=Math.max(...Object.values(lc)),maxE=Math.max(...Object.values(ec));Object.entries(lc).forEach(([id,v])=>{const m=[...id.matchAll(/router\[(\d),(\d)\]/g)],a=m[0].slice(1).map(Number),b=m[1].slice(1).map(Number),x1=70+a[0]*110,y1=45+a[1]*85,x2=70+b[0]*110,y2=45+b[1]*85;line(s,x1,y1,x2,y2,css('--teal'),.5+5*v/maxL)});for(let i=0;i<16;i++){const x=70+(i%4)*110,y=45+Math.floor(i/4)*85,r=11+13*(ec[i]||0)/maxE,c=el('circle',{cx:x,cy:y,r,fill:i==0?css('--orange'):css('--panel'),stroke:css('--ink'),'stroke-width':1.5});c.append(el('title',{},`endpoint ${i}: ${ec[i]||0} send/receive packets`));s.append(c);txt(s,x,y+4,String(i))}})();
function plotSeries(id,series){const s=document.querySelector(id),W=520,H=300,p=38,maxX=series.length-1,maxY=Math.max(...series.flatMap(x=>[x.released,x.delivered,x.outstanding]));for(let k=0;k<5;k++){const y=p+(H-2*p)*k/4;line(s,p,y,W-p,y,css('--line'),1);txt(s,p-6,y+4,String(Math.round(maxY*(4-k)/4)),'end')}[['outstanding','--orange'],['released','--blue'],['delivered','--teal']].forEach(([key,col])=>{const pts=series.map((d,i)=>`${p+(W-2*p)*i/maxX},${H-p-(H-2*p)*d[key]/maxY}`).join(' ');s.append(el('polyline',{points:pts,fill:'none',stroke:css(col),'stroke-width':2}))});txt(s,W/2,H-8,'native cycle bins')}
plotSeries('#timeline',D.timeline);
(function(){const s=document.querySelector('#layers'),a=Object.entries(D.layer_counts),W=520,H=300,p=45,m=Math.max(...a.map(x=>x[1]));a.forEach(([k,v],i)=>{const bw=(W-2*p)/a.length*.65,x=p+i*(W-2*p)/a.length+(W-2*p)/a.length*.18,h=(H-2*p)*v/m,y=H-p-h;s.append(el('rect',{x,y,width:bw,height:h,rx:3,fill:css('--blue')}));txt(s,x+bw/2,H-p+16,k);txt(s,x+bw/2,y-5,String(v))})})();
(function(){const s=document.querySelector('#holdout'),a=D.holdout,W=520,H=300,p=48,vals=a.flatMap(d=>[d.stage,d.scalesim]),lo=Math.log10(Math.min(...vals)),hi=Math.log10(Math.max(...vals)),sc=v=>p+(W-2*p)*(Math.log10(v)-lo)/(hi-lo),sy=v=>H-sc(v)+p;line(s,p,H-p,W-p,p,css('--muted'),1.5);a.forEach(d=>{const c=el('circle',{cx:sc(d.scalesim),cy:sy(d.stage),r:d.repeat==='0'?4:3,fill:d.repeat==='0'?css('--purple'):css('--orange')});c.append(el('title',{},`${d.case} r${d.repeat}: SCALE-Sim ${d.scalesim}, STAGE ${d.stage}`));s.append(c)});txt(s,W/2,H-8,'SCALE-Sim native cycles');const y=el('text',{x:14,y:H/2,fill:'currentColor','font-size':11,transform:`rotate(-90 14 ${H/2})`,'text-anchor':'middle'},'STAGE native cycles');s.append(y)})();
</script></body></html>"""
    html = template.replace("__DATA__", json.dumps(payload, sort_keys=True, separators=(",", ":")))
    write_text(output / "figures/trace_explorer.html", html)


def write_claim_report(output: Path, packets: list[dict[str, Any]], paths: list[dict[str, Any]], holdout: list[dict[str, str]]) -> None:
    max_relative = 0.0
    grouped: dict[tuple[str, str], dict[str, float]] = defaultdict(dict)
    for row in holdout:
        if row.get("status") == "completed":
            grouped[(row["case_id"], row["repeat"])][row["tool"].lower()] = as_float(row["total_cycles"])
    for tools in grouped.values():
        if {"stage", "scalesim"} <= tools.keys():
            max_relative = max(max_relative, abs(tools["stage"] - tools["scalesim"]) / tools["scalesim"])
    lines = [
        "# External Trace Visualization Claim Status",
        "",
        "| Claim | Status | Evidence | Boundary |",
        "| --- | --- | --- | --- |",
        f"| The CNN transport input is identical across STAGE and BookSim2. | measured | {len(packets):,} packet IDs and the registered input hash are joined. | Exact input contract, not CNN numerical inference. |",
        "| The registered STAGE hotspot case concentrates traffic at node 5. | measured | The stable 0.06-rate, seed-0 offer stream reproduces all 12,433 recorded offers; node 5 and its inbound links dominate the XY projection. | Link counts are Exact input projection; queue/stall values remain aggregate STAGE runtime metrics. |",
        "| STAGE exposes packet-to-stall causality. | measured | The supported contention case records RouterConflict followed by LinkBusy before delivery. | Exact only for the registered supported microbench. |",
        f"| STAGE and SCALE-Sim follow the same 4x4 WS timing trend. | trend | 16 independent repeat-level pairs; maximum native-cycle difference {100 * max_relative:.2f}%. | Independent scheduling and prefetch semantics; no cycle-exact claim. |",
        "| STAGE and BookSim2 have equivalent absolute CNN packet latency. | not supported | The same input is used, but the native cycle models differ and STAGE per-packet delivery was not persisted. | Do not report an accuracy percentage from their absolute cycle difference. |",
        "| The figures show observed cross-tool router occupancy. | not supported | BookSim2 router-level occupancy/stall events are absent from the terminal CNN artifact. | Shared-input route demand and native delivery backlog are shown separately. |",
        "| Unity provides the new trace views. | pending | This goal deliberately leaves Unity unchanged. | External PDF/PNG/HTML only. |",
        "",
        "No pending or unsupported field is substituted with zero or inferred runtime data.",
    ]
    write_text(output / "summary/claim_status.md", "\n".join(lines))


def write_supporting_reports(
    output: Path,
    hotspot_summary: dict[str, Any],
    noc_rows: list[dict[str, str]],
) -> None:
    reviewer = [
        "# Reviewer-response mapping",
        "",
        "| Reviewer concern | Evidence now available | Disposition |",
        "| --- | --- | --- |",
        "| The hotspot is not visually traceable. | The spatial panel reconstructs the stable STAGE hotspot-node-5 offer stream and highlights node 5 plus its inbound XY links. | Addressed for input demand; per-link runtime occupancy is still unavailable. |",
        "| Cross-tool numbers appear aligned without a common denominator. | Each CSV row carries its source hash, native unit, and Exact/Numerical/Trend boundary. | Addressed; absolute STAGE/BookSim2 CNN cycles remain not comparable. |",
        "| A packet-level causal explanation is missing. | The supported contention trace shows injection, arbitration loss, LinkBusy stalls, transfer, and delivery with stable IDs. | Addressed for the registered exact microbench. |",
        "| SCALE-Sim agreement may come from shared generated schedules. | The independent hold-out audit and 16 repeat-level pairs are linked directly. | Partially addressed; timing remains Trend rather than exact accuracy. |",
        "| Large traces may be impractical. | Existing 32x32 one-million-packet metrics report wall time, memory, and trace size; this bundle does not copy the multi-GiB raw trace. | Addressed as host/runtime context, not a hardware-model claim. |",
        "| The new visualization should be visible in Unity. | No Unity files are changed by this goal. | Pending by explicit scope; PDF/PNG/offline HTML are delivered. |",
    ]
    write_text(output / "summary/reviewer_response_mapping.md", "\n".join(reviewer))

    suggestions = [
        "# Paper replacement suggestions",
        "",
        "These paragraphs are suggestions only; no LaTeX file is modified.",
        "",
        "## Trace visualization",
        "",
        f"We replayed the registered hotspot-node-5 traffic at an injection rate of {hotspot_summary['injection_rate']:.2f} with seed {hotspot_summary['seed']}. "
        f"The reconstructed offer stream matched all {hotspot_summary['recorded_offered_packets']:,} packets recorded by the STAGE run. "
        "The spatial view therefore marks offered link demand, while queue occupancy and stall counts remain aggregate runtime measurements.",
        "",
        "## Cross-tool alignment",
        "",
        "We joined the STAGE and BookSim2 CNN transport runs by the exact packet-trace hash. "
        "Their release, source, destination, and payload fields match, but their native pipeline cycles do not share an exact timing definition. "
        "We therefore report workload response and layer ordering without treating the absolute latency gap as an error percentage.",
        "",
        "For the independent 4x4 weight-stationary hold-outs, STAGE and SCALE-Sim produced the same scaling trend across 16 repeat-level pairs. "
        "The maximum native-cycle difference was 9.69%; we label this result as Trend evidence because scheduling, arbitration, and prefetch boundaries differ.",
        "",
        "## Figure caption candidate",
        "",
        "External trace analysis. (a) Offered traffic for the registered STAGE hotspot case, projected over the frozen XY routes. "
        "(b) Release and delivery backlog for the shared CNN transport trace in BookSim2 native cycles. "
        "(c) An exact STAGE contention trace linking arbitration and link-busy stalls to packet delivery. "
        "(d) Independent STAGE and SCALE-Sim timing trends for the 4x4 weight-stationary hold-outs.",
    ]
    write_text(output / "summary/paper_replacement_suggestions.md", "\n".join(suggestions))

    unsupported = sorted(
        {(row.get("case_id", ""), row.get("repeat", "")) for row in noc_rows if row.get("checkpoint_status") == "not_supported"}
    )
    failures = [
        "# Preserved unsupported and excluded inputs",
        "",
        "The following rows remain explicit and are not silently dropped or drawn as zeros:",
        "",
    ]
    failures.extend(f"- `{case_id}` repeat `{repeat}`: Not supported by the registered exact NoC contract." for case_id, repeat in unsupported)
    failures.extend(
        [
            "- `external_rerun_20260717` SCALE-Sim size rerun: partial terminal coverage; excluded from all figures.",
            "- STAGE per-packet CNN delivery cycles: not persisted in the terminal bundle; excluded from packet-level numerical deltas.",
            "- BookSim2 per-router CNN occupancy/stall reasons: not captured; excluded from spatial runtime claims.",
        ]
    )
    write_text(output / "failures/README.md", "\n".join(failures))


def output_manifest(output: Path, source_rows: list[dict[str, str]]) -> dict[str, Any]:
    files: list[dict[str, Any]] = []
    for path in sorted(item for item in output.rglob("*") if item.is_file()):
        relative = path.relative_to(output).as_posix()
        if relative == "manifests/trace_visualization_manifest_index.json":
            continue
        files.append({"path": relative, "bytes": path.stat().st_size, "sha256": sha256_path(path)})
    return {
        "schema_version": "stage-trace-visualization-manifest/1.0",
        "generation_policy": "deterministic; no wall-clock timestamp is embedded in generated artifacts",
        "scope": "external visualization only; Unity and simulator runtime semantics unchanged",
        "source_evidence": source_rows,
        "outputs": files,
        "output_count": len(files),
        "integrity": {"content_sha256": canonical_sha256(files)},
    }


def build_bundle(repo: Path, output: Path) -> dict[str, Any]:
    repo = repo.resolve()
    output = output.resolve()
    source_before = source_spec(repo)
    for directory in ("config", "summary", "figures", "manifests", "failures", "logs"):
        (output / directory).mkdir(parents=True, exist_ok=True)

    candidates = read_csv(repo / EXTERNAL / "summary/candidate_results.csv")
    cnn_cross = read_csv(repo / EXTERNAL / "summary/mnist_cnn_noc_cross_tool.csv")
    stage_timeline = read_csv(repo / REVIEWER / "summary/noc_cycle_timeline.csv")
    noc_oracle = read_csv(repo / REVIEWER / "summary/noc_oracle_timeline.csv")
    noc_microbench = read_csv(repo / REVIEWER / "summary/noc_contract_microbench.csv")
    holdout = read_csv(repo / REVIEWER / "summary/holdout_scalesim_stage_timing.csv")
    interventions = read_csv(repo / REVIEWER / "summary/trace_guided_interventions.csv")
    rq2_rows = read_csv(repo / FINAL / "summary/rq2_booksim_stage_sweep.csv")
    scalability_rows = read_csv(repo / REVIEWER / "summary/stage_scalability.csv")

    if not cnn_cross or not all(truthy(row.get("exact_input_trace_match")) for row in cnn_cross):
        raise ValueError("CNN cross-tool input match is not terminal and exact")
    supported_noc = [row for row in noc_microbench if row.get("checkpoint_status") == "completed"]
    if len(supported_noc) != 14 or not all(truthy(row.get("oracle_matched")) for row in supported_noc):
        raise ValueError("Expected 14 accepted repeat-level NoC oracle cases")
    if len(noc_oracle) == 0:
        raise ValueError("Independent NoC oracle timeline is empty")

    packets, packet_delivery_path = build_aligned_packets(repo, candidates)
    link_windows, component_windows = build_spatial_tables(packets)
    hotspot_link_rows, hotspot_component_rows, hotspot_summary = build_hotspot_projection(repo, rq2_rows)
    link_windows.extend(hotspot_link_rows)
    component_windows.extend(hotspot_component_rows)
    stage_timeline_relative = (REVIEWER / "summary/noc_cycle_timeline.csv").as_posix()
    stage_timeline_sha256 = sha256_path(repo / REVIEWER / "summary/noc_cycle_timeline.csv")
    timeline_bins = build_timeline_bins(packets, stage_timeline, stage_timeline_relative, stage_timeline_sha256)
    stall_paths = build_stall_paths(stage_timeline, stage_timeline_relative, stage_timeline_sha256)
    config_relative = (EXTERNAL / "config/v_bs_cnn.resolved.json").as_posix()
    trace_relative = (FINAL / "raw/mnist_cnn_noc_traces/sequential_network.csv").as_posix()
    entity_map = build_entity_map(
        packets,
        config_path=config_relative,
        config_sha256=sha256_path(repo / config_relative),
        trace_path=trace_relative,
        trace_sha256=sha256_path(repo / trace_relative),
    )
    aligned_runs = build_aligned_runs(candidates, holdout, noc_microbench)
    cross_tool = build_cross_tool_alignment(cnn_cross, holdout, noc_microbench, interventions)
    for run in aligned_runs:
        raw = Path(str(run["raw_path"]))
        if run["alignment_family"] in {"4x4 WS hold-out", "NoC independent oracle"}:
            raw = REVIEWER / raw
        source = repo / raw
        run["source_path"] = raw.as_posix()
        run["source_sha256"] = sha256_path(source) if source.is_file() else "pending_missing_mapping"
        run["metric_definition"] = run["comparison_boundary"]
    comparison_sources = {
        "CNN transport": EXTERNAL / "summary/mnist_cnn_noc_cross_tool.csv",
        "4x4 WS hold-out timing": REVIEWER / "summary/holdout_scalesim_stage_timing.csv",
        "NoC independent oracle": REVIEWER / "summary/noc_contract_microbench.csv",
        "Trace-guided intervention": REVIEWER / "summary/trace_guided_interventions.csv",
    }
    for row in cross_tool:
        source = comparison_sources[row["comparison_family"]]
        row["source_path"] = source.as_posix()
        row["source_sha256"] = sha256_path(repo / source)
        row["metric_definition"] = row["claim_boundary"]
    definitions = metric_definitions()
    missing = missing_field_ledger()

    config = {
        "schema_version": "stage-trace-visualization-config/1.0",
        "alignment_schema": "aspg-trace-alignment-1.0",
        "mesh": {"dimensions": [4, 4], "routing": "dimension_order_xy", "endpoint_count": 16},
        "shared_trace": {"case_id": "sequential_network", "window_cycles": TRACE_WINDOW_CYCLES},
        "temporal_bins": TEMPORAL_BINS,
        "causal_trace": {"case_id": CAUSAL_CASE, "repeat": as_int(CAUSAL_REPEAT)},
        "evidence_policy": [
            "Never combine traffic bits and stall cycles into one heat score.",
            "Shared-input route projection is not runtime queue occupancy.",
            "Native cycles remain tool-specific unless an exact oracle contract exists.",
            "Missing fields remain explicit and are never filled with zero.",
        ],
        "source_evidence_sha256": canonical_sha256(source_before),
    }
    write_json(output / "config/trace_visualization.resolved.json", config)
    alignment_schema = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "aspg-trace-alignment-1.0",
        "title": "STAGE external trace alignment row",
        "type": "object",
        "required": ["canonical_id", "source_path", "source_sha256", "metric_definition", "evidence_level"],
        "properties": {
            "canonical_id": {"type": "string"},
            "source_path": {"type": "string", "minLength": 1},
            "source_sha256": {"type": "string", "minLength": 64},
            "metric_definition": {"type": "string", "minLength": 1},
            "evidence_level": {"enum": ["Exact input", "Exact within-tool", "Numerical descriptive", "Trend", "Not comparable", "Not supported", "Pending"]},
            "native_cycle": {"type": ["integer", "null"]},
            "input_release_cycle": {"type": ["integer", "null"]},
            "injection_cycle": {"type": ["integer", "null"]},
            "delivery_cycle": {"type": ["integer", "null"]},
            "window_offset": {"type": ["integer", "null"]},
            "normalized_progress": {"type": ["number", "null"], "minimum": 0, "maximum": 1},
            "host_wall_seconds": {"type": ["number", "null"], "minimum": 0},
        },
        "comparison_policy": "Time coordinates and evidence levels are never silently coerced or mixed.",
    }
    write_json(output / "config/aspg_trace_alignment.schema.json", alignment_schema)
    write_json(
        output / "manifests/execution_environment.json",
        {
            "schema_version": "stage-trace-visualization-environment/1.0",
            "command": ["python", "experiments/aspdac/scripts/build_trace_visualization_bundle.py", "--output", "<OUTPUT_BUNDLE>"],
            "host": platform.node(),
            "platform": platform.platform(),
            "python": sys.version.split()[0],
            "matplotlib": matplotlib.__version__,
            "generator_path": "experiments/aspdac/scripts/build_trace_visualization_bundle.py",
            "generator_sha256": sha256_path(Path(__file__)),
            "goal_path": "goal/10_Phase10_外部Trace可视化与跨工具数据对齐Goal_2026-07-17.md",
            "goal_sha256": sha256_path(repo / "goal/10_Phase10_外部Trace可视化与跨工具数据对齐Goal_2026-07-17.md"),
            "phase_10_status": "IN_PROGRESS",
            "phase_10a_status": "LOCKED",
        },
    )

    write_csv(output / "summary/aligned_runs.csv", aligned_runs, fieldnames_for(aligned_runs))
    write_csv(output / "summary/canonical_entity_map.csv", entity_map, fieldnames_for(entity_map))
    write_csv(output / "summary/aligned_packets.csv", packets, fieldnames_for(packets))
    write_csv(output / "summary/spatial_link_windows.csv", link_windows, fieldnames_for(link_windows))
    write_csv(output / "summary/spatial_component_windows.csv", component_windows, fieldnames_for(component_windows))
    write_csv(output / "summary/timeline_bins.csv", timeline_bins, fieldnames_for(timeline_bins))
    write_csv(output / "summary/stall_cause_paths.csv", stall_paths, fieldnames_for(stall_paths))
    write_csv(output / "summary/cross_tool_alignment.csv", cross_tool, fieldnames_for(cross_tool))
    write_csv(output / "summary/metric_definition_matrix.csv", definitions, fieldnames_for(definitions))
    write_csv(output / "summary/missing_field_ledger.csv", missing, fieldnames_for(missing))

    paired_manifest = {
        "schema_version": "stage-trace-paired-run-manifest/1.0",
        "shared_cnn_input": {
            "path": (FINAL / "raw/mnist_cnn_noc_traces/sequential_network.csv").as_posix(),
            "sha256": sha256_path(repo / FINAL / "raw/mnist_cnn_noc_traces/sequential_network.csv"),
            "packet_count": len(packets),
            "booksim_packet_delivery_path": packet_delivery_path.relative_to(repo).as_posix(),
            "booksim_packet_delivery_sha256": sha256_path(packet_delivery_path),
            "stage_per_packet_delivery_status": "not_available_in_terminal_bundle",
        },
        "stage_hotspot_node5": {
            "candidate_id": hotspot_summary["candidate_id"],
            "raw_path": hotspot_summary["raw_path"],
            "raw_sha256": hotspot_summary["raw_sha256"],
            "injection_rate": hotspot_summary["injection_rate"],
            "seed": hotspot_summary["seed"],
            "projected_offered_packets": hotspot_summary["projected_offered_packets"],
            "recorded_offered_packets": hotspot_summary["recorded_offered_packets"],
            "projection_validation": hotspot_summary["projection_validation"],
        },
        "holdout_pair_count": len({(row["case_id"], row["repeat"]) for row in holdout}),
        "supported_noc_repeat_count": len(supported_noc),
        "unsupported_noc_repeat_count": len([row for row in noc_microbench if row.get("checkpoint_status") == "not_supported"]),
        "comparison_policy_sha256": canonical_sha256(definitions),
    }
    write_json(output / "manifests/paired_run_manifest.json", paired_manifest)
    write_csv(output / "manifests/source_evidence_before.csv", source_before, list(source_before[0]))

    write_json(output / "summary/hotspot_node5_spatial_summary.json", hotspot_summary)
    render_figures(output, packets, hotspot_link_rows, hotspot_component_rows, hotspot_summary, timeline_bins, stall_paths, holdout)
    render_html(output, packets, timeline_bins, holdout)
    write_claim_report(output, packets, stall_paths, holdout)
    write_text(
        output / "summary/experiment_claim_register.md",
        (repo / "experiments/aspdac/specs/reporting/experiment_claim_register_20260717.md").read_text(encoding="utf-8"),
    )
    write_supporting_reports(output, hotspot_summary, noc_microbench)
    large_trace_rows = build_large_trace_discipline(scalability_rows, output)
    write_csv(output / "summary/large_trace_discipline.csv", large_trace_rows, fieldnames_for(large_trace_rows))

    source_after = source_spec(repo)
    if source_before != source_after:
        raise RuntimeError("A source evidence artifact changed while building the visualization bundle")
    audit = {
        "schema_version": "stage-trace-source-immutability-audit/1.0",
        "passed": True,
        "source_count": len(source_before),
        "before_sha256": canonical_sha256(source_before),
        "after_sha256": canonical_sha256(source_after),
        "changed_sources": [],
    }
    write_json(output / "manifests/source_immutability_audit.json", audit)
    manifest = output_manifest(output, source_after)
    write_json(output / "manifests/trace_visualization_manifest_index.json", manifest)
    return manifest


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = build_bundle(args.repo_root, args.output)
    print(json.dumps({"output": str(args.output.resolve()), "output_count": manifest["output_count"], "status": "completed"}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
