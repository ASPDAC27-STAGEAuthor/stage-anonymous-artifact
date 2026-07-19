# RF-E electrical NoC source provenance

## Repository audit

- `src/HardwareSim.Core/DeviceModels.cs` provides generic model import/binding and a link `energy_per_bit` binding, but no router action ERT.
- `src/HardwareSim.Core/CycleSimulationEngine.Deterministic.cs` charges `packet.Bits * link.EnergyPerBit` at link issue; router activity is counted but router component energy and static/leakage are not charged.
- `src/HardwareSim.Core/PhysicalModel.cs` uses the legacy fallback `0.01 pJ/bit + 0.0001 pJ/(bit um) * distance_um`; it is retained only as audited legacy data and is replaced for `endpoint-*`/`electrical-*` links in this sidecar.
- Session A uses Accelergy 0.4 at 45 nm and `global_cycle_seconds=1e-9`, but its own handoff excludes `noc`, `conversion`, and `optical` from supported ERT categories. Session A voltage is not specified.

## Primary/official external sources

| Source | Version/commit | Node | Voltage/frequency | Units | Use here | Boundary |
|---|---|---:|---|---|---|---|
| DSENT in official gem5 repository | DSENT 0.91; `bc5b00cd2b5dea850acf63f6a1055ff260c8fba7`; source-tree SHA-256 `83b68f61cca0979372c27784b20aed4c679e5ec6014026a95ca09b5aaa449551` | Bulk 45-nm LVT | 1.0 V; 1 GHz; 340 K model temperature | SI (J, W, m, m2) | Nominal router/link ERT | Analytic model, not silicon calibration; 3-stage input-buffered VC router |
| DSENT NOCS 2012 paper | Sun et al., NOCS 2012 | paper examples include 45 nm | paper-specific assumptions | energy/power/area | Methodology and model scope | Paper's 45-nm examples are not substituted for the checked-in Bulk45LVT file |
| Accelergy official paper/model framework | Accelergy paper; Session A tool 0.4 | Session A 45 nm | Session A 1 GHz; voltage unspecified | action energy tables | Action-contract structure and Session A alignment | Repository Session A contains no NoC/router/link ERT |
| BookSim2 official repository | `28f43299f1706a3160ffac721ca461d74eb6e618` | 32-nm HP (2007 ITRS) | 0.9 V; frequency configuration-dependent | internal power-model units | Independent decomposition audit only | Not used numerically in the 45-nm nominal ERT; no false node unification |
| ORION 2.0 primary paper | ICCAD 2009 model | multiple/scalable | paper/config dependent | power/area | Independent router-component taxonomy audit | No constants copied into nominal ERT |
| ORION 3.0 comprehensive model report | UCSD CS2012-0989 | report-dependent | report-dependent | power/area | Sensitivity/reference only | No constants copied into nominal ERT |

Official links:

- DSENT source: https://gem5.googlesource.com/public/gem5/+/bc5b00cd2b5dea850acf63f6a1055ff260c8fba7/ext/dsent/
- DSENT paper: https://projects.csail.mit.edu/wiki/pub/LSPgroup/PublicationList/dsent_nocs12.pdf
- Accelergy paper: https://accelergy.mit.edu/paper.pdf
- BookSim2: https://github.com/booksim/booksim2
- ORION 2.0: https://escholarship.org/uc/item/5jd3c1gv
- ORION comprehensive/3.0 report: https://vlsicad.ucsd.edu/Publications/Reports/CS2012-0989.pdf

## Nominal binding and normalization

- Router: 5 inputs, 5 outputs, 128-bit flit, one virtual network, 4 VCs, 128 buffers/VC, DFFRAM, two-stage MatrixArbiter, multiplexer crossbar, BroadcastHTree clock.
- Electrical endpoint link: DSENT RepeatedLink at 20 um and 1 ns delay.
- Electrical mesh link: DSENT RepeatedLink at 200 um and 1 ns delay.
- 1-mm link is reported only as a normalization point. Short-link pJ/bit-mm differs because DSENT includes a repeater/intercept cost; the runner therefore uses exact length classes instead of pretending one linear coefficient.
- Joule to picojoule: multiply by 1e12. Watt to pJ/cycle at 1 ns: multiply by `1e-9 * 1e12`. Square meter to square millimeter: multiply by 1e6.
- Route computation and arbitration are one combined contract: DSENT exposes the two switch-allocation stages but no separate route-computation event. The increment is therefore unresolved, not claimed to be zero.

## Applicability boundary

The nominal ERT is 45-nm Bulk-LVT, 1.0 V, 1 GHz. BookSim's audited 32-nm/0.9-V model is not blended or relabeled. The model applies only to 128-bit `endpoint-*` and `electrical-*` transfers and the 16 shared endpoint routers. `optical-*`, SerDes, EO/OE, laser, tuning, and photonic-link terms remain outside this task and are left to the parallel optical-energy task.
