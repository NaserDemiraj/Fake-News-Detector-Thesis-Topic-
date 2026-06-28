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
                var result = AnalysisResultParser.Parse(json);
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

            var prompt = $@"Analyze this news content. Detect the language. Reply ONLY with JSON, no extra text.

CONTENT: {truncated}

Rules:
- highlighted_sentences: quote ≤3 verbatim phrases (≤120 chars each) from CONTENT that directly triggered a red flag. Must be exact or near-exact quotes.
- language: ISO 639-1 code (e.g. ""en"", ""sq"", ""de"").

JSON:
{{""verdict"":""likely_true""|""likely_fake""|""uncertain"",""score"":0-100,""language"":""en"",""language_name"":""English"",""explanation"":""1-2 sentences"",""red_flags"":[""⚠ short flag""],""highlighted_sentences"":[{{""flag"":""⚠ exact flag text"",""sentence"":""verbatim ≤120 chars from CONTENT"",""reason"":""brief why""}}],""credibility_signals"":[""✓ short signal""],""bias_detection"":{{""emotional_language_score"":0-100,""fear_mongering"":false,""political_bias"":""neutral""|""left""|""right""|""mixed"",""manipulation_tactics"":[],""clarity"":""word""}},""factors"":[{{""name"":""short"",""score"":0-100,""details"":""brief""}}],""evidence_points"":[{{""text"":""brief"",""status"":""verified""|""warning""|""unverified""}}]}}";

            var requestPayload = new
            {
                model = _model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.1,
                max_tokens = 1000,
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

        // Parsing logic lives in AnalysisResultParser so it can be unit-tested independently.

        private static AnalysisResult MockAnalysis() => new()
        {
            Success = true,
            IsMock = true,
            Verdict = "likely_fake",
            Score = 35,
            Confidence = 0.72,
            Language = "en",
            LanguageName = "English",
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
            HighlightedSentences = new List<SentenceHighlight>
            {
                new() {
                    Flag     = "⚠ Extraordinary claims without credible evidence",
                    Sentence = "This SHOCKING revelation will DESTROY everything you know about modern medicine!",
                    Reason   = "All-caps sensational language; zero cited sources"
                },
                new() {
                    Flag     = "⚠ Appeal to conspiracy theories",
                    Sentence = "The mainstream media REFUSES to cover this because they are controlled by the globalist elite.",
                    Reason   = "Classic conspiracy framing targeting established institutions"
                },
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
                new() { Name = "Factual Accuracy",   Score = 15, Details = "Major claims are unsubstantiated" },
                new() { Name = "Balanced Reporting", Score = 10, Details = "One-sided narrative" },
                new() { Name = "Sensationalism",     Score = 85, Details = "Highly sensationalized language" }
            }
        };
    }
}
