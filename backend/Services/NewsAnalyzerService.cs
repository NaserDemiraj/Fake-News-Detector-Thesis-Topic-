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
        private readonly TavilyService _tavily;
        private readonly List<AiProvider> _providers;
        // Ablation control: zero_shot | skepticism | few_shot | full (default)
        private readonly string _promptVariant;

        public NewsAnalyzerService(ILogger<NewsAnalyzerService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration, TavilyService tavily)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _tavily = tavily;
            _providers = new List<AiProvider>();
            _promptVariant = configuration["PromptVariant"] ?? "full";
            _logger.LogInformation("Prompt variant: {Variant}", _promptVariant);

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

            var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 5)
            {
                _logger.LogWarning("Content too short ({Words} words) — returning uncertain", words.Length);
                return UncertainResult("Content is too short to assess reliably. Please provide at least a sentence or two.", rejected: true);
            }

            // Reject gibberish: require at least 3 words that look like real words
            // (contain a vowel, mostly letters, length 2–25). Catches random strings, symbols, codes.
            static bool IsRealWord(string w) =>
                w.Length >= 2 && w.Length <= 25 &&
                w.Any(c => "aeiouAEIOU".Contains(c)) &&
                w.Count(char.IsLetter) >= w.Length * 0.6;

            var realWordCount = words.Count(IsRealWord);
            if (realWordCount < 3)
            {
                _logger.LogWarning("Content appears to be gibberish ({RealWords}/{Total} real words) — returning uncertain", realWordCount, words.Length);
                return UncertainResult("This doesn't appear to be readable news content. Please paste an article or headline.", rejected: true);
            }

            if (_providers.Count == 0)
            {
                _logger.LogWarning("No AI providers configured — using mock analysis");
                return MockAnalysis();
            }

            // Start Tavily web-search in parallel with the LLM call to add real evidence
            var searchQuery = ExtractSearchQuery(content);
            var tavilyTask = _tavily.IsEnabled
                ? _tavily.SearchAsync(searchQuery)
                : Task.FromResult(new List<EvidencePoint>());

            foreach (var provider in _providers)
            {
                try
                {
                    _logger.LogInformation("Trying provider: {Provider}", provider.Name);
                    var json = await CallAIWithRetryAsync(content, provider);
                    var result = AnalysisResultParser.Parse(json);
                    _logger.LogInformation("Analysis complete via {Provider}. Verdict: {Verdict}, Score: {Score}", provider.Name, result.Verdict, result.Score);

                    // Merge Tavily evidence into the result
                    var webEvidence = await tavilyTask;
                    if (webEvidence.Count > 0)
                        result.EvidencePoints = webEvidence.Concat(result.EvidencePoints).ToList();

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

            var prompt = BuildPrompt(truncated);

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

        private string BuildPrompt(string truncated)
        {
            var useSkepticism = _promptVariant is "skepticism" or "full";
            var useFewShot    = _promptVariant is "few_shot"   or "full";

            const string schema = @"{""verdict"":""likely_true""|""likely_fake""|""uncertain"",""score"":0-100,""confidence"":0.0-1.0,""language"":""en"",""language_name"":""English"",""explanation"":""1-2 sentences"",""red_flags"":[""⚠ short flag""],""highlighted_sentences"":[{""flag"":""⚠ exact flag text"",""sentence"":""verbatim ≤120 chars from CONTENT"",""reason"":""brief why""}],""credibility_signals"":[""✓ short signal""],""bias_detection"":{""emotional_language_score"":0-100,""fear_mongering"":false,""political_bias"":""neutral""|""left""|""right""|""mixed"",""manipulation_tactics"":[],""clarity"":""word""},""factors"":[{""name"":""short"",""score"":0-100,""details"":""brief""}],""evidence_points"":[{""text"":""brief"",""status"":""verified""|""warning""|""unverified""}]}";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("You are a fact-checking AI that assesses news content authenticity. Reply ONLY with JSON — no extra text, no markdown.");

            if (useSkepticism)
            {
                sb.AppendLine();
                sb.AppendLine("Be SKEPTICAL by default:");
                sb.AppendLine("- Sensational ALL-CAPS language, appeals to \"hidden truth\" or \"mainstream media cover-up\" are strong fake indicators.");
                sb.AppendLine("- Unnamed or anonymous sources with no verifiable attribution → lean toward likely_fake or uncertain.");
                sb.AppendLine("- Extraordinary claims require extraordinary evidence — do not default to likely_true without verifiable facts.");
                sb.AppendLine("- Score 40–70 (uncertain) when you genuinely cannot determine authenticity.");
                sb.AppendLine("- Do NOT rate something likely_true simply because the writing style is formal or professional.");
            }

            if (useFewShot)
            {
                sb.AppendLine();
                sb.AppendLine("EXAMPLE 1 — FAKE:");
                sb.AppendLine("CONTENT: BOMBSHELL: Clinton Foundation EXPOSED Funneling Billions to ISIS! Whistleblower reveals the TRUTH mainstream media is HIDING. According to anonymous sources close to the investigation, Hillary Clinton personally approved wire transfers to known terrorist organizations. Share before they delete this!");
                sb.AppendLine(@"JSON: {""verdict"":""likely_fake"",""score"":10,""confidence"":0.92,""language"":""en"",""language_name"":""English"",""explanation"":""Uses ALL-CAPS sensationalism, exclusively anonymous sourcing, and conspiracy framing. No verifiable facts or named institutions."",""red_flags"":[""⚠ Sensational ALL-CAPS headline"",""⚠ Anonymous sources only"",""⚠ Conspiracy framing""],""highlighted_sentences"":[{""flag"":""⚠ Conspiracy framing"",""sentence"":""Share before they delete this!"",""reason"":""Classic suppression appeal with no basis""}],""credibility_signals"":[],""bias_detection"":{""emotional_language_score"":95,""fear_mongering"":true,""political_bias"":""right"",""manipulation_tactics"":[""fear_appeal"",""conspiracy_theory""],""clarity"":""Deliberately alarmist""},""factors"":[{""name"":""Source Credibility"",""score"":5,""details"":""All sources anonymous""}],""evidence_points"":[]}");
                sb.AppendLine();
                sb.AppendLine("EXAMPLE 2 — REAL:");
                sb.AppendLine("CONTENT: The Federal Reserve raised its benchmark interest rate by a quarter of a percentage point on Wednesday, the third increase this year. Fed Chair Jerome Powell said the decision was unanimous and signalled further gradual increases ahead, depending on incoming economic data.");
                sb.AppendLine(@"JSON: {""verdict"":""likely_true"",""score"":90,""confidence"":0.88,""language"":""en"",""language_name"":""English"",""explanation"":""Names specific institutional actors in verifiable roles. Describes a specific dateable policy event with measured language."",""red_flags"":[],""highlighted_sentences"":[],""credibility_signals"":[""✓ Named verifiable institutional source"",""✓ Specific dateable event"",""✓ Neutral measured tone""],""bias_detection"":{""emotional_language_score"":5,""fear_mongering"":false,""political_bias"":""neutral"",""manipulation_tactics"":[],""clarity"":""Clear""},""factors"":[{""name"":""Source Credibility"",""score"":92,""details"":""Named Fed Chair with verifiable role""}],""evidence_points"":[]}");
                sb.AppendLine();
                sb.AppendLine("EXAMPLE 3 — NOT NEWS:");
                sb.AppendLine("CONTENT: hello world testing 123 random stuff lol idk");
                sb.AppendLine(@"JSON: {""verdict"":""uncertain"",""score"":50,""confidence"":0.30,""language"":""en"",""language_name"":""English"",""explanation"":""Not a news article — no factual claims, sources, or reportable events."",""red_flags"":[],""highlighted_sentences"":[],""credibility_signals"":[],""bias_detection"":{""emotional_language_score"":0,""fear_mongering"":false,""political_bias"":""neutral"",""manipulation_tactics"":[],""clarity"":""N/A""},""factors"":[],""evidence_points"":[]}");
            }

            sb.AppendLine();
            sb.AppendLine("Now analyze the following content. Detect its language.");
            sb.AppendLine();
            sb.AppendLine($"CONTENT: {truncated}");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- highlighted_sentences: quote ≤3 verbatim phrases (≤120 chars each) from CONTENT that directly triggered a red flag.");
            sb.AppendLine("- language: ISO 639-1 code (e.g. \"en\", \"sq\", \"de\").");
            sb.AppendLine("- If CONTENT is gibberish, code, or clearly not a news article, return verdict=\"uncertain\", score=50, confidence=0.3.");
            sb.AppendLine();
            sb.Append("JSON:\n");
            sb.Append(schema);

            return sb.ToString();
        }

        private static string ExtractSearchQuery(string content)
        {
            var first = content.Split(new[] { '.', '!', '?' }, 2)[0].Trim();
            return first.Length > 150 ? first[..150] : first;
        }

        // Parsing logic lives in AnalysisResultParser so it can be unit-tested independently.

        private static AnalysisResult UncertainResult(string reason, bool rejected = false) => new()
        {
            Success = true,
            IsMock = false,
            IsRejected = rejected,
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
