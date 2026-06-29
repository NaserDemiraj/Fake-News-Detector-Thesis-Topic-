using FakeNewsDetector.Models;
using System.Net.Http.Json;
using System.Text.Json;

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

                    var snippet = text.Length > 280 ? text[..280] + "…" : text;
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
    }
}
