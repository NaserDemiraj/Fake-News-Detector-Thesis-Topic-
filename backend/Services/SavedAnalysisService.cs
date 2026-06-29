using FakeNewsDetector.Models;
using System.Text.Json.Nodes;

namespace FakeNewsDetector.Services
{
    public class SavedAnalysisService : ISavedAnalysisService
    {
        private readonly NeonHttpService _neon;
        private readonly ILogger<SavedAnalysisService> _logger;

        private const string AllColumns = @"""Id"",""Title"",""Url"",""ContentType"",""Content"",""Score"",""Verdict"",""Date"",""IsFavorite"",""Notes"",""ResultJson"",""UserId"",""ContentHash"",""IsPublic""";

        public SavedAnalysisService(NeonHttpService neon, ILogger<SavedAnalysisService> logger)
        {
            _neon = neon;
            _logger = logger;
        }

        public async Task SaveAnalysisAsync(SavedAnalysis analysis)
        {
            if (string.IsNullOrEmpty(analysis.Id))
                analysis.Id = Guid.NewGuid().ToString();
            if (analysis.Date == default)
                analysis.Date = DateTime.UtcNow;

            await _neon.ExecuteAsync(
                $@"INSERT INTO ""SavedAnalyses"" ({AllColumns})
                   VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14)
                   ON CONFLICT (""Id"") DO NOTHING",
                analysis.Id, analysis.Title,
                string.IsNullOrEmpty(analysis.Url) ? null : analysis.Url,
                analysis.ContentType,
                string.IsNullOrEmpty(analysis.Content) ? null : analysis.Content,
                analysis.Score,
                string.IsNullOrEmpty(analysis.Verdict) ? null : analysis.Verdict,
                analysis.Date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                analysis.IsFavorite, analysis.Notes, analysis.ResultJson,
                string.IsNullOrEmpty(analysis.UserId) ? null : analysis.UserId,
                string.IsNullOrEmpty(analysis.ContentHash) ? null : analysis.ContentHash,
                analysis.IsPublic);

            _logger.LogInformation("Analysis saved: {Title} (user: {UserId})", analysis.Title, analysis.UserId ?? "anonymous");
        }

        public async Task<List<SavedAnalysis>> GetRecentAnalysesAsync(int count, string userId)
        {
            var rows = await _neon.QueryAsync(
                $@"SELECT {AllColumns} FROM ""SavedAnalyses""
                   WHERE ""UserId"" = $2
                   ORDER BY ""Date"" DESC LIMIT $1",
                count, userId);
            return MapRows(rows);
        }

        public async Task<List<SavedAnalysis>> GetAllAnalysesAsync(string userId)
        {
            var rows = await _neon.QueryAsync(
                $@"SELECT {AllColumns} FROM ""SavedAnalyses""
                   WHERE ""UserId"" = $1
                   ORDER BY ""Date"" DESC",
                userId);
            return MapRows(rows);
        }

        public async Task<SavedAnalysis?> GetAnalysisByIdAsync(string id)
        {
            var rows = await _neon.QueryAsync(
                $@"SELECT {AllColumns} FROM ""SavedAnalyses"" WHERE ""Id""=$1", id);
            return MapRows(rows).FirstOrDefault();
        }

        public async Task UpdateAnalysisAsync(string id, bool isFavorite, string notes, string userId)
        {
            await _neon.ExecuteAsync(
                @"UPDATE ""SavedAnalyses""
                  SET ""IsFavorite""=$1,""Notes""=$2
                  WHERE ""Id""=$3 AND ""UserId""=$4",
                isFavorite, notes, id, userId);
        }

        public async Task DeleteAnalysisAsync(string id, string userId)
        {
            await _neon.ExecuteAsync(
                @"DELETE FROM ""SavedAnalyses"" WHERE ""Id""=$1 AND ""UserId""=$2",
                id, userId);
        }

        public async Task<SavedAnalysis?> GetByContentHashAsync(string hash)
        {
            var rows = await _neon.QueryAsync(
                $@"SELECT {AllColumns} FROM ""SavedAnalyses""
                   WHERE ""ContentHash"" = $1
                   ORDER BY ""Date"" DESC LIMIT 1",
                hash);
            return MapRows(rows).FirstOrDefault();
        }

        public async Task<SavedAnalysis?> GetPublicAnalysisAsync(string id)
        {
            var rows = await _neon.QueryAsync(
                $@"SELECT {AllColumns} FROM ""SavedAnalyses""
                   WHERE ""Id"" = $1 AND ""IsPublic"" = true LIMIT 1",
                id);
            return MapRows(rows).FirstOrDefault();
        }

        public async Task SetPublicAsync(string id, bool isPublic, string userId)
        {
            await _neon.ExecuteAsync(
                @"UPDATE ""SavedAnalyses"" SET ""IsPublic""=$1 WHERE ""Id""=$2 AND ""UserId""=$3",
                isPublic, id, userId);
        }

