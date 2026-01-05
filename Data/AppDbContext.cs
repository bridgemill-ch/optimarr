using Microsoft.EntityFrameworkCore;
using Optimarr.Models;

namespace Optimarr.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<LibraryScan> LibraryScans { get; set; }
        public DbSet<VideoAnalysis> VideoAnalyses { get; set; }
        public DbSet<LibraryPath> LibraryPaths { get; set; }
        public DbSet<FailedFile> FailedFiles { get; set; }
        public DbSet<PlaybackHistory> PlaybackHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // LibraryScan configuration
            modelBuilder.Entity<LibraryScan>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.StartedAt);
                entity.HasIndex(e => e.Status);
                entity.Property(e => e.Status)
                    .HasConversion<string>()
                    .HasMaxLength(50);
            });

            // VideoAnalysis configuration
            modelBuilder.Entity<VideoAnalysis>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.FilePath);
                entity.HasIndex(e => e.LibraryScanId);
                entity.HasIndex(e => e.AnalyzedAt);
                entity.HasIndex(e => e.OverallScore);
                entity.HasOne(e => e.LibraryScan)
                    .WithMany(s => s.VideoAnalyses)
                    .HasForeignKey(e => e.LibraryScanId)
                    .OnDelete(DeleteBehavior.Cascade);
                
                entity.Property(e => e.OverallScore)
                    .HasConversion<string>()
                    .HasMaxLength(50);
            });

            // LibraryPath configuration
            modelBuilder.Entity<LibraryPath>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Path).IsUnique();
                entity.HasIndex(e => e.IsActive);
            });

            // FailedFile configuration
            modelBuilder.Entity<FailedFile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.LibraryScanId);
                entity.HasIndex(e => e.FailedAt);
                entity.HasOne(e => e.LibraryScan)
                    .WithMany(s => s.FailedFileRecords)
                    .HasForeignKey(e => e.LibraryScanId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // PlaybackHistory configuration
            modelBuilder.Entity<PlaybackHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.JellyfinItemId);
                entity.HasIndex(e => e.FilePath);
                entity.HasIndex(e => e.PlaybackStartTime);
                entity.HasIndex(e => e.PlayMethod);
                entity.HasIndex(e => e.VideoAnalysisId);
                entity.HasIndex(e => e.LibraryPathId);
                entity.HasOne(e => e.VideoAnalysis)
                    .WithMany()
                    .HasForeignKey(e => e.VideoAnalysisId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.LibraryPath)
                    .WithMany()
                    .HasForeignKey(e => e.LibraryPathId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}

