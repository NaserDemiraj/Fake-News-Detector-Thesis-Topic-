# One-command thesis evaluation pipeline.
#
# Runs the whole analysis chain and collects every figure into ./figures.
# Each step is isolated: a failure (e.g. missing optional data) is reported
# but does NOT abort the rest.
#
# Usage:
#   .\run_full_evaluation.ps1                       # analysis only, on newest results_*.csv
#   .\run_full_evaluation.ps1 -Max 200              # run the live harness first (needs backend + key)
#   .\run_full_evaluation.ps1 -ResultsCsv results_200.csv
#   .\run_full_evaluation.ps1 -Max 200 -Live -Injection   # full run incl. live injection test
#
# Parameters:
#   -Max <int>       If >0, run the C# harness for <Max>/class first (needs backend running).
#   -ResultsCsv <s>  Analyse this specific CSV instead of the newest results_*.csv.
#   -Live            Also run live-backend robustness tests (adversarial).
#   -Injection       Also run the prompt-injection test (needs backend + key).
#   -Delay <int>     Per-call delay (ms) for live steps. Default 2000.

param(
    [int]$Max = 0,
    [string]$ResultsCsv = "",
    [switch]$Live,
    [switch]$Injection,
    [int]$Delay = 2000
)

$ErrorActionPreference = "Continue"
# Force UTF-8 stdout for child Python processes so Unicode banners/arrows don't
# crash on the Windows console (cp1252). Applies to every script the pipeline runs.
$env:PYTHONIOENCODING = "utf-8"
$py = "python"
$figures = "figures"
New-Item -ItemType Directory -Force -Path $figures | Out-Null

$steps = @()   # collected (Step, Status) for the final summary

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    Write-Host ""
    Write-Host "============================================================"
    Write-Host "  $Name"
    Write-Host "============================================================"
    try {
        & $Action
        if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
            $script:steps += [PSCustomObject]@{ Step = $Name; Status = "FAILED (exit $LASTEXITCODE)" }
        } else {
            $script:steps += [PSCustomObject]@{ Step = $Name; Status = "ok" }
        }
    } catch {
        Write-Host "  ! $Name failed: $_"
        $script:steps += [PSCustomObject]@{ Step = $Name; Status = "ERROR" }
    }
}

# 0. Optional live harness run
if ($Max -gt 0) {
    Invoke-Step "Live harness ($Max/class)" {
        dotnet run --no-build -- `
            --true data/True.csv --fake data/Fake.csv `
            --max $Max --delay $Delay --retries 3 `
            --label "Groq Full Prompt" `
            --output-csv "results_pipeline.csv" --output-json "metrics_pipeline.json"
    }
    if (Test-Path "results_pipeline.csv") { $ResultsCsv = "results_pipeline.csv" }
}

# Resolve the results CSV to analyse
if (-not $ResultsCsv) {
    $newest = Get-ChildItem -Filter "results_*.csv" -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest) { $ResultsCsv = $newest.Name }
}
if ($ResultsCsv) {
    Write-Host "`nAnalysing results file: $ResultsCsv"
} else {
    Write-Host "`nWARNING: no results_*.csv found - analysis steps that need one will be skipped."
}

# 1. Classical baseline
Invoke-Step "TF-IDF baseline" { & $py baseline.py }

# 2. ROC + optimal threshold
if ($ResultsCsv) { Invoke-Step "ROC curve + threshold" { & $py roc_curve.py $ResultsCsv } }

# 3. Calibration (with Platt)
if ($ResultsCsv) { Invoke-Step "Calibration (+Platt)" { & $py calibration.py $ResultsCsv --platt } }

# 4. Significance (ablation)
Invoke-Step "Significance / McNemar" { & $py significance.py }

# 5. Coverage / abstention curve
if ($ResultsCsv) { Invoke-Step "Coverage curve" { & $py coverage_curve.py $ResultsCsv } }

# 6. Error taxonomy
if ($ResultsCsv) { Invoke-Step "Error analysis" { & $py error_analysis.py $ResultsCsv } }

# 7. Cost / latency / quality
Invoke-Step "Cost / latency" { & $py cost_latency.py }

# 8. Cross-dataset (LIAR) - only if data present
if ((Test-Path "data/liar_true.csv") -and (Test-Path "baseline_model.pkl")) {
    Invoke-Step "Cross-dataset (LIAR)" { & $py cross_dataset.py }
} else {
    Write-Host "`n(Skipping cross-dataset: run liar_prep.py + baseline.py first.)"
}

# 9. Live robustness (opt-in)
if ($Live)      { Invoke-Step "Adversarial robustness" { & $py adversarial.py --max 30 --delay $Delay } }
if ($Injection) { Invoke-Step "Prompt injection"       { & $py prompt_injection.py --max 20 --delay $Delay } }

# Collect figures
Get-ChildItem -Filter "*.png" -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -ne (Resolve-Path $figures).Path } |
    Copy-Item -Destination $figures -Force

# Summary
Write-Host ""
Write-Host "============================================================"
Write-Host "  PIPELINE SUMMARY"
Write-Host "============================================================"
$steps | Format-Table -AutoSize
Write-Host "Figures collected in: $((Resolve-Path $figures).Path)"
Write-Host "CSV/JSON artifacts written alongside the scripts."