        public async Task<DomainStats> GetDomainStatsAsync(string host)
        {
            var rows = await _neon.QueryAsync(
                @"SELECT ""Score"",""Verdict"",""Date"" FROM ""SavedAnalyses""
                  WHERE ""Url"" LIKE $1 AND ""Url"" IS NOT NULL
                  ORDER BY ""Date"" DESC",
                "%" + host + "%");

            var analyses = rows
                .OfType<JsonObject>()
                .Select(o => new
                {
                    Score = DblOrZero(o, "Score"),
                    Verdict = Str(o, "Verdict"),
                    Date = DateOrDefault(o, "Date")
                })
                .ToList();

            if (analyses.Count == 0)
                return new DomainStats { Host = host };

            var trueCount = analyses.Count(a => a.Verdict == "likely_true");
            var fakeCount = analyses.Count(a => a.Verdict == "likely_fake");
            var uncertainCount = analyses.Count(a => a.Verdict == "uncertain");
            var mostCommon = trueCount >= fakeCount && trueCount >= uncertainCount ? "likely_true"
                           : fakeCount >= trueCount && fakeCount >= uncertainCount ? "likely_fake"
                           : "uncertain";

            // Credibility label: driven by fake ratio among decisive verdicts
            var decisive = trueCount + fakeCount;
            var fakeRatio = decisive > 0 ? (double)fakeCount / decisive : 0.5;
            var label = decisive == 0 ? "Unknown"
                      : fakeRatio >= 0.6 ? "Flagged"
                      : fakeRatio <= 0.25 ? "Reliable"
                      : "Mixed Record";

            // Confidence saturates at ~30 samples (Wilson-style approximation)
            var confidence = Math.Round(1.0 - Math.Exp(-analyses.Count / 15.0), 3);

            // Recent trend: compare avg score of newest 5 vs oldest 5 in dataset
            var recentAvg = analyses.Take(5).Average(a => a.Score);
            var olderAvg = analyses.Count >= 10
                ? analyses.Skip(analyses.Count - 5).Average(a => a.Score)
                : analyses.Average(a => a.Score);
            var trend = analyses.Count < 5 ? "stable"
                      : recentAvg - olderAvg > 5 ? "improving"
                      : olderAvg - recentAvg > 5 ? "declining"
                      : "stable";

            return new DomainStats
            {
                Host = host,
                TotalAnalyses = analyses.Count,
                AverageScore = Math.Round(analyses.Average(a => a.Score), 1),
                LikelyTrueCount = trueCount,
                LikelyFakeCount = fakeCount,
                UncertainCount = uncertainCount,
                MostCommonVerdict = mostCommon,
                CredibilityLabel = label,
                CredibilityConfidence = confidence,
                RecentTrend = trend,
                RecentAverageScore = Math.Round(recentAvg, 1)
            };
        }

        private static List<SavedAnalysis> MapRows(JsonArray rows)
        {
            var result = new List<SavedAnalysis>(rows.Count);
            foreach (var row in rows)
            {
                if (row is not JsonObject obj) continue;
                result.Add(new SavedAnalysis
                {
                    Id = Str(obj, "Id"),
                    Title = Str(obj, "Title"),
                    Url = Str(obj, "Url"),
                    ContentType = Str(obj, "ContentType", "text"),
                    Content = Str(obj, "Content"),
                    Score = DblOrZero(obj, "Score"),
                    Verdict = Str(obj, "Verdict"),
                    Date = DateOrDefault(obj, "Date"),
                    IsFavorite = BoolVal(obj, "IsFavorite"),
                    Notes = Str(obj, "Notes"),
                    ResultJson = obj["ResultJson"]?.GetValue<string>(),
                    UserId = obj["UserId"]?.GetValue<string>(),
                    ContentHash = obj["ContentHash"]?.GetValue<string>(),
                    IsPublic = BoolVal(obj, "IsPublic")
                });
            }
            return result;
        }

        private static string Str(JsonObject o, string key, string fallback = "")
            => o[key]?.GetValue<string>() ?? fallback;

        private static double DblOrZero(JsonObject o, string key)
            => o[key] != null && double.TryParse(o[key]!.ToString(), out var d) ? d : 0;

        private static bool BoolVal(JsonObject o, string key)
        {
            var v = o[key];
            if (v == null) return false;
            if (v is JsonValue jv && jv.TryGetValue<bool>(out var b)) return b;
            return v.ToString() is "true" or "True" or "1";
        }

        private static DateTime DateOrDefault(JsonObject o, string key)
        {
            var s = o[key]?.ToString();
            return s != null && DateTime.TryParse(s, out var dt) ? dt.ToUniversalTime() : default;
        }
    }
}
