using FakeNewsDetector.Models;

namespace FakeNewsDetector.Services
{
    public interface INewsAnalyzerService
    {
        Task<AnalysisResult> AnalyzeContentAsync(string content, string? sourceUrl = null);

        // Consensus mode: query ALL configured providers in parallel and aggregate their
        // verdicts, reporting an inter-model agreement score. Opt-in (uses 3x the tokens).
        Task<AnalysisResult> AnalyzeContentEnsembleAsync(string content, string? sourceUrl = null);
    }
}
