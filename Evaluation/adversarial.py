"""
Adversarial robustness test: measures how stable verdicts are under text perturbations.

Reads the most-recent results CSV for original text, fetches the raw articles
from the ISOT dataset, applies perturbations, re-calls the API, and reports
the verdict flip rate per perturbation type.

Perturbation types:
  - typo:    randomly swap 2% of characters with an adjacent keyboard key
  - swap:    randomly swap two adjacent words (5% of words)
  - caps:    randomly uppercase 20% of words
  - noise:   insert random 3-5 char strings between words (5% of gaps)

Usage (backend must be running on localhost:5000):
    python adversarial.py               # 30 items from most-recent results CSV
    python adversarial.py --max 20
    python adversarial.py --csv results_XYZ.csv --max 30 --delay 800

Output: adversarial_results.csv  +  terminal summary table
"""

import sys
import glob
import os
import json
import time
import random
import string
import argparse
import urllib.request
import urllib.error
import pandas as pd
import numpy as np

random.seed(42)
np.random.seed(42)

API_URL = "http://localhost:5000/api/Analysis"

# ── Keyboard neighbour map for realistic typos ────────────────────────────────
NEIGHBOURS = {
    'a': 'sqzw', 'b': 'vghn', 'c': 'xdfv', 'd': 'serfcx', 'e': 'wsdr',
    'f': 'drtgvc', 'g': 'ftyhbv', 'h': 'gyujnb', 'i': 'ujko', 'j': 'huiknm',
    'k': 'jiolm', 'l': 'kop', 'm': 'njk', 'n': 'bhjm', 'o': 'iklp',
    'p': 'ol', 'q': 'wa', 'r': 'edft', 's': 'awedxz', 't': 'rfgy',
    'u': 'yhji', 'v': 'cfgb', 'w': 'qase', 'x': 'zsdc', 'y': 'tghu',
    'z': 'asx',
}


def perturb_typo(text: str, rate: float = 0.02) -> str:
    chars = list(text)
    for i, c in enumerate(chars):
        if random.random() < rate and c.lower() in NEIGHBOURS:
            repl = random.choice(NEIGHBOURS[c.lower()])
            chars[i] = repl.upper() if c.isupper() else repl
    return "".join(chars)


def perturb_swap(text: str, rate: float = 0.05) -> str:
    words = text.split()
    for i in range(len(words) - 1):
        if random.random() < rate:
            words[i], words[i + 1] = words[i + 1], words[i]
    return " ".join(words)


def perturb_caps(text: str, rate: float = 0.20) -> str:
    words = text.split()
    return " ".join(w.upper() if random.random() < rate else w for w in words)


def perturb_noise(text: str, rate: float = 0.05) -> str:
    words = text.split()
    out = []
    for w in words:
        out.append(w)
        if random.random() < rate:
            noise = "".join(random.choices(string.ascii_lowercase, k=random.randint(3, 5)))
            out.append(noise)
    return " ".join(out)


PERTURBATIONS = {
    "typo":  perturb_typo,
    "swap":  perturb_swap,
    "caps":  perturb_caps,
    "noise": perturb_noise,
}


