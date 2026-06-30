"""
Classical ML baseline for ISOT fake news detection.
Compares TF-IDF + Logistic Regression against the LLM approach.

Requirements:
    pip install pandas scikit-learn matplotlib seaborn

Usage:
    python baseline.py
    python baseline.py --true data/True.csv --fake data/Fake.csv --max 5000
"""

import argparse
import json
import sys
import time
import pickle
import pandas as pd
import numpy as np

# Windows consoles default to cp1252 and choke on the box-drawing banners below.
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import train_test_split
from sklearn.metrics import (
    classification_report, confusion_matrix,
    accuracy_score, precision_score, recall_score, f1_score
)

try:
    import matplotlib.pyplot as plt
    import seaborn as sns
    HAS_PLOT = True
except ImportError:
    HAS_PLOT = False


def load_data(true_path: str, fake_path: str, max_per_class: int) -> pd.DataFrame:
    true_df = pd.read_csv(true_path)
    fake_df = pd.read_csv(fake_path)

    true_df.columns = true_df.columns.str.lower()
    fake_df.columns = fake_df.columns.str.lower()

    true_df["label"] = 1
    fake_df["label"] = 0

    true_df = true_df.dropna(subset=["text"]).query("text.str.len() > 80")
    fake_df = fake_df.dropna(subset=["text"]).query("text.str.len() > 80")

    true_sample = true_df.sample(min(max_per_class, len(true_df)), random_state=42)
    fake_sample = fake_df.sample(min(max_per_class, len(fake_df)), random_state=42)

    df = pd.concat([true_sample, fake_sample]).sample(frac=1, random_state=42).reset_index(drop=True)
    df["content"] = df["title"].fillna("") + " " + df["text"].fillna("")
    return df


def run(args):
    print()
    print("╔══════════════════════════════════════════════╗")
    print("║    Classical ML Baseline — TF-IDF + LogReg   ║")
    print("╚══════════════════════════════════════════════╝")
    print()

    print(f"  Loading dataset (max {args.max} per class)…")
    df = load_data(args.true_file, args.fake_file, args.max)
    print(f"  Loaded {len(df)} articles ({df['label'].sum()} true, {(df['label']==0).sum()} fake)")
    print()

    X = df["content"]
    y = df["label"]

    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.2, random_state=42, stratify=y
    )
    print(f"  Train: {len(X_train)}  |  Test: {len(X_test)}")
    print()

    print("  Fitting TF-IDF vectorizer…")
    t0 = time.time()
    vec = TfidfVectorizer(max_features=50000, ngram_range=(1, 2), stop_words="english", sublinear_tf=True)
    X_train_tf = vec.fit_transform(X_train)
    X_test_tf  = vec.transform(X_test)

    print("  Training Logistic Regression…")
    clf = LogisticRegression(max_iter=1000, C=1.0, random_state=42)
    clf.fit(X_train_tf, y_train)
    train_time = time.time() - t0
    print(f"  Training done in {train_time:.1f}s")
    print()

    t1 = time.time()
    y_pred = clf.predict(X_test_tf)
    infer_time = (time.time() - t1) / len(X_test) * 1000

    acc  = accuracy_score(y_test, y_pred)
    prec = precision_score(y_test, y_pred)
    rec  = recall_score(y_test, y_pred)
    f1   = f1_score(y_test, y_pred)
    cm   = confusion_matrix(y_test, y_pred)

    print("╔══════════════════════════════════════════════╗")
    print("║              BASELINE RESULTS                ║")
    print("╠══════════════════════════════════════════════╣")
    print(f"║  Model               : TF-IDF + LogReg       ║")
    print(f"║  Features            : 50k TF-IDF bigrams     ║")
    print(f"║  Train size          : {len(X_train):<5}                 ║")
    print(f"║  Test size           : {len(X_test):<5}                 ║")
    print("╠══════════════════════════════════════════════╣")
    print(f"║  Accuracy            : {acc*100:.1f}%                ║")
    print(f"║  Precision           : {prec*100:.1f}%                ║")
    print(f"║  Recall              : {rec*100:.1f}%                ║")
    print(f"║  F1 Score            : {f1*100:.1f}%                ║")
    print(f"║  Mean infer latency  : {infer_time:.2f}ms/article         ║")
    print("╠══════════════════════════════════════════════╣")
    print("║               CONFUSION MATRIX               ║")
    print("║                                              ║")
    print("║                    Pred TRUE   Pred FAKE     ║")
    print(f"║  Actual TRUE            {cm[1,1]:>5}      {cm[1,0]:>5}     ║")
    print(f"║  Actual FAKE            {cm[0,1]:>5}      {cm[0,0]:>5}     ║")
    print("╚══════════════════════════════════════════════╝")
    print()

    # Save JSON metrics (same format as EvaluationRunner for easy comparison)
    metrics = {
        "backendLabel": "TF-IDF + LogReg (baseline)",
        "dataset": "ISOT",
        "accuracy": round(acc, 4),
        "precision": round(prec, 4),
        "recall": round(rec, 4),
        "f1": round(f1, 4),
        "specificity": round(cm[0,0] / (cm[0,0] + cm[0,1]), 4) if (cm[0,0]+cm[0,1]) > 0 else 0,
        "coverage": 1.0,
        "meanLatencyMs": round(infer_time, 2),
        "total": len(X_test),
        "uncertain": 0,
        "errors": 0,
        "trainSize": len(X_train),
        "note": "Classical supervised baseline. Trained on ISOT train split."
    }

    out_path = "metrics_baseline.json"
    with open(out_path, "w") as f:
        json.dump(metrics, f, indent=2)
    print(f"  Metrics saved to: {out_path}")

    # Save trained model for cross-dataset testing
    model_path = "baseline_model.pkl"
    with open(model_path, "wb") as f:
        pickle.dump({"vectorizer": vec, "classifier": clf}, f)
    print(f"  Model saved to:   {model_path}  (use cross_dataset.py to test on LIAR)")

    # Optional confusion matrix plot
    if HAS_PLOT:
        fig, ax = plt.subplots(figsize=(5, 4))
        sns.heatmap(cm, annot=True, fmt="d", cmap="Blues",
                    xticklabels=["Pred FAKE", "Pred TRUE"],
                    yticklabels=["Actual FAKE", "Actual TRUE"], ax=ax)
        ax.set_title("TF-IDF + LogReg — Confusion Matrix")
        plt.tight_layout()
        plot_path = "baseline_confusion_matrix.png"
        plt.savefig(plot_path, dpi=150)
        print(f"  Confusion matrix plot saved to: {plot_path}")

    print()
    print("  NOTE: ISOT is a stylistically clean dataset — LLM vs TF-IDF comparison")
    print("  is most meaningful for cross-domain or adversarial samples.")
    print()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--true",  dest="true_file", default="data/True.csv")
    parser.add_argument("--fake",  dest="fake_file", default="data/Fake.csv")
    parser.add_argument("--max",   dest="max", type=int, default=5000,
                        help="Max articles per class (default 5000)")
    run(parser.parse_args())
