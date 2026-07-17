#!/usr/bin/env python3
"""Generate the compact result figures used by current_overleaf.

Every plotted value comes from a manifest-indexed terminal summary.  Figure 3
uses the reviewer-extension and trace-visualization bundles; Figure 4 adds the
optical intervention; Figure 5 uses the frozen mapping sweep; Figure 6 uses
the MNIST precision bundle.
"""

from __future__ import annotations

import csv
import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.colors import Normalize
from matplotlib.patches import FancyArrowPatch


REPO = Path(__file__).resolve().parents[3]
BUNDLE = REPO / "experiments/aspdac/results/final_20260716"
REVIEW_BUNDLE = REPO / "experiments/aspdac/results/reviewer_extension_20260717"
TRACE_BUNDLE = REPO / "experiments/aspdac/results/trace_visualization_20260717"
OPTICAL_INTERVENTION_BUNDLE = REPO / "experiments/aspdac/results/optical_intervention_20260717"
MNIST_PRECISION_BUNDLE = REPO / "experiments/aspdac/results/mnist_pe_precision_20260717"
OUT = REPO / "output/figures"
FIGSIZE = (3.35, 3.35)
PDF_METADATA = {"Author": "Anonymous", "Creator": "Matplotlib", "Subject": "Anonymous review figure", "CreationDate": None, "ModDate": None}

COLORS = {
    "booksim": "#222222",
    "electrical": "#59636e",
    "stage": "#2673b8",
    "accent": "#c04c3d",
    "secondary": "#4d9272",
    "gray": "#777777",
}


