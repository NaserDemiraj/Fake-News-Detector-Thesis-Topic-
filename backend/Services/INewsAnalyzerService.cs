using FakeNewsDetector.Models;

namespace FakeNewsDetector.Services
{
    public interface INewsAnalyzerService
    {
        Task<AnalysisResult> AnalyzeContentAsync(string content);
    }
}
