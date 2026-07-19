#!/usr/bin/env python3
"""Regenerate the four result figures used by the submitted STAGE paper.

The script reads frozen experiment summaries only. Figure 3 intentionally
uses the compact mapping layout included by the submitted TeX source. The
newer three-panel mapping candidate remains available in this file for audit
but is not emitted by the default artifact workflow.
"""

from __future__ import annotations

import csv
import runpy
from collections import defaultdict
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.lines import Line2D


REPO = Path(__file__).resolve().parents[3]
OUT = REPO / "output/figures"
FINAL = REPO / "experiments/aspdac/results/final_20260716/summary"
MNIST = REPO / "experiments/aspdac/results/mnist_pe_precision_20260717/summary"
PAPER_RESULTS = REPO / "experiments/aspdac/results/paper_revision_20260718"
OPTO = PAPER_RESULTS / "session_b_opto_noc_reduced_v1/summary"
QUEUE = PAPER_RESULTS / "session_d_o4_queue"
ELECTRICAL = PAPER_RESULTS / "session_e_electrical_energy"
OPTICAL_ENERGY = PAPER_RESULTS / "session_f_optical_energy"
FIGURE2_SCRIPT = PAPER_RESULTS / "session_c_matched_47bars/matched_47bar_figure.py"
LEGACY_PLOT_SCRIPT = REPO / "experiments/aspdac/scripts/plot_current_overleaf_v1.py"

PDF_METADATA = {
    "Author": "Anonymous",
    "Creator": "Matplotlib",
    "Subject": "Anonymous review figure",
    "CreationDate": None,
    "ModDate": None,
}

BLUE = "#2b73b6"
LIGHT_BLUE = "#91b8d5"
RED = "#c44b3a"
GREEN = "#58a24d"
LIGHT_GREEN = "#8acf78"
ORANGE = "#f28e2b"
PURPLE = "#ad7aa1"
GRAY = "#aaa29f"
DARK = "#252525"
ARCH_COLORS = {
    "E128": "#4e79a7",
    "O1": "#59a14f",
    "O2": "#8cd17d",
    "O4": "#f28e2b",
    "H4": "#af7aa1",
}


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def configure(font_size: float = 6.0) -> None:
    plt.rcParams.update(
        {
            "font.family": "DejaVu Sans",
            "font.size": font_size,
            "axes.titlesize": font_size + 0.4,
            "axes.labelsize": font_size,
            "xtick.labelsize": font_size - 0.4,
            "ytick.labelsize": font_size - 0.4,
            "legend.fontsize": font_size - 0.5,
            "axes.linewidth": 0.65,
            "axes.axisbelow": True,
            "lines.linewidth": 1.15,
            "lines.markersize": 3.5,
            "pdf.fonttype": 42,
            "ps.fonttype": 42,
        }
    )


def save(fig: plt.Figure, stem: str) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    fig.savefig(OUT / f"{stem}.pdf", bbox_inches="tight", pad_inches=0.02, metadata=PDF_METADATA)
    fig.savefig(OUT / f"{stem}.png", dpi=450, bbox_inches="tight", pad_inches=0.02)
    plt.close(fig)


def panel_label(ax: plt.Axes, label: str, x: float = -0.13, y: float = 1.08) -> None:
    ax.text(x, y, label, transform=ax.transAxes, fontweight="bold", va="top")


def make_figure2() -> None:
    module = runpy.run_path(str(FIGURE2_SCRIPT), run_name="stage_artifact_figure2")
    module["draw"](
        REPO,
        OUT / "STAGE_Figure2_Matched_47Bars.png",
        OUT / "STAGE_Figure2_Matched_47Bars.pdf",
    )


def make_submitted_figure3() -> None:
    module = runpy.run_path(str(LEGACY_PLOT_SCRIPT), run_name="stage_artifact_mapping")
    module["style"]()
    module["plot_mapping"]()
    for suffix in (".png", ".pdf"):
        source = OUT / f"STAGE_Figure5_Mapping_Codesign{suffix}"
        target = OUT / f"STAGE_Figure3_Mapping_Codesign{suffix}"
        source.replace(target)


def matrix_from_rows(
    rows: list[dict[str, str]], d_values: list[int], c_values: list[int], field: str, divisor: float = 1.0
) -> np.ndarray:
    lookup = {
        (int(row["partition_width_d"]), int(row["cluster_size_c"])): float(row[field]) / divisor
        for row in rows
    }
    return np.asarray([[lookup[(d, c)] for c in c_values] for d in d_values])


