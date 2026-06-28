namespace FakeNewsDetector.Models
{
    public class SavedAnalysis
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ContentType { get; set; } = "text"; // "url" or "text"
        public string Content { get; set; } = string.Empty; // original URL or truncated text
        public double Score { get; set; }
        public string Verdict { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string? ResultJson { get; set; } // full AnalysisResult serialized as JSON
        public bool IsFavorite { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? ContentHash { get; set; } // SHA-256 of extracted content for dedup
        public bool IsPublic { get; set; } = false; // shareable public link

        public string FormattedDate => Date.ToString("MMM dd, yyyy HH:mm");

        public string RelativeDate
        {
            get
            {
                var timeSpan = DateTime.UtcNow - Date;
                if (timeSpan.TotalMinutes < 1) return "Just now";
                if (timeSpan.TotalHours < 1) return $"{(int)timeSpan.TotalMinutes} minutes ago";
                if (timeSpan.TotalDays < 1) return $"{(int)timeSpan.TotalHours} hours ago";
                if (timeSpan.TotalDays < 7) return $"{(int)timeSpan.TotalDays} days ago";
                return Date.ToString("MMM dd, yyyy");
            }
        }
    }
}
