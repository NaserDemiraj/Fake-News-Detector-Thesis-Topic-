"""
Pairwise statistical significance tests between ablation variants.

For each pair of variants, runs:
  - McNemar's test on per-item correct/incorrect
  - Bootstrap 95% CI on F1 score (1000 resamples)

Usage:
    python significance.py              # uses all metrics_ablation_*.json + results CSVs
    python significance.py r1.csv r2.csv

Output: significance_table.csv  +  bootstrap_f1.csv  +  terminal table
"""

import sys
import glob
import json
import os
import itertools
import numpy as np
import pandas as pd
from scipy.stats import chi2

np.random.seed(42)
N_BOOTSTRAP = 1000
ABLATION_ORDER = ["zero_shot", "skepticism", "few_shot", "full"]


def load_csv(path):
    df = pd.read_csv(path)
    df = df[df["IsError"] == False].copy()
    df["correct_int"] = (df["Correct"] == True).astype(int)
    return df[["Index", "TrueLabel", "PredictedVerdict", "Score", "correct_int"]].reset_index(drop=True)


def mcnemar(c1, c2):
    b = int(((c1 == 1) & (c2 == 0)).sum())
    c = int(((c1 == 0) & (c2 == 1)).sum())
    bc = b + c
    if bc == 0:
        return 0.0, 1.0
    if bc < 25:
        from scipy.stats import binom
        p = 2 * binom.sf(max(b, c) - 1, bc, 0.5)
        return float(max(b, c)), float(min(p, 1.0))
    stat = (abs(b - c) - 1) ** 2 / bc
    return float(stat), float(chi2.sf(stat, df=1))


def bootstrap_f1(y_true, y_pred_verdict, n=N_BOOTSTRAP):
    pos = (y_pred_verdict == "likely_true").astype(int)
    y_t = (y_true == "true").astype(int)

    def f1(yt, yp):
        tp = ((yt == 1) & (yp == 1)).sum()
        fp = ((yt == 0) & (yp == 1)).sum()
        fn = ((yt == 1) & (yp == 0)).sum()
        pr = tp / (tp + fp) if (tp + fp) > 0 else 0.0
        re = tp / (tp + fn) if (tp + fn) > 0 else 0.0
        return 2 * pr * re / (pr + re) if (pr + re) > 0 else 0.0

    n_s = len(y_t)
    point = f1(y_t, pos)
    boots = [f1(y_t[idx := np.random.randint(0, n_s, n_s)], pos[idx]) for _ in range(n)]
    return point, float(np.percentile(boots, 2.5)), float(np.percentile(boots, 97.5))


# ── Build (csv_path, label) pairs from metrics_ablation_*.json ───────────────
def discover_ablation_pairs():
    ts_to_csv = {}
    for p in glob.glob("results_*.csv"):
        key = os.path.basename(p).replace("results_", "").replace(".csv", "")
        ts_to_csv[key] = p

    pairs = []
    for variant in ABLATION_ORDER:
        jf = f"metrics_ablation_{variant}.json"
        if not os.path.exists(jf):
            continue
        try:
            with open(jf, encoding="utf-8-sig") as f:
                m = json.load(f)
            raw_ts = m.get("timestamp", "")[:19]           # "2026-06-29T01:06:09"
            ts_key = (raw_ts
                      .replace("-", "")
                      .replace("T", "_")
                      .replace(":", ""))                    # "20260629_010609"
            label = m.get("backendLabel", variant.replace("_", " ").title())
            if ts_key in ts_to_csv:
                pairs.append((ts_to_csv[ts_key], label))
        except Exception:
            pass
    return pairs


# ── Discover CSVs ─────────────────────────────────────────────────────────────
explicit_csvs = [a for a in sys.argv[1:] if a.endswith(".csv")]

if explicit_csvs:
    # User supplied paths; use timestamp lookup for labels where possible
    ab_pairs = discover_ablation_pairs()
    path_to_label = {p: lbl for p, lbl in ab_pairs}
    csv_label_pairs = [(p, path_to_label.get(p, os.path.basename(p).replace("results_", "").replace(".csv", "")))
                       for p in explicit_csvs]
else:
    csv_label_pairs = discover_ablation_pairs()
    if not csv_label_pairs:
        # Fallback to most recent 4 CSVs with timestamp names
        recent = sorted(glob.glob("results_*.csv"))[-4:]
        csv_label_pairs = [(p, os.path.basename(p).replace("results_", "").replace(".csv", ""))
                           for p in recent]

if len(csv_label_pairs) < 2:
    print("Need at least 2 results CSVs to compare. Run the ablation harness first.")
    sys.exit(1)

# ── Load and align on shared Index ───────────────────────────────────────────
dfs = {}
for csv_path, label in csv_label_pairs:
    df = load_csv(csv_path)
    dfs[label] = df
    print(f"Loaded {label}: {len(df)} valid predictions")

common_idx = None
for df in dfs.values():
    s = set(df["Index"])
    common_idx = s if common_idx is None else common_idx & s
common_idx = sorted(common_idx)
print(f"\nShared predictions across all variants: {len(common_idx)}\n")

aligned = {name: df[df["Index"].isin(common_idx)].sort_values("Index").reset_index(drop=True)
           for name, df in dfs.items()}

# ── Per-variant bootstrap F1 + CI ─────────────────────────────────────────────
print("-" * 62)
print(f"{'Variant':<24}  {'F1':>6}  {'95% CI':>18}  n")
print("-" * 62)
variant_stats = {}
for name, df in aligned.items():
    f1, lo, hi = bootstrap_f1(df["TrueLabel"].values, df["PredictedVerdict"].values)
    variant_stats[name] = (f1, lo, hi, len(df))
    print(f"{name:<24}  {f1:.4f}  [{lo:.4f}, {hi:.4f}]  {len(df)}")

# ── Pairwise McNemar ──────────────────────────────────────────────────────────
names = list(aligned.keys())
rows = []
print("\n" + "-" * 62)
print(f"{'Pair':<40}  {'stat':>6}  {'p-value':>9}  sig?")
print("-" * 62)

for a, b in itertools.combinations(names, 2):
    c1 = aligned[a]["correct_int"].values
    c2 = aligned[b]["correct_int"].values
    stat, p = mcnemar(c1, c2)
    identical = (c1 == c2).all()
    sig = ("***" if p < 0.001 else "**" if p < 0.01 else "*" if p < 0.05 else "ns")
    note = " (identical — likely cache hit)" if identical else ""
    pair = f"{a} vs {b}"
    print(f"{pair:<40}  {stat:>6.2f}  {p:>9.4f}  {sig}{note}")
    rows.append({"Pair": pair, "Stat": round(stat, 4), "P-value": round(p, 4),
                 "Significant": p < 0.05, "Note": sig + note})

print("-" * 62)
print("Significance: *** p<0.001  ** p<0.01  * p<0.05  ns=not significant\n")

# ── Save outputs ──────────────────────────────────────────────────────────────
pd.DataFrame(rows).to_csv("significance_table.csv", index=False)
boot_rows = [{"Variant": n, "F1": round(f, 4), "CI_low": round(lo, 4),
              "CI_high": round(hi, 4), "n": nn}
             for n, (f, lo, hi, nn) in variant_stats.items()]
pd.DataFrame(boot_rows).to_csv("bootstrap_f1.csv", index=False)
print("Saved: significance_table.csv  bootstrap_f1.csv")
