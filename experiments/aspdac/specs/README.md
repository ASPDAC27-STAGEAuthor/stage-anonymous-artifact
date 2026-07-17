# Normalized ASP-DAC specs

These files define stable experiment intent before any external tool or VHaCSim
run is executed. Values marked `pending_phase` or `locked_pending` are not paper
evidence until the corresponding phase gate allows implementation and the result
bundle is generated.

Common comparison rules:

- Keep workload dimensions, precision, mapping, memory, topology, link width,
  routing, and clock assumptions explicit.
- Store both raw and resolved configs in result bundles.
- Record skip reasons instead of silently dropping invalid configurations.
- Use exact evidence for hand-derived microbenchmarks, tolerance evidence for
  normalized analytical comparisons, and trend evidence for cross-tool cases.

