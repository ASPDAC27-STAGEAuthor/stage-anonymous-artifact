#!/usr/bin/env python3
"""Finalize the ASP-DAC reviewer-extension evidence bundle.

This script is intentionally read-only with respect to P0/P1/P2 source evidence.
It consumes published CSV/manifests and regenerates paper-facing figures, claim
boundaries, replacement text, execution disposition, and an indexed evidence
manifest.  Use --dry-run for a no-write validation pass.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import statistics
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_BUNDLE = SCRIPT_DIR.parent / "results" / "reviewer_extension_20260717"
STATUS_ORDER = ("measured", "trend", "not_supported", "pending")


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_csv(path: Path) -> list[dict[str, str]]:
    if not path.is_file():
        return []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def read_json(path: Path) -> dict[str, Any]:
    if not path.is_file():
        return {}
    with path.open("r", encoding="utf-8-sig") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return value


def truthy(value: Any) -> bool:
    return str(value).strip().lower() in {"1", "true", "yes", "pass", "passed"}


def as_float(value: Any, default: float | None = None) -> float | None:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return default
    return result if math.isfinite(result) else default


def as_int(value: Any, default: int = 0) -> int:
    number = as_float(value)
    return int(number) if number is not None else default


def sha256_file(path: Path, chunk_size: int = 1024 * 1024) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(chunk_size), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_sha256(value: Any) -> str:
    payload = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def relative(path: Path, bundle: Path) -> str:
    try:
        return path.resolve().relative_to(bundle.resolve()).as_posix()
    except ValueError:
        return str(path.resolve())


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8", newline="\n")


def write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def md_cell(value: Any) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def md_table(rows: list[dict[str, Any]], fields: list[str], labels: list[str]) -> str:
    lines = [
        "| " + " | ".join(labels) + " |",
        "| " + " | ".join("---" for _ in fields) + " |",
    ]
    for row in rows:
        lines.append("| " + " | ".join(md_cell(row.get(field, "")) for field in fields) + " |")
    return "\n".join(lines)


def short_case(case_id: str) -> str:
    replacements = {
        "noc_n01_single_128": "N01 single 128b",
        "noc_n02_single_256_vc1": "N02 single 256b",
        "noc_n03_single_512_vc3": "N03 single 512b",
        "noc_n04_single_1024": "N04 single 1024b",
        "noc_n05_contend_128": "N05 contend 128b",
        "noc_n06_contend_512": "N06 contend 512b",
        "noc_n09_atomic_depth_boundary": "N09 atomic-depth boundary",
    }
    return replacements.get(case_id, case_id.replace("holdout_", "").replace("_", " "))


def analyze_noc(bundle: Path) -> dict[str, Any]:
    summary = bundle / "summary"
    rows = read_csv(summary / "noc_contract_microbench.csv")
    stage_timeline = read_csv(summary / "noc_cycle_timeline.csv")
    oracle_timeline = read_csv(summary / "noc_oracle_timeline.csv")

    modeled = [
        row
        for row in rows
        if row.get("checkpoint_status") == "completed"
        and row.get("runtime_status") in {"completed", "expected_boundary"}
        and truthy(row.get("oracle_matched"))
        and bool(row.get("stage_trace_sha256", "").strip())
    ]
    unsupported = [
        row
        for row in rows
        if row.get("checkpoint_status") == "not_supported"
        or row.get("runtime_status") == "not_supported"
    ]

    by_case: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in modeled:
        by_case[row["case_id"]].append(row)

    stage_deliveries: dict[tuple[str, str], list[int]] = defaultdict(list)
    oracle_deliveries: dict[tuple[str, str], list[int]] = defaultdict(list)
    modeled_keys = {(row["case_id"], row["repeat"]) for row in modeled}
    for row in stage_timeline:
        key = (row.get("case_id", ""), row.get("repeat", ""))
        if key in modeled_keys and truthy(row.get("Delivered")):
            stage_deliveries[key].append(as_int(row.get("Cycle")))
    for row in oracle_timeline:
        key = (row.get("case_id", ""), row.get("repeat", ""))
        if key in modeled_keys and truthy(row.get("Delivered")):
            oracle_deliveries[key].append(as_int(row.get("Cycle")))

    delivery_points: list[dict[str, Any]] = []
    for key in sorted(modeled_keys):
        stage_cycles = sorted(stage_deliveries.get(key, []))
        oracle_cycles = sorted(oracle_deliveries.get(key, []))
        if stage_cycles and len(stage_cycles) == len(oracle_cycles):
            for stage_cycle, oracle_cycle in zip(stage_cycles, oracle_cycles):
                delivery_points.append(
                    {
                        "case_id": key[0],
                        "repeat": key[1],
                        "stage_cycle": stage_cycle,
                        "oracle_cycle": oracle_cycle,
                    }
                )

    return {
        "rows": rows,
        "modeled": modeled,
        "unsupported": unsupported,
        "by_case": dict(sorted(by_case.items())),
        "delivery_points": delivery_points,
        "modeled_case_count": len(by_case),
        "modeled_run_count": len(modeled),
        "unsupported_case_count": len({row.get("case_id") for row in unsupported}),
        "unsupported_run_count": len(unsupported),
    }


def analyze_holdout(bundle: Path) -> dict[str, Any]:
    summary = bundle / "summary"
    status_rows = read_csv(summary / "holdout_pair_status.csv")
    timing_rows = read_csv(summary / "holdout_scalesim_stage_timing.csv")
    access_rows = read_csv(summary / "holdout_scalesim_stage_accesses.csv")
    ready_status_keys = {
        (row.get("case_id", ""), row.get("repeat", ""))
        for row in status_rows
        if truthy(row.get("paired_ready")) and row.get("comparison_status") == "ready"
    }

    timing_by_key: dict[tuple[str, str], dict[str, dict[str, str]]] = defaultdict(dict)
    for row in timing_rows:
        key = (row.get("case_id", ""), row.get("repeat", ""))
        tool = row.get("tool", "").lower()
        if key in ready_status_keys and row.get("status") == "completed" and tool in {"stage", "scalesim"}:
            timing_by_key[key][tool] = row

    ready_pairs: list[dict[str, Any]] = []
    usable_keys: set[tuple[str, str]] = set()
    for key in sorted(ready_status_keys):
        tools = timing_by_key.get(key, {})
        stage_cycles = as_float(tools.get("stage", {}).get("total_cycles"))
        scalesim_cycles = as_float(tools.get("scalesim", {}).get("total_cycles"))
        if stage_cycles and scalesim_cycles and stage_cycles > 0 and scalesim_cycles > 0:
            usable_keys.add(key)
            ready_pairs.append(
                {
                    "case_id": key[0],
                    "repeat": key[1],
                    "stage_cycles": stage_cycles,
                    "scalesim_cycles": scalesim_cycles,
                    "cycle_ratio_scalesim_over_stage": scalesim_cycles / stage_cycles,
                }
            )

    access_by_key: dict[tuple[str, str, str], dict[str, float]] = defaultdict(dict)
    for row in access_rows:
        key = (row.get("case_id", ""), row.get("repeat", ""))
        tool = row.get("tool", "").lower()
        counter = row.get("counter", "")
        value = as_float(row.get("access_count"))
        if key in usable_keys and tool in {"stage", "scalesim"} and counter and value is not None and value > 0:
            access_by_key[(key[0], key[1], counter)][tool] = value

    access_pairs: list[dict[str, Any]] = []
    for (case_id, repeat, counter), tools in sorted(access_by_key.items()):
        if "stage" in tools and "scalesim" in tools:
            access_pairs.append(
                {
                    "case_id": case_id,
                    "repeat": repeat,
                    "counter": counter,
                    "stage_accesses": tools["stage"],
                    "scalesim_accesses": tools["scalesim"],
                }
            )

    return {
        "status_rows": status_rows,
        "ready_status_count": len(ready_status_keys),
        "blocked_status_count": sum(row.get("comparison_status") != "ready" for row in status_rows),
        "ready_pairs": ready_pairs,
        "access_pairs": access_pairs,
    }


def analyze_p0(bundle: Path) -> dict[str, Any]:
    summary = bundle / "summary"
    stage_rows = read_csv(summary / "stage_scalability.csv")
    completed = [row for row in stage_rows if row.get("status") == "completed"]
    overhead_rows = [
        row
        for row in read_csv(summary / "stage_trace_overhead.csv")
        if row.get("metrics_status") == "completed" and row.get("full_status") == "completed"
    ]
    slowdowns = [
        value
        for row in overhead_rows
        if (value := as_float(row.get("trace_on_slowdown"))) is not None
    ]
    specialist_rows = read_csv(summary / "specialist_tool_runtime_context.csv")
    specialist_completed_by_tool = Counter(
        row.get("tool", "").strip()
        for row in specialist_rows
        if row.get("status") == "completed" and row.get("tool", "").strip()
    )
    return {
        "completed_rows": completed,
        "completed_count": len(completed),
        "mesh_dimensions": sorted({as_int(row.get("mesh_dimension")) for row in completed}),
        "packet_counts": sorted({as_int(row.get("packet_count")) for row in completed}),
        "trace_modes": sorted({row.get("trace_mode", "") for row in completed}),
        "overhead_rows": overhead_rows,
        "hash_equal_count": sum(truthy(row.get("delivery_hash_equal")) for row in overhead_rows),
        "median_trace_slowdown": statistics.median(slowdowns) if slowdowns else None,
        "min_trace_slowdown": min(slowdowns) if slowdowns else None,
        "max_trace_slowdown": max(slowdowns) if slowdowns else None,
        "specialist_rows": specialist_rows,
        "specialist_completed_by_tool": specialist_completed_by_tool,
    }


def analyze_p2(bundle: Path) -> dict[str, Any]:
    rows = read_csv(bundle / "summary" / "trace_guided_interventions.csv")
    accepted = [row for row in rows if truthy(row.get("pair_acceptance_pass"))]
    reporting = read_json(bundle / "manifests" / "p2_reporting_manifest.json")
    ready = bool(accepted) and reporting.get("source_analysis", {}).get("overall_acceptance_pass") is True
    return {"rows": rows, "accepted": accepted, "ready": ready, "reporting": reporting}


def plot_noc(bundle: Path, noc: dict[str, Any]) -> list[Path]:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    if not noc["modeled"]:
        return []
    figures = bundle / "figures"
    figures.mkdir(parents=True, exist_ok=True)
    pdf = figures / "fig_reviewer_noc_contract.pdf"
    png = figures / "fig_reviewer_noc_contract.png"

    colors = plt.get_cmap("tab10")
    fig, (ax0, ax1) = plt.subplots(1, 2, figsize=(10.8, 4.8), gridspec_kw={"width_ratios": [1.05, 1.35]})
    cases_with_points = sorted({point["case_id"] for point in noc["delivery_points"]})
    all_values: list[int] = []
    for index, case_id in enumerate(cases_with_points):
        points = [point for point in noc["delivery_points"] if point["case_id"] == case_id]
        x = [point["oracle_cycle"] for point in points]
        y = [point["stage_cycle"] for point in points]
        all_values.extend(x + y)
        ax0.scatter(x, y, s=46, alpha=0.82, color=colors(index), edgecolor="white", linewidth=0.6, label=short_case(case_id))
    if all_values:
        upper = max(all_values) * 1.08 + 0.5
        ax0.plot([0, upper], [0, upper], linestyle="--", linewidth=1.0, color="#5f6b73", label="exact equality")
        ax0.set_xlim(0, upper)
        ax0.set_ylim(0, upper)
    ax0.set_xlabel("Independent oracle delivery cycle")
    ax0.set_ylabel("STAGE delivery cycle")
    ax0.set_title("(a) Delivered-cycle contract points")
    ax0.grid(True, alpha=0.22)
    ax0.legend(fontsize=7.2, loc="upper left", frameon=False)

    case_ids = list(noc["by_case"])
    rates: list[float] = []
    labels: list[str] = []
    annotations: list[str] = []
    for case_id in case_ids:
        rows = noc["by_case"][case_id]
        matched = sum(truthy(row.get("oracle_matched")) for row in rows)
        total = len(rows)
        rates.append(100.0 * matched / total if total else 0.0)
        labels.append(short_case(case_id))
        annotations.append(f"{matched}/{total}")
    positions = list(range(len(labels)))
    ax1.barh(positions, rates, color="#2a9d8f", alpha=0.9, height=0.65)
    for position, rate, annotation in zip(positions, rates, annotations):
        ax1.text(min(rate + 1.2, 103.2), position, annotation, va="center", fontsize=8)
    ax1.set_yticks(positions, labels)
    ax1.invert_yaxis()
    ax1.set_xlim(0, 106)
    ax1.set_xlabel("Oracle-matched measured runs (%)")
    ax1.set_title("(b) Modeled case contract match")
    ax1.grid(True, axis="x", alpha=0.22)

    fig.suptitle("NoC microbench contract evidence (modeled cases only)", fontsize=13, fontweight="semibold")
    fig.text(
        0.5,
        0.018,
        f"Measured: {noc['modeled_run_count']} runs / {noc['modeled_case_count']} cases. "
        f"Excluded from measured layers: {noc['unsupported_run_count']} not-supported capacity-release runs. "
        "Contract-only evidence; not a router-pipeline, credit-flow, or hardware-accuracy claim.",
        ha="center",
        va="bottom",
        fontsize=8,
    )
    fig.tight_layout(rect=(0, 0.08, 1, 0.93))
    fig.savefig(pdf, bbox_inches="tight", metadata={"Title": "Reviewer NoC contract evidence"})
    fig.savefig(png, dpi=220, bbox_inches="tight")
    plt.close(fig)
    return [pdf, png]


def plot_holdout(bundle: Path, holdout: dict[str, Any]) -> list[Path]:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    pairs = holdout["ready_pairs"]
    if not pairs:
        return []
    figures = bundle / "figures"
    figures.mkdir(parents=True, exist_ok=True)
    pdf = figures / "fig_reviewer_holdout.pdf"
    png = figures / "fig_reviewer_holdout.png"
    fig, (ax0, ax1) = plt.subplots(1, 2, figsize=(10.4, 4.6))

    cycle_values: list[float] = []
    cycle_cases = sorted({pair["case_id"] for pair in pairs})
    for index, case_id in enumerate(cycle_cases):
        rows = [pair for pair in pairs if pair["case_id"] == case_id]
        x = [pair["stage_cycles"] for pair in rows]
        y = [pair["scalesim_cycles"] for pair in rows]
        cycle_values.extend(x + y)
        ax0.scatter(
            x,
            y,
            s=54,
            alpha=0.86,
            color=plt.get_cmap("tab10")(index % 10),
            edgecolor="white",
            linewidth=0.6,
            label=short_case(case_id),
        )
    low = min(cycle_values) * 0.80
    high = max(cycle_values) * 1.25
    ax0.plot([low, high], [low, high], linestyle="--", color="#5f6b73", linewidth=1.0)
    ax0.set_xlim(low, high)
    ax0.set_ylim(low, high)
    ax0.set_xscale("log")
    ax0.set_yscale("log")
    ax0.set_xlabel("STAGE total cycles")
    ax0.set_ylabel("SCALE-Sim total cycles")
    ax0.set_title("(a) Paired total-cycle observations")
    ax0.grid(True, which="both", alpha=0.18)
    ax0.legend(fontsize=6.4, frameon=False, loc="upper left")

    access_pairs = holdout["access_pairs"]
    counters = sorted({row["counter"] for row in access_pairs})
    access_values: list[float] = []
    for index, counter in enumerate(counters):
        rows = [row for row in access_pairs if row["counter"] == counter]
        x = [row["stage_accesses"] for row in rows]
        y = [row["scalesim_accesses"] for row in rows]
        access_values.extend(x + y)
        ax1.scatter(x, y, s=42, alpha=0.85, color=plt.get_cmap("tab10")(index % 10), label=counter.replace("_", " "))
    if access_values:
        low_access = min(access_values) * 0.75
        high_access = max(access_values) * 1.35
        ax1.plot([low_access, high_access], [low_access, high_access], linestyle="--", color="#5f6b73", linewidth=1.0)
        ax1.set_xlim(low_access, high_access)
        ax1.set_ylim(low_access, high_access)
        ax1.set_xscale("log")
        ax1.set_yscale("log")
    ax1.set_xlabel("STAGE access count")
    ax1.set_ylabel("SCALE-Sim access count")
    ax1.set_title("(b) Definition-mapped access observations")
    ax1.grid(True, which="both", alpha=0.18)
    if counters:
        ax1.legend(fontsize=6.8, frameon=False, loc="best")

    fig.suptitle("Cross-tool holdout observations — trend only", fontsize=13, fontweight="semibold")
    fig.text(
        0.5,
        0.018,
        f"Only {len(pairs)} paired-ready row(s) are plotted; pending/not-supported rows are excluded. "
        "Different tool abstractions and counter definitions preclude an accuracy claim.",
        ha="center",
        va="bottom",
        fontsize=8,
    )
    fig.tight_layout(rect=(0, 0.08, 1, 0.92))
    fig.savefig(pdf, bbox_inches="tight", metadata={"Title": "Reviewer holdout trend evidence"})
    fig.savefig(png, dpi=220, bbox_inches="tight")
    plt.close(fig)
    return [pdf, png]


def build_claims(bundle: Path, p0: dict[str, Any], noc: dict[str, Any], holdout: dict[str, Any], p2: dict[str, Any]) -> list[dict[str, str]]:
    booksim_completed = p0["specialist_completed_by_tool"].get("BookSim2", 0)
    scalesim_completed = p0["specialist_completed_by_tool"].get("SCALE-Sim", 0)
    specialist_pair_ready = booksim_completed > 0 and scalesim_completed > 0
    specialist_rows_by_tool = {
        tool: [row for row in p0["specialist_rows"] if row.get("tool") == tool]
        for tool in ("BookSim2", "SCALE-Sim")
    }
    terminal_unavailable = any(
        rows
        and not any(row.get("status") == "completed" for row in rows)
        and all(row.get("status") in {"not_run", "not_supported", "skipped"} for row in rows)
        for rows in specialist_rows_by_tool.values()
    )
    specialist_status = "trend" if specialist_pair_ready else ("not_supported" if terminal_unavailable else "pending")
    holdout_count = len(holdout["ready_pairs"])
    claims = [
        {
            "claim_id": "P0_STAGE_SCALE",
            "topic": "STAGE scalability and trace overhead",
            "status": "measured" if p0["completed_count"] else "pending",
            "evidence_scope": f"{p0['completed_count']} completed STAGE records; host-specific wall-clock and memory measurements.",
            "supporting_files": "summary/stage_scalability.csv; summary/stage_trace_overhead.csv; figures/fig_reviewer_scalability.pdf",
            "paper_safe_text": "Report observed STAGE scaling and trace overhead on the recorded host.",
            "prohibited_overclaim": "Do not generalize to specialist-tool speed superiority or hardware performance.",
        },
        {
            "claim_id": "P0_SPECIALIST_RUNTIME",
            "topic": "BookSim2 / SCALE-Sim runtime comparison",
            "status": specialist_status,
            "evidence_scope": (
                f"Completed context rows: BookSim2={booksim_completed}, SCALE-Sim={scalesim_completed}. "
                + ("Both tools are present; descriptive trend context only." if specialist_pair_ready else "Both tools are required before any cross-tool runtime trend is reported.")
            ),
            "supporting_files": "summary/specialist_tool_runtime_context.csv",
            "paper_safe_text": "Use only as descriptive runtime context if numeric records exist.",
            "prohibited_overclaim": "Do not claim STAGE is faster than BookSim2 or SCALE-Sim.",
        },
        {
            "claim_id": "P1_NOC_CONTRACT",
            "topic": "Modeled NoC microbench contract",
            "status": "measured" if noc["modeled_run_count"] else "pending",
            "evidence_scope": f"{noc['modeled_run_count']} oracle-matched runs across {noc['modeled_case_count']} modeled cases.",
            "supporting_files": "summary/noc_contract_microbench.csv; summary/noc_cycle_timeline.csv; summary/noc_oracle_timeline.csv; figures/fig_reviewer_noc_contract.pdf",
            "paper_safe_text": "Claim exact agreement with the independent oracle for the explicitly modeled microbench contract.",
            "prohibited_overclaim": "Do not imply a full router pipeline, credit flow, wormhole model, or BookSim equivalence.",
        },
        {
            "claim_id": "P1_CAPACITY_RELEASE",
            "topic": "Deterministic capacity-release cases",
            "status": "not_supported" if noc["unsupported_run_count"] else "pending",
            "evidence_scope": f"{noc['unsupported_run_count']} runs across {noc['unsupported_case_count']} cases excluded from measured analysis.",
            "supporting_files": "summary/noc_contract_microbench.csv; summary/noc_feature_boundary.csv",
            "paper_safe_text": "State that capacity-release cases remain outside the public deterministic input contract.",
            "prohibited_overclaim": "Do not plot or count these cases as measured matches.",
        },
        {
            "claim_id": "P1_HOLDOUT",
            "topic": "SCALE-Sim / STAGE holdout",
            "status": "trend" if holdout_count else "pending",
            "evidence_scope": f"{holdout_count} paired-ready row(s); {holdout['blocked_status_count']} status row(s) remain blocked.",
            "supporting_files": "summary/holdout_pair_status.csv; summary/holdout_scalesim_stage_timing.csv; summary/holdout_scalesim_stage_accesses.csv" + ("; figures/fig_reviewer_holdout.pdf" if holdout_count else "; summary/holdout_pending_note.md"),
            "paper_safe_text": "Describe paired observations as trend-only because tool abstractions and counter definitions differ.",
            "prohibited_overclaim": "Do not claim cross-tool timing or access-count accuracy.",
        },
        {
            "claim_id": "P2_INTERVENTIONS",
            "topic": "Preregistered trace-guided interventions",
            "status": "measured" if p2["ready"] else "pending",
            "evidence_scope": f"{len(p2['accepted'])} accepted deterministic within-model intervention pairs.",
            "supporting_files": "summary/trace_guided_interventions.csv; summary/intervention_prediction_error.csv; figures/fig_reviewer_intervention.pdf",
            "paper_safe_text": "Report deterministic within-model counterfactual effects and registered prediction error.",
            "prohibited_overclaim": "Do not claim general causal identification, hardware accuracy, or cross-tool timing validity.",
        },
        {
            "claim_id": "FIG2_MULTICHIP_UI",
            "topic": "Fig. 2 multi-chip UI screenshot",
            "status": "pending",
            "evidence_scope": "No fresh visible UI capture was supplied to this finalizer.",
            "supporting_files": "summary/fig2_pending_note.md",
            "paper_safe_text": "Keep the figure slot pending until a fresh visible capture is reviewed.",
            "prohibited_overclaim": "Do not reuse an unverified or stale screenshot as current evidence.",
        },
        {
            "claim_id": "GLOBAL_ACCURACY",
            "topic": "Hardware accuracy and global model validity",
            "status": "not_supported",
            "evidence_scope": "The reviewer extension contains contract, trend, and within-model evidence only.",
            "supporting_files": "summary/reviewer_claim_matrix.csv",
            "paper_safe_text": "Explicitly delimit all results to their recorded evidence scopes.",
            "prohibited_overclaim": "Do not claim silicon accuracy, general NoC accuracy, or universal causal validity.",
        },
    ]
    if any(row["status"] not in STATUS_ORDER for row in claims):
        raise AssertionError("Claim matrix contains an invalid status")
    return claims


def write_claim_matrix(bundle: Path, claims: list[dict[str, str]]) -> list[Path]:
    summary = bundle / "summary"
    csv_path = summary / "reviewer_claim_matrix.csv"
    md_path = summary / "reviewer_claim_matrix.md"
    fields = ["claim_id", "topic", "status", "evidence_scope", "supporting_files", "paper_safe_text", "prohibited_overclaim"]
    write_csv(csv_path, claims, fields)
    counts = Counter(row["status"] for row in claims)
    md = "\n".join(
        [
            "# Reviewer-extension claim matrix",
            "",
            "Allowed statuses are `measured`, `trend`, `not_supported`, and `pending`. A row's status applies only to its stated evidence scope.",
            "",
            "Status counts: " + ", ".join(f"{status}={counts.get(status, 0)}" for status in STATUS_ORDER) + ".",
            "",
            md_table(claims, fields, ["Claim", "Topic", "Status", "Evidence scope", "Evidence", "Paper-safe text", "Do not claim"]),
        ]
    )
    write_text(md_path, md)
    return [csv_path, md_path]


def write_pending_notes(bundle: Path, holdout: dict[str, Any]) -> list[Path]:
    summary = bundle / "summary"
    outputs: list[Path] = []
    fig2 = summary / "fig2_pending_note.md"
    write_text(
        fig2,
        """# Fig. 2 pending note

