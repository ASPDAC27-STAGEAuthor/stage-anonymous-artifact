#!/usr/bin/env python3
"""Run the RQ3 size matrix with real WSL Timeloop and Accelergy-backed Timeloop.

The script is intended to run inside WSL.  It creates immutable per-case input
decks, an explicit mapping in the frozen 16-MAC schedule family, raw tool
outputs, per-case metadata, top-level CSV summaries, manifests, and retained
failure records.  Existing completed cases are resumed rather than overwritten.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import platform
import re
import socket
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import yaml


DEFAULT_TIMELOOP_MODEL = Path("/opt/stage-tools/bin/timeloop-model")
DEFAULT_TIMELOOP_REPO = Path(
    "/opt/stage-baselines/tools/accelergy-timeloop-infrastructure"
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def relative(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def run_capture(command: list[str], *, cwd: Path | None = None, env: dict[str, str] | None = None) -> dict[str, Any]:
    started = time.perf_counter()
    completed = subprocess.run(
        command,
        cwd=cwd,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    return {
        "command": command,
        "cwd": str(cwd) if cwd else None,
        "return_code": completed.returncode,
        "elapsed_seconds": round(time.perf_counter() - started, 6),
        "stdout": completed.stdout,
        "stderr": completed.stderr,
    }


def last_float(pattern: str, text: str) -> float | None:
    values = re.findall(pattern, text, flags=re.MULTILINE)
    return float(values[-1]) if values else None


def parse_stats(path: Path) -> dict[str, float | None]:
    text = path.read_text(encoding="utf-8", errors="replace")
    summary = text.split("Summary Stats", 1)[-1]
    result: dict[str, float | None] = {
        "cycles": last_float(r"^Cycles:\s+([0-9.eE+-]+)", summary),
        "utilization_pct": last_float(r"^Utilization:\s+([0-9.eE+-]+)%", summary),
        "energy_uj": last_float(r"^Energy:\s+([0-9.eE+-]+) uJ", summary),
        "computes": last_float(r"^Computes\s*=\s*([0-9.eE+-]+)", summary),
    }
    for level, key in (
        ("Registers", "register_accesses"),
        ("LocalBuffer", "local_buffer_accesses"),
        ("GlobalBuffer", "global_buffer_accesses"),
        ("DRAM", "dram_accesses"),
    ):
        match = re.search(
            rf"=== {level} ===\s+Total scalar accesses\s+:\s+([0-9.eE+-]+)",
            text,
            flags=re.MULTILINE,
        )
        result[key] = float(match.group(1)) if match else None
    return result


def configure_problem(template: str, m: int, n: int, k: int) -> str:
    configured = template
    for name, value in (("M", m), ("N", n), ("K", k)):
        configured, count = re.subn(
            rf"(?m)^  {name}:\s+\d+\s*$",
            f"  {name}: {value}",
            configured,
            count=1,
        )
        if count != 1:
            raise RuntimeError(f"Could not set problem dimension {name}")
    return configured


def configure_mapping(template: str, m: int, n: int, k: int) -> str:
    if m % 4 or n % 4 or k % 16:
        raise RuntimeError(f"Dimensions cannot use frozen M4 N4 K1 / K16 mapping: {m}x{n}x{k}")
    configured, count = re.subn(
        r"(?m)(^\s+- target: DRAM\s*$\n^\s+type: temporal\s*$\n^\s+factors:)\s+M\d+\s+N\d+\s+K\d+\s*$",
        rf"\1 M{m // 4} N{n // 4} K{k // 16}",
        template,
        count=1,
    )
    if count != 1:
        raise RuntimeError("Could not set outer DRAM factors in mapping")
    return configured


def load_cases(matrix_path: Path) -> tuple[str, list[dict[str, Any]]]:
    matrix_text = matrix_path.read_text(encoding="utf-8")
    matrix = yaml.safe_load(matrix_text)
    cases = matrix.get("cases", [])
    if len(cases) != 9:
        raise RuntimeError(f"Expected 9 shared size cases, found {len(cases)}")
    for case in cases:
        expected = int(case["M"]) * int(case["N"]) * int(case["K"])
        if int(case["expected_macs"]) != expected:
            raise RuntimeError(f"MAC contract mismatch for {case['case_id']}")
    return hashlib.sha256(matrix_text.encode("utf-8")).hexdigest(), cases


def tool_identity(executable: Path, timeloop_repo: Path, env: dict[str, str]) -> dict[str, Any]:
    revision = run_capture(["git", "-C", str(timeloop_repo), "rev-parse", "HEAD"])
    git_version = run_capture(["git", "--version"])
    accelergy_python = Path("/opt/stage-baselines/venv/bin/python")
    accelergy_version = run_capture([str(accelergy_python), "-c", "import importlib.metadata as m; print(m.version('accelergy'))"], env=env)
    python_version = run_capture(["python3", "--version"])
    return {
        "timeloop_model": str(executable),
        "timeloop_model_sha256": sha256(executable),
        "timeloop_infrastructure_git": revision["stdout"].strip() if revision["return_code"] == 0 else "unknown",
        "accelergy_version": (accelergy_version["stdout"] + accelergy_version["stderr"]).strip(),
        "accelergy_version_command": accelergy_version["command"],
        "git_version": git_version["stdout"].strip(),
        "python_version": (python_version["stdout"] + python_version["stderr"]).strip(),
        "host": {
            "hostname": socket.gethostname(),
            "platform": platform.platform(),
            "machine": platform.machine(),
        },
    }


def validate_ert(path: Path, combined_log: str) -> dict[str, Any]:
    result = {
        "ert_exists": path.exists(),
        "ert_nonempty": path.exists() and path.stat().st_size > 0,
        "ert_table_count": 0,
        "all_tables_have_actions": False,
        "dummy_action_estimate": False,
        "schema_fallback": False,
    }
    ert_text = ""
    if path.exists():
        ert_text = path.read_text(encoding="utf-8", errors="replace")
        parsed = yaml.safe_load(ert_text) or {}
        tables = parsed.get("ERT", {}).get("tables", [])
        result["ert_table_count"] = len(tables)
        result["all_tables_have_actions"] = bool(tables) and all(table.get("actions") for table in tables)
    audit_text = (combined_log + "\n" + ert_text).lower()
    result["dummy_action_estimate"] = any(
        marker in audit_text for marker in ("dummy estimated", "dummy action", "dummy table")
    )
    result["schema_fallback"] = any(
        marker in audit_text for marker in ("key not found: tables", "schema fallback", "falling back to schema")
    )
    return result


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        raise RuntimeError(f"No rows for {path}")
    columns: list[str] = []
    for row in rows:
        for key in row:
            if key not in columns:
                columns.append(key)
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def write_failure(failures: Path, tool: str, case_id: str, result: dict[str, Any]) -> Path:
    failures.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    target = failures / f"size_scaling_{tool}_{case_id}_{stamp}.json"
    target.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return target


def completed_result(case_dir: Path) -> dict[str, Any] | None:
    result_path = case_dir / "case_result.json"
    if not result_path.exists():
        return None
    result = json.loads(result_path.read_text(encoding="utf-8"))
    return result if result.get("status") == "completed" else None


def run_case(
    *,
    tool: str,
    case: dict[str, Any],
    output_root: Path,
    failures: Path,
    repo_root: Path,
    plain_template: str,
    energy_template: str,
    mapping_template: str,
    executable: Path,
    env: dict[str, str],
    matrix_sha256: str,
) -> dict[str, Any]:
    case_id = str(case["case_id"])
    case_dir = output_root / case_id
    prior = completed_result(case_dir)
    if prior is not None:
        return prior
    if case_dir.exists() and any(case_dir.iterdir()):
        raise RuntimeError(f"Refusing to overwrite incomplete case directory: {case_dir}")
    case_dir.mkdir(parents=True, exist_ok=True)

    m, n, k = int(case["M"]), int(case["N"]), int(case["K"])
    mapping_path = case_dir / "mapping.yaml"
    mapping_path.write_text(configure_mapping(mapping_template, m, n, k), encoding="utf-8", newline="\n")
    if tool == "timeloop":
        deck_path = case_dir / "input_timeloop.yaml"
        deck_path.write_text(configure_problem(plain_template, m, n, k), encoding="utf-8", newline="\n")
    elif tool == "accelergy":
        deck_path = case_dir / "input_accelergy_45nm.yaml"
        deck_path.write_text(configure_problem(energy_template, m, n, k), encoding="utf-8", newline="\n")
    else:
        raise ValueError(tool)

    command = [str(executable), str(deck_path), str(mapping_path)]
    started_utc = utc_now()
    run = run_capture(command, cwd=case_dir, env=env)
    completed_utc = utc_now()
    log_path = case_dir / "tool.stdout.log"
    combined_log = run.pop("stdout") + "\n--- STDERR ---\n" + run.pop("stderr")
    log_path.write_text(combined_log, encoding="utf-8", newline="\n")
    command_record = {
        **run,
        "started_utc": started_utc,
        "completed_utc": completed_utc,
        "environment": {"LD_LIBRARY_PATH": env.get("LD_LIBRARY_PATH", ""), "PATH": env.get("PATH", "")},
    }
    (case_dir / "command.json").write_text(
        json.dumps(command_record, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )

    stats_path = case_dir / "timeloop-model.stats.txt"
    stats = parse_stats(stats_path) if stats_path.exists() else {}
    expected_macs = int(case["expected_macs"])
    common = {
        "case_id": case_id,
        "family": case["family"],
        "scale": case["scale"],
        "M": m,
        "N": n,
        "K": k,
        "expected_macs": expected_macs,
        "analytical_16mac_floor_cycles": expected_macs // 16,
        "matrix_sha256": matrix_sha256,
        "deck_sha256": sha256(deck_path),
        "mapping_sha256": sha256(mapping_path),
        "return_code": command_record["return_code"],
        "elapsed_seconds": command_record["elapsed_seconds"],
        "external_raw_path": relative(case_dir, repo_root),
        **stats,
    }
    if tool == "timeloop":
        completed = (
            command_record["return_code"] == 0
            and stats.get("cycles") is not None
            and stats.get("computes") == float(expected_macs)
            and stats.get("cycles") == float(expected_macs // 16)
        )
        result = {**common, "status": "completed" if completed else "failed"}
    else:
        ert_path = case_dir / "timeloop-model.ERT.yaml"
        ert = validate_ert(ert_path, combined_log)
        completed = (
            command_record["return_code"] == 0
            and stats.get("cycles") is not None
            and stats.get("computes") == float(expected_macs)
            and ert["ert_nonempty"]
            and ert["ert_table_count"] == 5
            and ert["all_tables_have_actions"]
            and not ert["dummy_action_estimate"]
            and not ert["schema_fallback"]
        )
        result = {
            **common,
            **ert,
            "status": "completed" if completed else "failed",
            "technology": "45nm",
            "energy_claim_boundary": "shared CACTI/Aladdin reference; not silicon-calibrated",
        }
    (case_dir / "case_result.json").write_text(
        json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    if not completed:
        failure_path = write_failure(failures, tool, case_id, result)
        result["failure_record"] = relative(failure_path, repo_root)
    return result


def build_manifest(
    *,
    schema: str,
    tool: str,
    output_root: Path,
    manifest_path: Path,
    repo_root: Path,
    matrix_path: Path,
    matrix_sha256: str,
    identity: dict[str, Any],
    rows: list[dict[str, Any]],
    started_utc: str,
) -> dict[str, Any]:
    files = sorted(path for path in output_root.rglob("*") if path.is_file())
    manifest = {
        "schema_version": schema,
        "tool_lane": tool,
        "phase": "Phase 10 IN_PROGRESS",
        "started_utc": started_utc,
        "completed_utc": utc_now(),
        "matrix": relative(matrix_path, repo_root),
        "matrix_sha256": matrix_sha256,
        "tool_identity": identity,
        "schedule_contract": "16 total MAC/cycle; M4 N4 K1 spatial; K16 LocalBuffer temporal; outer factors exactly cover M,N,K",
        "total_cases": len(rows),
        "completed_cases": sum(row.get("status") == "completed" for row in rows),
        "failed_cases": sum(row.get("status") != "completed" for row in rows),
        "files": [
            {"path": relative(path, repo_root), "sha256": sha256(path), "bytes": path.stat().st_size}
            for path in files
        ],
    }
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--matrix", type=Path, required=True)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--timeloop-model", type=Path, default=DEFAULT_TIMELOOP_MODEL)
    parser.add_argument("--timeloop-repo", type=Path, default=DEFAULT_TIMELOOP_REPO)
    parser.add_argument("--repo-commit", default="", help="Source repository commit when running from an isolated WSL staging copy")
    parser.add_argument("--limit", type=int, default=0, help="Run only the first N cases; completed cases remain resumable")
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    matrix_path = args.matrix.resolve()
    bundle = args.bundle.resolve()
    timeloop_root = bundle / "raw/size_scaling_timeloop"
    accelergy_root = bundle / "raw/size_scaling_accelergy"
    failures = bundle / "failures"
    timeloop_manifest_path = bundle / "manifests/size_scaling_timeloop_manifest.json"
    accelergy_manifest_path = bundle / "manifests/size_scaling_accelergy_manifest.json"
    timeloop_root.mkdir(parents=True, exist_ok=True)
    accelergy_root.mkdir(parents=True, exist_ok=True)

    matrix_sha256, cases = load_cases(matrix_path)
    if args.limit:
        cases = cases[: args.limit]
    plain_template_path = bundle / "raw/external_timeloop_model/gemm_256/gemm_256_16pe.yaml"
    mapping_template_path = bundle / "raw/external_timeloop_model/gemm_256/timeloop-mapper.map.yaml"
    energy_template_path = repo_root / "experiments/aspdac/baseline_tools/timeloop_accelergy/gemm_256_16pe_accelergy.yaml"
    plain_template = plain_template_path.read_text(encoding="utf-8")
    mapping_template = mapping_template_path.read_text(encoding="utf-8")
    energy_template = energy_template_path.read_text(encoding="utf-8")

    env = dict(os.environ)
    env["PATH"] = "/opt/stage-tools/bin:" + env.get("PATH", "")
    env["LD_LIBRARY_PATH"] = ":".join(
        (
            str(args.timeloop_repo / "src/timeloop/lib"),
            "/usr/local/lib",
            "/usr/lib/x86_64-linux-gnu",
            env.get("LD_LIBRARY_PATH", ""),
        )
    )
    identity = tool_identity(args.timeloop_model, args.timeloop_repo, env)
    runner_script = Path(__file__).resolve()
    identity["runner_script"] = relative(runner_script, repo_root)
    identity["runner_script_sha256"] = sha256(runner_script)
    if args.repo_commit:
        identity["repo_commit"] = args.repo_commit
    else:
        identity["repo_commit"] = run_capture(["git", "-C", str(repo_root), "rev-parse", "HEAD"])["stdout"].strip()
    identity["plain_template"] = relative(plain_template_path, repo_root)
    identity["plain_template_sha256"] = sha256(plain_template_path)
    identity["energy_template"] = relative(energy_template_path, repo_root)
    identity["energy_template_sha256"] = sha256(energy_template_path)
    identity["mapping_template"] = relative(mapping_template_path, repo_root)
    identity["mapping_template_sha256"] = sha256(mapping_template_path)

    started_utc = utc_now()
    timeloop_rows: list[dict[str, Any]] = []
    accelergy_rows: list[dict[str, Any]] = []
    for case in cases:
        timeloop_rows.append(
            run_case(
                tool="timeloop",
                case=case,
                output_root=timeloop_root,
                failures=failures,
                repo_root=repo_root,
                plain_template=plain_template,
                energy_template=energy_template,
                mapping_template=mapping_template,
                executable=args.timeloop_model,
                env=env,
                matrix_sha256=matrix_sha256,
            )
        )
        accelergy_rows.append(
            run_case(
                tool="accelergy",
                case=case,
                output_root=accelergy_root,
                failures=failures,
                repo_root=repo_root,
                plain_template=plain_template,
                energy_template=energy_template,
                mapping_template=mapping_template,
                executable=args.timeloop_model,
                env=env,
                matrix_sha256=matrix_sha256,
            )
        )

    write_csv(timeloop_root / "timeloop_size_summary.csv", timeloop_rows)
    write_csv(accelergy_root / "accelergy_size_summary.csv", accelergy_rows)
    timeloop_manifest = build_manifest(
        schema="aspg-rq3-size-timeloop-1.0",
        tool="timeloop-model",
        output_root=timeloop_root,
        manifest_path=timeloop_manifest_path,
        repo_root=repo_root,
        matrix_path=matrix_path,
        matrix_sha256=matrix_sha256,
        identity=identity,
        rows=timeloop_rows,
        started_utc=started_utc,
    )
    accelergy_manifest = build_manifest(
        schema="aspg-rq3-size-accelergy-1.0",
        tool="timeloop-model+Accelergy",
        output_root=accelergy_root,
        manifest_path=accelergy_manifest_path,
        repo_root=repo_root,
        matrix_path=matrix_path,
        matrix_sha256=matrix_sha256,
        identity=identity,
        rows=accelergy_rows,
        started_utc=started_utc,
    )
    report = {
        "timeloop": {key: timeloop_manifest[key] for key in ("total_cases", "completed_cases", "failed_cases")},
        "accelergy": {key: accelergy_manifest[key] for key in ("total_cases", "completed_cases", "failed_cases")},
        "timeloop_summary": relative(timeloop_root / "timeloop_size_summary.csv", repo_root),
        "accelergy_summary": relative(accelergy_root / "accelergy_size_summary.csv", repo_root),
    }
    print(json.dumps(report, indent=2))
    return 0 if not timeloop_manifest["failed_cases"] and not accelergy_manifest["failed_cases"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
