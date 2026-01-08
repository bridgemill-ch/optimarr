using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Optimarr.Data;
using Optimarr.Models;
using Optimarr.Services;
using System.Collections.Concurrent;

namespace Optimarr.Services
{
    public class VideoServarrMatcherService
    {
        private readonly AppDbContext _dbContext;
        private readonly SonarrService _sonarrService;
        private readonly RadarrService _radarrService;
        private readonly ILogger<VideoServarrMatcherService> _logger;
        private readonly IConfiguration _configuration;

        public VideoServarrMatcherService(
            AppDbContext dbContext,
            SonarrService sonarrService,
            RadarrService radarrService,
            ILogger<VideoServarrMatcherService> logger,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Matches a video file with Sonarr/Radarr data based on file path
        /// </summary>
        public async Task MatchVideoWithServarrAsync(VideoAnalysis video, 
            Dictionary<string, SonarrEpisode>? sonarrEpisodes = null,
            Dictionary<int, SonarrSeries>? sonarrSeries = null,
            Dictionary<string, RadarrMovie>? radarrMovies = null)
        {
            if (string.IsNullOrEmpty(video.FilePath))
            {
                _logger.LogDebug("Skipping video {VideoId} - no file path", video.Id);
                return;
            }

            var normalizedPath = NormalizePath(video.FilePath);

            // Try to match with Sonarr first (using pre-loaded data if available)
            if (_sonarrService.IsEnabled && _sonarrService.IsConnected)
            {
                try
                {
                    SonarrEpisode? episode = null;
                    
                    // Use pre-loaded data if available (much faster)
                    if (sonarrEpisodes != null && sonarrEpisodes.TryGetValue(normalizedPath, out episode))
                    {
                        // Episode found in cache
                    }
                    else
                    {
                        // Fallback to API call if cache not available
                        episode = await _sonarrService.FindEpisodeByPath(video.FilePath);
                    }

                    if (episode != null)
                    {
                        // Get series information
                        SonarrSeries? series = null;
                        if (sonarrSeries != null && sonarrSeries.TryGetValue(episode.SeriesId, out series))
                        {
                            // Series found in cache
                        }
                        else
                        {
                            // Fallback to API call
                            var allSeries = await _sonarrService.GetSeries();
                            series = allSeries.FirstOrDefault(s => s.Id == episode.SeriesId);
                        }

                        if (series != null)
                        {
                            video.ServarrType = "Sonarr";
                            video.SonarrSeriesId = series.Id;
                            video.SonarrSeriesTitle = series.Title;
                            video.SonarrEpisodeId = episode.Id;
                            video.SonarrEpisodeNumber = episode.EpisodeNumber;
                            video.SonarrSeasonNumber = episode.SeasonNumber;
                            video.ServarrMatchedAt = DateTime.UtcNow;
                            _logger.LogDebug("Matched video {FilePath} with Sonarr series {SeriesTitle} S{Season}E{Episode}",
                                video.FilePath, series.Title, episode.SeasonNumber, episode.EpisodeNumber);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error matching video with Sonarr: {FilePath}", video.FilePath);
                }
            }

            // Try to match with Radarr (using pre-loaded data if available)
            if (_radarrService.IsEnabled && _radarrService.IsConnected)
            {
                try
                {
                    RadarrMovie? movie = null;
                    
                    // Use pre-loaded data if available (much faster)
                    if (radarrMovies != null && radarrMovies.TryGetValue(normalizedPath, out movie))
                    {
                        // Movie found in cache
                    }
                    else
                    {
                        // Fallback to API call if cache not available
                        movie = await _radarrService.FindMovieByPath(video.FilePath);
                    }

                    if (movie != null)
                    {
                        video.ServarrType = "Radarr";
                        video.RadarrMovieId = movie.Id;
                        video.RadarrMovieTitle = movie.Title;
                        // Try to extract year from path or use current year as fallback
                        video.RadarrYear = ExtractYearFromPath(video.FilePath) ?? DateTime.Now.Year;
                        video.ServarrMatchedAt = DateTime.UtcNow;
                        _logger.LogDebug("Matched video {FilePath} with Radarr movie {MovieTitle}",
                            video.FilePath, movie.Title);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error matching video with Radarr: {FilePath}", video.FilePath);
                }
            }

            // If no match found, clear any existing Servarr data
            if (!string.IsNullOrEmpty(video.ServarrType))
            {
                _logger.LogDebug("Clearing Servarr match for video {FilePath}", video.FilePath);
                video.ServarrType = null;
                video.SonarrSeriesId = null;
                video.SonarrSeriesTitle = null;
                video.SonarrEpisodeId = null;
                video.SonarrEpisodeNumber = null;
                video.SonarrSeasonNumber = null;
                video.RadarrMovieId = null;
                video.RadarrMovieTitle = null;
                video.RadarrYear = null;
                video.ServarrMatchedAt = null;
            }
        }

        /// <summary>
        /// Matches all videos in the database with Sonarr/Radarr data
        /// </summary>
        public async Task<int> MatchAllVideosAsync()
        {
            return await MatchAllVideosAsync(null, null);
        }

        /// <summary>
        /// Matches all videos in the database with Sonarr/Radarr data (with progress tracking)
        /// </summary>
        public async Task<int> MatchAllVideosAsync(VideoMatchingProgressService? progressService, string? matchId)
        {
            _logger.LogInformation("Starting video matching process...");
            
            // Get total count first without loading all videos
            var totalVideos = await _dbContext.VideoAnalyses
                .Where(v => !string.IsNullOrEmpty(v.FilePath))
                .CountAsync();

            _logger.LogInformation("Found {TotalVideos} videos to process", totalVideos);

            if (totalVideos == 0)
            {
                _logger.LogInformation("No videos to match");
                if (progressService != null && matchId != null)
                {
                    progressService.CompleteProgress(matchId, 0, 0);
                }
                return 0;
            }

            if (progressService != null && matchId != null)
            {
                progressService.UpdateProgress(matchId, 0, totalVideos, 0, 0, "Pre-loading Servarr data...");
            }
            
            // Update progress after Servarr data is loaded
            if (progressService != null && matchId != null)
            {
                progressService.UpdateProgress(matchId, 0, totalVideos, 0, 0, "Starting video matching...");
            }

            // Pre-load Servarr data to avoid repeated API calls
            _logger.LogInformation("Pre-loading Servarr data...");
            Dictionary<string, SonarrEpisode>? sonarrEpisodes = null;
            Dictionary<int, SonarrSeries>? sonarrSeries = null;
            Dictionary<string, RadarrMovie>? radarrMovies = null;

            if (_sonarrService.IsEnabled && _sonarrService.IsConnected)
            {
                try
                {
                    _logger.LogInformation("Loading Sonarr series and episodes...");
                    var allSeries = await _sonarrService.GetSeries();
                    sonarrSeries = allSeries.ToDictionary(s => s.Id, s => s);
                    _logger.LogInformation("Loaded {SeriesCount} series from Sonarr", allSeries.Count);

                    // Load all episodes in parallel for better performance
                    var sonarrEpisodesConcurrent = new ConcurrentDictionary<string, SonarrEpisode>();
                    var semaphore = new SemaphoreSlim(10, 10); // Limit to 10 concurrent series processing
                    var tasks = allSeries.Select(async series =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var episodes = await _sonarrService.GetEpisodesBySeries(series.Id);
                            var episodeFileTasks = episodes
                                .Where(e => e.EpisodeFileId.HasValue)
                                .Select(async episode =>
                                {
                                    try
                                    {
                                        var episodeFile = await _sonarrService.GetEpisodeFile(episode.EpisodeFileId!.Value);
                                        if (episodeFile != null && !string.IsNullOrEmpty(episodeFile.Path))
                                        {
                                            var normalizedPath = NormalizePath(episodeFile.Path);
                                            sonarrEpisodesConcurrent.TryAdd(normalizedPath, episode);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Error loading episode file {EpisodeFileId} for series {SeriesId}", 
                                            episode.EpisodeFileId.Value, series.Id);
                                    }
                                });
                            await Task.WhenAll(episodeFileTasks);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error loading episodes for series {SeriesId}: {Error}", series.Id, ex.Message);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                    await Task.WhenAll(tasks);
                    sonarrEpisodes = sonarrEpisodesConcurrent.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    _logger.LogInformation("Loaded {EpisodeCount} episode paths from Sonarr", sonarrEpisodes.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error pre-loading Sonarr data");
                }
            }

            if (_radarrService.IsEnabled && _radarrService.IsConnected)
            {
                try
                {
                    _logger.LogInformation("Loading Radarr movies...");
                    var movies = await _radarrService.GetMovies();
                    radarrMovies = new Dictionary<string, RadarrMovie>();
                    foreach (var movie in movies)
                    {
                        if (movie.MovieFile != null && !string.IsNullOrEmpty(movie.MovieFile.Path))
                        {
                            var normalizedPath = NormalizePath(movie.MovieFile.Path);
                            if (!radarrMovies.ContainsKey(normalizedPath))
                            {
                                radarrMovies[normalizedPath] = movie;
                            }
                        }
                    }
                    _logger.LogInformation("Loaded {MovieCount} movie paths from Radarr", radarrMovies.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error pre-loading Radarr data");
                }
            }

            _logger.LogInformation("Starting to match videos...");
            var matchedCount = 0;
            var processedCount = 0;
            var errorCount = 0;
            const int videoBatchSize = 500; // Process videos in batches of 500 to reduce memory usage
            const int saveBatchSize = 50; // Save every 50 videos
            const int logInterval = 100; // Log progress every 100 videos
            const int progressUpdateInterval = 10; // Update progress every 10 videos (more frequent for better UX)
            const int maxConcurrency = 10; // Maximum concurrent video matching operations

            var matchingSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var processedLock = new object();
            
            // Helper method to update progress (called from within lock)
            void UpdateProgressIfNeeded(int currentProcessed, int currentTotal, int currentMatched, int currentErrors, string? currentItem = null)
            {
                if (progressService != null && matchId != null)
                {
                    // Always update on first video, then every N videos, or if it's been a while
                    if (currentProcessed == 1 || 
                        currentProcessed % progressUpdateInterval == 0 || 
                        currentProcessed == currentTotal)
                    {
                        progressService.UpdateProgress(matchId, currentProcessed, currentTotal, currentMatched, currentErrors, currentItem);
                    }
                }
            }

            // Process videos in batches to reduce memory usage
            int skip = 0;
            while (skip < totalVideos)
            {
                var videoBatch = await _dbContext.VideoAnalyses
                    .Where(v => !string.IsNullOrEmpty(v.FilePath))
                    .OrderBy(v => v.Id)
                    .Skip(skip)
                    .Take(videoBatchSize)
                    .ToListAsync();

                if (videoBatch.Count == 0) break;

                _logger.LogInformation("Processing batch: {Skip} to {End} of {Total} videos", 
                    skip + 1, skip + videoBatch.Count, totalVideos);

                // Process videos in parallel with controlled concurrency
                var batchTasks = videoBatch.Select(async video =>
                {
                    await matchingSemaphore.WaitAsync();
                    try
                    {
                        // Skip videos that already have a valid match
                        var normalizedPath = NormalizePath(video.FilePath ?? string.Empty);
                        var alreadyMatched = false;
                        
                        if (!string.IsNullOrEmpty(video.ServarrType))
                        {
                            // Check if the existing match is still valid
                            if (video.ServarrType == "Sonarr" && sonarrEpisodes != null)
                            {
                                alreadyMatched = sonarrEpisodes.ContainsKey(normalizedPath);
                            }
                            else if (video.ServarrType == "Radarr" && radarrMovies != null)
                            {
                                alreadyMatched = radarrMovies.ContainsKey(normalizedPath);
                            }
                        }

                        if (!alreadyMatched)
                        {
                            var hadMatch = !string.IsNullOrEmpty(video.ServarrType);
                            await MatchVideoWithServarrAsync(video, sonarrEpisodes, sonarrSeries, radarrMovies);
                            
                            lock (processedLock)
                            {
                                if (!hadMatch && !string.IsNullOrEmpty(video.ServarrType))
                                {
                                    matchedCount++;
                                }
                                processedCount++;

                                var fileName = System.IO.Path.GetFileName(video.FilePath);
                                UpdateProgressIfNeeded(processedCount, totalVideos, matchedCount, errorCount, fileName);

                                // Log progress at intervals
                                if (processedCount % logInterval == 0)
                                {
                                    var percent = totalVideos > 0 ? (processedCount * 100 / totalVideos) : 0;
                                    _logger.LogInformation("Matching progress: {Processed}/{Total} videos processed ({Percent}%), {Matched} matched", 
                                        processedCount, totalVideos, percent, matchedCount);
                                }
                            }
                        }
                        else
                        {
                            // Video already matched, just increment counter and update progress
                            lock (processedLock)
                            {
                                processedCount++;
                                UpdateProgressIfNeeded(processedCount, totalVideos, matchedCount, errorCount);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (processedLock)
                        {
                            errorCount++;
                        }
                        _logger.LogWarning(ex, "Error processing video {VideoId} ({FilePath}): {Error}", 
                            video.Id, video.FilePath, ex.Message);
                    }
                    finally
                    {
                        matchingSemaphore.Release();
                    }
                });

                await Task.WhenAll(batchTasks);
                
                // Save changes after processing each batch
                await _dbContext.SaveChangesAsync();
                
                // Update progress after each batch completes
                lock (processedLock)
                {
                    // Always update progress after batch completion to ensure UI reflects current state
                    UpdateProgressIfNeeded(processedCount, totalVideos, matchedCount, errorCount);
                    
                    if (processedCount % saveBatchSize < videoBatchSize || processedCount % logInterval < videoBatchSize)
                    {
                        _logger.LogInformation("Progress: {Processed}/{Total} videos processed, {Matched} matched so far", 
                            processedCount, totalVideos, matchedCount);
                    }
                }
                
                skip += videoBatchSize;
            }

            // Final save
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Video matching completed: {Processed}/{Total} processed, {Matched} matched, {Errors} errors", 
                processedCount, totalVideos, matchedCount, errorCount);
            
            // Update final progress
            if (progressService != null && matchId != null)
            {
                progressService.CompleteProgress(matchId, matchedCount, errorCount);
            }
            
            return matchedCount;
        }

        /// <summary>
        /// Matches videos for a specific library path
        /// </summary>
        public async Task<int> MatchVideosForLibraryPathAsync(int libraryPathId)
        {
            var libraryPath = await _dbContext.LibraryPaths.FindAsync(libraryPathId);
            if (libraryPath == null)
            {
                _logger.LogWarning("Library path {LibraryPathId} not found", libraryPathId);
                return 0;
            }

            _logger.LogInformation("Starting video matching for library path: {Path}", libraryPath.Path);

            // Use database-friendly query (avoid NormalizePath in LINQ)
            var libraryPathLower = libraryPath.Path.Replace('\\', '/').ToLowerInvariant();
            var totalVideos = await _dbContext.VideoAnalyses
                .Where(v => !string.IsNullOrEmpty(v.FilePath) && 
                           (v.FilePath.Replace('\\', '/').ToLowerInvariant().StartsWith(libraryPathLower) ||
                            v.FilePath.Replace('\\', '/').ToLowerInvariant().StartsWith(libraryPathLower + "/")))
                .CountAsync();

            _logger.LogInformation("Found {TotalVideos} videos in library path to process", totalVideos);

            if (totalVideos == 0)
            {
                _logger.LogInformation("No videos found in library path");
                return 0;
            }

            // Pre-load Servarr data (same optimized approach as MatchAllVideosAsync)
            Dictionary<string, SonarrEpisode>? sonarrEpisodes = null;
            Dictionary<int, SonarrSeries>? sonarrSeries = null;
            Dictionary<string, RadarrMovie>? radarrMovies = null;

            if (_sonarrService.IsEnabled && _sonarrService.IsConnected)
            {
                try
                {
                    var allSeries = await _sonarrService.GetSeries();
                    sonarrSeries = allSeries.ToDictionary(s => s.Id, s => s);
                    var sonarrEpisodesConcurrent = new ConcurrentDictionary<string, SonarrEpisode>();
                    var semaphore = new SemaphoreSlim(10, 10);
                    var tasks = allSeries.Select(async series =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var episodes = await _sonarrService.GetEpisodesBySeries(series.Id);
                            var episodeFileTasks = episodes
                                .Where(e => e.EpisodeFileId.HasValue)
                                .Select(async episode =>
                                {
                                    try
                                    {
                                        var episodeFile = await _sonarrService.GetEpisodeFile(episode.EpisodeFileId!.Value);
                                        if (episodeFile != null && !string.IsNullOrEmpty(episodeFile.Path))
                                        {
                                            var normalizedPath = NormalizePath(episodeFile.Path);
                                            sonarrEpisodesConcurrent.TryAdd(normalizedPath, episode);
                                        }
                                    }
                                    catch { }
                                });
                            await Task.WhenAll(episodeFileTasks);
                        }
                        catch { }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                    await Task.WhenAll(tasks);
                    sonarrEpisodes = sonarrEpisodesConcurrent.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }
                catch { }
            }

            if (_radarrService.IsEnabled && _radarrService.IsConnected)
            {
                try
                {
                    var movies = await _radarrService.GetMovies();
                    radarrMovies = new Dictionary<string, RadarrMovie>();
                    foreach (var movie in movies)
                    {
                        if (movie.MovieFile != null && !string.IsNullOrEmpty(movie.MovieFile.Path))
                        {
                            var normalizedPath = NormalizePath(movie.MovieFile.Path);
                            if (!radarrMovies.ContainsKey(normalizedPath))
                            {
                                radarrMovies[normalizedPath] = movie;
                            }
                        }
                    }
                }
                catch { }
            }

            var matchedCount = 0;
            var processedCount = 0;
            var errorCount = 0;
            const int videoBatchSize = 500;
            const int maxConcurrency = 10;

            var matchingSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var processedLock = new object();

            // Process videos in batches
            int skip = 0;
            while (skip < totalVideos)
            {
                var videoBatch = await _dbContext.VideoAnalyses
                    .Where(v => !string.IsNullOrEmpty(v.FilePath) && 
                               (v.FilePath.Replace('\\', '/').ToLowerInvariant().StartsWith(libraryPathLower) ||
                                v.FilePath.Replace('\\', '/').ToLowerInvariant().StartsWith(libraryPathLower + "/")))
                    .OrderBy(v => v.Id)
                    .Skip(skip)
                    .Take(videoBatchSize)
                    .ToListAsync();

                if (videoBatch.Count == 0) break;

                // Process videos in parallel with controlled concurrency
                var batchTasks = videoBatch.Select(async video =>
                {
                    await matchingSemaphore.WaitAsync();
                    try
                    {
                        var normalizedPath = NormalizePath(video.FilePath ?? string.Empty);
                        var alreadyMatched = false;
                        
                        if (!string.IsNullOrEmpty(video.ServarrType))
                        {
                            if (video.ServarrType == "Sonarr" && sonarrEpisodes != null)
                            {
                                alreadyMatched = sonarrEpisodes.ContainsKey(normalizedPath);
                            }
                            else if (video.ServarrType == "Radarr" && radarrMovies != null)
                            {
                                alreadyMatched = radarrMovies.ContainsKey(normalizedPath);
                            }
                        }

                        if (!alreadyMatched)
                        {
                            var hadMatch = !string.IsNullOrEmpty(video.ServarrType);
                            await MatchVideoWithServarrAsync(video, sonarrEpisodes, sonarrSeries, radarrMovies);
                            
                            lock (processedLock)
                            {
                                if (!hadMatch && !string.IsNullOrEmpty(video.ServarrType))
                                {
                                    matchedCount++;
                                }
                                processedCount++;
                            }
                        }
                        else
                        {
                            lock (processedLock)
                            {
                                processedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (processedLock)
                        {
                            errorCount++;
                        }
                        _logger.LogWarning(ex, "Error processing video {VideoId} ({FilePath})", video.Id, video.FilePath);
                    }
                    finally
                    {
                        matchingSemaphore.Release();
                    }
                });

                await Task.WhenAll(batchTasks);
                await _dbContext.SaveChangesAsync();
                skip += videoBatchSize;
            }

            // Final save
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Library path matching completed: {Processed}/{Total} processed, {Matched} matched, {Errors} errors for path {Path}", 
                processedCount, totalVideos, matchedCount, errorCount, libraryPath.Path);
            
            return matchedCount;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        private int? ExtractYearFromPath(string path)
        {
            // Try to extract year from path (common patterns: (2023), [2023], 2023, etc.)
            var yearPattern = new System.Text.RegularExpressions.Regex(@"\b(19|20)\d{2}\b");
            var match = yearPattern.Match(path);
            if (match.Success && int.TryParse(match.Value, out var year))
            {
                if (year >= 1900 && year <= DateTime.Now.Year + 1)
                {
                    return year;
                }
            }
            return null;
        }
    }
}
