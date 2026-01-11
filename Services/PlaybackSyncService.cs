using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Optimarr.Data;
using Optimarr.Services;

namespace Optimarr.Services
{
    public class PlaybackSyncService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PlaybackSyncService> _logger;
        private readonly TimeSpan _syncInterval = TimeSpan.FromHours(1); // Sync every hour

        public PlaybackSyncService(
            IServiceProvider serviceProvider,
            ILogger<PlaybackSyncService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Playback sync service started");

            // Wait a bit before first sync to let the app fully start
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncPlaybackHistory(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in playback sync service");
                }

                // Wait for the next sync interval
                await Task.Delay(_syncInterval, stoppingToken);
            }
        }

        private async Task SyncPlaybackHistory(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jellyfinService = scope.ServiceProvider.GetRequiredService<JellyfinService>();

            if (!jellyfinService.IsEnabled || !jellyfinService.IsConnected)
            {
                _logger.LogDebug("Jellyfin service is not enabled or connected, skipping sync");
                return;
            }

            try
            {
                _logger.LogInformation("Starting automatic playback history sync");

                // Get the most recent playback in the database to sync only new items
                var lastSyncTime = await dbContext.PlaybackHistories
                    .OrderByDescending(p => p.PlaybackStartTime)
                    .Select(p => p.PlaybackStartTime)
                    .FirstOrDefaultAsync(cancellationToken);

                // Sync from last sync time (or last 7 days if no previous sync)
                var startDate = lastSyncTime > DateTime.MinValue 
                    ? lastSyncTime.AddMinutes(-5) // 5 minute overlap to catch any missed items
                    : DateTime.UtcNow.AddDays(-7);

                var syncedCount = 0;
                var matchedCount = 0;
                var skippedCount = 0;
                const int batchSize = 50; // Save in batches to balance performance and progress visibility
                var batch = new List<Models.PlaybackHistory>();

                _logger.LogInformation("Starting streaming playback history sync from {StartDate}", startDate);

                // Process items as they arrive (streaming)
                await foreach (var item in jellyfinService.GetPlaybackHistoryStreamAsync(startDate, DateTime.UtcNow))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (string.IsNullOrEmpty(item.Path))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Check if already exists (duplicate check)
                    var existing = await dbContext.PlaybackHistories
                        .FirstOrDefaultAsync(p => 
                            p.JellyfinItemId == item.ItemId && 
                            p.PlaybackStartTime == item.PlaybackStartTime &&
                            p.JellyfinUserId == item.UserId, cancellationToken);

                    if (existing != null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var playback = new Models.PlaybackHistory
                    {
                        JellyfinItemId = item.ItemId ?? string.Empty,
                        JellyfinUserId = item.UserId,
                        JellyfinUserName = item.UserName,
                        ItemName = item.ItemName ?? string.Empty,
                        ItemType = item.ItemType,
                        MediaType = item.MediaType,
                        FilePath = item.Path ?? string.Empty,
                        PlaybackStartTime = item.PlaybackStartTime,
                        PlaybackStopTime = item.PlaybackStopTime,
                        PlaybackDuration = item.PlaybackStopTime.HasValue 
                            ? item.PlaybackStopTime.Value - item.PlaybackStartTime 
                            : null,
                        ClientName = item.ClientName,
                        DeviceName = item.DeviceName,
                        PlayMethod = item.PlayMethod ?? "Unknown",
                        IsDirectPlay = item.IsDirectPlay,
                        IsDirectStream = item.IsDirectStream,
                        IsTranscode = item.IsTranscode,
                        TranscodeReason = item.TranscodeReason,
                        SyncedAt = DateTime.UtcNow
                    };

                    // Try to match with local library
                    await MatchPlaybackWithLibrary(playback, dbContext, cancellationToken);

                    batch.Add(playback);
                    syncedCount++;
                    
                    if (playback.VideoAnalysisId != null || playback.LibraryPathId != null)
                    {
                        matchedCount++;
                    }

                    // Save batch when it reaches the batch size
                    if (batch.Count >= batchSize)
                    {
                        dbContext.PlaybackHistories.AddRange(batch);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        _logger.LogDebug("Saved batch of {Count} playback records (total synced: {Total})", batch.Count, syncedCount);
                        batch.Clear();
                    }
                }

                // Save any remaining items in the batch
                if (batch.Count > 0)
                {
                    dbContext.PlaybackHistories.AddRange(batch);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogDebug("Saved final batch of {Count} playback records", batch.Count);
                }

                if (syncedCount > 0 || skippedCount > 0)
                {
                    _logger.LogInformation("Auto-synced {SyncedCount} playback records, matched {MatchedCount} with local libraries, skipped {SkippedCount} (duplicates or invalid)", 
                        syncedCount, matchedCount, skippedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in automatic playback history sync");
            }
        }

        private async Task MatchPlaybackWithLibrary(Models.PlaybackHistory playback, AppDbContext dbContext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(playback.FilePath)) return;

            // Normalize path for comparison
            var normalizedPlaybackPath = NormalizePath(playback.FilePath);

            // Try to match with VideoAnalysis by file path
            // Load video analyses into memory to avoid LINQ translation issues with NormalizePath
            var allVideoAnalyses = await dbContext.VideoAnalyses
                .Select(v => new { v.Id, v.FilePath })
                .ToListAsync(cancellationToken);

            foreach (var video in allVideoAnalyses)
            {
                if (!string.IsNullOrEmpty(video.FilePath))
                {
                    var normalizedVideoPath = NormalizePath(video.FilePath);
                    if (normalizedVideoPath == normalizedPlaybackPath)
                    {
                        playback.VideoAnalysisId = video.Id;
                        break;
                    }
                }
            }

            // Try to match with LibraryPath
            var libraryPaths = await dbContext.LibraryPaths
                .Where(lp => lp.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var libraryPath in libraryPaths)
            {
                var normalizedLibraryPath = NormalizePath(libraryPath.Path);
                if (normalizedPlaybackPath.StartsWith(normalizedLibraryPath, StringComparison.OrdinalIgnoreCase))
                {
                    playback.LibraryPathId = libraryPath.Id;
                    break;
                }
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            
            // Normalize path separators and case
            var normalized = path.Replace('\\', '/');
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                normalized = normalized.ToLowerInvariant();
            }
            
            return normalized.TrimEnd('/');
        }
    }
}

