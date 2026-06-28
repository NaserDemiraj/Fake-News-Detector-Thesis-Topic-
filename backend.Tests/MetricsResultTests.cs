using FakeNewsDetector.Evaluation;
using Xunit;

namespace FakeNewsDetector.Tests;

/// <summary>
/// Verifies the confusion-matrix math and derived metrics in MetricsResult.
/// </summary>
public class MetricsResultTests
{
    private static EvaluationRow Row(string trueLabel, string predicted, bool error = false)
        => new(0, "", trueLabel, predicted, 80, 0.8, 100, error);

    [Fact]
    public void PerfectClassifier_AllMetrics100Pct()
    {
        var rows = Enumerable.Repeat(Row("true", "likely_true"), 50)
            .Concat(Enumerable.Repeat(Row("fake", "likely_fake"), 50))
            .ToList();

        var m = MetricsResult.Compute(rows);

        Assert.Equal(1.0, m.Accuracy,   precision: 3);
        Assert.Equal(1.0, m.Precision,  precision: 3);
        Assert.Equal(1.0, m.Recall,     precision: 3);
        Assert.Equal(1.0, m.F1,         precision: 3);
        Assert.Equal(1.0, m.Specificity,precision: 3);
        Assert.Equal(0, m.Uncertain);
        Assert.Equal(0, m.Errors);
    }

    [Fact]
    public void AllWrong_ZeroAccuracy()
    {
        var rows = Enumerable.Repeat(Row("true", "likely_fake"), 10)
            .Concat(Enumerable.Repeat(Row("fake", "likely_true"), 10))
            .ToList();

        var m = MetricsResult.Compute(rows);

        Assert.Equal(0.0, m.Accuracy,  precision: 3);
        Assert.Equal(10,  m.FP);
        Assert.Equal(10,  m.FN);
        Assert.Equal(0,   m.TP);
        Assert.Equal(0,   m.TN);
    }

    [Fact]
    public void UncertainPredictions_ExcludedFromConfusionMatrix()
    {
        var rows = new List<EvaluationRow>
        {
            Row("true", "likely_true"),
            Row("fake", "likely_fake"),
            Row("true", "uncertain"),
            Row("fake", "uncertain"),
        };

        var m = MetricsResult.Compute(rows);

        Assert.Equal(1, m.TP);
        Assert.Equal(1, m.TN);
        Assert.Equal(0, m.FP);
        Assert.Equal(0, m.FN);
        Assert.Equal(2, m.Uncertain);
        Assert.Equal(0.5, m.Coverage, precision: 3); // 2 decided / 4 non-error
    }

    [Fact]
    public void ErrorRows_ExcludedFromAllMetrics()
    {
        var rows = new List<EvaluationRow>
        {
            Row("true", "likely_true"),
            Row("fake", "error", error: true),
        };

        var m = MetricsResult.Compute(rows);

        Assert.Equal(1, m.Errors);
        Assert.Equal(1, m.Decided);
        Assert.Equal(1.0, m.Accuracy, precision: 3);
    }

    [Fact]
    public void Coverage_CorrectlyExcludesUncertainAndErrors()
    {
        var rows = Enumerable.Repeat(Row("true", "likely_true"), 8)
            .Concat(Enumerable.Repeat(Row("true", "uncertain"), 1))
            .Concat(new[] { Row("fake", "error", error: true) })
            .ToList();

        var m = MetricsResult.Compute(rows);

        // Coverage = decided / (total - errors) = 8 / 9
        Assert.Equal(8.0 / 9.0, m.Coverage, precision: 4);
    }

    [Fact]
    public void F1_ZeroWhenNoPredictedPositives()
    {
        var rows = Enumerable.Repeat(Row("true", "likely_fake"), 5).ToList();
        var m = MetricsResult.Compute(rows);
        Assert.Equal(0.0, m.F1, precision: 3);
    }

    [Fact]
    public void CalibrationBuckets_HighConfidenceCorrect_HighFractionCorrect()
    {
        // 10 rows all with confidence 0.95, all correct
        var rows = Enumerable.Range(0, 10)
            .Select(_ => new EvaluationRow(0, "", "true", "likely_true", 90, 0.95, 100, false))
            .ToList();

        var m = MetricsResult.Compute(rows);

        // All should land in the 0.9–1.0 bucket
        Assert.Single(m.Calibration);
        Assert.Equal(1.0, m.Calibration[0].FractionCorrect, precision: 3);
    }
}
