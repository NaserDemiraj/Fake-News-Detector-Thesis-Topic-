using Microsoft.EntityFrameworkCore;
using FakeNewsDetector.Models;

namespace FakeNewsDetector.Data
{
    public class FakeNewsDetectorDbContext : DbContext
    {
        public FakeNewsDetectorDbContext(DbContextOptions<FakeNewsDetectorDbContext> options)
            : base(options)
        {
        }

        public DbSet<SavedAnalysis> SavedAnalyses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SavedAnalysis>(entity =>
            {
                entity.HasKey(sa => sa.Id);
                entity.Property(sa => sa.Id).ValueGeneratedOnAdd();

                entity.Property(sa => sa.Title).IsRequired().HasMaxLength(500);
                entity.Property(sa => sa.Url).HasMaxLength(2000);
                entity.Property(sa => sa.ContentType).HasMaxLength(10).HasDefaultValue("text");
                entity.Property(sa => sa.Content).HasColumnType("text");
                entity.Property(sa => sa.Verdict).HasMaxLength(100);
                entity.Property(sa => sa.Date).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(sa => sa.ResultJson).HasColumnType("text");
                entity.Property(sa => sa.IsFavorite).HasDefaultValue(false);
                entity.Property(sa => sa.Notes).HasDefaultValue("").HasColumnType("text");

                // Computed properties are not mapped to columns
                entity.Ignore(sa => sa.FormattedDate);
                entity.Ignore(sa => sa.RelativeDate);
            });
        }
    }
}
