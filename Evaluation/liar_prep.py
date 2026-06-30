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

import argparse
import os
import zipfile
import requests
import pandas as pd

DATA_DIR = "data"
# The original UCSB host is frequently down; try mirrors in order.
ZIP_URLS = [
    "https://www.cs.ucsb.edu/~william/data/liar_dataset.zip",
    "https://huggingface.co/datasets/liar/resolve/main/liar_dataset.zip",
    "https://github.com/thiagorainmaker77/liar_dataset/raw/master/liar_dataset.zip",
]
ZIP_PATH = os.path.join(DATA_DIR, "liar_dataset.zip")
TSV_FILES = ["train.tsv", "test.tsv", "valid.tsv"]

COLUMNS = [
    "id", "label", "statement", "subject", "speaker",
    "speaker_job", "state", "party",
    "barely_true_count", "false_count", "half_true_count",
    "mostly_true_count", "pants_fire_count", "venue"
]

# Binary mapping: only keep unambiguous labels
TRUE_LABELS = {"true", "mostly-true"}
FAKE_LABELS = {"false", "pants-fire"}


def already_extracted() -> bool:
    return all(os.path.exists(os.path.join(DATA_DIR, f)) for f in TSV_FILES)


def download():
    os.makedirs(DATA_DIR, exist_ok=True)
    if os.path.exists(ZIP_PATH):
        print(f"  Already downloaded: {ZIP_PATH}")
        return
    last_err = None
    for url in ZIP_URLS:
        try:
            print(f"  Downloading LIAR dataset from {url} ...")
            r = requests.get(url, timeout=30)
            r.raise_for_status()
            with open(ZIP_PATH, "wb") as f:
                f.write(r.content)
            print(f"  Saved to {ZIP_PATH}")
            return
        except Exception as e:
            last_err = e
            print(f"    failed ({e}); trying next mirror...")
    # All mirrors failed — give clear manual instructions.
    raise SystemExit(
        "\nERROR: could not download the LIAR dataset from any mirror.\n"
        f"Last error: {last_err}\n\n"
        "Manual fix: download liar_dataset.zip (search 'LIAR dataset PolitiFact'),\n"
        f"place train.tsv / test.tsv / valid.tsv directly in '{DATA_DIR}/', then re-run.\n")


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
    parser = argparse.ArgumentParser()
    parser.add_argument("--balance", action="store_true",
                        help="Downsample the larger class so true/fake counts match "
                             "(fair comparison with balanced ISOT)")
    cli = parser.parse_args()

    print()
    print("╔══════════════════════════════════════════════╗")
    print("║          LIAR Dataset Preparation            ║")
    print("╚══════════════════════════════════════════════╝")
    print()

    # Skip the (flaky) download if the TSVs are already present.
    if already_extracted():
        print("  TSV files already present — skipping download/extract.")
    else:
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

    if cli.balance:
        n = min(len(true_df), len(fake_df))
        true_df = true_df.sample(n, random_state=42).reset_index(drop=True)
        fake_df = fake_df.sample(n, random_state=42).reset_index(drop=True)
        print(f"\n  Balanced to {n} per class.")

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
