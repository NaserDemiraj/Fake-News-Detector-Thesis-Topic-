"""
Multilingual evaluation of the LLM detector.

Most fake-news datasets (ISOT, LIAR) are English-only, so English-trained models
say nothing about other languages. Because this detector is an LLM prompted to
"detect the language" and reason in it, we can test cross-lingual behaviour directly.

For a small labelled set across 5 languages we measure:
  1. Language-detection accuracy  (does result.language match the true ISO code?)
  2. Verdict correctness          (real -> not fake / high score; fake -> fake / low score)

Runs against the DEPLOYED backend (production 70B model) so it reflects the live system.

Usage:
    python multilingual_eval.py
Output: multilingual_results.csv + terminal summary
"""

import json
import time
import urllib.request
import csv

API_URL = "https://naserd-fake-news-backend.hf.space/api/Analysis"

# (text, ISO-639-1, label, category)
#   real   = plausible factual reporting
#   absurd = physically impossible / long-debunked (easy case)
#   subtle = plausible-sounding but false claim (hard case)
SAMPLES = [
    # English
    ("The European Central Bank kept its benchmark interest rate unchanged at its "
     "latest meeting, citing easing inflation across the eurozone.", "en", "true", "real"),
    ("Scientists confirm the Earth is flat and the moon is made of cheese, according "
     "to leaked secret government documents they tried to hide.", "en", "fake", "absurd"),
    ("A new study published this week found that drinking two glasses of red wine every "
     "day completely eliminates the risk of heart disease, and doctors now recommend it "
     "for all adults.", "en", "fake", "subtle"),
    # Albanian
    ("Banka e Shqipërisë mbajti të pandryshuar normën bazë të interesit gjatë mbledhjes "
     "së fundit, duke përmendur ngadalësimin e inflacionit.", "sq", "true", "real"),
    ("Shkencëtarët konfirmojnë se Toka është e sheshtë dhe Hëna është prej djathi, sipas "
     "dokumenteve sekrete që u fshehën nga qeveria.", "sq", "fake", "absurd"),
    ("Një studim i ri tregon se pirja e dy gotave verë çdo ditë eliminon plotësisht "
     "rrezikun e sëmundjeve të zemrës, dhe mjekët tani e rekomandojnë për të gjithë.",
     "sq", "fake", "subtle"),
    # German
    ("Die Europäische Zentralbank beließ den Leitzins auf ihrer jüngsten Sitzung "
     "unverändert und verwies auf die nachlassende Inflation.", "de", "true", "real"),
    ("Wissenschaftler bestätigen, dass Impfstoffe Mikrochips zur Gedankenkontrolle "
     "enthalten, wie ein geheimes Dokument beweist.", "de", "fake", "absurd"),
    # Spanish
    ("El Banco Central Europeo mantuvo sin cambios su tipo de interés de referencia en "
     "su última reunión, citando la moderación de la inflación.", "es", "true", "real"),
    ("Científicos confirman que la Tierra es plana y que la NASA ha ocultado la verdad "
     "durante décadas, según documentos filtrados.", "es", "fake", "absurd"),
    # French
    ("La Banque centrale européenne a maintenu son taux directeur inchangé lors de sa "
     "dernière réunion, invoquant le ralentissement de l'inflation.", "fr", "true", "real"),
    ("Des scientifiques confirment que la Lune est faite de fromage, selon des documents "
     "secrets divulgués que les médias refusent de couvrir.", "fr", "fake", "absurd"),
    # Italian
    ("La Banca Centrale Europea ha mantenuto invariato il tasso di riferimento nella sua "
     "ultima riunione, citando il rallentamento dell'inflazione.", "it", "true", "real"),
    ("Gli scienziati confermano che la Terra è piatta e che la Luna è fatta di formaggio, "
     "secondo documenti segreti.", "it", "fake", "absurd"),
    # Portuguese
    ("O Banco Central Europeu manteve inalterada a sua taxa de juro de referência na "
     "última reunião, citando a desaceleração da inflação.", "pt", "true", "real"),
    ("Cientistas confirmam que a Terra é plana e que a NASA escondeu a verdade durante "
     "décadas, segundo documentos vazados.", "pt", "fake", "absurd"),
    # Turkish
    ("Avrupa Merkez Bankası, son toplantısında gösterge faiz oranını değiştirmedi ve "
     "enflasyondaki yavaşlamaya işaret etti.", "tr", "true", "real"),
    ("Bilim insanları, aşıların zihin kontrolü için mikroçip içerdiğini gizli bir belgeye "
     "dayanarak doğruladı.", "tr", "fake", "absurd"),
]

