# Competitor baseline runner

`run_competitor_baselines.py` is a lightweight orchestration harness for the
paper-track competitor simulations.

It does not install external tools. It only:

- detects native Windows executables and WSL executables;
- writes normalized raw configs for planned baseline cases;
- runs a case only when the required executable is already available;
- writes result manifests with either command output or an explicit skip reason.

Example:

```powershell
python experiments\aspdac\scripts\run_competitor_baselines.py --repo-root .
```

The generated manifests live under `experiments/aspdac/results/`.