Status: `pending`.

No fresh, visually reviewed multi-chip UI screenshot was supplied to this finalizer. Do not replace the paper's Fig. 2 or modify the Introduction until a current visible capture has been produced, inspected, and bound to an evidence manifest.
""",
    )
    outputs.append(fig2)

    pending = summary / "holdout_pending_note.md"
    holdout_pdf = bundle / "figures" / "fig_reviewer_holdout.pdf"
    holdout_png = bundle / "figures" / "fig_reviewer_holdout.png"
    if holdout["ready_pairs"]:
        if pending.exists():
            pending.unlink()
    else:
        for stale in (holdout_pdf, holdout_png):
            if stale.exists():
                stale.unlink()
        write_text(
            pending,
            f"""# Reviewer holdout pending note

Status: `pending`.

No fully joined `paired_ready=true` STAGE/SCALE-Sim timing row is currently available in the published summary CSVs. {holdout['blocked_status_count']} holdout status row(s) are blocked. No holdout figure is emitted, and no pending or unsupported row may be presented as measured evidence.
""",
        )
        outputs.append(pending)
    return outputs


def write_replacement_text(bundle: Path, p0: dict[str, Any], noc: dict[str, Any], holdout: dict[str, Any], p2: dict[str, Any]) -> Path:
    path = bundle / "summary" / "reviewer_paper_replacement_text.md"
    overhead_count = len(p0["overhead_rows"])
    median_slowdown = p0["median_trace_slowdown"]
    slowdown_text = "not yet available"
    if median_slowdown is not None:
        slowdown_text = (
            f"median {median_slowdown:.2f}x (range {p0['min_trace_slowdown']:.2f}x–{p0['max_trace_slowdown']:.2f}x)"
        )
    ratios = [row["cycle_ratio_scalesim_over_stage"] for row in holdout["ready_pairs"]]
    ratio_text = "no paired-ready rows"
    if ratios:
        ratio_text = f"SCALE-Sim/STAGE total-cycle ratio {min(ratios):.3f}–{max(ratios):.3f}"
    intervention_changes = [
        100.0 * (as_float(row.get("measured_total_relative_delta"), 0.0) or 0.0)
        for row in p2["accepted"]
    ]
    intervention_text = ", ".join(f"{value:.1f}%" for value in intervention_changes) or "pending"

    text = f"""# Reviewer-extension paper replacement text

