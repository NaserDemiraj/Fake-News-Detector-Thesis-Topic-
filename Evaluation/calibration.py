"""
Reliability diagram + Expected Calibration Error (ECE) for the LLM classifier.
Optionally fits Platt scaling (logistic regression on raw scores) and overlays
the calibrated curve, showing ECE improvement.

Treats Score (0-100) as the model's raw probability of an article being true.

Usage:
    python calibration.py                    # uses most recent results_*.csv
    python calibration.py results_XYZ.csv   # specific file
    python calibration.py --platt           # include Platt-scaled calibration curve

Output: calibration.png + calibration_platt.png (if --platt) + prints ECE / MCE
"""

import sys
import glob
import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.gridspec as gridspec
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import cross_val_predict, StratifiedKFold

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "axes.spines.top": False,
    "axes.spines.right": False,
    "figure.dpi": 150,
})

N_BINS = 10
COLORS = ["#4f8ef7", "#e06c3a", "#5cb85c", "#9b59b6"]
COLORS_CAL = ["#1a5fcc", "#a03010", "#2e7a2e", "#5a1e8a"]  # darker versions for calibrated


def load_results_csv(path):
    df = pd.read_csv(path)
    df = df[df["IsError"] == False].copy()
    df["y_true"] = (df["TrueLabel"] == "true").astype(int)
    df["prob"] = df["Score"] / 100.0
    return df


def calibration_stats(probs, y_true, n_bins=N_BINS):
    """Return per-bin (mean_conf, mean_acc, count) and overall ECE/MCE."""
    bins = np.linspace(0.0, 1.0, n_bins + 1)
    bin_ids = np.digitize(probs, bins) - 1
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
            confs.append(probs[mask].mean())
            accs.append(y_true[mask].mean())
            counts.append(n)

    confs = np.array(confs)
    accs = np.array(accs)
    counts = np.array(counts)
    total = counts.sum()

    valid = counts > 0
    ece = float(np.sum(counts[valid] / total * np.abs(confs[valid] - accs[valid])))
    mce = float(np.max(np.abs(confs[valid] - accs[valid]))) if valid.any() else 0.0

    return confs, accs, counts, ece, mce


def platt_scale_oos(probs, y_true, n_splits=5):
    """
    Out-of-sample Platt scaling via stratified k-fold cross-validation.

    Each sample's calibrated probability is produced by a logistic-regression
    scaler trained ONLY on the other folds — so the reported ECE is an honest
    held-out estimate, not an in-sample fit. This is the defensible way to
    report calibration gain on a small dataset (no data wasted on one split).

    Returns calibrated probabilities aligned to the input order, or None if
    there are too few samples / only one class present.
    """
    n = len(probs)
    pos = int(y_true.sum())
    # Need at least n_splits per class for stratified folds to be valid
    folds = min(n_splits, pos, n - pos)
    if folds < 2:
        return None

    lr = LogisticRegression(C=1e9, solver="lbfgs", max_iter=1000)
    skf = StratifiedKFold(n_splits=folds, shuffle=True, random_state=42)
    calibrated = cross_val_predict(
        lr, probs.reshape(-1, 1), y_true,
        cv=skf, method="predict_proba")[:, 1]
    return calibrated


def friendly_name(path):
    base = os.path.basename(path)
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


def draw_reliability_axis(ax_rel, ax_hist, csvs, do_platt, offsets, bar_width):
    """Render reliability diagram + histogram. Returns list of (name, ece_raw, ece_cal) rows."""
    summary = []
    for i, csv_path in enumerate(csvs):
        df = load_results_csv(csv_path)
        if len(df) < 10:
            print(f"Skipping {csv_path}: fewer than 10 valid predictions")
            continue

        probs   = df["prob"].values
        y_true  = df["y_true"].values
        confs, accs, counts, ece, mce = calibration_stats(probs, y_true)
        name = friendly_name(csv_path)
        color = COLORS[i % len(COLORS)]

        print(f"{name}: ECE_raw={ece:.4f}  MCE={mce:.4f}  n={int(counts.sum())}", end="")

        valid = ~np.isnan(confs)
        ax_rel.plot(confs[valid], accs[valid], "o-", lw=1.8, ms=6,
                    color=color, label=f"{name}  ECE={ece:.3f}")
        for b in range(N_BINS):
            if not np.isnan(confs[b]):
                ax_rel.plot([confs[b], confs[b]], [confs[b], accs[b]],
                            color=color, lw=1.5, alpha=0.35)

        ece_cal = None
        if do_platt:
            probs_cal = platt_scale_oos(probs, y_true)
            if probs_cal is not None:
                confs_c, accs_c, counts_c, ece_cal, mce_cal = calibration_stats(probs_cal, y_true)
                color_c = COLORS_CAL[i % len(COLORS_CAL)]
                valid_c = ~np.isnan(confs_c)
                ax_rel.plot(confs_c[valid_c], accs_c[valid_c], "s--", lw=1.5, ms=5,
                            color=color_c, alpha=0.85,
                            label=f"{name} + Platt (CV)  ECE={ece_cal:.3f}")
                print(f"  ECE_platt(oos)={ece_cal:.4f}  improvement={ece - ece_cal:+.4f}", end="")
            else:
                print(f"  (too few samples per class for k-fold Platt: n={len(df)})", end="")
        print()

        bin_centers = np.linspace(0.05, 0.95, N_BINS)
        fracs = counts / counts.sum()
        ax_hist.bar(bin_centers + offsets[i], fracs, width=0.07,
                    color=color, alpha=0.65, label=name)

        summary.append((name, ece, ece_cal))
    return summary


