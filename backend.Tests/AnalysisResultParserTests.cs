using FakeNewsDetector.Services;
using Xunit;

namespace FakeNewsDetector.Tests;

public class AnalysisResultParserTests
{
    // ── ExtractJson ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractJson_PlainJson_ReturnedAsIs()
    {
        var json = """{"verdict":"likely_true","score":80}""";
        Assert.Equal(json, AnalysisResultParser.ExtractJson(json));
    }

    [Fact]
    public void ExtractJson_StripsMdFence()
    {
        var wrapped = "```json\n{\"verdict\":\"likely_fake\"}\n```";
        var result  = AnalysisResultParser.ExtractJson(wrapped);
        Assert.Equal("{\"verdict\":\"likely_fake\"}", result);
    }

    [Fact]
    public void ExtractJson_StripsBareBacktickFence()
    {
        var wrapped = "```\n{\"score\":42}\n```";
        Assert.Equal("{\"score\":42}", AnalysisResultParser.ExtractJson(wrapped));
    }

    [Fact]
    public void ExtractJson_ExtraLeadingText_ExtractsBraces()
    {
        var messy = "Here is the JSON: {\"verdict\":\"uncertain\"}. Done.";
        Assert.Equal("{\"verdict\":\"uncertain\"}", AnalysisResultParser.ExtractJson(messy));
    }

    // ── Parse — happy path ──────────────────────────────────────────────────

    [Fact]
    public void Parse_MinimalJson_SetsVerdictAndScore()
    {
        var json = """{"verdict":"likely_true","score":85}""";
        var result = AnalysisResultParser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal("likely_true", result.Verdict);
        Assert.Equal(85, result.Score);
    }

    [Fact]
    public void Parse_FullJson_MapsAllTopLevelFields()
    {
        var json = """
        {
          "verdict": "likely_fake",
          "score": 22,
          "confidence": 0.91,
          "explanation": "Clear misinformation.",
          "red_flags": ["⚠ Sensational language", "⚠ No sources"],
          "credibility_signals": ["✓ Dateline present"]
        }
        """;

        var r = AnalysisResultParser.Parse(json);

        Assert.True(r.Success);
        Assert.Equal("likely_fake", r.Verdict);
        Assert.Equal(22, r.Score);
        Assert.Equal(0.91, r.Confidence, precision: 2);
        Assert.Equal("Clear misinformation.", r.Explanation);
        Assert.Equal(2, r.RedFlags.Count);
        Assert.Single(r.CredibilitySignals);
    }

    [Fact]
    public void Parse_BiasDetectionBlock_Parsed()
    {
        var json = """
        {
          "verdict": "uncertain",
          "score": 55,
          "bias_detection": {
            "emotional_language_score": 70,
            "fear_mongering": true,
            "political_bias": "right",
            "clarity": "Deliberately vague",
            "manipulation_tactics": ["fear_appeal"]
          }
        }
        """;

        var r = AnalysisResultParser.Parse(json);

        Assert.NotNull(r.BiasDetection);
        Assert.Equal(70, r.BiasDetection!.EmotionalLanguageScore);
        Assert.True(r.BiasDetection.FearMongering);
        Assert.Equal("right", r.BiasDetection.PoliticalBias);
        Assert.Equal("Deliberately vague", r.BiasDetection.Clarity);
        Assert.Single(r.BiasDetection.ManipulationTactics);
    }

    [Fact]
    public void Parse_FactorsArray_Parsed()
    {
        var json = """
        {
          "verdict": "likely_true",
          "score": 78,
          "factors": [
            {"name": "Source Credibility", "score": 90, "details": "Reuters cited"},
            {"name": "Factual Accuracy",   "score": 75, "details": "Verified claims"}
          ]
        }
        """;

        var r = AnalysisResultParser.Parse(json);

        Assert.Equal(2, r.Factors.Count);
        Assert.Equal("Source Credibility", r.Factors[0].Name);
        Assert.Equal(90, r.Factors[0].Score);
    }

