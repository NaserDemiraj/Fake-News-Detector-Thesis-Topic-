namespace FakeNewsDetector.Models
{
    public class AnalysisRequest
    {
        public string Type { get; set; } = "text"; // "text" or "url"
        public string Content { get; set; } = string.Empty;
        public bool Ensemble { get; set; } = false; // opt-in consensus mode (queries all LLMs)
    }
}

