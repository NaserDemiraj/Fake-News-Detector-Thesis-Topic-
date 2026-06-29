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

        // Derived credibility reputation fields
        /// <summary>"Reliable", "Mixed Record", "Flagged", or "Unknown" (no data)</summary>
        public string CredibilityLabel { get; set; } = "Unknown";
        /// <summary>0–1 confidence in the label; grows with sample size (saturates at ~30 samples)</summary>
        public double CredibilityConfidence { get; set; }
        /// <summary>Trend over the most recent analyses: "improving", "declining", or "stable"</summary>
        public string RecentTrend { get; set; } = "stable";
        /// <summary>Average score of the 5 most recent analyses for this domain</summary>
        public double RecentAverageScore { get; set; }
    }
}
