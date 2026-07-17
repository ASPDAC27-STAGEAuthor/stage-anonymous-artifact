#!/usr/bin/env python3
"""Run the installed WSL external baselines for the first ASP-DAC draft.

The runner is external-tool only. All STAGE columns are deliberately blank.
"""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import json
import math
import os
import re
import shutil
import statistics
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

BOOKSIM = Path("/opt/stage-baselines/tools/booksim2/src/booksim")
BOOKSIM_REPO = BOOKSIM.parents[1]
TIMELOOP = Path("/opt/stage-tools/bin/timeloop-mapper")
TIMELOOP_REPO = Path("/opt/stage-baselines/tools/accelergy-timeloop-infrastructure")
TIMELOOP_LIB = TIMELOOP_REPO / "src/timeloop/lib"
ACCELERGY = Path("/opt/stage-tools/bin/accelergy")
SCALESIM_REPO = Path("/opt/stage-baselines/tools/SCALE-Sim")
SCALESIM_PYTHON = Path("/opt/stage-baselines/venv/bin/python")

RATES = [0.005, 0.010, 0.020, 0.040, 0.060, 0.080, 0.100, 0.120,
         0.160, 0.200, 0.240, 0.280, 0.320]
SEEDS = list(range(10))
TRAFFIC = {
    "uniform": "uniform",
    "transpose": "transpose",
    "bit_complement": "bitcomp",
    "hotspot_node5": "hotspot({5})",
}
WORKLOADS = [
    ("gemm_256", "GEMM 256^3", 256, 256, 256,
     "experiments/aspdac/results/timeloop_wsl/gemm_256_v1/gemm_256_16pe.yaml"),
    ("mlp_l1", "MLP L1", 128, 256, 512,
     "experiments/aspdac/results/timeloop_wsl/mlp_attention_v1/mlp_512_256_batch128_layer1/mlp_512_256_batch128_layer1.yaml"),
    ("mlp_l2", "MLP L2", 128, 128, 256,
     "experiments/aspdac/results/timeloop_wsl/mlp_attention_v1/mlp_512_256_batch128_layer2/mlp_512_256_batch128_layer2.yaml"),
    ("attention_qk", "Attention QK^T", 128, 128, 64,
     "experiments/aspdac/results/timeloop_wsl/mlp_attention_v1/attention_128_64_qk/attention_128_64_qk.yaml"),
    ("attention_pv", "Attention PV", 128, 64, 128,
     "experiments/aspdac/results/timeloop_wsl/mlp_attention_v1/attention_128_64_prob_v/attention_128_64_prob_v.yaml"),
]


def run(command, cwd, timeout=600, env=None):
    started = time.perf_counter()
    cp = subprocess.run(command, cwd=str(cwd), env=env, text=True,
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                        timeout=timeout, check=False)
    return cp, time.perf_counter() - started


def write_csv(path, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        raise RuntimeError(f"No rows for {path}")
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]), extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def git_rev(path):
    cp, _ = run(["git", "rev-parse", "HEAD"], path, timeout=30)
    return cp.stdout.strip() if cp.returncode == 0 else "unknown"


def tl_env():
    env = dict(os.environ)
    env["PATH"] = "/opt/stage-tools/bin:" + env.get("PATH", "")
    env["LD_LIBRARY_PATH"] = ":".join([
        str(TIMELOOP_LIB), "/usr/local/lib", "/usr/lib/x86_64-linux-gnu",
        env.get("LD_LIBRARY_PATH", ""),
    ])
    return env


def last_float(pattern, text):
    values = re.findall(pattern, text, flags=re.MULTILINE)
    return float(values[-1]) if values else None


def booksim_config(traffic, rate, seed):
    return f"""topology = mesh;
k = 4;
n = 2;
routing_function = dim_order;
num_vcs = 1;
vc_buf_size = 16;
wait_for_tail_credit = 0;
traffic = {traffic};
injection_rate = {rate:.3f};
packet_size = 1;
seed = {seed};
sim_type = latency;
sample_period = 1000;
warmup_periods = 3;
max_samples = 10;
"""


