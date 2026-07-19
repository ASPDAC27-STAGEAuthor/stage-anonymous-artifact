# RF-J BookSim production paper handoff

Status: **COMPLETE**  
Scientific status: **PRODUCTION_STAGE_SELECTED_CONTRACT_EXACT**  
Commit: `b357fa7c35c47c2b3226de25369c6263992ae9c9`

Production `AspdacStageNocRuntime` is event-exact against native BookSim2 for the selected tested contract. Two independent complete workflows reproduced all frozen values and every normalized event timeline: injection, route, VC grant, switch grant, router send, credit return, ejection, and retirement. All case/stochastic canonical hashes match across workflow repeats; the only whole-file change is the non-scientific `generated_utc` field.

| Contract | Scope | Exact cases | Checked events | Result |
|---|---|---:|---|---|
| Base one-flit | 4x4 mesh, XY, 1 VC, depths 1/4/16 | 7/7; 869/869 packets | injection/arrival/order/packet hash | EXACT |
| Multi-flit / multi-VC | 3/4 flits, 1/2 VCs | 3/3 | full selected event timeline | EXACT |
| Routing RNG | XY/YX + ROMM, BookSim Knuth RNG, seed 40 | 4/4 | RNG decisions plus full selected event timeline | EXACT |
| Bernoulli uniform | rate 0.02, seed 40, one class | 2/2 native repeats; 69 packets, 255 sends each | generation through retirement plus hashes | EXACT |

## Frozen-paper impact

Commit `b357fa7` does not change `AspdacVbsRuntime` (identical parent/current blob). Entry-point tracing shows no Figure 2–5 runner calls `AspdacStageNocRuntime`; therefore existing figures are unchanged and no mandatory rerun is required. Figure 5's direct frozen runtime is the lightweight cycle-stepped `AspdacTransportRuntime.Run`, while the separate VBS NoC/trace path remains `AspdacVbsRuntime.FastRouter`. Neither is the new BookSim-matched kernel.

## Paper wording boundary

The defensible statement is selected-contract event exactness on the tested 4x4 mesh configurations. Do not claim arbitrary BookSim equivalence, and do not imply that existing Figure 5 was generated with the BookSim-matched production kernel. Full limits are in `claim_boundary.md`; entry-point evidence is in `frozen_figure_impact_audit.csv`.

## Backend-independence evidence

The production engine is managed STAGE code with zero external backend invocations. Beyond BookSim's network timeline, STAGE preserves workload/hardware provenance, provides CNN compute/memory/NoC/post-op attribution, and validates numerical PE outputs. These co-design artifacts are recorded in both repeats under `raw/*/production_integration/not_booksim_backend_evidence.json`.
