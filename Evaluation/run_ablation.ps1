# Ablation study: runs eval 4 times with different prompt variants.
# Each variant tests a different component of the prompt design.
#
# Usage: .\run_ablation.ps1 [-Max 30] [-Delay 4000]
# Output: metrics_ablation_*.json + ablation_summary.json

param(
    [int]$Max   = 30,
    [int]$Delay = 4000
)

$appsettings = "..\backend\appsettings.Development.json"
$config = Get-Content $appsettings -Raw | ConvertFrom-Json

$variants = @(
    @{ Name = "zero_shot";  Label = "Groq Zero-Shot";          Description = "Plain JSON schema only" },
    @{ Name = "skepticism"; Label = "Groq + Skepticism";        Description = "Skepticism preamble, no examples" },
    @{ Name = "few_shot";   Label = "Groq + Few-Shot";          Description = "3 labeled examples, no skepticism rules" },
    @{ Name = "full";       Label = "Groq Full Prompt";         Description = "Both skepticism + examples combined" }
)

$results = @()

foreach ($v in $variants) {
    Write-Host ""
    Write-Host "============================================================"
    Write-Host ("  VARIANT: " + $v.Label)
    Write-Host ("  " + $v.Description)
    Write-Host "============================================================"

    # Kill any existing dotnet backend before starting a fresh one
    Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    # Patch PromptVariant in appsettings
    $config | Add-Member -NotePropertyName "PromptVariant" -NotePropertyValue $v.Name -Force
    $config | ConvertTo-Json -Depth 10 | Set-Content $appsettings -Encoding utf8

    # Start backend (--no-build: build once before running this script)
    Write-Host ("Starting backend with variant '" + $v.Name + "'...")
    $backend = Start-Process -FilePath "dotnet" `
        -ArgumentList "run","--project","..\backend\FakeNewsDetector.csproj","--no-build" `
        -PassThru -WindowStyle Minimized
    Start-Sleep -Seconds 8

    # Wait for health check (up to ~44 seconds)
    $ready = $false
    for ($i = 0; $i -lt 12; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 3 -UseBasicParsing
            $ready = $true
            break
        } catch {
            Start-Sleep -Seconds 3
        }
    }

    if (-not $ready) {
        Write-Host ("Backend failed to start for variant " + $v.Name + " -- skipping")
        Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue
        continue
    }

    $outJson = "metrics_ablation_" + $v.Name + ".json"

    $msg = "Running eval (" + $Max + " articles/class, " + $Delay + "ms delay)..."
    Write-Host $msg

    dotnet run --no-build -- `
        --true    data/True.csv `
        --fake    data/Fake.csv `
        --max     $Max `
        --delay   $Delay `
        --retries 3 `
        --label   $v.Label `
        --output-json $outJson

    Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    if (Test-Path $outJson) {
        $m = Get-Content $outJson | ConvertFrom-Json
        $results += [PSCustomObject]@{
            Variant     = $v.Name
            Label       = $v.Label
            Accuracy    = [math]::Round($m.accuracy * 100, 1)
            Precision   = [math]::Round($m.precision * 100, 1)
            Recall      = [math]::Round($m.recall * 100, 1)
            F1          = [math]::Round($m.f1 * 100, 1)
            Specificity = [math]::Round($m.specificity * 100, 1)
            Errors      = $m.errors
        }
    }
}

# Restore appsettings to "full"
$config | Add-Member -NotePropertyName "PromptVariant" -NotePropertyValue "full" -Force
$config | ConvertTo-Json -Depth 10 | Set-Content $appsettings -Encoding utf8

Write-Host ""
Write-Host "============================================================"
Write-Host "  ABLATION RESULTS"
Write-Host "============================================================"
$results | Format-Table -AutoSize

$results | ConvertTo-Json | Set-Content "ablation_summary.json" -Encoding utf8
Write-Host ""
Write-Host "Full results saved to ablation_summary.json"
Write-Host "Per-variant metrics in metrics_ablation_VARIANT.json"
