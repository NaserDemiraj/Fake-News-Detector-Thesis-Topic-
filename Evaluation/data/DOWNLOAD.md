# ISOT Fake News Dataset

Place the two CSV files from the ISOT dataset here:

```
data/
  True.csv   ← real news articles
  Fake.csv   ← fake news articles
```

## Where to download

**ISOT Fake News Dataset** — University of New Brunswick
https://www.uvic.ca/engineering/ece/isot/datasets/fake-news/index.php

Or via Kaggle (same data, easier download):
https://www.kaggle.com/datasets/clmentbisaillon/fake-and-real-news-dataset

Both files are ~50 MB total (≈45 000 articles).

## CSV format expected

```
title,text,subject,date
"Article headline","Full article text...","Politics","January 1, 2018"
```

CsvHelper handles quoted multi-line text fields automatically.

## Quick smoke-test (no dataset needed)

A small hand-crafted sample is provided as `sample_true.csv` and
`sample_fake.csv` in this folder. Run:

```
dotnet run -- --true data/sample_true.csv --fake data/sample_fake.csv --max 5 --label "smoke-test"
```

This lets you verify the harness end-to-end without downloading the full corpus.
