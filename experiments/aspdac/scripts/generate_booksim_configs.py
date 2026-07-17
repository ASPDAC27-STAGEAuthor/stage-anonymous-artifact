#!/usr/bin/env python3
"""Materialize the frozen BookSim2 saturation configs and verify their SHA-256 ledger."""

from __future__ import annotations

import argparse
import csv
import hashlib
from pathlib import Path

BASE_RATES = [0.005, 0.010, 0.020, 0.040, 0.060, 0.080, 0.10, 0.12, 0.16, 0.20, 0.24, 0.28, 0.32]
UNIFORM_EXTENSION_RATES = [0.36]
TRAFFIC = {
    "uniform": "uniform",
    "transpose": "transpose",
    "bit_complement": "bitcomp",
    "hotspot_node5": "hotspot({5})",
}
ROOT = Path(__file__).resolve().parents[3]
DEFAULT_LEDGER = ROOT / "experiments" / "aspdac" / "external_inputs" / "booksim_config_hashes.csv"


def config(booksim_traffic: str, rate: float, seed: int) -> bytes:
    text = f"""topology = mesh;
k = 4;
n = 2;
routing_function = dim_order;
num_vcs = 1;
vc_buf_size = 16;
wait_for_tail_credit = 0;
traffic = {booksim_traffic};
injection_rate = {rate:.3f};
packet_size = 1;
seed = {seed};
sim_type = latency;
sample_period = 1000;
warmup_periods = 3;
max_samples = 10;
"""
    return text.encode("utf-8")


def records() -> list[dict[str, str]]:
    result: list[dict[str, str]] = []
    for label, booksim_name in TRAFFIC.items():
        for rate in BASE_RATES:
            for seed in range(10):
                name = f"{label}_inj_{rate:.3f}_seed_{seed}.cfg"
                payload = config(booksim_name, rate, seed)
                result.append({
                    "suite": "registered",
                    "filename": name,
                    "traffic": label,
                    "injection_rate": f"{rate:.3f}",
                    "seed": str(seed),
                    "sha256": hashlib.sha256(payload).hexdigest(),
                })
    for rate in UNIFORM_EXTENSION_RATES:
        for seed in range(10):
            name = f"uniform_inj_{rate:.3f}_seed_{seed}.cfg"
            payload = config("uniform", rate, seed)
            result.append({
                "suite": "uniform_extension",
                "filename": name,
                "traffic": "uniform",
                "injection_rate": f"{rate:.3f}",
                "seed": str(seed),
                "sha256": hashlib.sha256(payload).hexdigest(),
            })
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--ledger", type=Path, default=DEFAULT_LEDGER)
    parser.add_argument("--write-ledger", action="store_true", help="maintainer-only: create the frozen ledger")
    args = parser.parse_args()

    expected = records()
    if args.output.exists() and any(args.output.iterdir()):
        raise SystemExit(f"Refusing non-empty output directory: {args.output}")
    args.output.mkdir(parents=True, exist_ok=True)
    for row in expected:
        payload = config(TRAFFIC[row["traffic"]], float(row["injection_rate"]), int(row["seed"]))
        (args.output / row["filename"]).write_bytes(payload)

    if args.write_ledger:
        args.ledger.parent.mkdir(parents=True, exist_ok=True)
        with args.ledger.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(expected[0]))
            writer.writeheader()
            writer.writerows(expected)
    else:
        with args.ledger.open(newline="", encoding="utf-8-sig") as stream:
            locked = list(csv.DictReader(stream))
        if locked != expected:
            raise SystemExit("BookSim config ledger mismatch")

    print(f"BookSim config verification passed: {len(expected)}/{len(expected)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
