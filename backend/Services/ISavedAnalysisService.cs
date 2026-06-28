using FakeNewsDetector.Models;

namespace FakeNewsDetector.Services
{
    public interface ISavedAnalysisService
    {
        Task SaveAnalysisAsync(SavedAnalysis analysis);
        Task<List<SavedAnalysis>> GetRecentAnalysesAsync(int count, string userId);
        Task<List<SavedAnalysis>> GetAllAnalysesAsync(string userId);
        Task<SavedAnalysis?> GetAnalysisByIdAsync(string id);
        Task UpdateAnalysisAsync(string id, bool isFavorite, string notes, string userId);
        Task DeleteAnalysisAsync(string id, string userId);
        Task<SavedAnalysis?> GetByContentHashAsync(string hash);
        Task<SavedAnalysis?> GetPublicAnalysisAsync(string id);
        Task SetPublicAsync(string id, bool isPublic, string userId);
        Task<DomainStats> GetDomainStatsAsync(string host);
    }
}
