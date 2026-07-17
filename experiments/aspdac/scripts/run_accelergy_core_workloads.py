#!/usr/bin/env python3
"""Run the repaired Timeloop+Accelergy energy baseline in WSL.

The output directory must be empty so failed runs cannot be masked by stale ERT or
statistics files. STAGE comparison columns are intentionally left blank.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import re
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

import yaml

TIMELOOP = Path("/opt/stage-tools/bin/timeloop-mapper")
TIMELOOP_REPO = Path("/opt/stage-baselines/tools/accelergy-timeloop-infrastructure")
TIMELOOP_LIB = TIMELOOP_REPO / "src/timeloop/lib"
TEMPLATE = Path("experiments/aspdac/baseline_tools/timeloop_accelergy/gemm_256_16pe_accelergy.yaml")
WORKLOADS = [
    ("gemm_256", "GEMM 256^3", 256, 256, 256),
    ("mlp_l1", "MLP L1", 128, 256, 512),
    ("mlp_l2", "MLP L2", 128, 128, 256),
    ("attention_qk", "Attention QK^T", 128, 128, 64),
    ("attention_pv", "Attention PV", 128, 64, 128),
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def last_float(pattern: str, text: str):
    values = re.findall(pattern, text, flags=re.MULTILINE)
    return float(values[-1]) if values else None


def parse_stats(path: Path) -> dict:
    text = path.read_text(encoding="utf-8", errors="replace")
    summary = text.split("Summary Stats", 1)[-1]
    result = {
        "cycles": last_float(r"^Cycles:\s+([0-9.eE+-]+)", summary),
        "utilization_pct": last_float(r"^Utilization:\s+([0-9.eE+-]+)%", summary),
        "energy_uj": last_float(r"^Energy:\s+([0-9.eE+-]+) uJ", summary),
        "computes": last_float(r"^Computes\s*=\s*([0-9.eE+-]+)", summary),
    }
    for level, key in [
        ("Registers", "register_accesses"),
        ("LocalBuffer", "local_buffer_accesses"),
        ("GlobalBuffer", "global_buffer_accesses"),
        ("DRAM", "dram_accesses"),
    ]:
        match = re.search(
            rf"=== {level} ===\s+Total scalar accesses\s+:\s+([0-9.eE+-]+)",
            text,
            flags=re.MULTILINE,
        )
        result[key] = float(match.group(1)) if match else None
    return result


def write_csv(path: Path, rows: list[dict]) -> None:
    if not rows:
        raise RuntimeError(f"No rows for {path}")
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def configured_deck(template: str, m: int, n: int, k: int) -> str:
    result = template
    for dimension, value in (("M", m), ("N", n), ("K", k)):
        result, count = re.subn(
            rf"(?m)^  {dimension}:\s+\d+\s*$",
            f"  {dimension}: {value}",
            result,
            count=1,
        )
        if count != 1:
            raise RuntimeError(f"Could not set problem dimension {dimension}")
    return result


def git_rev(path: Path) -> str:
    cp = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=path, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
    )
    return cp.stdout.strip() if cp.returncode == 0 else "unknown"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    repo = Path(args.repo_root).resolve()
    output = Path(args.output).resolve()
    if output.exists() and any(output.iterdir()):
        raise SystemExit(f"Refusing non-empty output directory: {output}")
    output.mkdir(parents=True, exist_ok=True)

    template_path = repo / TEMPLATE
    template = template_path.read_text(encoding="utf-8")
    env = dict(os.environ)
    env["PATH"] = "/opt/stage-tools/bin:" + env.get("PATH", "")
    env["LD_LIBRARY_PATH"] = ":".join([
        str(TIMELOOP_LIB), "/usr/local/lib", "/usr/lib/x86_64-linux-gnu",
        env.get("LD_LIBRARY_PATH", ""),
    ])

    rows = []
    action_rows = None
    started = datetime.now(timezone.utc).isoformat()
    for case_id, label, m, n, k in WORKLOADS:
        case = output / case_id
        case.mkdir()
        deck = case / f"{case_id}_timeloop_accelergy.yaml"
        deck.write_text(configured_deck(template, m, n, k), encoding="utf-8", newline="\n")

        before = time.perf_counter()
        cp = subprocess.run(
            [str(TIMELOOP), str(deck)], cwd=case, env=env, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=900, check=False,
        )
        elapsed = time.perf_counter() - before
        combined = cp.stdout + "\n--- STDERR ---\n" + cp.stderr
        (case / "timeloop_mapper.stdout.log").write_text(
            combined, encoding="utf-8", newline="\n"
        )

        stats_path = case / "timeloop-mapper.stats.txt"
        ert_path = case / "timeloop-mapper.ERT.yaml"
        accelergy_logs = sorted(case.glob("*.accelergy.log"))
        accelergy_text = "\n".join(
            path.read_text(encoding="utf-8", errors="replace")
            for path in accelergy_logs
        )
        metrics = parse_stats(stats_path) if stats_path.exists() else {}
        tables = []
        if ert_path.exists():
            tables = yaml.safe_load(ert_path.read_text(encoding="utf-8"))["ERT"]["tables"]
        dummy = "dummy estimated" in accelergy_text
        schema_error = "key not found: tables" in combined
        completed = (
            cp.returncode == 0 and bool(metrics) and len(tables) == 5
            and not dummy and not schema_error
        )
        rows.append({
            "case_id": case_id,
            "label": label,
            "M": m,
            "N": n,
            "K": k,
            "expected_macs": m * n * k,
            "status": "completed" if completed else "failed",
            "return_code": cp.returncode,
            "elapsed_seconds": round(elapsed, 6),
            "ert_table_count": len(tables),
            "dummy_action_estimate": dummy,
            "schema_error_key_not_found_tables": schema_error,
            "config_sha256": sha256(deck),
            **metrics,
            "stage_cycles": "",
            "stage_energy_uj": "",
        })
        if action_rows is None and tables:
            action_rows = []
            for table in tables:
                component = table["name"]
                estimator = (
                    "CACTI-DRAM" if component.endswith("DRAM") else
                    "CACTI-SRAM" if "Buffer" in component else
                    "Aladdin"
                )
                for action in table["actions"]:
                    action_rows.append({
                        "component": component,
                        "action": action["name"],
                        "energy_pj": action["energy"],
                        "estimator": estimator,
                        "stage_energy_pj": "",
                    })

    write_csv(output / "timeloop_accelergy_summary.csv", rows)
    write_csv(output / "action_reference.csv", action_rows or [])
    manifest = {
        "schema": "aspdac.timeloop_accelergy_baseline.v1",
        "started_at_utc": started,
        "completed_at_utc": datetime.now(timezone.utc).isoformat(),
        "template": str(TEMPLATE),
        "template_sha256": sha256(template_path),
        "timeloop_infrastructure_git": git_rev(TIMELOOP_REPO),
        "accelergy_version": "0.4",
        "technology": "45nm",
        "datawidth_bits": 16,
        "estimators": {
            "DRAM": "CactiDRAM, plugin-reported 80% accuracy",
            "SRAM": "CactiSRAM, plugin-reported 80% accuracy",
            "MAC_and_registers": "Aladdin table, plugin-reported 70% accuracy",
        },
        "completed_cases": sum(row["status"] == "completed" for row in rows),
        "total_cases": len(rows),
        "stage_columns": "intentionally blank",
        "claim_boundary": (
            "Reference-model energy under the stated estimator assumptions; "
            "not silicon-calibrated absolute power evidence."
        ),
    }
    (output / "manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps({"manifest": manifest, "rows": rows}, indent=2))
    if manifest["completed_cases"] != manifest["total_cases"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()