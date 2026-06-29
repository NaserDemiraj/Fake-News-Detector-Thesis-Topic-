using FakeNewsDetector.Models;
using FakeNewsDetector.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FakeNewsDetector.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalysisController : ControllerBase
    {
        private readonly INewsAnalyzerService _analyzerService;
        private readonly ISavedAnalysisService _savedAnalysisService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AnalysisController> _logger;

        private const int MaxContentBytes = 5 * 1024 * 1024;
        private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

        public AnalysisController(
            INewsAnalyzerService analyzerService,
            ISavedAnalysisService savedAnalysisService,
            IHttpClientFactory httpClientFactory,
            ILogger<AnalysisController> logger)
        {
            _analyzerService = analyzerService;
            _savedAnalysisService = savedAnalysisService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // Helper — returns userId from JWT if present, null otherwise
        private string? CurrentUserId => User.FindFirst("sub")?.Value;

        [HttpPost]
        public async Task<IActionResult> AnalyzeNews([FromBody] AnalysisRequest request)
        {
            var outcome = await ProcessAnalysisAsync(request.Type, request.Content);
            if (!outcome.Ok)
                return Problem(title: "Analysis error", detail: outcome.Error, statusCode: outcome.StatusCode);

            return Ok(new { result = outcome.Result, analysisId = outcome.AnalysisId, saved = outcome.Saved, savedForUser = outcome.SavedForUser, cached = outcome.Cached });
        }

        // POST /api/Analysis/batch — analyze many items at once (CSV rows / bulk)
        [HttpPost("batch")]
        public async Task<IActionResult> AnalyzeBatch([FromBody] BatchAnalysisRequest request)
        {
            if (request?.Items == null || request.Items.Count == 0)
                return Problem(title: "Validation error", detail: "No items provided.", statusCode: 400);

            const int maxItems = 25;
            if (request.Items.Count > maxItems)
                return Problem(title: "Too many items", detail: $"Batch is limited to {maxItems} items per request.", statusCode: 400);

            var results = new List<object>(request.Items.Count);
            var succeeded = 0;
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;

                var type = string.IsNullOrWhiteSpace(item.Type)
                    ? (item.Content.StartsWith("http://") || item.Content.StartsWith("https://") ? "url" : "text")
                    : item.Type;

                var outcome = await ProcessAnalysisAsync(type, item.Content);
                if (outcome.Ok && outcome.Result != null)
                {
                    succeeded++;
                    results.Add(new
                    {
                        ok = true,
                        input = Truncate(item.Content, 120),
                        title = outcome.Title,
                        url = outcome.SourceUrl,
                        analysisId = outcome.AnalysisId,
                        verdict = outcome.Result.Verdict,
                        score = outcome.Result.Score,
                        cached = outcome.Cached
                    });
                }
                else
                {
                    results.Add(new { ok = false, input = Truncate(item.Content, 120), error = outcome.Error });
                }
            }

            return Ok(new { total = results.Count, succeeded, results });
        }

        // Shared analysis pipeline used by both the single and batch endpoints.
        private async Task<AnalysisOutcome> ProcessAnalysisAsync(string type, string rawContent)
        {
            var outcome = new AnalysisOutcome { Type = type };

            if (string.IsNullOrEmpty(rawContent))
                return outcome.Fail("Content cannot be empty", 400);

            string content;
            string title;
            string sourceUrl = "";

            if (type == "url")
            {
                if (!Uri.TryCreate(rawContent, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return outcome.Fail("Invalid URL format. Only http and https URLs are supported.", 400);

                if (IsPrivateOrLocalhost(uri))
                    return outcome.Fail("The provided URL points to a private or restricted address.", 400);

                sourceUrl = rawContent;

                try
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    httpClient.Timeout = FetchTimeout;
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "FakeNewsDetector/1.0");

                    using var response = await httpClient.GetAsync(rawContent, HttpCompletionOption.ResponseHeadersRead);

                    if (!response.IsSuccessStatusCode)
                        return outcome.Fail($"Failed to fetch URL: HTTP {(int)response.StatusCode}", 400);

                    if (response.Content.Headers.ContentLength > MaxContentBytes)
                        return outcome.Fail("The page content exceeds the 5 MB limit.", 400);

                    using var stream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[MaxContentBytes];
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    var htmlContent = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    var extracted = ExtractTextAndTitleFromHtml(htmlContent);
                    content = extracted.Text;
                    title = string.IsNullOrEmpty(extracted.Title) ? "Article from " + uri.Host : extracted.Title;
                }
                catch (TaskCanceledException)
                {
                    return outcome.Fail("The request to the URL timed out (10s limit).", 408);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching URL: {Url}", rawContent);
                    return outcome.Fail("Could not retrieve content from the URL.", 400);
                }
            }
            else
            {
                if (rawContent.Length > 10_000)
                    return outcome.Fail("Text input exceeds the 10,000 character limit.", 400);

                content = rawContent;
                // Use first sentence (up to 80 chars) as title instead of generic label
                var firstSentence = rawContent.Split(new[] { '.', '!', '?' }, 2)[0].Trim();
                title = firstSentence.Length > 80 ? firstSentence[..80] + "…" : firstSentence;
            }

            if (string.IsNullOrWhiteSpace(content))
                return outcome.Fail("No text could be extracted for analysis.", 400);

            try
            {
                // Content-hash dedup: if we've seen this exact content before, return cached result
                // X-Bypass-Cache: 1 skips the lookup (used by the evaluation harness so each prompt variant gets a fresh LLM call)
                var bypassCache = Request.Headers.TryGetValue("X-Bypass-Cache", out var bypassVal) && bypassVal == "1";
                var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
                SavedAnalysis? cached = null;
                if (!bypassCache)
                    try { cached = await _savedAnalysisService.GetByContentHashAsync(contentHash); } catch { }

                AnalysisResult result;
                string analysisId;
                bool fromCache = false;

                if (cached?.ResultJson != null)
                {
                    result = JsonSerializer.Deserialize<AnalysisResult>(cached.ResultJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? await _analyzerService.AnalyzeContentAsync(content);
                    analysisId = cached.Id;
                    fromCache = true;
                    _logger.LogInformation("Cache hit for content hash {Hash}", contentHash[..8]);
                }
                else
                {
                    result = await _analyzerService.AnalyzeContentAsync(content);
                    analysisId = Guid.NewGuid().ToString();
                }

                var truncatedContent = rawContent.Length > 500 ? rawContent.Substring(0, 500) : rawContent;

                var savedAnalysis = new SavedAnalysis
                {
                    Id = analysisId,
                    Title = title,
                    Url = sourceUrl,
                    ContentType = type,
                    Content = truncatedContent,
                    Score = result.Score,
                    Verdict = result.Verdict,
                    Date = DateTime.UtcNow,
                    ResultJson = JsonSerializer.Serialize(result),
                    IsFavorite = false,
                    Notes = string.Empty,
                    UserId = CurrentUserId,
                    ContentHash = contentHash
                };

                // Save when: (a) not cached at all, OR (b) cached but under a different user —
                // so the current logged-in user always gets their own record in history.
                bool needsSave = !fromCache
                    || (CurrentUserId != null && cached?.UserId != CurrentUserId);

                var saved = true;
                if (needsSave)
                {
                    // For cross-user cache hits, generate a fresh ID for this user's record
                    // and update analysisId so the frontend gets the correct record ID.
                    if (fromCache && CurrentUserId != null)
                    {
                        savedAnalysis.Id = Guid.NewGuid().ToString();
                        analysisId = savedAnalysis.Id;
                    }

                    try { await _savedAnalysisService.SaveAnalysisAsync(savedAnalysis); }
                    catch (Exception dbEx)
                    {
                        saved = false;
                        _logger.LogWarning(dbEx, "Could not save analysis to DB — returning result anyway");
                    }
                }

                // Tell the frontend whether the record was saved under the user's account.
                // If savedForUser=false the record exists but won't appear in their history.
                var savedForUser = needsSave && saved && CurrentUserId != null;

                outcome.Ok = true;
                outcome.Result = result;
                outcome.AnalysisId = analysisId;
                outcome.Saved = saved;
                outcome.SavedForUser = savedForUser;
                outcome.Cached = fromCache;
                outcome.Title = title;
                outcome.SourceUrl = sourceUrl;
                return outcome;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing content");
                return outcome.Fail("An error occurred while analyzing the content. Please try again.", 500);
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

        [Authorize]
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentAnalyses([FromQuery] int count = 20)
        {
            var userId = CurrentUserId!;
            try
            {
                var analyses = await _savedAnalysisService.GetRecentAnalysesAsync(Math.Clamp(count, 1, 500), userId);
                return Ok(analyses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recent analyses");
                return Problem(title: "Database error", detail: "Could not retrieve recent analyses.", statusCode: 500);
            }
        }

        [Authorize]
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateAnalysis(string id, [FromBody] UpdateAnalysisRequest request)
        {
            var userId = CurrentUserId!;
            try
            {
                await _savedAnalysisService.UpdateAnalysisAsync(id, request.IsFavorite, request.Notes ?? string.Empty, userId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating analysis: {Id}", id);
                return Problem(title: "Update error", detail: "Could not update the analysis.", statusCode: 500);
            }
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAnalysis(string id)
        {
            var userId = CurrentUserId!;
            try
            {
                await _savedAnalysisService.DeleteAnalysisAsync(id, userId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting analysis: {Id}", id);
                return Problem(title: "Delete error", detail: "Could not delete the analysis.", statusCode: 500);
            }
        }

        [Authorize]
        [HttpGet("stats")]
        public async Task<IActionResult> GetAnalysisStats()
        {
            var userId = CurrentUserId!;
            try
            {
                var all = await _savedAnalysisService.GetAllAnalysesAsync(userId);
                return Ok(new
                {
                    TotalAnalyses = all.Count,
                    FakeNewsDetected = all.Count(a => a.Verdict == "likely_fake"),
                    LikelyTrue = all.Count(a => a.Verdict == "likely_true"),
                    Uncertain = all.Count(a => a.Verdict == "uncertain"),
                    AverageScore = all.Any() ? Math.Round(all.Average(a => a.Score), 1) : 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving stats");
                return Problem(title: "Stats error", detail: "Could not retrieve statistics.", statusCode: 500);
            }
        }

        [Authorize]
        [HttpGet("database")]
        public async Task<IActionResult> GetDatabaseRecords()
        {
            var userId = CurrentUserId!;
            try
            {
                var all = await _savedAnalysisService.GetAllAnalysesAsync(userId);
                return Ok(new
                {
                    TotalRecords = all.Count,
                    Records = all.Select(a => new
                    {
                        a.Id, a.Title, a.Url, a.ContentType, a.Verdict,
                        a.Score, a.IsFavorite, a.Notes,
                        a.FormattedDate, a.RelativeDate
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving database records");
                return Problem(title: "Database error", detail: "Could not retrieve database records.", statusCode: 500);
            }
        }

        // GET /api/Analysis/public/{id} — no auth, returns analysis only if IsPublic=true
        [HttpGet("public/{id}")]
        public async Task<IActionResult> GetPublicAnalysis(string id)
        {
            try
            {
                var analysis = await _savedAnalysisService.GetPublicAnalysisAsync(id);
                if (analysis == null)
                    return NotFound(new { detail = "Analysis not found or not publicly shared." });

                var result = analysis.ResultJson != null
                    ? JsonSerializer.Deserialize<object>(analysis.ResultJson)
                    : null;

                return Ok(new
                {
                    analysisId = analysis.Id,
                    title = analysis.Title,
                    url = analysis.Url,
                    contentType = analysis.ContentType,
                    verdict = analysis.Verdict,
                    score = analysis.Score,
                    date = analysis.FormattedDate,
                    result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching public analysis: {Id}", id);
                return Problem(title: "Error", detail: "Could not retrieve analysis.", statusCode: 500);
            }
        }

        // PATCH /api/Analysis/{id}/share — toggle public sharing
        [Authorize]
        [HttpPatch("{id}/share")]
        public async Task<IActionResult> SetPublicSharing(string id, [FromBody] SetPublicRequest request)
        {
            var userId = CurrentUserId!;
            try
            {
                await _savedAnalysisService.SetPublicAsync(id, request.IsPublic, userId);
                return Ok(new { isPublic = request.IsPublic });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating public status: {Id}", id);
                return Problem(title: "Error", detail: "Could not update sharing status.", statusCode: 500);
            }
        }

        // GET /api/Analysis/domain-stats?host=bbc.com
        [HttpGet("domain-stats")]
        public async Task<IActionResult> GetDomainStats([FromQuery] string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return Problem(title: "Validation error", detail: "host parameter is required.", statusCode: 400);
            try
            {
                var stats = await _savedAnalysisService.GetDomainStatsAsync(host.ToLowerInvariant());
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching domain stats: {Host}", host);
                return Problem(title: "Error", detail: "Could not retrieve domain stats.", statusCode: 500);
            }
        }

        private static bool IsPrivateOrLocalhost(Uri uri)
        {
            var host = uri.Host.ToLowerInvariant();
            if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0") return true;
            if (host.StartsWith("169.254.")) return true;
            if (host.StartsWith("10.") || host.StartsWith("192.168.")) return true;
            if (host.StartsWith("172."))
            {
                var parts = host.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var second) && second >= 16 && second <= 31)
                    return true;
            }
            if (host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".localhost")) return true;
            return false;
        }

        private static (string Text, string Title) ExtractTextAndTitleFromHtml(string html)
            => HtmlExtractor.ExtractTextAndTitle(html);
    }

    public class UpdateAnalysisRequest
    {
        public bool IsFavorite { get; set; }
        public string? Notes { get; set; }
    }

    public class SetPublicRequest
    {
        public bool IsPublic { get; set; }
    }

    public class BatchAnalysisRequest
    {
        public List<BatchAnalysisItem> Items { get; set; } = new();
    }

    public class BatchAnalysisItem
    {
        public string Type { get; set; } = "";       // "url" | "text" | "" (auto-detect)
        public string Content { get; set; } = "";
    }

    // Internal result holder for the shared analysis pipeline (not serialized directly)
    internal sealed class AnalysisOutcome
    {
        public bool Ok;
        public string? Error;
        public int StatusCode = 400;
        public AnalysisResult? Result;
        public string AnalysisId = "";
        public bool Saved = true;
        public bool SavedForUser = false;
        public bool Cached;
        public string Title = "";
        public string SourceUrl = "";
        public string Type = "text";

        public AnalysisOutcome Fail(string error, int statusCode)
        {
            Ok = false;
            Error = error;
            StatusCode = statusCode;
            return this;
        }
    }
}
