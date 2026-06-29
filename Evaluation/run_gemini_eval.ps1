# Runs the evaluation harness against Gemini by temporarily disabling Groq.
# Usage: .\run_gemini_eval.ps1 [-Max 100] [-Delay 3000]

param(
    [int]$Max   = 100,
    [int]$Delay = 3000
)

$appsettings = "..\backend\appsettings.Development.json"
$backup      = "..\backend\appsettings.Development.json.bak"

# Back up appsettings
Copy-Item $appsettings $backup -Force
Write-Host "Backed up appsettings to $backup"

try {
    # Read and blank Groq key so Gemini becomes primary
    $config = Get-Content $appsettings -Raw | ConvertFrom-Json
    $groqKey = $config.Groq.ApiKey
    $config.Groq.ApiKey = ""
    $config | ConvertTo-Json -Depth 10 | Set-Content $appsettings -Encoding utf8
    Write-Host "Groq key blanked — Gemini is now primary provider"

    Write-Host ""
    Write-Host "Restart the backend now (dotnet run in the backend folder), then press Enter..."
    Read-Host

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $outJson   = "metrics_gemini_$timestamp.json"
    $outCsv    = "results_gemini_$timestamp.csv"

    Write-Host "Running evaluation: $Max articles per class, ${Delay}ms delay..."
    $env:DOTNET_ENVIRONMENT = "Development"
    dotnet run -- `
        --true  data/True.csv `
        --fake  data/Fake.csv `
        --max   $Max `
        --delay $Delay `
        --label "Gemini 2.0 Flash" `
        --output-json $outJson
}
finally {
    # Always restore the original config
    Copy-Item $backup $appsettings -Force
    Remove-Item $backup -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "Groq key restored. Restart the backend to switch back to Groq."
    Write-Host "Gemini metrics saved to: $outJson"
}
