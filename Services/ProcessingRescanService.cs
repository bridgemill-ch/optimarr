using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Optimarr.Data;
using Optimarr.Models;
using Optimarr.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Optimarr.Services
{
    public class ProcessingRescanService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProcessingRescanService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1); // Check every hour
        private readonly TimeSpan _rescanDelay = TimeSpan.FromHours(24); // Rescan after 24 hours

        public ProcessingRescanService(
            IServiceProvider serviceProvider,
            ILogger<ProcessingRescanService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }
        
        // Helper method to calculate OverallScore dynamically based on current thresholds
        private string CalculateOverallScore(int directPlayCount, int remuxCount, string? videoCodec = null, int bitDepth = 8)
        {
            // Get thresholds (using global thresholds for now, could be codec-specific)
            var optimalThreshold = _configuration.GetValue<int>("CompatibilityRating:OptimalDirectPlayThreshold", 8);
            var goodDirectThreshold = _configuration.GetValue<int>("CompatibilityRating:GoodDirectPlayThreshold", 5);
            var goodCombinedThreshold = _configuration.GetValue<int>("CompatibilityRating:GoodCombinedThreshold", 8);

            if (directPlayCount >= optimalThreshold)
                return "Optimal";
            else if (directPlayCount >= goodDirectThreshold || (directPlayCount + remuxCount) >= goodCombinedThreshold)
                return "Good";
            else
                return "Poor";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Processing rescan service started");

            // Wait a bit before first check to let the app fully start
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndRescanProcessingVideos(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in processing rescan service");
                }

                // Wait for the next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task CheckAndRescanProcessingVideos(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var videoAnalyzer = scope.ServiceProvider.GetRequiredService<VideoAnalyzerService>();
            var reportGenerator = new ReportGenerator();

            try
            {
                // Find videos that have been in Processing state for 24+ hours
                var cutoffTime = DateTime.UtcNow - _rescanDelay;
                var processingVideos = await dbContext.VideoAnalyses
                    .Where(v => v.ProcessingStatus == ProcessingStatus.Processing &&
                                v.ProcessingStartedAt.HasValue &&
                                v.ProcessingStartedAt.Value <= cutoffTime)
                    .ToListAsync(cancellationToken);

                if (processingVideos.Count == 0)
                {
                    _logger.LogDebug("No processing videos ready for rescan");
                    return;
                }

                _logger.LogInformation("Found {Count} processing video(s) ready for rescan (marked {Hours} hours ago or earlier)", 
                    processingVideos.Count, _rescanDelay.TotalHours);

                // Rescan each video
                int successCount = 0;
                int failCount = 0;

                foreach (var video in processingVideos)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        // Check if file exists
                        if (string.IsNullOrEmpty(video.FilePath) || !System.IO.File.Exists(video.FilePath))
                        {
                            _logger.LogWarning("Processing video {VideoId} file no longer exists: {FilePath}", 
                                video.Id, video.FilePath);
                            
                            // Remove from database if file doesn't exist
                            dbContext.VideoAnalyses.Remove(video);
                            await dbContext.SaveChangesAsync(cancellationToken);
                            failCount++;
                            continue;
                        }

                        _logger.LogInformation("Rescanning processing video {VideoId}: {FilePath}", video.Id, video.FilePath);

                        // Find external subtitles (using the same logic as LibraryScannerService)
                        var videoDir = System.IO.Path.GetDirectoryName(video.FilePath);
                        var videoNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(video.FilePath);
                        var subtitleExtensions = new[] { ".srt", ".vtt", ".ass", ".ssa", ".sub", ".idx", ".sup" };
                        var externalSubtitlePaths = new List<string>();
                        
                        if (!string.IsNullOrEmpty(videoDir) && System.IO.Directory.Exists(videoDir))
                        {
                            var allFiles = System.IO.Directory.GetFiles(videoDir, "*.*", System.IO.SearchOption.TopDirectoryOnly);
                            foreach (var file in allFiles)
                            {
                                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                                if (subtitleExtensions.Contains(ext))
                                {
                                    var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(file);
                                    if (fileNameWithoutExt.Equals(videoNameWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                                        fileNameWithoutExt.StartsWith(videoNameWithoutExt + ".", StringComparison.OrdinalIgnoreCase))
                                    {
                                        externalSubtitlePaths.Add(file);
                                    }
                                }
                            }
                        }

                        // Analyze the video
                        var (videoInfo, compatibilityResult) = videoAnalyzer.AnalyzeVideoStructured(
                            video.FilePath, 
                            externalSubtitlePaths);

                        // Update video record with new analysis
                        video.FileName = System.IO.Path.GetFileName(video.FilePath);
                        video.FileSize = new System.IO.FileInfo(video.FilePath).Length;
                        video.Duration = videoInfo.Duration;
                        video.Container = videoInfo.Container;
                        video.VideoCodec = videoInfo.VideoCodec;
                        video.VideoCodecTag = videoInfo.VideoCodecTag;
                        video.IsCodecTagCorrect = videoInfo.IsCodecTagCorrect;
                        video.BitDepth = videoInfo.BitDepth;
                        video.Width = videoInfo.Width;
                        video.Height = videoInfo.Height;
                        video.FrameRate = videoInfo.FrameRate;
                        video.IsHDR = videoInfo.IsHDR;
                        video.HDRType = videoInfo.HDRType;
                        video.IsFastStart = videoInfo.IsFastStart;
                        video.AudioCodecs = string.Join(",", videoInfo.AudioTracks.Select(t => t.Codec));
                        video.AudioTrackCount = videoInfo.AudioTracks.Count;
                        video.AudioTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo.AudioTracks);
                        video.SubtitleFormats = string.Join(",", videoInfo.SubtitleTracks.Select(t => t.Format));
                        video.SubtitleTrackCount = videoInfo.SubtitleTracks.Count;
                        video.SubtitleTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo.SubtitleTracks);
                        video.CompatibilityRating = compatibilityResult.CompatibilityRating;
                        video.DirectPlayClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Direct Play");
                        video.RemuxClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Remux");
                        video.TranscodeClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Transcode");
                        video.Issues = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.Issues);
                        video.Recommendations = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.Recommendations);
                        video.ClientResults = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.ClientResults);
                        
                        // Generate full report
                        video.FullReport = reportGenerator.GenerateReport(videoInfo, compatibilityResult);
                        
                        // Recalculate OverallScore
                        var recalculatedScore = CalculateOverallScore(
                            video.CompatibilityRating, 
                            video.RemuxClients, 
                            video.VideoCodec, 
                            video.BitDepth);
                        
                        if (Enum.TryParse<CompatibilityScore>(recalculatedScore, true, out var parsedScore))
                        {
                            video.OverallScore = parsedScore;
                        }
                        else
                        {
                            video.OverallScore = CompatibilityScore.Unknown;
                        }
                        video.IsBroken = false;
                        video.BrokenReason = null;
                        video.AnalyzedAt = DateTime.UtcNow;

                        // Reset processing status
                        video.ProcessingStatus = ProcessingStatus.None;
                        video.ProcessingStartedAt = null;

                        await dbContext.SaveChangesAsync(cancellationToken);
                        successCount++;
                        _logger.LogInformation("Successfully rescanned processing video {VideoId}", video.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error rescanning processing video {VideoId}: {FilePath}", 
                            video.Id, video.FilePath);
                        failCount++;
                    }
                }

                _logger.LogInformation("Completed rescan of processing videos: {SuccessCount} succeeded, {FailCount} failed", 
                    successCount, failCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking and rescanning processing videos");
            }
        }
    }
}
