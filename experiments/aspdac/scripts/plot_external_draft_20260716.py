#!/usr/bin/env python3
"""Aggregate WSL external baselines and render first-draft paper figures."""

from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib.patches import Patch

TRAFFIC_ORDER = ["uniform", "transpose", "bit_complement", "hotspot_node5"]
TRAFFIC_LABEL = {
    "uniform": "Uniform", "transpose": "Transpose",
    "bit_complement": "Bit complement", "hotspot_node5": "Hotspot (node 5)",
}
COLORS = {
    "uniform": "#0072B2", "transpose": "#E69F00",
    "bit_complement": "#009E73", "hotspot_node5": "#D55E00",
}
WORKLOAD_ORDER = ["gemm_256", "mlp_l1", "mlp_l2", "attention_qk", "attention_pv"]
SHORT = {
    "gemm_256": "GEMM", "mlp_l1": "MLP L1", "mlp_l2": "MLP L2",
    "attention_qk": "QK$^T$", "attention_pv": "PV",
}


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def style():
    plt.rcParams.update({
        "font.family": "DejaVu Sans", "font.size": 8.5,
        "axes.titlesize": 9.5, "axes.labelsize": 8.5,
        "xtick.labelsize": 7.5, "ytick.labelsize": 7.5,
        "legend.fontsize": 7.5, "axes.linewidth": 0.7,
        "grid.linewidth": 0.5, "lines.linewidth": 1.6,
        "savefig.dpi": 300, "figure.dpi": 130,
    })


def pending_handle():
    return Patch(facecolor="none", edgecolor="#777777", linestyle="--",
                 linewidth=1.2, label="STAGE pending")


def plot_booksim(bs, figdir):
    fig, axes = plt.subplots(1, 2, figsize=(7.15, 2.75), constrained_layout=True)
    ax_lat, ax_thr = axes
    for traffic in TRAFFIC_ORDER:
        frame = bs[bs["traffic"] == traffic].sort_values("injection_rate")
        x = frame["injection_rate"].to_numpy(float)
        y = frame["packet_latency_mean"].to_numpy(float)
        ci = frame["packet_latency_ci95"].to_numpy(float)
        color, label = COLORS[traffic], TRAFFIC_LABEL[traffic]
        ax_lat.plot(x, y, marker="o", markersize=3.2, color=color, label=label)
        ax_lat.fill_between(x, np.maximum(y-ci, 0.1), y+ci,
                            color=color, alpha=0.13, linewidth=0)
        unstable = frame["unstable_fraction"].to_numpy(float) > 0
        if unstable.any():
            ax_lat.scatter(x[unstable], y[unstable], marker="x", s=22,
                           color=color, linewidths=1.0, zorder=4)
        ay = frame["accepted_rate_mean"].to_numpy(float)
        aci = frame["accepted_rate_ci95"].to_numpy(float)
        ax_thr.plot(x, ay, marker="o", markersize=3.2, color=color, label=label)
        ax_thr.fill_between(x, np.maximum(ay-aci, 0), ay+aci,
                            color=color, alpha=0.13, linewidth=0)
    max_rate = float(bs["injection_rate"].max())
    ax_thr.plot([0, max_rate], [0, max_rate], color="#777777",
                linewidth=1.0, linestyle=":", label="Offered = accepted")
    ax_lat.set_yscale("log")
    ax_lat.set_xlabel("Injection rate (packet/node/cycle)")
    ax_lat.set_ylabel("Average packet latency (cycles, log)")
    ax_lat.set_title("(a) BookSim2 latency; x = unstable")
    ax_thr.set_xlabel("Injection rate (packet/node/cycle)")
    ax_thr.set_ylabel("Accepted rate (packet/node/cycle)")
    ax_thr.set_title("(b) BookSim2 accepted throughput")
    for ax in axes:
        ax.grid(True, which="both", alpha=0.25)
        ax.spines[["top", "right"]].set_visible(False)
    handles, labels = ax_lat.get_legend_handles_labels()
    handles.append(pending_handle())
    labels.append("STAGE pending")
    fig.legend(handles, labels, loc="upper center", ncol=5, frameon=False,
               bbox_to_anchor=(0.5, 1.08))
    fig.savefig(figdir / "fig_rq2_booksim_curves_draft.png", bbox_inches="tight")
    fig.savefig(figdir / "fig_rq2_booksim_curves_draft.pdf", bbox_inches="tight")
    plt.close(fig)


