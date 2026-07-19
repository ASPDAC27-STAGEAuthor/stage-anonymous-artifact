# RF-F-OPTICAL-ENERGY-V1 paper handoff

## Outcome

A trace-driven sidecar now replaces the synthetic optical-energy columns for the frozen Attention QK scale-2 O1/O2/O4/H4 comparison. No production core, paper TeX, or original reduced-lane artifact was modified.

The calibrated numbers remain sensitivity results, not tapeout-grade absolute predictions. They combine measured literature blocks from different process nodes without unsupported process scaling and therefore must be described as sourced low/nominal/high envelopes.

## Why the old O1/O2/O4 optical-path energy rises with wavelengths

The old rise is primarily an accounting/topology artifact, not a demonstrated physical wavelength law. The physical compiler adds the complete OpticalModel stack (laser 0.2 + modulator 0.05 + receiver 0.08 + tuning 0.02 = 0.35 pJ/bit) to every optical-domain graph link. O1 routes contain EO->waveguide->OE, which creates two optical graph links. O2/O4 insert WDM mux/demux and create EO->mux->waveguide->demux->OE, which creates four. The same complete stack is therefore charged twice per O1 optical hop and four times per WDM optical hop, while E/O and O/E components are also charged separately. O4 then adds a smaller placement/route-length effect relative to O2.

The sidecar removes that coupling: wavelength/lane count can increase static source and tuning energy, while bit-event counts increase serializers, CDR/deserializer, E/O, and O/E energy. Mux/demux and waveguide affect the loss budget only.

## Nominal profile, repeat 0

| Architecture | Cycles | Wavelengths | Optical edges | Provisioned TX lanes | Observed TX lanes | Dynamic pJ | Laser pJ | Tuning pJ | Total pJ | Loss dB | Required launch dBm/lane |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| O1 | 490 | 1 | 24 | 24 | 21 | 134597.568 | 17552.887 | 45864.000 | 198014.455 | 3.700 | -6.500 |
| O2 | 563 | 2 | 24 | 48 | 31 | 134597.568 | 63945.873 | 105393.600 | 303937.041 | 5.702 | -4.498 |
| O4 | 731 | 4 | 24 | 96 | 36 | 134597.568 | 166142.749 | 273686.400 | 574426.717 | 5.704 | -4.496 |
| H4 | 356 | 4 | 16 | 64 | 8 | 25637.632 | 53949.575 | 88857.600 | 168444.807 | 5.704 | -4.496 |

## Claim boundary

- Serializer, deserializer/CDR, E/O, and O/E scale with trace-owned bit counts.
- Laser scales with full execution time and provisioned E/O TX lanes (directed optical edges x wavelengths per edge); wall-plug efficiency converts required optical launch to electrical power.
- Thermal tuning scales with time, provisioned TX lanes, and profile resonator count. Low explicitly selects zero active resonators; nominal/high use sourced sensitivity points.
- WDM mux/demux and waveguide do not receive invented pJ/bit terms. They add insertion/propagation loss and thereby affect the laser budget.
- 64B/66B is applied once: serializer activity uses original bits; deserializer/CDR and optical electronics use encoded bits.
- The sidecar does not add electrical mesh/router energy. H4 results here are its optical/SerDes subsystem only.
- Cross-node literature mixing, laser sharing topology, coupler loss, extinction ratio penalties, BER curve fitting, packaging loss, and temperature-dependent drift are not resolved by the frozen graph. Do not present the output as a single exact silicon value.

## One-command reproduction

From the repository root:

```powershell
python experiments/aspdac/tests/results_first_optical_energy_runner/run_optical_energy.py all
```

The `all` command enforces analytical contracts before architecture evaluation and rewrites only `experiments/aspdac/results/paper_revision_20260718/session_f_optical_energy/`.
