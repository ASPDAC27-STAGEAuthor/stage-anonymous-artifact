#!/usr/bin/env python3
"""Run frozen Timeloop mappings with timeloop-model for final ASP-DAC evidence."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import re
import shutil
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

WORKLOADS = {
    "gemm_256": ("gemm_256_16pe.yaml", 256, 256, 256),
    "mlp_l1": ("mlp_512_256_batch128_layer1.yaml", 128, 256, 512),
    "mlp_l2": ("mlp_512_256_batch128_layer2.yaml", 128, 128, 256),
    "attention_qk": ("attention_128_64_qk.yaml", 128, 128, 64),
    "attention_pv": ("attention_128_64_prob_v.yaml", 128, 64, 128),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def last_float(pattern: str, text: str) -> float | None:
    values = re.findall(pattern, text, flags=re.MULTILINE)
    return float(values[-1]) if values else None


def parse_stats(path: Path) -> dict[str, float | None]:
    text = path.read_text(encoding="utf-8", errors="replace")
    summary = text.split("Summary Stats", 1)[-1]
    metrics: dict[str, float | None] = {
        "cycles": last_float(r"^Cycles:\s+([0-9.eE+-]+)", summary),
        "utilization_pct": last_float(r"^Utilization:\s+([0-9.eE+-]+)%", summary),
        "energy_uj_uncalibrated": last_float(r"^Energy:\s+([0-9.eE+-]+) uJ", summary),
        "computes": last_float(r"^Computes\s*=\s*([0-9.eE+-]+)", summary),
    }
    for level, key in (("Registers", "register_accesses"), ("LocalBuffer", "local_buffer_accesses"), ("GlobalBuffer", "global_buffer_accesses"), ("DRAM", "dram_accesses")):
        match = re.search(rf"=== {level} ===\s+Total scalar accesses\s+:\s+([0-9.eE+-]+)", text, flags=re.MULTILINE)
        metrics[key] = float(match.group(1)) if match else None
    return metrics


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeloop-model", type=Path, default=Path("/opt/stage-tools/bin/timeloop-model"))
    parser.add_argument("--timeloop-repo", type=Path, default=Path("/opt/stage-baselines/tools/accelergy-timeloop-infrastructure"))
    args = parser.parse_args()
    if args.output.exists() and any(args.output.iterdir()):
        raise SystemExit(f"Refusing non-empty output: {args.output}")
    args.output.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ)
    env["LD_LIBRARY_PATH"] = ":".join((str(args.timeloop_repo / "src/timeloop/lib"), "/usr/local/lib", "/usr/lib/x86_64-linux-gnu", env.get("LD_LIBRARY_PATH", "")))
    rows: list[dict[str, object]] = []
    commands: list[dict[str, object]] = []
    for case_id, (deck_name, m, n, k) in WORKLOADS.items():
        source_case = args.source / case_id
        case = args.output / case_id
        case.mkdir()
        deck = case / deck_name
        mapping = case / "timeloop-mapper.map.yaml"
        shutil.copy2(source_case / deck_name, deck)
        shutil.copy2(source_case / mapping.name, mapping)
        command = [str(args.timeloop_model), str(deck), str(mapping)]
        started = time.perf_counter()
        completed = subprocess.run(command, cwd=case, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=900, check=False)
        elapsed = time.perf_counter() - started
        log = case / "timeloop-model.stdout.log"
        log.write_text(completed.stdout + "\n--- STDERR ---\n" + completed.stderr, encoding="utf-8")
        stats = case / "timeloop-model.stats.txt"
        metrics = parse_stats(stats) if stats.exists() else {}
        macs = m * n * k
        status = "completed" if completed.returncode == 0 and metrics.get("cycles") is not None else "failed"
        rows.append({"case_id": case_id, "M": m, "N": n, "K": k, "expected_macs": macs, "analytical_16mac_floor_cycles": macs // 16, "status": status, "return_code": completed.returncode, "elapsed_seconds": round(elapsed, 6), "deck_sha256": sha256(deck), "mapping_sha256": sha256(mapping), "stats_sha256": sha256(stats) if stats.exists() else "", **metrics})
        commands.append({"case_id": case_id, "command": command, "cwd": str(case), "return_code": completed.returncode, "stdout_log": str(log)})
    summary = args.output / "timeloop_model_summary.csv"
    with summary.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=args.timeloop_repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=False).stdout.strip()
    files = sorted(path for path in args.output.rglob("*") if path.is_file())
    manifest = {"schema_version": "aspg-timeloop-model-final-1.0", "completed_utc": datetime.now(timezone.utc).isoformat(), "tool": str(args.timeloop_model), "timeloop_git": revision or "unknown", "source": str(args.source), "commands": commands, "cases": len(rows), "completed_cases": sum(row["status"] == "completed" for row in rows), "files": [{"path": str(path.relative_to(args.output)), "sha256": sha256(path), "bytes": path.stat().st_size} for path in files]}
    (args.output / "timeloop_model_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"cases": len(rows), "completed": manifest["completed_cases"], "output": str(args.output)}, indent=2))
    return 0 if manifest["completed_cases"] == len(rows) else 1


if __name__ == "__main__":
    raise SystemExit(main())
