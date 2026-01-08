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

                var historyItems = await jellyfinService.GetPlaybackHistoryAsync(startDate, DateTime.UtcNow);

                _logger.LogInformation("Retrieved {Count} playback history items from Jellyfin", historyItems.Count);

                var syncedCount = 0;
                var matchedCount = 0;

                foreach (var item in historyItems)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (string.IsNullOrEmpty(item.Path)) continue;

                    // Check if already exists
                    var existing = await dbContext.PlaybackHistories
                        .FirstOrDefaultAsync(p => 
                            p.JellyfinItemId == item.ItemId && 
                            p.PlaybackStartTime == item.PlaybackStartTime &&
                            p.JellyfinUserId == item.UserId, cancellationToken);

                    if (existing != null) continue;

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

                    dbContext.PlaybackHistories.Add(playback);
                    syncedCount++;
                    
                    if (playback.VideoAnalysisId != null || playback.LibraryPathId != null)
                    {
                        matchedCount++;
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                if (syncedCount > 0)
                {
                    _logger.LogInformation("Auto-synced {SyncedCount} playback records, matched {MatchedCount} with local libraries", 
                        syncedCount, matchedCount);
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
            var videoAnalysis = await dbContext.VideoAnalyses
                .FirstOrDefaultAsync(v => NormalizePath(v.FilePath) == normalizedPlaybackPath, cancellationToken);

            if (videoAnalysis != null)
            {
                playback.VideoAnalysisId = videoAnalysis.Id;
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

