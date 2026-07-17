#!/usr/bin/env python3
"""Run immutable BookSim2 uniform-extension points into the final ASP-DAC bundle."""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import json
import re
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path


BOOKSIM = Path("/opt/stage-baselines/tools/booksim2/src/booksim")
BOOKSIM_REPO = BOOKSIM.parents[1]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def last_float(pattern: str, text: str) -> float | None:
    values = re.findall(pattern, text, flags=re.MULTILINE)
    return float(values[-1]) if values else None


def config(rate: float, seed: int) -> str:
    return f"""topology = mesh;
k = 4;
n = 2;
routing_function = dim_order;
num_vcs = 1;
vc_buf_size = 16;
wait_for_tail_credit = 0;
traffic = uniform;
injection_rate = {rate:.3f};
packet_size = 1;
seed = {seed};
sim_type = latency;
sample_period = 1000;
warmup_periods = 3;
max_samples = 10;
"""


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--rates", type=float, nargs="+", required=True)
    args = parser.parse_args()
    bundle = args.bundle.resolve()
    raw = bundle / "raw/external_booksim_uniform_extension"
    configs = raw / "configs"
    logs = raw / "logs"
    summary = bundle / "summary"
    manifests = bundle / "manifests"
    for directory in (configs, logs, summary, manifests):
        directory.mkdir(parents=True, exist_ok=True)
    rows: list[dict[str, object]] = []
    commands: list[dict[str, object]] = []
    for rate in args.rates:
        for seed in range(10):
            token = f"uniform_inj_{rate:.3f}_seed_{seed}"
            cfg = configs / f"{token}.cfg"
            cfg.write_text(config(rate, seed), encoding="utf-8", newline="\n")
            command = [str(BOOKSIM), str(cfg)]
            started = time.perf_counter()
            completed = subprocess.run(command, cwd=configs, capture_output=True, text=True, timeout=120, check=False)
            elapsed = time.perf_counter() - started
            combined = completed.stdout + "\n--- STDERR ---\n" + completed.stderr
            log = logs / f"{token}.log.gz"
            with gzip.open(log, "wt", encoding="utf-8") as stream:
                stream.write(combined)
            latency = last_float(r"Packet latency average = ([0-9.eE+-]+)", completed.stdout)
            accepted = last_float(r"Accepted packet rate average = ([0-9.eE+-]+)", completed.stdout)
            injected = last_float(r"Injected packet rate average = ([0-9.eE+-]+)", completed.stdout)
            unstable = "Simulation unstable" in completed.stdout or "exceeded 500 cycles" in completed.stdout
            status = "unstable" if unstable else ("completed" if latency is not None and accepted is not None else "failed")
            rows.append(
                {
                    "traffic": "uniform",
                    "injection_rate": rate,
                    "seed": seed,
                    "status": status,
                    "return_code": completed.returncode,
                    "packet_latency_avg": latency,
                    "injected_packet_rate_avg": injected,
                    "accepted_packet_rate_avg": accepted,
                    "accepted_offered_ratio": accepted / rate if accepted is not None else None,
                    "elapsed_seconds": round(elapsed, 6),
                    "config_hash": sha256(cfg),
                    "log_hash": sha256(log),
                    "config_path": str(cfg),
                    "log_path": str(log),
                }
            )
            commands.append({"candidate": token, "command": command, "elapsed_seconds": elapsed})
            print(f"[{len(rows)}/{len(args.rates) * 10}] {token} {status}", flush=True)
    csv_path = summary / "rq2_booksim_uniform_extension_raw.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    git = subprocess.run(["git", "-C", str(BOOKSIM_REPO), "rev-parse", "HEAD"], capture_output=True, text=True, check=False).stdout.strip()
    manifest = {
        "schema_version": "aspg-external-run-manifest-1.0",
        "created_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "tool": "BookSim2",
        "tool_path": str(BOOKSIM),
        "tool_git_commit": git or "unknown",
        "traffic": "uniform",
        "rates": args.rates,
        "seeds": list(range(10)),
        "topology": "4x4 mesh; XY; 1 VC; 16 flits/VC; one-flit packet",
        "commands": commands,
        "result_csv": str(csv_path),
        "result_csv_sha256": sha256(csv_path),
        "case_count": len(rows),
        "failed_count": sum(row["status"] == "failed" for row in rows),
    }
    (manifests / "rq2_booksim_uniform_extension_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return 1 if manifest["failed_count"] else 0


if __name__ == "__main__":
    raise SystemExit(main())

