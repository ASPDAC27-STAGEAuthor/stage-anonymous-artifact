#!/usr/bin/env python3
"""Create a deterministic, history-free upload ZIP and SHA-256 sidecar."""

from __future__ import annotations

import hashlib
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT_ZIP = ROOT.parent / f"{ROOT.name}.zip"
EXCLUDED_PARTS = {".git", ".venv", "bin", "obj", "__pycache__"}


def included(path: Path) -> bool:
    relative = path.relative_to(ROOT)
    if any(part in EXCLUDED_PARTS for part in relative.parts):
        return False
    if relative.parts and relative.parts[0] == "output":
        return relative.as_posix() == "output/.gitkeep"
    return path.suffix.lower() not in {".pyc", ".zip"}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    subprocess.run([sys.executable, str(ROOT / "scripts" / "anonymization_scan.py")], check=True)
    subprocess.run([sys.executable, str(ROOT / "scripts" / "build_manifest.py"), "--verify"], check=True)
    files = [path for path in sorted(ROOT.rglob("*")) if path.is_file() and included(path)]
    with zipfile.ZipFile(OUTPUT_ZIP, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            relative = path.relative_to(ROOT).as_posix()
            info = zipfile.ZipInfo(f"{ROOT.name}/{relative}", date_time=(1980, 1, 1, 0, 0, 0))
            mode = 0o755 if path.suffix.lower() in {".sh", ".py"} else 0o644
            info.external_attr = mode << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, path.read_bytes(), compresslevel=9)
    digest = sha256(OUTPUT_ZIP)
    sidecar = OUTPUT_ZIP.with_suffix(OUTPUT_ZIP.suffix + ".sha256")
    sidecar.write_text(f"{digest}  {OUTPUT_ZIP.name}\n", encoding="ascii")
    print(f"Created {OUTPUT_ZIP}")
    print(f"Files: {len(files)}")
    print(f"Bytes: {OUTPUT_ZIP.stat().st_size}")
    print(f"SHA-256: {digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
