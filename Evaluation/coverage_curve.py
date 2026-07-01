"""
Coverage-accuracy (selective prediction) curve.

Shows: if the model abstains on low-confidence predictions, how much does
accuracy improve on the ones it keeps?

Key insight: a well-calibrated model should have much higher accuracy on
high-confidence predictions. This plot shows whether the LLM's confidence
score is actually informative.

Usage:
    python coverage_curve.py                    # most recent results_*.csv
    python coverage_curve.py results_XYZ.csv
"""

import sys
import glob
import json
import numpy as np
import pandas as pd

try:
    import matplotlib.pyplot as plt
    HAS_PLOT = True
except ImportError:
    HAS_PLOT = False

# ── Load ──────────────────────────────────────────────────────────────────────
paths = [a for a in sys.argv[1:] if a.endswith(".csv")]
if not paths:
    paths = sorted(glob.glob("results_*.csv"))
if not paths:
    print("No results_*.csv found.")
    sys.exit(1)

fig, ax = plt.subplots(figsize=(8, 5)) if HAS_PLOT else (None, None)
colors = ["#4f8ef7", "#e06c3a", "#5cb85c", "#9b59b6"]

for i, path in enumerate(paths):
    df = pd.read_csv(path)
    df = df[df["IsError"] == False].copy()
    if len(df) < 5:
        continue

    # Sort by confidence descending — most confident first
    df = df.sort_values("Confidence", ascending=False).reset_index(drop=True)

    coverages, accuracies = [], []
    for cutoff in range(1, len(df) + 1):
        subset = df.iloc[:cutoff]
        acc = subset["Correct"].mean()
        coverage = cutoff / len(df)
        coverages.append(coverage)
        accuracies.append(acc)

    label = path.replace("results_", "").replace(".csv", "").replace("_", " ")
    color = colors[i % len(colors)]

    print(f"\n{path}")
    print(f"  Full coverage accuracy:  {df['Correct'].mean()*100:.1f}%")
    # Find accuracy at 50% coverage
    half = df.iloc[:len(df)//2]
    print(f"  Top-50% confidence accuracy: {half['Correct'].mean()*100:.1f}%")
    # Find accuracy at 25% coverage
    quarter = df.iloc[:len(df)//4]
    print(f"  Top-25% confidence accuracy: {quarter['Correct'].mean()*100:.1f}%  (n={len(quarter)})")

    # Check if confidence is actually discriminative
    conf_corr = df[["Confidence", "Correct"]].corr().iloc[0, 1]
    print(f"  Confidence-Correct correlation: {conf_corr:.3f}")
    if abs(conf_corr) < 0.05:
        print("  [!] Confidence score is not informative (near-zero correlation)")
    elif conf_corr > 0.15:
        print("  [ok] Higher confidence correlates with correctness")

    if HAS_PLOT:
        ax.plot(coverages, accuracies, lw=2, color=color, label=label)
        # Mark the full-coverage point
        ax.scatter([1.0], [df["Correct"].mean()], s=60, color=color, zorder=5)

if HAS_PLOT:
    ax.axhline(0.5, color="gray", linestyle="--", lw=1, alpha=0.5, label="Random (50%)")
    ax.set_xlabel("Coverage (fraction of predictions kept)", fontsize=12)
    ax.set_ylabel("Accuracy on kept predictions", fontsize=12)
    ax.set_title("Coverage-Accuracy Curve\n(left = highest confidence only; right = all predictions)", fontsize=12)
    ax.yaxis.set_major_formatter(plt.FuncFormatter(lambda v, _: f"{v*100:.0f}%"))
    ax.xaxis.set_major_formatter(plt.FuncFormatter(lambda v, _: f"{v*100:.0f}%"))
    ax.set_xlim(0, 1.05)
    ax.legend(fontsize=9)
    ax.grid(alpha=0.25)
    plt.tight_layout()
    out = "coverage_curve.png"
    plt.savefig(out, dpi=150)
    print(f"\nSaved: {out}")
    if plt.get_backend().lower() != "agg":
        plt.show()
