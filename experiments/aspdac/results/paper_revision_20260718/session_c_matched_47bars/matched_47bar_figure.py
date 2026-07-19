#!/usr/bin/env python3
"""Draw the formal paired dumbbell plus tolerance-strip figure for 47 matched cases."""

from __future__ import annotations

import argparse
import csv
from datetime import datetime, timezone
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
from matplotlib.patches import Rectangle
from matplotlib.ticker import LogFormatterMathtext, LogLocator


OUTPUT_NAME = "STAGE_Figure2_Matched_47Bars.png"
INDEX_REL = Path(
    "experiments/aspdac/results/paper_revision_20260718/"
    "session_c_matched_47bars/matched_47bar_values.csv"
)
OUTPUT_REL = INDEX_REL.parent / OUTPUT_NAME
FAMILIES = (
    "square_gemm",
    "rectangular_gemm",
    "mlp_l1",
    "mlp_l2",
    "attention_qk",
    "attention_pv",
)
FAMILY_LABELS = {
    "square_gemm": "Square GEMM",
    "rectangular_gemm": "Rectangular GEMM",
    "mlp_l1": "MLP-L1",
    "mlp_l2": "MLP-L2",
    "attention_qk": "Attention QK",
    "attention_pv": "Attention PV",
}


def find_repo_root(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit).resolve()
    for parent in Path(__file__).resolve().parents:
        if (parent / "VisualHardwareAiCoDesignSimulator.sln").is_file():
            return parent
    raise FileNotFoundError("Repository root not found")


