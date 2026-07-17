#!/usr/bin/env python3
"""Build a compact cross-tool view of the MNIST precision experiment."""

from __future__ import annotations

import csv
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path


REPO = Path(__file__).resolve().parents[3]
STAGE_CSV = (
    REPO
    / "experiments"
    / "aspdac"
    / "results"
    / "mnist_pe_precision_20260717"
    / "summary"
    / "mnist_pe_precision_paired.csv"
)
ROOT = (
    REPO
    / "experiments"
    / "aspdac"
    / "results"
    / "mnist_precision_external_20260717"
)
TIMELOOP_CSV = ROOT / "timeloop" / "summary" / "timeloop_precision_trend.csv"
ZIGZAG_CSV = ROOT / "zigzag" / "summary" / "zigzag_precision_trend.csv"
SUMMARY_DIR = ROOT / "summary"


def read_rows(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def pct(value: str | float | None) -> str:
    if value in (None, ""):
        return ""
    return f"{float(value):.6f}"


def main() -> None:
    stage = read_rows(STAGE_CSV)
    timeloop = read_rows(TIMELOOP_CSV)
    zigzag = read_rows(ZIGZAG_CSV)

    rows: list[dict[str, str]] = []
    for item in stage:
        rows.append(
            {
                "tool": "STAGE",
                "mode": "system_runtime",
                "profile_id": item["profile_id"],
                "payload_bits": item["profile_id"].split("_")[0].removeprefix("fp"),
                "accumulator_bits": item["profile_id"].split("a")[-1],
                "external_traffic_metric": "packetized_bits",
                "external_traffic_bits": item["packetized_bits"],
                "external_traffic_reduction_vs_fp32_percent": pct(
                    item["traffic_reduction_vs_fp32_percent"]
                ),
                "internal_traffic_reduction_vs_fp32_percent": "",
                "energy_reduction_vs_fp32_percent": "",
                "cycle_or_latency_reduction_vs_fp32_percent": pct(
                    item["cycle_reduction_vs_fp32_percent"]
                ),
                "mapping_policy": "fixed lowering and mapping",
                "boundary": "traffic is dense logical payload packetized at 128 bits",
            }
        )

    for item in timeloop:
        rows.append(
            {
                "tool": "Timeloop",
                "mode": item["mode"],
                "profile_id": item["profile_id"],
                "payload_bits": item["payload_bits"],
                "accumulator_bits": item["accumulator_bits"],
                "external_traffic_metric": "dram_traffic_bits",
                "external_traffic_bits": item["dram_traffic_bits"],
                "external_traffic_reduction_vs_fp32_percent": pct(
                    item["dram_traffic_reduction_vs_fp32_percent"]
                ),
                "internal_traffic_reduction_vs_fp32_percent": pct(
                    item["hierarchy_traffic_reduction_vs_fp32_percent"]
                ),
                "energy_reduction_vs_fp32_percent": pct(
                    item["energy_reduction_vs_fp32_percent"]
                ),
                "cycle_or_latency_reduction_vs_fp32_percent": pct(
                    item["cycle_reduction_vs_fp32_percent"]
                ),
                "mapping_policy": (
                    "reuse exact FP32 mapping"
                    if item["mode"] == "frozen_fp32_mapping"
                    else "independent mapper search per profile"
                ),
                "boundary": "reference action energy; fixed 4x4 PE count and bit capacities/bandwidths",
            }
        )

    for item in zigzag:
        rows.append(
            {
                "tool": "ZigZag",
                "mode": "loma_optimized_mapping",
                "profile_id": item["profile_id"],
                "payload_bits": item["payload_bits"],
                "accumulator_bits": item["accumulator_bits"],
                "external_traffic_metric": "l3_transaction_bits",
                "external_traffic_bits": item["l3_transaction_bits"],
                "external_traffic_reduction_vs_fp32_percent": pct(
                    item["l3_transaction_bits_reduction_vs_fp32_percent"]
                ),
                "internal_traffic_reduction_vs_fp32_percent": pct(
                    item["total_word_accesses_reduction_vs_fp32_percent"]
                ),
                "energy_reduction_vs_fp32_percent": pct(
                    item["energy_pj_reduction_vs_fp32_percent"]
                ),
                "cycle_or_latency_reduction_vs_fp32_percent": pct(
                    item["latency_cycles_reduction_vs_fp32_percent"]
                ),
                "mapping_policy": "LOMA optimized independently per profile",
                "boundary": "fixed reference costs and 0.04 pJ/op; no precision-aware compute circuit",
            }
        )

    SUMMARY_DIR.mkdir(parents=True, exist_ok=True)
    output_csv = SUMMARY_DIR / "mnist_precision_cross_tool_trend.csv"
    with output_csv.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    def select(
        source: list[dict[str, str]],
        profile: str,
        mode: str | None = None,
    ) -> dict[str, str]:
        return next(
            item
            for item in source
            if item["profile_id"] == profile
            and (mode is None or item.get("mode") == mode)
        )

    md_lines = [
        "# MNIST PE precision cross-tool findings",
        "",
        "Generated from completed STAGE, Timeloop, and ZigZag runs. Reductions are relative to FP32/A32 within each tool and mode.",
        "",
        "## Why STAGE shows exactly 50% and 75%",
        "",
        "STAGE first counts dense input, weight, and output elements, multiplies that fixed count by payload precision, then rounds each layer to 128-bit packets. The MNIST aggregate is so large that the remaining padding is negligible (FP8 is only 128 bits above an exact quarter across 2.33 billion bits), so the result displays as 50.00% and 75.00%. This is an intentional transport model result, not an independently discovered compression ratio.",
        "",
        "## Observed reductions",
        "",
        "| Tool / mode | Profile | External traffic | Internal hierarchy / accesses | Energy | Cycles / latency |",
        "|---|---:|---:|---:|---:|---:|",
    ]

    for profile in ("fp16_a16", "fp8_a16", "fp8_a8"):
        s = select(stage, profile)
        md_lines.append(
            f"| STAGE system | {profile} | {float(s['traffic_reduction_vs_fp32_percent']):.2f}% | n/a | n/a | {float(s['cycle_reduction_vs_fp32_percent']):.2f}% |"
        )
    for profile in ("fp16_a16", "fp8_a16", "fp8_a8"):
        t = select(timeloop, profile, "precision_optimized_mapping")
        md_lines.append(
            f"| Timeloop re-mapped | {profile} | {float(t['dram_traffic_reduction_vs_fp32_percent']):.2f}% | {float(t['hierarchy_traffic_reduction_vs_fp32_percent']):.2f}% | {float(t['energy_reduction_vs_fp32_percent']):.2f}% | {float(t['cycle_reduction_vs_fp32_percent']):.2f}% |"
        )
    for profile in ("fp16_a16", "fp8_a16", "fp8_a8"):
        z = select(zigzag, profile)
        md_lines.append(
            f"| ZigZag LOMA | {profile} | {float(z['l3_transaction_bits_reduction_vs_fp32_percent']):.2f}% | {float(z['total_word_accesses_reduction_vs_fp32_percent']):.2f}% | {float(z['energy_pj_reduction_vs_fp32_percent']):.2f}% | {float(z['latency_cycles_reduction_vs_fp32_percent']):.2f}% |"
        )

    md_lines.extend(
        [
            "",
            "## Interpretation",
            "",
            "- All three models confirm the same first-order off-chip payload trend: dense FP16 traffic is about half of FP32 and dense FP8 traffic is about one quarter.",
            "- Timeloop's independent per-precision search moves DRAM traffic away from the exact ratios to 51.53% and 75.76%, and its hierarchy traffic distinguishes FP8/A16 (67.79%) from FP8/A8 (77.31%). This is mapping and accumulator reuse, not simple arithmetic.",
            "- ZigZag is the clearest system-level caution: L3 transfer bits fall by about 50%/75%, while total word accesses fall only 10.90% to 11.71% and latency falls only about 14.16%. Partial sums, compute, and inner-memory service remain.",
            "- Timeloop cycles remain unchanged because this setup deliberately holds MAC throughput constant. ZigZag latency changes because its memory scheduling and utilization react to precision. Neither tool validates numerical accuracy; the STAGE PE arithmetic harness remains the accuracy authority.",
            "- The defensible claim is: STAGE models precision-proportional dense payload traffic and exposes its system-cycle consequence under the configured transport. It should not be phrased as proving that all traffic, energy, or runtime improve by exactly 50%/75%.",
            "",
        ]
    )
    output_md = SUMMARY_DIR / "mnist_precision_cross_tool_findings.md"
    output_md.write_text("\n".join(md_lines), encoding="utf-8")

    manifest = {
        "schema": "aspg.mnist_precision_cross_tool.v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_files": {
            str(STAGE_CSV.relative_to(REPO)).replace("\\", "/"): sha256(STAGE_CSV),
            str(TIMELOOP_CSV.relative_to(REPO)).replace("\\", "/"): sha256(TIMELOOP_CSV),
            str(ZIGZAG_CSV.relative_to(REPO)).replace("\\", "/"): sha256(ZIGZAG_CSV),
        },
        "row_count": len(rows),
        "outputs": [output_csv.name, output_md.name],
        "claim_boundary": "trend comparison only; no cross-tool absolute calibration or RTL-equivalent energy claim",
    }
    (SUMMARY_DIR / "manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    main()
