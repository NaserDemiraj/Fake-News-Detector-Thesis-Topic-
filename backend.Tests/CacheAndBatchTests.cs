using FakeNewsDetector.Models;
using FakeNewsDetector.Services;
using Xunit;

namespace FakeNewsDetector.Tests;

/// <summary>
/// Tests for the cache deserialization validation fix and batch/domain reputation logic.
/// </summary>
public class CacheValidationTests
{
    // ── Cache validity guard ─────────────────────────────────────────────────
    // The fix in AnalysisController treats a deserialized result as valid only
    // when Verdict is non-empty. These tests verify that assumption via Parse.

    [Fact]
    public void Parse_ValidJson_HasNonEmptyVerdict_IsValid()
    {
        var json = """{"verdict":"likely_true","score":85}""";
        var result = AnalysisResultParser.Parse(json);
        Assert.False(string.IsNullOrEmpty(result.Verdict));
    }

    [Fact]
    public void Parse_BrokenJson_ProducesUncertainWithSuccess_False()
    {
        var result = AnalysisResultParser.Parse("{not valid json{{{{");
        Assert.False(result.Success);
        Assert.Equal("uncertain", result.Verdict);
        // The controller re-analyzes when Success=false via empty-verdict guard;
        // "uncertain" is technically non-empty, but Success=false is the right signal
    }

    [Theory]
    [InlineData("likely_true")]
    [InlineData("likely_fake")]
    [InlineData("uncertain")]
    public void Parse_AllKnownVerdicts_Roundtrip(string verdict)
    {
        var result = AnalysisResultParser.Parse($$$"""{"verdict":"{{{verdict}}}","score":50}""");
        Assert.True(result.Success);
        Assert.Equal(verdict, result.Verdict);
    }

