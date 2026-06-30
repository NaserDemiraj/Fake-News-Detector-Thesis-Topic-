# Evaluation Suite

Thesis evaluation harness for the fake-news detector. The C# runner measures the
live backend against the ISOT dataset; the Python scripts turn its output CSVs
into thesis figures and statistical analyses.

## Prerequisites

- ISOT dataset CSVs in `Evaluation/data/` as `True.csv` and `Fake.csv`.
- For scripts that call the API: the backend running on `http://localhost:5000`
  with an AI provider configured (otherwise results are mock and are rejected).
- Python deps: `pip install pandas numpy scikit-learn matplotlib seaborn`

## C# runner

```
dotnet run -- --true data/True.csv --fake data/Fake.csv --max 200 --delay 2000 --retries 3 \
              --label "Groq Full Prompt" --output-csv results_200.csv --output-json metrics_200.json
dotnet run -- --resume ...        # continue an interrupted run (skips done rows)
dotnet run -- compare metrics_a.json metrics_b.json   # side-by-side table
```

Produces `results_*.csv` (per-article predictions) and `metrics_*.json` (aggregate).

## Python analysis scripts

| Script | Purpose | Input | Output |
|---|---|---|---|
| `baseline.py` | TF-IDF + LogReg supervised baseline (trains on ISOT) | `data/*.csv` | `metrics_baseline.json`, `baseline_model.pkl` |
| `roc_curve.py` | ROC + AUC; prints the Youden-optimal verdict threshold to set in backend config | `results_*.csv` | `roc_curve.png` |
| `calibration.py` | Reliability diagram + ECE/MCE; `--platt` adds out-of-sample (k-fold) Platt scaling | `results_*.csv` | `calibration.png` |
| `significance.py` | McNemar's test + bootstrap F1 CI between ablation variants | `results_*.csv` + `metrics_ablation_*.json` | `significance_table.csv`, `bootstrap_f1.csv` |
| `adversarial.py` | Verdict stability under text perturbations (typo/swap/caps/noise) | API + `data/*.csv` | `adversarial_results.csv` |
| `prompt_injection.py` | Robustness to instructions hidden in the article text (with a control condition) | API + `data/*.csv` | `prompt_injection_results.csv`, `prompt_injection.png` |
| `cost_latency.py` | Accuracy vs. latency vs. estimated $/1k analyses across providers | `metrics_*.json` | `cost_latency.csv`, `cost_latency.png` |
| `cross_dataset.py` | Generalization: train on ISOT, test on LIAR (baseline vs LLM) | `baseline_model.pkl`, LIAR data | figures |
| `liar_prep.py` | Convert the LIAR dataset into the harness format | LIAR `.tsv` | normalized CSV |
| `coverage_curve.py` | Accuracy vs. abstention (uncertain) trade-off | `results_*.csv` | figure |
| `error_analysis.py` | Categorize false positives / false negatives | `results_*.csv` | summary |
| `model_agreement.py` | Agreement between providers/runs | multiple `results_*.csv` | summary |

## Model-capacity sweep

The 8B model could not separate ISOT fake from real (AUC ~0.55 — scores for
true and fake articles overlap almost entirely). `run_models.ps1` re-runs the
eval across larger Groq models so you can see whether capacity is the bottleneck:

```powershell
.\run_models.ps1 -Max 100
python roc_curve.py results_model_*.csv   # compare AUC; ~0.5 = no separation
```

If AUC jumps with a bigger model, adopt it and re-tune the threshold. If it
stays flat, the task is genuinely hard from text alone — itself a thesis finding.

## Ablation study

`run_ablation.ps1` runs the harness once per prompt variant
(`zero_shot | skepticism | few_shot | full`), each with `--retries 3` and
cache bypass, producing `metrics_ablation_*.json` for `significance.py`.

## One-command pipeline

`run_full_evaluation.ps1` chains every analysis step, isolates failures (one
broken step doesn't abort the rest), and collects all figures into `figures/`.

```powershell
.\run_full_evaluation.ps1                      # analyse the newest results_*.csv
.\run_full_evaluation.ps1 -Max 200             # run the live harness first (needs backend + key)
.\run_full_evaluation.ps1 -ResultsCsv results_200.csv
.\run_full_evaluation.ps1 -Max 200 -Live -Injection   # full run incl. live robustness tests
```

It sets `PYTHONIOENCODING=utf-8` so Unicode banners don't crash the Windows console.

## Suggested order (if running manually)

1. Run the C# harness (>=200/class) -> `results_*.csv`, `metrics_*.json`
2. `baseline.py` -> classical baseline to compare against
3. `roc_curve.py` -> read off the optimal threshold, set it in backend `VerdictThresholds`
4. `calibration.py --platt` -> calibration before/after
5. `significance.py` -> is the best ablation variant significantly better?
6. `cost_latency.py` -> is the LLM worth it vs. the baseline?
7. `adversarial.py` / `prompt_injection.py` -> robustness
8. `liar_prep.py` then `cross_dataset.py` -> generalization to LIAR
