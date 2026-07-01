namespace FakeNewsDetector.Models
{
    public class AnalysisResult
    {
        public bool Success { get; set; }
        public bool IsMock { get; set; } = false;
        public bool IsServiceUnavailable { get; set; } = false; // true when all AI providers failed (rate limit) — placeholder, not "no key"
        public bool IsRejected { get; set; } = false; // true when content failed pre-filter (too short / gibberish)
        public double Score { get; set; }
        public double Confidence { get; set; } = 0.8;
        public string Verdict { get; set; } = string.Empty; // "likely_true", "likely_fake", "uncertain"
        public string Summary { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string Language { get; set; } = "en";       // ISO 639-1 code detected by AI
        public string LanguageName { get; set; } = "English"; // Human-readable language name
        public List<string> CredibilitySignals { get; set; } = new List<string>();
        public List<string> RedFlags { get; set; } = new List<string>();
        // Sentence-level explainability: which exact phrases triggered each red flag
        public List<SentenceHighlight> HighlightedSentences { get; set; } = new List<SentenceHighlight>();
        public BiasDetection? BiasDetection { get; set; }
        public List<AnalysisFactor> Factors { get; set; } = new List<AnalysisFactor>();
        public List<EvidencePoint> EvidencePoints { get; set; } = new List<EvidencePoint>();
        public List<Claim> Claims { get; set; } = new List<Claim>();
        public List<RiskCategory> RiskCategories { get; set; } = new List<RiskCategory>();
        public string? Reasoning { get; set; }

        // Ensemble / consensus mode: populated when multiple LLMs analyse the same content.
        public bool IsEnsemble { get; set; } = false;
        public double AgreementScore { get; set; } = 0.0; // 0-1: fraction of models sharing the majority verdict
        public List<EnsembleVote> EnsembleVotes { get; set; } = new List<EnsembleVote>();
    }

    // One model's vote in ensemble/consensus mode.
    public class EnsembleVote
    {
        public string Provider { get; set; } = string.Empty; // "Groq", "Cerebras", "Gemini"
        public string Model { get; set; } = string.Empty;
        public string Verdict { get; set; } = string.Empty;
        public double Score { get; set; }
        public double Confidence { get; set; }
    }

    public class SentenceHighlight
    {
        public string Flag { get; set; } = string.Empty;     // the red flag this sentence triggered
        public string Sentence { get; set; } = string.Empty; // verbatim excerpt from source text
        public string Reason { get; set; } = string.Empty;   // brief explanation why this is a problem
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
        public string? Source { get; set; } // URL of the web source (Tavily)
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
