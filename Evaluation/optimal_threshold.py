"""
Find the verdict threshold that maximizes BALANCED accuracy on labeled eval runs.

The LLM clusters scores high, so the default fake<=40 / true>=70 band leaves a
lot of fakes scored 80 misclassified. This sweeps the cutoff T (score >= T => true)
and reports the T with the best balanced accuracy = mean(recall_true, recall_fake).
That value goes into appsettings VerdictThresholds (set FakeMax == TrueMin == T for
a pure binary decision, or keep a small uncertain band around T).

NOTE: derive this from runs produced by the CURRENT prompt. After the grounding
change, re-run the eval and re-run this script — the score distribution shifts.

Usage:
    python optimal_threshold.py                     # all results_*.csv
    python optimal_threshold.py results_model_70b.csv
"""
import csv
import glob
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

files = sys.argv[1:] or sorted(glob.glob("results_*.csv"))
rows = []
for path in files:
    try:
        with open(path, encoding="utf-8-sig") as f:
            r = csv.DictReader(f)
            if not r.fieldnames or "TrueLabel" not in r.fieldnames or "Score" not in r.fieldnames:
                continue
            for row in r:
                if (row.get("IsError", "").strip().lower() == "true"):
                    continue
                label = (row.get("TrueLabel") or "").strip().lower()
                if label not in ("true", "fake"):
                    continue
                try:
                    score = float(row["Score"])
                except (ValueError, TypeError):
                    continue
                rows.append((label, score))
    except FileNotFoundError:
        print(f"  (skip missing {path})")

if not rows:
    sys.exit("No usable labeled rows found.")

n_true = sum(1 for l, _ in rows if l == "true")
n_fake = len(rows) - n_true
print(f"\n  Loaded {len(rows)} labeled predictions from {len(files)} file(s): {n_true} true / {n_fake} fake")
print(f"  Sweeping cutoff T (score >= T -> likely_true)…\n")

best = None
print(f"  {'T':>4}  {'bal_acc':>8}  {'recall_true':>11}  {'recall_fake':>11}")
for T in range(0, 101, 5):
    tp = sum(1 for l, s in rows if l == "true" and s >= T)
    fn = n_true - tp
    tn = sum(1 for l, s in rows if l == "fake" and s < T)
    fp = n_fake - tn
    rec_true = tp / n_true if n_true else 0
    rec_fake = tn / n_fake if n_fake else 0
    bal = (rec_true + rec_fake) / 2
    flag = ""
    if best is None or bal > best[1]:
        best = (T, bal, rec_true, rec_fake)
        flag = "  <= best so far"
    print(f"  {T:>4}  {bal*100:>7.1f}%  {rec_true*100:>10.1f}%  {rec_fake*100:>10.1f}%{flag}")

T, bal, rt, rf = best
print(f"\n  OPTIMAL cutoff T = {T}")
print(f"    balanced accuracy = {bal*100:.1f}%   (recall_true {rt*100:.1f}% / recall_fake {rf*100:.1f}%)")
print(f"\n  Current config: fake<=40 / true>=70 (scores 40-70 = uncertain)")
print(f"  Suggested appsettings:")
print(f'    "VerdictThresholds": {{ "FakeMax": {T}, "TrueMin": {T} }}   (binary at T)')
print(f"  Or keep a small uncertain band, e.g. FakeMax {max(T-10,0)} / TrueMin {min(T+10,100)}.\n")