def run_booksim(out):
    root = out / "booksim"
    configs, logs = root / "configs", root / "logs"
    configs.mkdir(parents=True, exist_ok=True)
    logs.mkdir(parents=True, exist_ok=True)
    rows = []
    for traffic_name, traffic_value in TRAFFIC.items():
        for rate in RATES:
            for seed in SEEDS:
                token = f"{traffic_name}_inj_{rate:.3f}_seed_{seed}"
                cfg = configs / f"{token}.cfg"
                cfg.write_text(booksim_config(traffic_value, rate, seed),
                               encoding="utf-8", newline="\n")
                cp, elapsed = run([str(BOOKSIM), str(cfg)], configs, timeout=120)
                combined = cp.stdout + "\n--- STDERR ---\n" + cp.stderr
                with gzip.open(logs / f"{token}.log.gz", "wt", encoding="utf-8") as handle:
                    handle.write(combined)
                latency = last_float(r"Packet latency average = ([0-9.eE+-]+)", cp.stdout)
                accepted = last_float(r"Accepted packet rate average = ([0-9.eE+-]+)", cp.stdout)
                injected = last_float(r"Injected packet rate average = ([0-9.eE+-]+)", cp.stdout)
                unstable = "Simulation unstable" in cp.stdout or "exceeded 500 cycles" in cp.stdout
                has_stats = latency is not None and accepted is not None
                rows.append({
                    "traffic": traffic_name,
                    "injection_rate": rate,
                    "seed": seed,
                    "status": "unstable" if unstable else ("completed" if has_stats else "failed"),
                    "return_code": cp.returncode,
                    "packet_latency_avg": latency,
                    "injected_packet_rate_avg": injected,
                    "accepted_packet_rate_avg": accepted,
                    "accepted_offered_ratio": accepted / rate if accepted is not None else None,
                    "elapsed_seconds": round(elapsed, 6),
                    "config_hash": sha256(cfg),
                    "stage_packet_latency_avg": "",
                    "stage_accepted_packet_rate_avg": "",
                })
    summary = []
    for traffic_name in TRAFFIC:
        for rate in RATES:
            group = [r for r in rows if r["traffic"] == traffic_name
                     and r["injection_rate"] == rate and r["packet_latency_avg"] is not None]
            def describe(key):
                values = [float(r[key]) for r in group]
                if not values:
                    return (None, None, None)
                mean = statistics.fmean(values)
                std = statistics.stdev(values) if len(values) > 1 else 0.0
                return mean, std, 1.96 * std / math.sqrt(len(values))
            lat = describe("packet_latency_avg")
            acc = describe("accepted_packet_rate_avg")
            ratio = describe("accepted_offered_ratio")
            summary.append({
                "traffic": traffic_name,
                "injection_rate": rate,
                "runs_with_stats": len(group),
                "packet_latency_mean": lat[0],
                "packet_latency_std": lat[1],
                "packet_latency_ci95": lat[2],
                "accepted_rate_mean": acc[0],
                "accepted_rate_std": acc[1],
                "accepted_rate_ci95": acc[2],
                "accepted_offered_ratio_mean": ratio[0],
                "unstable_fraction": (
                    sum(r["status"] == "unstable" for r in group) / len(group)
                    if group else None
                ),
                "stage_packet_latency_mean": "",
                "stage_packet_latency_ci95": "",
                "stage_accepted_rate_mean": "",
                "stage_accepted_rate_ci95": "",
            })
    saturation = []
    for traffic_name in TRAFFIC:
        points = [r for r in summary if r["traffic"] == traffic_name]
        failing = [(float(r["accepted_offered_ratio_mean"]) < 0.95
                    or float(r["unstable_fraction"]) > 0.0) for r in points]
        sat = "not_reached"
        for i in range(len(points) - 1):
            if failing[i] and failing[i + 1]:
                sat = points[i]["injection_rate"]
                break
        saturation.append({
            "traffic": traffic_name,
            "booksim_saturation_rate": sat,
            "definition": "first of two consecutive rates with unstable or accepted/offered < 0.95",
            "stage_saturation_rate": "",
        })
    write_csv(root / "booksim_runs.csv", rows)
    write_csv(root / "booksim_summary.csv", summary)
    write_csv(root / "booksim_saturation.csv", saturation)
    return summary, saturation


