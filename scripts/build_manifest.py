#!/usr/bin/env python3
"""Build or verify a deterministic SHA-256 manifest for the review package."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "metadata" / "artifact_manifest.json"
EXCLUDED_PARTS = {".git", ".venv", "bin", "obj", "__pycache__", "output"}
EXCLUDED_FILES = {"metadata/artifact_manifest.json"}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def collect() -> dict[str, object]:
    files: list[dict[str, object]] = []
    for path in sorted(ROOT.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(ROOT).as_posix()
        if relative in EXCLUDED_FILES or any(part in EXCLUDED_PARTS for part in path.relative_to(ROOT).parts):
            continue
        files.append({"path": relative, "bytes": path.stat().st_size, "sha256": sha256(path)})
    return {
        "schema": "stage-anonymous-artifact-manifest-1.0",
        "hash_algorithm": "SHA-256",
        "identity_fields_omitted": True,
        "excluded_generated_roots": sorted(EXCLUDED_PARTS),
        "file_count": len(files),
        "total_bytes": sum(int(item["bytes"]) for item in files),
        "files": files,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    current = collect()
    if args.verify:
        expected = json.loads(MANIFEST.read_text(encoding="utf-8"))
        if current != expected:
            raise SystemExit("artifact manifest mismatch; run build_manifest.py after intentional package changes")
        print(f"Manifest verified: {current['file_count']} files, {current['total_bytes']} bytes")
        return 0
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps(current, indent=2) + "\n", encoding="utf-8")
    print(f"Manifest written: {current['file_count']} files, {current['total_bytes']} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