def draw_heatmap(
    ax: plt.Axes,
    values: np.ndarray,
    d_values: list[int],
    c_values: list[int],
    title: str,
    fmt: str,
) -> None:
    image = ax.imshow(values, cmap="Blues", aspect="auto")
    threshold = (float(values.min()) + float(values.max())) / 2
    for row in range(values.shape[0]):
        for column in range(values.shape[1]):
            value = values[row, column]
            ax.text(
                column,
                row,
                format(value, fmt),
                ha="center",
                va="center",
                color="white" if value > threshold else DARK,
                fontsize=4.8,
            )
    ax.set_xticks(range(len(c_values)), c_values)
    ax.set_yticks(range(len(d_values)), d_values)
    ax.set_xlabel("Cluster size C", labelpad=1)
    ax.set_ylabel("Partition width D", labelpad=1)
    ax.set_title(title, pad=2)
    for spine in ax.spines.values():
        spine.set_visible(True)
    image.set_clim(values.min(), values.max())


def make_figure3() -> None:
    configure(4.8)
    rows = read_csv(FINAL / "stage_mot_mapping.csv")
    d_values = [32, 64, 128, 256]
    c_values = [2, 4, 8, 16, 32, 64]
    packet_moves = matrix_from_rows(rows, d_values, c_values, "physical_packet_moves_1024b")
    cycles = matrix_from_rows(rows, d_values, c_values, "cycles")

    fig = plt.figure(figsize=(3.45, 3.25))
    grid = fig.add_gridspec(2, 2, height_ratios=[1.0, 1.08], hspace=0.54, wspace=0.42)
    ax_a = fig.add_subplot(grid[0, 0])
    ax_b = fig.add_subplot(grid[0, 1])
    ax_c = fig.add_subplot(grid[1, :])
    draw_heatmap(ax_a, packet_moves, d_values, c_values, "Packet moves", ".0f")
    draw_heatmap(ax_b, cycles, d_values, c_values, "Full-system cycles", ".0f")
    panel_label(ax_a, "(a)", x=-0.28)
    panel_label(ax_b, "(b)", x=-0.28)

    points = []
    for row in rows:
        points.append(
            {
                "d": int(row["partition_width_d"]),
                "c": int(row["cluster_size_c"]),
                "cycles": float(row["cycles"]),
                "distance": float(row["physical_bit_distance_bit_um"]) / 1_000_000,
            }
        )
    d_colors = {32: "#4e79a7", 64: "#59a14f", 128: "#f28e2b", 256: "#af7aa1"}
    for d in d_values:
        selected = [point for point in points if point["d"] == d]
        ax_c.scatter(
            [point["distance"] for point in selected],
            [point["cycles"] for point in selected],
            s=[12 + 6 * np.log2(point["c"]) for point in selected],
            color=d_colors[d],
            edgecolors="white",
            linewidths=0.4,
            label=f"D={d}",
            zorder=3,
        )

    pareto = []
    for point in points:
        dominated = any(
            other["cycles"] <= point["cycles"]
            and other["distance"] <= point["distance"]
            and (other["cycles"] < point["cycles"] or other["distance"] < point["distance"])
            for other in points
        )
        if not dominated:
            pareto.append(point)
    pareto.sort(key=lambda point: point["distance"])
    ax_c.plot(
        [point["distance"] for point in pareto],
        [point["cycles"] for point in pareto],
        color=DARK,
        linestyle="--",
        linewidth=0.75,
        zorder=2,
    )
    for point in pareto:
        ax_c.scatter(
            point["distance"],
            point["cycles"],
            s=34,
            facecolors="none",
            edgecolors=DARK,
            linewidths=0.65,
            zorder=4,
        )
        ax_c.annotate(
            f"{point['d']}/{point['c']}",
            (point["distance"], point["cycles"]),
            xytext=(2, 3),
            textcoords="offset points",
            fontsize=4.3,
        )
    ax_c.set_xlabel(r"Physical bit-distance (Mbit-$\mu$m)", labelpad=1)
    ax_c.set_ylabel("Cycles", labelpad=1)
    ax_c.set_title("All 24 mapping--geometry candidates", pad=2)
    ax_c.grid(True, color="#dddddd", linewidth=0.35)
    ax_c.legend(frameon=False, ncol=4, loc="upper right", handletextpad=0.2, columnspacing=0.6)
    panel_label(ax_c, "(c)", x=-0.12)
    fig.subplots_adjust(left=0.14, right=0.985, bottom=0.12, top=0.95)
    save(fig, "STAGE_Figure3_Mapping_Codesign")


