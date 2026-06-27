using FakeNewsDetector.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FakeNewsDetector.Services
{
    public class NewsAnalyzerService : INewsAnalyzerService
    {
        private readonly ILogger<NewsAnalyzerService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _apiUrl;

        public NewsAnalyzerService(ILogger<NewsAnalyzerService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;

            var ollamaEnabled = configuration.GetValue<bool>("Ollama:Enabled");
            var groqKey = configuration["Groq:ApiKey"] ?? "";
            var geminiKey = configuration["Gemini:ApiKey"] ?? "";

            if (ollamaEnabled)
            {
                var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
                _apiKey = "ollama";
                _model = configuration["Ollama:Model"] ?? "llama3.2";
                _apiUrl = $"{baseUrl}/v1/chat/completions";
                _logger.LogInformation("Using Ollama ({Model}) at {Url}", _model, _apiUrl);
            }
            else if (!string.IsNullOrEmpty(groqKey) && !groqKey.StartsWith("SET_VIA"))
            {
                _apiKey = groqKey;
                _model = configuration["Groq:Model"] ?? "llama-3.1-8b-instant";
                _apiUrl = "https://api.groq.com/openai/v1/chat/completions";
                _logger.LogInformation("Using Groq AI ({Model})", _model);
            }
            else if (!string.IsNullOrEmpty(geminiKey) && !geminiKey.StartsWith("SET_VIA"))
            {
                _apiKey = geminiKey;
                _model = configuration["Gemini:Model"] ?? "gemini-2.0-flash";
                _apiUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
                _logger.LogInformation("Using Gemini AI ({Model})", _model);
            }
            else
            {
                _apiKey = "";
                _model = "";
                _apiUrl = "";
            }
        }

        public async Task<AnalysisResult> AnalyzeContentAsync(string content)
        {
            _logger.LogInformation("Analyzing content, length: {Length}", content.Length);

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("No AI API key configured — using mock analysis");
                return MockAnalysis();
            }

            try
            {
                var json = await CallAIWithRetryAsync(content);
                var result = ParseGroqResponse(json, content);
                _logger.LogInformation("Analysis complete. Verdict: {Verdict}, Score: {Score}", result.Verdict, result.Score);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI API failed after retries — falling back to mock");
                return MockAnalysis();
            }
        }

        private async Task<string> CallAIWithRetryAsync(string content, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await CallAIApiAsync(content);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogWarning("AI API attempt {Attempt} failed: {Message}. Retrying in {Delay}s...", attempt, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
            return await CallAIApiAsync(content);
        }

        private async Task<string> CallAIApiAsync(string content)
        {
            const int maxContentLength = 5000;
            var truncated = content.Length > maxContentLength
                ? content.Substring(0, maxContentLength) + "\n\n[... content truncated ...]"
                : content;

            var apiUrl = _apiUrl;

            var prompt = $@"Analyze this news content. Reply ONLY with JSON, no extra text.

CONTENT: {truncated}

JSON:
{{""verdict"":""likely_true""|""likely_fake""|""uncertain"",""score"":0-100,""explanation"":""1-2 sentences"",""red_flags"":[""⚠ short flag""],""credibility_signals"":[""✓ short signal""],""bias_detection"":{{""emotional_language_score"":0-100,""fear_mongering"":false,""political_bias"":""neutral""|""left""|""right""|""mixed"",""manipulation_tactics"":[],""clarity"":""word""}},""factors"":[{{""name"":""short"",""score"":0-100,""details"":""brief""}}],""evidence_points"":[{{""text"":""brief"",""status"":""verified""|""warning""|""unverified""}}]}}";

            var requestPayload = new
            {
                model = _model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.1,
                max_tokens = 600,
                response_format = new { type = "json_object" }
            };

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(3);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await httpClient.PostAsJsonAsync(apiUrl, requestPayload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Groq API error {Status}: {Error}", response.StatusCode, error);
                throw new Exception($"Groq API error: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseJson = JsonDocument.Parse(responseContent);

            return responseJson.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";
        }

        // Strip markdown fences that LLMs sometimes add despite instructions
        private static string ExtractJson(string raw)
        {
            var text = raw.Trim();

            if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(7);
            else if (text.StartsWith("```"))
                text = text.Substring(3);

            if (text.EndsWith("```"))
                text = text.Substring(0, text.Length - 3);

            text = text.Trim();

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
                return text.Substring(start, end - start + 1);

            return text;
        }

        private AnalysisResult ParseGroqResponse(string rawResponse, string originalContent)
        {
            try
            {
                var json = ExtractJson(rawResponse);
                _logger.LogDebug("Parsing response: {Json}", json.Substring(0, Math.Min(200, json.Length)));

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var result = new AnalysisResult { Success = true };

                if (root.TryGetProperty("verdict", out var v)) result.Verdict = v.GetString() ?? "uncertain";
                if (root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number) result.Score = s.GetDouble();
                if (root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number) result.Confidence = c.GetDouble();
                if (root.TryGetProperty("summary", out var sum)) result.Summary = sum.GetString() ?? "";
                if (root.TryGetProperty("explanation", out var exp)) result.Explanation = exp.GetString() ?? "";
                if (root.TryGetProperty("reasoning", out var r)) result.Reasoning = r.GetString();

                result.CredibilitySignals = ExtractStringList(root, "credibility_signals");
                result.RedFlags = ExtractStringList(root, "red_flags");

                if (root.TryGetProperty("bias_detection", out var bias))
                {
                    var bd = new BiasDetection();
                    if (bias.TryGetProperty("emotional_language_score", out var el) && el.ValueKind == JsonValueKind.Number)
                        bd.EmotionalLanguageScore = (int)el.GetDouble();
                    if (bias.TryGetProperty("fear_mongering", out var fm)) bd.FearMongering = fm.GetBoolean();
                    if (bias.TryGetProperty("political_bias", out var pb)) bd.PoliticalBias = pb.GetString() ?? "neutral";
                    if (bias.TryGetProperty("clarity", out var cl)) bd.Clarity = cl.GetString() ?? "Clear";
                    bd.ManipulationTactics = ExtractStringList(bias, "manipulation_tactics");
                    result.BiasDetection = bd;
                }

                if (root.TryGetProperty("factors", out var factors))
                {
                    result.Factors = factors.EnumerateArray().Select(f => new AnalysisFactor
                    {
                        Name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Score = f.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 50,
                        Details = f.TryGetProperty("details", out var d) ? d.GetString() : null
                    }).ToList();
                }

                if (root.TryGetProperty("evidence_points", out var ep))
                {
                    result.EvidencePoints = ep.EnumerateArray().Select(e => new EvidencePoint
                    {
                        Text = e.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                        Status = e.TryGetProperty("status", out var st) ? st.GetString() ?? "unverified" : "unverified"
                    }).ToList();
                }

                if (root.TryGetProperty("claims", out var claims))
                {
                    result.Claims = claims.EnumerateArray().Select(cl => new Claim
                    {
                        Text = cl.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                        Status = cl.TryGetProperty("status", out var st) ? st.GetString() ?? "unverified" : "unverified",
                        Sources = cl.TryGetProperty("sources", out var src)
                            ? src.EnumerateArray()
                                .Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() : null)
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList()!
                            : new List<string>()
                    }).ToList();
                }

                if (root.TryGetProperty("risk_categories", out var risks))
                {
                    result.RiskCategories = risks.EnumerateArray().Select(rk => new RiskCategory
                    {
                        Name = rk.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Score = rk.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 50,
                        Label = rk.TryGetProperty("label", out var lb) ? lb.GetString() ?? "Medium" : "Medium"
                    }).ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Groq response");
                return new AnalysisResult
                {
                    Success = false,
                    Score = 50,
                    Verdict = "uncertain",
                    Summary = "Could not parse AI response",
                    Explanation = "The AI response could not be parsed. Please try again.",
                    Confidence = 0.0,
                    Factors = new List<AnalysisFactor> { new() { Name = "Parse Error", Score = 50 } },
                    BiasDetection = new BiasDetection()
                };
            }
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

        private static AnalysisResult MockAnalysis() => new()
        {
            Success = true,
            Verdict = "likely_fake",
            Score = 35,
            Confidence = 0.72,
            Summary = "This content shows multiple warning signs of misinformation.",
            Explanation = "The content uses conspiracy framing and makes extraordinary claims without credible evidence. " +
                          "This is a mock result — configure a Groq API key for real AI analysis.",
            CredibilitySignals = new List<string>(),
            RedFlags = new List<string>
            {
                "⚠ Extraordinary claims without credible evidence",
                "⚠ Appeal to conspiracy theories",
                "⚠ Anti-establishment framing"
            },
            BiasDetection = new BiasDetection
            {
                EmotionalLanguageScore = 78,
                FearMongering = true,
                PoliticalBias = "mixed",
                Clarity = "Deliberately obfuscated",
                ManipulationTactics = new List<string> { "fear_appeal", "conspiracy_theory" }
            },
            Factors = new List<AnalysisFactor>
            {
                new() { Name = "Source Credibility", Score = 20, Details = "No credible source identified" },
                new() { Name = "Factual Accuracy", Score = 15, Details = "Major claims are unsubstantiated" },
                new() { Name = "Balanced Reporting", Score = 10, Details = "One-sided narrative" },
                new() { Name = "Sensationalism", Score = 85, Details = "Highly sensationalized language" }
            }
        };
    }
}
