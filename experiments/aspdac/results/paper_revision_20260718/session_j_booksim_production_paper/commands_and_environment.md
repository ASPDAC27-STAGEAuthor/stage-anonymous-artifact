# Commands and environment

## Isolated checkout

- Repository commit: `b357fa7c35c47c2b3226de25369c6263992ae9c9` (`b357fa7`, detached HEAD).
- Scientific checkout: `C:\tmp\rf-j-booksim-production-paper-v1-lf-20260719`.
- Checkout policy: `core.autocrlf=false`; tracked `git status --porcelain=v1` was empty before validation.
- The solution references three `*.csproj` files excluded by the repository-wide `*.csproj` ignore rule. The clean checkout initially failed with MSB3202 until the three machine-local project descriptors were materialized from the verified source workspace; their SHA-256 values are recorded in `sha256_manifest.csv`. They remain ignored, so the tracked checkout stayed clean.
- A first default-Windows checkout is retained as environment evidence: CRLF conversion changed `data/characterization/phase7c-literature-catalog-v1.json` and caused only `P7C-LIT-003` to fail (500/501). The LF-preserving checkout restored the committed blob bytes and passed 501/501. This was an environment/checkout failure, not a production or scientific-result refresh.

## Required serial gate

Commands were run sequentially, never in parallel:

1. `dotnet build VisualHardwareAiCoDesignSimulator.sln -c Debug --disable-build-servers` — PASS, 0 warnings, 0 errors.
2. `dotnet build VisualHardwareAiCoDesignSimulator.sln -c Release --disable-build-servers` — PASS, 0 warnings, 0 errors.
3. `dotnet run --project tests\HardwareSim.Tests\HardwareSim.Tests.csproj -c Debug --no-build -- --filter P10-NOC-BS- --results rfj_phase10_booksim_focused_debug.json` — PASS 6/6.
4. `dotnet run --project tests\HardwareSim.Tests\HardwareSim.Tests.csproj -c Debug --no-build -- --results rfj_full_debug.json` — PASS 501/501.
5. `dotnet run --project tests\HardwareSim.Tests\HardwareSim.Tests.csproj -c Release --no-build -- --results rfj_full_release.json` — PASS 501/501.

## Production/native cross-validation

From `experiments\aspdac\tests\results_first_noc_booksim_crossval_runner\minimal_repro\extended_repro`:

- Repeat 1: `& .\run_extended_repro.ps1` — PASS.
- Repeat 2: `& .\run_extended_repro.ps1` — PASS.

Each repeat rebuilt/ran the production STAGE reproduction and regenerated native BookSim2 multi-flit, multi-VC, XY/YX RNG, ROMM RNG, and stochastic watch timelines. Raw outputs are frozen under `raw/repeat_1` and `raw/repeat_2`.

## Environment

- OS: Microsoft Windows NT 10.0.26200.0
- PowerShell: 7.6.3
- .NET SDK: 9.0.315
- Python: 3.10.6
- Git: 2.38.0.windows.1
- Native BookSim2 base commit: `28f43299f1706a3160ffac721ca461d74eb6e618`
- Native BookSim2 binary SHA-256: `7579da3acfd960ad02c18ca9257b87dfb617f8916246207749dff8ff4fee5c20`

## Frozen-paper impact checks

- `git diff b357fa7^ b357fa7 -- src/HardwareSim.Core/AspdacVbsRuntime.cs` produced no diff.
- Parent and commit blob IDs for `AspdacVbsRuntime.cs` are both `46bd95fc2a5e099adf7ade9aa2e6720509f960c1`.
- `git show --name-only b357fa7` contains production STAGE/test/evidence files, not paper TeX or Figure 2–5 input data.
- A source audit of every listed figure runner/packager found no `AspdacStageNocRuntime` call.

Initial CRLF diagnostic: 500/501 passed, with the sole failure retained in `raw/validation/initial_crlf_checkout_full_debug.json`.