def parse_tl_stats(path):
    text = path.read_text(encoding="utf-8", errors="replace")
    summary = text.split("Summary Stats", 1)[-1]
    result = {
        "cycles": last_float(r"^Cycles:\s+([0-9.eE+-]+)", summary),
        "utilization_pct": last_float(r"^Utilization:\s+([0-9.eE+-]+)%", summary),
        "energy_uj_uncalibrated": last_float(r"^Energy:\s+([0-9.eE+-]+) uJ", summary),
        "computes": last_float(r"^Computes\s*=\s*([0-9.eE+-]+)", summary),
    }
    for level, key in [("Registers", "register_accesses"),
                       ("LocalBuffer", "local_buffer_accesses"),
                       ("GlobalBuffer", "global_buffer_accesses"),
                       ("DRAM", "dram_accesses")]:
        match = re.search(rf"=== {level} ===\s+Total scalar accesses\s+:\s+([0-9.eE+-]+)",
                          text, flags=re.MULTILINE)
        result[key] = float(match.group(1)) if match else None
    return result


def run_timeloop(repo, out):
    root = out / "timeloop"
    root.mkdir(parents=True, exist_ok=True)
    rows = []
    for case_id, label, m, n, k, rel_cfg in WORKLOADS:
        case = root / case_id
        case.mkdir(parents=True, exist_ok=True)
        source = repo / rel_cfg
        cfg = case / source.name
        shutil.copy2(source, cfg)
        cp, elapsed = run([str(TIMELOOP), str(cfg)], case, timeout=600, env=tl_env())
        (case / "timeloop.stdout.log").write_text(
            cp.stdout + "\n--- STDERR ---\n" + cp.stderr, encoding="utf-8", newline="\n")
        stats = case / "timeloop-mapper.stats.txt"
        metrics = parse_tl_stats(stats) if stats.exists() else {}
        macs = m * n * k
        rows.append({
            "case_id": case_id, "label": label, "M": m, "N": n, "K": k,
            "expected_macs": macs, "analytical_16mac_floor_cycles": macs // 16,
            "status": "completed" if cp.returncode == 0 and metrics else "failed",
            "return_code": cp.returncode, "elapsed_seconds": round(elapsed, 6),
            "config_hash": sha256(cfg), **metrics,
            "stage_compute_only_cycles": "", "stage_full_system_cycles": "",
            "stage_utilization_pct": "",
        })
    write_csv(root / "timeloop_summary.csv", rows)
    return rows


def read_report(path):
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return [{k.strip(): v.strip() for k, v in row.items() if k is not None}
                for row in csv.DictReader(handle)]


def run_scalesim(repo, out):
    root = out / "scalesim"
    root.mkdir(parents=True, exist_ok=True)
    old = repo / "experiments/aspdac/results/scalesim_wsl/core_mnk_v1"
    cfg, topology = root / "aspdac_4x4_ws.cfg", root / "aspdac_core_mnk.csv"
    shutil.copy2(old / cfg.name, cfg)
    shutil.copy2(old / topology.name, topology)
    raw = root / "raw_outputs"
    cp, elapsed = run([
        str(SCALESIM_PYTHON), "-m", "scalesim.scale",
        "-c", str(cfg), "-t", str(topology), "-p", str(raw), "-i", "gemm", "-s", "N"
    ], SCALESIM_REPO, timeout=1800)
    (root / "scalesim.stdout.log").write_text(
        cp.stdout + "\n--- STDERR ---\n" + cp.stderr, encoding="utf-8", newline="\n")
    report = raw / "aspdac_4x4_ws_core_mnk"
    compute = read_report(report / "COMPUTE_REPORT.csv")
    access = read_report(report / "DETAILED_ACCESS_REPORT.csv")
    bandwidth = read_report(report / "BANDWIDTH_REPORT.csv")
    rows = []
    for i, (case_id, label, m, n, k, _) in enumerate(WORKLOADS):
        c, a, b = compute[i], access[i], bandwidth[i]
        rows.append({
            "case_id": case_id, "label": label, "M": m, "N": n, "K": k,
            "expected_macs": m * n * k,
            "status": "completed" if cp.returncode == 0 else "failed",
            "return_code": cp.returncode, "elapsed_seconds_full_run": round(elapsed, 6),
            "total_cycles_incl_prefetch": float(c["Total Cycles (incl. prefetch)"]),
            "total_cycles": float(c["Total Cycles"]),
            "stall_cycles": float(c["Stall Cycles"]),
            "overall_util_pct": float(c["Overall Util %"]),
            "mapping_efficiency_pct": float(c["Mapping Efficiency %"]),
            "compute_util_pct": float(c["Compute Util %"]),
            "sram_ifmap_reads": float(a["SRAM IFMAP Reads"]),
            "sram_filter_reads": float(a["SRAM Filter Reads"]),
            "sram_ofmap_writes": float(a["SRAM OFMAP Writes"]),
            "dram_ifmap_reads": float(a["DRAM IFMAP Reads"]),
            "dram_filter_reads": float(a["DRAM Filter Reads"]),
            "dram_ofmap_writes": float(a["DRAM OFMAP Writes"]),
            "avg_ifmap_dram_bw": float(b["Avg IFMAP DRAM BW"]),
            "avg_filter_dram_bw": float(b["Avg FILTER DRAM BW"]),
            "avg_ofmap_dram_bw": float(b["Avg OFMAP DRAM BW"]),
            "stage_total_cycles": "", "stage_stall_cycles": "",
            "stage_overall_util_pct": "",
        })
    traces = list(report.rglob("*TRACE.csv"))
    trace_bytes = sum(p.stat().st_size for p in traces)
    for path in traces:
        path.unlink()
    (root / "run_metadata.json").write_text(json.dumps({
        "elapsed_seconds": elapsed,
        "trace_files_removed_after_summary": len(traces),
        "trace_bytes_removed_after_summary": trace_bytes,
        "source_patch": "existing local np.max compatibility patch",
    }, indent=2) + "\n", encoding="utf-8")
    write_csv(root / "scalesim_summary.csv", rows)
    return rows