# ── API call ──────────────────────────────────────────────────────────────────
def analyze(text: str, timeout: int = 15) -> dict | None:
    payload = json.dumps({"type": "text", "content": text[:8000]}).encode()
    req = urllib.request.Request(
        API_URL,
        data=payload,
        headers={"Content-Type": "application/json",
                 "X-Bypass-Cache": "1"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read())
    except Exception:
        return None


# ── Load articles from ISOT dataset CSVs (if available) ──────────────────────
def load_isot_texts(true_csv="data/True.csv", fake_csv="data/Fake.csv") -> dict[int, str]:
    """Returns {1-based-index: full_text} from ISOT, matching the eval harness seed."""
    try:
        import csv as csvlib
        items = []
        for path, label in [(true_csv, "true"), (fake_csv, "fake")]:
            if not os.path.exists(path):
                return {}
            with open(path, encoding="utf-8", errors="ignore") as f:
                reader = csvlib.DictReader(f)
                for row in reader:
                    text = (row.get("text") or "").strip()
                    title = (row.get("title") or "").strip()
                    if len(text) > 80:
                        items.append((title, text, label))
        # Reproduce the eval harness sampling (seed=42, balanced)
        rng = random.Random(42)
        true_items = [(t, x, l) for t, x, l in items if l == "true"]
        fake_items = [(t, x, l) for t, x, l in items if l == "fake"]
        sampled = (sorted(true_items, key=lambda _: rng.random())
                   + sorted(fake_items, key=lambda _: rng.random()))
        sampled = sorted(sampled, key=lambda _: rng.random())
        return {i + 1: (f"{t}\n\n{x}" if t else x) for i, (t, x, _) in enumerate(sampled)}
    except Exception:
        return {}


# ── Parse args ────────────────────────────────────────────────────────────────
parser = argparse.ArgumentParser()
parser.add_argument("--max", type=int, default=30, help="Number of articles to test")
parser.add_argument("--csv", type=str, default="", help="Results CSV to use")
parser.add_argument("--delay", type=int, default=600, help="Delay between API calls (ms)")
args = parser.parse_args()

csv_path = args.csv or (sorted(glob.glob("results_*.csv")) or [""])[-1]
if not csv_path or not os.path.exists(csv_path):
    print("No results CSV found. Run the evaluation harness first.")
    sys.exit(1)

print(f"Using results CSV: {csv_path}")
orig_df = pd.read_csv(csv_path)
orig_df = orig_df[orig_df["IsError"] == False].head(args.max).reset_index(drop=True)
print(f"Testing {len(orig_df)} articles x {len(PERTURBATIONS)} perturbations "
      f"= {len(orig_df) * len(PERTURBATIONS)} API calls\n")

# Try to load full text from ISOT; fall back to TitlePreview
isot_texts = load_isot_texts()
if isot_texts:
    print("Full ISOT text loaded — using complete articles")
else:
    print("ISOT data not available — using TitlePreview (shorter text, expect more flips)")

# ── Ping backend ──────────────────────────────────────────────────────────────
ping = analyze("test")
if ping is None:
    print("\nERROR: Cannot reach backend at http://localhost:5000")
    print("Start the backend first: cd backend && dotnet run")
    sys.exit(1)

# ── Run perturbations ─────────────────────────────────────────────────────────
rows = []
total = len(orig_df) * len(PERTURBATIONS)
done = 0

for _, article in orig_df.iterrows():
    idx = int(article["Index"])
    orig_verdict = article["PredictedVerdict"]
    true_label = article["TrueLabel"]

    # Get full text: prefer ISOT dataset, fall back to TitlePreview
    text = isot_texts.get(idx, str(article.get("TitlePreview", "")))
    if len(text) < 20:
        continue

    for ptype, pfn in PERTURBATIONS.items():
        perturbed = pfn(text)
        result = analyze(perturbed)
        done += 1

        if result:
            new_verdict = result.get("result", {}).get("verdict", "error")
            flipped = (new_verdict != orig_verdict) and new_verdict not in ("error", "uncertain")
        else:
            new_verdict = "error"
            flipped = False

        rows.append({
            "Index": idx,
            "TrueLabel": true_label,
            "OrigVerdict": orig_verdict,
            "Perturbation": ptype,
            "NewVerdict": new_verdict,
            "Flipped": flipped,
        })

        status = "FLIP" if flipped else ("err" if new_verdict == "error" else "ok")
        print(f"  [{done:3d}/{total}] idx={idx:3d}  {ptype:<6}  {orig_verdict:<12} -> {new_verdict:<12}  [{status}]")

        if done < total:
            time.sleep(args.delay / 1000)

# ── Summary ───────────────────────────────────────────────────────────────────
df = pd.DataFrame(rows)
valid = df[df["NewVerdict"] != "error"]

print("\n" + "=" * 55)
print("  ADVERSARIAL ROBUSTNESS SUMMARY")
print("=" * 55)
print(f"  Articles tested   : {orig_df.shape[0]}")
print(f"  Total calls       : {len(rows)}")
print(f"  Errors            : {(df['NewVerdict'] == 'error').sum()}")
print(f"  Valid responses   : {len(valid)}")
print()
print(f"  {'Perturbation':<12}  {'Flips':>6}  {'Valid':>6}  {'Flip Rate':>10}")
print("  " + "-" * 40)
for ptype in PERTURBATIONS:
    sub = valid[valid["Perturbation"] == ptype]
    flips = sub["Flipped"].sum()
    rate = flips / len(sub) if len(sub) > 0 else 0.0
    print(f"  {ptype:<12}  {flips:>6}  {len(sub):>6}  {rate:>9.1%}")
print()
overall_rate = valid["Flipped"].mean() if len(valid) > 0 else 0.0
print(f"  Overall flip rate : {overall_rate:.1%}")
print("=" * 55)

out_csv = "adversarial_results.csv"
df.to_csv(out_csv, index=False)
print(f"\nSaved: {out_csv}")
