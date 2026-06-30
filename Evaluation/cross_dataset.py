"""
Cross-dataset generalization test.

Tests the ISOT-trained TF-IDF baseline on LIAR statements (no retraining),
then compares against LLM performance on the same dataset.

This is the key thesis finding:
  TF-IDF: 98.7% F1 on ISOT  →  ~50-60% on LIAR  (overfit to style)
  LLM:    ~55-70% F1 on ISOT →  similar on LIAR   (reasons about claims)

Requirements:
    pip install pandas scikit-learn matplotlib
    Run baseline.py first (creates baseline_model.pkl)
    Run liar_prep.py first (creates data/liar_true.csv + liar_fake.csv)
    Run eval harness on LIAR (creates metrics_groq_liar.json)

Usage:
    python cross_dataset.py
"""

import json
import os
import pickle
import sys
import pandas as pd
import numpy as np
from sklearn.metrics import accuracy_score, precision_score, recall_score, f1_score, confusion_matrix

try:
    import matplotlib.pyplot as plt
    import matplotlib.patches as mpatches
    HAS_PLOT = True
except ImportError:
    HAS_PLOT = False


# ── Load model ────────────────────────────────────────────────────────────────
MODEL_PATH = "baseline_model.pkl"
if not os.path.exists(MODEL_PATH):
    print("ERROR: baseline_model.pkl not found.")
    print("Run:  python baseline.py  first to train and save the model.")
    sys.exit(1)

with open(MODEL_PATH, "rb") as f:
    model = pickle.load(f)
vec = model["vectorizer"]
clf = model["classifier"]
print(f"Loaded ISOT-trained TF-IDF model from {MODEL_PATH}")


# ── Load LIAR data ────────────────────────────────────────────────────────────
LIAR_TRUE = "data/liar_true.csv"
LIAR_FAKE = "data/liar_fake.csv"

if not os.path.exists(LIAR_TRUE) or not os.path.exists(LIAR_FAKE):
    print("ERROR: LIAR data not found.")
    print("Run:  python liar_prep.py  first.")
    sys.exit(1)

true_df = pd.read_csv(LIAR_TRUE)
fake_df = pd.read_csv(LIAR_FAKE)
true_df["label"] = 1
fake_df["label"] = 0

liar_df = pd.concat([true_df, fake_df]).sample(frac=1, random_state=42).reset_index(drop=True)
liar_df["content"] = liar_df["title"].fillna("") + " " + liar_df["text"].fillna("")

print(f"LIAR test set: {len(liar_df)} statements ({true_df['label'].sum()} true, {len(fake_df)} fake)")


# ── Run ISOT model on LIAR (zero transfer — no retraining) ───────────────────
X_liar = vec.transform(liar_df["content"])
y_true = liar_df["label"].values
y_pred = clf.predict(X_liar)
y_prob = clf.predict_proba(X_liar)[:, 1]

liar_acc  = accuracy_score(y_true, y_pred)
liar_prec = precision_score(y_true, y_pred, zero_division=0)
liar_rec  = recall_score(y_true, y_pred, zero_division=0)
liar_f1   = f1_score(y_true, y_pred, zero_division=0)
liar_cm   = confusion_matrix(y_true, y_pred)


# ── Load baseline ISOT metrics (from baseline.py run) ────────────────────────
isot_metrics = {}
if os.path.exists("metrics_baseline.json"):
    # utf-8-sig: PowerShell writes metrics JSON with a UTF-8 BOM
    with open("metrics_baseline.json", encoding="utf-8-sig") as f:
        isot_metrics = json.load(f)


# ── Load LLM metrics on both datasets ────────────────────────────────────────
def load_llm_metrics(path):
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)

llm_isot = load_llm_metrics("metrics_groq_fewshot.json") or \
           load_llm_metrics("metrics_groq.json")
llm_liar = load_llm_metrics("metrics_groq_liar.json")


# ── Print comparison table ────────────────────────────────────────────────────
def pct(v):
    return f"{v*100:.1f}%" if v is not None else "N/A"

print()
print("╔══════════════════════════════════════════════════════════════════╗")
print("║          CROSS-DATASET GENERALIZATION RESULTS                   ║")
print("╠══════════════════════════════════════════════════════════════════╣")
print("║                        ISOT (in-domain)    LIAR (out-of-domain) ║")
print("╠══════════════════════════════════════════════════════════════════╣")

tfidf_isot_f1  = isot_metrics.get("f1")
tfidf_isot_acc = isot_metrics.get("accuracy")

print(f"║  TF-IDF + LogReg                                               ║")
print(f"║    Accuracy    {pct(tfidf_isot_acc):>10}             {pct(liar_acc):>10}           ║")
print(f"║    F1          {pct(tfidf_isot_f1):>10}             {pct(liar_f1):>10}           ║")
print(f"║    Precision   {pct(isot_metrics.get('precision')):>10}             {pct(liar_prec):>10}           ║")
print(f"║    Recall      {pct(isot_metrics.get('recall')):>10}             {pct(liar_rec):>10}           ║")
print("╠══════════════════════════════════════════════════════════════════╣")

