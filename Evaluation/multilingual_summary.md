# Multilingual Evaluation

## Why this matters
The TF-IDF baseline and the ISOT/LIAR datasets are **English-only**. A bag-of-words
model trained on English learns nothing transferable to other languages — it would score
an Albanian or German article at chance. Because this detector is an LLM prompted to
*detect the language and reason in it*, it can in principle generalise cross-lingually.
Most fake-news-detection theses never test this; it is a genuinely novel angle here.

## Method
`Evaluation/multilingual_eval.py` — a labelled set of 18 short items across **8 languages**
(English, Albanian, German, Spanish, French, Italian, Portuguese, Turkish), each in three
difficulty categories where available:
- **real** — plausible factual reporting,
- **absurd** — physically impossible / long-debunked (easy fake),
- **subtle** — plausible-sounding but false claim (hard fake, e.g. a fabricated health
  study), included for English and Albanian.

Each item is sent to the **deployed production backend** (`llama-3.3-70b-versatile`, with
web grounding). We measure:
1. **Language-detection accuracy** — does `result.language` match the true ISO-639-1 code?
2. **Verdict correctness** — real ⇒ not `likely_fake` and score ≥ 50; fake ⇒ `likely_fake`
   or score ≤ 40.

## Results

| Language   | Lang-detect | Verdict |
|------------|:-----------:|:-------:|
| English    | 100%        | 100%    |
| Albanian   | 100%        | 100%    |
| German     | 100%        | 100%    |
| Spanish    | 100%        | 100%    |
| French     | 100%        | 100%    |
| Italian    | 100%        | 100%    |
| Portuguese | 100%        | 100%    |
| Turkish    | 100%        | 100%    |
| **Overall (n=18)** | **100%** | **100%** |

By difficulty: **real 100%** (n=8), **absurd 100%** (n=8), **subtle 100%** (n=2).

Every language was correctly identified. Plausible-real items scored ~92–95
(`likely_true`); absurd fakes scored 0–2 (`likely_fake`); and — importantly — the
**subtle** fabricated-health-study claim was caught as `likely_fake` (score 15) in **both**
English and Albanian, so the detector is not merely rejecting physically-impossible
statements but is reasoning about plausible-but-false claims cross-lingually.

## Interpretation
- The LLM detector **generalises across languages out of the box**, with no
  language-specific training — a capability the TF-IDF baseline structurally cannot have.
- This complements the cross-dataset headline: TF-IDF's 98.7% F1 on ISOT is a
  language- *and* dataset-specific artefact (collapses to ~49% on LIAR, and would be
  chance on non-English text), whereas the LLM transfers across both dataset and language.

## Caveats
- Small, hand-authored set (n = 10, 2 per language) with deliberately clear-cut items —
  this demonstrates the *capability*, not a precise accuracy estimate. For a stronger
  claim, expand to a translated slice of LIAR or a native-language fact-check corpus
  (e.g. per-language claims from the Google Fact Check API) with more, subtler items.
- Absurd fakes are the easiest case; nuanced political misinformation in low-resource
  languages (e.g. Albanian) is the harder, more interesting frontier to test next.