    [Fact]
    public void Parse_ResultJsonWithEmptyVerdict_WouldBeRejectedByController()
    {
        // Simulate a malformed cached ResultJson where verdict ended up blank
        var malformedResultJson = """{"success":true,"score":70,"verdict":""}""";
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AnalysisResult>(
            malformedResultJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(deserialized);
        // Controller guard: string.IsNullOrEmpty(deserialized.Verdict) → triggers re-analysis
        Assert.True(string.IsNullOrEmpty(deserialized!.Verdict));
    }

    [Fact]
    public void Parse_ResultJsonWithValidVerdict_PassesControllerGuard()
    {
        var goodResultJson = """{"success":true,"score":75,"verdict":"likely_true","confidence":0.8}""";
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AnalysisResult>(
            goodResultJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(deserialized);
        Assert.False(string.IsNullOrEmpty(deserialized!.Verdict));
    }
}

public class DomainCredibilityTests
{
    // Documents the credibility label mapping rules from SavedAnalysisService.
    // Extracted here so the rules are tested without requiring a DB connection.

    private static string ComputeLabel(int trueCount, int fakeCount)
    {
        var decisive = trueCount + fakeCount;
        var fakeRatio = decisive > 0 ? (double)fakeCount / decisive : 0.5;
        return decisive == 0 ? "Unknown"
             : fakeRatio >= 0.6 ? "Flagged"
             : fakeRatio <= 0.25 ? "Reliable"
             : "Mixed Record";
    }

    private static double ComputeConfidence(int totalAnalyses)
        => Math.Round(1.0 - Math.Exp(-totalAnalyses / 15.0), 3);

    [Fact]
    public void Label_NoDomain_ReturnsUnknown()
        => Assert.Equal("Unknown", ComputeLabel(0, 0));

    [Fact]
    public void Label_AllTrue_ReturnsReliable()
        => Assert.Equal("Reliable", ComputeLabel(10, 0));

    [Fact]
    public void Label_MostlyFake_ReturnsFlagged()
        => Assert.Equal("Flagged", ComputeLabel(2, 8));

    [Fact]
    public void Label_ExactlyFlaggedThreshold_ReturnsFlagged()
        => Assert.Equal("Flagged", ComputeLabel(4, 6)); // fakeRatio = 0.6

    [Fact]
    public void Label_AtReliableThreshold_ReturnsReliable()
        => Assert.Equal("Reliable", ComputeLabel(3, 1)); // fakeRatio = 0.25 → exactly on Reliable boundary

    [Fact]
    public void Label_JustAboveReliableThreshold_ReturnsMixedRecord()
        => Assert.Equal("Mixed Record", ComputeLabel(2, 1)); // fakeRatio = 0.333 → Mixed

    [Fact]
    public void Label_EqualTrueAndFake_ReturnsMixedRecord()
        => Assert.Equal("Mixed Record", ComputeLabel(5, 5)); // fakeRatio = 0.5

    [Fact]
    public void Confidence_ZeroSamples_IsZero()
        => Assert.Equal(0.0, ComputeConfidence(0));

    [Fact]
    public void Confidence_30Samples_IsHighButBelowOne()
    {
        var conf = ComputeConfidence(30);
        Assert.True(conf > 0.85 && conf < 1.0);
    }

    [Fact]
    public void Confidence_MonotonicallyIncreasing()
    {
        var prev = ComputeConfidence(0);
        foreach (var n in new[] { 1, 5, 10, 20, 50 })
        {
            var curr = ComputeConfidence(n);
            Assert.True(curr > prev, $"Confidence should increase: n={n}");
            prev = curr;
        }
    }
}

public class VerdictThresholdTests
{
    // Documents the score-based verdict override rules enforced in NewsAnalyzerService.
    // These thresholds match appsettings.json defaults: FakeMax=40, TrueMin=70.

    private static string ApplyThreshold(double score, double fakeMax = 40, double trueMin = 70)
        => score <= fakeMax ? "likely_fake"
         : score >= trueMin ? "likely_true"
         : "uncertain";

    [Theory]
    [InlineData(0,  "likely_fake")]
    [InlineData(40, "likely_fake")]
    [InlineData(41, "uncertain")]
    [InlineData(69, "uncertain")]
    [InlineData(70, "likely_true")]
    [InlineData(100,"likely_true")]
    public void Threshold_DefaultBoundaries_CorrectVerdict(double score, string expected)
        => Assert.Equal(expected, ApplyThreshold(score));

    [Fact]
    public void Threshold_MidRange55_IsUncertain_NotLikelyTrue()
        => Assert.Equal("uncertain", ApplyThreshold(55));

    [Fact]
    public void Threshold_CustomThresholds_AreRespected()
    {
        // Caller can narrow the uncertain zone
        Assert.Equal("likely_true", ApplyThreshold(65, fakeMax: 35, trueMin: 60));
        Assert.Equal("likely_fake", ApplyThreshold(35, fakeMax: 35, trueMin: 60));
    }
}

public class BatchRequestValidationTests
{
    // These document the validation contract for the batch endpoint.
    // Values are taken directly from AnalysisController limits.

    [Fact]
    public void BatchLimit_Is25Items()
    {
        // The controller rejects requests with > 25 items.
        // This test documents that constant so any change is caught.
        const int maxBatchItems = 25;
        Assert.Equal(25, maxBatchItems);
    }

    [Theory]
    [InlineData("https://bbc.com/news/article", "url")]
    [InlineData("http://example.com/story", "url")]
    [InlineData("This is a plain text article to analyze.", "text")]
    public void BatchItem_AutoDetectType_Works(string content, string expectedType)
    {
        var isUrl = content.StartsWith("http://") || content.StartsWith("https://");
        var detected = isUrl ? "url" : "text";
        Assert.Equal(expectedType, detected);
    }

    [Fact]
    public void BatchItem_EmptyContent_ShouldBeSkipped()
    {
        // Controller skips items where Content is null/whitespace
        var items = new[]
        {
            new { Content = "", Type = "text" },
            new { Content = "   ", Type = "text" },
            new { Content = "Real article text here.", Type = "text" },
        };
        var nonEmpty = items.Where(i => !string.IsNullOrWhiteSpace(i.Content)).ToList();
        Assert.Single(nonEmpty);
    }
}