llm_isot_f1  = llm_isot["f1"] if llm_isot else None
llm_isot_acc = llm_isot["accuracy"] if llm_isot else None
llm_liar_f1  = llm_liar["f1"] if llm_liar else None
llm_liar_acc = llm_liar["accuracy"] if llm_liar else None

print(f"║  LLM (Groq)                                                    ║")
print(f"║    Accuracy    {pct(llm_isot_acc):>10}             {pct(llm_liar_acc):>10}           ║")
print(f"║    F1          {pct(llm_isot_f1):>10}             {pct(llm_liar_f1):>10}           ║")
print("╚══════════════════════════════════════════════════════════════════╝")
print()

# Key finding summary
if tfidf_isot_f1 and liar_f1:
    drop = (tfidf_isot_f1 - liar_f1) * 100
    print(f"KEY FINDING: TF-IDF F1 drops {drop:.1f} percentage points out-of-domain.")
    if llm_isot_f1 and llm_liar_f1:
        llm_drop = (llm_isot_f1 - llm_liar_f1) * 100
        print(f"             LLM F1 drops    {llm_drop:.1f} percentage points out-of-domain.")
        if drop > llm_drop:
            print(f"  → TF-IDF overfits to ISOT style; LLM generalizes better ({drop-llm_drop:.1f}pp gap).")
        else:
            print(f"  → Both models struggle on LIAR — short political claims are harder than news articles.")
print()

# ── Plot: side-by-side bars ───────────────────────────────────────────────────
if HAS_PLOT:
    fig, axes = plt.subplots(1, 2, figsize=(11, 5), sharey=True)
    fig.suptitle("Cross-Dataset Generalization: ISOT (in-domain) vs LIAR (out-of-domain)",
                 fontsize=13, fontweight="bold")

    metrics_list = ["Accuracy", "Precision", "Recall", "F1"]
    x = np.arange(len(metrics_list))
    width = 0.35

    def vals_baseline(dataset):
        if dataset == "isot":
            return [isot_metrics.get(k.lower(), 0) for k in metrics_list]
        else:
            return [liar_acc, liar_prec, liar_rec, liar_f1]

    def vals_llm(dataset):
        src = llm_isot if dataset == "isot" else llm_liar
        if not src:
            return [0, 0, 0, 0]
        return [src.get("accuracy", 0), src.get("precision", 0),
                src.get("recall", 0), src.get("f1", 0)]

    colors = {"tfidf": "#4f8ef7", "llm": "#e06c3a"}

    for ax, dataset, title in [(axes[0], "isot", "ISOT (in-domain — trained here)"),
                                (axes[1], "liar", "LIAR (out-of-domain — never seen)")]:
        b1 = ax.bar(x - width/2, vals_baseline(dataset), width,
                    label="TF-IDF + LogReg", color=colors["tfidf"], alpha=0.85)
        b2 = ax.bar(x + width/2, vals_llm(dataset), width,
                    label="LLM (Groq)", color=colors["llm"], alpha=0.85)

        ax.set_title(title, fontsize=11)
        ax.set_xticks(x)
        ax.set_xticklabels(metrics_list)
        ax.set_ylim(0, 1.1)
        ax.set_ylabel("Score")
        ax.yaxis.set_major_formatter(plt.FuncFormatter(lambda v, _: f"{v*100:.0f}%"))
        ax.axhline(0.5, color="gray", linestyle="--", linewidth=0.8, alpha=0.5, label="Random")
        ax.grid(axis="y", alpha=0.3)

        for bar in list(b1) + list(b2):
            h = bar.get_height()
            if h > 0.02:
                ax.annotate(f"{h*100:.0f}%",
                            xy=(bar.get_x() + bar.get_width() / 2, h),
                            xytext=(0, 3), textcoords="offset points",
                            ha="center", fontsize=8)

    axes[0].legend(loc="lower right", fontsize=9)
    plt.tight_layout()
    out = "cross_dataset.png"
    plt.savefig(out, dpi=150)
    print(f"Chart saved to {out}")
    plt.show()

# Save summary
summary = {
    "tfidf_isot_f1":  round(tfidf_isot_f1,  4) if tfidf_isot_f1  else None,
    "tfidf_isot_acc": round(tfidf_isot_acc, 4) if tfidf_isot_acc else None,
    "tfidf_liar_f1":  round(liar_f1,  4),
    "tfidf_liar_acc": round(liar_acc, 4),
    "llm_isot_f1":    round(llm_isot_f1,  4) if llm_isot_f1  else None,
    "llm_isot_acc":   round(llm_isot_acc, 4) if llm_isot_acc else None,
    "llm_liar_f1":    round(llm_liar_f1,  4) if llm_liar_f1  else None,
    "llm_liar_acc":   round(llm_liar_acc, 4) if llm_liar_acc else None,
}
with open("cross_dataset_summary.json", "w") as f:
    json.dump(summary, f, indent=2)
print("Summary saved to cross_dataset_summary.json")
