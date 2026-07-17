#!/usr/bin/env python3
"""Prepare, resume, audit, and summarize ASP-DAC reviewer-extension P1 runs.

This orchestrator never launches dotnet or an external simulator.  It writes stable
candidate inputs, owns a resumable checkpoint, records terminal outcomes without
dropping failures, and opens the comparison gate only after independent raw
artifacts from both hold-out providers are complete.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import shutil
import sys
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


SEED = 40
REPEATS = (0, 1)
TERMINAL = {"completed", "failed", "skipped", "not_supported"}
HOLDOUT_CASES = (
    ("holdout_gemm_096", 96, 96, 96),
    ("holdout_gemm_192", 192, 192, 192),
    ("holdout_gemm_384", 384, 384, 384),
    ("holdout_rect_128x256x64", 128, 256, 64),
    ("holdout_rect_256x64x192", 256, 64, 192),
    ("holdout_rect_64x384x128", 64, 384, 128),
    ("holdout_attn_qk_s096_d064", 96, 96, 64),
    ("holdout_attn_qk_s192_d064", 192, 192, 64),
)
NOC_CASE_IDS = (
    "noc_n01_single_128",
    "noc_n02_single_256_vc1",
    "noc_n03_single_512_vc3",
    "noc_n04_single_1024",
    "noc_n05_contend_128",
    "noc_n06_contend_512",
    "noc_n07_block_release_256",
    "noc_n08_block_tail_1024",
    "noc_n09_atomic_depth_boundary",
)
HOLDOUT_ALLOWED = ("case_id", "M", "N", "K", "precision_bits", "repeat", "seed")
NOC_ALLOWED = ("case_id", "repeat", "seed")
FORBIDDEN_CORE_TOKENS = (
    "aspdacmatchedworkload",
    "runscalesimmatched",
    "scalesim",
    "scale-sim",
    "timeloop",
    "reference_",
    "external_",
    "raw_path",
    "schedule_csv",
    "access_count_csv",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def atomic_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + f".{os.getpid()}.tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


@contextmanager
def checkpoint_lock(run_root: Path) -> Iterable[None]:
    lock = run_root / ".checkpoint.lock"
    descriptor: int | None = None
    try:
        descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        os.write(descriptor, f"pid={os.getpid()} utc={utc_now()}\n".encode("utf-8"))
        yield
    finally:
        if descriptor is not None:
            os.close(descriptor)
            lock.unlink(missing_ok=True)


def config_identity(kind: str, provider: str, parameters: dict[str, Any]) -> tuple[str, str]:
    identity_parameters = {key: value for key, value in parameters.items() if key != "repeat"}
    canonical = canonical_json({"kind": kind, "provider": provider, "parameters": identity_parameters})
    digest = sha256_text(canonical)
    return digest, canonical


def candidate(kind: str, provider: str, parameters: dict[str, Any]) -> dict[str, Any]:
    digest, canonical = config_identity(kind, provider, parameters)
    repeat = parameters["repeat"]
    candidate_id = f"p1-{kind}-{parameters['case_id']}-{digest[:16]}-r{repeat}"
    raw_relpath = f"raw/{provider}/{parameters['case_id']}/r{repeat}/result.json"
    return {
        "candidate_id": candidate_id,
        "kind": kind,
        "provider": provider,
        "case_id": parameters["case_id"],
        "repeat": repeat,
        "seed": parameters["seed"],
        "config_hash": digest,
        "config_canonical_json": canonical,
        "raw_relpath": raw_relpath,
        "resolved": {"parameters": parameters},
    }


def build_candidates() -> list[dict[str, Any]]:
    values: list[dict[str, Any]] = []
    for case_id, m, n, k in HOLDOUT_CASES:
        for repeat in REPEATS:
            parameters = {
                "case_id": case_id,
                "M": m,
                "N": n,
                "K": k,
                "precision_bits": 16,
                "repeat": repeat,
                "seed": SEED,
            }
            values.append(candidate("holdout", "stage", dict(parameters)))
            values.append(candidate("holdout", "scalesim", dict(parameters)))
    for case_id in NOC_CASE_IDS:
        for repeat in REPEATS:
            values.append(candidate("noc", "stage", {"case_id": case_id, "repeat": repeat, "seed": SEED}))
    return values


def plan_document(candidates: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "schema_version": "reviewer-extension-p1-plan/1.0",
        "created_utc": utc_now(),
        "seed": SEED,
        "repeat_values": list(REPEATS),
        "candidate_count": len(candidates),
        "identity_contract": {
            "algorithm": "sha256/canonical-json/v1",
            "repeat_excluded_from_config_hash": True,
            "candidate_id_appends_repeat": True,
        },
        "status_contract": ["pending", "running", "completed", "failed", "skipped", "not_supported"],
        "comparison_gate": "A case/repeat is paired only after stage and scalesim raw statuses are both completed.",
        "candidates": candidates,
    }


def prepare(run_root: Path) -> dict[str, Any]:
    run_root.mkdir(parents=True, exist_ok=True)
    for name in ("inputs", "raw", "summary", "manifests", "failures", "figures"):
        (run_root / name).mkdir(exist_ok=True)
    candidates = build_candidates()
    plan = plan_document(candidates)
    atomic_json(run_root / "manifests" / "p1_plan.json", plan)
    atomic_json(run_root / "manifests" / "p1_candidates.json", candidates)
    for item in candidates:
        payload = {
            "scenario": "reviewer_holdout_ws" if item["kind"] == "holdout" and item["provider"] == "stage"
            else "reviewer_holdout_scalesim" if item["kind"] == "holdout"
            else "reviewer_noc_contract",
            "candidate_id": item["candidate_id"],
            "config_hash": item["config_hash"],
            "resolved": item["resolved"],
        }
        atomic_json(run_root / "inputs" / f"{item['candidate_id']}.json", payload)

    checkpoint_path = run_root / "manifests" / "p1_checkpoint.json"
    if checkpoint_path.exists():
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
        known = checkpoint.get("candidates", {})
    else:
        known = {}
    states: dict[str, Any] = {}
    for item in candidates:
        previous = known.get(item["candidate_id"], {})
        states[item["candidate_id"]] = {
            "status": previous.get("status", "pending"),
            "attempts": int(previous.get("attempts", 0)),
            "worker": previous.get("worker", ""),
            "updated_utc": previous.get("updated_utc", utc_now()),
            "reason": previous.get("reason", ""),
            "raw_relpath": item["raw_relpath"],
        }
    checkpoint = {"schema_version": "reviewer-extension-p1-checkpoint/1.0", "updated_utc": utc_now(), "candidates": states}
    atomic_json(checkpoint_path, checkpoint)
    return {"run_root": str(run_root), "candidate_count": len(candidates), "checkpoint": str(checkpoint_path)}


def load_plan_index(run_root: Path) -> dict[str, dict[str, Any]]:
    path = run_root / "manifests" / "p1_candidates.json"
    if not path.exists():
        prepare(run_root)
    return {item["candidate_id"]: item for item in json.loads(path.read_text(encoding="utf-8"))}


def claim_next(run_root: Path, worker: str) -> dict[str, Any] | None:
    index = load_plan_index(run_root)
    checkpoint_path = run_root / "manifests" / "p1_checkpoint.json"
    with checkpoint_lock(run_root):
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
        selected_id = next((candidate_id for candidate_id in index if checkpoint["candidates"][candidate_id]["status"] == "pending"), None)
        if selected_id is None:
            return None
        state = checkpoint["candidates"][selected_id]
        state.update({"status": "running", "worker": worker, "attempts": state["attempts"] + 1, "updated_utc": utc_now(), "reason": ""})
        checkpoint["updated_utc"] = utc_now()
        atomic_json(checkpoint_path, checkpoint)
    item = dict(index[selected_id])
    item["input_path"] = str(run_root / "inputs" / f"{selected_id}.json")
    item["raw_path"] = str(run_root / item["raw_relpath"])
    return item


def record_status(run_root: Path, candidate_id: str, status: str, reason: str, result_path: Path | None) -> dict[str, Any]:
    if status not in TERMINAL:
        raise ValueError(f"Terminal status must be one of {sorted(TERMINAL)}")
    index = load_plan_index(run_root)
    if candidate_id not in index:
        raise KeyError(f"Unknown candidate id: {candidate_id}")
    raw_path = run_root / index[candidate_id]["raw_relpath"]
    raw_path.parent.mkdir(parents=True, exist_ok=True)
    if result_path is not None:
        source = result_path.resolve()
        if not source.exists():
            raise FileNotFoundError(source)
        if source != raw_path.resolve():
            temporary = raw_path.with_suffix(raw_path.suffix + f".{os.getpid()}.tmp")
            shutil.copyfile(source, temporary)
            os.replace(temporary, raw_path)
    if status == "completed" and not raw_path.exists():
        raise FileNotFoundError(f"completed requires a raw artifact: {raw_path}")
    if status != "completed" and not raw_path.exists():
        atomic_json(raw_path, {
            "status": status,
            "candidate_id": candidate_id,
            "reason": reason,
            "recorded_utc": utc_now(),
        })
    checkpoint_path = run_root / "manifests" / "p1_checkpoint.json"
    with checkpoint_lock(run_root):
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
        checkpoint["candidates"][candidate_id].update({
            "status": status,
            "updated_utc": utc_now(),
            "reason": reason,
            "raw_relpath": index[candidate_id]["raw_relpath"],
        })
        checkpoint["updated_utc"] = utc_now()
        atomic_json(checkpoint_path, checkpoint)
    return {"candidate_id": candidate_id, "status": status, "raw_path": str(raw_path)}


def reset_running(run_root: Path) -> int:
    checkpoint_path = run_root / "manifests" / "p1_checkpoint.json"
    reset = 0
    with checkpoint_lock(run_root):
        checkpoint = json.loads(checkpoint_path.read_text(encoding="utf-8"))
        for state in checkpoint["candidates"].values():
            if state["status"] == "running":
                state.update({"status": "pending", "worker": "", "reason": "resume_reset", "updated_utc": utc_now()})
                reset += 1
        checkpoint["updated_utc"] = utc_now()
        atomic_json(checkpoint_path, checkpoint)
    return reset


def nested(payload: dict[str, Any], *path: str, default: Any = None) -> Any:
    value: Any = payload
    for key in path:
        if not isinstance(value, dict) or key not in value:
            return default
        value = value[key]
    return value


def write_csv(path: Path, fieldnames: list[str], rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def summarize(run_root: Path) -> dict[str, Any]:
    index = load_plan_index(run_root)
    checkpoint = json.loads((run_root / "manifests" / "p1_checkpoint.json").read_text(encoding="utf-8"))
    states = checkpoint["candidates"]
    by_key = {(item["kind"], item["provider"], item["case_id"], item["repeat"]): item for item in index.values()}
    pair_rows: list[dict[str, Any]] = []
    timing_rows: list[dict[str, Any]] = []
    access_rows: list[dict[str, Any]] = []
    access_keys = ("sram_ifmap_reads", "sram_filter_reads", "sram_ofmap_writes", "dram_ifmap_reads", "dram_filter_reads", "dram_ofmap_writes")
    for case_id, *_ in HOLDOUT_CASES:
        for repeat in REPEATS:
            stage = by_key[("holdout", "stage", case_id, repeat)]
            scale = by_key[("holdout", "scalesim", case_id, repeat)]
            stage_status = states[stage["candidate_id"]]["status"]
            scale_status = states[scale["candidate_id"]]["status"]
            paired = stage_status == scale_status == "completed"
            pair_rows.append({"case_id": case_id, "repeat": repeat, "stage_status": stage_status, "scalesim_status": scale_status,
                              "paired_ready": str(paired).lower(), "comparison_status": "ready" if paired else "blocked"})
            if not paired:
                continue
            for tool, item in (("stage", stage), ("scalesim", scale)):
                raw_path = run_root / item["raw_relpath"]
                payload = json.loads(raw_path.read_text(encoding="utf-8"))
                metrics = payload.get("metrics", payload)
                timing_rows.append({
                    "case_id": case_id, "tool": tool, "repeat": repeat, "seed": SEED, "status": "completed",
                    "evidence_label": "Trend", "total_cycles": metrics.get("total_cycles", metrics.get("cycles")),
                    "warm_cycles": metrics.get("warm_cycles", metrics.get("cycles")), "prefetch_cycles": metrics.get("prefetch_cycles"),
                    "stall_cycles": metrics.get("memory_stall_cycles", metrics.get("stall_cycles")),
                    "utilization_percent": metrics.get("utilization_percent", metrics.get("utilization")),
                    "canonical_trace_sha256": metrics.get("canonical_trace_sha256", ""), "config_hash": item["config_hash"],
                    "raw_path": item["raw_relpath"],
                })
                accesses = metrics.get("accesses", payload.get("accesses", {}))
                for counter in access_keys:
                    access_rows.append({"case_id": case_id, "tool": tool, "repeat": repeat, "counter": counter,
                                        "access_count": accesses.get(counter), "evidence_label": "Numerical_or_Trend_by_definition",
                                        "raw_path": item["raw_relpath"]})

    summary = run_root / "summary"
    write_csv(summary / "holdout_pair_status.csv", ["case_id", "repeat", "stage_status", "scalesim_status", "paired_ready", "comparison_status"], pair_rows)
    write_csv(summary / "holdout_scalesim_stage_timing.csv",
              ["case_id", "tool", "repeat", "seed", "status", "evidence_label", "total_cycles", "warm_cycles", "prefetch_cycles",
               "stall_cycles", "utilization_percent", "canonical_trace_sha256", "config_hash", "raw_path"], timing_rows)
    write_csv(summary / "holdout_scalesim_stage_accesses.csv",
              ["case_id", "tool", "repeat", "counter", "access_count", "evidence_label", "raw_path"], access_rows)

    noc_rows: list[dict[str, Any]] = []
    actual_timeline: list[dict[str, Any]] = []
    oracle_timeline: list[dict[str, Any]] = []
    feature_rows: list[dict[str, Any]] = []
    for case_id in NOC_CASE_IDS:
        for repeat in REPEATS:
            item = by_key[("noc", "stage", case_id, repeat)]
            state = states[item["candidate_id"]]
            raw_path = run_root / item["raw_relpath"]
            payload = json.loads(raw_path.read_text(encoding="utf-8")) if raw_path.exists() else {}
            metrics = payload.get("metrics", {})
            noc_rows.append({"case_id": case_id, "repeat": repeat, "checkpoint_status": state["status"],
                             "runtime_status": payload.get("status", state["status"]), "oracle_matched": metrics.get("oracle_matched"),
                             "stage_trace_sha256": metrics.get("canonical_timeline_sha256", ""),
                             "oracle_timeline_sha256": metrics.get("oracle_timeline_sha256", ""),
                             "stage_support_reason": metrics.get("stage_support_reason", state.get("reason", "")),
                             "raw_path": item["raw_relpath"]})
            for entry in payload.get("timeline", []):
                actual_timeline.append({"case_id": case_id, "repeat": repeat, **entry})
            for entry in payload.get("oracle_timeline", []):
                oracle_timeline.append({"case_id": case_id, "repeat": repeat, **entry})
            if not feature_rows and payload.get("feature_boundary"):
                feature_rows = list(payload["feature_boundary"])
    write_csv(summary / "noc_contract_microbench.csv",
              ["case_id", "repeat", "checkpoint_status", "runtime_status", "oracle_matched", "stage_trace_sha256",
               "oracle_timeline_sha256", "stage_support_reason", "raw_path"], noc_rows)
    timeline_fields = ["case_id", "repeat", "Sequence", "Cycle", "Phase", "event_type", "packet_id", "flit_id", "flit_index",
                       "total_flits", "component_id", "input_port", "virtual_channel", "output_port", "link_id", "occupancy_before",
                       "occupancy_after", "Ready", "Valid", "Granted", "serialization_bits", "arrival_cycle", "tail_complete",
                       "committed_visible", "Delivered", "buffer_released", "Reason", "evidence_label"]
    write_csv(summary / "noc_cycle_timeline.csv", timeline_fields, actual_timeline)
    write_csv(summary / "noc_oracle_timeline.csv", timeline_fields, oracle_timeline)
    write_csv(summary / "noc_feature_boundary.csv",
              ["feature_id", "feature_group", "status", "modeled_semantics", "evidence_label", "comparison_permission"], feature_rows)

    p1_manifest_paths = [
        *(run_root / "manifests").glob("p1_*.json"),
        run_root / "manifests" / "holdout_independence_audit.md",
        *(run_root / "inputs").glob("p1-*.json"),
        *(run_root / "raw" / "stage").rglob("*"),
        *(run_root / "raw" / "scalesim").rglob("*"),
        *(run_root / "summary").glob("holdout_*.csv"),
        *(run_root / "summary").glob("noc_*.csv"),
    ]
    manifest_files = sorted({path for path in p1_manifest_paths if path.is_file() and path.name != "p1_bundle_manifest.json"})
    manifest = {
        "schema_version": "reviewer-extension-p1-bundle/1.0",
        "generated_utc": utc_now(),
        "comparison_gate_ready_count": sum(row["comparison_status"] == "ready" for row in pair_rows),
        "comparison_gate_blocked_count": sum(row["comparison_status"] == "blocked" for row in pair_rows),
        "status_counts": {status: sum(state["status"] == status for state in states.values()) for status in ("pending", "running", *sorted(TERMINAL))},
        "files": [{"path": path.relative_to(run_root).as_posix(), "sha256": sha256_file(path), "bytes": path.stat().st_size} for path in manifest_files],
    }
    atomic_json(run_root / "manifests" / "p1_bundle_manifest.json", manifest)
    return manifest


def audit(repo_root: Path, output: Path | None) -> dict[str, Any]:
    source = repo_root / "src" / "HardwareSim.Core" / "AspdacReviewerHoldoutRuntime.cs"
    text = source.read_text(encoding="utf-8").lower()
    hits = [token for token in FORBIDDEN_CORE_TOKENS if token in text]
    candidates = build_candidates()
    holdout_stage = [item for item in candidates if item["kind"] == "holdout" and item["provider"] == "stage"]
    result = {
        "schema_version": "reviewer-extension-p1-source-audit/1.0",
        "audited_utc": utc_now(),
        "source": source.relative_to(repo_root).as_posix(),
        "source_sha256": sha256_file(source),
        "forbidden_tokens": list(FORBIDDEN_CORE_TOKENS),
        "hits": hits,
        "holdout_stage_candidates": len(holdout_stage),
        "noc_stage_candidates": sum(item["kind"] == "noc" for item in candidates),
        "stable_candidate_ids": len({item["candidate_id"] for item in candidates}) == len(candidates),
        "passed": not hits and len(holdout_stage) == 16 and len(candidates) == 50,
    }
    if output:
        atomic_json(output, result)
    return result


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    for name in ("prepare", "claim-next", "reset-running", "summarize"):
        command = subparsers.add_parser(name)
        command.add_argument("--run-root", type=Path, required=True)
        if name == "claim-next":
            command.add_argument("--worker", required=True)
    record = subparsers.add_parser("record")
    record.add_argument("--run-root", type=Path, required=True)
    record.add_argument("--candidate-id", required=True)
    record.add_argument("--status", choices=sorted(TERMINAL), required=True)
    record.add_argument("--reason", default="")
    record.add_argument("--result", type=Path)
    audit_command = subparsers.add_parser("audit")
    audit_command.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[3])
    audit_command.add_argument("--output", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        if args.command == "prepare":
            result = prepare(args.run_root.resolve())
        elif args.command == "claim-next":
            result = claim_next(args.run_root.resolve(), args.worker)
        elif args.command == "record":
            result = record_status(args.run_root.resolve(), args.candidate_id, args.status, args.reason, args.result)
        elif args.command == "reset-running":
            result = {"reset": reset_running(args.run_root.resolve())}
        elif args.command == "summarize":
            result = summarize(args.run_root.resolve())
        else:
            result = audit(args.repo_root.resolve(), args.output.resolve() if args.output else None)
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0 if not isinstance(result, dict) or result.get("passed", True) else 2
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(json.dumps({"status": "failed", "error": str(error)}, indent=2), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
