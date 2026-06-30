# Model-capacity sweep: runs the eval with different Groq models to see whether
# a larger model can actually separate fake from real (our AUC was ~0.55 with 8b).
#
# Usage: .\run_models.ps1 [-Max 100] [-Delay 2000]
# Output: results_model_<key>.csv + metrics_model_<key>.json per model
# Then:   python roc_curve.py results_model_*.csv   (compares AUC across models)
#
# Uses the existing Groq key (just swaps Groq:Model). Add Gemini/other entries
# to $models if you want to sweep providers too.

param(
    [int]$Max   = 100,
    [int]$Delay = 2000
)

$appsettings = "..\backend\appsettings.Development.json"
$config = Get-Content $appsettings -Raw | ConvertFrom-Json
$originalModel = $config.Groq.Model

# (key, model-string, label). Key is used in output filenames.
$models = @(
    @{ Key = "8b";  Model = "llama-3.1-8b-instant";   Label = "Groq llama-3.1-8b" },
    @{ Key = "70b"; Model = "llama-3.3-70b-versatile"; Label = "Groq llama-3.3-70b" }
)

foreach ($m in $models) {
    Write-Host ""
    Write-Host "============================================================"
    Write-Host ("  MODEL: " + $m.Label + "  (" + $m.Model + ")")
    Write-Host "============================================================"

    Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    # Patch Groq:Model in appsettings
    $config.Groq.Model = $m.Model
    $config | ConvertTo-Json -Depth 10 | Set-Content $appsettings -Encoding utf8

    Write-Host ("Starting backend with model '" + $m.Model + "'...")
    $backend = Start-Process -FilePath "dotnet" `
        -ArgumentList "run","--project","..\backend\FakeNewsDetector.csproj","--no-build" `
        -PassThru -WindowStyle Minimized
    Start-Sleep -Seconds 8

    # Wait for health (up to ~44s)
    $ready = $false
    for ($i = 0; $i -lt 12; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 3 -UseBasicParsing
            $ready = $true; break
        } catch { Start-Sleep -Seconds 3 }
    }
    if (-not $ready) {
        Write-Host ("Backend failed to start for model " + $m.Key + " -- skipping")
        Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue
        continue
    }

    $csv  = "results_model_" + $m.Key + ".csv"
    $json = "metrics_model_" + $m.Key + ".json"
    Write-Host ("Running eval (" + $Max + " articles/class)...")

    dotnet run --no-build -- `
        --true    data/True.csv `
        --fake    data/Fake.csv `
        --max     $Max `
        --delay   $Delay `
        --retries 3 `
        --label   $m.Label `
        --output-csv  $csv `
        --output-json $json

    Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

# Restore the original model
$config.Groq.Model = $originalModel
$config | ConvertTo-Json -Depth 10 | Set-Content $appsettings -Encoding utf8

Write-Host ""
Write-Host "============================================================"
Write-Host "  DONE. Compare separation power across models:"
Write-Host "    python roc_curve.py results_model_*.csv"
Write-Host "  (AUC ~0.5 = can't tell fake from real; higher = better)"
Write-Host "============================================================"
