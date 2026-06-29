"""
Inter-model agreement analysis: Groq vs Gemini on the same articles.
Computes Cohen's kappa and shows where the models disagree.

Requirements:
    pip install pandas scikit-learn
    Need two results CSVs from running the same articles through both models.

Usage:
    python model_agreement.py results_groq.csv results_gemini.csv

The CSVs must have the same articles in the same order (same --true/--fake
files and --max value, run back-to-back with matching indices).
"""

import sys
import glob
import json
import pandas as pd
from sklearn.metrics import cohen_kappa_score, confusion_matrix

def load_csv(path):
    df = pd.read_csv(path)
    df = df[df["IsError"] == False].copy()
    df["binary"] = df["PredictedVerdict"].map({
        "likely_true": 1, "likely_fake": 0, "uncertain": -1
    })
    return df

# ── Find files ────────────────────────────────────────────────────────────────
if len(sys.argv) >= 3:
    path_a, path_b = sys.argv[1], sys.argv[2]
else:
    csvs = sorted(glob.glob("results_*.csv"))
    if len(csvs) < 2:
        print("Need at least 2 results_*.csv files.")
        print("Usage: python model_agreement.py results_A.csv results_B.csv")
        sys.exit(1)
    path_a, path_b = csvs[-2], csvs[-1]
    print(f"Auto-selected: {path_a} vs {path_b}")

df_a = load_csv(path_a)
df_b = load_csv(path_b)

label_a = path_a.replace("results_", "").replace(".csv", "").replace("_", " ")
label_b = path_b.replace("results_", "").replace(".csv", "").replace("_", " ")

# ── Align on Index (same article position in both runs) ───────────────────────
merged = df_a[["Index", "TrueLabel", "PredictedVerdict", "Score", "binary"]].merge(
    df_b[["Index", "PredictedVerdict", "Score", "binary"]],
    on="Index", suffixes=("_a", "_b")
)

print(f"\nComparing:")
print(f"  A: {path_a}  ({len(df_a)} valid predictions)")
print(f"  B: {path_b}  ({len(df_b)} valid predictions)")
print(f"  Matched on Index: {len(merged)} articles\n")

if len(merged) < 10:
    print("Not enough matched articles — make sure both runs used the same dataset and --max value.")
    sys.exit(1)

# ── Overall agreement ────────────────────────────────────────────────────────
agree      = (merged["binary_a"] == merged["binary_b"]).sum()
total      = len(merged)
agree_rate = agree / total

# Kappa on non-uncertain predictions (exclude -1)
both_committed = merged[(merged["binary_a"] != -1) & (merged["binary_b"] != -1)]
kappa = cohen_kappa_score(both_committed["binary_a"], both_committed["binary_b"]) if len(both_committed) > 5 else None

print("╔══════════════════════════════════════════════╗")
print("║         INTER-MODEL AGREEMENT REPORT         ║")
print("╠══════════════════════════════════════════════╣")
print(f"║  Model A  : {label_a[:36]:<36} ║")
print(f"║  Model B  : {label_b[:36]:<36} ║")
print("╠══════════════════════════════════════════════╣")
print(f"║  Articles compared   : {total:<5}                ║")
print(f"║  Agreement rate      : {agree_rate*100:.1f}%               ║")
if kappa is not None:
    kappa_interp = ("poor" if kappa < 0.2 else
                    "fair" if kappa < 0.4 else
                    "moderate" if kappa < 0.6 else
                    "substantial" if kappa < 0.8 else "near-perfect")
    print(f"║  Cohen's kappa       : {kappa:.3f}  ({kappa_interp})      ║")
print("╚══════════════════════════════════════════════╝")

# ── Disagreement breakdown ────────────────────────────────────────────────────
disagree = merged[merged["binary_a"] != merged["binary_b"]]
print(f"\nDisagreements: {len(disagree)} articles ({len(disagree)/total*100:.1f}%)\n")

combos = disagree.groupby(["PredictedVerdict_a", "PredictedVerdict_b"]).size().reset_index(name="count")
combos = combos.sort_values("count", ascending=False)
print("  Pattern                              Count")
print("  " + "-"*42)
for _, row in combos.iterrows():
    print(f"  {row['PredictedVerdict_a']:<18} → {row['PredictedVerdict_b']:<18} {row['count']}")

# ── Score correlation ─────────────────────────────────────────────────────────
score_corr = merged[["Score_a", "Score_b"]].corr().iloc[0, 1]
print(f"\nScore correlation (Pearson r): {score_corr:.3f}")
score_delta = (merged["Score_a"] - merged["Score_b"]).abs()
print(f"Mean absolute score difference: {score_delta.mean():.1f} points")
print(f"Median absolute score difference: {score_delta.median():.1f} points")

# ── Per-class accuracy comparison ─────────────────────────────────────────────
print("\nAccuracy by true label:")
for label in ["true", "fake"]:
    sub = merged[merged["TrueLabel"] == label]
    if len(sub) == 0:
        continue
    correct_map = {"true": 1, "fake": 0}
    expected = correct_map[label]
    acc_a = (sub["binary_a"] == expected).mean()
    acc_b = (sub["binary_b"] == expected).mean()
    print(f"  {label.upper():<6}  A={acc_a*100:.1f}%  B={acc_b*100:.1f}%  (n={len(sub)})")

# ── Save ──────────────────────────────────────────────────────────────────────
summary = {
    "model_a": label_a, "model_b": label_b,
    "articles_compared": total,
    "agreement_rate": round(agree_rate, 4),
    "cohens_kappa": round(kappa, 4) if kappa else None,
    "score_correlation": round(score_corr, 4),
    "mean_score_delta": round(float(score_delta.mean()), 2),
}
with open("model_agreement.json", "w", encoding="utf-8") as f:
    json.dump(summary, f, indent=2)
print("\nSaved to model_agreement.json")
