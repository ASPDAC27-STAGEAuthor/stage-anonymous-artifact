#!/usr/bin/env python3
"""Final Timeloop wrapper with layer-specific 4x4 spatial-axis assignment."""

from __future__ import annotations

import sys

import run_mnist_precision_timeloop as runner


_original_deck_text = runner.deck_text


def deck_text(layer: dict[str, object], profile: dict[str, object]) -> str:
    text = _original_deck_text(layer, profile)
    permutation = "NKM" if str(layer["layer_id"]).startswith("fc") else "MNK"
    anchor = """  - target: GlobalBuffer
    type: spatial
    factors: {spatial}
    permutation: MNK
  - target: Registers
    type: utilization
""".format(spatial=layer["spatial"])
    replacement = """  - target: GlobalBuffer
    type: spatial
    factors: {spatial}
    permutation: {permutation}
    split: 1
  - target: Registers
    type: temporal
    factors: M1 N1 K1
    permutation: MNK
  - target: Registers
    type: utilization
""".format(spatial=layer["spatial"], permutation=permutation)
    if anchor not in text:
        raise RuntimeError("Timeloop mapspace anchor changed")
    return text.replace(anchor, replacement, 1)


runner.deck_text = deck_text

if "--smoke-fc" in sys.argv:
    sys.argv.remove("--smoke-fc")
    runner.LAYERS = runner.LAYERS[2:3]
    runner.PROFILES = runner.PROFILES[:1]

runner.main()
