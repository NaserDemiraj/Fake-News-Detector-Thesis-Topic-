using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FakeNewsDetector.Services
{
    public class NeonHttpService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _url;
        private readonly string _connStr;
        private readonly ILogger<NeonHttpService> _logger;

        public NeonHttpService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<NeonHttpService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _url = configuration["Neon:HttpUrl"]
                ?? throw new InvalidOperationException("Neon:HttpUrl is required in configuration");
            _connStr = configuration["Neon:ConnectionUri"]
                ?? throw new InvalidOperationException("Neon:ConnectionUri is required in configuration");
        }

        public async Task<JsonArray> QueryAsync(string sql, params object?[] parameters)
        {
            var result = await SendAsync(sql, parameters);
            return result?["rows"]?.AsArray() ?? [];
        }

        public async Task ExecuteAsync(string sql, params object?[] parameters)
        {
            await SendAsync(sql, parameters);
        }

        private async Task<JsonObject?> SendAsync(string sql, object?[] parameters)
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, _url);
            request.Headers.TryAddWithoutValidation("Neon-Connection-String", _connStr);
            // Serialize params as a plain JSON array with proper types
            var bodyJson = JsonSerializer.Serialize(new { query = sql, @params = parameters });
            request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Neon HTTP API {Status}: {Body}", (int)response.StatusCode, body);
                response.EnsureSuccessStatusCode();
            }
            return await response.Content.ReadFromJsonAsync<JsonObject>();
        }
    }
}
