using FakeNewsDetector.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FakeNewsDetector.Services
{
    public class NewsAnalyzerService : INewsAnalyzerService
    {
        private record AiProvider(string Name, string ApiKey, string Model, string ApiUrl, bool IsOllama = false);

        private readonly ILogger<NewsAnalyzerService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly List<AiProvider> _providers;

        public NewsAnalyzerService(ILogger<NewsAnalyzerService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _providers = new List<AiProvider>();

            var ollamaEnabled = configuration.GetValue<bool>("Ollama:Enabled");
            var groqKey    = configuration["Groq:ApiKey"]    ?? "";
            var xaiKey     = configuration["XAI:ApiKey"]     ?? "";
            var geminiKey  = configuration["Gemini:ApiKey"]  ?? "";

            if (ollamaEnabled)
            {
                var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
                var model   = configuration["Ollama:Model"]   ?? "llama3.2";
                _providers.Add(new AiProvider("Ollama", "ollama", model, $"{baseUrl}/v1/chat/completions", IsOllama: true));
            }

            if (!string.IsNullOrEmpty(groqKey) && !groqKey.StartsWith("SET_VIA") && !groqKey.StartsWith("PASTE"))
            {
                var model = configuration["Groq:Model"] ?? "llama-3.1-8b-instant";
                _providers.Add(new AiProvider("Groq", groqKey, model, "https://api.groq.com/openai/v1/chat/completions"));
            }

            if (!string.IsNullOrEmpty(xaiKey) && !xaiKey.StartsWith("SET_VIA") && !xaiKey.StartsWith("PASTE"))
            {
                var model = configuration["XAI:Model"] ?? "grok-3";
                _providers.Add(new AiProvider("xAI Grok", xaiKey, model, "https://api.x.ai/v1/chat/completions"));
            }

            if (!string.IsNullOrEmpty(geminiKey) && !geminiKey.StartsWith("SET_VIA") && !geminiKey.StartsWith("PASTE"))
            {
                var model = configuration["Gemini:Model"] ?? "gemini-2.0-flash";
                _providers.Add(new AiProvider("Gemini", geminiKey, model, "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"));
            }

            if (_providers.Count > 0)
                _logger.LogInformation("AI providers loaded: {Providers}", string.Join(" → ", _providers.Select(p => p.Name)));
            else
                _logger.LogWarning("No AI providers configured — mock mode active");
        }

        public async Task<AnalysisResult> AnalyzeContentAsync(string content)
        {
            _logger.LogInformation("Analyzing content, length: {Length}", content.Length);

            var wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount < 20)
            {
                _logger.LogWarning("Content too short ({Words} words) — returning uncertain", wordCount);
                return UncertainResult("Content is too short to assess reliably. Please provide at least a full sentence or paragraph.");
            }

            if (_providers.Count == 0)
            {
                _logger.LogWarning("No AI providers configured — using mock analysis");
                return MockAnalysis();
            }

            foreach (var provider in _providers)
            {
                try
                {
                    _logger.LogInformation("Trying provider: {Provider}", provider.Name);
                    var json = await CallAIWithRetryAsync(content, provider);
                    var result = AnalysisResultParser.Parse(json);
                    _logger.LogInformation("Analysis complete via {Provider}. Verdict: {Verdict}, Score: {Score}", provider.Name, result.Verdict, result.Score);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Provider {Provider} failed — trying next", provider.Name);
                }
            }

            _logger.LogError("All AI providers failed — falling back to mock");
            return MockAnalysis();
        }

        private async Task<string> CallAIWithRetryAsync(string content, AiProvider provider, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await CallAIApiAsync(content, provider);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogWarning("Provider {Provider} attempt {Attempt} failed: {Message}. Retrying in {Delay}s...", provider.Name, attempt, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
            return await CallAIApiAsync(content, provider);
        }

        private async Task<string> CallAIApiAsync(string content, AiProvider provider)
        {
            const int maxContentLength = 5000;
            var truncated = content.Length > maxContentLength
                ? content.Substring(0, maxContentLength) + "\n\n[... content truncated ...]"
                : content;

            var prompt = $@"Analyze this news content. Detect the language. Reply ONLY with JSON, no extra text.

CONTENT: {truncated}

Rules:
- highlighted_sentences: quote ≤3 verbatim phrases (≤120 chars each) from CONTENT that directly triggered a red flag. Must be exact or near-exact quotes.
- language: ISO 639-1 code (e.g. ""en"", ""sq"", ""de"").
- If CONTENT is gibberish, code, random text, or clearly not a news article, return verdict=""uncertain"", score=50, confidence=0.3, and explain in explanation.

JSON:
{{""verdict"":""likely_true""|""likely_fake""|""uncertain"",""score"":0-100,""language"":""en"",""language_name"":""English"",""explanation"":""1-2 sentences"",""red_flags"":[""⚠ short flag""],""highlighted_sentences"":[{{""flag"":""⚠ exact flag text"",""sentence"":""verbatim ≤120 chars from CONTENT"",""reason"":""brief why""}}],""credibility_signals"":[""✓ short signal""],""bias_detection"":{{""emotional_language_score"":0-100,""fear_mongering"":false,""political_bias"":""neutral""|""left""|""right""|""mixed"",""manipulation_tactics"":[],""clarity"":""word""}},""factors"":[{{""name"":""short"",""score"":0-100,""details"":""brief""}}],""evidence_points"":[{{""text"":""brief"",""status"":""verified""|""warning""|""unverified""}}]}}";

            // Ollama's small local models don't reliably support response_format=json_object;
            // rely on prompt-level instruction + ExtractJson() to recover the JSON.
            object requestPayload = provider.IsOllama
                ? new { model = provider.Model, messages = new[] { new { role = "user", content = prompt } }, temperature = 0.1, max_tokens = 1000 }
                : new { model = provider.Model, messages = new[] { new { role = "user", content = prompt } }, temperature = 0.1, max_tokens = 1000, response_format = new { type = "json_object" } };

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(3);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", provider.ApiKey);

            var response = await httpClient.PostAsJsonAsync(provider.ApiUrl, requestPayload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("{Provider} API error {Status}: {Error}", provider.Name, response.StatusCode, error);
                throw new Exception($"{provider.Name} API error: {response.StatusCode}");
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

        private static AnalysisResult UncertainResult(string reason) => new()
        {
            Success = true,
            IsMock = false,
            Verdict = "uncertain",
            Score = 50,
            Confidence = 0.0,
            Language = "en",
            LanguageName = "English",
            Summary = reason,
            Explanation = reason,
            CredibilitySignals = new List<string>(),
            RedFlags = new List<string>(),
            HighlightedSentences = new List<SentenceHighlight>(),
            BiasDetection = new BiasDetection
            {
                EmotionalLanguageScore = 0,
                FearMongering = false,
                PoliticalBias = "neutral",
                Clarity = "N/A",
                ManipulationTactics = new List<string>()
            },
            Factors = new List<AnalysisFactor>(),
            EvidencePoints = new List<EvidencePoint>()
        };

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
