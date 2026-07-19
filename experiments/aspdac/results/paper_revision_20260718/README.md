# Submitted-paper evidence snapshot

This directory contains the compact frozen inputs added for the final submitted-paper artifact.

- `session_c_matched_47bars` contains the 47 matched Timeloop and Accelergy values used by Figure 2.
- `session_b_opto_noc_reduced_v1`, `session_d_o4_queue`, `session_e_electrical_energy`, and `session_f_optical_energy` contain the normalized transport and energy inputs used by Figure 5.
- `session_j_booksim_production_paper` contains selected-contract BookSim2 timing summaries, event exactness rows, repeat hashes, and explicit claim boundaries.

Only normalized summaries, plotting inputs, audits, and compact manifests needed by the portable review are included. Multi-gigabyte STAGE traces and bulk native third-party logs are omitted. Their provenance is retained through the supplied hashes and source records.

The live managed BookSim-matched implementation is `src/HardwareSim.Core/AspdacStageNocRuntime.cs`. Six focused checks are included in the `paper` test group. They make no external backend calls.

These directories are frozen evidence. Fresh runs must write under `output/` or another reviewer-owned path.