def make_figure4() -> None:
    configure(5.9)
    rows = read_csv(MNIST / "mnist_pe_precision_paired.csv")
    order = ["fp32_a32", "fp16_a16", "fp8_a16", "fp8_a8"]
    labels = ["FP32/A32", "FP16/A16", "FP8/A16", "FP8/A8"]
    lookup = {row["profile_id"]: row for row in rows}
    baseline_bits = float(lookup[order[0]]["packetized_bits"])
    baseline_cycles = float(lookup[order[0]]["sequential_cycles"])
    traffic = np.asarray([100 * float(lookup[key]["packetized_bits"]) / baseline_bits for key in order])
    system_cycles = np.asarray([100 * float(lookup[key]["sequential_cycles"]) / baseline_cycles for key in order])
    accuracy = np.asarray([float(lookup[key]["accuracy_percent"]) for key in order])

    fig, ax = plt.subplots(figsize=(3.45, 1.72))
    fig.subplots_adjust(left=0.14, right=0.86, bottom=0.23, top=0.88)
    x = np.arange(len(order), dtype=float)
    width = 0.29
    traffic_bars = ax.bar(x - width / 2, traffic, width, color=LIGHT_BLUE, edgecolor=BLUE, linewidth=0.5, label="Traffic")
    cycle_bars = ax.bar(x + width / 2, system_cycles, width, color="#b6d7a8", edgecolor=GREEN, linewidth=0.5, label="Service cycles")
    ax.set_xticks(x, labels)
    ax.set_ylabel("Relative to FP32 (%)", labelpad=2)
    ax.set_ylim(0, 112)
    ax.grid(True, axis="y", color="#dddddd", linewidth=0.35)
    for bars, values in ((traffic_bars, traffic), (cycle_bars, system_cycles)):
        for bar, value in zip(bars, values):
            ax.text(bar.get_x() + bar.get_width() / 2, value + 2.2, f"{value:.0f}", ha="center", va="bottom", fontsize=4.4)
    accuracy_ax = ax.twinx()
    accuracy_ax.plot(x, accuracy, color=RED, marker="o", linewidth=1.0, zorder=4)
    accuracy_ax.set_ylabel("Accuracy (%)", color=RED, labelpad=2)
    accuracy_ax.tick_params(axis="y", colors=RED)
    accuracy_ax.set_ylim(97.0, 98.62)
    for index, value in enumerate(accuracy):
        offset = -0.08 if index == 0 else (0.08 if index == 1 else 0.0)
        accuracy_ax.text(index + offset, value + 0.06, f"{value:.2f}", color=RED, ha="center", fontsize=4.7)
    handles, legend_labels = ax.get_legend_handles_labels()
    handles.append(Line2D([0], [0], color=RED, marker="o", linewidth=1.0))
    legend_labels.append("Accuracy")
    ax.legend(
        handles,
        legend_labels,
        frameon=False,
        ncol=1,
        loc="upper right",
        handlelength=1.2,
        handletextpad=0.3,
        labelspacing=0.2,
    )
    ax.set_title("Traffic, service time, and accuracy", pad=2)
    save(fig, "STAGE_Figure4_MNIST_Precision")

def final_cycle_ratio_matrix() -> tuple[list[str], list[str], np.ndarray]:
    rows = read_csv(OPTO / "opto_noc_main_matrix_adjudicated.csv")
    grouped: dict[tuple[str, int, str], list[float]] = defaultdict(list)
    for row in rows:
        if row["status"] == "completed":
            grouped[(row["workload"], int(row["traffic_scale"]), row["architecture"])].append(float(row["total_cycles"]))

    queue_rows = read_csv(QUEUE / "queue_depth_sweep.csv")
    final_o4 = [
        float(row["total_cycles"])
        for row in queue_rows
        if row["design_id"] == "O4-Q16-effective" and row["status"] == "completed"
    ]
    grouped[("gemm_256_collection", 4, "O4")] = final_o4

    workload_order = [
        ("attention_qk_s128_d64", "Attention QK"),
        ("attention_pv_s128_d64", "Attention PV"),
        ("gemm_256_collection", "GEMM"),
        ("mnist_conv2_im2col", "MNIST Conv2"),
        ("deterministic_long_distance_exchange", "Long-distance"),
    ]
    architectures = ["E128", "O1", "O2", "O4", "H4"]
    labels: list[str] = []
    values: list[list[float]] = []
    for workload, label in workload_order:
        for scale in (1, 2, 4):
            labels.append(f"{label} {scale}x")
            baseline = float(np.median(grouped[(workload, scale, "E128")]))
            values.append(
                [float(np.median(grouped[(workload, scale, architecture)])) / baseline for architecture in architectures]
            )
    return labels, architectures, np.asarray(values)