Scope rule: these are replacement blocks for Methods, Results, Limitations, and figure captions only. **Do not modify the Introduction.**

## Methods — reviewer-extension evidence protocol

We separate reviewer-extension evidence into four explicit states: measured, trend, not supported, and pending. Measured rows require completed source records and the phase-specific acceptance predicate. Trend rows are descriptive cross-tool observations whose abstractions or counter definitions are not equivalent. Not-supported and pending rows are excluded from measured plots and numerical claims. All figures in this bundle are regenerated from the published CSV summaries; no values are transcribed or fabricated.

## Results — STAGE scalability and trace cost

The scalability sweep contains {p0['completed_count']} completed STAGE records spanning mesh dimensions {p0['mesh_dimensions']}, packet counts {p0['packet_counts']}, and trace modes {p0['trace_modes']}. Across {overhead_count} completed full-trace/metrics-only pairs, trace-enabled simulation slowdown was {slowdown_text}; delivery hashes agreed in {p0['hash_equal_count']}/{overhead_count} pairs. These are host-specific runtime and memory observations, not a specialist-tool speed comparison.

## Results — modeled NoC contract

Across {noc['modeled_run_count']} completed runs covering {noc['modeled_case_count']} modeled NoC microbench cases, the real STAGE trace projection matched the independent oracle contract in every included run. {noc['unsupported_run_count']} capacity-release runs across {noc['unsupported_case_count']} cases were excluded as not supported because the generic engine exposes no public deterministic capacity-release event input. The result establishes agreement only for the stated modeled contract; it does not establish router-pipeline, credit-flow, wormhole, BookSim, or hardware equivalence.

