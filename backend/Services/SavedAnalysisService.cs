using FakeNewsDetector.Models;
using FakeNewsDetector.Data;
using Microsoft.EntityFrameworkCore;

namespace FakeNewsDetector.Services
{
    public class SavedAnalysisService : ISavedAnalysisService
    {
        private readonly FakeNewsDetectorDbContext _dbContext;
        private readonly ILogger<SavedAnalysisService> _logger;

        public SavedAnalysisService(FakeNewsDetectorDbContext dbContext, ILogger<SavedAnalysisService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public void SaveAnalysis(SavedAnalysis analysis)
        {
            try
            {
                if (string.IsNullOrEmpty(analysis.Id))
                    analysis.Id = Guid.NewGuid().ToString();

                if (analysis.Date == default)
                    analysis.Date = DateTime.UtcNow;

                _dbContext.SavedAnalyses.Add(analysis);
                _dbContext.SaveChanges();
                _logger.LogInformation("Analysis saved: {Title}", analysis.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving analysis: {Title}", analysis.Title);
                throw;
            }
        }

        public List<SavedAnalysis> GetRecentAnalyses(int count)
        {
            try
            {
                return _dbContext.SavedAnalyses
                    .OrderByDescending(a => a.Date)
                    .Take(count)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recent analyses");
                return new List<SavedAnalysis>();
            }
        }

        public List<SavedAnalysis> GetAllAnalyses()
        {
            try
            {
                return _dbContext.SavedAnalyses
                    .OrderByDescending(a => a.Date)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all analyses");
                return new List<SavedAnalysis>();
            }
        }

        public SavedAnalysis? GetAnalysisById(string id)
        {
            try
            {
                return _dbContext.SavedAnalyses.FirstOrDefault(a => a.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving analysis by id: {Id}", id);
                return null;
            }
        }

        public void UpdateAnalysis(string id, bool isFavorite, string notes)
        {
            try
            {
                var existing = _dbContext.SavedAnalyses.FirstOrDefault(a => a.Id == id);
                if (existing == null) return;

                existing.IsFavorite = isFavorite;
                existing.Notes = notes;
                _dbContext.SaveChanges();
                _logger.LogInformation("Analysis updated: {Id}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating analysis: {Id}", id);
                throw;
            }
        }

        public void DeleteAnalysis(string id)
        {
            try
            {
                var analysis = _dbContext.SavedAnalyses.FirstOrDefault(a => a.Id == id);
                if (analysis != null)
                {
                    _dbContext.SavedAnalyses.Remove(analysis);
                    _dbContext.SaveChanges();
                    _logger.LogInformation("Analysis deleted: {Id}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting analysis: {Id}", id);
                throw;
            }
        }
    }
}
