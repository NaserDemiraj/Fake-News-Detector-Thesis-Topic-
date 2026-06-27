using FakeNewsDetector.Models;
using FakeNewsDetector.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
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

        private const int MaxContentBytes = 5 * 1024 * 1024; // 5 MB
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

        [HttpPost]
        public async Task<IActionResult> AnalyzeNews([FromBody] AnalysisRequest request)
        {
            if (string.IsNullOrEmpty(request.Content))
                return Problem(title: "Validation error", detail: "Content cannot be empty", statusCode: 400);

            string content;
            string title;
            string sourceUrl = "";

            if (request.Type == "url")
            {
                if (!Uri.TryCreate(request.Content, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return Problem(title: "Validation error", detail: "Invalid URL format. Only http and https URLs are supported.", statusCode: 400);

                if (IsPrivateOrLocalhost(uri))
                    return Problem(title: "Validation error", detail: "The provided URL points to a private or restricted address.", statusCode: 400);

                sourceUrl = request.Content;

                try
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    httpClient.Timeout = FetchTimeout;
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "FakeNewsDetector/1.0");

                    using var response = await httpClient.GetAsync(request.Content, HttpCompletionOption.ResponseHeadersRead);

                    if (!response.IsSuccessStatusCode)
                        return Problem(title: "Fetch error", detail: $"Failed to fetch URL: HTTP {(int)response.StatusCode}", statusCode: 400);

                    if (response.Content.Headers.ContentLength > MaxContentBytes)
                        return Problem(title: "Content too large", detail: "The page content exceeds the 5 MB limit.", statusCode: 400);

                    using var stream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[MaxContentBytes];
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    var htmlContent = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    var extracted = ExtractTextAndTitleFromHtml(htmlContent);
                    content = extracted.Text;
                    title = string.IsNullOrEmpty(extracted.Title)
                        ? "Article from " + uri.Host
                        : extracted.Title;
                }
                catch (TaskCanceledException)
                {
                    return Problem(title: "Timeout", detail: "The request to the URL timed out (10s limit).", statusCode: 408);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching URL: {Url}", request.Content);
                    return Problem(title: "Fetch error", detail: "Could not retrieve content from the URL.", statusCode: 400);
                }
            }
            else
            {
                if (request.Content.Length > 10_000)
                    return Problem(title: "Content too long", detail: "Text input exceeds the 10,000 character limit.", statusCode: 400);

                content = request.Content;
                title = "Text Analysis";
            }

            if (string.IsNullOrWhiteSpace(content))
                return Problem(title: "No content", detail: "No text could be extracted for analysis.", statusCode: 400);

            try
            {
                var result = await _analyzerService.AnalyzeContentAsync(content);

                var truncatedContent = request.Content.Length > 500
                    ? request.Content.Substring(0, 500)
                    : request.Content;

                var savedAnalysis = new SavedAnalysis
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = title,
                    Url = sourceUrl,
                    ContentType = request.Type,
                    Content = truncatedContent,
                    Score = result.Score,
                    Verdict = result.Verdict,
                    Date = DateTime.UtcNow,
                    ResultJson = JsonSerializer.Serialize(result),
                    IsFavorite = false,
                    Notes = string.Empty
                };

                try { _savedAnalysisService.SaveAnalysis(savedAnalysis); }
                catch (Exception dbEx) { _logger.LogWarning(dbEx, "Could not save analysis to DB — returning result anyway"); }

                return Ok(new { result, analysisId = savedAnalysis.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing content");
                return Problem(title: "Analysis error", detail: "An error occurred while analyzing the content. Please try again.", statusCode: 500);
            }
        }

        [HttpGet("recent")]
        public IActionResult GetRecentAnalyses([FromQuery] int count = 20)
        {
            try
            {
                var analyses = _savedAnalysisService.GetRecentAnalyses(Math.Clamp(count, 1, 100));
                return Ok(analyses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recent analyses");
                return Problem(title: "Database error", detail: "Could not retrieve recent analyses.", statusCode: 500);
            }
        }

        [HttpPatch("{id}")]
        public IActionResult UpdateAnalysis(string id, [FromBody] UpdateAnalysisRequest request)
        {
            try
            {
                _savedAnalysisService.UpdateAnalysis(id, request.IsFavorite, request.Notes ?? string.Empty);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating analysis: {Id}", id);
                return Problem(title: "Update error", detail: "Could not update the analysis.", statusCode: 500);
            }
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteAnalysis(string id)
        {
            try
            {
                _savedAnalysisService.DeleteAnalysis(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting analysis: {Id}", id);
                return Problem(title: "Delete error", detail: "Could not delete the analysis.", statusCode: 500);
            }
        }

        [HttpGet("stats")]
        public IActionResult GetAnalysisStats()
        {
            try
            {
                var all = _savedAnalysisService.GetAllAnalyses();
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

        [HttpGet("database")]
        public IActionResult GetDatabaseRecords()
        {
            try
            {
                var all = _savedAnalysisService.GetAllAnalyses();
                return Ok(new
                {
                    TotalRecords = all.Count,
                    Records = all.OrderByDescending(a => a.Date).Select(a => new
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

        // SSRF protection: block private/loopback addresses
        private static bool IsPrivateOrLocalhost(Uri uri)
        {
            var host = uri.Host.ToLowerInvariant();

            if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0")
                return true;

            // AWS/GCP/Azure metadata endpoints
            if (host.StartsWith("169.254."))
                return true;

            // RFC 1918 private ranges
            if (host.StartsWith("10.") || host.StartsWith("192.168."))
                return true;

            // 172.16.0.0/12
            if (host.StartsWith("172."))
            {
                var parts = host.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var second) && second >= 16 && second <= 31)
                    return true;
            }

            // Internal TLDs
            if (host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".localhost"))
                return true;

            return false;
        }

        private static (string Text, string Title) ExtractTextAndTitleFromHtml(string html)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim() ?? "";
                if (string.IsNullOrEmpty(title) || title.Contains("Home") || title.Contains("Index"))
                    title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? title;

                var scripts = doc.DocumentNode.SelectNodes("//script|//style");
                if (scripts != null)
                    foreach (var node in scripts) node.Remove();

                var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
                if (bodyNode == null)
                    return (doc.DocumentNode.InnerText, title);

                var contentNodes = bodyNode.SelectNodes("//p|//h1|//h2|//h3|//h4|//h5|//h6|//article|//section");
                if (contentNodes == null || contentNodes.Count == 0)
                    return (bodyNode.InnerText, title);

                var sb = new StringBuilder();
                foreach (var node in contentNodes)
                    sb.AppendLine(node.InnerText.Trim());

                var text = System.Net.WebUtility.HtmlDecode(sb.ToString());
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                return (text, title);
            }
            catch
            {
                var text = System.Net.WebUtility.HtmlDecode(
                    System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " "));
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
                return (text, "");
            }
        }
    }

    public class UpdateAnalysisRequest
    {
        public bool IsFavorite { get; set; }
        public string? Notes { get; set; }
    }
}
