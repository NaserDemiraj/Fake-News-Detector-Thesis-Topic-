using System.Net.Http.Json;
using System.Text.Json;

namespace FakeNewsDetector.Evaluation;

public record PredictionResult(
    string Verdict,
    double Score,
    double Confidence,
    long LatencyMs,
    bool IsError,
    string? ErrorMessage = null);

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("VerifyNews-Evaluator/1.0");
    }

    /// <summary>Checks that the backend is reachable before starting a long run.</summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            // Try the swagger JSON — always present; no auth needed
            var resp = await _http.GetAsync($"{_baseUrl}/swagger/v1/swagger.json",
                HttpCompletionOption.ResponseHeadersRead);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Sends article text to POST /api/Analysis and returns the prediction.</summary>
    public async Task<PredictionResult> AnalyzeAsync(string content)
    {
        const int maxChars = 5000;
        var truncated = content.Length > maxChars
            ? content[..maxChars]
            : content;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var payload = new { type = "text", content = truncated };
            using var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/Analysis", payload, JsonOpts);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var excerpt = body.Length > 120 ? body[..120] : body;
                return new PredictionResult("error", 50, 0, sw.ElapsedMilliseconds, true,
                    $"HTTP {(int)response.StatusCode}: {excerpt}");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

            // Response shape: { result: { verdict, score, confidence, ... }, analysisId, saved, cached }
            var result = json.TryGetProperty("result", out var r) ? r : json;

            var verdict    = result.TryGetProperty("verdict",    out var v) ? v.GetString() ?? "uncertain" : "uncertain";
            var score      = result.TryGetProperty("score",      out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 50;
            var confidence = result.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0.5;
            var isMock     = result.TryGetProperty("isMock",     out var m) && m.ValueKind == JsonValueKind.True;

            if (isMock)
                return new PredictionResult("error", 50, 0, sw.ElapsedMilliseconds, true,
                    "Mock result — all AI providers failed (rate limit / key error)");

            return new PredictionResult(verdict, score, confidence, sw.ElapsedMilliseconds, false);
        }
        catch (TaskCanceledException)
        {
            return new PredictionResult("error", 50, 0, sw.ElapsedMilliseconds, true, "Request timed out (3 min limit)");
        }
        catch (Exception ex)
        {
            return new PredictionResult("error", 50, 0, sw.ElapsedMilliseconds, true, ex.Message);
        }
    }

    public void Dispose() => _http.Dispose();
}
