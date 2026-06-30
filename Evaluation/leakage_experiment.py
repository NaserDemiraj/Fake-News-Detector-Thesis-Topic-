"""
Leakage experiment: why does TF-IDF score 98.7% on ISOT but ~50% on LIAR?

ISOT's "true" articles are all Reuters wire copy and start with a dateline like
"WASHINGTON (Reuters) - ...". A bag-of-words model can shortcut on the literal
token "reuters" (and Reuters' house style) instead of learning anything about
truthfulness. This script proves it by training two models on the SAME data:

  1. LEAKY     — raw text (reproduces the inflated ~98.7%)
  2. DE-LEAKED — same text with the Reuters dateline/token and URLs stripped

…then tests BOTH on the ISOT held-out split (in-domain) and on LIAR
(out-of-domain). If de-leaking lowers ISOT but the model still can't do LIAR,
that confirms the leak is pervasive (stylistic, not one token) — which is the
core argument for using an LLM that reasons about claims instead.

Requirements:  pip install pandas scikit-learn matplotlib
Prereq:        run liar_prep.py first (creates data/liar_*.csv)

Usage:         python leakage_experiment.py
"""

import argparse
import json
import re
import sys
import pickle

import pandas as pd
import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import train_test_split
from sklearn.metrics import accuracy_score, precision_score, recall_score, f1_score, confusion_matrix

try:
    import matplotlib.pyplot as plt
    HAS_PLOT = True
except ImportError:
    HAS_PLOT = False


# ── De-leaking: strip the documented ISOT source giveaways ────────────────────
DATELINE_RE = re.compile(r'^[A-Z][A-Za-z\s,\.\-/]{0,60}\(Reuters\)\s*[-–]\s*')
REUTERS_RE  = re.compile(r'\(?\breuters\b\)?', re.IGNORECASE)
URL_RE      = re.compile(r'http\S+|www\.\S+')
# Common fake-side source tags (21st Century Wire, "Featured image via", etc.)
FAKE_TAGS_RE = re.compile(
    r'featured image via.*$|21st century wire|getty images|via\s+\w+\s+/\s+\w+',
    re.IGNORECASE)


def deleak(text: str) -> str:
    t = DATELINE_RE.sub('', text)
    t = REUTERS_RE.sub(' ', t)
    t = URL_RE.sub(' ', t)
    t = FAKE_TAGS_RE.sub(' ', t)
    t = re.sub(r'\s+', ' ', t).strip()
    return t


def load_isot(true_path, fake_path, max_per_class):
    true_df = pd.read_csv(true_path); true_df.columns = true_df.columns.str.lower()
    fake_df = pd.read_csv(fake_path); fake_df.columns = fake_df.columns.str.lower()
    true_df["label"] = 1
    fake_df["label"] = 0
    true_df = true_df.dropna(subset=["text"]).query("text.str.len() > 80")
    fake_df = fake_df.dropna(subset=["text"]).query("text.str.len() > 80")
    true_s = true_df.sample(min(max_per_class, len(true_df)), random_state=42)
    fake_s = fake_df.sample(min(max_per_class, len(fake_df)), random_state=42)
    df = pd.concat([true_s, fake_s]).sample(frac=1, random_state=42).reset_index(drop=True)
    df["content"] = df["title"].fillna("") + " " + df["text"].fillna("")
    return df


def load_liar(true_path, fake_path):
    t = pd.read_csv(true_path); t["label"] = 1
    f = pd.read_csv(fake_path); f["label"] = 0
    df = pd.concat([t, f]).sample(frac=1, random_state=42).reset_index(drop=True)
    df["content"] = df["title"].fillna("") + " " + df["text"].fillna("")
    return df


def evaluate(clf, vec, X_text, y):
    X = vec.transform(X_text)
    p = clf.predict(X)
    return {
        "accuracy":  accuracy_score(y, p),
        "precision": precision_score(y, p, zero_division=0),
        "recall":    recall_score(y, p, zero_division=0),
        "f1":        f1_score(y, p, zero_division=0),
        "cm":        confusion_matrix(y, p),
    }


def train_variant(name, isot_df, liar_df, clean):
    content = isot_df["content"].map(deleak) if clean else isot_df["content"]
    y = isot_df["label"]
    Xtr, Xte, ytr, yte = train_test_split(content, y, test_size=0.2, random_state=42, stratify=y)

    vec = TfidfVectorizer(max_features=50000, ngram_range=(1, 2), stop_words="english", sublinear_tf=True)
    Xtr_tf = vec.fit_transform(Xtr)
    clf = LogisticRegression(max_iter=1000, C=1.0, random_state=42)
    clf.fit(Xtr_tf, ytr)

    isot_res = evaluate(clf, vec, Xte, yte)

    liar_content = liar_df["content"].map(deleak) if clean else liar_df["content"]
    liar_res = evaluate(clf, vec, liar_content, liar_df["label"])

    return {"name": name, "clean": clean, "vec": vec, "clf": clf,
            "isot": isot_res, "liar": liar_res}


