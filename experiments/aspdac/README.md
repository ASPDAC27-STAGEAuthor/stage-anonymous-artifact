# ASP-DAC experiment workspace

This directory holds manuscript-supporting experiment assets for the ASP-DAC push.
It is deliberately separate from production simulator code, schemas, tests, and
phase evidence.

## Scope

- Store normalized experiment specs under `specs/`.
- Store external reference parameters under `reference_data/`.
- Store baseline-tool setup notes and planned configs under `baseline_tools/`.
- Store machine-readable result manifests under `results/` after experiments run.

## Phase-gate rule

Only approved VHaCSim phases may be used as implemented paper evidence. Phase 6B
is currently treated as `awaiting_approval`; Phase 6C and later capabilities are
`locked_pending` and must remain planning, parameter, or placeholder material.

External tool results are reference or trend evidence only. They must never be
presented as VHaCSim measurements.

