using FakeNewsDetector.Models;
using System.Text.Json;

namespace FakeNewsDetector.Services;

/// <summary>
/// Parses the raw JSON string returned by the AI API into an <see cref="AnalysisResult"/>.
/// Isolated here so it can be unit-tested independently of the HTTP plumbing in NewsAnalyzerService.
/// </summary>
public static class AnalysisResultParser
{
    public static AnalysisResult Parse(string rawResponse)
    {
        try
        {
            var json = ExtractJson(rawResponse);
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new AnalysisResult { Success = true, Verdict = "uncertain" };

            if (root.TryGetProperty("verdict",       out var v))   result.Verdict      = v.GetString() ?? "uncertain";
            if (root.TryGetProperty("score",         out var s) && s.ValueKind == JsonValueKind.Number) result.Score = s.GetDouble();
            if (root.TryGetProperty("confidence",    out var c) && c.ValueKind == JsonValueKind.Number) result.Confidence = c.GetDouble();
            if (root.TryGetProperty("summary",       out var sum)) result.Summary      = sum.GetString() ?? "";
            if (root.TryGetProperty("explanation",   out var exp)) result.Explanation  = exp.GetString() ?? "";
            if (root.TryGetProperty("reasoning",     out var r))   result.Reasoning    = r.GetString();
            if (root.TryGetProperty("language",      out var lg))  result.Language     = lg.GetString() ?? "en";
            if (root.TryGetProperty("language_name", out var ln))  result.LanguageName = ln.GetString() ?? "English";

            result.CredibilitySignals = ExtractStringList(root, "credibility_signals");
            result.RedFlags           = ExtractStringList(root, "red_flags");

            if (root.TryGetProperty("highlighted_sentences", out var hs))
            {
                result.HighlightedSentences = hs.EnumerateArray().Select(h => new SentenceHighlight
                {
                    Flag     = h.TryGetProperty("flag",     out var f) ? f.GetString() ?? "" : "",
                    Sentence = h.TryGetProperty("sentence", out var se) ? se.GetString() ?? "" : "",
                    Reason   = h.TryGetProperty("reason",   out var re) ? re.GetString() ?? "" : "",
                }).Where(h => !string.IsNullOrEmpty(h.Sentence)).ToList();
            }

            if (root.TryGetProperty("bias_detection", out var bias))
            {
                var bd = new BiasDetection();
                if (bias.TryGetProperty("emotional_language_score", out var el) && el.ValueKind == JsonValueKind.Number)
                    bd.EmotionalLanguageScore = (int)el.GetDouble();
                if (bias.TryGetProperty("fear_mongering", out var fm)) bd.FearMongering = fm.GetBoolean();
                if (bias.TryGetProperty("political_bias", out var pb)) bd.PoliticalBias = pb.GetString() ?? "neutral";
                if (bias.TryGetProperty("clarity",        out var cl)) bd.Clarity       = cl.GetString() ?? "Clear";
                bd.ManipulationTactics = ExtractStringList(bias, "manipulation_tactics");
                result.BiasDetection = bd;
            }

            if (root.TryGetProperty("factors", out var factors))
            {
                result.Factors = factors.EnumerateArray().Select(f => new AnalysisFactor
                {
                    Name    = f.TryGetProperty("name",    out var n)  ? n.GetString() ?? "" : "",
                    Score   = f.TryGetProperty("score",   out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 50,
                    Details = f.TryGetProperty("details", out var d)  ? d.GetString() : null
                }).ToList();
            }

            if (root.TryGetProperty("evidence_points", out var ep))
            {
                result.EvidencePoints = ep.EnumerateArray().Select(e => new EvidencePoint
                {
                    Text   = e.TryGetProperty("text",   out var t)  ? t.GetString() ?? "" : "",
                    Status = e.TryGetProperty("status", out var st) ? st.GetString() ?? "unverified" : "unverified"
                }).ToList();
            }

            if (root.TryGetProperty("claims", out var claims))
            {
                result.Claims = claims.EnumerateArray().Select(cl => new Claim
                {
                    Text    = cl.TryGetProperty("text",   out var t)  ? t.GetString() ?? "" : "",
                    Status  = cl.TryGetProperty("status", out var st) ? st.GetString() ?? "unverified" : "unverified",
                    Sources = cl.TryGetProperty("sources", out var src)
                        ? src.EnumerateArray()
                            .Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() : null)
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList()!
                        : new List<string>()
                }).ToList();
            }

            return result;
        }
        catch
        {
            return new AnalysisResult
            {
                Success     = false,
                Score       = 50,
                Verdict     = "uncertain",
                Explanation = "The AI response could not be parsed. Please try again.",
                Confidence  = 0.0,
                Factors     = new List<AnalysisFactor> { new() { Name = "Parse Error", Score = 50 } },
                BiasDetection = new BiasDetection()
            };
        }
    }

    // Strips markdown code-fences that some LLMs add despite instructions
    public static string ExtractJson(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        else if (text.StartsWith("```")) text = text[3..];
        if (text.EndsWith("```")) text = text[..^3];
        text = text.Trim();

        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        return text;
    }

    private static List<string> ExtractStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arr)) return new List<string>();
        return arr.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.TryGetProperty("text", out var t) ? t.GetString() : null)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList()!;
    }
}
