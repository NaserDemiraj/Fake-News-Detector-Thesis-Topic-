namespace FakeNewsDetector.Models
{
    public class DomainStats
    {
        public string Host { get; set; } = string.Empty;
        public int TotalAnalyses { get; set; }
        public double AverageScore { get; set; }
        public int LikelyTrueCount { get; set; }
        public int LikelyFakeCount { get; set; }
        public int UncertainCount { get; set; }
        public string MostCommonVerdict { get; set; } = "uncertain";
    }
}
