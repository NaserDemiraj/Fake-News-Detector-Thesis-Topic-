using FakeNewsDetector.Models;
using System.Net;
using System.Text.Json;

namespace FakeNewsDetector.Services
{
    public class FactCheckService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FactCheckService> _logger;
        private readonly string? _apiKey;

        private static readonly string BaseUrl =
            "https://factchecktools.googleapis.com/v1alpha1/claims:search";

        // Ratings that indicate a claim was found to be false / misleading
        private static readonly string[] NegativeRatingKeywords =
            ["false", "wrong", "incorrect", "mislead", "fabricat", "invent", "fake",
             "pants", "fiction", "debunk", "distort", "exaggerat"];

        private static readonly string[] PositiveRatingKeywords =
            ["true", "accurate", "correct", "verified", "legit"];

        public FactCheckService(IHttpClientFactory httpClientFactory,
                                ILogger<FactCheckService> logger,
                                IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiKey = configuration["FactCheck:ApiKey"];
        }

        public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<List<EvidencePoint>> SearchAsync(string query)
        {
            if (!IsEnabled) return [];

            try
            {
                var encodedQuery = WebUtility.UrlEncode(query[..Math.Min(query.Length, 200)]);
                var url = $"{BaseUrl}?query={encodedQuery}&key={_apiKey}&languageCode=en&pageSize=5";

                var http = _httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(8);

                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Google Fact Check API returned {Status}", resp.StatusCode);
                    return [];
                }

                var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
                if (!json.TryGetProperty("claims", out var claims))
                    return [];

                var evidence = new List<EvidencePoint>();
                foreach (var claim in claims.EnumerateArray().Take(5))
                {
                    var claimText = claim.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    if (!claim.TryGetProperty("claimReview", out var reviews)) continue;

                    foreach (var review in reviews.EnumerateArray().Take(2))
                    {
                        var rating = review.TryGetProperty("textualRating", out var r) ? r.GetString() ?? "" : "";
                        var reviewUrl = review.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                        var title = review.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
                        var publisherName = "";
                        if (review.TryGetProperty("publisher", out var pub) &&
                            pub.TryGetProperty("name", out var pn))
                            publisherName = pn.GetString() ?? "";

                        if (string.IsNullOrWhiteSpace(rating)) continue;

                        var ratingLower = rating.ToLowerInvariant();
                        var status = NegativeRatingKeywords.Any(k => ratingLower.Contains(k)) ? "warning"
                                   : PositiveRatingKeywords.Any(k => ratingLower.Contains(k)) ? "verified"
                                   : "unverified";

                        var snippet = string.IsNullOrEmpty(title) ? claimText : title;
                        if (snippet.Length > 240) snippet = snippet[..240] + "…";
                        var text = $"[{publisherName}] {snippet} — Rated: {rating}";

                        evidence.Add(new EvidencePoint
                        {
                            Text = text,
                            Status = status,
                            Source = string.IsNullOrEmpty(reviewUrl) ? null : reviewUrl
                        });
                    }
                }

                _logger.LogInformation("Google Fact Check returned {Count} results for query: {Query}",
                    evidence.Count, query[..Math.Min(60, query.Length)]);
                return evidence;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Fact Check error — continuing without it");
                return [];
            }
        }
    }
}
