using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text.Json;

namespace FakeNewsDetector.Evaluation;

public static class EvaluationRunner
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task RunAsync(string[] args)
    {
        var opts = ParseArgs(args);

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║       VerifyNews Evaluation Harness v1.0     ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  API endpoint : {opts.ApiUrl}");
        Console.WriteLine($"  Backend label: {opts.Label}");
        Console.WriteLine($"  Max per class: {opts.MaxPerClass}");
        Console.WriteLine($"  Delay (ms)   : {opts.DelayMs}");
        Console.WriteLine($"  Output CSV   : {opts.OutputCsv}");
        Console.WriteLine($"  Output JSON  : {opts.OutputJson}");
        Console.WriteLine();

        // ── 1. Check backend is reachable ──────────────────────────────────
        using var client = new ApiClient(opts.ApiUrl);
        Console.Write("  Pinging backend… ");
        if (!await client.PingAsync())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILED");
            Console.ResetColor();
            Console.WriteLine($"  Cannot reach {opts.ApiUrl}. Start the backend first:");
            Console.WriteLine("    cd backend && dotnet run");
            Environment.Exit(1);
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("OK");
        Console.ResetColor();
        Console.WriteLine();

        // ── 2. Load dataset ────────────────────────────────────────────────
        Console.WriteLine("  Loading dataset…");
        List<DatasetItem> items;
        try
        {
            items = DatasetReader.LoadIsot(opts.TrueFile, opts.FakeFile, opts.MaxPerClass, opts.Seed);
        }
        catch (FileNotFoundException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
            return;
        }
        Console.WriteLine();

        // ── 3. Run predictions ─────────────────────────────────────────────
        Console.WriteLine($"  Running {items.Count} predictions…");
        Console.WriteLine();

        var rows = new List<EvaluationRow>(items.Count);
        int idx = 0;

        foreach (var item in items)
        {
            idx++;
            var content = string.IsNullOrEmpty(item.Title)
                ? item.Text
                : item.Title + "\n\n" + item.Text;

            var pred = await client.AnalyzeAsync(content);

            var row = new EvaluationRow(
                idx,
                TitlePreview(item.Title, item.Text),
                item.TrueLabel,
                pred.IsError ? "error" : pred.Verdict,
                pred.Score,
                pred.Confidence,
                pred.LatencyMs,
                pred.IsError,
                pred.ErrorMessage);

            rows.Add(row);
            PrintRow(row, items.Count);

            if (opts.DelayMs > 0 && idx < items.Count)
                await Task.Delay(opts.DelayMs);
        }

        // ── 4. Compute metrics ─────────────────────────────────────────────
        var metrics = MetricsResult.Compute(rows, opts.Label, "ISOT", opts.MaxPerClass);
        metrics.PrintToConsole();

        // ── 5. Save outputs ────────────────────────────────────────────────
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var csvPath  = opts.OutputCsv  ?? $"results_{stamp}.csv";
        var jsonPath = opts.OutputJson ?? $"metrics_{stamp}.json";

        SaveCsv(rows, csvPath);
        SaveJson(metrics, jsonPath);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Results CSV  : {Path.GetFullPath(csvPath)}");
        Console.WriteLine($"  Metrics JSON : {Path.GetFullPath(jsonPath)}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("  To compare multiple backends:");
        Console.WriteLine("    dotnet run -- compare metrics_groq.json metrics_gemini.json metrics_ollama.json");
        Console.WriteLine();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void PrintRow(EvaluationRow row, int total)
    {
        var correct = !row.IsError && row.TrueLabel != "uncertain" &&
            ((row.TrueLabel == "true" && row.PredictedVerdict == "likely_true") ||
             (row.TrueLabel == "fake" && row.PredictedVerdict == "likely_fake"));

        var icon = row.IsError ? "✗" : row.PredictedVerdict == "uncertain" ? "?" : correct ? "✓" : "✗";

        Console.ForegroundColor = row.IsError ? ConsoleColor.Red
            : row.PredictedVerdict == "uncertain" ? ConsoleColor.Yellow
            : correct ? ConsoleColor.Green
            : ConsoleColor.Red;

        Console.Write($"  [{row.Index,3}/{total}] {icon} ");
        Console.ResetColor();

        var pred = row.IsError ? $"ERROR: {(row.ErrorMessage ?? "")[-Math.Min(row.ErrorMessage?.Length ?? 0, 50)..]}"
                               : $"{row.PredictedVerdict,-12} (score={row.Score:F0}, conf={row.Confidence:F2}) {row.LatencyMs}ms";

        Console.WriteLine($"true={row.TrueLabel,-4}  {pred}");
        if (row.IsError)
            Console.WriteLine($"         {row.ErrorMessage}");
    }

    private static void SaveCsv(List<EvaluationRow> rows, string path)
    {
        using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.WriteRecords(rows.Select(r => new
        {
            r.Index,
            r.TitlePreview,
            r.TrueLabel,
            r.PredictedVerdict,
            r.Score,
            r.Confidence,
            r.LatencyMs,
            r.IsError,
            r.ErrorMessage,
            Correct = !r.IsError && r.PredictedVerdict != "uncertain" &&
                      ((r.TrueLabel == "true" && r.PredictedVerdict == "likely_true") ||
                       (r.TrueLabel == "fake" && r.PredictedVerdict == "likely_fake")),
        }));
    }

    private static void SaveJson(MetricsResult metrics, string path)
    {
        var json = JsonSerializer.Serialize(metrics, JsonOpts);
        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
    }

    private static string TitlePreview(string title, string text)
    {
        var src = string.IsNullOrEmpty(title) ? text : title;
        return src.Length > 80 ? src[..80] + "…" : src;
    }

    // ── Argument parsing ───────────────────────────────────────────────────

    private record Options(
        string ApiUrl,
        string Label,
        string TrueFile,
        string FakeFile,
        int MaxPerClass,
        int DelayMs,
        int Seed,
        string? OutputCsv,
        string? OutputJson);

    private static Options ParseArgs(string[] args)
    {
        string apiUrl      = "http://localhost:5000";
        string label       = "Unknown backend";
        string trueFile    = Path.Combine("data", "True.csv");
        string fakeFile    = Path.Combine("data", "Fake.csv");
        int    maxPerClass = 50;
        int    delayMs     = 1500;  // 1.5s between calls — safe for Groq free tier (30 req/min)
        int    seed        = 42;
        string? outputCsv  = null;
        string? outputJson = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--api":   apiUrl   = args[++i]; break;
                case "--label": label    = args[++i]; break;
                case "--true":  trueFile = args[++i]; break;
                case "--fake":  fakeFile = args[++i]; break;
                case "--max":   maxPerClass = int.Parse(args[++i]); break;
                case "--delay": delayMs     = int.Parse(args[++i]); break;
                case "--seed":  seed        = int.Parse(args[++i]); break;
                case "--output-csv":  outputCsv  = args[++i]; break;
                case "--output-json": outputJson = args[++i]; break;
            }
        }

        return new Options(apiUrl, label, trueFile, fakeFile, maxPerClass, delayMs, seed, outputCsv, outputJson);
    }
}