def plot_workloads(tl, ss, figdir):
    merged = tl.merge(ss, on="case_id", suffixes=("_tl", "_ss"))
    merged = merged.set_index("case_id").loc[WORKLOAD_ORDER].reset_index()
    merged["tl_norm"] = merged["cycles"] / merged["analytical_16mac_floor_cycles"]
    merged["ss_norm"] = merged["total_cycles"] / merged["analytical_16mac_floor_cycles"]
    x, width = np.arange(len(merged)), 0.25
    fig, ax = plt.subplots(figsize=(7.15, 2.8), constrained_layout=True)
    b1 = ax.bar(x-width, merged["tl_norm"], width, color="#0072B2", label="Timeloop")
    b2 = ax.bar(x, merged["ss_norm"], width, color="#E69F00", label="SCALE-Sim")
    for xpos in x+width:
        ax.text(xpos, 0.06, "pending", rotation=90, ha="center", va="bottom",
                color="#666666", fontsize=7)
    for bars in [b1, b2]:
        for bar in bars:
            ax.text(bar.get_x()+bar.get_width()/2, bar.get_height()+0.035,
                    f"{bar.get_height():.2f}x", ha="center", va="bottom", fontsize=7)
    ax.set_xticks(x, [SHORT[c] for c in merged["case_id"]])
    ax.set_ylabel("Cycles / 16-MAC analytical floor")
    ax.set_title("External workload timing; STAGE columns intentionally empty")
    ax.set_ylim(0, max(3.0, float(merged["ss_norm"].max())*1.18))
    ax.grid(axis="y", alpha=0.25)
    ax.spines[["top", "right"]].set_visible(False)
    ax.legend(handles=[
        Patch(facecolor="#0072B2", label="Timeloop"),
        Patch(facecolor="#E69F00", label="SCALE-Sim"), pending_handle(),
    ], frameon=False, ncol=3, loc="upper right")
    fig.savefig(figdir / "fig_rq3_workload_cycles_draft.png", bbox_inches="tight")
    fig.savefig(figdir / "fig_rq3_workload_cycles_draft.pdf", bbox_inches="tight")
    plt.close(fig)
    return merged


def plot_scalesim(ss, figdir):
    frame = ss.set_index("case_id").loc[WORKLOAD_ORDER].reset_index()
    frame["stall_fraction"] = frame["stall_cycles"] / frame["total_cycles"]
    x = np.arange(len(frame))
    fig, axes = plt.subplots(1, 2, figsize=(7.15, 2.6), constrained_layout=True)
    axes[0].bar(x, 100*frame["stall_fraction"], color="#D55E00")
    axes[1].bar(x, frame["overall_util_pct"], color="#009E73")
    for ax in axes:
        ax.set_xticks(x, [SHORT[c] for c in frame["case_id"]])
        ax.tick_params(axis="x", rotation=20)
        ax.grid(axis="y", alpha=0.25)
        ax.spines[["top", "right"]].set_visible(False)
    axes[0].set_ylabel("Stall fraction (%)")
    axes[0].set_title("(a) SCALE-Sim stalls")
    axes[1].set_ylabel("Overall utilization (%)")
    axes[1].set_ylim(0, 105)
    axes[1].set_title("(b) SCALE-Sim utilization")
    for ax, values in [(axes[0], 100*frame["stall_fraction"]),
                       (axes[1], frame["overall_util_pct"])]:
        for i, value in enumerate(values):
            ax.text(i, value+1.4, f"{value:.1f}", ha="center", fontsize=7)
    fig.text(0.5, -0.02, "STAGE matched WS results: pending",
             ha="center", color="#666666")
    fig.savefig(figdir / "fig_rq3_scalesim_stalls_draft.png", bbox_inches="tight")
    fig.savefig(figdir / "fig_rq3_scalesim_stalls_draft.pdf", bbox_inches="tight")
    plt.close(fig)


