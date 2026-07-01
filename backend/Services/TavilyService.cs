using FakeNewsDetector.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FakeNewsDetector.Services
{
    public class TavilyService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TavilyService> _logger;
        private readonly string? _apiKey;

        public TavilyService(IHttpClientFactory httpClientFactory, ILogger<TavilyService> logger, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiKey = configuration["Tavily:ApiKey"];
        }

        public bool IsEnabled => !string.IsNullOrEmpty(_apiKey);

        public async Task<List<EvidencePoint>> SearchAsync(string query)
        {
            if (!IsEnabled) return new();

            try
            {
                var payload = new
                {
                    api_key = _apiKey,
                    query,
                    max_results = 5,
                    search_depth = "basic",
                    include_answer = false
                };

                var http = _httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);

                var resp = await http.PostAsJsonAsync("https://api.tavily.com/search", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Tavily search failed: {Status}", resp.StatusCode);
                    return new();
                }

                var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
                if (!json.TryGetProperty("results", out var results))
                    return new();

                var evidence = new List<EvidencePoint>();
                foreach (var r in results.EnumerateArray().Take(5))
                {
                    var text = r.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    var url  = r.TryGetProperty("url",     out var u) ? u.GetString() ?? "" : "";
                    var score = r.TryGetProperty("score",  out var s) ? s.GetDouble() : 0.0;

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var snippet = CleanSnippet(text, 280);
                    if (string.IsNullOrWhiteSpace(snippet)) continue;
                    var status = score > 0.7 ? "verified" : score > 0.4 ? "unverified" : "warning";
                    evidence.Add(new EvidencePoint { Text = snippet, Status = status, Source = string.IsNullOrEmpty(url) ? null : url });
                }

                _logger.LogInformation("Tavily returned {Count} results for query: {Query}", evidence.Count, query[..Math.Min(60, query.Length)]);
                return evidence;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tavily search error — continuing without web evidence");
                return new();
            }
        }

        // Tavily returns raw scraped page content that often contains markdown
        // (## headings, **bold**, [links](url)) and hard line breaks. Strip those to a
        // clean plain-text snippet and truncate at a word boundary so sentences don't
        // get cut mid-word.
        public static string CleanSnippet(string raw, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var s = raw;
            // Markdown links / images  [text](url) -> text ;  ![alt](url) -> alt
            s = Regex.Replace(s, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            // Heading hashes at start of a line  (## Post -> Post)
            s = Regex.Replace(s, @"(?m)^\s{0,3}#{1,6}\s*", "");
            // Emphasis / code markers  ** __ * _ ` ~
            s = Regex.Replace(s, @"[*_`~]{1,3}", "");
            // Blockquote / list markers at line start
            s = Regex.Replace(s, @"(?m)^\s{0,3}[>\-\+\*]\s+", "");
            // Collapse all whitespace (incl. newlines) to single spaces
            s = Regex.Replace(s, @"\s+", " ").Trim();

            if (s.Length <= maxLen) return s;

            // Truncate at the last word boundary before maxLen (avoid mid-word cuts)
            var cut = s[..maxLen];
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > maxLen - 40) cut = cut[..lastSpace];
            return cut.TrimEnd(',', ';', ':', '-', ' ', '.') + "…";
        }
    }
}