# ── Parse args ────────────────────────────────────────────────────────────────
do_platt = "--platt" in sys.argv
csvs_arg = [a for a in sys.argv[1:] if a.endswith(".csv")]
if not csvs_arg:
    ablation_csvs = sorted(glob.glob("results_2026062*.csv"))
    csvs_arg = ablation_csvs if ablation_csvs else sorted(glob.glob("results_*.csv"))[-1:]

if not csvs_arg:
    print("No results_*.csv found. Run the evaluation harness first.")
    sys.exit(1)

# ── Layout ────────────────────────────────────────────────────────────────────
fig = plt.figure(figsize=(8, 8), layout="constrained")
gs = gridspec.GridSpec(2, 1, height_ratios=[3, 1], hspace=0.08, figure=fig)
ax_rel  = fig.add_subplot(gs[0])
ax_hist = fig.add_subplot(gs[1], sharex=ax_rel)

ax_rel.plot([0, 1], [0, 1], "k--", lw=1.2, alpha=0.5, label="Perfect calibration")
ax_rel.fill_between([0, 1], [0, 1], [1, 1], alpha=0.04, color="red")
ax_rel.fill_between([0, 1], [0, 0], [0, 1], alpha=0.04, color="blue")
ax_rel.text(0.72, 0.55, "Overconfident", fontsize=8, color="red",  alpha=0.7)
ax_rel.text(0.08, 0.42, "Underconfident", fontsize=8, color="blue", alpha=0.7)

bar_width = 0.8 / len(csvs_arg) / N_BINS
offsets = np.linspace(
    -bar_width * (len(csvs_arg) - 1) / 2,
     bar_width * (len(csvs_arg) - 1) / 2,
    len(csvs_arg))

summary = draw_reliability_axis(ax_rel, ax_hist, csvs_arg, do_platt, offsets, bar_width)

# ── Print summary table ───────────────────────────────────────────────────────
if do_platt and any(row[2] is not None for row in summary):
    print()
    print("-" * 56)
    print(f"{'Variant':<22} {'ECE_raw':>8} {'ECE_platt':>10} {'Delta':>8}")
    print("-" * 56)
    for name, ece_raw, ece_cal in summary:
        delta = f"{ece_raw - ece_cal:.4f}" if ece_cal is not None else "  n/a"
        ece_c = f"{ece_cal:.4f}" if ece_cal is not None else "  n/a"
        print(f"{name:<22} {ece_raw:>8.4f} {ece_c:>10} {delta:>8}")
    print("-" * 56)
    print("(Platt scaling via 5-fold CV — out-of-sample, honest calibration gain)")

# ── Reliability diagram formatting ───────────────────────────────────────────
ax_rel.set_xlim([0, 1])
ax_rel.set_ylim([0, 1.05])
ax_rel.set_ylabel("Fraction of true articles  (accuracy)", fontsize=11)
title = "Reliability Diagram — Fake News Detector"
if do_platt:
    title += "\n(solid = raw score, dashed = Platt-scaled)"
ax_rel.set_title(title, fontsize=13, fontweight="bold")
ax_rel.legend(loc="upper left", fontsize=8, framealpha=0.9)
ax_rel.grid(True, alpha=0.18)
plt.setp(ax_rel.get_xticklabels(), visible=False)

ax_hist.set_xlim([0, 1])
ax_hist.set_xlabel("Predicted confidence  (Score / 100)", fontsize=11)
ax_hist.set_ylabel("Sample\nfraction", fontsize=9)
ax_hist.grid(True, alpha=0.18)
ax_hist.tick_params(axis="x", labelsize=9)

out = "calibration_platt.png" if do_platt else "calibration.png"
plt.savefig(out, dpi=150, bbox_inches="tight")
print(f"\nSaved: {out}")
plt.show()
