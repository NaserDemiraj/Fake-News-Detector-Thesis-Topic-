"""
Downloads the LIAR dataset and converts it to the True.csv/Fake.csv format
used by the evaluation harness and cross_dataset.py.

Requirements:
    pip install pandas requests

Usage:
    python liar_prep.py

Output:
    data/liar_true.csv   — statements rated "true" or "mostly-true"
    data/liar_fake.csv   — statements rated "false" or "pants-fire"
"""

import os
import zipfile
import requests
import pandas as pd

DATA_DIR = "data"
ZIP_URL  = "https://www.cs.ucsb.edu/~william/data/liar_dataset.zip"
ZIP_PATH = os.path.join(DATA_DIR, "liar_dataset.zip")

COLUMNS = [
    "id", "label", "statement", "subject", "speaker",
    "speaker_job", "state", "party",
    "barely_true_count", "false_count", "half_true_count",
    "mostly_true_count", "pants_fire_count", "venue"
]

# Binary mapping: only keep unambiguous labels
TRUE_LABELS = {"true", "mostly-true"}
FAKE_LABELS = {"false", "pants-fire"}


def download():
    os.makedirs(DATA_DIR, exist_ok=True)
    if os.path.exists(ZIP_PATH):
        print(f"  Already downloaded: {ZIP_PATH}")
        return
    print(f"  Downloading LIAR dataset from UCSB...")
    r = requests.get(ZIP_URL, timeout=30)
    r.raise_for_status()
    with open(ZIP_PATH, "wb") as f:
        f.write(r.content)
    print(f"  Saved to {ZIP_PATH}")


def extract():
    print("  Extracting...")
    with zipfile.ZipFile(ZIP_PATH, "r") as z:
        z.extractall(DATA_DIR)
    print("  Done.")


def load_split(filename: str) -> pd.DataFrame:
    path = os.path.join(DATA_DIR, filename)
    df = pd.read_csv(path, sep="\t", header=None, names=COLUMNS)
    return df


def convert(df: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    true_rows = df[df["label"].isin(TRUE_LABELS)].copy()
    fake_rows = df[df["label"].isin(FAKE_LABELS)].copy()

    def to_isot_format(rows: pd.DataFrame, default_subject: str) -> pd.DataFrame:
        out = pd.DataFrame()
        out["title"] = rows["statement"].str.strip()
        out["text"]  = (
            rows["speaker"].fillna("unknown speaker") + " said: " +
            rows["statement"].str.strip() +
            " (Source: PolitiFact; Subject: " + rows["subject"].fillna("politics") + ")"
        )
        out["subject"] = rows["subject"].fillna(default_subject)
        out["date"]    = "unknown"
        return out.reset_index(drop=True)

    return to_isot_format(true_rows, "politics"), to_isot_format(fake_rows, "politics")


def main():
    print()
    print("╔══════════════════════════════════════════════╗")
    print("║          LIAR Dataset Preparation            ║")
    print("╚══════════════════════════════════════════════╝")
    print()

    download()
    extract()

    # Combine train + test + validation splits for maximum coverage
    splits = []
    for fname in ["train.tsv", "test.tsv", "valid.tsv"]:
        path = os.path.join(DATA_DIR, fname)
        if os.path.exists(path):
            splits.append(load_split(fname))
            print(f"  Loaded {fname}: {len(splits[-1])} rows")

    df = pd.concat(splits, ignore_index=True)
    print(f"  Combined: {len(df)} statements")

    label_counts = df["label"].value_counts()
    print(f"\n  Label distribution:")
    for label, count in label_counts.items():
        marker = "✓ TRUE" if label in TRUE_LABELS else ("✗ FAKE" if label in FAKE_LABELS else "  skip")
        print(f"    {marker}  {label:<15} {count}")

    true_df, fake_df = convert(df)

    true_path = os.path.join(DATA_DIR, "liar_true.csv")
    fake_path = os.path.join(DATA_DIR, "liar_fake.csv")
    true_df.to_csv(true_path, index=False)
    fake_df.to_csv(fake_path, index=False)

    print(f"\n  Saved {len(true_df)} TRUE statements → {true_path}")
    print(f"  Saved {len(fake_df)} FAKE statements → {fake_path}")
    print()
    print("  Next steps:")
    print("  1. python baseline.py  (trains on ISOT — run first if not done)")
    print("  2. python cross_dataset.py  (tests ISOT-trained model on LIAR)")
    print("  3. dotnet run -- --true data/liar_true.csv --fake data/liar_fake.csv")
    print("             --max 50 --delay 3000 --label 'Groq LIAR'")
    print("             --output-json metrics_groq_liar.json")
    print()


if __name__ == "__main__":
    main()