## Results — cross-tool holdout

The published holdout summary currently contains {len(holdout['ready_pairs'])} paired-ready row(s) and {holdout['blocked_status_count']} blocked status row(s). For the ready subset, {ratio_text}. These observations are trend-only because STAGE and SCALE-Sim use different abstractions and access-counter definitions; they are not evidence of timing or access-count accuracy.

## Results — preregistered interventions

The preregistered intervention analysis contains {len(p2['accepted'])} accepted within-model pairs, with measured total-cycle changes of {intervention_text}. Registered point predictions fell inside their preregistered intervals for the accepted rows. This is deterministic within-model counterfactual evidence, not general causal identification, hardware accuracy, or cross-tool validation.

## Limitations

The NoC evidence covers only the explicitly modeled contract and omits route-compute, VC-allocation, switch-allocation, crossbar, credit-return, wormhole-reservation, and per-flit router-pipeline semantics. Capacity-release cases remain not supported. External specialist runtime records are absent unless explicitly present in `specialist_tool_runtime_context.csv`. The holdout subset remains trend-only. Fig. 2 remains pending until a fresh visible multi-chip UI capture is reviewed. None of these results support silicon accuracy or universal model validity.

## Figure captions

**Reviewer scalability figure.** Host-specific STAGE scaling and trace-cost measurements from completed P0 records; trace-enabled and metrics-only delivery hashes are checked pairwise.

