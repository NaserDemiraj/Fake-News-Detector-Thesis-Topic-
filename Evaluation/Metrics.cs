using System.Text.Json;
using System.Text.Json.Serialization;

namespace FakeNewsDetector.Evaluation;

public record EvaluationRow(
    int Index,
    string TitlePreview,
    string TrueLabel,
    string PredictedVerdict,
    double Score,
    double Confidence,
    long LatencyMs,
    bool IsError,
    string? ErrorMessage = null);

public sealed class MetricsResult
{
    public string BackendLabel   { get; set; } = "";
    public string Dataset        { get; set; } = "ISOT";
    public int    SamplesPerClass { get; set; }
    public string Timestamp      { get; set; } = DateTime.UtcNow.ToString("o");

    // Raw counts
    public int Total     { get; set; }
    public int Errors    { get; set; }
    public int Uncertain { get; set; }

    // Confusion matrix  (excludes uncertain + error rows)
    public int TP { get; set; } // predicted true,  actually true
    public int TN { get; set; } // predicted fake,  actually fake
    public int FP { get; set; } // predicted true,  actually fake  (false alarm)
    public int FN { get; set; } // predicted fake,  actually true  (missed)

    [JsonIgnore] public int Decided  => TP + TN + FP + FN;
    [JsonIgnore] public int TruePos  => TP + FN; // all actual positives
    [JsonIgnore] public int TrueNeg  => TN + FP; // all actual negatives

    // Derived metrics (computed properties — serialised via getter)
    public double Accuracy  => Decided > 0 ? (TP + TN) / (double)Decided : 0;
    public double Precision => (TP + FP) > 0 ? TP / (double)(TP + FP) : 0;
    public double Recall    => (TP + FN) > 0 ? TP / (double)(TP + FN) : 0;
    public double F1        => (Precision + Recall) > 0 ? 2 * Precision * Recall / (Precision + Recall) : 0;
    public double Specificity => (TN + FP) > 0 ? TN / (double)(TN + FP) : 0; // true-negative rate

    // Coverage = fraction of non-error samples where the model gave a definite verdict
    public double Coverage => (Total - Errors) > 0 ? Decided / (double)(Total - Errors) : 0;

    public double MeanLatencyMs { get; set; }

    // Calibration: list of (confidence bucket centre → fraction correct)
    public List<CalibrationBucket> Calibration { get; set; } = new();

    // -----------------------------------------------------------------------

    public static MetricsResult Compute(
        IReadOnlyList<EvaluationRow> rows,
        string label = "",
        string dataset = "ISOT",
        int samplesPerClass = 50)
    {
        var m = new MetricsResult
        {
            BackendLabel    = label,
            Dataset         = dataset,
            SamplesPerClass = samplesPerClass,
            Total           = rows.Count,
        };

        // Calibration buckets: 10 bins from 0–1 confidence
        const int bins = 10;
        var bucketCorrect = new int[bins];
        var bucketTotal   = new int[bins];

        var latencies = new List<long>(rows.Count);

        foreach (var row in rows)
        {
            if (row.IsError) { m.Errors++;    continue; }
            if (row.PredictedVerdict == "uncertain") { m.Uncertain++; continue; }

            latencies.Add(row.LatencyMs);

            bool predTrue   = row.PredictedVerdict == "likely_true";
            bool actualTrue = row.TrueLabel == "true";
            bool correct    = predTrue == actualTrue;

            if (predTrue && actualTrue)  m.TP++;
            else if (!predTrue && !actualTrue) m.TN++;
            else if (predTrue && !actualTrue)  m.FP++;
            else                               m.FN++;

            // Calibration bin
            int bin = Math.Min((int)(row.Confidence * bins), bins - 1);
            bucketTotal[bin]++;
            if (correct) bucketCorrect[bin]++;
        }

        m.MeanLatencyMs = latencies.Count > 0 ? latencies.Average() : 0;

        for (int i = 0; i < bins; i++)
        {
            if (bucketTotal[i] == 0) continue;
            m.Calibration.Add(new CalibrationBucket
            {
                ConfidenceLow   = i / (double)bins,
                ConfidenceHigh  = (i + 1) / (double)bins,
                Count           = bucketTotal[i],
                FractionCorrect = bucketCorrect[i] / (double)bucketTotal[i],
            });
        }

        return m;
    }

    public void PrintToConsole()
    {
        var w = 46;
        var line = new string('═', w);
        Console.WriteLine();
        Console.WriteLine($"╔{line}╗");
        Console.WriteLine($"║{"EVALUATION RESULTS".PadLeft((w + 18) / 2).PadRight(w)}║");
        Console.WriteLine($"╠{line}╣");
        Row("Backend",    BackendLabel);
        Row("Dataset",    $"{Dataset} ({SamplesPerClass}/class, {Total} total)");
        Console.WriteLine($"╠{line}╣");
        Row("Accuracy",   Pct(Accuracy));
        Row("Precision",  Pct(Precision));
        Row("Recall",     Pct(Recall));
        Row("F1 Score",   Pct(F1));
        Row("Specificity",Pct(Specificity));
        Row("Coverage",   $"{Pct(Coverage)}  ({Decided}/{Total - Errors} decided)");
        Row("Mean latency", $"{MeanLatencyMs / 1000.0:F2}s");
        Console.WriteLine($"╠{line}╣");
        Console.WriteLine($"║{"CONFUSION MATRIX".PadLeft((w + 16) / 2).PadRight(w)}║");
        Console.WriteLine($"║{"".PadRight(w)}║");
        Console.WriteLine($"║  {"".PadRight(22)}Pred TRUE   Pred FAKE║");
        Console.WriteLine($"║  {"Actual TRUE".PadRight(22)}{TP,8}    {FN,8}║");
        Console.WriteLine($"║  {"Actual FAKE".PadRight(22)}{FP,8}    {TN,8}║");
        Console.WriteLine($"║{"".PadRight(w)}║");
        Row("Uncertain predictions", Uncertain.ToString());
        Row("Errors",     Errors.ToString());
        Console.WriteLine($"╚{line}╝");
    }

    private static void Row(string label, string value)
    {
        const int w = 46;
        var line = $"  {label,-20}: {value}";
        Console.WriteLine($"║{line.PadRight(w)}║");
    }

    private static string Pct(double v) => $"{v * 100:F1}%";
}

public sealed class CalibrationBucket
{
    public double ConfidenceLow     { get; set; }
    public double ConfidenceHigh    { get; set; }
    public int    Count             { get; set; }
    public double FractionCorrect   { get; set; }
}
