# Claim boundary

## Paper-safe claim

Production STAGE (`AspdacStageNocRuntime`) matches native BookSim2 event by event on the selected 4x4 mesh contract exercised here. The checked scope contains deterministic dimension-order routing, BookSim Knuth-RNG XY/YX and ROMM choices, packet sizes of 1/3/4 flits, 1/2 virtual channels, buffer depths 1/4/16, one traffic class, and Bernoulli uniform traffic at rate 0.02 with seed 40.

The two independent workflow executions match for injection, route, VC grant, switch grant, router send, reconstructed BookSim credit-channel return, ejection, packet retirement, all production canonical event hashes, and native watch hashes. This is `PRODUCTION_STAGE_SELECTED_CONTRACT_EXACT`.

## Explicit exclusions

This package does not establish unrestricted BookSim equivalence. It excludes arbitrary VC counts or VC-class partitions, arbitrary packet sizes, adaptive/valiant/torus routing or topology, multiple traffic classes or subnetworks, arbitrary rates/seeds or non-uniform traffic, router speedup, speculative allocation, hold-switch variants, and other untested BookSim features.

## Frozen-paper kernel separation

No existing Figure 2–5 input calls `AspdacStageNocRuntime`, so no mandatory rerun is required. In particular, current paper Figure 5 is generated from frozen `AspdacTransportRuntime.Run` outputs: it is a lightweight cycle-stepped transport kernel, not the BookSim-matched production STAGE kernel. The separate existing VBS NoC/trace path remains on `AspdacVbsRuntime.FastRouter`; this audit does not relabel Figure 5's direct runtime provenance as VBS.

## Why this is not a BookSim backend

The production run reports `engine_identity=stage_managed_flit_vc_runtime` and `external_backend_invocations=0`; source scans find no forbidden BookSim process/backend tokens. STAGE additionally supplies workload/hardware provenance (layer, tensor role, mapping, component, route-resource identities), CNN compute/memory/NoC/post-op service attribution, and numerical PE expected-versus-actual output hashes. Native BookSim2 does not supply these STAGE-native co-design semantics.