def run_accelergy(repo, out):
    root = out / "accelergy"
    root.mkdir(parents=True, exist_ok=True)
    source = repo / "experiments/aspdac/baseline_tools/timeloop_accelergy/gemm_256_16pe_accelergy.yaml"
    cfg = root / source.name
    shutil.copy2(source, cfg)
    cp, elapsed = run([str(TIMELOOP), str(cfg)], root, timeout=600, env=tl_env())
    combined = cp.stdout + "\n--- STDERR ---\n" + cp.stderr
    (root / "timeloop_accelergy.stdout.log").write_text(
        combined, encoding="utf-8", newline="\n")
    ert_files = sorted(root.glob("*.ERT.yaml"))
    ert_text = "\n".join(
        p.read_text(encoding="utf-8", errors="replace") for p in ert_files)
    accelergy_text = "\n".join(
        p.read_text(encoding="utf-8", errors="replace")
        for p in sorted(root.glob("*.accelergy.log")))
    empty = bool(re.search(r"tables:\s*\[\s*\]", ert_text))
    table_count = len(re.findall(r"^\s{6}- name:", ert_text, flags=re.MULTILINE))
    schema = "key not found: tables" in combined
    dummy = "dummy estimated" in accelergy_text
    stats = root / "timeloop-mapper.stats.txt"
    metrics = parse_tl_stats(stats) if stats.exists() else {}
    completed = (
        cp.returncode == 0 and not empty and table_count == 5
        and not schema and not dummy and bool(metrics)
    )
    status = "completed" if completed else (
        "failed_schema_compatibility" if empty or schema else "failed")
    result = {
        "tool": "Timeloop+Accelergy", "status": status,
        "return_code": cp.returncode, "elapsed_seconds": round(elapsed, 6),
        "empty_ert_tables": empty, "ert_table_count": table_count,
        "schema_error_key_not_found_tables": schema,
        "dummy_action_estimate": dummy,
        "ert_files": ";".join(p.name for p in ert_files),
        "cycles": metrics.get("cycles"),
        "energy_uj": metrics.get("energy_uj_uncalibrated"),
        "stage_energy_status": "",
        "paper_use": ("reference-model energy under matched assumptions"
                      if completed else "omit numerical cross-tool energy validation"),
        "claim_boundary": ("CACTI/Aladdin reference-model energy; not "
                           "silicon-calibrated absolute power evidence"),
    }
    write_csv(root / "accelergy_status.csv", [result])
    (root / "accelergy_status.json").write_text(
        json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


def write_oracle(out):
    rows = [
        ("optical_loss_1mm", "route_loss", 4.11, "dB"),
        ("optical_power_0dbm_minus_4p11db", "received_power", -4.11, "dBm"),
        ("optical_margin_minus3dbm", "receiver_margin", -1.11, "dB"),
        ("serdes_128bit_64b66b", "encoded_bits", 132, "bit"),
        ("serdes_132bit_at_128bit_per_cycle", "link_service_cycles", 2, "cycle"),
    ]
    data = [{"case_id": c, "metric": m, "reference_value": v, "unit": u,
             "stage_value": ""} for c, m, v, u in rows]
    write_csv(out / "analytical_oracle/analytical_oracle.csv", data)
    return data


def write_combined(out, bs, tl, ss, ae, oracle):
    rows = []
    for r in bs:
        for metric, unit in [("packet_latency_mean", "cycle"),
                             ("accepted_rate_mean", "packet/node/cycle")]:
            rows.append({"rq": "RQ2", "tool": "BookSim2",
                         "case_id": f"{r['traffic']}@{float(r['injection_rate']):.3f}",
                         "metric": metric, "external_value": r[metric], "unit": unit,
                         "stage_value": "", "evidence": "trend_pending_matched_stage"})
    for r in tl:
        for metric, unit in [("cycles", "cycle"), ("utilization_pct", "%")]:
            rows.append({"rq": "RQ3", "tool": "Timeloop", "case_id": r["case_id"],
                         "metric": metric, "external_value": r.get(metric), "unit": unit,
                         "stage_value": "", "evidence": "reference_pending_matched_stage"})
    for r in ss:
        for metric, unit in [("total_cycles", "cycle"), ("stall_cycles", "cycle"),
                             ("overall_util_pct", "%")]:
            rows.append({"rq": "RQ3", "tool": "SCALE-Sim", "case_id": r["case_id"],
                         "metric": metric, "external_value": r[metric], "unit": unit,
                         "stage_value": "", "evidence": "trend_pending_matched_stage"})
    rows.append({"rq": "RQ3-energy", "tool": "Timeloop+Accelergy",
                 "case_id": "gemm_256", "metric": "integration_status",
                 "external_value": ae["status"], "unit": "status", "stage_value": "",
                 "evidence": "failed_or_conditional"})
    for r in oracle:
        rows.append({"rq": "RQ1/RQ4", "tool": "Independent analytical oracle",
                     "case_id": r["case_id"], "metric": r["metric"],
                     "external_value": r["reference_value"], "unit": r["unit"],
                     "stage_value": "", "evidence": "exact_pending_stage_bundle"})
    write_csv(out / "combined_external_results.csv", rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    args = parser.parse_args()
    repo = Path(args.repo_root).resolve()
    out = repo / "experiments/aspdac/results/external_draft_20260716"
    out.mkdir(parents=True, exist_ok=True)
    missing = [str(p) for p in [BOOKSIM, TIMELOOP, ACCELERGY, SCALESIM_PYTHON]
               if not p.exists()]
    if missing:
        raise SystemExit("Missing tools: " + ", ".join(missing))
    started = datetime.now(timezone.utc).isoformat()
    bs, saturation = run_booksim(out)
    tl = run_timeloop(repo, out)
    ss = run_scalesim(repo, out)
    ae = run_accelergy(repo, out)
    oracle = write_oracle(out)
    write_combined(out, bs, tl, ss, ae, oracle)
    manifest = {
        "schema": "aspdac.external_draft_run.v1",
        "started_at_utc": started,
        "completed_at_utc": datetime.now(timezone.utc).isoformat(),
        "stage_columns": "intentionally blank",
        "parameters": {
            "booksim_rates": RATES, "booksim_seeds": SEEDS,
            "booksim_traffic": TRAFFIC,
            "booksim_topology": "4x4 mesh; XY; 1 VC; 16 flits/VC; one-flit packet",
            "timeloop": "16 arithmetic instances; current mapper decks rerun",
            "scalesim": "4x4 WS; 8 KiB each SRAM; bandwidth 8,8,8; dense",
        },
        "tool_versions": {
            "booksim2_git": git_rev(BOOKSIM_REPO),
            "timeloop_infrastructure_git": git_rev(TIMELOOP_REPO),
            "scalesim_git": git_rev(SCALESIM_REPO),
            "accelergy_executable": str(ACCELERGY),
        },
        "result_counts": {
            "booksim_aggregate_points": len(bs),
            "booksim_saturation_rows": len(saturation),
            "timeloop_cases": len(tl), "scalesim_cases": len(ss),
            "analytical_cases": len(oracle),
        },
        "limitations": [
            "STAGE columns are blank and no cross-tool agreement is claimed.",
            "Timeloop mapper decks were rerun; freeze selected maps for final evidence.",
            "SCALE-Sim uses the existing local NumPy compatibility patch.",
            "Optical values are arithmetic references, not a photonic simulator baseline.",
        ],
    }
    (out / "external_run_manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"counts": manifest["result_counts"], "accelergy": ae}, indent=2))


if __name__ == "__main__":
    main()