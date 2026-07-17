#!/usr/bin/env python3
"""Final ZigZag wrapper with private per-MAC output registers."""

from __future__ import annotations

import run_mnist_precision_zigzag as runner


_original_hardware_text = runner.hardware_text


def hardware_text(profile: dict[str, object]) -> str:
    text = _original_hardware_text(profile)
    replacements = {
        "allocation: [O, tl]": "allocation:\n          - O, tl",
        "allocation: [O, th]": "allocation:\n          - O, th",
        "allocation: [O, fh]": "allocation:\n          - O, fh",
        "allocation: [O, fl]": "allocation:\n          - O, fl",
        "allocation: [I1, tl]": "allocation:\n          - I1, tl",
        "allocation: [I2, tl]": "allocation:\n          - I2, tl",
        "allocation: [O, tl, O, fl]": "allocation:\n          - O, tl\n          - O, fl",
        "allocation: [I1, fh, I2, fh, O, fh, O, th]": (
            "allocation:\n          - I1, fh\n          - I2, fh\n          - O, fh\n          - O, th"
        ),
        "allocation: [I1, fh, I1, tl, I2, fh, I2, tl, O, fh, O, tl, O, fl, O, th]": (
            "allocation:\n          - I1, fh\n          - I1, tl\n          - I2, fh\n          - I2, tl\n"
            "          - O, fh\n          - O, tl\n          - O, fl\n          - O, th"
        ),
        "served_dimensions: [D2]": "served_dimensions: []",
    }
    for source, target in replacements.items():
        if source not in text:
            raise RuntimeError(f"ZigZag hardware YAML anchor changed: {source}")
        text = text.replace(source, target, 1)
    return text


runner.hardware_text = hardware_text
runner.main()