def read_csv(name: str, bundle: Path = BUNDLE) -> list[dict[str, str]]:
    with (bundle / "summary" / name).open(newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def read_json(name: str, bundle: Path) -> dict[str, object]:
    with (bundle / "summary" / name).open(encoding="utf-8-sig") as stream:
        return json.load(stream)


def style() -> None:
    plt.rcParams.update(
        {
            "font.family": "DejaVu Sans",
            "font.size": 6.0,
            "axes.titlesize": 6.5,
            "axes.labelsize": 5.8,
            "xtick.labelsize": 5.2,
            "ytick.labelsize": 5.2,
            "legend.fontsize": 5.2,
            "axes.linewidth": 0.6,
            "axes.axisbelow": True,
            "lines.linewidth": 1.0,
            "lines.markersize": 2.8,
            "pdf.fonttype": 42,
            "ps.fonttype": 42,
        }
    )


def save(fig: plt.Figure, stem: str, crop: bool = False) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    crop_options = {"bbox_inches": "tight", "pad_inches": 0.01} if crop else {}
    fig.savefig(OUT / f"{stem}.pdf", metadata=PDF_METADATA, **crop_options)
    fig.savefig(OUT / f"{stem}.png", dpi=450, **crop_options)
    plt.close(fig)


def panel_label(ax: plt.Axes, label: str, x: float = -0.17) -> None:
    ax.text(x, 1.08, label, transform=ax.transAxes, fontsize=6.5, fontweight="bold", va="top")


def plot_cross_tool_legacy() -> None:
    timeloop = read_csv("rq3_size_timeloop_stage_scaling.csv")
    scalesim = read_csv("rq3_size_scalesim_stage_scaling.csv")
    energy = read_csv("rq3_energy_microbench.csv")
    cnn = [row for row in read_csv("mnist_cnn_noc_cross_tool.csv") if row["case_id"] != "sequential_network"]

    family_style = {
        "gemm": ("GEMM", "o", COLORS["stage"]),
        "mlp_l1": ("MLP", "s", COLORS["accent"]),
        "attention_qk": ("Attention", "^", COLORS["secondary"]),
    }
    fig, axes = plt.subplots(2, 2, figsize=FIGSIZE)
    fig.subplots_adjust(left=0.14, right=0.985, bottom=0.12, top=0.94, wspace=0.38, hspace=0.44)

    ax = axes[0, 0]
    for family, (label, marker, color) in family_style.items():
        rows = [row for row in timeloop if row["family"] == family]
        ax.scatter(
            [float(row["external_cycle_scale_vs_small"]) for row in rows],
            [float(row["stage_cycle_scale_vs_small"]) for row in rows],
            label=label,
            marker=marker,
            s=18,
            facecolors="white",
            edgecolors=color,
            linewidths=0.9,
            zorder=3,
        )
    ax.plot([1, 64], [1, 64], color="#777777", linestyle="--", linewidth=0.7)
    ax.set_xscale("log", base=2)
    ax.set_yscale("log", base=2)
    ax.set_xlim(0.8, 80)
    ax.set_ylim(0.8, 80)
    ax.set_xlabel("Timeloop cycle scale")
    ax.set_ylabel("STAGE cycle scale")
    ax.set_title("Timeloop exact replay", pad=2)
    ax.text(0.05, 0.90, "9/9 exact", transform=ax.transAxes, fontsize=5.3)
    ax.grid(True, which="major", color="#d8d8d8", linewidth=0.35)
    ax.legend(frameon=False, ncol=1, loc="lower right", handletextpad=0.3)
    panel_label(ax, "(a)")

    ax = axes[0, 1]
    for family, (_, marker, color) in family_style.items():
        rows = [row for row in scalesim if row["family"] == family]
        ax.scatter(
            [float(row["external_cycle_scale_vs_small"]) for row in rows],
            [float(row["stage_cycle_scale_vs_small"]) for row in rows],
            marker=marker,
            s=18,
            facecolors="white",
            edgecolors=color,
            linewidths=0.9,
            zorder=3,
        )
    ax.plot([1, 64], [1, 64], color="#777777", linestyle="--", linewidth=0.7)
    ax.set_xscale("log", base=2)
    ax.set_yscale("log", base=2)
    ax.set_xlim(0.8, 80)
    ax.set_ylim(0.8, 80)
    ax.set_xlabel("SCALE-Sim cycle scale")
    ax.set_ylabel("STAGE cycle scale")
    ax.set_title("SCALE-Sim timing trend", pad=2)
    ax.text(0.05, 0.90, "max diff. 5.41%", transform=ax.transAxes, fontsize=5.3)
    ax.grid(True, which="major", color="#d8d8d8", linewidth=0.35)
    panel_label(ax, "(b)")

    ax = axes[1, 0]
    ert = np.asarray([float(row["accelergy_ert_energy_pj"]) for row in energy])
    replay = np.asarray([float(row["stage_energy_pj"]) for row in energy])
    ax.scatter(ert, replay, color=COLORS["stage"], marker="o", s=17, facecolors="white", linewidths=0.9, zorder=3)
    ax.plot([0.006, 3000], [0.006, 3000], color="#777777", linestyle="--", linewidth=0.7)
    ax.set_xscale("log")
    ax.set_yscale("log")
    ax.set_xlim(0.006, 3000)
    ax.set_ylim(0.006, 3000)
    ax.set_xlabel("Accelergy ERT (pJ)")
    ax.set_ylabel("STAGE replay (pJ)")
    ax.set_title("Shared-action energy", pad=2)
    ax.text(0.05, 0.90, "9/9 exact", transform=ax.transAxes, fontsize=5.3)
    ax.grid(True, which="major", color="#d8d8d8", linewidth=0.35)
    panel_label(ax, "(c)")

    ax = axes[1, 1]
    cases = ["conv1", "conv2", "fc1", "fc2", "fc3"]
    lookup = {row["case_id"]: row for row in cnn}

    def ranks(field: str) -> list[int]:
        ordered = sorted(cases, key=lambda case: float(lookup[case][field]))
        rank = {case: index + 1 for index, case in enumerate(ordered)}
        return [rank[case] for case in cases]

    x = np.arange(len(cases), dtype=float)
    ax.plot(x - 0.04, ranks("booksim_network_makespan_cycles"), color=COLORS["booksim"], marker="o", markerfacecolor="white", label="BookSim2")
    ax.plot(x + 0.04, ranks("stage_network_makespan_cycles"), color=COLORS["stage"], marker="s", markerfacecolor="white", linestyle="--", label="STAGE")
    ax.set_xticks(x, ["Conv1", "Conv2", "FC1", "FC2", "FC3"], rotation=20)
    ax.set_yticks([1, 2, 3, 4, 5])
    ax.set_ylim(0.6, 5.4)
    ax.set_ylabel("Network makespan rank")
    ax.set_title("CNN layer trend", pad=2)
    ax.text(0.05, 0.90, r"$\rho=1.0$", transform=ax.transAxes, fontsize=5.3)
    ax.grid(True, axis="y", color="#d8d8d8", linewidth=0.35)
    ax.legend(frameon=False, loc="lower left", handlelength=1.5)
    panel_label(ax, "(d)")

    save(fig, "STAGE_Figure3_CrossTool_Validation")


def truthy(value: object) -> bool:
    return str(value).strip().lower() in {"true", "1", "yes"}


def plot_cross_tool() -> None:
    """Plot the new validation and trace-evidence overview for Fig. 3."""
    contract = read_csv("noc_contract_microbench.csv", REVIEW_BUNDLE)
    supported = [
        row
        for row in contract
        if row["checkpoint_status"] == "completed"
        and row["runtime_status"] in {"completed", "expected_boundary"}
        and truthy(row["oracle_matched"])
    ]
    supported_keys = {(row["case_id"], row["repeat"]) for row in supported}
    stage_timeline = read_csv("noc_cycle_timeline.csv", REVIEW_BUNDLE)
    oracle_timeline = read_csv("noc_oracle_timeline.csv", REVIEW_BUNDLE)

    def comparison_cycles(rows: list[dict[str, str]], stage_rows: bool) -> dict[tuple[str, str], int]:
        events: dict[tuple[str, str], list[dict[str, str]]] = {}
        for row in rows:
            key = (row["case_id"], row["repeat"])
            if key in supported_keys:
                events.setdefault(key, []).append(row)
        cycles: dict[tuple[str, str], int] = {}
        for key, key_events in events.items():
            delivered = [int(row["Cycle"]) for row in key_events if truthy(row["Delivered"])]
            if delivered:
                cycles[key] = max(delivered)
                continue
            if key[0] == "noc_n09_atomic_depth_boundary":
                if stage_rows:
                    boundary = [int(row["Cycle"]) for row in key_events if row["event_type"] == "Stall"]
                else:
                    boundary = [int(row["Cycle"]) for row in key_events if row["event_type"] == "atomic_admission_rejected"]
                if boundary:
                    cycles[key] = min(boundary)
        return cycles

    stage_delivery = comparison_cycles(stage_timeline, stage_rows=True)
    oracle_delivery = comparison_cycles(oracle_timeline, stage_rows=False)

    holdout = [
        row
        for row in read_csv("holdout_scalesim_stage_timing.csv", REVIEW_BUNDLE)
        if row["status"] == "completed"
    ]
    holdout_pairs: list[tuple[str, int, int]] = []
    for case_id, repeat in sorted({(row["case_id"], row["repeat"]) for row in holdout}):
        rows = [row for row in holdout if row["case_id"] == case_id and row["repeat"] == repeat]
        by_tool = {row["tool"]: row for row in rows}
        if {"stage", "scalesim"}.issubset(by_tool):
            holdout_pairs.append(
                (case_id, int(by_tool["scalesim"]["total_cycles"]), int(by_tool["stage"]["total_cycles"]))
            )

    link_rows = [
        row
        for row in read_csv("spatial_link_windows.csv", TRACE_BUNDLE)
        if row["domain"] == "stage_hotspot_node5_input"
    ]
    component_rows = [
        row
        for row in read_csv("spatial_component_windows.csv", TRACE_BUNDLE)
        if row["domain"] == "stage_hotspot_node5_input"
    ]
    hotspot = read_json("hotspot_node5_spatial_summary.json", TRACE_BUNDLE)
    interventions = {
        row["pair_id"]: row
        for row in read_csv("trace_guided_interventions.csv", REVIEW_BUNDLE)
    }
    link_step = interventions["p2_link_bits_64_to_128"]
    memory_step = interventions["p2_mem_ports_1_to_2"]

    fig, axes = plt.subplots(2, 2, figsize=FIGSIZE)
    fig.subplots_adjust(left=0.14, right=0.985, bottom=0.12, top=0.94, wspace=0.38, hspace=0.45)

    ax = axes[0, 0]
    case_labels = {
        "noc_n01_single_128": "N1",
        "noc_n02_single_256_vc1": "N2",
        "noc_n03_single_512_vc3": "N3",
        "noc_n04_single_1024": "N4",
        "noc_n05_contend_128": "N5",
        "noc_n06_contend_512": "N6",
        "noc_n09_atomic_depth_boundary": "N9",
    }
    colors = plt.cm.tab10(np.linspace(0, 0.85, len(case_labels)))
    all_cycles: list[int] = []
    for color, (case_id, label) in zip(colors, case_labels.items()):
        xs: list[int] = []
        ys: list[int] = []
        for key in sorted(key for key in supported_keys if key[0] == case_id):
            if key in oracle_delivery and key in stage_delivery:
                xs.append(oracle_delivery[key])
                ys.append(stage_delivery[key])
        if xs:
            ax.scatter(xs, ys, s=18, marker="o", facecolors="white", edgecolors=color, linewidths=0.9, zorder=3)
            all_cycles.extend(xs)
            all_cycles.extend(ys)
    upper = max(all_cycles) + 1
    ax.plot([0, upper], [0, upper], color=COLORS["gray"], linestyle="--", linewidth=0.7)
    ax.set_xlim(0, upper)
    ax.set_ylim(0, upper)
    ax.set_xlabel("Reference event cycle")
    ax.set_ylabel("STAGE event cycle")
    ax.set_title("NoC contract check", pad=2)
    ax.text(0.04, 0.92, "14 runs; 7 cases", transform=ax.transAxes, fontsize=5.2, va="top")
    ax.grid(True, color="#dddddd", linewidth=0.35)
    panel_label(ax, "(a)")

    ax = axes[0, 1]
    family_style = {
        "gemm": ("GEMM", "o", COLORS["stage"]),
        "attn": ("Attention", "^", COLORS["secondary"]),
        "rect": ("Rectangular", "s", COLORS["accent"]),
    }
    for family, (label, marker, color) in family_style.items():
        selected = [pair for pair in holdout_pairs if family in pair[0]]
        ax.scatter(
            [pair[1] for pair in selected],
            [pair[2] for pair in selected],
            s=20,
            marker=marker,
            facecolors="white",
            edgecolors=color,
            linewidths=0.9,
            label=label,
            zorder=3,
        )
    minimum = min(min(pair[1], pair[2]) for pair in holdout_pairs) * 0.8
    maximum = max(max(pair[1], pair[2]) for pair in holdout_pairs) * 1.25
    envelope_x = np.geomspace(minimum, maximum, 100)
    ax.fill_between(envelope_x, 0.9 * envelope_x, envelope_x / 0.9, color="#e8e8e8", label="10% envelope")
    ax.plot(envelope_x, envelope_x, color=COLORS["gray"], linestyle="--", linewidth=0.7)
    ax.set_xscale("log")
    ax.set_yscale("log")
    ax.set_xlim(minimum, maximum)
    ax.set_ylim(minimum, maximum)
    ax.set_xlabel("SCALE-Sim total cycles")
    ax.set_ylabel("STAGE total cycles")
    ax.set_title("Array hold-out trend", pad=2)
    max_difference = max(abs(stage - scale) / max(stage, scale) * 100 for _, scale, stage in holdout_pairs)
    ax.text(0.04, 0.92, f"16 pairs; max diff. {max_difference:.2f}%", transform=ax.transAxes, fontsize=5.4, va="top")
    ax.grid(True, which="major", color="#dddddd", linewidth=0.35)
    handles, labels = ax.get_legend_handles_labels()
    ax.legend(
        handles[1:] + handles[:1],
        labels[1:] + labels[:1],
        frameon=False,
        loc="lower right",
        bbox_to_anchor=(1.015, 0.0),
        borderaxespad=0.0,
        handletextpad=0.25,
        fontsize=5.0,
    )
    panel_label(ax, "(b)")

    ax = axes[1, 0]
    for x in range(4):
        for y in range(4):
            if x < 3:
                ax.plot([x, x + 1], [y, y], color="#e0e0e0", linewidth=0.6, zorder=0)
            if y < 3:
                ax.plot([x, x], [y, y + 1], color="#e0e0e0", linewidth=0.6, zorder=0)

    def parse_link(link_id: str) -> tuple[tuple[int, int], tuple[int, int]]:
        clean = link_id.replace("link[router[", "").replace("]]", "")
        source_text, target_text = clean.split("]->router[")
        source = tuple(int(value) for value in source_text.split(","))
        target = tuple(int(value) for value in target_text.split(","))
        return (source[0], source[1]), (target[0], target[1])

    packet_counts = [int(row["packet_count"]) for row in link_rows]
    normalization = Normalize(vmin=0, vmax=max(packet_counts))
    color_map = plt.cm.viridis
    hotspot_label_offsets = {
        (1, 0): (0.0, -0.14),
        (0, 1): (0.0, -0.22),
        (2, 1): (0.0, -0.22),
        (1, 2): (0.0, 0.14),
    }
    for row in link_rows:
        source, target = parse_link(row["canonical_link_id"])
        count = int(row["packet_count"])
        arrow = FancyArrowPatch(
            source,
            target,
            arrowstyle="-|>",
            mutation_scale=5.0,
            linewidth=0.55 + 2.0 * count / max(packet_counts),
            color=color_map(normalization(count)),
            shrinkA=7,
            shrinkB=7,
            zorder=1,
        )
        ax.add_patch(arrow)
        if target == (1, 1):
            midpoint = ((source[0] + target[0]) / 2, (source[1] + target[1]) / 2)
            offset = hotspot_label_offsets[source]
            ax.text(
                midpoint[0] + offset[0],
                midpoint[1] + offset[1],
                f"{count:,}",
                fontsize=4.2,
                ha="center",
                va="center",
                color="#222222",
            )

    component_volume: dict[int, int] = {}
    for row in component_rows:
        component_id = int(row["canonical_component_id"].removeprefix("endpoint[").removesuffix("]"))
        component_volume[component_id] = int(row["sent_packets"]) + int(row["received_packets"])
    max_volume = max(component_volume.values())
    for component_id in range(16):
        x, y = component_id % 4, component_id // 4
        size = 23 + 34 * np.sqrt(component_volume.get(component_id, 0) / max_volume)
        color = COLORS["accent"] if component_id == 5 else "white"
        ax.scatter([x], [y], s=size, facecolors=color, edgecolors="#333333", linewidths=0.65, zorder=3)
        ax.text(x, y, str(component_id), ha="center", va="center", fontsize=4.5, color="white" if component_id == 5 else "#222222", zorder=4)
    ax.set_xlim(-0.35, 3.35)
    ax.set_ylim(3.35, -0.35)
    ax.set_aspect("equal")
    ax.set_xticks(range(4))
    ax.set_yticks(range(4))
    ax.set_xlabel("Mesh x")
    ax.set_ylabel("Mesh y")
    ax.set_title("Hotspot traffic map", pad=2)
    panel_label(ax, "(c)")

    ax = axes[1, 1]
    totals = [int(link_step["before_total_cycles"]), int(link_step["after_total_cycles"]), int(memory_step["after_total_cycles"])]
    bars = ax.bar(np.arange(3), totals, width=0.58, color=[COLORS["stage"], COLORS["accent"], COLORS["secondary"]])
    ax.set_xticks(np.arange(3), ["64b\n1 port", "128b\n1 port", "128b\n2 ports"])
    ax.set_ylabel("Total cycles")
    ax.set_ylim(0, max(totals) * 1.18)
    ax.set_title("Bottleneck migration", pad=2)
    dominant = ["NoC", "Memory", "NoC"]
    relative_changes = [
        None,
        float(link_step["measured_total_relative_delta"]),
        float(memory_step["measured_total_relative_delta"]),
    ]
    for bar, total, bottleneck in zip(bars, totals, dominant):
        center = bar.get_x() + bar.get_width() / 2
        ax.text(center, total - 85, f"{total:,}", ha="center", va="top", fontsize=5.0, color="white", fontweight="bold")
        bottleneck_fontsize = 4.2 if bottleneck == "Memory" else 5.2
        ax.text(center, total * 0.54, bottleneck, ha="center", va="center", fontsize=bottleneck_fontsize, color="white", fontweight="bold")
    ax.grid(True, axis="y", color="#dddddd", linewidth=0.35)
    change_colors = (COLORS["accent"], COLORS["secondary"])
    for source_bar, before_total, after_total, relative_change, change_color in zip(
        bars[:-1],
        totals[:-1],
        totals[1:],
        relative_changes[1:],
        change_colors,
    ):
        arrow_x = source_bar.get_x() + source_bar.get_width() + 0.13
        cap_half_width = 0.07
        ax.plot(
            [arrow_x - cap_half_width, arrow_x + cap_half_width],
            [before_total, before_total],
            color=COLORS["gray"],
            linewidth=0.8,
            solid_capstyle="round",
            zorder=4,
        )
        ax.annotate(
            "",
            xy=(arrow_x, after_total),
            xytext=(arrow_x, before_total),
            arrowprops={
                "arrowstyle": "-|>",
                "color": COLORS["gray"],
                "linewidth": 0.8,
                "mutation_scale": 7.0,
                "shrinkA": 0,
                "shrinkB": 0,
            },
            zorder=4,
        )
        ax.text(
            arrow_x + 0.10,
            (before_total + after_total) / 2,
            f"{relative_change:+.1%}",
            ha="left",
            va="center",
            fontsize=4.6,
            color=change_color,
            fontweight="bold",
            zorder=5,
        )
    panel_label(ax, "(d)")

    save(fig, "STAGE_Figure3_CrossTool_Validation")

def plot_optical() -> None:
    rows = [row for row in read_csv("rq4_matched_transport.csv") if row["workload"] == "attention_128_64"]
    intervention = read_csv("optical_intervention.csv", OPTICAL_INTERVENTION_BUNDLE)
    constrained_electrical = next(row for row in intervention if row["transport_mode"] == "electrical_contended")
    wdm_optical = next(row for row in intervention if row["transport_mode"] == "optical_contended")
    electrical = next(row for row in rows if row["transport_mode"] == "electrical")
    contended = sorted(
        (row for row in rows if row["transport_mode"] == "optical_contended"),
        key=lambda row: int(row["channel_capacity"]),
    )
    capacities = np.asarray([int(row["channel_capacity"]) for row in contended])

    fig, axes = plt.subplots(2, 2, figsize=FIGSIZE)
    fig.subplots_adjust(left=0.14, right=0.985, bottom=0.12, top=0.94, wspace=0.39, hspace=0.45)

    ax = axes[0, 0]
    metrics = ["Cycles", "Energy"]
    electrical_values = np.asarray([float(electrical["total_cycles"]), float(electrical["total_transport_energy_pj"])])
    optical_values = np.asarray([float(contended[-1]["total_cycles"]), float(contended[-1]["total_transport_energy_pj"])])
    x = np.arange(len(metrics), dtype=float)
    width = 0.34
    electrical_bars = ax.bar(x - width / 2, np.ones(2), width=width, color=COLORS["electrical"], label="Electrical")
    optical_bars = ax.bar(x + width / 2, optical_values / electrical_values, width=width, color=COLORS["stage"], label="Optical cap8")
    ax.set_xticks(x, metrics)
    ax.set_ylabel("Relative to electrical")
    ax.set_ylim(0, 15)
    ax.set_title("Matched outcome", pad=2)
    for bar, value in zip(electrical_bars, ["1.0x", "1.0x"]):
        ax.text(bar.get_x() + bar.get_width() / 2, bar.get_height() + 0.35, value, ha="center", fontsize=4.8)
    for bar, value in zip(optical_bars, optical_values / electrical_values):
        ax.text(bar.get_x() + bar.get_width() / 2, bar.get_height() + 0.35, f"{value:.1f}x", ha="center", fontsize=4.8)
    ax.grid(True, axis="y", color="#d8d8d8", linewidth=0.35)
    ax.legend(frameon=False, loc="upper left", handlelength=1.2)
    panel_label(ax, "(a)")

    ax = axes[0, 1]
    cycles = np.asarray([float(row["total_cycles"]) for row in contended])
    unserved_requests = np.asarray([float(row["conflict_count"]) for row in contended])
    queue_full_cycles = np.asarray([float(row["backpressure_events"]) for row in contended])
    ax.plot(capacities, 100 * cycles / cycles[0], color=COLORS["stage"], marker="o", label="Cycles")
    ax.plot(capacities, 100 * unserved_requests / unserved_requests[0], color=COLORS["accent"], marker="s", label="Unserved req.")
    ax.plot(capacities, 100 * queue_full_cycles / queue_full_cycles[0], color=COLORS["gray"], marker="^", linestyle="--", label="Queue-full")
    ax.set_xticks(capacities)
    ax.set_xlabel("Wavelength capacity")
    ax.set_ylabel("% of cap-1 baseline")
    ax.set_ylim(0, 106)
    ax.set_title("Normalized contention", pad=2)
    ax.grid(True, color="#d8d8d8", linewidth=0.35)
    ax.legend(frameon=False, loc="upper right", handlelength=1.3)
    panel_label(ax, "(b)")

    ax = axes[1, 0]
    selected = [electrical, contended[-1], contended[0]]
    labels = ["Elec.", "Opt. cap8", "Opt. cap1"]
    interface = np.asarray([(float(row["serdes_energy_pj"]) + float(row["conversion_energy_pj"])) / 1000 for row in selected])
    path = np.asarray([(float(row["link_energy_pj"]) + float(row["endpoint_energy_pj"])) / 1000 for row in selected])
    laser = np.asarray([float(row["laser_energy_pj"]) / 1000 for row in selected])
    x = np.arange(len(selected), dtype=float)
    ax.bar(x, interface, color=COLORS["stage"], label="Interface")
    ax.bar(x, path, bottom=interface, color=COLORS["secondary"], label="Link+endpoint")
    ax.bar(x, laser, bottom=interface + path, color=COLORS["accent"], label="Laser")
    ax.set_xticks(x, labels, rotation=16)
    ax.set_ylabel("Transport energy (nJ)")
    ax.set_title("Energy breakdown", pad=2)
    ax.grid(True, axis="y", color="#d8d8d8", linewidth=0.35)
    ax.legend(frameon=False, loc="upper left", handlelength=1.2)
    panel_label(ax, "(c)")

    ax = axes[1, 1]
    intervention_cycles = np.asarray([
        float(constrained_electrical["total_cycles"]),
        float(wdm_optical["total_cycles"]),
    ])
    intervention_labels = ["1-ch Elec.", "8-wl Opt."]
    bars = ax.bar(np.arange(2), intervention_cycles, color=[COLORS["electrical"], COLORS["stage"]], width=0.58)
    ax.set_xticks(np.arange(2), intervention_labels)
    ax.set_ylabel("Transport cycles")
    ax.set_ylim(0, intervention_cycles.max() * 1.18)
    ax.set_title("Bandwidth intervention", pad=2)
    for bar, value in zip(bars, intervention_cycles):
        ax.text(bar.get_x() + bar.get_width() / 2, value + 170, f"{int(value):,}", ha="center", fontsize=4.8)
    cycle_reduction = 100 * (1 - intervention_cycles[1] / intervention_cycles[0])
    payload_rate_ratio = float(wdm_optical["effective_payload_bits_per_cycle"]) / float(constrained_electrical["effective_payload_bits_per_cycle"])
    ax.text(0.62, 0.56, f"{payload_rate_ratio:.0f}x payload rate\n-{cycle_reduction:.1f}% cycles", transform=ax.transAxes, ha="center", va="center", fontsize=5.0, color=COLORS["stage"])
    ax.grid(True, axis="y", color="#d8d8d8", linewidth=0.35)
    panel_label(ax, "(d)")

    save(fig, "STAGE_Figure4_Optical_Transport")


def heatmap(
    ax: plt.Axes,
    matrix: np.ndarray,
    title: str,
    fmt: str,
    label: str,
    label_x: float = -0.17,
) -> None:
    image = ax.imshow(matrix, cmap="Blues", aspect="auto")
    midpoint = (float(np.nanmin(matrix)) + float(np.nanmax(matrix))) / 2
    for row in range(matrix.shape[0]):
        for column in range(matrix.shape[1]):
            value = matrix[row, column]
            ax.text(column, row, format(value, fmt), ha="center", va="center", fontsize=4.3, color="white" if value > midpoint else "#222222")
    ax.set_xticks(range(6), [2, 4, 8, 16, 32, 64])
    ax.set_yticks(range(4), [32, 64, 128, 256])
    ax.set_xlabel("Cluster size C")
    ax.set_ylabel("Partition width D")
    ax.set_title(title, pad=2)
    ax.tick_params(length=0, pad=1.5)
    panel_label(ax, label, x=label_x)
    image.set_clim(float(np.nanmin(matrix)), float(np.nanmax(matrix)))


def plot_mapping() -> None:
    rows = read_csv("stage_mot_mapping.csv")
    d_values = [32, 64, 128, 256]
    c_values = [2, 4, 8, 16, 32, 64]

    def matrix(field: str, divisor: float = 1.0) -> np.ndarray:
        lookup = {(int(row["partition_width_d"]), int(row["cluster_size_c"])): float(row[field]) / divisor for row in rows}
        return np.asarray([[lookup[(d, c)] for c in c_values] for d in d_values])

    cycles = matrix("cycles")
    packets = matrix("physical_packet_moves_1024b")
    distance = matrix("physical_bit_distance_bit_um", 1_000_000)

    fig, axes = plt.subplots(1, 2, figsize=(3.35, 1.75))
    fig.subplots_adjust(left=0.14, right=0.985, bottom=0.21, top=0.86, wspace=0.38)
    heatmap(axes[0], packets, "1024-bit packet moves", ".0f", "(a)")

    ax = axes[1]
    row_index = d_values.index(64)
    normalized_cycles = cycles[row_index] / np.min(cycles[row_index])
    normalized_distance = distance[row_index] / np.min(distance[row_index])
    ax.plot(c_values, normalized_cycles, color=COLORS["stage"], marker="o", label="Cycles")
    ax.plot(c_values, normalized_distance, color=COLORS["accent"], marker="s", label="Bit-distance")
    ax.set_xscale("log", base=2)
    ax.set_xticks(c_values, c_values)
    ax.set_xlabel("Cluster size C (D=64)")
    ax.set_ylabel("Normalized to minimum")
    ax.set_title("Latency-distance trade-off", pad=2)
    ax.grid(True, color="#d8d8d8", linewidth=0.35)
    ax.legend(frameon=False, loc="upper right")
    panel_label(ax, "(b)", x=-0.20)

    save(fig, "STAGE_Figure5_Mapping_Codesign", crop=True)

def plot_mnist_precision() -> None:
    rows = read_csv("mnist_pe_precision_paired.csv", MNIST_PRECISION_BUNDLE)
    profile_order = ["fp32_a32", "fp16_a16", "fp8_a16", "fp8_a8"]
    profiles = {row["profile_id"]: row for row in rows}
    profile_labels = ["FP32/A32", "FP16/A16", "FP8/A16", "FP8/A8"]

    baseline_bits = float(profiles["fp32_a32"]["packetized_bits"])
    traffic_percent = np.asarray([
        100 * float(profiles[profile]["packetized_bits"]) / baseline_bits
        for profile in profile_order
    ])
    accuracy = np.asarray([
        float(profiles[profile]["accuracy_percent"])
        for profile in profile_order
    ])

    fig, ax = plt.subplots(figsize=(3.35, 1.70))
    fig.subplots_adjust(left=0.14, right=0.86, bottom=0.23, top=0.86)
    x = np.arange(len(profile_order))
    bars = ax.bar(
        x,
        traffic_percent,
        color="#8fb9d8",
        edgecolor=COLORS["stage"],
        linewidth=0.55,
        width=0.58,
        zorder=2,
    )
    ax.set_xticks(x, profile_labels)
    ax.set_ylabel("Traffic (% FP32)", color=COLORS["stage"], labelpad=2)
    ax.tick_params(axis="y", colors=COLORS["stage"])
    ax.set_ylim(0, 108)
    ax.grid(True, axis="y", color="#d8d8d8", linewidth=0.35)
    for bar, value in zip(bars, traffic_percent):
        ax.text(
            bar.get_x() + bar.get_width() / 2,
            value - 9,
            f"{value:.0f}%",
            ha="center",
            va="top",
            fontsize=5.2,
            fontweight="bold",
            color=COLORS["stage"],
        )

    accuracy_ax = ax.twinx()
    accuracy_ax.plot(
        x,
        accuracy,
        color=COLORS["accent"],
        marker="o",
        linewidth=1.15,
        markersize=3.4,
        zorder=3,
    )
    accuracy_ax.set_ylabel("Accuracy (%)", color=COLORS["accent"], labelpad=2)
    accuracy_ax.tick_params(axis="y", colors=COLORS["accent"])
    accuracy_ax.set_ylim(97.0, 98.58)
    for position, value, offset in zip(x, accuracy, [-0.10, 0.10, 0.0, 0.0]):
        accuracy_ax.text(
            position + offset,
            value + 0.07,
            f"{value:.2f}",
            ha="center",
            va="bottom",
            fontsize=5.0,
            color=COLORS["accent"],
        )

    save(fig, "STAGE_Figure6_MNIST_Precision", crop=True)


def main() -> None:
    style()
    plot_cross_tool()
    plot_optical()
    plot_mapping()
    plot_mnist_precision()
    print("Generated Fig. 3-6 under output/figures")


if __name__ == "__main__":
    main()
