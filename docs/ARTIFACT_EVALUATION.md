# Reviewer evaluation checklist

Start with the claim-to-command matrix in <code>README.md</code>. It states the exact command, expected result, evidence mode, and estimated runtime for each paper claim.

## A. Portable end-to-end check

Run one command from the artifact root:

~~~powershell
.\scripts\run_all.ps1
~~~

or:

~~~bash
bash scripts/run_all.sh
~~~

Acceptance condition: exit code 0, <code>output/run_summary.json</code> reports <code>pass</code>, all 7 golden and 25 paper tests pass, <code>output/frozen_validation.json</code> reports <code>pass</code> for every named claim, and eight generated figure files appear under <code>output/figures/</code>.

## B. Independent checks

1. <code>python scripts/build_manifest.py --verify</code> authenticates the static package.
2. <code>python scripts/anonymization_scan.py</code> checks the review copy for common identity leaks.
3. <code>scripts/run_tests.*</code> builds and runs the seven exact-cycle plus 25 paper-claim tests.
4. <code>python scripts/validate_frozen.py --claim NAME</code> runs one assertion set; allowed names are shown by <code>--help</code>.
5. <code>python experiments/aspdac/scripts/generate_booksim_configs.py --output output/external-rerun/booksim-configs</code> regenerates and verifies all 530 BookSim inputs.
6. <code>scripts/run_figures.*</code> recreates Figures 3--6.
7. <code>python scripts/compare_hashes.py expected.json actual.json</code> localizes a canonical-trace mismatch.

## C. Expected headline values

- Nine analytical contracts pass with 18 byte-identical repeated traces and locked SHA-256 values.
- 14 supported NoC oracle runs across seven cases; N07/N08 remain explicitly unsupported.
- Timeloop: five compute-cycle cases and twenty hierarchy-access rows match exactly.
- SCALE-Sim: 16 paired hold-out runs; maximum relative difference 9.691109%, within the predefined 10% engineering tolerance.
- Accelergy: nine shared ERT action values match exactly.
- BookSim/STAGE: <code>hotspot_node5</code> is first in both, while 0.04 versus 0.08 is retained as a non-equivalent absolute result.
- Controlled attribution: NoC -> memory -> NoC across the two accepted interventions.
- Mapping: 24 candidates, 174--1,741 cycles, and 316--600 1024-bit packet moves.
- MNIST accuracy: 98.39%, 98.40%, 98.28%, and 97.35% for FP32/A32, FP16/A16, FP8/A16, and FP8/A8.
- Optical intervention: effective payload 128 to 512 bit/base-cycle and total cycles 8,193 to 2,049.
- Largest trace case: 48,874 simulated cycles, 46,852,084 events, 3.92 GiB raw trace, and 366 MiB compressed trace.

## D. Non-goals of the portable check

The default check does not install or rebuild BookSim2, SCALE-Sim, Timeloop, Accelergy, ZigZag, Unity, CUDA, or the historical PyTorch environment. It also does not regenerate the omitted multi-gigabyte raw trace. Small external input decks/configuration ledgers, adapters, audits, hashes, and terminal summaries are included. Fresh third-party executions must target a reviewer-owned output directory and are documented in <code>docs/EXTERNAL_TOOLS.md</code>.