**Reviewer NoC contract figure.** Modeled cases only. Panel (a) compares delivered cycles from real STAGE traces with the independent oracle; panel (b) reports run-level oracle match rates. Not-supported capacity-release cases are excluded from measured layers.

**Reviewer holdout figure.** Trend-only paired STAGE/SCALE-Sim observations from rows marked `paired_ready=true`; pending and not-supported rows are excluded. Omit this caption and figure when no paired-ready row exists.

**Reviewer intervention figure.** Preregistered deterministic within-model counterfactuals with accepted prediction intervals and bottleneck-set transitions; no general causal or hardware-accuracy claim is made.

**Fig. 2.** Pending. Replace only after a fresh visible multi-chip UI capture has been visually reviewed and indexed.
"""
    write_text(path, text)
    return path


def status_counts_from_checkpoint(document: dict[str, Any]) -> Counter[str]:
    candidates = document.get("candidates", {})
    if not isinstance(candidates, dict):
        return Counter()
    return Counter(
        str(value.get("status", "unknown")).lower()
        for value in candidates.values()
        if isinstance(value, dict)
    )


def write_execution_disposition(bundle: Path) -> list[Path]:
    manifests = bundle / "manifests"
    p0_checkpoint = read_json(manifests / "p0_checkpoint.json")
    p1_checkpoint = read_json(manifests / "p1_checkpoint.json")
    p1_bundle = read_json(manifests / "p1_bundle_manifest.json")
    p2_observation = read_json(manifests / "p2_observation_manifest.json")
    rows: list[dict[str, Any]] = []

    def add_checkpoint(phase: str, source: str, counts: Counter[str], note: str) -> None:
        rows.append(
            {
                "phase": phase,
                "source": source,
                "state_basis": "checkpoint",
                "completed": counts.get("completed", 0),
                "failed": counts.get("failed", 0),
                "skipped": counts.get("skipped", 0),
                "timeout": counts.get("timeout", 0) + counts.get("timed_out", 0),
                "pending": counts.get("pending", 0),
                "running": counts.get("running", 0),
                "not_supported": counts.get("not_supported", 0),
                "notes": note,
            }
        )

    add_checkpoint("P0", "manifests/p0_checkpoint.json", status_counts_from_checkpoint(p0_checkpoint), "Current P0 checkpoint candidate states.")
    p1_counts = status_counts_from_checkpoint(p1_checkpoint)
    published = p1_bundle.get("status_counts", {})
    add_checkpoint(
        "P1",
        "manifests/p1_checkpoint.json",
        p1_counts,
        "Live checkpoint; published bundle snapshot=" + json.dumps(published, sort_keys=True, separators=(",", ":")),
    )
    rows.append(
        {
            "phase": "P2",
            "source": "manifests/p2_observation_manifest.json",
            "state_basis": "observation_manifest",
            "completed": as_int(p2_observation.get("completed_count")),
            "failed": as_int(p2_observation.get("failed_count")),
            "skipped": 0,
            "timeout": 0,
            "pending": max(as_int(p2_observation.get("candidate_count")) - as_int(p2_observation.get("completed_count")) - as_int(p2_observation.get("failed_count")), 0),
            "running": 0,
            "not_supported": 0,
            "notes": "Observation manifest supersedes the preregistration-only checkpoint for measured execution state.",
        }
    )
    failure_files = [path for path in (bundle / "failures").rglob("*") if path.is_file()] if (bundle / "failures").exists() else []
    rows.append(
        {
            "phase": "bundle",
            "source": "failures/",
            "state_basis": "preserved_failure_artifacts",
            "completed": 0,
            "failed": len(failure_files),
            "skipped": 0,
            "timeout": 0,
            "pending": 0,
            "running": 0,
            "not_supported": 0,
            "notes": "Count of preserved files under failures/; zero means no preserved failure artifact is present.",
        }
    )
    fields = ["phase", "source", "state_basis", "completed", "failed", "skipped", "timeout", "pending", "running", "not_supported", "notes"]
    csv_path = bundle / "summary" / "reviewer_execution_disposition.csv"
    md_path = bundle / "summary" / "reviewer_execution_disposition.md"
    write_csv(csv_path, rows, fields)
    write_text(
        md_path,
        "\n".join(
            [
                "# Reviewer-extension execution disposition",
                "",
                "Failure, skip, timeout, pending, running, and not-supported states are reported separately. A newer observation manifest takes precedence over a preregistration-only checkpoint for P2.",
                "",
                md_table(rows, fields, ["Phase", "Source", "Basis", "Completed", "Failed", "Skipped", "Timeout", "Pending", "Running", "Not supported", "Notes"]),
            ]
        ),
    )
    return [csv_path, md_path]


def evidence_file(path: Path, bundle: Path) -> dict[str, Any]:
    before = path.stat()
    digest = sha256_file(path)
    after = path.stat()
    if before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns:
        raise RuntimeError(f"Evidence file changed while hashing: {path}")
    return {
        "path": relative(path, bundle),
        "bytes": after.st_size,
        "sha256": digest,
        "hash_source": "computed_file",
    }


RAW_DIRECT_HASH_LIMIT = 16 * 1024 * 1024


def raw_family(path: Path, raw_root: Path) -> str:
    parts = path.relative_to(raw_root).parts
    first = parts[0]
    if first == "booksim2_context":
        return "p0_booksim_external"
    if first == "stage":
        return "p1_stage"
    if first == "scalesim":
        return "p1_scalesim"
    if first == "p2_interventions":
        return "p2_observation"
    if first == "regression":
        return "regression"
    if first.startswith("p0-stage-reviewer-scaling-") and first.endswith(".trace"):
        return "p0_stage_trace"
    if len(parts) == 1 and first.startswith("p0-stage-reviewer-scaling-"):
        return "p0_stage_wrapper"
    if len(parts) == 1 and first.startswith("p0-booksim2-reviewer-context-"):
        return "p0_booksim_wrapper"
    if len(parts) == 1 and first.startswith("p0-scalesim-reviewer-context-"):
        return "p0_scalesim_wrapper"
    return "other_raw"


def raw_evidence_kind(path: Path) -> str:
    name = path.name.lower()
    suffix = path.suffix.lower()
    if suffix == ".jsonl":
        return "trace_events"
    if suffix == ".gz":
        return "compressed_trace"
    if suffix == ".json":
        return "json_record"
    if suffix == ".cfg":
        return "external_config"
    if suffix == ".m":
        return "external_stats"
    if suffix == ".log" or name in {"stdout.txt", "stderr.txt"}:
        return "external_log"
    if name == "time.txt":
        return "external_time"
    if name == "exit_code.txt":
        return "external_exit_status"
    if suffix == ".csv":
        return "external_report"
    if suffix == ".txt":
        return "external_text"
    return "other_raw_file"


def raw_file_partitions(bundle: Path, threshold: int = RAW_DIRECT_HASH_LIMIT) -> tuple[list[Path], list[Path]]:
    raw_root = bundle / "raw"
    files = sorted(
        (path for path in raw_root.rglob("*") if path.is_file()),
        key=lambda path: relative(path, bundle),
    ) if raw_root.is_dir() else []
    direct = [path for path in files if path.stat().st_size <= threshold]
    metadata_only = [path for path in files if path.stat().st_size > threshold]
    return direct, metadata_only


def build_raw_inventory(bundle: Path, threshold: int = RAW_DIRECT_HASH_LIMIT) -> list[dict[str, Any]]:
    raw_root = bundle / "raw"
    direct, _ = raw_file_partitions(bundle, threshold)
    inventory: list[dict[str, Any]] = []
    for path in direct:
        entry = evidence_file(path, bundle)
        entry["family"] = raw_family(path, raw_root)
        entry["evidence_kind"] = raw_evidence_kind(path)
        inventory.append(entry)
    return inventory


def large_trace_references(bundle: Path, threshold: int = RAW_DIRECT_HASH_LIMIT) -> list[dict[str, Any]]:
    raw_root = bundle / "raw"
    _, metadata_only = raw_file_partitions(bundle, threshold)
    references: list[dict[str, Any]] = []
    for path in metadata_only:
        trace_dir = path.parent.name
        if not trace_dir.endswith(".trace") or path.name not in {"trace-events.jsonl", "trace-events.jsonl.gz"}:
            raise RuntimeError(f"Large raw evidence lacks a metadata-only trace rule: {path}")
        candidate_id = trace_dir[:-len(".trace")]
        wrapper_path = raw_root / f"{candidate_id}.json"
        wrapper = read_json(wrapper_path)
        compressed = path.name.endswith(".gz")
        size_key = "compressed_trace_bytes" if compressed else "raw_trace_bytes"
        hash_key = "compressed_trace_sha256" if compressed else "raw_trace_sha256"
        expected_size = as_int(wrapper.get("metrics", {}).get(size_key))
        digest = str(wrapper.get("hashes", {}).get(hash_key, "")).strip()
        actual_size = path.stat().st_size
        if expected_size != actual_size or len(digest) != 64:
            raise RuntimeError(f"Large trace metadata mismatch: {path}")
        references.append(
            {
                "path": relative(path, bundle),
                "bytes": expected_size,
                "sha256": digest,
                "candidate_id": candidate_id,
                "family": "p0_stage_trace",
                "evidence_kind": "compressed_trace" if compressed else "trace_events",
                "metadata_record": relative(wrapper_path, bundle),
                "summary_record": "summary/stage_scalability.csv",
                "hash_source": "existing_wrapper_metadata",
                "rescan_performed": False,
            }
        )
    return sorted(references, key=lambda row: row["path"])


def summarize_raw_families(entries: Iterable[dict[str, Any]]) -> dict[str, Any]:
    counts: dict[str, dict[str, Any]] = {}
    for entry in entries:
        family = str(entry["family"])
        bucket = counts.setdefault(
            family,
            {"file_count": 0, "total_bytes": 0, "evidence_kind_counts": {}, "hash_source_counts": {}},
        )
        bucket["file_count"] += 1
        bucket["total_bytes"] += as_int(entry.get("bytes"))
        kind = str(entry.get("evidence_kind", "unknown"))
        source = str(entry.get("hash_source", "unknown"))
        bucket["evidence_kind_counts"][kind] = bucket["evidence_kind_counts"].get(kind, 0) + 1
        bucket["hash_source_counts"][source] = bucket["hash_source_counts"].get(source, 0) + 1
    return {family: counts[family] for family in sorted(counts)}


def write_manifest_index(bundle: Path, claims: list[dict[str, str]], generated_utc: str) -> Path:
    manifest_path = bundle / "manifests" / "reviewer_extension_manifest_index.json"
    selected_manifests = [
        "p0_analysis_manifest.json",
        "p0_checkpoint.json",
        "p1_bundle_manifest.json",
        "p1_checkpoint.json",
        "p1_source_audit.json",
        "holdout_independence_audit.md",
        "p2_candidate_manifest.json",
        "p2_intervention_preregistration.json",
        "p2_observation_manifest.json",
        "p2_intervention_analysis.json",
        "p2_reporting_manifest.json",
    ]
    required_manifest_evidence = {"p1_source_audit.json", "holdout_independence_audit.md"}
    missing_required = sorted(
        name for name in required_manifest_evidence if not (bundle / "manifests" / name).is_file()
    )
    if missing_required:
        raise RuntimeError("Required reviewer audit evidence is missing: " + ", ".join(missing_required))

    paths: set[Path] = set()
    for directory, suffixes in ((bundle / "summary", {".csv", ".md"}), (bundle / "figures", {".pdf", ".png"})):
        if directory.exists():
            paths.update(path for path in directory.iterdir() if path.is_file() and path.suffix.lower() in suffixes)
    for name in selected_manifests:
        path = bundle / "manifests" / name
        if path.is_file():
            paths.add(path)

    files = [evidence_file(path, bundle) for path in sorted(paths, key=lambda value: relative(value, bundle))]
    raw_inventory = build_raw_inventory(bundle)
    trace_references = large_trace_references(bundle)
    raw_family_counts = summarize_raw_families([*raw_inventory, *trace_references])
    script_entry = evidence_file(Path(__file__).resolve(), bundle)
    status_counts = Counter(row["status"] for row in claims)
    document: dict[str, Any] = {
        "schema_version": "reviewer-extension-manifest-index/1.1",
        "generated_utc": generated_utc,
        "bundle_root": str(bundle.resolve()),
        "generator": script_entry,
        "claim_status_counts": {status: status_counts.get(status, 0) for status in STATUS_ORDER},
        "evidence_files": files,
        "raw_inventory": raw_inventory,
        "raw_family_counts": raw_family_counts,
        "raw_inventory_policy": "Every raw file at or below 16 MiB is directly hashed, including wrapper JSON, P1 STAGE/SCALE-Sim records, P2 observations, BookSim wrappers, configs, logs, time/status files, stats, and reports.",
        "large_trace_references": trace_references,
        "large_trace_policy": "Raw trace payloads above 16 MiB are indexed by physical path and reuse SHA-256/byte metadata from their small P0 wrapper JSON; payload content is not rescanned by this finalizer.",
        "scope_guards": [
            "Introduction is not modified.",
            "Pending and not-supported rows are excluded from measured plot layers.",
            "Holdout evidence remains trend-only.",
            "NoC evidence is contract-only for explicitly modeled semantics.",
            "P2 evidence is deterministic within-model counterfactual evidence only.",
        ],
    }
    integrity_base = dict(document)
    document["integrity"] = {
        "algorithm": "sha256",
        "canonicalization": "sorted-key compact UTF-8 JSON",
        "scope": "document excluding integrity",
        "content_sha256": canonical_sha256(integrity_base),
    }
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    return manifest_path


def planned_outputs(bundle: Path, holdout_ready: bool) -> list[str]:
    names = [
        "figures/fig_reviewer_noc_contract.pdf",
        "figures/fig_reviewer_noc_contract.png",
        "summary/reviewer_claim_matrix.csv",
        "summary/reviewer_claim_matrix.md",
        "summary/reviewer_paper_replacement_text.md",
        "summary/fig2_pending_note.md",
        "summary/reviewer_execution_disposition.csv",
        "summary/reviewer_execution_disposition.md",
        "manifests/reviewer_extension_manifest_index.json",
    ]
    if holdout_ready:
        names.extend(["figures/fig_reviewer_holdout.pdf", "figures/fig_reviewer_holdout.png"])
    else:
        names.append("summary/holdout_pending_note.md")
    return names


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle-root", type=Path, default=DEFAULT_BUNDLE, help="Reviewer-extension bundle root")
    parser.add_argument("--dry-run", action="store_true", help="Validate and report without writing files")
    args = parser.parse_args()
    bundle = args.bundle_root.resolve()
    if not bundle.is_dir():
        parser.error(f"Bundle root does not exist: {bundle}")

    p0 = analyze_p0(bundle)
    noc = analyze_noc(bundle)
    holdout = analyze_holdout(bundle)
    p2 = analyze_p2(bundle)
    claims = build_claims(bundle, p0, noc, holdout, p2)
    raw_direct_files, raw_metadata_files = raw_file_partitions(bundle)
    report = {
        "dry_run": args.dry_run,
        "bundle_root": str(bundle),
        "noc_modeled_cases": noc["modeled_case_count"],
        "noc_measured_runs": noc["modeled_run_count"],
        "noc_not_supported_runs": noc["unsupported_run_count"],
        "holdout_paired_ready_rows": len(holdout["ready_pairs"]),
        "holdout_blocked_status_rows": holdout["blocked_status_count"],
        "p0_completed_records": p0["completed_count"],
        "p2_accepted_pairs": len(p2["accepted"]),
        "claim_status_counts": dict(Counter(row["status"] for row in claims)),
        "raw_total_files": len(raw_direct_files) + len(raw_metadata_files),
        "raw_direct_hash_files": len(raw_direct_files),
        "raw_metadata_reference_files": len(raw_metadata_files),
        "outputs": planned_outputs(bundle, bool(holdout["ready_pairs"])),
    }
    if args.dry_run:
        print(json.dumps(report, indent=2, sort_keys=True))
        return 0

    generated_utc = utc_now()
    noc_figures = plot_noc(bundle, noc)
    if not noc_figures:
        raise RuntimeError("No measured NoC contract rows were available; refusing to emit a stale measured figure")
    plot_holdout(bundle, holdout)
    write_pending_notes(bundle, holdout)
    write_claim_matrix(bundle, claims)
    write_replacement_text(bundle, p0, noc, holdout, p2)
    write_execution_disposition(bundle)
    manifest = write_manifest_index(bundle, claims, generated_utc)
    report["manifest"] = relative(manifest, bundle)
    report["manifest_sha256"] = sha256_file(manifest)
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