    [Fact]
    public void Parse_WrappedInMarkdownFences_StillParsed()
    {
        var fenced = "```json\n{\"verdict\":\"likely_fake\",\"score\":12}\n```";
        var r = AnalysisResultParser.Parse(fenced);

        Assert.True(r.Success);
        Assert.Equal("likely_fake", r.Verdict);
        Assert.Equal(12, r.Score);
    }

    // ── Parse — error handling ──────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ReturnsUncertainFallback()
    {
        var r = AnalysisResultParser.Parse("");
        Assert.False(r.Success);
        Assert.Equal("uncertain", r.Verdict);
        Assert.Equal(0.0, r.Confidence);
    }

    [Fact]
    public void Parse_BrokenJson_ReturnsUncertainFallback()
    {
        var r = AnalysisResultParser.Parse("{verdict: broken json <<<");
        Assert.False(r.Success);
        Assert.Equal("uncertain", r.Verdict);
    }

    [Fact]
    public void Parse_MissingVerdict_DefaultsToUncertain()
    {
        var r = AnalysisResultParser.Parse("""{"score":60}""");
        Assert.True(r.Success);
        Assert.Equal("uncertain", r.Verdict);
    }

    [Fact]
    public void Parse_MissingScore_DefaultsToZero()
    {
        var r = AnalysisResultParser.Parse("""{"verdict":"likely_true"}""");
        Assert.True(r.Success);
        Assert.Equal(0, r.Score);
    }

    // ── Sentence highlights ─────────────────────────────────────────────────

    [Fact]
    public void Parse_HighlightedSentences_Parsed()
    {
        var json = """
        {
          "verdict": "likely_fake",
          "score": 18,
          "highlighted_sentences": [
            {"flag": "⚠ Sensational language", "sentence": "This SHOCKING truth will DESTROY everything!", "reason": "All-caps sensationalism"},
            {"flag": "⚠ No sources cited",     "sentence": "Sources say the government is hiding this.", "reason": "Anonymous unnamed sources"}
          ]
        }
        """;

        var r = AnalysisResultParser.Parse(json);

        Assert.Equal(2, r.HighlightedSentences.Count);
        Assert.Equal("⚠ Sensational language", r.HighlightedSentences[0].Flag);
        Assert.Contains("SHOCKING", r.HighlightedSentences[0].Sentence);
        Assert.Equal("All-caps sensationalism", r.HighlightedSentences[0].Reason);
    }

    [Fact]
    public void Parse_HighlightedSentences_EmptySentenceFiltered()
    {
        var json = """
        {
          "verdict": "likely_fake",
          "score": 20,
          "highlighted_sentences": [
            {"flag": "⚠ Flag", "sentence": "", "reason": "reason"},
            {"flag": "⚠ Flag 2", "sentence": "Real excerpt from the article.", "reason": "legit reason"}
          ]
        }
        """;

        var r = AnalysisResultParser.Parse(json);
        Assert.Single(r.HighlightedSentences);
        Assert.Equal("Real excerpt from the article.", r.HighlightedSentences[0].Sentence);
    }

    [Fact]
    public void Parse_MissingHighlights_ReturnsEmptyList()
    {
        var r = AnalysisResultParser.Parse("""{"verdict":"likely_true","score":80}""");
        Assert.Empty(r.HighlightedSentences);
    }

    // ── Language detection ──────────────────────────────────────────────────

    [Fact]
    public void Parse_LanguageFields_Parsed()
    {
        var json = """{"verdict":"likely_fake","score":30,"language":"sq","language_name":"Albanian"}""";
        var r = AnalysisResultParser.Parse(json);
        Assert.Equal("sq", r.Language);
        Assert.Equal("Albanian", r.LanguageName);
    }

    [Fact]
    public void Parse_MissingLanguage_DefaultsToEnglish()
    {
        var r = AnalysisResultParser.Parse("""{"verdict":"likely_true","score":80}""");
        Assert.Equal("en", r.Language);
        Assert.Equal("English", r.LanguageName);
    }
}
