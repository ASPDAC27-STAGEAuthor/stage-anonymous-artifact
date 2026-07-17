# Baseline-tool staging

This directory stores setup notes and normalized reference configs for external
tools. Running or installing these tools may require network access or local
system changes and must be approved separately.

Allowed evidence labels:

- `reference_trend`: tool output used to compare direction, ranking, or
  saturation behavior.
- `tolerance_reference`: analytical or tool output whose assumptions are
  normalized enough for bounded relative error.
- `not_run`: config prepared, no result generated yet.

Forbidden wording:

- Do not write that VHaCSim achieved a value if the value came only from an
  external tool.
- Do not claim cycle-exact agreement with BookSim unless router, arbitration,
  injection, packetization, buffering, and measurement windows are all matched.
- Do not claim device-level optical or CIM accuracy from parameter collection.