def plot_status(accelergy_status, figdir):
    rows = ["Analytical oracle", "BookSim2", "Timeloop", "SCALE-Sim", "Accelergy", "STAGE"]
    cols = ["RQ1 exact", "RQ2 NoC", "RQ3 compute", "RQ3 array", "RQ3 energy", "RQ4 optical"]
    code = np.zeros((len(rows), len(cols)))
    labels = [["" for _ in cols] for _ in rows]
    def setcell(r, c, value, label):
        code[rows.index(r), cols.index(c)] = value
        labels[rows.index(r)][cols.index(c)] = label
    setcell("Analytical oracle", "RQ1 exact", 2, "ready")
    setcell("Analytical oracle", "RQ4 optical", 1, "functional")
    setcell("BookSim2", "RQ2 NoC", 2, "ready")
    setcell("Timeloop", "RQ3 compute", 2, "ready")
    setcell("SCALE-Sim", "RQ3 array", 2, "ready")
    setcell("Accelergy", "RQ3 energy", -1,
            "failed" if accelergy_status != "completed" else "ready")
    for c in cols:
        setcell("STAGE", c, 0, "pending")
    from matplotlib.colors import ListedColormap, BoundaryNorm
    cmap = ListedColormap(["#D55E00", "#EEEEEE", "#E69F00", "#009E73"])
    norm = BoundaryNorm([-1.5, -0.5, 0.5, 1.5, 2.5], cmap.N)
    fig, ax = plt.subplots(figsize=(7.15, 2.7), constrained_layout=True)
    ax.imshow(code, cmap=cmap, norm=norm, aspect="auto")
    ax.set_xticks(np.arange(len(cols)), cols)
    ax.set_yticks(np.arange(len(rows)), rows)
    ax.tick_params(axis="x", rotation=25)
    for i in range(len(rows)):
        for j in range(len(cols)):
            if labels[i][j]:
                color = "white" if code[i, j] in (-1, 2) else "#333333"
                ax.text(j, i, labels[i][j], ha="center", va="center",
                        fontsize=7, color=color)
    ax.set_title("Draft evidence availability (not cross-tool agreement)")
    for edge in ax.spines.values():
        edge.set_visible(False)
    fig.savefig(figdir / "fig_validation_status_draft.png", bbox_inches="tight")
    fig.savefig(figdir / "fig_validation_status_draft.pdf", bbox_inches="tight")
    plt.close(fig)


