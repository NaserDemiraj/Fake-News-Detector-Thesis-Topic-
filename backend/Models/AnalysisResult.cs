namespace FakeNewsDetector.Models
{
    public class AnalysisResult
    {
        public bool Success { get; set; }
        public bool IsMock { get; set; } = false;
        public double Score { get; set; }
        public double Confidence { get; set; } = 0.8; // AI confidence in the result
        public string Verdict { get; set; } = string.Empty; // "likely_true", "likely_fake", "uncertain"
        public string Summary { get; set; } = string.Empty; // Brief explanation
        public string Explanation { get; set; } = string.Empty; // Detailed explanation
        public List<string> CredibilitySignals { get; set; } = new List<string>(); // Positive signals
        public List<string> RedFlags { get; set; } = new List<string>(); // Warning signs
        public BiasDetection? BiasDetection { get; set; } // Manipulation & bias analysis
        public List<AnalysisFactor> Factors { get; set; } = new List<AnalysisFactor>();
        public List<EvidencePoint> EvidencePoints { get; set; } = new List<EvidencePoint>(); // Verified/unverified evidence
        public List<Claim> Claims { get; set; } = new List<Claim>(); // Individual claim verification
        public List<RiskCategory> RiskCategories { get; set; } = new List<RiskCategory>(); // Risk assessment metrics
        public string? Reasoning { get; set; } // AI reasoning for the score
    }

    public class BiasDetection
    {
        public int EmotionalLanguageScore { get; set; } // 0-100
        public bool FearMongering { get; set; }
        public string PoliticalBias { get; set; } = "neutral"; // "left", "right", "neutral", "mixed"
        public List<string> ManipulationTactics { get; set; } = new List<string>(); // ["clickbait_headline", "fear_appeal", etc]
        public string Clarity { get; set; } = "Clear"; // How clear the message is
    }

    public class AnalysisFactor
    {
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }
        public string? Details { get; set; }
    }

    public class EvidencePoint
    {
        public string Text { get; set; } = string.Empty;
        public string Status { get; set; } = "unverified"; // "verified", "warning", "unverified"
    }

    public class Claim
    {
        public string Text { get; set; } = string.Empty;
        public string Status { get; set; } = "unverified"; // "verified", "partially_verified", "unverified"
        public List<string> Sources { get; set; } = new List<string>();
    }

    public class RiskCategory
    {
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }
        public string Label { get; set; } = string.Empty; // "High", "Low"
    }
}
