"""
ROC curve + AUC for the LLM classifier and the TF-IDF baseline.
Uses the numeric score (0-100) as the continuous predictor.

Usage:
    python roc_curve.py                    # uses most recent results_*.csv
    python roc_curve.py results_XYZ.csv   # specific file

Output: roc_curve.png  (thesis-ready figure)
"""

import sys
import glob
import json
import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from sklearn.metrics import roc_curve, auc, roc_auc_score

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "axes.spines.top": False,
    "axes.spines.right": False,
    "figure.dpi": 150,
})

fig, ax = plt.subplots(figsize=(7, 6))

# ── Perfect calibration reference ────────────────────────────────────────────
ax.plot([0, 1], [0, 1], "k--", lw=1, alpha=0.4, label="Random classifier (AUC = 0.50)")

colors = ["#4f8ef7", "#e06c3a", "#5cb85c", "#9b59b6"]
color_i = 0
recommended_thresholds = []  # (label, T_on_0-100, tpr, fpr) per run

# ── Load LLM results ─────────────────────────────────────────────────────────
def load_results_csv(path):
    df = pd.read_csv(path)
    df = df[df["IsError"] == False].copy()
    df["y_true"]  = (df["TrueLabel"] == "true").astype(int)
    df["y_score"] = df["Score"] / 100.0
    return df

csvs_arg = [a for a in sys.argv[1:] if a.endswith(".csv")]
if not csvs_arg:
    csvs_arg = sorted(glob.glob("results_*.csv"))

if not csvs_arg:
    print("No results_*.csv found. Run the evaluation harness first.")
    sys.exit(1)

for csv_path in csvs_arg:
    df = load_results_csv(csv_path)
    if len(df) < 10:
        print(f"Skipping {csv_path}: fewer than 10 valid predictions")
        continue

    fpr, tpr, thresholds = roc_curve(df["y_true"], df["y_score"])
    roc_auc = auc(fpr, tpr)

    # Find threshold that maximises Youden's J (sensitivity + specificity - 1)
    j_scores = tpr - fpr
    best_idx = np.argmax(j_scores)
    best_t   = thresholds[best_idx]
    best_fpr = fpr[best_idx]
    best_tpr = tpr[best_idx]

    label = csv_path.replace("results_", "").replace(".csv", "").replace("_", " ")
    color = colors[color_i % len(colors)]
    color_i += 1

    ax.plot(fpr, tpr, lw=2, color=color,
            label=f"LLM ({label}) — AUC = {roc_auc:.3f}")
    ax.scatter([best_fpr], [best_tpr], s=80, zorder=5, color=color,
               marker="o", edgecolors="white", linewidths=1.5)
    ax.annotate(f"T={best_t*100:.0f}", (best_fpr, best_tpr),
                textcoords="offset points", xytext=(8, -12),
                fontsize=8, color=color)

    print(f"{csv_path}: AUC={roc_auc:.3f}, best threshold={best_t*100:.0f} "
          f"(TPR={best_tpr:.2f}, FPR={best_fpr:.2f})")
    recommended_thresholds.append((label, best_t * 100, best_tpr, best_fpr))

# ── Recommended backend config (close the data → config loop) ─────────────────
# The Youden-optimal point is a single binary cutoff. To apply it in the backend,
# set BOTH VerdictThresholds:FakeMax and TrueMin to this value (binary mode, no
# "uncertain" band → 100% coverage, maximised sensitivity + specificity).
if recommended_thresholds:
    # Average across runs if multiple; the single run otherwise.
    avg_t = float(np.mean([t for _, t, _, _ in recommended_thresholds]))
    print()
    print("=" * 60)
    print("  RECOMMENDED VERDICT THRESHOLD (from Youden's J)")
    print("=" * 60)
    for lbl, t, tpr_, fpr_ in recommended_thresholds:
        print(f"    {lbl:<28} T = {t:.0f}  (sens={tpr_:.2f}, spec={1-fpr_:.2f})")
    print(f"\n  Suggested appsettings.json (binary cutoff at T={avg_t:.0f}):")
    print('    "VerdictThresholds": {')
    print(f'      "FakeMax": {avg_t:.0f},')
    print(f'      "TrueMin": {avg_t:.0f}')
    print("    }")
    print("  (Keep FakeMax < TrueMin instead if you want an explicit uncertain band.)")
    print("=" * 60)

# ── Load TF-IDF baseline (if available) ──────────────────────────────────────
if os.path.exists("metrics_baseline.json"):
    with open("metrics_baseline.json", encoding="utf-8-sig") as f:  # utf-8-sig: strip BOM
        bl = json.load(f)
    # Baseline is a point estimate (no continuous score), plot as a single point
    precision = bl.get("precision", 0)
    recall    = bl.get("recall",    0)
    specificity = bl.get("specificity", 0)
    fpr_point = 1 - specificity
    tpr_point = recall
    ax.scatter([fpr_point], [tpr_point], s=120, zorder=6, color="#5cb85c",
               marker="*", label=f"TF-IDF baseline (F1={bl.get('f1',0):.3f})")
    ax.annotate("TF-IDF", (fpr_point, tpr_point),
                textcoords="offset points", xytext=(8, 4), fontsize=8, color="#5cb85c")

# ── Formatting ────────────────────────────────────────────────────────────────
ax.set_xlabel("False Positive Rate  (1 − Specificity)", fontsize=12)
ax.set_ylabel("True Positive Rate  (Sensitivity / Recall)", fontsize=12)
ax.set_title("ROC Curve — Fake News Detector", fontsize=14, fontweight="bold")
ax.set_xlim([0, 1]); ax.set_ylim([0, 1.02])
ax.legend(loc="lower right", fontsize=9, framealpha=0.9)
ax.grid(True, alpha=0.2)

out = "roc_curve.png"
plt.tight_layout()
plt.savefig(out, dpi=150)
print(f"\nSaved: {out}")
plt.show()