def plot_collage(bs, merged, ss, accelergy_status, figdir):
    fig, axes = plt.subplots(2, 2, figsize=(10.5, 7.2), constrained_layout=True)
    ax1, ax2, ax3, ax4 = axes.ravel()
    for traffic in TRAFFIC_ORDER:
        frame = bs[bs["traffic"] == traffic].sort_values("injection_rate")
        x = frame["injection_rate"].to_numpy(float)
        ax1.plot(x, frame["packet_latency_mean"], marker="o", markersize=2.8,
                 color=COLORS[traffic], label=TRAFFIC_LABEL[traffic])
        ax2.plot(x, frame["accepted_rate_mean"], marker="o", markersize=2.8,
                 color=COLORS[traffic])
    ax1.set_yscale("log")
    ax1.set_title("A. BookSim2 latency")
    ax1.set_ylabel("Cycles (log)")
    ax2.plot([0, 0.32], [0, 0.32], ":", color="#777777")
    ax2.set_title("B. BookSim2 accepted throughput")
    ax2.set_ylabel("Packet/node/cycle")
    for ax in [ax1, ax2]:
        ax.set_xlabel("Injection rate")
        ax.grid(True, alpha=0.25)
        ax.spines[["top", "right"]].set_visible(False)
    ax1.legend(frameon=False, ncol=2, fontsize=7)
    x = np.arange(len(merged))
    ax3.bar(x-0.14, merged["tl_norm"], 0.28, color="#0072B2", label="Timeloop")
    ax3.bar(x+0.14, merged["ss_norm"], 0.28, color="#E69F00", label="SCALE-Sim")
    ax3.set_xticks(x, [SHORT[c] for c in merged["case_id"]], rotation=15)
    ax3.set_ylabel("Normalized cycles")
    ax3.set_title("C. Workload timing; STAGE pending")
    ax3.grid(axis="y", alpha=0.25)
    ax3.spines[["top", "right"]].set_visible(False)
    ax3.legend(frameon=False)
    frame = ss.set_index("case_id").loc[WORKLOAD_ORDER].reset_index()
    stall = 100*frame["stall_cycles"]/frame["total_cycles"]
    ax4.bar(x-0.16, frame["overall_util_pct"], 0.32,
            color="#009E73", label="Utilization")
    ax4.bar(x+0.16, stall, 0.32, color="#D55E00", label="Stall fraction")
    ax4.set_xticks(x, [SHORT[c] for c in frame["case_id"]], rotation=15)
    ax4.set_ylabel("Percent")
    ax4.set_title("D. SCALE-Sim; Accelergy " + accelergy_status.replace("_", " "))
    ax4.grid(axis="y", alpha=0.25)
    ax4.spines[["top", "right"]].set_visible(False)
    ax4.legend(frameon=False)
    fig.suptitle("STAGE ASP-DAC external baseline draft — STAGE series intentionally blank",
                 fontsize=12, fontweight="normal")
    fig.savefig(figdir / "external_baseline_draft_collage.png", bbox_inches="tight")
    fig.savefig(figdir / "external_baseline_draft_collage.pdf", bbox_inches="tight")
    plt.close(fig)


def fmt(v, digits=4):
    if isinstance(v, str):
        return v
    if v is None or (isinstance(v, float) and math.isnan(v)):
        return "—"
    return f"{v:.{digits}f}"


