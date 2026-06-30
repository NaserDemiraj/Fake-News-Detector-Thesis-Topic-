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
        Console.WriteLine($"  Retries      : {opts.MaxRetries} (on 429 / 5xx)");
        Console.WriteLine($"  Output CSV   : {opts.OutputCsv ?? "(auto-stamped)"}");
        Console.WriteLine($"  Output JSON  : {opts.OutputJson ?? "(auto-stamped)"}");
        if (opts.Resume) Console.WriteLine($"  Resume mode  : ON — skipping already-completed rows");
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

        // ── 3. Load existing rows (resume mode) ───────────────────────────
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var csvPath  = opts.OutputCsv  ?? $"results_{stamp}.csv";
        var jsonPath = opts.OutputJson ?? $"metrics_{stamp}.json";

        var rows = new List<EvaluationRow>(items.Count);
        var doneIndices = new HashSet<int>();

        if (opts.Resume && File.Exists(csvPath))
        {
            try
            {
                using var reader = new StreamReader(csvPath, System.Text.Encoding.UTF8);
                using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
                foreach (var rec in csv.GetRecords<dynamic>())
                {
                    double.TryParse(rec.Score?.ToString(),      out double sc);
                    double.TryParse(rec.Confidence?.ToString(), out double cf);
                    long.TryParse(rec.LatencyMs?.ToString(),    out long   ms);
                    bool.TryParse(rec.IsError?.ToString(),      out bool   ie);
                    var row = new EvaluationRow(
                        int.Parse(rec.Index),
                        rec.TitlePreview?.ToString() ?? "",
                        rec.TrueLabel?.ToString()    ?? "",
                        rec.PredictedVerdict?.ToString() ?? "error",
                        sc > 0 ? sc : 50,
                        cf,
                        ms,
                        ie,
                        rec.ErrorMessage?.ToString());
                    rows.Add(row);
                    doneIndices.Add(row.Index);
                }
                Console.WriteLine($"  Resumed: loaded {rows.Count} existing rows, skipping them.");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: could not load existing CSV for resume: {ex.Message}");
            }
        }

        // ── 4. Run predictions ─────────────────────────────────────────────
        var pending = items.Count - doneIndices.Count;
        Console.WriteLine($"  Running {pending} prediction(s)  ({doneIndices.Count} already done)…");
        Console.WriteLine();

        int idx = 0;

        foreach (var item in items)
        {
            idx++;
            if (doneIndices.Contains(idx)) continue;

            var content = string.IsNullOrEmpty(item.Title)
                ? item.Text
                : item.Title + "\n\n" + item.Text;

            var pred = await AnalyzeWithRetry(client, content, opts.MaxRetries, opts.DelayMs);

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

        // Sort by original index so CSV order is stable across resumes
        rows.Sort((a, b) => a.Index.CompareTo(b.Index));

        // ── 5. Compute metrics ─────────────────────────────────────────────
        var metrics = MetricsResult.Compute(rows, opts.Label, "ISOT", opts.MaxPerClass);
        metrics.PrintToConsole();

        // ── 6. Save outputs ────────────────────────────────────────────────
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

    // ── Retry wrapper ──────────────────────────────────────────────────────

    // Retries on rate-limit (429) and transient server errors (5xx).
    // Uses exponential backoff: 4s, 8s, 16s between attempts.
    private static async Task<PredictionResult> AnalyzeWithRetry(
        ApiClient client, string content, int maxRetries, int baseDelayMs)
    {
        PredictionResult pred = default!;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            pred = await client.AnalyzeAsync(content);
            if (!pred.IsError) return pred;

            var err = pred.ErrorMessage ?? "";
            var isRateLimit  = err.Contains("429");
            var isServerErr  = err.StartsWith("HTTP 5", StringComparison.Ordinal);
            if ((!isRateLimit && !isServerErr) || attempt == maxRetries) return pred;

            // Back off: 4s, 8s, 16s … (independent of the between-request delay)
            var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"         Retryable error ({(isRateLimit ? "429 rate-limit" : "5xx server")}) — backoff {backoff.TotalSeconds:F0}s (attempt {attempt}/{maxRetries})");
            Console.ResetColor();
            await Task.Delay(backoff);
        }
        return pred;
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

        var errMsg = row.ErrorMessage ?? "";
        var pred = row.IsError ? $"ERROR: {(errMsg.Length > 50 ? errMsg[^50..] : errMsg)}"
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
        int MaxRetries,
        bool Resume,
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
        int    maxRetries  = 3;
        bool   resume      = false;
        string? outputCsv  = null;
        string? outputJson = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--api":    apiUrl   = args[++i]; break;
                case "--label":  label    = args[++i]; break;
                case "--true":   trueFile = args[++i]; break;
                case "--fake":   fakeFile = args[++i]; break;
                case "--max":    maxPerClass = int.Parse(args[++i]); break;
                case "--delay":  delayMs     = int.Parse(args[++i]); break;
                case "--seed":   seed        = int.Parse(args[++i]); break;
                case "--retries": maxRetries = int.Parse(args[++i]); break;
                case "--resume": resume      = true; break;
                case "--output-csv":  outputCsv  = args[++i]; break;
                case "--output-json": outputJson = args[++i]; break;
            }
        }

        return new Options(apiUrl, label, trueFile, fakeFile, maxPerClass, delayMs, seed, maxRetries, resume, outputCsv, outputJson);
    }
}
