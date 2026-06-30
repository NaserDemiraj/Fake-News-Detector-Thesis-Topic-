"""
Error analysis: reads results CSV(s), categorises misclassifications,
and prints a thesis-ready summary with representative examples.

Usage:
    python error_analysis.py                          # uses most recent results_*.csv
    python error_analysis.py results_20260628_141141.csv
"""

import sys
import glob
import os
import json
import pandas as pd

# Windows consoles default to cp1252 and choke on the Unicode arrows/banners below.
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

# ── Find CSV ──────────────────────────────────────────────────────────────────
if len(sys.argv) > 1:
    csv_path = sys.argv[1]
else:
    csvs = sorted(glob.glob("results_*.csv"))
    if not csvs:
        print("No results_*.csv found. Run the evaluation harness first.")
        sys.exit(1)
    csv_path = csvs[-1]

print(f"Analysing: {csv_path}\n")
df = pd.read_csv(csv_path)

# ── Exclude harness errors (mock fallbacks, timeouts) ─────────────────────────
errors = df[df["IsError"] == True]
df = df[df["IsError"] == False].copy()

total   = len(df)
correct = int(df["Correct"].sum())
wrong   = df[~df["Correct"]]

print(f"{'='*60}")
print(f"  OVERVIEW")
print(f"{'='*60}")
print(f"  Total valid predictions : {total}")
print(f"  Harness errors skipped  : {len(errors)}")
print(f"  Correct                 : {correct}  ({correct/total*100:.1f}%)")
print(f"  Wrong                   : {len(wrong)}  ({len(wrong)/total*100:.1f}%)")

# ── Failure mode breakdown ────────────────────────────────────────────────────
fn  = wrong[(wrong["TrueLabel"] == "fake") & (wrong["PredictedVerdict"] == "likely_true")]
fp  = wrong[(wrong["TrueLabel"] == "true") & (wrong["PredictedVerdict"] == "likely_fake")]
fn_u = wrong[(wrong["TrueLabel"] == "fake") & (wrong["PredictedVerdict"] == "uncertain")]
fp_u = wrong[(wrong["TrueLabel"] == "true") & (wrong["PredictedVerdict"] == "uncertain")]

print(f"\n{'='*60}")
print(f"  FAILURE MODES")
print(f"{'='*60}")
print(f"  False negatives  — fake → predicted likely_true   : {len(fn)}")
print(f"  False positives  — real → predicted likely_fake   : {len(fp)}")
print(f"  Fake → uncertain (misclassified as uncertain)      : {len(fn_u)}")
print(f"  Real → uncertain (misclassified as uncertain)      : {len(fp_u)}")

# ── Score distributions ───────────────────────────────────────────────────────
print(f"\n{'='*60}")
print(f"  SCORE DISTRIBUTIONS  (higher = model thinks more credible)")
print(f"{'='*60}")

fake_df = df[df["TrueLabel"] == "fake"]
true_df = df[df["TrueLabel"] == "true"]

print(f"  All FAKE articles   score: mean={fake_df['Score'].mean():.1f}  "
      f"median={fake_df['Score'].median():.0f}  std={fake_df['Score'].std():.1f}")
print(f"  All REAL articles   score: mean={true_df['Score'].mean():.1f}  "
      f"median={true_df['Score'].median():.0f}  std={true_df['Score'].std():.1f}")
print(f"  Wrong predictions   score: mean={wrong['Score'].mean():.1f}  "
      f"median={wrong['Score'].median():.0f}")
print(f"  Correct predictions score: mean={df[df['Correct']]['Score'].mean():.1f}  "
      f"median={df[df['Correct']]['Score'].median():.0f}")

# ── Threshold analysis: what cutoff maximises accuracy? ──────────────────────
print(f"\n{'='*60}")
print(f"  THRESHOLD ANALYSIS  (score > T = likely_true)")
print(f"{'='*60}")
best_acc, best_t = 0, 50
for t in range(10, 95, 5):
    preds = df["Score"].apply(lambda s: "likely_true" if s > t else "likely_fake")
    acc = (preds == df["TrueLabel"].map({"true": "likely_true", "fake": "likely_fake"})).mean()
    if acc > best_acc:
        best_acc, best_t = acc, t

for t in range(max(10, best_t - 15), min(95, best_t + 20), 5):
    preds = df["Score"].apply(lambda s: "likely_true" if s > t else "likely_fake")
    mapped = df["TrueLabel"].map({"true": "likely_true", "fake": "likely_fake"})
    acc = (preds == mapped).mean()
    marker = " ◄ best" if t == best_t else ""
    print(f"  T={t:3d}  accuracy={acc*100:.1f}%{marker}")

print(f"\n  Optimal threshold: score > {best_t}  →  accuracy={best_acc*100:.1f}%")
print(f"  (Current model uses LLM verdict; this shows what score-only would achieve)")

# ── Representative examples ───────────────────────────────────────────────────
def show_examples(subset, label, n=5):
    print(f"\n{'='*60}")
    print(f"  {label}  ({len(subset)} total, showing {min(n, len(subset))})")
    print(f"{'='*60}")
    for _, row in subset.head(n).iterrows():
        title = str(row["TitlePreview"])[:90]
        print(f"  Score {row['Score']:>3.0f} | conf {row['Confidence']:.2f} | {title}")

show_examples(fn,  "FALSE NEGATIVES — fake articles the model rated as credible")
show_examples(fp,  "FALSE POSITIVES — real articles the model flagged as fake")
show_examples(fn_u,"FAKE → UNCERTAIN — model hedged instead of calling fake")

# ── Save JSON summary ────────────────────────────────────────────────────────
summary = {
    "source_csv": os.path.basename(csv_path),
    "total": total,
    "errors_skipped": len(errors),
    "correct": correct,
    "accuracy": round(correct / total, 4),
    "false_negatives": len(fn),
    "false_positives": len(fp),
    "fake_uncertain": len(fn_u),
    "true_uncertain": len(fp_u),
    "optimal_score_threshold": best_t,
    "optimal_threshold_accuracy": round(best_acc, 4),
    "score_mean_fake": round(float(fake_df["Score"].mean()), 1),
    "score_mean_true": round(float(true_df["Score"].mean()), 1),
    "false_negative_examples": fn["TitlePreview"].head(10).tolist(),
    "false_positive_examples": fp["TitlePreview"].head(10).tolist(),
}
out = "error_analysis.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(summary, f, indent=2)
print(f"\nSummary saved to {out}")