def read_rows(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8", newline="") as stream:
        rows = list(csv.DictReader(stream))
    if len(rows) != 47:
        raise ValueError(f"Expected 47 configurations, found {len(rows)}")
    if [row["config_id"] for row in rows] != [f"C{index:02d}" for index in range(1, 48)]:
        raise ValueError("Configuration index is not contiguous C01-C47")
    return rows


def family_ranges(rows: list[dict[str, str]]) -> list[tuple[str, int, int]]:
    ranges: list[tuple[str, int, int]] = []
    start = 0
    for family in FAMILIES:
        count = sum(row["family"] == family for row in rows)
        if count == 0:
            raise ValueError(f"Missing family: {family}")
        ranges.append((family, start, start + count - 1))
        start += count
    if start != 47:
        raise ValueError("Family ranges do not cover all configurations")
    return ranges


def style_main_axis(axis: plt.Axes, ranges: list[tuple[str, int, int]]) -> None:
    axis.set_yscale("log")
    axis.set_xlim(-0.55, 46.55)
    axis.yaxis.set_major_locator(LogLocator(base=10))
    axis.yaxis.set_major_formatter(LogFormatterMathtext(base=10))
    axis.grid(axis="y", which="major", color="#D9DEE2", linewidth=0.65)
    axis.grid(axis="y", which="minor", color="#EEF1F3", linewidth=0.35)
    axis.spines["top"].set_visible(False)
    axis.spines["right"].set_visible(False)
    axis.tick_params(axis="x", bottom=False, labelbottom=False)
    for index, (_, start, end) in enumerate(ranges):
        if index % 2:
            axis.axvspan(start - 0.5, end + 0.5, color="#F6F3EC", alpha=0.55, zorder=0)
        if end < 46:
            axis.axvline(end + 0.5, color="#AAB2B9", linewidth=0.7, zorder=1)


def draw_pairs(
    axis: plt.Axes,
    reference: list[float],
    stage: list[float],
    reference_color: str,
    stage_color: str,
) -> None:
    x_positions = list(range(47))
    offset = 0.14
    for x, left, right in zip(x_positions, reference, stage):
        axis.plot(
            (x - offset, x + offset),
            (left, right),
            color="#7C858C",
            linewidth=0.75,
            zorder=3,
        )
    axis.scatter(
        [x - offset for x in x_positions],
        reference,
        s=24,
        marker="o",
        facecolor="white",
        edgecolor=reference_color,
        linewidth=1.15,
        zorder=4,
    )
    axis.scatter(
        [x + offset for x in x_positions],
        stage,
        s=20,
        marker="D",
        facecolor=stage_color,
        edgecolor="white",
        linewidth=0.45,
        zorder=5,
    )


def style_error_axis(
    axis: plt.Axes,
    errors: list[float],
    ranges: list[tuple[str, int, int]],
    color: str,
    show_x_labels: bool,
) -> None:
    x_positions = list(range(47))
    axis.set_xlim(-0.55, 46.55)
    axis.set_ylim(0, 1.06)
    axis.axhspan(0, 1, color="#F4F6F7", zorder=0)
    axis.bar(x_positions, errors, width=0.64, color=color, edgecolor="none", zorder=3)
    axis.axhline(1, color="#A43D3D", linewidth=0.7, linestyle=(0, (2, 2)), zorder=4)
    axis.set_yticks((0, 1), labels=("0", "1"))
    axis.set_ylabel("|err| / tol.", fontsize=6.9, labelpad=2)
    axis.tick_params(axis="y", labelsize=6.6, length=2)
    axis.spines["top"].set_visible(False)
    axis.spines["right"].set_visible(False)
    axis.spines["left"].set_color("#AAB2B9")
    axis.spines["bottom"].set_color("#AAB2B9")
    for _, _, end in ranges:
        if end < 46:
            axis.axvline(end + 0.5, color="#AAB2B9", linewidth=0.7, zorder=2)
    if show_x_labels:
        axis.set_xticks(x_positions, labels=[f"C{index:02d}" for index in range(1, 48)], rotation=90)
        axis.tick_params(axis="x", labelsize=6.0, length=2.0, pad=1.2)
        axis.set_xlabel("Configuration index", labelpad=5)
    else:
        axis.set_xticks(x_positions, labels=[""] * 47)
        axis.tick_params(axis="x", length=0)


def draw(
    repo_root: Path,
    output: Path | None = None,
    pdf_output: Path | None = None,
) -> Path:
    rows = read_rows(repo_root / INDEX_REL)
    ranges = family_ranges(rows)
    output = output or (repo_root / OUTPUT_REL)

    reference_color = "#0072B2"
    stage_color = "#E69F00"
    error_color = "#6A4C93"
    timeloop_cycles = [float(row["timeloop_compute_cycles"]) for row in rows]
    stage_cycles = [float(row["stage_matched_compute_cycles"]) for row in rows]
    accelergy_energy = [float(row["accelergy_shared_ert_energy_pj"]) for row in rows]
    stage_energy = [float(row["stage_matched_energy_pj"]) for row in rows]
    cycle_errors = [abs(float(row["cycle_relative_error"])) / 1e-15 for row in rows]
    energy_errors = [abs(float(row["energy_relative_error"])) / 1e-15 for row in rows]

    plt.rcParams.update(
        {
            "font.family": "DejaVu Sans",
            "font.size": 8.6,
            "axes.titlesize": 10.0,
            "axes.labelsize": 8.7,
            "xtick.labelsize": 6.5,
            "ytick.labelsize": 7.4,
            "legend.fontsize": 7.6,
            "axes.linewidth": 0.65,
        }
    )
    fig = plt.figure(figsize=(7.25, 5.00), facecolor="white")
    outer = fig.add_gridspec(3, 1, height_ratios=(0.15, 1.0, 1.0), hspace=0.31)
    band_axis = fig.add_subplot(outer[0])
    cycle_grid = outer[1].subgridspec(2, 1, height_ratios=(1.0, 0.18), hspace=0.06)
    energy_grid = outer[2].subgridspec(2, 1, height_ratios=(1.0, 0.18), hspace=0.06)
    cycle_axis = fig.add_subplot(cycle_grid[0])
    cycle_error_axis = fig.add_subplot(cycle_grid[1], sharex=cycle_axis)
    energy_axis = fig.add_subplot(energy_grid[0], sharex=cycle_axis)
    energy_error_axis = fig.add_subplot(energy_grid[1], sharex=cycle_axis)

    band_axis.set_xlim(-0.55, 46.55)
    band_axis.set_ylim(0, 1)
    band_colors = ("#E7EEF3", "#F3EFE6")
    for family_index, (family, start, end) in enumerate(ranges):
        band_axis.add_patch(
            Rectangle(
                (start - 0.5, 0),
                end - start + 1,
                1,
                facecolor=band_colors[family_index % 2],
                edgecolor="white",
                linewidth=0.7,
            )
        )
        band_axis.text(
            (start + end) / 2,
            0.5,
            FAMILY_LABELS[family],
            ha="center",
            va="center",
            fontsize=7.5,
            fontweight="medium",
            color="#252A2E",
        )
    band_axis.set_axis_off()

    legend_handles = (
        Line2D(
            (0,),
            (0,),
            marker="o",
            linestyle="none",
            markerfacecolor="white",
            markeredgecolor=reference_color,
            markeredgewidth=1.15,
            label="Timeloop (a) / Accelergy (b)",
        ),
        Line2D(
            (0,),
            (0,),
            marker="D",
            linestyle="none",
            markerfacecolor=stage_color,
            markeredgecolor="white",
            label="STAGE matched",
        ),
        Rectangle((0, 0), 1, 1, facecolor=error_color, edgecolor="none", label="Absolute relative error"),
    )
    fig.legend(
        handles=legend_handles,
        loc="upper center",
        bbox_to_anchor=(0.5, 0.995),
        ncol=3,
        frameon=False,
        handletextpad=0.55,
        columnspacing=1.6,
    )

    style_main_axis(cycle_axis, ranges)
    draw_pairs(cycle_axis, timeloop_cycles, stage_cycles, reference_color, stage_color)
    cycle_axis.set_ylabel("Cycles (log)")
    cycle_axis.set_title("(a) Matched compute cycles", loc="left", pad=5, fontweight="medium")
    cycle_axis.text(
        0.995,
        1.02,
        "47/47 exact",
        transform=cycle_axis.transAxes,
        ha="right",
        va="bottom",
        fontsize=7.5,
        color="#4A5055",
    )
    style_error_axis(cycle_error_axis, cycle_errors, ranges, error_color, False)
    cycle_error_axis.text(
        0.995,
        0.12,
        "all zero",
        transform=cycle_error_axis.transAxes,
        ha="right",
        va="bottom",
        fontsize=6.8,
        color="#4A5055",
    )

    style_main_axis(energy_axis, ranges)
    draw_pairs(energy_axis, accelergy_energy, stage_energy, reference_color, stage_color)
    energy_axis.set_ylabel("Energy (pJ, log)")
    energy_axis.set_title("(b) Matched shared 45-nm ERT energy", loc="left", pad=5, fontweight="medium")
    energy_axis.text(
        0.995,
        1.02,
        f"max |relative error| = {max(abs(float(row['energy_relative_error'])) for row in rows):.2e}",
        transform=energy_axis.transAxes,
        ha="right",
        va="bottom",
        fontsize=7.5,
        color="#4A5055",
    )
    style_error_axis(energy_error_axis, energy_errors, ranges, error_color, False)
    energy_error_axis.set_xlabel("Matched configurations by workload family", labelpad=5)
    cycle_error_axis.tick_params(axis="x", bottom=False, labelbottom=False)

    fig.subplots_adjust(left=0.102, right=0.995, top=0.935, bottom=0.085)
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=300, facecolor="white")
    if pdf_output is not None:
        pdf_output.parent.mkdir(parents=True, exist_ok=True)
        fig.savefig(
            pdf_output,
            format="pdf",
            facecolor="white",
            metadata={
                "Title": "STAGE Figure 2 - Matched 47 paired configurations",
                "Author": "STAGE artifact pipeline",
                "Subject": "RF-C-MATCHED-47BAR-V1",
                "Keywords": "Timeloop, Accelergy, STAGE, matched validation",
                "CreationDate": datetime(2026, 7, 18, tzinfo=timezone.utc),
                "ModDate": datetime(2026, 7, 18, tzinfo=timezone.utc),
            },
        )
    plt.close(fig)
    return output


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root")
    args = parser.parse_args()
    output = draw(find_repo_root(args.repo_root))
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
