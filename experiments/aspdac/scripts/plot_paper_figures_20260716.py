#!/usr/bin/env python3
"""Render publication-layout drafts from the measured external-tool baselines.

STAGE series are left as explicit red outline slots until matched runs exist.
No placeholder height is interpreted as a measurement.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib.lines import Line2D
from matplotlib.patches import Patch, Rectangle

INK = "#25313B"
MUTED = "#65717B"
GRID = "#D7DDE1"
BLUE = "#2B6F94"
TEAL = "#2A8C82"
ORANGE = "#D8892B"
PURPLE = "#7467A8"
RED = "#B84A4A"
PALE = "#F5F7F8"

TRAFFIC_ORDER = ["uniform", "transpose", "bit_complement", "hotspot_node5"]
TRAFFIC_LABELS = {
    "uniform": "Uniform",
    "transpose": "Transpose",
    "bit_complement": "Bit complement",
    "hotspot_node5": "Hotspot (node 5)",
}
TRAFFIC_STYLE = {
    "uniform": (BLUE, "o"),
    "transpose": (ORANGE, "s"),
    "bit_complement": (TEAL, "^"),
    "hotspot_node5": (PURPLE, "D"),
}
WORKLOAD_ORDER = ["gemm_256", "mlp_l1", "mlp_l2", "attention_qk", "attention_pv"]
WORKLOAD_LABELS = {
    "gemm_256": "GEMM",
    "mlp_l1": "MLP L1",
    "mlp_l2": "MLP L2",
    "attention_qk": "QK$^T$",
    "attention_pv": "PV",
}


def configure_style() -> None:
    plt.rcParams.update({
        "font.family": "DejaVu Sans",
        "font.size": 7.2,
        "axes.titlesize": 8.0,
        "axes.labelsize": 7.2,
        "xtick.labelsize": 6.3,
        "ytick.labelsize": 6.3,
        "legend.fontsize": 6.2,
        "axes.linewidth": 0.65,
        "grid.linewidth": 0.45,
        "lines.linewidth": 1.35,
        "pdf.fonttype": 42,
        "ps.fonttype": 42,
        "savefig.dpi": 600,
    })


def clean_axis(ax, *, grid="both") -> None:
    ax.spines[["top", "right"]].set_visible(False)
    if grid == "y":
        ax.grid(axis="y", color=GRID, linewidth=0.45, zorder=0)
    else:
        ax.grid(True, which="both", color=GRID, linewidth=0.45, zorder=0)
    ax.tick_params(length=2.5, width=0.55, color=MUTED)
    ax.set_axisbelow(True)


def stage_slot_handle() -> Patch:
    return Patch(facecolor="none", edgecolor=RED, linestyle="--",
                 linewidth=1.1, label="STAGE data slot")


def save(fig, out_dir: Path, stem: str) -> None:
    for suffix in ("pdf", "png"):
        fig.savefig(out_dir / f"{stem}.{suffix}", facecolor="white",
                    edgecolor="none", bbox_inches="tight", pad_inches=0.025,
                    dpi=600)
    plt.close(fig)


def plot_booksim(bs: pd.DataFrame, saturation: pd.DataFrame, out_dir: Path) -> None:
    fig, (ax_l, ax_t) = plt.subplots(1, 2, figsize=(7.15, 2.28))
    fig.subplots_adjust(left=0.075, right=0.992, bottom=0.22, top=0.78, wspace=0.28)
    sat_map = dict(zip(saturation["traffic"], saturation["booksim_saturation_rate"]))

    for traffic in TRAFFIC_ORDER:
        frame = bs[bs["traffic"] == traffic].sort_values("injection_rate")
        x = frame["injection_rate"].to_numpy(float)
        color, marker = TRAFFIC_STYLE[traffic]
        latency = frame["packet_latency_mean"].to_numpy(float)
        latency_ci = frame["packet_latency_ci95"].to_numpy(float)
        accepted = frame["accepted_rate_mean"].to_numpy(float)
        accepted_ci = frame["accepted_rate_ci95"].to_numpy(float)
        label = TRAFFIC_LABELS[traffic]

        ax_l.plot(x, latency, color=color, marker=marker, markersize=3.0,
                  markerfacecolor="white", markeredgewidth=0.75, label=label)
        ax_l.fill_between(x, np.maximum(latency - latency_ci, 0.1),
                          latency + latency_ci, color=color, alpha=0.10, linewidth=0)
        unstable = frame["unstable_fraction"].to_numpy(float) > 0
        if unstable.any():
            ax_l.scatter(x[unstable], latency[unstable], marker="x", s=18,
                         color=color, linewidths=0.85, zorder=5)

        ax_t.plot(x, accepted, color=color, marker=marker, markersize=3.0,
                  markerfacecolor="white", markeredgewidth=0.75)
        ax_t.fill_between(x, np.maximum(accepted - accepted_ci, 0),
                          accepted + accepted_ci, color=color, alpha=0.10, linewidth=0)

        sat = sat_map.get(traffic, "not_reached")
        if sat not in (None, "", "not_reached"):
            sat_x = float(sat)
            idx = int(np.argmin(np.abs(x - sat_x)))
            ax_l.scatter([x[idx]], [latency[idx]], s=48, facecolors="none",
                         edgecolors=color, linewidths=0.9, zorder=6)
            ax_t.scatter([x[idx]], [accepted[idx]], s=48, facecolors="none",
                         edgecolors=color, linewidths=0.9, zorder=6)

    max_rate = float(bs["injection_rate"].max())
    ax_t.plot([0, max_rate], [0, max_rate], color=MUTED, linestyle=":",
              linewidth=0.9, zorder=1)
    ax_l.set_yscale("log")
    ax_l.set_title("(a) Packet latency", loc="left", fontweight="bold", color=INK)
    ax_t.set_title("(b) Accepted throughput", loc="left", fontweight="bold", color=INK)
    ax_l.set_ylabel("Mean packet latency (cycles, log)")
    ax_t.set_ylabel("Accepted rate (packet/node/cycle)")
    for ax in (ax_l, ax_t):
        ax.set_xlabel("Offered load (packet/node/cycle)")
        clean_axis(ax)

    traffic_handles, traffic_labels = ax_l.get_legend_handles_labels()
    extra = [Line2D([], [], color=MUTED, marker="o", markerfacecolor="none",
                    linestyle="none", label="BookSim saturation"), stage_slot_handle()]
    fig.legend(traffic_handles + extra, traffic_labels + ["BookSim saturation", "STAGE data slot"],
               loc="upper center", ncol=6, frameon=False, bbox_to_anchor=(0.53, 0.985),
               handlelength=1.8, columnspacing=1.0)
    fig.text(0.99, 0.025, "BookSim2: 4 traffic patterns x 13 rates x 10 seeds",
             ha="right", va="bottom", fontsize=5.8, color=MUTED)
    save(fig, out_dir, "result_booksim_reference")


def draw_empty_slots(ax, xs: np.ndarray, width: float, height: float) -> None:
    for xpos in xs:
        rect = Rectangle((xpos - width / 2, 0), width, height,
                         facecolor="none", edgecolor=RED, linewidth=0.9,
                         linestyle="--", zorder=4)
        ax.add_patch(rect)
        ax.text(xpos, height / 2, "--", ha="center", va="center",
                fontsize=6.0, color=RED, zorder=5)


def plot_workloads(tl: pd.DataFrame, ss: pd.DataFrame, energy: pd.DataFrame,
                   out_dir: Path) -> None:
    tl = tl.set_index("case_id").loc[WORKLOAD_ORDER].reset_index()
    ss = ss.set_index("case_id").loc[WORKLOAD_ORDER].reset_index()
    energy = energy.set_index("case_id").loc[WORKLOAD_ORDER].reset_index()
    labels = [WORKLOAD_LABELS[x] for x in WORKLOAD_ORDER]
    x = np.arange(len(labels), dtype=float)

    floor = tl["analytical_16mac_floor_cycles"].to_numpy(float)
    tl_norm = tl["cycles"].to_numpy(float) / floor
    ss_norm = ss["total_cycles"].to_numpy(float) / floor
    stall_pct = 100.0 * ss["stall_cycles"].to_numpy(float) / ss["total_cycles"].to_numpy(float)
    util_pct = ss["overall_util_pct"].to_numpy(float)
    pj_per_mac = energy["energy_uj"].to_numpy(float) * 1e6 / energy["expected_macs"].to_numpy(float)

    fig, axes = plt.subplots(1, 3, figsize=(7.15, 2.35))
    fig.subplots_adjust(left=0.07, right=0.993, bottom=0.23, top=0.78, wspace=0.34)

    # (a) Timing normalized by the same analytical floor.
    ax = axes[0]
    width = 0.24
    ax.bar(x - width, tl_norm, width, color=BLUE, edgecolor=BLUE, label="Timeloop", zorder=3)
    ax.bar(x, ss_norm, width, color=ORANGE, edgecolor=ORANGE, label="SCALE-Sim", zorder=3)
    draw_empty_slots(ax, x + width, width * 0.82, 0.14)
    for i, value in enumerate(ss_norm):
        if value > 1.25:
            ax.text(i, value + 0.08, f"{value:.2f}x", ha="center", va="bottom",
                    fontsize=5.8, color=ORANGE, fontweight="bold")
    ax.set_title("(a) Workload timing", loc="left", fontweight="bold", color=INK)
    ax.set_ylabel("Cycles / 16-MAC floor")
    ax.set_ylim(0, max(3.0, float(ss_norm.max()) * 1.14))
    ax.set_xticks(x, labels, rotation=18, ha="right")
    clean_axis(ax, grid="y")

    # (b) SCALE-Sim exposes the array-level stall outlier.
    ax = axes[1]
    width2 = 0.34
    ax.bar(x - width2 / 2, util_pct, width2, color=TEAL, label="Utilization", zorder=3)
    ax.bar(x + width2 / 2, stall_pct, width2, color=PURPLE, label="Stall fraction", zorder=3)
    outlier = int(np.argmax(stall_pct))
    ax.annotate(f"{stall_pct[outlier]:.1f}% stalls", xy=(x[outlier] + width2 / 2, stall_pct[outlier]),
                xytext=(x[outlier] + 0.8, 82), fontsize=5.8, color=PURPLE,
                arrowprops={"arrowstyle": "->", "color": PURPLE, "lw": 0.75},
                ha="center")
    ax.set_title("(b) Array behavior", loc="left", fontweight="bold", color=INK)
    ax.set_ylabel("Share of cycles (%)")
    ax.set_ylim(0, 105)
    ax.set_xticks(x, labels, rotation=18, ha="right")
    clean_axis(ax, grid="y")

    # (c) Energy is normalized per operation; STAGE remains a blank slot.
    ax = axes[2]
    ax.bar(x - 0.13, pj_per_mac, 0.26, color=ORANGE, edgecolor=ORANGE,
           label="Timeloop + Accelergy", zorder=3)
    draw_empty_slots(ax, x + 0.13, 0.21, 2.8)
    for i, value in enumerate(pj_per_mac):
        ax.text(i - 0.13, value + 0.55, f"{value:.1f}", ha="center", va="bottom",
                fontsize=5.5, color=INK)
    ax.set_title("(c) Energy reference", loc="left", fontweight="bold", color=INK)
    ax.set_ylabel("Energy (pJ/MAC)")
    ax.set_ylim(0, max(66, float(pj_per_mac.max()) * 1.16))
    ax.set_xticks(x, labels, rotation=18, ha="right")
    clean_axis(ax, grid="y")

    handles = [Patch(facecolor=BLUE, label="Timeloop"),
               Patch(facecolor=ORANGE, label="SCALE-Sim / Accelergy"),
               Patch(facecolor=TEAL, label="SCALE-Sim utilization"),
               Patch(facecolor=PURPLE, label="SCALE-Sim stalls"), stage_slot_handle()]
    fig.legend(handles=handles, loc="upper center", ncol=5, frameon=False,
               bbox_to_anchor=(0.52, 0.985), handlelength=1.6, columnspacing=1.1)
    fig.text(0.99, 0.025, "Matched STAGE bars remain empty until measured",
             ha="right", va="bottom", fontsize=5.8, color=RED)
    save(fig, out_dir, "result_workload_reference")


def plot_coverage(accelergy_ready: bool, out_dir: Path) -> None:
    rows = ["Analytical oracle", "BookSim2", "Timeloop", "SCALE-Sim", "Accelergy", "STAGE"]
    cols = ["Exact", "NoC", "Compute", "Array", "Energy", "Optical"]
    state = np.zeros((len(rows), len(cols)), dtype=int)
    labels = [["" for _ in cols] for _ in rows]

    def mark(row: str, col: str, value: int, label: str) -> None:
        i, j = rows.index(row), cols.index(col)
        state[i, j], labels[i][j] = value, label

    mark("Analytical oracle", "Exact", 2, "ready")
    mark("Analytical oracle", "Optical", 1, "check")
    mark("BookSim2", "NoC", 2, "ready")
    mark("Timeloop", "Compute", 2, "ready")
    mark("SCALE-Sim", "Array", 2, "ready")
    mark("Accelergy", "Energy", 2 if accelergy_ready else -1,
         "ready" if accelergy_ready else "blocked")
    for col in cols:
        mark("STAGE", col, 0, "data slot")

    from matplotlib.colors import BoundaryNorm, ListedColormap
    cmap = ListedColormap([RED, PALE, ORANGE, TEAL])
    norm = BoundaryNorm([-1.5, -0.5, 0.5, 1.5, 2.5], cmap.N)
    fig, ax = plt.subplots(figsize=(7.15, 2.20))
    fig.subplots_adjust(left=0.18, right=0.99, bottom=0.20, top=0.88)
    ax.imshow(state, cmap=cmap, norm=norm, aspect="auto")
    ax.set_xticks(np.arange(len(cols)), cols)
    ax.set_yticks(np.arange(len(rows)), rows)
    ax.tick_params(length=0)
    for i in range(len(rows)):
        for j in range(len(cols)):
            if labels[i][j]:
                color = "white" if state[i, j] in (-1, 2) else INK
                ax.text(j, i, labels[i][j], ha="center", va="center",
                        fontsize=6.1, color=color, fontweight="bold" if state[i, j] == 2 else "normal")
    ax.set_title("Validation evidence coverage", loc="left", fontweight="bold", color=INK)
    for spine in ax.spines.values():
        spine.set_visible(False)
    save(fig, out_dir, "evidence_coverage")


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--result-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()
    result_dir = Path(args.result_dir).resolve()
    out_dir = Path(args.output_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    configure_style()

    booksim = pd.read_csv(result_dir / "booksim" / "booksim_summary.csv")
    saturation = pd.read_csv(result_dir / "booksim" / "booksim_saturation.csv", keep_default_na=False)
    timeloop = pd.read_csv(result_dir / "timeloop" / "timeloop_summary.csv")
    scalesim = pd.read_csv(result_dir / "scalesim" / "scalesim_summary.csv")
    energy = pd.read_csv(result_dir / "accelergy" / "timeloop_accelergy_summary.csv")
    accelergy_status = json.loads((result_dir / "accelergy" / "accelergy_status.json").read_text(encoding="utf-8"))

    plot_booksim(booksim, saturation, out_dir)
    plot_workloads(timeloop, scalesim, energy, out_dir)
    plot_coverage(accelergy_status.get("status") == "completed", out_dir)

    manifest = {
        "schema": "aspdac.paper_figure_draft.v1",
        "sources": str(result_dir),
        "stage_values": "blank red outline slots",
        "assets": [],
    }
    for path in sorted(out_dir.iterdir()):
        if path.suffix.lower() in {".pdf", ".png"}:
            manifest["assets"].append({"file": path.name, "bytes": path.stat().st_size,
                                       "sha256": sha256(path)})
    (out_dir / "figure_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"assets": len(manifest["assets"]), "output": str(out_dir)}, indent=2))


if __name__ == "__main__":
    main()