def pct(v): return f"{v*100:.1f}%"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--true", dest="true_file", default="data/True.csv")
    ap.add_argument("--fake", dest="fake_file", default="data/Fake.csv")
    ap.add_argument("--max", type=int, default=5000)
    args = ap.parse_args()

    print("\n╔══════════════════════════════════════════════════╗")
    print("║   ISOT Leakage Experiment — TF-IDF generalization ║")
    print("╚══════════════════════════════════════════════════╝\n")

    isot_df = load_isot(args.true_file, args.fake_file, args.max)
    print(f"  ISOT loaded: {len(isot_df)} articles ({int(isot_df['label'].sum())} true / {int((isot_df['label']==0).sum())} fake)")

    import os
    if not (os.path.exists("data/liar_true.csv") and os.path.exists("data/liar_fake.csv")):
        sys.exit("ERROR: LIAR data missing. Run:  python liar_prep.py --balance  first.")
    liar_df = load_liar("data/liar_true.csv", "data/liar_fake.csv")
    print(f"  LIAR loaded: {len(liar_df)} statements\n")

    print("  Training LEAKY baseline (raw text)…")
    leaky = train_variant("Leaky (raw)", isot_df, liar_df, clean=False)
    print("  Training DE-LEAKED baseline (Reuters/dateline/URL stripped)…")
    clean = train_variant("De-leaked", isot_df, liar_df, clean=True)
    print()

    # ── Comparison table ──────────────────────────────────────────────────────
    print("╔════════════════════════════════════════════════════════════════╗")
    print("║                ISOT (in-domain)     LIAR (out-of-domain)        ║")
    print("╠════════════════════════════════════════════════════════════════╣")
    for v in (leaky, clean):
        print(f"║  {v['name']:<16}                                              ║")
        print(f"║     Accuracy      {pct(v['isot']['accuracy']):>8}            {pct(v['liar']['accuracy']):>8}              ║")
        print(f"║     F1            {pct(v['isot']['f1']):>8}            {pct(v['liar']['f1']):>8}              ║")
        print("╠════════════════════════════════════════════════════════════════╣")
    print("╚════════════════════════════════════════════════════════════════╝\n")

    isot_drop_leaky = (leaky['isot']['f1'] - leaky['liar']['f1']) * 100
    isot_drop_clean = (clean['isot']['f1'] - clean['liar']['f1']) * 100
    print(f"  Leaky    : ISOT {pct(leaky['isot']['f1'])} → LIAR {pct(leaky['liar']['f1'])}  (drop {isot_drop_leaky:.1f} pp)")
    print(f"  De-leaked: ISOT {pct(clean['isot']['f1'])} → LIAR {pct(clean['liar']['f1'])}  (drop {isot_drop_clean:.1f} pp)")
    print()
    liar_gain = (clean['liar']['f1'] - leaky['liar']['f1']) * 100
    isot_cost = (leaky['isot']['f1'] - clean['isot']['f1']) * 100
    print(f"  → Removing the leak cost {isot_cost:.1f} pp on ISOT and "
          f"{'gained' if liar_gain>=0 else 'lost'} {abs(liar_gain):.1f} pp on LIAR.")
    if clean['liar']['f1'] < 0.6:
        print("  → Even de-leaked, the baseline still can't do LIAR: the leak is")
        print("    pervasive (whole writing style), not one token. This is the core")
        print("    argument for an LLM that reasons about claims, not surface style.")
    print()

    # ── Save de-leaked model + metrics ────────────────────────────────────────
    with open("baseline_model_deleaked.pkl", "wb") as f:
        pickle.dump({"vectorizer": clean["vec"], "classifier": clean["clf"]}, f)

    def jsonable(res):
        return {k: round(float(v), 4) for k, v in res.items() if k != "cm"}
    summary = {
        "leaky":    {"isot": jsonable(leaky['isot']), "liar": jsonable(leaky['liar'])},
        "deleaked": {"isot": jsonable(clean['isot']), "liar": jsonable(clean['liar'])},
    }
    with open("leakage_experiment.json", "w") as f:
        json.dump(summary, f, indent=2)
    print("  Saved: baseline_model_deleaked.pkl, leakage_experiment.json")

    # ── Chart ─────────────────────────────────────────────────────────────────
    if HAS_PLOT:
        labels = ["Leaky\nISOT", "Leaky\nLIAR", "De-leaked\nISOT", "De-leaked\nLIAR"]
        f1s = [leaky['isot']['f1'], leaky['liar']['f1'], clean['isot']['f1'], clean['liar']['f1']]
        colors = ["#4f8ef7", "#9ec3fb", "#e06c3a", "#f0b08e"]
        fig, ax = plt.subplots(figsize=(8, 5))
        bars = ax.bar(labels, f1s, color=colors)
        ax.axhline(0.5, color="gray", ls="--", lw=0.8, label="Random (50%)")
        ax.set_ylim(0, 1.05)
        ax.set_ylabel("F1 Score")
        ax.set_title("TF-IDF generalization: leaky vs de-leaked, ISOT vs LIAR")
        ax.yaxis.set_major_formatter(plt.FuncFormatter(lambda v, _: f"{v*100:.0f}%"))
        for b, v in zip(bars, f1s):
            ax.annotate(f"{v*100:.1f}%", (b.get_x()+b.get_width()/2, v),
                        xytext=(0, 3), textcoords="offset points", ha="center", fontsize=9)
        ax.legend()
        plt.tight_layout()
        plt.savefig("leakage_experiment.png", dpi=150)
        print("  Chart: leakage_experiment.png")
    print()


if __name__ == "__main__":
    main()
