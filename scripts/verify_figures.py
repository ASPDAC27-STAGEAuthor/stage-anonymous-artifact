#!/usr/bin/env python3
"""Check regenerated Figures 2-5 against the submitted visual references."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
GENERATED = ROOT / "output" / "figures"
EXPECTED = ROOT / "expected" / "figures"
OUT = ROOT / "output"
STEMS = [
    "STAGE_Figure2_Matched_47Bars",
    "STAGE_Figure3_Mapping_Codesign",
    "STAGE_Figure4_MNIST_Precision",
    "STAGE_Figure5_Optoelectronic_Final",
]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    report: dict[str, object] = {"status": "pass", "figures": {}}
    for stem in STEMS:
        png = GENERATED / f"{stem}.png"
        pdf = GENERATED / f"{stem}.pdf"
        if not png.is_file() or not pdf.is_file() or png.stat().st_size == 0 or pdf.stat().st_size == 0:
            raise FileNotFoundError(f"missing generated PDF/PNG pair for {stem}")
        with Image.open(png) as image:
            dimensions = list(image.size)
        entry: dict[str, object] = {
            "generated_png_sha256": sha256(png),
            "generated_pdf_sha256": sha256(pdf),
            "generated_png_dimensions": dimensions,
        }
        reference_png = EXPECTED / f"{stem}.png"
        reference_pdf = EXPECTED / f"{stem}.pdf"
        if reference_png.is_file():
            with Image.open(reference_png) as image:
                reference_dimensions = list(image.size)
            if reference_dimensions != dimensions:
                raise AssertionError(f"figure geometry changed for {stem}: {dimensions} vs {reference_dimensions}")
            reference_png_hash = sha256(reference_png)
            if reference_png_hash != entry["generated_png_sha256"]:
                raise AssertionError(f"generated PNG does not match the submitted reference for {stem}")
            entry["reference_png_sha256"] = reference_png_hash
            entry["reference_png_dimensions"] = reference_dimensions
            entry["submission_png_exact"] = True
        if reference_pdf.is_file():
            reference_pdf_hash = sha256(reference_pdf)
            if reference_pdf_hash != entry["generated_pdf_sha256"]:
                raise AssertionError(f"generated PDF does not match the submitted reference for {stem}")
            entry["reference_pdf_sha256"] = reference_pdf_hash
            entry["submission_pdf_exact"] = True
        report["figures"][stem] = entry
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "figure_verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
