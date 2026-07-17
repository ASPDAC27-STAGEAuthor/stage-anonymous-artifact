#!/usr/bin/env python3
"""Build tidy ASP-DAC final summaries and manifest-indexed vector figures."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import statistics
from pathlib import Path
from typing import Any

import matplotlib.pyplot as plt


REPO = Path(__file__).resolve().parents[3]
BUNDLE = REPO / "experiments/aspdac/results/final_20260716"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def repo_path(path: Path) -> str:
    return path.resolve().relative_to(REPO).as_posix()


def write_csv(path: Path, rows: list[dict[str, Any]], fields: list[str] | None = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = fields or sorted({key for row in rows for key in row})
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def load_plan_results(plan_name: str, bundle: Path | None = None) -> tuple[list[dict[str, Any]], list[Path]]:
    bundle = bundle or BUNDLE
    manifest_path = bundle / "manifests" / f"candidates_{plan_name}.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    expected = {item["candidate_id"] for item in manifest["candidates"]}
    raw_root = bundle / "raw"
    results: list[dict[str, Any]] = []
    paths: list[Path] = [manifest_path]
    for path in sorted(raw_root.glob("*/*.json")):
        payload = json.loads(path.read_text(encoding="utf-8"))
        if payload.get("candidate_id") not in expected:
            continue
        results.append(payload)
        paths.append(path)
    found = {item["candidate_id"] for item in results}
    missing = expected - found
    if missing:
        raise RuntimeError(f"Plan {plan_name} is missing {len(missing)} raw result(s): {sorted(missing)[:3]}")
    return results, paths


def rq1() -> None:
    results, inputs = load_plan_results("rq1_exact")
    repeat_rows: list[dict[str, Any]] = []
    grouped: dict[str, list[dict[str, Any]]] = {}
    for result in results:
        metrics = result["metrics"]
        exact = result.get("exact_values") or {}
        row = {
            "case": metrics["case_name"],
            "repeat": metrics["repeat"],
            "passed": metrics["passed"],
            "trace_kind": metrics["trace_kind"],
            "canonical_trace_hash": metrics["canonical_trace_hash"],
            "canonical_trace_bytes": metrics["canonical_trace_bytes"],
            "candidate_id": result["candidate_id"],
            "runner_output_sha256": result["runner_output_sha256"],
        }
        for key, value in sorted(exact.items()):
            row[f"exact_{key}"] = value
        repeat_rows.append(row)
        grouped.setdefault(str(metrics["case_name"]), []).append(row)
    repeat_rows.sort(key=lambda row: (row["case"], int(row["repeat"])))
    case_rows: list[dict[str, Any]] = []
    for case_name, rows in sorted(grouped.items()):
        hashes = {row["canonical_trace_hash"] for row in rows}
        byte_counts = {row["canonical_trace_bytes"] for row in rows}
        if len(rows) < 2 or len(hashes) != 1 or len(byte_counts) != 1:
            raise RuntimeError(f"RQ1 repeat mismatch for {case_name}")
        case_rows.append(
            {
                "case": case_name,
                "evidence_level": "Exact",
                "repeats": len(rows),
                "all_passed": all(bool(row["passed"]) for row in rows),
                "byte_identical": True,
                "canonical_trace_hash": rows[0]["canonical_trace_hash"],
                "canonical_trace_bytes": rows[0]["canonical_trace_bytes"],
                "trace_kind": rows[0]["trace_kind"],
            }
        )
    exact_path = BUNDLE / "summary/rq1_exact_cases.csv"
    repeat_path = BUNDLE / "summary/rq1_repeat_hashes.csv"
    write_csv(exact_path, case_rows)
    write_csv(repeat_path, repeat_rows)

    labels = [row["case"].replace("_", " ") for row in case_rows]
    y_positions = list(range(len(case_rows)))
    fig, axes = plt.subplots(1, 2, figsize=(10.2, 4.8), gridspec_kw={"width_ratios": [1.25, 1]})
    axes[0].barh(y_positions, [1] * len(case_rows), color="#238636", height=0.58)
    axes[0].set_yticks(y_positions, labels)
    axes[0].invert_yaxis()
    axes[0].set_xlim(0, 1.08)
    axes[0].set_xticks([0, 1], ["", "PASS"])
    axes[0].set_title("(a) Independent exact checks")
    axes[0].grid(axis="x", alpha=0.2)
    kinds = ["STAGE cycle runtime", "Independent oracle"]
    counts = [sum(row["trace_kind"] == "stage_cycle_runtime" for row in case_rows), sum(row["trace_kind"] != "stage_cycle_runtime" for row in case_rows)]
    axes[1].bar(kinds, counts, color=["#1f6feb", "#8b5cf6"], width=0.62)
    axes[1].set_ylabel("Exact cases")
    axes[1].set_title("(b) Evidence source")
    axes[1].tick_params(axis="x", rotation=15)
    axes[1].grid(axis="y", alpha=0.2)
    axes[1].text(0.5, 0.92, "2 repeats/case\nbyte-identical SHA-256", transform=axes[1].transAxes, ha="center", va="top", fontsize=10)
    fig.suptitle("RQ1 exact validation and deterministic repeatability")
    fig.tight_layout()
    figure_pdf = BUNDLE / "figures/fig_rq1_exact_validation.pdf"
    figure_png = BUNDLE / "figures/fig_rq1_exact_validation.png"
    figure_pdf.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(figure_pdf, bbox_inches="tight")
    fig.savefig(figure_png, dpi=220, bbox_inches="tight")
    plt.close(fig)

    manifest_path = BUNDLE / "manifests/rq1_output_manifest.json"
    output_paths = [exact_path, repeat_path, figure_pdf, figure_png]
    manifest = {
        "schema_version": "aspg-derived-output-manifest-1.0",
        "rq": "RQ1",
        "inputs": [{"path": repo_path(path), "sha256": sha256(path)} for path in inputs],
        "outputs": [{"path": repo_path(path), "sha256": sha256(path)} for path in output_paths],
        "case_count": len(case_rows),
        "repeat_count": len(repeat_rows),
        "all_byte_identical": all(row["byte_identical"] for row in case_rows),
    }
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"rq": "RQ1", "cases": len(case_rows), "repeats": len(repeat_rows), "all_byte_identical": True}, indent=2))


def _float(value: Any) -> float:
    return float(value) if value not in (None, "") else math.nan


def describe(values: list[float]) -> dict[str, float | int]:
    clean = [float(value) for value in values if math.isfinite(float(value))]
    if not clean:
        return {"n": 0, "mean": math.nan, "std": math.nan, "ci95_low": math.nan, "ci95_high": math.nan}
    mean = statistics.fmean(clean)
    if len(clean) == 1:
        return {"n": 1, "mean": mean, "std": 0.0, "ci95_low": mean, "ci95_high": mean}
    std = statistics.stdev(clean)
    t95 = 2.262157 if len(clean) == 10 else 2.776445 if len(clean) == 5 else 1.96
    half = t95 * std / math.sqrt(len(clean))
    return {"n": len(clean), "mean": mean, "std": std, "ci95_low": mean - half, "ci95_high": mean + half}


def _read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def _stage_raw_path(result: dict[str, Any]) -> Path:
    return BUNDLE / "raw" / str(result["experiment_name"]) / f"{result['candidate_id']}.json"


def _booksim_local_paths(row: dict[str, str], root_name: str) -> tuple[str, str]:
    root = BUNDLE / "raw" / root_name
    stem = f"{row['traffic']}_inj_{float(row['injection_rate']):0.3f}_seed_{row['seed']}"
    config_name = Path(row.get("config_path") or f"{stem}.cfg").name
    log_name = Path(row.get("log_path") or f"{stem}.log.gz").name
    return repo_path(root / "configs" / config_name), repo_path(root / "logs" / log_name)


def _saturation(rows: list[dict[str, Any]], tool: str, traffic: str) -> dict[str, Any]:
    selected = [row for row in rows if row["tool"] == tool and row["traffic"] == traffic]
    by_rate: dict[float, list[dict[str, Any]]] = {}
    for row in selected:
        by_rate.setdefault(float(row["injection_rate"]), []).append(row)
    rates = sorted(by_rate)
    states: list[dict[str, Any]] = []
    for rate in rates:
        group = by_rate[rate]
        ratio = describe([float(row["accepted_offered_ratio"]) for row in group])
        unstable_fraction = statistics.fmean(float(bool(row["unstable"])) for row in group)
        states.append({"rate": rate, "mean_ratio": float(ratio["mean"]), "unstable_fraction": unstable_fraction, "failing": unstable_fraction > 0.0 or float(ratio["mean"]) < 0.95})
    saturation_rate: float | None = None
    for current, following in zip(states, states[1:]):
        if current["failing"] and following["failing"]:
            saturation_rate = float(current["rate"])
            break
    bound = rates[-1]
    return {"saturation_rate": saturation_rate, "saturation_label": f"{saturation_rate:g}" if saturation_rate is not None else f">{bound:g}", "tested_min": rates[0], "tested_max": bound, "tested_rate_count": len(rates), "rate_states": states}


def rq2() -> None:
    stage_results, stage_inputs = load_plan_results("rq2_booksim_stage")
    tidy_rows: list[dict[str, Any]] = []
    for result in stage_results:
        metrics = result["metrics"]
        raw_path = _stage_raw_path(result)
        parameters = result["resolved"]["parameters"]
        tidy_rows.append({
            "tool": "STAGE", "traffic": result["axes"].get("traffic", parameters["traffic"]), "injection_rate": result["axes"].get("injection_rate", parameters["injection_rate"]), "seed": result["axes"].get("seed", parameters["seed"]),
            "evidence_level": "Numerical", "measurement_kind": result["measurement_kind"], "status": result["status"],
            "unstable": bool(metrics["unstable"]), "timeout": bool(metrics["timeout"]), "offered_rate_avg": metrics["offered_rate_avg"],
            "accepted_rate_avg": metrics["accepted_rate_avg"], "accepted_offered_ratio": metrics["accepted_offered_ratio"],
            "packet_latency_avg": metrics["packet_latency_avg"], "packet_latency_p95": metrics["packet_latency_p95"],
            "queue_occupancy_avg_flits": metrics["queue_occupancy_avg_flits"], "queue_occupancy_max_flits": metrics["queue_occupancy_max_flits"],
            "congestion_cycles": metrics["congestion_cycles"], "router_conflict_stalls": metrics["router_conflict_stalls"],
            "backpressure_cycles": metrics["backpressure_cycles"], "injection_queue_stalls": metrics["injection_queue_stalls"],
            "config_hash": result["config_hash"], "candidate_id": result["candidate_id"], "raw_path": repo_path(raw_path),
            "raw_sha256": sha256(raw_path), "config_path": "", "log_path": "",
        })

    registered_csv = BUNDLE / "raw/external_booksim_registered/booksim_runs.csv"
    extension_csv = BUNDLE / "summary/rq2_booksim_uniform_extension_raw.csv"
    for root_name, source_csv in [("external_booksim_registered", registered_csv), ("external_booksim_uniform_extension", extension_csv)]:
        for row in _read_csv(source_csv):
            config_path, log_path = _booksim_local_paths(row, root_name)
            config_file, log_file = REPO / config_path, REPO / log_path
            tidy_rows.append({
                "tool": "BookSim2", "traffic": row["traffic"], "injection_rate": _float(row["injection_rate"]), "seed": int(row["seed"]),
                "evidence_level": "Numerical", "measurement_kind": "booksim2_packet_cycle_runtime", "status": row["status"],
                "unstable": row["status"].lower() == "unstable", "timeout": row["status"].lower() == "timeout",
                "offered_rate_avg": _float(row["injected_packet_rate_avg"]), "accepted_rate_avg": _float(row["accepted_packet_rate_avg"]),
                "accepted_offered_ratio": _float(row["accepted_offered_ratio"]), "packet_latency_avg": _float(row["packet_latency_avg"]),
                "packet_latency_p95": math.nan, "queue_occupancy_avg_flits": math.nan, "queue_occupancy_max_flits": math.nan,
                "congestion_cycles": math.nan, "router_conflict_stalls": math.nan, "backpressure_cycles": math.nan,
                "injection_queue_stalls": math.nan, "config_hash": row["config_hash"], "candidate_id": "",
                "raw_path": repo_path(source_csv), "raw_sha256": sha256(source_csv), "config_path": config_path, "log_path": log_path,
                "config_sha256": sha256(config_file), "log_sha256": sha256(log_file),
            })
    tidy_rows.sort(key=lambda row: (row["traffic"], float(row["injection_rate"]), row["tool"], int(row["seed"])))
    tidy_path = BUNDLE / "summary/rq2_booksim_stage_sweep.csv"
    write_csv(tidy_path, tidy_rows)

    grouped: dict[tuple[str, str, float], list[dict[str, Any]]] = {}
    for row in tidy_rows:
        grouped.setdefault((row["tool"], row["traffic"], float(row["injection_rate"])), []).append(row)
    rate_rows: list[dict[str, Any]] = []
    for (tool, traffic, rate), rows in sorted(grouped.items()):
        latency = describe([float(row["packet_latency_avg"]) for row in rows])
        accepted = describe([float(row["accepted_rate_avg"]) for row in rows])
        ratio = describe([float(row["accepted_offered_ratio"]) for row in rows])
        rate_rows.append({
            "tool": tool, "traffic": traffic, "injection_rate": rate, "seeds": len(rows),
            "packet_latency_mean": latency["mean"], "packet_latency_std": latency["std"],
            "packet_latency_ci95_low": latency["ci95_low"], "packet_latency_ci95_high": latency["ci95_high"],
            "accepted_rate_mean": accepted["mean"], "accepted_rate_std": accepted["std"],
            "accepted_rate_ci95_low": accepted["ci95_low"], "accepted_rate_ci95_high": accepted["ci95_high"],
            "accepted_offered_ratio_mean": ratio["mean"], "accepted_offered_ratio_std": ratio["std"],
            "accepted_offered_ratio_ci95_low": ratio["ci95_low"], "accepted_offered_ratio_ci95_high": ratio["ci95_high"],
            "unstable_fraction": statistics.fmean(float(bool(row["unstable"])) for row in rows),
            "timeout_fraction": statistics.fmean(float(bool(row["timeout"])) for row in rows),
        })
    rate_path = BUNDLE / "summary/rq2_rate_summary.csv"
    write_csv(rate_path, rate_rows)

    traffics = ["uniform", "transpose", "bit_complement", "hotspot_node5"]
    saturation_rows: list[dict[str, Any]] = []
    saturation_details: dict[tuple[str, str], dict[str, Any]] = {}
    for traffic in traffics:
        booksim, stage = _saturation(tidy_rows, "BookSim2", traffic), _saturation(tidy_rows, "STAGE", traffic)
        saturation_details[("BookSim2", traffic)], saturation_details[("STAGE", traffic)] = booksim, stage
        both_numeric = booksim["saturation_rate"] is not None and stage["saturation_rate"] is not None
        gap = float(stage["saturation_rate"]) - float(booksim["saturation_rate"]) if both_numeric else math.nan
        saturation_rows.append({
            "traffic": traffic, "criterion": "first of two consecutive tested rates with any unstable seed or mean accepted/offered < 0.95",
            "booksim_saturation": booksim["saturation_label"], "booksim_tested_max": booksim["tested_max"], "booksim_tested_rate_count": booksim["tested_rate_count"],
            "stage_saturation": stage["saturation_label"], "stage_tested_max": stage["tested_max"], "stage_tested_rate_count": stage["tested_rate_count"],
            "stage_minus_booksim_saturation": gap, "ordering_match_eligible": both_numeric, "evidence_level": "Numerical",
        })
    saturation_path = BUNDLE / "summary/rq2_saturation_summary.csv"
    write_csv(saturation_path, saturation_rows)

    failure_rows: list[dict[str, Any]] = []
    successful_ids = {row["candidate_id"]: row for row in tidy_rows if row["tool"] == "STAGE"}
    failure_paths = sorted((BUNDLE / "failures").glob("*.json"))
    for failure_path in failure_paths:
        failure = json.loads(failure_path.read_text(encoding="utf-8"))
        candidate_id = failure["candidate"]["candidate_id"]
        success = successful_ids.get(candidate_id)
        failure_rows.append({
            "candidate_id": candidate_id, "traffic": failure["candidate"]["axes"]["traffic"],
            "injection_rate": failure["candidate"]["axes"]["injection_rate"], "seed": failure["candidate"]["axes"]["seed"],
            "failure_disposition": "superseded_by_completed_result" if success else "unresolved",
            "failure_class": "concurrent_manager_file_lock" if success else failure["exception_type"],
            "failure_path": repo_path(failure_path), "failure_sha256": sha256(failure_path),
            "completed_raw_path": success["raw_path"] if success else "", "completed_raw_sha256": success["raw_sha256"] if success else "",
        })
    failure_disposition_path = BUNDLE / "summary/rq2_failure_disposition.csv"
    write_csv(failure_disposition_path, failure_rows)

    fig, axes = plt.subplots(2, 4, figsize=(13.8, 6.4), sharex="col")
    styles = {"BookSim2": {"color": "#d97706", "marker": "o", "linestyle": "--"}, "STAGE": {"color": "#2563eb", "marker": "s", "linestyle": "-"}}
    titles = {"uniform": "Uniform", "transpose": "Transpose", "bit_complement": "Bit complement", "hotspot_node5": "Hotspot node 5"}
    for column, traffic in enumerate(traffics):
        for tool in ("BookSim2", "STAGE"):
            rows = sorted([row for row in rate_rows if row["tool"] == tool and row["traffic"] == traffic], key=lambda row: float(row["injection_rate"]))
            x = [float(row["injection_rate"]) for row in rows]
            latency = [float(row["packet_latency_mean"]) for row in rows]
            latency_low = [max(0.0, value - float(row["packet_latency_ci95_low"])) for value, row in zip(latency, rows)]
            latency_high = [max(0.0, float(row["packet_latency_ci95_high"]) - value) for value, row in zip(latency, rows)]
            accepted = [float(row["accepted_rate_mean"]) for row in rows]
            accepted_low = [max(0.0, value - float(row["accepted_rate_ci95_low"])) for value, row in zip(accepted, rows)]
            accepted_high = [max(0.0, float(row["accepted_rate_ci95_high"]) - value) for value, row in zip(accepted, rows)]
            axes[0, column].errorbar(x, latency, yerr=[latency_low, latency_high], markersize=3.2, linewidth=1.25, capsize=1.5, label=tool, **styles[tool])
            axes[1, column].errorbar(x, accepted, yerr=[accepted_low, accepted_high], markersize=3.2, linewidth=1.25, capsize=1.5, label=tool, **styles[tool])
        axes[0, column].set_yscale("log")
        axes[0, column].set_title(f"({chr(97 + column)}) {titles[traffic]}")
        axes[0, column].grid(alpha=0.22, which="both")
        axes[1, column].grid(alpha=0.22)
        axes[1, column].set_xlabel("Offered injection rate")
        for tool, color in (("BookSim2", "#d97706"), ("STAGE", "#2563eb")):
            saturation = saturation_details[(tool, traffic)]["saturation_rate"]
            if saturation is not None:
                axes[0, column].axvline(float(saturation), color=color, alpha=0.22, linewidth=1.0)
    axes[0, 0].set_ylabel("Packet latency (cycles, log)")
    axes[1, 0].set_ylabel("Accepted packets/node/cycle")
    handles, labels = axes[0, 0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="upper center", ncol=2, frameon=False, bbox_to_anchor=(0.5, 1.015))
    fig.suptitle("RQ2 BookSim2 and STAGE V-BS packet-cycle sweeps (mean and 95% CI)", y=1.055)
    fig.tight_layout()
    figure_pdf, figure_png = BUNDLE / "figures/fig_rq2_noc_curves.pdf", BUNDLE / "figures/fig_rq2_noc_curves.png"
    fig.savefig(figure_pdf, bbox_inches="tight")
    fig.savefig(figure_png, dpi=220, bbox_inches="tight")
    plt.close(fig)

    external_roots = [BUNDLE / "raw/external_booksim_registered", BUNDLE / "raw/external_booksim_uniform_extension"]
    external_files = sorted(path for root in external_roots for path in root.rglob("*") if path.is_file())
    external_manifest_path = BUNDLE / "manifests/rq2_external_raw_manifest.json"
    external_manifest_path.write_text(json.dumps({
        "schema_version": "aspg-external-raw-manifest-1.0", "source_policy": "immutable copy; original external_draft_20260716 remains unmodified",
        "files": [{"path": repo_path(path), "sha256": sha256(path), "bytes": path.stat().st_size} for path in external_files], "file_count": len(external_files),
    }, indent=2) + "\n", encoding="utf-8")

    output_manifest_path = BUNDLE / "manifests/rq2_output_manifest.json"
    inputs = sorted({path.resolve() for path in [*stage_inputs, registered_csv, extension_csv, *failure_paths, external_manifest_path]})
    outputs = [tidy_path, rate_path, saturation_path, failure_disposition_path, figure_pdf, figure_png]
    output_manifest = {
        "schema_version": "aspg-derived-output-manifest-1.0", "rq": "RQ2",
        "inputs": [{"path": repo_path(path), "sha256": sha256(path)} for path in inputs],
        "outputs": [{"path": repo_path(path), "sha256": sha256(path)} for path in outputs],
        "stage_cases": sum(row["tool"] == "STAGE" for row in tidy_rows), "booksim_cases": sum(row["tool"] == "BookSim2" for row in tidy_rows),
        "administrative_failures_retained": len(failure_rows),
        "administrative_failures_superseded": sum(row["failure_disposition"] == "superseded_by_completed_result" for row in failure_rows),
        "saturation_rule": "first of two consecutive tested rates with any unstable seed or mean accepted/offered < 0.95",
        "limitations": ["Independent PRNG streams require aggregate comparison.", "Router pipeline definitions introduce tool-specific latency offsets.", "Saturation disagreements are retained."],
    }
    output_manifest_path.write_text(json.dumps(output_manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"rq": "RQ2", "stage_cases": output_manifest["stage_cases"], "booksim_cases": output_manifest["booksim_cases"], "saturation": saturation_rows, "retained_failures": len(failure_rows)}, indent=2))
def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rq", choices=("rq1", "rq2"))
    args = parser.parse_args()
    if args.rq == "rq1":
        rq1()
    elif args.rq == "rq2":
        rq2()


if __name__ == "__main__":
    main()