def write_summary(result_dir, figdir, bs, sat, ss, accelergy, merged):
    at040 = bs[np.isclose(bs["injection_rate"], 0.04)].copy()
    sat_map = dict(zip(sat["traffic"], sat["booksim_saturation_rate"]))
    energy = pd.read_csv(result_dir / "accelergy/timeloop_accelergy_summary.csv")
    lines = [
        "# External Baseline Draft Summary", "",
        "Generated from the local WSL tools on 2026-07-16. STAGE columns remain empty.", "",
        "## Run coverage", "",
        "- BookSim2: 4 traffic patterns x 13 injection rates x 10 seeds = 520 runs.",
        "- Timeloop: 5 shared matrix workloads.",
        "- SCALE-Sim: 5 shared matrix workloads on the 4x4 WS array.",
        "- Accelergy integration: " + accelergy["status"] + " (5 shared workloads).",
        "- Independent optical/SerDes arithmetic: 5 reference cases.", "",
        "## BookSim2 draft saturation", "",
        "| Traffic | Saturation rate | Mean latency at 0.040 | Mean accepted rate at 0.040 |",
        "|---|---:|---:|---:|",
    ]
    for traffic in TRAFFIC_ORDER:
        row = at040[at040["traffic"] == traffic].iloc[0]
        lines.append(
            f"| {TRAFFIC_LABEL[traffic]} | {sat_map[traffic]} | "
            f"{fmt(row['packet_latency_mean'])} | {fmt(row['accepted_rate_mean'], 6)} |"
        )
    lines += [
        "", "Uniform traffic did not reach the registered saturation condition by injection 0.32.",
        "The explicit hotspot is endpoint 5; this replaces BookSim2's random default hotspot.", "",
        "## Workload references", "",
        "| Kernel | Timeloop cycles | SCALE-Sim cycles | SCALE / analytical floor | SCALE stalls | SCALE utilization |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for _, row in merged.iterrows():
        ssrow = ss[ss["case_id"] == row["case_id"]].iloc[0]
        lines.append(
            f"| {SHORT[row['case_id']]} | {int(row['cycles']):,} | "
            f"{int(row['total_cycles']):,} | {row['ss_norm']:.3f}x | "
            f"{int(ssrow['stall_cycles']):,} | {ssrow['overall_util_pct']:.2f}% |"
        )
    lines += [
        "", "Timeloop reproduced the analytical 16-MAC floor with 100% utilization.",
        "MLP L1 is the clear SCALE-Sim stall/utilization outlier.", "",
        "## Energy status", "",
        "| Kernel | Timeloop+Accelergy energy (uJ) | DRAM scalar accesses | STAGE energy |",
        "|---|---:|---:|---:|",
    ]
    for _, row in energy.set_index("case_id").loc[WORKLOAD_ORDER].reset_index().iterrows():
        lines.append(
            f"| {SHORT[row['case_id']]} | {row['energy_uj']:.2f} | "
            f"{int(row['dram_accesses']):,} | pending |"
        )
    lines += [
        "", "- Status: " + accelergy["status"] + ".",
        "- ERT tables per case: " + str(accelergy["ert_table_count"]) + ".",
        "- Empty ERT tables: " + str(accelergy["empty_ert_tables"]) + ".",
        "- Timeloop schema error: " + str(accelergy["schema_error_key_not_found_tables"]) + ".",
        "- Dummy action estimate: " + str(accelergy["dummy_action_estimate"]) + ".",
        "- Claim boundary: " + accelergy["claim_boundary"] + ".", "",
        "## Draft figures", "",
        "- " + figdir.name + "/fig_rq2_booksim_curves_draft.png",
        "- " + figdir.name + "/fig_rq3_workload_cycles_draft.png",
        "- " + figdir.name + "/fig_rq3_scalesim_stalls_draft.png",
        "- " + figdir.name + "/fig_validation_status_draft.png",
        "- " + figdir.name + "/external_baseline_draft_collage.png", "",
        "All STAGE series are pending; no external value is presented as STAGE.", "",
    ]
    (result_dir / "EXTERNAL_BASELINE_DRAFT_SUMMARY.md").write_text(
        "\n".join(lines), encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--result-dir", required=True)
    parser.add_argument("--figure-dir", required=True)
    args = parser.parse_args()
    result_dir, figdir = Path(args.result_dir).resolve(), Path(args.figure_dir).resolve()
    figdir.mkdir(parents=True, exist_ok=True)
    style()
    bs = pd.read_csv(result_dir/"booksim/booksim_summary.csv")
    sat = pd.read_csv(result_dir/"booksim/booksim_saturation.csv", keep_default_na=False)
    tl = pd.read_csv(result_dir/"timeloop/timeloop_summary.csv")
    ss = pd.read_csv(result_dir/"scalesim/scalesim_summary.csv")
    accelergy = json.loads((result_dir/"accelergy/accelergy_status.json").read_text(encoding="utf-8"))
    plot_booksim(bs, figdir)
    merged = plot_workloads(tl, ss, figdir)
    plot_scalesim(ss, figdir)
    plot_status(accelergy["status"], figdir)
    plot_collage(bs, merged, ss, accelergy["status"], figdir)
    write_summary(result_dir, figdir, bs, sat, ss, accelergy, merged)
    manifest = {"schema": "aspdac.figure_draft.v1",
                "generated_from": str(result_dir),
                "stage_series": "intentionally blank", "figures": []}
    for path in sorted(figdir.iterdir()):
        if path.suffix.lower() in {".png", ".pdf"}:
            manifest["figures"].append({
                "file": path.name, "sha256": sha256(path), "bytes": path.stat().st_size})
    (figdir/"figure_manifest.json").write_text(
        json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"figure_count": len(manifest["figures"])}, indent=2))


if __name__ == "__main__":
    main()