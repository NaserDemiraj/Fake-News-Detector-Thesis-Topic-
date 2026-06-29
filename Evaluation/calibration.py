"""
Reliability diagram + Expected Calibration Error (ECE) for the LLM classifier.

Treats Score (0-100) as the model's probability of an article being true.
Bins predictions, compares avg confidence vs avg accuracy in each bin.

Usage:
    python calibration.py                    # uses most recent results_*.csv
    python calibration.py results_XYZ.csv   # specific file
    python calibration.py r1.csv r2.csv     # overlay multiple runs

Output: calibration.png + prints ECE / MCE per run
"""

import sys
import glob
import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.gridspec as gridspec

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "axes.spines.top": False,
    "axes.spines.right": False,
    "figure.dpi": 150,
})

N_BINS = 10
COLORS = ["#4f8ef7", "#e06c3a", "#5cb85c", "#9b59b6"]


def load_results_csv(path):
    df = pd.read_csv(path)
    df = df[df["IsError"] == False].copy()
    df["y_true"] = (df["TrueLabel"] == "true").astype(int)
    df["prob"] = df["Score"] / 100.0
    return df


def calibration_stats(df, n_bins=N_BINS):
    """Return per-bin (mean_conf, mean_acc, count) and overall ECE/MCE."""
    bins = np.linspace(0.0, 1.0, n_bins + 1)
    bin_ids = np.digitize(df["prob"], bins) - 1
    bin_ids = np.clip(bin_ids, 0, n_bins - 1)

    confs, accs, counts = [], [], []
    for b in range(n_bins):
        mask = bin_ids == b
        n = mask.sum()
        if n == 0:
            confs.append(np.nan)
            accs.append(np.nan)
            counts.append(0)
        else:
            confs.append(df.loc[mask, "prob"].mean())
            accs.append(df.loc[mask, "y_true"].mean())
            counts.append(n)

    confs = np.array(confs)
    accs = np.array(accs)
    counts = np.array(counts)
    total = counts.sum()

    valid = counts > 0
    ece = float(np.sum(counts[valid] / total * np.abs(confs[valid] - accs[valid])))
    mce = float(np.max(np.abs(confs[valid] - accs[valid]))) if valid.any() else 0.0

    return confs, accs, counts, ece, mce


def friendly_name(path):
    base = os.path.basename(path)
    # Map ablation suffixes to readable labels
    mapping = {
        "zero_shot": "Zero-Shot",
        "skepticism": "Skepticism",
        "few_shot": "Few-Shot",
        "full": "Full Prompt",
    }
    name = base.replace("results_", "").replace(".csv", "")
    for key, label in mapping.items():
        if key in name:
            return f"Groq {label}"
    return name.replace("_", " ")


# ── Load CSVs ─────────────────────────────────────────────────────────────────
csvs_arg = [a for a in sys.argv[1:] if a.endswith(".csv")]
if not csvs_arg:
    # Default: all four ablation variants, or the single most recent
    ablation_csvs = sorted(glob.glob("results_2026062*.csv"))
    csvs_arg = ablation_csvs if ablation_csvs else sorted(glob.glob("results_*.csv"))[-1:]

if not csvs_arg:
    print("No results_*.csv found. Run the evaluation harness first.")
    sys.exit(1)

# ── Layout: reliability diagram (top) + sample histogram (bottom) ─────────────
fig = plt.figure(figsize=(8, 8), layout="constrained")
gs = gridspec.GridSpec(2, 1, height_ratios=[3, 1], hspace=0.08, figure=fig)
ax_rel = fig.add_subplot(gs[0])
ax_hist = fig.add_subplot(gs[1], sharex=ax_rel)

# Perfect calibration line
ax_rel.plot([0, 1], [0, 1], "k--", lw=1.2, alpha=0.5, label="Perfect calibration")
# Shade the overconfident / underconfident regions
ax_rel.fill_between([0, 1], [0, 1], [1, 1], alpha=0.04, color="red")
ax_rel.fill_between([0, 1], [0, 0], [0, 1], alpha=0.04, color="blue")
ax_rel.text(0.72, 0.55, "Overconfident", fontsize=8, color="red", alpha=0.7)
ax_rel.text(0.08, 0.42, "Underconfident", fontsize=8, color="blue", alpha=0.7)

bin_centers = np.linspace(0.05, 0.95, N_BINS)
bar_width = 0.8 / len(csvs_arg) / N_BINS  # thin bars, side by side
offsets = np.linspace(-bar_width * (len(csvs_arg) - 1) / 2,
                       bar_width * (len(csvs_arg) - 1) / 2,
                       len(csvs_arg))

for i, csv_path in enumerate(csvs_arg):
    df = load_results_csv(csv_path)
    if len(df) < 10:
        print(f"Skipping {csv_path}: fewer than 10 valid predictions")
        continue

    confs, accs, counts, ece, mce = calibration_stats(df)
    name = friendly_name(csv_path)
    color = COLORS[i % len(COLORS)]

    print(f"{name}: ECE={ece:.4f}  MCE={mce:.4f}  n={counts.sum()}")

    valid = ~np.isnan(confs)

    # Reliability diagram — line + dots
    ax_rel.plot(confs[valid], accs[valid], "o-", lw=1.8, ms=6,
                color=color, label=f"{name}  (ECE={ece:.3f})")

    # Gap bars (actual − expected) shown as thin vertical lines
    for b in range(N_BINS):
        if not np.isnan(confs[b]):
            ax_rel.plot([confs[b], confs[b]], [confs[b], accs[b]],
                        color=color, lw=1.5, alpha=0.35)

    # Sample count histogram
    fracs = counts / counts.sum()
    ax_hist.bar(bin_centers + offsets[i], fracs, width=0.07,
                color=color, alpha=0.65, label=name)

# ── Reliability diagram formatting ────────────────────────────────────────────
ax_rel.set_xlim([0, 1])
ax_rel.set_ylim([0, 1.05])
ax_rel.set_ylabel("Fraction of true articles  (accuracy)", fontsize=11)
ax_rel.set_title("Reliability Diagram — Fake News Detector\n"
                 "(Score as model confidence in 'true')", fontsize=13, fontweight="bold")
ax_rel.legend(loc="upper left", fontsize=9, framealpha=0.9)
ax_rel.grid(True, alpha=0.18)
plt.setp(ax_rel.get_xticklabels(), visible=False)

# ── Histogram formatting ───────────────────────────────────────────────────────
ax_hist.set_xlim([0, 1])
ax_hist.set_xlabel("Predicted confidence  (Score / 100)", fontsize=11)
ax_hist.set_ylabel("Sample\nfraction", fontsize=9)
ax_hist.grid(True, alpha=0.18)
ax_hist.tick_params(axis="x", labelsize=9)

out = "calibration.png"
plt.savefig(out, dpi=150, bbox_inches="tight")
print(f"\nSaved: {out}")
plt.show()
