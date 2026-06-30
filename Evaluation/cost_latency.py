"""
Cost / latency / accuracy trade-off analysis across providers and prompt variants.

Reads every metrics_*.json the harness produced and builds a comparison of:
  - quality   : accuracy, F1, specificity (measured)
  - latency   : mean seconds per analysis (measured)
  - cost      : estimated USD per 1,000 analyses (token estimate x provider price)

The harness does not log token counts, so cost is an ESTIMATE from documented
assumptions (printed below) — latency and quality are real measurements.
This directly answers the practical thesis question: "is an LLM detector
worth it vs. the near-free TF-IDF baseline?"

Usage:
    python cost_latency.py                  # all metrics_*.json in cwd
    python cost_latency.py metrics_a.json metrics_b.json

Output: cost_latency.csv + cost_latency.png (+ terminal table)
"""

import sys
import os
import glob
import json
import pandas as pd

try:
    import matplotlib.pyplot as plt
    HAS_PLOT = True
except ImportError:
    HAS_PLOT = False

# ── Cost model (assumptions — edit to match your provider's current pricing) ──
# USD per 1M tokens (input, output). Free tiers shown as 0.0 with a note.
PRICING = {
    # provider key (matched case-insensitively against backendLabel) : (in, out, note)
    "groq":    (0.05, 0.08, "Groq llama-3.1-8b (paid tier est.)"),
    "gemini":  (0.10, 0.40, "Gemini 2.0 Flash"),
    "grok":    (2.00, 10.00, "xAI grok-3"),
    "xai":     (2.00, 10.00, "xAI grok-3"),
    "ollama":  (0.00, 0.00, "Ollama local (no API cost; hardware only)"),
    "tf-idf":  (0.00, 0.00, "Classical baseline (CPU only)"),
    "baseline":(0.00, 0.00, "Classical baseline (CPU only)"),
}
# Token estimate per analysis. Harness truncates content to 5000 chars (~1250
# tokens); add ~250 for the prompt scaffold; output JSON is ~400 tokens.
EST_INPUT_TOKENS  = 1500
EST_OUTPUT_TOKENS = 400


def match_pricing(label: str):
    low = (label or "").lower()
    for key, val in PRICING.items():
        if key in low:
            return key, val
    return "unknown", (None, None, "unknown provider — cost not estimated")


def cost_per_1k(label: str):
    _, (pin, pout, _) = match_pricing(label)
    if pin is None:
        return None
    per_call = (EST_INPUT_TOKENS / 1e6) * pin + (EST_OUTPUT_TOKENS / 1e6) * pout
    return round(per_call * 1000, 4)  # per 1,000 analyses


def load_metrics(paths):
    rows = []
    for p in paths:
        try:
            with open(p, encoding="utf-8-sig") as f:  # utf-8-sig: PowerShell BOM
                d = json.load(f)
        except Exception as e:
            print(f"  skip {p}: {e}")
            continue
        label = d.get("backendLabel", os.path.basename(p))
        rows.append({
            "File": os.path.basename(p),
            "Backend": label,
            "Dataset": d.get("dataset", "?"),
            "N": d.get("total", 0),
            "Accuracy": d.get("accuracy"),
            "F1": d.get("f1"),
            "Specificity": d.get("specificity"),
            "LatencySec": round(d.get("meanLatencyMs", 0) / 1000.0, 2),
            "CostPer1k": cost_per_1k(label),
            "Provider": match_pricing(label)[0],
        })
    return pd.DataFrame(rows)


# ── Collect inputs ────────────────────────────────────────────────────────────
args = [a for a in sys.argv[1:] if a.endswith(".json")]
paths = args or sorted(glob.glob("metrics_*.json"))
if not paths:
    print("No metrics_*.json found. Run the evaluation harness / baseline first.")
    sys.exit(1)

df = load_metrics(paths)
if df.empty:
    print("No metrics could be loaded.")
    sys.exit(1)

# De-duplicate by Backend label, keeping the run with the most samples
df = df.sort_values("N", ascending=False).drop_duplicates("Backend").reset_index(drop=True)
df = df.sort_values("Accuracy", ascending=False, na_position="last").reset_index(drop=True)

# ── Print table ───────────────────────────────────────────────────────────────
def pct(v): return f"{v*100:.1f}%" if isinstance(v, (int, float)) else "N/A"
def usd(v): return f"${v:.3f}" if isinstance(v, (int, float)) else "N/A"

print()
print("=" * 92)
print("  COST / LATENCY / QUALITY TRADE-OFF")
print("=" * 92)
print(f"  {'Backend':<26} {'N':>4} {'Acc':>7} {'F1':>7} {'Spec':>7} {'Latency':>9} {'$/1k runs':>10}")
print("  " + "-" * 86)
for _, r in df.iterrows():
    print(f"  {str(r['Backend'])[:26]:<26} {int(r['N']):>4} {pct(r['Accuracy']):>7} "
          f"{pct(r['F1']):>7} {pct(r['Specificity']):>7} {r['LatencySec']:>7.2f}s {usd(r['CostPer1k']):>10}")
print("=" * 92)
print("  Cost is ESTIMATED: "
      f"{EST_INPUT_TOKENS} input + {EST_OUTPUT_TOKENS} output tokens/analysis x provider price.")
print("  Latency and quality metrics are measured. Edit PRICING in this script to update rates.")
print("=" * 92)

out_csv = "cost_latency.csv"
df.to_csv(out_csv, index=False)
print(f"\nSaved: {out_csv}")

# ── Plot: accuracy vs latency, bubble size ~ cost ─────────────────────────────
if HAS_PLOT and df["Accuracy"].notna().any():
    plot_df = df[df["Accuracy"].notna() & (df["LatencySec"] > 0)]
    if not plot_df.empty:
        fig, ax = plt.subplots(figsize=(9, 6), layout="constrained")
        costs = plot_df["CostPer1k"].fillna(0.0)
        sizes = [80 + c * 400 for c in costs]  # bubble area grows with cost
        sc = ax.scatter(plot_df["LatencySec"], plot_df["Accuracy"] * 100,
                        s=sizes, c=range(len(plot_df)), cmap="viridis",
                        alpha=0.75, edgecolors="white", linewidths=1.5)
        for _, r in plot_df.iterrows():
            ax.annotate(str(r["Backend"])[:22],
                        (r["LatencySec"], r["Accuracy"] * 100),
                        textcoords="offset points", xytext=(8, 4), fontsize=8)
        ax.set_xlabel("Mean latency per analysis (seconds)  →  slower")
        ax.set_ylabel("Accuracy (%)  →  better")
        ax.set_title("Quality vs. Latency vs. Cost\n(bubble size ∝ estimated $/1k analyses)",
                     fontweight="bold")
        ax.grid(True, alpha=0.25)
        out_png = "cost_latency.png"
        plt.savefig(out_png, dpi=150)
        print(f"Saved: {out_png}")
