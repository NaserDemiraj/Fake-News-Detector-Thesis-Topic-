using System.Text.Json;

namespace FakeNewsDetector.Evaluation;

/// <summary>
/// Reads 2-4 metrics JSON files produced by EvaluationRunner and prints a
/// side-by-side comparison table — ready to paste into a thesis results chapter.
/// </summary>
public static class CompareMode
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(string[] files)
    {
        if (files.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run -- compare metrics_groq.json metrics_gemini.json [metrics_ollama.json]");
            return;
        }

        var results = new List<MetricsResult>();
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"File not found: {file}");
                Console.ResetColor();
                continue;
            }

            var json = await File.ReadAllTextAsync(file);
            var m = JsonSerializer.Deserialize<MetricsResult>(json, JsonOpts);
            if (m != null) results.Add(m);
        }

        if (results.Count == 0)
        {
            Console.WriteLine("No valid metrics files loaded.");
            return;
        }

        // ── Print comparison table ──────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║               BACKEND COMPARISON RESULTS                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Header
        var col = 18;
        var header = "Metric".PadRight(18);
        foreach (var m in results)
            header += (m.BackendLabel.Length > col ? m.BackendLabel[..col] : m.BackendLabel).PadLeft(col);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  " + header);
        Console.ResetColor();
        Console.WriteLine("  " + new string('─', 18 + results.Count * col));

        PrintRow("Accuracy",    results, col, m => $"{m.Accuracy * 100:F1}%");
        PrintRow("Precision",   results, col, m => $"{m.Precision * 100:F1}%");
        PrintRow("Recall",      results, col, m => $"{m.Recall * 100:F1}%");
        PrintRow("F1 Score",    results, col, m => $"{m.F1 * 100:F1}%");
        PrintRow("Specificity", results, col, m => $"{m.Specificity * 100:F1}%");
        PrintRow("Coverage",    results, col, m => $"{m.Coverage * 100:F1}%");
        PrintRow("Mean latency",results, col, m => $"{m.MeanLatencyMs / 1000.0:F2}s");
        PrintRow("Uncertain %", results, col, m => $"{(double)m.Uncertain / m.Total * 100:F1}%");
        PrintRow("Errors",      results, col, m => m.Errors.ToString());
        PrintRow("N (total)",   results, col, m => m.Total.ToString());

        Console.WriteLine();

        // Highlight best F1
        var bestF1 = results.MaxBy(m => m.F1);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Best F1: {bestF1?.BackendLabel} ({bestF1?.F1 * 100:F1}%)");

        var fastestDecided = results.MinBy(m => m.MeanLatencyMs);
        Console.WriteLine($"  Fastest: {fastestDecided?.BackendLabel} ({fastestDecided?.MeanLatencyMs / 1000.0:F2}s/article)");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintRow(string label, List<MetricsResult> results, int col, Func<MetricsResult, string> fn)
    {
        var line = label.PadRight(18);
        foreach (var m in results)
            line += fn(m).PadLeft(col);
        Console.WriteLine("  " + line);
    }
}