def make_figure5() -> None:
    configure(4.8)
    labels, architectures, ratios = final_cycle_ratio_matrix()
    electrical_rows = read_csv(ELECTRICAL / "five_architecture_breakdown.csv")
    electrical = {row["architecture"]: row for row in electrical_rows}
    optical_rows = read_csv(OPTICAL_ENERGY / "sensitivity_results.csv")
    optical: dict[tuple[str, str], float] = {}
    for architecture in architectures[1:]:
        for profile in ("low", "nominal", "high"):
            matching = [
                float(row["calibrated_optical_serdes_energy_pj"])
                for row in optical_rows
                if row["architecture"] == architecture and row["profile"] == profile and row["repeat"] == "0"
            ]
            optical[(architecture, profile)] = matching[0]

    fig = plt.figure(figsize=(3.45, 3.90))
    grid = fig.add_gridspec(3, 2, height_ratios=[0.72, 1.0, 1.0], hspace=0.30, wspace=0.52)
    ax_a = fig.add_subplot(grid[0, :])
    ax_b = fig.add_subplot(grid[1, 0])
    ax_c = fig.add_subplot(grid[1, 1])
    ax_d = fig.add_subplot(grid[2, 0])
    ax_e = fig.add_subplot(grid[2, 1])

    horizontal_ratios = ratios.T
    short_labels = [
        "Q1", "Q2", "Q4", "P1", "P2", "P4", "G1", "G2", "G4",
        "M1", "M2", "M4", "L1", "L2", "L4",
    ]
    image = ax_a.imshow(horizontal_ratios, cmap="YlOrRd", aspect="auto", vmin=1.0, vmax=max(14.0, float(ratios.max())))
    for row in range(horizontal_ratios.shape[0]):
        for column in range(horizontal_ratios.shape[1]):
            value = horizontal_ratios[row, column]
            ax_a.text(
                column,
                row,
                f"{value:.1f}x",
                ha="center",
                va="center",
                fontsize=3.15,
                fontweight="bold",
                color="white" if value >= 5.7 else DARK,
            )
    ax_a.set_xticks(range(len(short_labels)), short_labels)
    ax_a.set_yticks(range(len(architectures)), architectures)
    for boundary in (2.5, 5.5, 8.5, 11.5):
        ax_a.axvline(boundary, color="white", linewidth=0.55)
    ax_a.set_title("(a) Workload-scale cycle ratio to E128 (Q/P/G/M/L)", pad=2)
    image.set_clim(1.0, max(14.0, float(ratios.max())))

    cycles = [float(electrical[architecture]["total_cycles"]) for architecture in architectures]
    bars = ax_b.bar(range(5), cycles, color=[ARCH_COLORS[key] for key in architectures], width=0.66)
    ax_b.set_xticks(range(5), architectures)
    ax_b.set_ylabel("Cycles")
    ax_b.set_title("(b) Attention QK, scale 2", pad=3)
    ax_b.grid(True, axis="y", color="#dddddd", linewidth=0.35)
    ax_b.set_ylim(0, max(cycles) * 1.22)
    for bar, value in zip(bars, cycles):
        ax_b.text(bar.get_x() + bar.get_width() / 2, value + 18, f"{value:.0f}", ha="center", va="bottom", fontsize=5.0)

    dynamic = np.asarray(
        [
            (float(electrical[key]["router_dynamic_energy_pj"]) + float(electrical[key]["electrical_link_dynamic_energy_pj"]))
            / 1_000_000
            for key in architectures
        ]
    )
    remainder = np.asarray([float(electrical[key]["electrical_noc_total_energy_pj"]) / 1_000_000 for key in architectures]) - dynamic
    x = np.arange(5, dtype=float)
    width = 0.34
    ax_c.bar(x - width / 2, dynamic, width, color=ARCH_COLORS["E128"], label="Dynamic")
    ax_c.bar(x + width / 2, remainder, width, color=GRAY, label="Clock + leakage")
    ax_c.set_yscale("log")
    ax_c.set_xticks(x, architectures)
    ax_c.set_ylabel(r"Energy ($\mu$J, log)")
    ax_c.set_title("(c) DSENT electrical NoC", pad=3)
    ax_c.grid(True, which="both", axis="y", color="#dddddd", linewidth=0.3)
    ax_c.legend(frameon=False, loc="upper left", handlelength=1.2, handletextpad=0.3)

    physical = read_csv(OPTO / "opto_noc_physical_aggregates.csv")
    route_styles = [
        ("attention_qk_s128_d64", 1, "Attention O1", BLUE, "o", "-"),
        ("attention_qk_s128_d64", 4, "Attention O4", ORANGE, "o", "--"),
        ("mnist_conv2_im2col", 1, "MNIST O1", BLUE, "s", "-"),
        ("mnist_conv2_im2col", 4, "MNIST O4", ORANGE, "s", "--"),
    ]
    for workload, wavelengths, label, color, marker, linestyle in route_styles:
        selected = sorted(
            [
                row
                for row in physical
                if row["workload"] == workload and int(row["wavelengths"]) == wavelengths and row["status"] == "completed"
            ],
            key=lambda row: float(row["route_length_mm"]),
        )
        ax_d.plot(
            [float(row["route_length_mm"]) for row in selected],
            [float(row["total_cycles"]) for row in selected],
            color=color,
            marker=marker,
            linestyle=linestyle,
            label=label,
        )
    ax_d.set_xticks([1, 5, 10])
    ax_d.set_xlabel("Route length (mm)")
    ax_d.set_ylabel("Route cycles")
    ax_d.set_title("(d) Provisioned O1/O4 routes", pad=3)
    ax_d.grid(True, color="#dddddd", linewidth=0.35)
    ax_d.legend(frameon=False, ncol=2, loc="upper left", handlelength=1.4, columnspacing=0.6, handletextpad=0.25)

    low = []
    nominal = []
    high = []
    for architecture in architectures:
        electrical_value = float(electrical[architecture]["electrical_noc_total_energy_pj"])
        if architecture == "E128":
            values = [electrical_value, electrical_value, electrical_value]
        else:
            values = [electrical_value + optical[(architecture, profile)] for profile in ("low", "nominal", "high")]
        low.append(values[0] / 1_000_000)
        nominal.append(values[1] / 1_000_000)
        high.append(values[2] / 1_000_000)
    low_array = np.asarray(low)
    nominal_array = np.asarray(nominal)
    high_array = np.asarray(high)
    yerr = np.vstack((nominal_array - low_array, high_array - nominal_array))
    ax_e.errorbar(x, nominal_array, yerr=yerr, fmt="none", ecolor="#c9c3c0", elinewidth=5.0, capsize=0, zorder=1)
    for index, architecture in enumerate(architectures):
        marker = "D" if architecture == "E128" else "o"
        ax_e.scatter(index, nominal_array[index], color=ARCH_COLORS[architecture], marker=marker, s=35, edgecolors="white", linewidths=0.45, zorder=3)
        ax_e.text(index, nominal_array[index] + 0.6, f"{nominal_array[index]:.2f}", ha="center", fontsize=4.7)
    ax_e.set_xticks(x, architectures)
    ax_e.set_ylabel(r"Interconnect energy ($\mu$J)")
    ax_e.set_title("(e) Sourced low--high envelope", pad=3)
    ax_e.set_ylim(0, max(high) * 1.08)
    ax_e.grid(True, axis="y", color="#dddddd", linewidth=0.35)
    ax_e.legend(
        [
            Line2D([0], [0], color="#c9c3c0", linewidth=5),
            Line2D([0], [0], marker="o", color="none", markerfacecolor=GREEN, markeredgecolor="white"),
        ],
        ["Low--high", "Nominal"],
        frameon=False,
        loc="upper left",
        handlelength=1.2,
        handletextpad=0.35,
    )
    fig.subplots_adjust(left=0.145, right=0.985, bottom=0.085, top=0.97)
    save(fig, "STAGE_Figure5_Optoelectronic_Final")


def main() -> None:
    make_figure2()
    make_submitted_figure3()
    make_figure4()
    make_figure5()


if __name__ == "__main__":
    main()
