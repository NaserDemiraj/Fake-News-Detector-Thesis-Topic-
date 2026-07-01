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
        private readonly FactCheckService _factCheck;
        private readonly List<AiProvider> _providers;
        // Ablation control: zero_shot | skepticism | few_shot | full (default)
        private readonly string _promptVariant;
        // Score-based verdict override thresholds (score 0-100).
        // Corrects for LLM bias toward "likely_true" by enforcing the prompt's own score contract.
        private readonly double _fakeMaxScore;
        private readonly double _trueMinScore;

        public NewsAnalyzerService(ILogger<NewsAnalyzerService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration, TavilyService tavily, FactCheckService factCheck)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _tavily = tavily;
            _factCheck = factCheck;
            _providers = new List<AiProvider>();
            _promptVariant = configuration["PromptVariant"] ?? "full";
            _fakeMaxScore  = configuration.GetValue<double>("VerdictThresholds:FakeMax", 40.0);
            _trueMinScore  = configuration.GetValue<double>("VerdictThresholds:TrueMin", 70.0);
            _logger.LogInformation("Prompt variant: {Variant}, thresholds: fake≤{FakeMax} true≥{TrueMin}",
                _promptVariant, _fakeMaxScore, _trueMinScore);

            var ollamaEnabled = configuration.GetValue<bool>("Ollama:Enabled");
            var groqKey     = configuration["Groq:ApiKey"]     ?? "";
            var cerebrasKey = configuration["Cerebras:ApiKey"] ?? "";
            var xaiKey      = configuration["XAI:ApiKey"]      ?? "";
            var geminiKey   = configuration["Gemini:ApiKey"]   ?? "";

            if (ollamaEnabled)
            {
                var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
                var model   = configuration["Ollama:Model"]   ?? "llama3.2";
                _providers.Add(new AiProvider("Ollama", "ollama", model, $"{baseUrl}/v1/chat/completions", IsOllama: true));
            }

            if (!string.IsNullOrEmpty(groqKey) && !groqKey.StartsWith("SET_VIA") && !groqKey.StartsWith("PASTE"))
            {
                var model = configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";
                _providers.Add(new AiProvider("Groq", groqKey, model, "https://api.groq.com/openai/v1/chat/completions"));
            }

            if (!string.IsNullOrEmpty(cerebrasKey) && !cerebrasKey.StartsWith("SET_VIA") && !cerebrasKey.StartsWith("PASTE"))
            {
                var model = configuration["Cerebras:Model"] ?? "gemma-4-31b";
                _providers.Add(new AiProvider("Cerebras", cerebrasKey, model, "https://api.cerebras.ai/v1/chat/completions"));
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

            // Reproducible benchmarks: set LlmOnlyProvider (e.g. "Groq") to disable the
            // fallback chain and pin every request to ONE provider/model. Leave unset in
            // production so the multi-provider fallback stays active.
            var onlyProvider = configuration["LlmOnlyProvider"];
            if (!string.IsNullOrWhiteSpace(onlyProvider))
            {
                var before = _providers.Count;
                _providers.RemoveAll(p => !p.Name.Contains(onlyProvider, StringComparison.OrdinalIgnoreCase));
                _logger.LogInformation("LlmOnlyProvider={Only}: pinned to {Count} provider(s) (was {Before}) — fallback disabled for reproducible runs",
                    onlyProvider, _providers.Count, before);
            }

            if (_providers.Count > 0)
                _logger.LogInformation("AI providers loaded: {Providers}", string.Join(" → ", _providers.Select(p => p.Name)));
            else
                _logger.LogWarning("No AI providers configured — mock mode active");
        }

        public async Task<AnalysisResult> AnalyzeContentAsync(string content, string? sourceUrl = null)
        {
            _logger.LogInformation("Analyzing content, length: {Length}, source: {Source}", content.Length, sourceUrl ?? "none");

            var prefilter = PreFilterContent(content);
            if (prefilter != null) return prefilter;

            var evidence = await GatherEvidenceAsync(content);

            foreach (var provider in _providers)
            {
                try
                {
                    _logger.LogInformation("Trying provider: {Provider} (grounding with {Count} evidence point(s))", provider.Name, evidence.Count);
                    var json = await CallAIWithRetryAsync(content, provider, evidence, sourceUrl);
                    var result = AnalysisResultParser.Parse(json);

                    // If this provider returned something unparseable, don't return a broken
                    // 50/uncertain result — treat it as a failure and try the next provider.
                    if (!result.Success)
                    {
                        _logger.LogWarning("Provider {Provider} returned an unparseable response — trying next", provider.Name);
                        continue;
                    }

                    ApplyVerdictThreshold(result);
                    _logger.LogInformation("Analysis complete via {Provider}. Verdict: {Verdict}, Score: {Score}", provider.Name, result.Verdict, result.Score);

                    // Also attach the raw evidence to the result for the UI
                    if (evidence.Count > 0)
                        result.EvidencePoints = evidence.Concat(result.EvidencePoints).ToList();

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Provider {Provider} failed — trying next", provider.Name);
                }
            }

            _logger.LogError("All AI providers failed (rate limit / error) — returning service-unavailable placeholder");
            return ServiceUnavailableResult();
        }

        // Consensus mode: run every configured provider in parallel and aggregate.
        public async Task<AnalysisResult> AnalyzeContentEnsembleAsync(string content, string? sourceUrl = null)
        {
            _logger.LogInformation("Ensemble analysis, length: {Length}, providers: {Count}", content.Length, _providers.Count);

            var prefilter = PreFilterContent(content);
            if (prefilter != null) return prefilter;

            // With only one provider there's nothing to reach consensus over — fall back.
            if (_providers.Count < 2)
                return await AnalyzeContentAsync(content, sourceUrl);

            var evidence = await GatherEvidenceAsync(content);

            // Query all providers concurrently. Each task is self-contained and never throws.
            var tasks = _providers.Select(async provider =>
            {
                try
                {
                    var json = await CallAIWithRetryAsync(content, provider, evidence, sourceUrl);
                    var r = AnalysisResultParser.Parse(json);
                    if (!r.Success) return (provider, result: (AnalysisResult?)null);
                    ApplyVerdictThreshold(r);
                    return (provider, result: (AnalysisResult?)r);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ensemble: provider {Provider} failed", provider.Name);
                    return (provider, result: (AnalysisResult?)null);
                }
            });

            var settled = await Task.WhenAll(tasks);
            var votes = settled.Where(x => x.result != null)
                               .Select(x => (x.provider, r: x.result!))
                               .ToList();

            if (votes.Count == 0)
            {
                _logger.LogError("Ensemble: all providers failed — returning service-unavailable placeholder");
                return ServiceUnavailableResult();
            }

            // Majority verdict; ties broken by the mean score via the standard thresholds.
            var meanScore = votes.Average(v => v.r.Score);
            var byVerdict = votes.GroupBy(v => v.r.Verdict)
                                 .Select(g => new { Verdict = g.Key, Count = g.Count() })
                                 .OrderByDescending(g => g.Count)
                                 .ToList();
            var topCount = byVerdict.First().Count;
            var tied = byVerdict.Where(g => g.Count == topCount).Select(g => g.Verdict).ToList();
            string majorityVerdict = tied.Count == 1
                ? tied[0]
                : (meanScore >= _trueMinScore ? "likely_true"
                   : meanScore <= _fakeMaxScore ? "likely_fake" : "uncertain");

            // Agreement = fraction of models sharing the winning verdict.
            double agreement = (double)votes.Count(v => v.r.Verdict == majorityVerdict) / votes.Count;

            // Base the rich fields on the vote closest to the mean score (most representative),
            // preferring one whose verdict matches the majority.
            var basis = votes.Where(v => v.r.Verdict == majorityVerdict)
                             .OrderBy(v => Math.Abs(v.r.Score - meanScore))
                             .FirstOrDefault().r
                        ?? votes.OrderBy(v => Math.Abs(v.r.Score - meanScore)).First().r;

            // Snapshot every model's vote BEFORE mutating basis — basis is one of these
            // same result references, so overriding its fields first would corrupt its
            // own vote row (making the aggregate look identical to that model's vote).
            var voteList = votes.Select(v => new EnsembleVote
            {
                Provider = v.provider.Name,
                Model = v.provider.Model,
                Verdict = v.r.Verdict,
                Score = Math.Round(v.r.Score),
                Confidence = v.r.Confidence
            }).ToList();

            basis.Score = Math.Round(meanScore);
            basis.Verdict = majorityVerdict;
            basis.Confidence = agreement; // consensus IS the confidence signal
            basis.IsEnsemble = true;
            basis.AgreementScore = agreement;
            basis.EnsembleVotes = voteList;

            var agreePct = (int)Math.Round(agreement * 100);
            basis.Explanation = $"{votes.Count} models analysed this; {agreePct}% agreed on \"{majorityVerdict.Replace('_', ' ')}\" " +
                                $"(mean score {basis.Score:0}). " + basis.Explanation;

            if (evidence.Count > 0)
                basis.EvidencePoints = evidence.Concat(basis.EvidencePoints).ToList();

            _logger.LogInformation("Ensemble complete: {Votes} votes, verdict={Verdict}, mean={Score}, agreement={Agree:P0}",
                votes.Count, majorityVerdict, basis.Score, agreement);
            return basis;
        }

        // Pre-filter: returns a rejection/mock result if the content can't be analysed,
        // otherwise null (proceed to the LLM). Shared by single + ensemble paths.
        private AnalysisResult? PreFilterContent(string content)
        {
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

            return null;
        }

        // Gather web evidence FIRST so it can be fed into the prompt (real grounding).
        // Fact-check + Tavily searches run concurrently; we await both. Never throws.
        private async Task<List<EvidencePoint>> GatherEvidenceAsync(string content)
        {
            var searchQuery = ExtractSearchQuery(content);
            var tavilyTask = _tavily.IsEnabled
                ? _tavily.SearchAsync(searchQuery)
                : Task.FromResult(new List<EvidencePoint>());
            var factCheckTask = _factCheck.IsEnabled
                ? _factCheck.SearchAsync(searchQuery)
                : Task.FromResult(new List<EvidencePoint>());

            try
            {
                var factCheckEvidence = await factCheckTask;
                var webEvidence = await tavilyTask;

                // Deduplicate by text — different URLs often scrape the same boilerplate
                // (e.g. an author bio repeated across profile pages). Keep the first
                // occurrence so the same snippet doesn't show up as two evidence points.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deduped = new List<EvidencePoint>();
                foreach (var e in factCheckEvidence.Concat(webEvidence))
                {
                    // Key on a normalised prefix so near-identical snippets that differ only
                    // in their trailing truncation still collapse to one.
                    var text = (e.Text ?? "").Trim();
                    var key = new string(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                    if (key.Length > 60) key = key[..60];
                    if (key.Length > 0 && seen.Add(key))
                        deduped.Add(e);
                }
                return deduped;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Evidence search failed — proceeding without grounding");
                return new List<EvidencePoint>();
            }
        }

        // Returned when the API key(s) ARE configured but every provider failed (almost
        // always the free-tier daily rate limit). Distinct from MockAnalysis() so the UI
        // can say "try again shortly" instead of falsely claiming no key is configured.
        private static AnalysisResult ServiceUnavailableResult() => new()
        {
            Success = true,
            IsMock = true,
            IsServiceUnavailable = true,
            Verdict = "uncertain",
            Score = 50,
            Confidence = 0.0,
            Language = "en",
            LanguageName = "English",
            Summary = "AI analysis is temporarily unavailable.",
            Explanation = "All AI providers are currently rate-limited (the free-tier daily quota was reached). " +
                          "This is not a real verdict — please try again in a few minutes.",
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

        private async Task<string> CallAIWithRetryAsync(string content, AiProvider provider, IReadOnlyList<EvidencePoint> evidence, string? sourceUrl = null, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await CallAIApiAsync(content, provider, evidence, sourceUrl);
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogWarning("Provider {Provider} attempt {Attempt} failed: {Message}. Retrying in {Delay}s...", provider.Name, attempt, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
            return await CallAIApiAsync(content, provider, evidence, sourceUrl);
        }

        private async Task<string> CallAIApiAsync(string content, AiProvider provider, IReadOnlyList<EvidencePoint> evidence, string? sourceUrl = null)
        {
            const int maxContentLength = 5000;
            var truncated = content.Length > maxContentLength
                ? content.Substring(0, maxContentLength) + "\n\n[... content truncated ...]"
                : content;

            var prompt = BuildPrompt(truncated, evidence, sourceUrl);

            // Ollama's small local models don't reliably support response_format=json_object;
            // rely on prompt-level instruction + ExtractJson() to recover the JSON.
            object requestPayload = provider.IsOllama
                ? new { model = provider.Model, messages = new[] { new { role = "user", content = prompt } }, temperature = 0.1, max_tokens = 2500 }
                : new { model = provider.Model, messages = new[] { new { role = "user", content = prompt } }, temperature = 0.1, max_tokens = 2500, response_format = new { type = "json_object" } };

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

        private string BuildPrompt(string truncated, IReadOnlyList<EvidencePoint>? evidence = null, string? sourceUrl = null)
        {
            var useSkepticism = _promptVariant is "skepticism" or "full";
            var useFewShot    = _promptVariant is "few_shot"   or "full";

            const string schema = @"{""reasoning"":""think step by step FIRST: list the central factual claims, then for each say whether the WEB EVIDENCE supports/contradicts/omits it, then justify the score"",""verdict"":""likely_true""|""likely_fake""|""uncertain"",""score"":0-100,""confidence"":0.0-1.0,""language"":""en"",""language_name"":""English"",""explanation"":""1-2 sentences"",""red_flags"":[""⚠ short flag""],""highlighted_sentences"":[{""flag"":""⚠ exact flag text"",""sentence"":""verbatim ≤120 chars from CONTENT"",""reason"":""brief why""}],""credibility_signals"":[""✓ short signal""],""bias_detection"":{""emotional_language_score"":0-100,""fear_mongering"":false,""political_bias"":""neutral""|""left""|""right""|""mixed"",""manipulation_tactics"":[],""clarity"":""word""},""factors"":[{""name"":""short"",""score"":0-100,""details"":""brief""}],""claims"":[{""text"":""a single factual claim from CONTENT, ≤120 chars"",""status"":""verified""|""partially_verified""|""unverified""}],""evidence_points"":[{""text"":""brief"",""status"":""verified""|""warning""|""unverified""}]}";

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

            // Real grounding: feed live search results into the prompt so the verdict
            // is based on whether the claims are actually corroborated — not just style.
            if (evidence is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("WEB EVIDENCE (live search results — use these to verify the CONTENT's claims):");
                int i = 1;
                foreach (var e in evidence.Take(6))
                {
                    var src = string.IsNullOrWhiteSpace(e.Source) ? "" : $" [source: {e.Source}]";
                    var text = e.Text.Length > 300 ? e.Text[..300] + "…" : e.Text;
                    sb.AppendLine($"  {i}. {text}{src}");
                    i++;
                }
                sb.AppendLine();
                sb.AppendLine("Ground your verdict in this evidence:");
                sb.AppendLine("- Evidence CORROBORATES the main claims → likely_true (high score).");
                sb.AppendLine("- Evidence CONTRADICTS or debunks the claims → likely_fake (low score).");
                sb.AppendLine("- Evidence is absent or only tangential → judge on journalistic merit: well-sourced, specific, measured reporting can still be likely_true; only lean uncertain if the content is ALSO vague, sensational, or unsourced.");
                sb.AppendLine("- Set each evidence_points[].status (verified/warning/unverified) according to this evidence.");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("NO external evidence was retrieved. Judge on journalistic credibility and internal consistency: clear reporting with named/verifiable sources, specific dateable events and a measured tone can be likely_true; sensational, conspiratorial, anonymously-sourced or vague claims lean likely_fake or uncertain. Do NOT force uncertain just because there was no web hit.");
            }

            sb.AppendLine();
            sb.AppendLine("Now analyze the following content. Detect its language.");

            // Tell the model where this content came from — domain reputation is a strong signal
            if (!string.IsNullOrEmpty(sourceUrl) && Uri.TryCreate(sourceUrl, UriKind.Absolute, out var parsedUri))
            {
                sb.AppendLine($"SOURCE URL: {sourceUrl}");
                sb.AppendLine($"SOURCE DOMAIN: {parsedUri.Host}");
                sb.AppendLine("Weight the source domain's established editorial reputation when calibrating the score. " +
                              "Major wire services and national broadcasters (e.g. reuters.com, apnews.com, bbc.com, nytimes.com) " +
                              "have editorial oversight that counts as a strong credibility signal. " +
                              "Unknown, tabloid, or satire domains reduce credibility. " +
                              "If only OG/meta text was available (paywalled page), note this in the reasoning.");
            }

            sb.AppendLine();
            sb.AppendLine($"CONTENT: {truncated}");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- score: use the FULL 0-100 range. Absurd, physically impossible, or long-debunked claims (e.g. flat earth, moon made of cheese, vaccines contain microchips) → 0-5. Fabricated but plausible-sounding claims → 10-35. Genuinely undetermined → 40-70. Credible, well-sourced reporting → 75-100.");
            sb.AppendLine("- reasoning: fill this FIRST, before deciding score/verdict. Identify the main factual claims and check them against any WEB EVIDENCE. A professional tone alone does not prove an EXTRAORDINARY claim — but routine, well-sourced reporting can score high (likely_true) even without external corroboration.");
            sb.AppendLine("- highlighted_sentences: quote ≤3 verbatim phrases (≤120 chars each) from CONTENT that directly triggered a red flag.");
            sb.AppendLine("- claims: extract 2-5 of the CONTENT's key factual claims (each a short standalone statement). Mark status against the WEB EVIDENCE: verified = evidence supports it; partially_verified = mixed/only partly supported; unverified = no evidence found or evidence contradicts it. If CONTENT is not a news article, return an empty claims array.");
            sb.AppendLine("- language: ISO 639-1 code (e.g. \"en\", \"sq\", \"de\").");
            sb.AppendLine("- If CONTENT is gibberish, code, or clearly not a news article, return verdict=\"uncertain\", score=50, confidence=0.3.");
            sb.AppendLine();
            sb.Append("JSON:\n");
            sb.Append(schema);

            return sb.ToString();
        }

        // Overrides the LLM's text verdict with what its numeric score says.
        // Prevents the common LLM bias of labelling everything "likely_true" while assigning
        // mid-range scores (50-65) that the prompt explicitly defines as "uncertain".
        //
        // Threshold values should be set from the ROC analysis (Evaluation/roc_curve.py prints
        // the Youden-optimal cutoff). Setting FakeMax == TrueMin == T gives a pure binary
        // decision at T (score >= T → true) with no uncertain band and 100% coverage.
        // TrueMin is checked first so the boundary matches sklearn's convention (score >= T → true).
        private void ApplyVerdictThreshold(AnalysisResult result)
        {
            if (result.IsRejected || result.IsMock || !result.Success) return;
            var overridden = result.Score >= _trueMinScore ? "likely_true"
                           : result.Score <= _fakeMaxScore ? "likely_fake"
                           : "uncertain";
            if (overridden != result.Verdict)
                _logger.LogDebug("Verdict overridden: {Old} → {New} (score={Score})", result.Verdict, overridden, result.Score);
            result.Verdict = overridden;
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