LANG_NAME = {"en": "English", "sq": "Albanian", "de": "German", "es": "Spanish",
             "fr": "French", "it": "Italian", "pt": "Portuguese", "tr": "Turkish"}


def analyze(text, timeout=90):
    payload = json.dumps({"type": "text", "content": text}).encode()
    req = urllib.request.Request(
        API_URL, data=payload,
        headers={"Content-Type": "application/json", "X-Bypass-Cache": "1"},
        method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read())
    r = data.get("result", data)
    return {"language": (r.get("language") or "").lower(),
            "languageName": r.get("languageName", ""),
            "verdict": r.get("verdict", "error"),
            "score": float(r.get("score", 50)),
            "isMock": bool(r.get("isMock", False))}


rows = []
print(f"{'Lang':<11}{'Cat':<7}{'Detect':<8}{'Verdict':<13}{'Score':>5}  Result")
print("-" * 60)
for text, lang, label, category in SAMPLES:
    try:
        r = analyze(text)
    except Exception as e:
        print(f"{LANG_NAME.get(lang,lang):<11}{category:<7}ERROR: {e}")
        continue

    lang_ok = r["language"] == lang
    # verdict correctness: fake should read as likely_fake/low; real as not-fake/high
    if label == "fake":
        verdict_ok = r["verdict"] == "likely_fake" or r["score"] <= 40
    else:
        verdict_ok = r["verdict"] != "likely_fake" and r["score"] >= 50

    rows.append({"Language": LANG_NAME.get(lang, lang), "ISO": lang, "Label": label,
                 "Category": category, "Detected": r["language"], "DetectName": r["languageName"],
                 "Verdict": r["verdict"], "Score": round(r["score"], 1),
                 "LangOK": lang_ok, "VerdictOK": verdict_ok, "Mock": r["isMock"]})

    tag = ("lang✗ " if not lang_ok else "") + ("verdict✗" if not verdict_ok else "ok" if lang_ok else "")
    print(f"{LANG_NAME.get(lang,lang):<11}{category:<7}{r['language']:<8}{r['verdict']:<13}{r['score']:>5.0f}  {tag}")
    time.sleep(0.4)

# ── Summary ──────────────────────────────────────────────────────────────────
if rows:
    with open("multilingual_results.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader(); w.writerows(rows)

    n = len(rows)
    lang_acc = sum(r["LangOK"] for r in rows) / n
    verdict_acc = sum(r["VerdictOK"] for r in rows) / n
    print("\n" + "=" * 60)
    print("  MULTILINGUAL SUMMARY")
    print("=" * 60)
    print(f"  Samples                 : {n}")
    print(f"  Language-detection acc  : {lang_acc:.0%}")
    print(f"  Verdict correctness     : {verdict_acc:.0%}")
    print("\n  Per language (lang-detect / verdict):")
    for code, name in LANG_NAME.items():
        sub = [r for r in rows if r["ISO"] == code]
        if not sub:
            continue
        la = sum(r["LangOK"] for r in sub) / len(sub)
        va = sum(r["VerdictOK"] for r in sub) / len(sub)
        print(f"    {name:<11} lang {la:.0%}  verdict {va:.0%}")

    print("\n  By difficulty (verdict correctness):")
    for cat in ("real", "absurd", "subtle"):
        sub = [r for r in rows if r["Category"] == cat]
        if not sub:
            continue
        va = sum(r["VerdictOK"] for r in sub) / len(sub)
        print(f"    {cat:<11} {va:.0%}  (n={len(sub)})")
    print("=" * 60)
    print("  Saved: multilingual_results.csv")
