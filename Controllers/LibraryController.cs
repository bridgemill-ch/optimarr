using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Optimarr.Data;
using Optimarr.Models;
using Optimarr.Services;
using System.IO;
using System.Linq;
using System.Security;

namespace Optimarr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LibraryController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly LibraryScannerService _scannerService;
        private readonly ILogger<LibraryController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly IServiceScopeFactory _scopeFactory;

        public LibraryController(
            AppDbContext dbContext,
            LibraryScannerService scannerService,
            ILogger<LibraryController> logger,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            IServiceScopeFactory scopeFactory)
        {
            _dbContext = dbContext;
            _scannerService = scannerService;
            _logger = logger;
            _configuration = configuration;
            _environment = environment;
            _scopeFactory = scopeFactory;
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

        [HttpPost("scan")]
        public async Task<ActionResult<LibraryScan>> StartScan([FromBody] ScanRequest request)
        {
            _logger.LogInformation("Library scan requested for path: {Path}", request.Path);
            
            try
            {
                var scan = await _scannerService.StartScanAsync(request.Path, request.Name, request.Category);
                return Ok(scan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting library scan");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("rescan/{libraryPathId}")]
        public async Task<ActionResult<LibraryScan>> RescanLibrary(int libraryPathId)
        {
            _logger.LogInformation("Rescan requested for library path ID: {Id}", libraryPathId);
            
            try
            {
                var libraryPath = await _dbContext.LibraryPaths.FindAsync(libraryPathId);
                if (libraryPath == null)
                {
                    return NotFound(new { error = $"Library path with ID {libraryPathId} not found" });
                }

                if (!libraryPath.IsActive)
                {
                    return BadRequest(new { error = "Library path is not active" });
                }

                var scan = await _scannerService.StartScanAsync(libraryPath.Path, libraryPath.Name, libraryPath.Category);
                return Ok(scan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rescanning library");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("rescan")]
        public async Task<ActionResult<LibraryScan>> RescanLibraryByPath([FromBody] RescanRequest request)
        {
            _logger.LogInformation("Rescan requested for path: {Path}", request.Path);
            
            try
            {
                var scan = await _scannerService.StartScanAsync(request.Path, request.Name, request.Category);
                return Ok(scan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rescanning library");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("browse")]
        public IActionResult BrowseDirectory([FromQuery] string? path = null)
        {
            try
            {
                // Default to root paths based on OS
                string browsePath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    // Start from common root paths
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        browsePath = "C:\\";
                    }
                    else
                    {
                        browsePath = "/";
                    }
                }
                else
                {
                    browsePath = path;
                }

                // Security: Validate path exists and is accessible
                if (!Directory.Exists(browsePath))
                {
                    return Ok(new DirectoryBrowseResponse
                    {
                        CurrentPath = browsePath,
                        Error = "Directory does not exist"
                    });
                }

                var items = new List<DirectoryItem>();
                string? parentPath = null;

                try
                {
                    // Get parent directory
                    var parentDir = Directory.GetParent(browsePath);
                    if (parentDir != null && parentDir.Exists)
                    {
                        parentPath = parentDir.FullName;
                    }

                    // Get directories
                    var directories = Directory.GetDirectories(browsePath);
                    foreach (var dir in directories.OrderBy(d => d))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            items.Add(new DirectoryItem
                            {
                                Name = dirInfo.Name,
                                FullPath = dirInfo.FullName,
                                IsDirectory = true
                            });
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip directories we can't access
                            continue;
                        }
                        catch (SecurityException)
                        {
                            // Skip directories we don't have permission for
                            continue;
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Ok(new DirectoryBrowseResponse
                    {
                        CurrentPath = browsePath,
                        Error = $"Access denied: {ex.Message}"
                    });
                }
                catch (SecurityException ex)
                {
                    return Ok(new DirectoryBrowseResponse
                    {
                        CurrentPath = browsePath,
                        Error = $"Security error: {ex.Message}"
                    });
                }

                return Ok(new DirectoryBrowseResponse
                {
                    CurrentPath = browsePath,
                    ParentPath = parentPath,
                    Items = items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error browsing directory: {Path}", path);
                return Ok(new DirectoryBrowseResponse
                {
                    CurrentPath = path ?? "",
                    Error = $"Error: {ex.Message}"
                });
            }
        }

        [HttpDelete("paths/{id}")]
        public async Task<ActionResult> DeleteLibraryPath(int id)
        {
            _logger.LogInformation("Delete requested for library path ID: {Id}", id);
            
            try
            {
                var libraryPath = await _dbContext.LibraryPaths.FindAsync(id);
                if (libraryPath == null)
                {
                    return NotFound(new { error = $"Library path with ID {id} not found" });
                }

                var libraryPathValue = libraryPath.Path;
                _logger.LogInformation("Starting bulk delete for library path: {Path}", libraryPathValue);

                // Get scan IDs first (just IDs, not full objects)
                var scanIds = await _dbContext.LibraryScans
                    .Where(s => s.LibraryPath == libraryPathValue)
                    .Select(s => s.Id)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} scans to delete", scanIds.Count);

                if (scanIds.Count > 0)
                {
                    // Delete scans using raw SQL - CASCADE deletes will automatically handle
                    // VideoAnalyses and FailedFiles (configured in AppDbContext)
                    // Delete in batches to avoid SQLite parameter limits and improve performance
                    const int batchSize = 500; // Conservative batch size for SQLite
                    int totalScansDeleted = 0;
                    
                    for (int i = 0; i < scanIds.Count; i += batchSize)
                    {
                        var batch = scanIds.Skip(i).Take(batchSize).ToList();
                        var batchNumber = (i / batchSize) + 1;
                        var totalBatches = (scanIds.Count + batchSize - 1) / batchSize;
                        
                        _logger.LogInformation("Deleting scans batch {Batch}/{Total} ({Count} scans)", 
                            batchNumber, totalBatches, batch.Count);
                        
                        // Use parameterized query to avoid SQL injection (though IDs are safe)
                        // SQLite doesn't support array parameters well, so we'll use string interpolation
                        // but only with integer IDs from the database (safe)
                        var scanIdsParam = string.Join(",", batch);
                        var deleteScansSql = $@"
                            DELETE FROM LibraryScans 
                            WHERE Id IN ({scanIdsParam})";
                        
                        var scansDeleted = await _dbContext.Database.ExecuteSqlRawAsync(deleteScansSql);
                        totalScansDeleted += scansDeleted;
                        
                        _logger.LogInformation("Deleted {Count} scans in batch {Batch} (CASCADE deleted related VideoAnalyses and FailedFiles)", 
                            scansDeleted, batchNumber);
                    }
                    
                    _logger.LogInformation("Total scans deleted: {Count} (with CASCADE deletes for related data)", totalScansDeleted);
                }

                // Delete the library path
                _dbContext.LibraryPaths.Remove(libraryPath);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Library path {Id} deleted successfully", id);
                return Ok(new { message = "Library deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting library path");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("paths")]
        public async Task<ActionResult<List<LibraryPathInfo>>> GetLibraryPaths()
        {
            var libraryPaths = await _dbContext.LibraryPaths
                .Where(lp => lp.IsActive)
                .OrderByDescending(lp => lp.LastScannedAt ?? lp.CreatedAt)
                .ToListAsync();
            
            var result = new List<LibraryPathInfo>();
            foreach (var lp in libraryPaths)
            {
                // Get latest scan info
                var latestScan = await _dbContext.LibraryScans
                    .Where(s => s.LibraryPath == lp.Path)
                    .OrderByDescending(s => s.StartedAt)
                    .FirstOrDefaultAsync();
                
                result.Add(new LibraryPathInfo
                {
                    Id = lp.Id,
                    Path = lp.Path,
                    Name = lp.Name,
                    Category = lp.Category,
                    IsActive = lp.IsActive,
                    CreatedAt = lp.CreatedAt,
                    LastScannedAt = lp.LastScannedAt,
                    TotalFiles = lp.TotalFiles,
                    TotalSize = lp.TotalSize,
                    LatestScanStatus = latestScan?.Status.ToString(),
                    LatestScanStartedAt = latestScan?.StartedAt,
                    LatestScanCompletedAt = latestScan?.CompletedAt,
                    LatestScanProcessedFiles = latestScan?.ProcessedFiles ?? 0,
                    LatestScanFailedFiles = latestScan?.FailedFiles ?? 0
                });
            }
            
            return Ok(result);
        }

        [HttpGet("scans")]
        public async Task<ActionResult<List<LibraryScan>>> GetScans()
        {
            var scans = await _dbContext.LibraryScans
                .OrderByDescending(s => s.StartedAt)
                .Take(50)
                .ToListAsync();
            
            return Ok(scans);
        }

        [HttpGet("scans/{id}")]
        public async Task<ActionResult<ScanProgress>> GetScan(int id)
        {
            var scan = await _dbContext.LibraryScans
                .Include(s => s.VideoAnalyses)
                .Include(s => s.FailedFileRecords)
                .FirstOrDefaultAsync(s => s.Id == id);
            
            if (scan == null)
                return NotFound();
            
            // Get current processing file from database (persists across page reloads)
            var currentFile = scan.CurrentProcessingFile;
            
            // Calculate progress metrics
            var elapsed = DateTime.UtcNow - scan.StartedAt;
            var progress = scan.TotalFiles > 0 
                ? (double)scan.ProcessedFiles / scan.TotalFiles * 100 
                : 0;
            
            var filesPerSecond = elapsed.TotalSeconds > 0 && scan.ProcessedFiles > 0
                ? scan.ProcessedFiles / elapsed.TotalSeconds
                : 0;
            
            var remainingFiles = scan.TotalFiles - scan.ProcessedFiles;
            var estimatedTimeRemaining = filesPerSecond > 0 && remainingFiles > 0
                ? TimeSpan.FromSeconds(remainingFiles / filesPerSecond)
                : TimeSpan.Zero;
            
            // Get most recently analyzed file
            var lastAnalyzed = scan.VideoAnalyses
                .OrderByDescending(v => v.AnalyzedAt)
                .FirstOrDefault();
            
            // Get failed files with error details
            var failedFilesList = scan.FailedFileRecords?
                .OrderByDescending(f => f.FailedAt)
                .Select(f => new FailedFileInfo
                {
                    FileName = f.FileName ?? System.IO.Path.GetFileName(f.FilePath),
                    FilePath = f.FilePath,
                    ErrorMessage = f.ErrorMessage,
                    ErrorType = f.ErrorType,
                    FailedAt = f.FailedAt,
                    FileSize = f.FileSize
                })
                .ToList();
            
            return Ok(new ScanProgress
            {
                Id = scan.Id,
                LibraryPath = scan.LibraryPath,
                Status = scan.Status.ToString(),
                StartedAt = scan.StartedAt,
                CompletedAt = scan.CompletedAt,
                TotalFiles = scan.TotalFiles,
                ProcessedFiles = scan.ProcessedFiles,
                FailedFiles = scan.FailedFiles,
                ErrorMessage = scan.ErrorMessage,
                Progress = Math.Round(progress, 2),
                ElapsedTime = elapsed,
                FilesPerSecond = Math.Round(filesPerSecond, 2),
                EstimatedTimeRemaining = estimatedTimeRemaining,
                CurrentFile = currentFile ?? (lastAnalyzed?.FileName ?? "Initializing..."),
                LastAnalyzedFile = lastAnalyzed?.FileName,
                LastAnalyzedAt = lastAnalyzed?.AnalyzedAt,
                FailedFilesList = failedFilesList ?? new List<FailedFileInfo>()
            });
        }

        [HttpPost("scans/{id}/cancel")]
        public async Task<ActionResult> CancelScan(int id)
        {
            await _scannerService.CancelScan(id);
            return Ok(new { message = "Scan cancellation requested" });
        }

        [HttpGet("dashboard/stats")]
        public async Task<ActionResult<DashboardStats>> GetDashboardStats()
        {
            try
            {
                var totalVideos = await _dbContext.VideoAnalyses.CountAsync();
                
                // Get thresholds once
                var optimalThreshold = _configuration.GetValue<int>("CompatibilityRating:OptimalDirectPlayThreshold", 8);
                var goodDirectThreshold = _configuration.GetValue<int>("CompatibilityRating:GoodDirectPlayThreshold", 5);
                var goodCombinedThreshold = _configuration.GetValue<int>("CompatibilityRating:GoodCombinedThreshold", 8);
                
                // Calculate broken videos count
                var brokenCount = await _dbContext.VideoAnalyses.CountAsync(v => v.IsBroken);
                
                // Calculate counts efficiently by loading only necessary fields (excluding broken videos)
                var videoScores = await _dbContext.VideoAnalyses
                    .Where(v => !v.IsBroken)
                    .Select(v => new { v.CompatibilityRating, v.RemuxClients })
                    .ToListAsync();
                
                var optimalCount = videoScores.Count(v => v.CompatibilityRating >= optimalThreshold);
                var goodCount = videoScores.Count(v => 
                    v.CompatibilityRating < optimalThreshold && // Not optimal
                    ((v.CompatibilityRating >= goodDirectThreshold) || 
                     ((v.CompatibilityRating + v.RemuxClients) >= goodCombinedThreshold)));
                var poorCount = videoScores.Count - optimalCount - goodCount;
            
                var totalSize = await _dbContext.VideoAnalyses.SumAsync(v => v.FileSize);
                var totalDuration = await _dbContext.VideoAnalyses.SumAsync(v => v.Duration);
                
                var codecStats = await _dbContext.VideoAnalyses
                    .Where(v => !string.IsNullOrEmpty(v.VideoCodec) && v.VideoCodec != "NULL")
                    .GroupBy(v => v.VideoCodec)
                    .Select(g => new { Codec = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToListAsync();
                
                var containerStats = await _dbContext.VideoAnalyses
                    .Where(v => !string.IsNullOrEmpty(v.Container) && v.Container != "NULL")
                    .GroupBy(v => v.Container)
                    .Select(g => new { Container = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToListAsync();
                
                var hdrCount = await _dbContext.VideoAnalyses.CountAsync(v => v.IsHDR);
                
                var recentScans = await _dbContext.LibraryScans
                    .OrderByDescending(s => s.StartedAt)
                    .Take(5)
                    .ToListAsync();

                return Ok(new DashboardStats
                {
                    TotalVideos = totalVideos,
                    OptimalCount = optimalCount,
                    GoodCount = goodCount,
                    PoorCount = poorCount,
                    BrokenCount = brokenCount,
                    TotalSize = totalSize,
                    TotalDuration = totalDuration,
                    HdrCount = hdrCount,
                    CodecDistribution = codecStats.ToDictionary(x => x.Codec, x => x.Count),
                    ContainerDistribution = containerStats.ToDictionary(x => x.Container, x => x.Count),
                    RecentScans = recentScans
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard stats");
                return StatusCode(500, new { error = $"Failed to load dashboard stats: {ex.Message}" });
            }
        }

        [HttpGet("dashboard/compatibility")]
        public async Task<ActionResult<CompatibilityBreakdown>> GetCompatibilityBreakdown()
        {
            // Calculate breakdown dynamically based on current thresholds
            var allVideos = await _dbContext.VideoAnalyses.ToListAsync();
            var breakdown = allVideos
                .GroupBy(v => CalculateOverallScore(v.CompatibilityRating, v.RemuxClients, v.VideoCodec, v.BitDepth))
                .Select(g => new
                {
                    Score = g.Key,
                    Count = g.Count(),
                    AvgDirectPlay = g.Average(v => v.DirectPlayClients),
                    AvgRemux = g.Average(v => v.RemuxClients),
                    AvgTranscode = g.Average(v => v.TranscodeClients)
                })
                .ToList();

            return Ok(new CompatibilityBreakdown
            {
                Breakdown = breakdown.Select(b => new ScoreBreakdown
                {
                    Score = b.Score.ToString(),
                    Count = b.Count,
                    AvgDirectPlay = (int)Math.Round(b.AvgDirectPlay),
                    AvgRemux = (int)Math.Round(b.AvgRemux),
                    AvgTranscode = (int)Math.Round(b.AvgTranscode)
                }).ToList()
            });
        }

        [HttpGet("dashboard/issues")]
        public async Task<ActionResult<object>> GetTopIssues([FromQuery] int limit = 10)
        {
            var videos = await _dbContext.VideoAnalyses
                .Where(v => v.OverallScore == CompatibilityScore.Poor || v.OverallScore == CompatibilityScore.Good)
                .OrderByDescending(v => v.TranscodeClients)
                .Take(limit)
                .Select(v => new IssueSummary
                {
                    Id = v.Id,
                    FileName = v.FileName,
                    FilePath = v.FilePath,
                    VideoCodec = v.VideoCodec,
                    Container = v.Container,
                    OverallScore = v.OverallScore.ToString(),
                    DirectPlayClients = v.DirectPlayClients,
                    RemuxClients = v.RemuxClients,
                    TranscodeClients = v.TranscodeClients,
                    IsHDR = v.IsHDR
                })
                .ToListAsync();

            // Calculate optimization summary
            var allVideos = await _dbContext.VideoAnalyses.ToListAsync();
            var totalVideos = allVideos.Count;
            var poorVideos = allVideos.Count(v => v.OverallScore == CompatibilityScore.Poor);
            var goodVideos = allVideos.Count(v => v.OverallScore == CompatibilityScore.Good);
            var optimalVideos = allVideos.Count(v => v.OverallScore == CompatibilityScore.Optimal);
            
            // Calculate videos that could benefit from optimization (Poor + some Good)
            var videosNeedingOptimization = poorVideos;
            var videosWithTranscoding = allVideos.Count(v => v.TranscodeClients > 0);
            var avgTranscodeClients = allVideos.Count > 0 ? allVideos.Average(v => v.TranscodeClients) : 0;
            var avgDirectPlayClients = allVideos.Count > 0 ? allVideos.Average(v => v.DirectPlayClients) : 0;

            return Ok(new
            {
                issues = videos,
                optimizationSummary = new
                {
                    totalVideos,
                    optimalVideos,
                    goodVideos,
                    poorVideos,
                    videosNeedingOptimization,
                    videosWithTranscoding,
                    avgTranscodeClients = Math.Round(avgTranscodeClients, 1),
                    avgDirectPlayClients = Math.Round(avgDirectPlayClients, 1),
                    optimizationPotential = totalVideos > 0 ? Math.Round((double)videosNeedingOptimization / totalVideos * 100, 1) : 0
                }
            });
        }

        [HttpGet("dashboard/client-compatibility")]
        public async Task<ActionResult<object>> GetClientCompatibilityStats()
        {
            var allVideos = await _dbContext.VideoAnalyses.ToListAsync();
            
            // Get enabled clients (filter out disabled ones)
            var allClientsList = Services.JellyfinCompatibilityData.AllClients;
            var disabledClientsSection = _configuration?.GetSection("CompatibilityRating:DisabledClients");
            var enabledClients = new List<string>();
            
            foreach (var client in allClientsList)
            {
                var isDisabled = disabledClientsSection?.GetValue<bool>(client) ?? false;
                if (!isDisabled)
                {
                    enabledClients.Add(client);
                }
            }
            
            // If all clients are disabled, enable all by default
            var allClients = enabledClients.Count > 0 ? enabledClients.ToArray() : allClientsList;
            
            var clientStats = new Dictionary<string, object>();
            
            foreach (var client in allClients)
            {
                var directPlayCount = 0;
                var remuxCount = 0;
                var transcodeCount = 0;
                var totalVideos = allVideos.Count;
                
                foreach (var video in allVideos)
                {
                    if (string.IsNullOrEmpty(video.ClientResults)) continue;
                    
                    try
                    {
                        var clientResults = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Models.ClientCompatibility>>(video.ClientResults);
                        if (clientResults != null && clientResults.ContainsKey(client))
                        {
                            var clientResult = clientResults[client];
                            var status = clientResult?.Status ?? "";
                            
                            if (status == "Direct Play") directPlayCount++;
                            else if (status == "Remux") remuxCount++;
                            else if (status == "Transcode") transcodeCount++;
                        }
                    }
                    catch
                    {
                        // Skip videos with invalid client results
                    }
                }
                
                var compatibilityPercent = totalVideos > 0 
                    ? Math.Round((double)directPlayCount / totalVideos * 100, 1) 
                    : 0;
                
                var compatibilityLevel = compatibilityPercent >= 80 ? "Excellent" :
                                        compatibilityPercent >= 60 ? "Good" :
                                        compatibilityPercent >= 40 ? "Fair" :
                                        compatibilityPercent >= 20 ? "Poor" : "Very Poor";
                
                clientStats[client] = new
                {
                    directPlayCount,
                    remuxCount,
                    transcodeCount,
                    totalVideos,
                    compatibilityPercent,
                    compatibilityLevel
                };
            }
            
            return Ok(new { clients = clientStats });
        }

        [HttpGet("videos")]
        public async Task<ActionResult<PagedResult<VideoAnalysis>>> GetVideos(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? codec = null,
            [FromQuery] string? container = null,
            [FromQuery] string? score = null,
            [FromQuery] int? libraryPathId = null,
            [FromQuery] string? search = null,
            [FromQuery] string? sortBy = "analyzedAt",
            [FromQuery] string? sortOrder = "desc",
            [FromQuery] bool? isBroken = null,
            [FromQuery] string? servarrFilter = null)
        {
            try
            {
                var query = _dbContext.VideoAnalyses.AsQueryable();

                if (!string.IsNullOrEmpty(codec))
                    query = query.Where(v => v.VideoCodec == codec);
                
                if (!string.IsNullOrEmpty(container))
                    query = query.Where(v => v.Container == container);

                string? libraryPathFilter = null;
                if (libraryPathId.HasValue)
                {
                    // Get the library path from LibraryPath table
                    var libraryPath = await _dbContext.LibraryPaths
                        .FirstOrDefaultAsync(lp => lp.Id == libraryPathId.Value);
                    
                    if (libraryPath != null)
                    {
                        libraryPathFilter = libraryPath.Path;
                    }
                    else
                    {
                        // If library path not found, return empty result
                        libraryPathFilter = string.Empty; // Will result in no matches
                    }
                }
                
                if (libraryPathFilter != null)
                {
                    // Filter by matching the LibraryScan's LibraryPath string with the LibraryPath's Path
                    var filterPath = libraryPathFilter; // Capture for closure
                    query = query.Where(v => v.LibraryScan != null && v.LibraryScan.LibraryPath == filterPath);
                }

                if (!string.IsNullOrEmpty(search))
                {
                    var searchLower = search.ToLowerInvariant();
                    query = query.Where(v => 
                        (v.FileName != null && v.FileName.ToLower().Contains(searchLower)) ||
                        (v.FilePath != null && v.FilePath.ToLower().Contains(searchLower)) ||
                        (v.VideoCodec != null && v.VideoCodec.ToLower().Contains(searchLower)) ||
                        (v.Container != null && v.Container.ToLower().Contains(searchLower)) ||
                        (v.AudioCodecs != null && v.AudioCodecs.ToLower().Contains(searchLower)) ||
                        (v.SubtitleFormats != null && v.SubtitleFormats.ToLower().Contains(searchLower)));
                }

                // Filter by broken status
                if (isBroken.HasValue)
                {
                    query = query.Where(v => v.IsBroken == isBroken.Value);
                }

                // Filter by Servarr sync status
                if (!string.IsNullOrEmpty(servarrFilter))
                {
                    var filterLower = servarrFilter.ToLowerInvariant();
                    if (filterLower == "synced")
                    {
                        // Show only videos that have been synced with Sonarr or Radarr
                        query = query.Where(v => !string.IsNullOrEmpty(v.ServarrType));
                    }
                    else if (filterLower == "not-synced")
                    {
                        // Show only videos that have NOT been synced
                        query = query.Where(v => string.IsNullOrEmpty(v.ServarrType));
                    }
                    // If filter is something else, ignore it (show all)
                }

                // Load all matching videos to recalculate scores and filter
                // Note: We'll apply sorting after filtering and score recalculation
                var allVideos = await query.ToListAsync();

                // Recalculate OverallScore dynamically based on current thresholds
                foreach (var video in allVideos)
                {
                    try
                    {
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
                            _logger.LogWarning("Failed to parse score '{Score}' for video {VideoId}, defaulting to Unknown", 
                                recalculatedScore, video.Id);
                            video.OverallScore = CompatibilityScore.Unknown;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error recalculating score for video {VideoId}, defaulting to Unknown", video.Id);
                        video.OverallScore = CompatibilityScore.Unknown;
                    }
                }

                // Apply score filter after recalculation if specified
                if (!string.IsNullOrEmpty(score))
                {
                    var targetScore = score.Trim();
                    allVideos = allVideos.Where(v => 
                    {
                        try
                        {
                            var calculatedScore = CalculateOverallScore(v.CompatibilityRating, v.RemuxClients, v.VideoCodec, v.BitDepth);
                            return calculatedScore.Equals(targetScore, StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    }).ToList();
                }

                // Calculate total after filtering
                var total = allVideos.Count;

                // Apply sorting
                var sortByLower = (sortBy ?? "analyzedAt").ToLowerInvariant();
                var sortOrderLower = (sortOrder ?? "desc").ToLowerInvariant();
                var isDescending = sortOrderLower == "desc";

                var sortedVideos = sortByLower switch
                {
                    "filename" => isDescending 
                        ? allVideos.OrderByDescending(v => v.FileName ?? string.Empty).ToList()
                        : allVideos.OrderBy(v => v.FileName ?? string.Empty).ToList(),
                    "filesize" => isDescending 
                        ? allVideos.OrderByDescending(v => v.FileSize).ToList()
                        : allVideos.OrderBy(v => v.FileSize).ToList(),
                    "compatibilityrating" => isDescending 
                        ? allVideos.OrderByDescending(v => v.CompatibilityRating).ToList()
                        : allVideos.OrderBy(v => v.CompatibilityRating).ToList(),
                    "duration" => isDescending 
                        ? allVideos.OrderByDescending(v => v.Duration).ToList()
                        : allVideos.OrderBy(v => v.Duration).ToList(),
                    "width" => isDescending 
                        ? allVideos.OrderByDescending(v => v.Width * v.Height).ToList() // Sort by total pixels
                        : allVideos.OrderBy(v => v.Width * v.Height).ToList(),
                    "analyzedat" or _ => isDescending 
                        ? allVideos.OrderByDescending(v => v.AnalyzedAt).ToList()
                        : allVideos.OrderBy(v => v.AnalyzedAt).ToList()
                };

                // Apply pagination after sorting
                var videos = sortedVideos
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new PagedResult<VideoAnalysis>
                {
                    Items = videos,
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(total / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading videos");
                return StatusCode(500, new { error = "Failed to load videos", message = ex.Message });
            }
        }

        [HttpGet("videos/filters")]
        public async Task<ActionResult<FilterOptions>> GetFilterOptions()
        {
            var codecs = await _dbContext.VideoAnalyses
                .Where(v => !string.IsNullOrEmpty(v.VideoCodec))
                .Select(v => v.VideoCodec)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            var containers = await _dbContext.VideoAnalyses
                .Where(v => !string.IsNullOrEmpty(v.Container))
                .Select(v => v.Container)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return Ok(new FilterOptions
            {
                Codecs = codecs,
                Containers = containers
            });
        }

        [HttpGet("settings/rating")]
        public ActionResult<RatingSettings> GetRatingSettings()
        {
            return Ok(new RatingSettings
            {
                OptimalDirectPlayThreshold = _configuration.GetValue<int>("CompatibilityRating:OptimalDirectPlayThreshold", 8),
                GoodDirectPlayThreshold = _configuration.GetValue<int>("CompatibilityRating:GoodDirectPlayThreshold", 5),
                GoodCombinedThreshold = _configuration.GetValue<int>("CompatibilityRating:GoodCombinedThreshold", 8)
            });
        }

        [HttpPost("settings/rating")]
        public async Task<ActionResult> SaveRatingSettings([FromBody] RatingSettings settings)
        {
            try
            {
                // Get the path to appsettings.json (same logic as Program.cs)
                var contentRoot = _environment.ContentRootPath;
                var configAppsettingsPath = System.IO.Path.Combine(contentRoot, "config", "appsettings.json");
                var rootAppsettingsPath = System.IO.Path.Combine(contentRoot, "appsettings.json");
                
                string appsettingsPath;
                if (System.IO.File.Exists(configAppsettingsPath))
                {
                    appsettingsPath = configAppsettingsPath;
                }
                else if (System.IO.File.Exists(rootAppsettingsPath))
                {
                    appsettingsPath = rootAppsettingsPath;
                }
                else
                {
                    // Default to config directory
                    appsettingsPath = configAppsettingsPath;
                }
                
                if (!System.IO.File.Exists(appsettingsPath))
                {
                    _logger.LogWarning("appsettings.json not found at {Path}, creating new file", appsettingsPath);
                    // Create directory if needed
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(appsettingsPath)!);
                }
                
                // Read existing configuration
                var jsonContent = System.IO.File.Exists(appsettingsPath) 
                    ? await System.IO.File.ReadAllTextAsync(appsettingsPath) 
                    : "{}";
                
                // Parse JSON and update the CompatibilityRating section
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                // Create a mutable JSON structure using JsonObject
                var jsonObject = new System.Text.Json.Nodes.JsonObject();
                
                // Copy all existing properties except CompatibilityRating
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name != "CompatibilityRating")
                    {
                        // Preserve the original JSON structure by cloning the JsonElement
                        jsonObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                    }
                }
                
                // Update CompatibilityRating section
                var compatibilityRating = new System.Text.Json.Nodes.JsonObject
                {
                    ["OptimalDirectPlayThreshold"] = settings.OptimalDirectPlayThreshold,
                    ["GoodDirectPlayThreshold"] = settings.GoodDirectPlayThreshold,
                    ["GoodCombinedThreshold"] = settings.GoodCombinedThreshold
                };
                jsonObject["CompatibilityRating"] = compatibilityRating;
                
                // Write back to file with proper formatting
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var newJson = jsonObject.ToJsonString(options);
                await System.IO.File.WriteAllTextAsync(appsettingsPath, newJson);
                
                _logger.LogInformation("Rating settings saved: Optimal={Optimal}, GoodDirect={GoodDirect}, GoodCombined={GoodCombined}",
                    settings.OptimalDirectPlayThreshold, settings.GoodDirectPlayThreshold, settings.GoodCombinedThreshold);
                
                // Trigger configuration reload if IConfigurationRoot is available
                if (_configuration is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                    _logger.LogInformation("Configuration reloaded. New values: Optimal={Optimal}, GoodDirect={GoodDirect}, GoodCombined={GoodCombined}",
                        _configuration.GetValue<int>("CompatibilityRating:OptimalDirectPlayThreshold", 8),
                        _configuration.GetValue<int>("CompatibilityRating:GoodDirectPlayThreshold", 5),
                        _configuration.GetValue<int>("CompatibilityRating:GoodCombinedThreshold", 8));
                }
                else
                {
                    _logger.LogWarning("Configuration is not IConfigurationRoot, cannot reload. Changes may not take effect until restart.");
                }
                
                return Ok(new { message = "Rating settings saved successfully. Changes will apply to newly scanned videos." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving rating settings");
                return StatusCode(500, new { error = $"Failed to save rating settings: {ex.Message}" });
            }
        }

        [HttpGet("settings/rating/codecs")]
        public ActionResult<object> GetCodecThresholds()
        {
            var codecThresholds = new List<object>();
            
            foreach (var codecEntry in Services.JellyfinCompatibilityData.VideoCodecSupport)
            {
                var codecName = codecEntry.Key;
                var support = codecEntry.Value;
                
                // Count clients with each support level
                var supported = support.Values.Count(s => s == Services.JellyfinCompatibilityData.SupportLevel.Supported);
                var partial = support.Values.Count(s => s == Services.JellyfinCompatibilityData.SupportLevel.Partial);
                var unsupported = support.Values.Count(s => s == Services.JellyfinCompatibilityData.SupportLevel.Unsupported);
                var totalClients = Services.JellyfinCompatibilityData.AllClients.Length;
                
                // Calculate default thresholds
                var optimalThreshold = (int)Math.Ceiling(supported * 0.8);
                var goodDirectThreshold = (int)Math.Ceiling(supported * 0.6);
                var goodCombinedThreshold = (int)Math.Ceiling((supported + partial) * 0.8);
                
                optimalThreshold = Math.Max(1, optimalThreshold);
                goodDirectThreshold = Math.Max(1, goodDirectThreshold);
                goodCombinedThreshold = Math.Max(1, goodCombinedThreshold);
                
                // Load from configuration if available
                var configPath = $"CompatibilityRating:CodecThresholds:{codecName}";
                var configuredOptimal = _configuration.GetValue<int>($"{configPath}:OptimalDirectPlayThreshold", optimalThreshold);
                var configuredGoodDirect = _configuration.GetValue<int>($"{configPath}:GoodDirectPlayThreshold", goodDirectThreshold);
                var configuredGoodCombined = _configuration.GetValue<int>($"{configPath}:GoodCombinedThreshold", goodCombinedThreshold);
                
                codecThresholds.Add(new
                {
                    codec = codecName,
                    expectedDirectPlay = supported,
                    expectedRemux = partial,
                    expectedTranscode = unsupported,
                    totalClients = totalClients,
                    optimalThreshold = configuredOptimal,
                    goodDirectThreshold = configuredGoodDirect,
                    goodCombinedThreshold = configuredGoodCombined,
                    defaultOptimalThreshold = optimalThreshold,
                    defaultGoodDirectThreshold = goodDirectThreshold,
                    defaultGoodCombinedThreshold = goodCombinedThreshold
                });
            }
            
            return Ok(new { codecs = codecThresholds });
        }

        [HttpPost("settings/rating/codecs")]
        public ActionResult SaveCodecThresholds([FromBody] Dictionary<string, CodecThresholdConfig> thresholds)
        {
            // Note: In a real application, you would save this to appsettings.json or a database
            // For now, we'll just return success - the actual saving would require file system access
            // or a configuration management system
            
            _logger.LogInformation("Codec thresholds update requested for {Count} codecs", thresholds?.Count ?? 0);
            
            return Ok(new { message = "Codec thresholds saved. Restart the application for changes to take effect." });
        }

        [HttpGet("settings/compatibility")]
        public ActionResult<object> GetCompatibilitySettings()
        {
            // Get enabled clients (filter out disabled ones)
            var allClients = JellyfinCompatibilityData.AllClients;
            var disabledClientsSection = _configuration?.GetSection("CompatibilityRating:DisabledClients");
            var enabledClients = new List<string>();
            
            foreach (var client in allClients)
            {
                var isDisabled = disabledClientsSection?.GetValue<bool>(client) ?? false;
                if (!isDisabled)
                {
                    enabledClients.Add(client);
                }
            }
            
            // If all clients are disabled, enable all by default
            if (enabledClients.Count == 0)
            {
                enabledClients = allClients.ToList();
            }
            
            // Get default compatibility data
            var videoCodecs = JellyfinCompatibilityData.VideoCodecSupport;
            var audioCodecs = JellyfinCompatibilityData.AudioCodecSupport;
            var containers = JellyfinCompatibilityData.ContainerSupport;
            var subtitles = JellyfinCompatibilityData.SubtitleSupport;
            var clients = enabledClients.ToArray();

            // Get custom overrides from configuration
            var overridesSection = _configuration.GetSection("CompatibilityOverrides");
            List<CompatibilityOverride> overrides;
            if (overridesSection.Exists())
            {
                overrides = overridesSection.Get<List<CompatibilityOverride>>() ?? new List<CompatibilityOverride>();
            }
            else
            {
                overrides = new List<CompatibilityOverride>();
            }

            // Build response with defaults and overrides
            var response = new
            {
                clients = clients,
                videoCodecs = videoCodecs.Keys.ToList(),
                audioCodecs = audioCodecs.Keys.ToList(),
                containers = containers.Keys.ToList(),
                subtitleFormats = subtitles.Keys.ToList(),
                defaults = new
                {
                    video = videoCodecs,
                    audio = audioCodecs,
                    container = containers,
                    subtitle = subtitles
                },
                overrides = overrides
            };

            return Ok(response);
        }

        [HttpPost("settings/compatibility")]
        public async Task<ActionResult> SaveCompatibilitySettings([FromBody] List<CompatibilityOverride> overrides)
        {
            try
            {
                // Get the path to appsettings.json
                var contentRoot = _environment.ContentRootPath;
                var configAppsettingsPath = System.IO.Path.Combine(contentRoot, "config", "appsettings.json");
                var rootAppsettingsPath = System.IO.Path.Combine(contentRoot, "appsettings.json");
                
                string appsettingsPath;
                if (System.IO.File.Exists(configAppsettingsPath))
                {
                    appsettingsPath = configAppsettingsPath;
                }
                else if (System.IO.File.Exists(rootAppsettingsPath))
                {
                    appsettingsPath = rootAppsettingsPath;
                }
                else
                {
                    appsettingsPath = configAppsettingsPath;
                }

                // Ensure directory exists
                if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(appsettingsPath)))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(appsettingsPath)!);
                }

                // Read existing configuration
                var jsonContent = System.IO.File.Exists(appsettingsPath) 
                    ? await System.IO.File.ReadAllTextAsync(appsettingsPath) 
                    : "{}";

                // Parse JSON and update the CompatibilityOverrides section
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = System.Text.Json.Nodes.JsonNode.Parse(jsonContent) ?? new System.Text.Json.Nodes.JsonObject();

                // Update CompatibilityOverrides section
                var overridesArray = new System.Text.Json.Nodes.JsonArray();
                foreach (var overrideItem in overrides)
                {
                    var overrideObj = new System.Text.Json.Nodes.JsonObject
                    {
                        ["Codec"] = overrideItem.Codec,
                        ["Client"] = overrideItem.Client,
                        ["SupportLevel"] = overrideItem.SupportLevel,
                        ["Category"] = overrideItem.Category
                    };
                    overridesArray.Add(overrideObj);
                }

                root["CompatibilityOverrides"] = overridesArray;

                // Write back to file
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var updatedJson = root.ToJsonString(options);
                await System.IO.File.WriteAllTextAsync(appsettingsPath, updatedJson);

                // Reload configuration if IConfigurationRoot is available
                if (_configuration is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                }
                else
                {
                    _logger.LogWarning("Configuration is not IConfigurationRoot, cannot reload. Changes may not take effect until restart.");
                }

                _logger.LogInformation("Compatibility overrides saved: {Count} overrides", overrides.Count);

                return Ok(new { message = "Compatibility settings saved successfully. Changes will apply to newly analyzed videos." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving compatibility settings");
                return StatusCode(500, new { error = "Failed to save compatibility settings", message = ex.Message });
            }
        }

        private List<string> FindAllExternalSubtitles(string videoPath)
        {
            var foundSubtitles = new List<string>();
            
            if (string.IsNullOrEmpty(videoPath)) return foundSubtitles;
            
            try
            {
                var videoDir = System.IO.Path.GetDirectoryName(videoPath);
                if (string.IsNullOrEmpty(videoDir)) return foundSubtitles;
                
                var videoNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(videoPath);
                var subtitleExtensions = new[] { ".srt", ".vtt", ".ass", ".ssa", ".sub" };
                
                _logger.LogDebug("Searching for subtitles for video: {VideoName} in directory: {Directory}", videoNameWithoutExt, videoDir);
                
                // Get all subtitle files in the directory
                var allSubtitleFiles = System.IO.Directory.GetFiles(videoDir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                    .Where(f => subtitleExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();
                
                _logger.LogDebug("Found {Count} subtitle files in directory", allSubtitleFiles.Count);
                
                // Normalize video name for comparison (remove common separators and make lowercase)
                var normalizedVideoName = videoNameWithoutExt.ToLowerInvariant()
                    .Replace(" ", "")
                    .Replace("_", "")
                    .Replace("-", "")
                    .Replace(".", "");
                
                foreach (var subtitleFile in allSubtitleFiles)
                {
                    var subtitleNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(subtitleFile);
                    var subtitleFileName = System.IO.Path.GetFileName(subtitleFile);
                    
                    // Trim whitespace from both names for comparison
                    var trimmedVideoName = videoNameWithoutExt.Trim();
                    var trimmedSubtitleName = subtitleNameWithoutExt.Trim();
                    
                    _logger.LogDebug("Checking subtitle file: {SubtitleFile} (name without ext: {NameWithoutExt})", subtitleFileName, subtitleNameWithoutExt);
                    _logger.LogDebug("Video name: '{VideoName}', Subtitle name: '{SubtitleName}'", trimmedVideoName, trimmedSubtitleName);
                    
                    // Check multiple matching patterns:
                    // 1. Exact match (case-insensitive, trimmed)
                    if (trimmedSubtitleName.Equals(trimmedVideoName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Match found (exact): {SubtitleFile}", subtitleFileName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                    
                    // 2. Starts with video name followed by dot (e.g., video.en.srt, video.forced.srt, video.ger.srt)
                    // This handles cases like "video.ger.srt" where .ger is a language code
                    // Check both trimmed and original versions
                    var videoNameWithDot = trimmedVideoName + ".";
                    var videoNameWithDotOriginal = videoNameWithoutExt + ".";
                    
                    if (trimmedSubtitleName.StartsWith(videoNameWithDot, StringComparison.OrdinalIgnoreCase) ||
                        subtitleNameWithoutExt.StartsWith(videoNameWithDotOriginal, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Match found (dot separator): {SubtitleFile} (video: '{VideoName}', subtitle: '{SubtitleName}')", 
                            subtitleFileName, trimmedVideoName, trimmedSubtitleName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                    
                    // 2b. Video name is a prefix of subtitle name followed by dot (handles language codes)
                    // Example: video = "Black Mirror (2011) - S07E01 - Common People"
                    //          subtitle = "Black Mirror (2011) - S07E01 - Common People.ger.srt"
                    //          subtitleNameWithoutExt = "Black Mirror (2011) - S07E01 - Common People.ger"
                    // Check both trimmed and original versions
                    if ((trimmedSubtitleName.Length > trimmedVideoName.Length && 
                         trimmedSubtitleName.Substring(0, trimmedVideoName.Length).Equals(trimmedVideoName, StringComparison.OrdinalIgnoreCase) &&
                         trimmedSubtitleName[trimmedVideoName.Length] == '.') ||
                        (subtitleNameWithoutExt.Length > videoNameWithoutExt.Length && 
                         subtitleNameWithoutExt.Substring(0, videoNameWithoutExt.Length).Equals(videoNameWithoutExt, StringComparison.OrdinalIgnoreCase) &&
                         subtitleNameWithoutExt[videoNameWithoutExt.Length] == '.'))
                    {
                        _logger.LogDebug("Match found (video name prefix with dot): {SubtitleFile} (video: '{VideoName}', subtitle: '{SubtitleName}')", 
                            subtitleFileName, trimmedVideoName, trimmedSubtitleName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                    
                    // 3. Starts with video name followed by space, dash, or underscore
                    // (e.g., video - subtitle.srt, video_subtitle.srt, video subtitle.srt)
                    var separators = new[] { " ", "-", "_", " - ", " -", "- " };
                    bool matchedSeparator = false;
                    foreach (var separator in separators)
                    {
                        if (subtitleNameWithoutExt.StartsWith(videoNameWithoutExt + separator, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Match found (separator '{Separator}'): {SubtitleFile}", separator, subtitleFileName);
                            foundSubtitles.Add(subtitleFile);
                            matchedSeparator = true;
                            break;
                        }
                    }
                    if (matchedSeparator) continue;
                    
                    // 4. Contains video name (more lenient matching)
                    // This catches cases where subtitle might have additional text before or after
                    if (subtitleNameWithoutExt.Contains(videoNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Match found (contains video name): {SubtitleFile}", subtitleFileName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                    
                    // 5. Normalized name starts with normalized video name
                    // This catches variations like "Video Name" vs "video_name" vs "video-name"
                    var normalizedSubtitleName = subtitleNameWithoutExt.ToLowerInvariant()
                        .Replace(" ", "")
                        .Replace("_", "")
                        .Replace("-", "")
                        .Replace(".", "");
                    
                    if (normalizedSubtitleName.StartsWith(normalizedVideoName))
                    {
                        // If names are equal (after normalization), it's a match
                        if (normalizedSubtitleName == normalizedVideoName)
                        {
                            _logger.LogDebug("Match found (normalized exact): {SubtitleFile}", subtitleFileName);
                            foundSubtitles.Add(subtitleFile);
                            continue;
                        }
                        
                        // If subtitle name is longer, check if the next character is a separator
                        // This prevents "video1" from matching "video10"
                        var nextCharIndex = normalizedVideoName.Length;
                        if (nextCharIndex < normalizedSubtitleName.Length)
                        {
                            var nextChar = normalizedSubtitleName[nextCharIndex];
                            // Allow if next char is a separator (dot, space, dash, underscore, or parenthesis)
                            if (nextChar == '.' || nextChar == ' ' || nextChar == '-' || 
                                nextChar == '_' || nextChar == '(' || nextChar == '[')
                            {
                                _logger.LogDebug("Match found (normalized with separator): {SubtitleFile}", subtitleFileName);
                                foundSubtitles.Add(subtitleFile);
                                continue;
                            }
                        }
                    }
                    
                    // 6. Normalized name contains normalized video name (most lenient)
                    if (normalizedSubtitleName.Contains(normalizedVideoName) && normalizedVideoName.Length >= 3)
                    {
                        _logger.LogDebug("Match found (normalized contains): {SubtitleFile}", subtitleFileName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                }
                
                // Remove duplicates and sort
                foundSubtitles = foundSubtitles
                    .Distinct()
                    .OrderBy(f => f.Length) // Prefer shorter names (closer match)
                    .ThenBy(f => f) // Then alphabetically for consistency
                    .ToList();
                
                if (foundSubtitles.Count > 0)
                {
                    _logger.LogInformation("Found {Count} external subtitle file(s) for: {VideoPath}: {SubtitleFiles}", 
                        foundSubtitles.Count, videoPath, string.Join(", ", foundSubtitles.Select(f => System.IO.Path.GetFileName(f))));
                }
                else
                {
                    _logger.LogDebug("No matching subtitle files found for: {VideoPath}", videoPath);
                }
                
                return foundSubtitles;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching for external subtitle files for: {VideoPath}", videoPath);
                return foundSubtitles;
            }
        }

        private string? FindExternalSubtitle(string videoPath)
        {
            var allSubtitles = FindAllExternalSubtitles(videoPath);
            return allSubtitles.FirstOrDefault();
        }

        [HttpPost("videos/{id}/rescan")]
        public async Task<ActionResult<VideoAnalysis>> RescanVideo(int id)
        {
            try
            {
                var video = await _dbContext.VideoAnalyses
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (video == null)
                {
                    return NotFound(new { error = $"Video with ID {id} not found" });
                }

                if (string.IsNullOrEmpty(video.FilePath) || !System.IO.File.Exists(video.FilePath))
                {
                    return BadRequest(new { error = "Video file not found on disk" });
                }

                _logger.LogInformation("Rescanning video ID {Id}: {FilePath}", id, video.FilePath);

                // Get services from DI
                var serviceProvider = HttpContext.RequestServices;
                var videoAnalyzer = serviceProvider.GetRequiredService<VideoAnalyzerService>();
                
                // Look for ALL external subtitle files (there can be multiple)
                var externalSubtitlePaths = FindAllExternalSubtitles(video.FilePath);
                
                // Re-analyze the video using AnalyzeVideoStructured which returns structured data
                // Run analysis in a background task to prevent HTTP request timeout
                _logger.LogInformation("Starting video analysis for ID {Id}", id);
                
                (VideoInfo? videoInfo, CompatibilityResult? compatibilityResult) analysisResult;
                try
                {
                    // Pass cancellation token to allow cancellation if client disconnects
                    analysisResult = await Task.Run(() => 
                    {
                        try
                        {
                            return videoAnalyzer.AnalyzeVideoStructured(video.FilePath, externalSubtitlePaths);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during video analysis for ID {Id} at path {Path}", id, video.FilePath);
                            throw;
                        }
                    }, HttpContext.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Video analysis was cancelled for ID {Id}", id);
                    return StatusCode(499, new { error = "Video analysis was cancelled" });
                }
                catch (Exception analysisEx)
                {
                    _logger.LogError(analysisEx, "Video analysis failed for ID {Id}", id);
                    return StatusCode(500, new { error = $"Video analysis failed: {analysisEx.Message}" });
                }
                
                // Check if request was cancelled before processing results
                if (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning("Request was cancelled after analysis for video ID {Id}", id);
                    return StatusCode(499, new { error = "Request was cancelled" });
                }
                
                var (videoInfo, compatibilityResult) = analysisResult;
                
                if (videoInfo == null || compatibilityResult == null)
                {
                    _logger.LogError("Video analysis returned null for ID {Id}", id);
                    return StatusCode(500, new { error = "Video analysis returned null results" });
                }
                
                _logger.LogInformation("Video analysis completed for ID {Id}", id);
                
                // Check if media information is valid (not broken)
                bool isBroken = false;
                string? brokenReason = null;
                
                // Check for critical missing fields that indicate broken/invalid media
                if (string.IsNullOrWhiteSpace(videoInfo.VideoCodec) && string.IsNullOrWhiteSpace(videoInfo.Container))
                {
                    isBroken = true;
                    brokenReason = "No video codec or container information found";
                }
                else if (videoInfo.Width == 0 || videoInfo.Height == 0)
                {
                    isBroken = true;
                    brokenReason = "Invalid video resolution (width or height is 0)";
                }
                else if (videoInfo.Duration <= 0)
                {
                    isBroken = true;
                    brokenReason = "Invalid duration (duration is 0 or negative)";
                }
                else if (videoInfo.FileSize == 0)
                {
                    isBroken = true;
                    brokenReason = "File size is 0 bytes";
                }
                
                // Update the existing video analysis record
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
                video.HDRType = videoInfo.HDRType ?? string.Empty;
                video.IsFastStart = videoInfo.IsFastStart;
                video.IsBroken = isBroken;
                video.BrokenReason = brokenReason;
                
                // Update audio tracks
                video.AudioCodecs = string.Join(",", videoInfo.AudioTracks.Select(t => t.Codec).Distinct());
                video.AudioTrackCount = videoInfo.AudioTracks.Count;
                var audioJsonOptions = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                video.AudioTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo.AudioTracks, audioJsonOptions);
                
                // Update subtitle tracks (including external)
                var subtitleFormats = videoInfo.SubtitleTracks
                    .Select(t => t.IsEmbedded ? t.Format : $"{t.Format} (External)").Distinct();
                video.SubtitleFormats = string.Join(",", subtitleFormats);
                video.SubtitleTrackCount = videoInfo.SubtitleTracks.Count;
                var subtitleJsonOptions = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                video.SubtitleTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo.SubtitleTracks, subtitleJsonOptions);
                
                // Update compatibility results
                video.CompatibilityRating = compatibilityResult.CompatibilityRating;
                video.DirectPlayClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Direct Play");
                video.RemuxClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Remux");
                video.TranscodeClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Transcode");
                
                // Update issues and recommendations
                video.Issues = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.Issues);
                video.Recommendations = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.Recommendations);
                video.ClientResults = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.ClientResults);
                
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
                
                video.AnalyzedAt = DateTime.UtcNow;
                
                _logger.LogInformation("Saving updated video analysis for ID {Id}", id);
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Successfully rescanned video ID {Id}", id);
                
                // Check if request was cancelled
                if (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogWarning("Request was cancelled for video ID {Id}", id);
                    return StatusCode(499, new { error = "Request was cancelled" });
                }
                
                // Detach the entity to avoid serialization issues with navigation properties
                _dbContext.Entry(video).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                
                // Reload without navigation properties to avoid circular references
                var updatedVideo = await _dbContext.VideoAnalyses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == id);
                
                if (updatedVideo == null)
                {
                    return StatusCode(500, new { error = "Video not found after update" });
                }
                
                return Ok(updatedVideo);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Video rescan operation was cancelled for ID {Id}", id);
                return StatusCode(499, new { error = "Operation was cancelled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rescanning video ID {Id}", id);
                return StatusCode(500, new { error = $"Failed to rescan video: {ex.Message}" });
            }
        }

        [HttpPost("videos/rescan-broken")]
        public async Task<ActionResult<object>> RescanAllBrokenVideos()
        {
            try
            {
                // Get all broken videos
                var brokenVideos = await _dbContext.VideoAnalyses
                    .Where(v => v.IsBroken)
                    .Select(v => new { v.Id, v.FilePath })
                    .ToListAsync();
                
                if (brokenVideos.Count == 0)
                {
                    return Ok(new { count = 0, message = "No broken videos found" });
                }
                
                _logger.LogInformation("Starting rescan for {Count} broken videos", brokenVideos.Count);
                
                // Rescan videos in background (don't wait for completion)
                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var videoAnalyzer = scope.ServiceProvider.GetRequiredService<VideoAnalyzerService>();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    int successCount = 0;
                    int failCount = 0;
                    
                    foreach (var video in brokenVideos)
                    {
                        try
                        {
                            if (!System.IO.File.Exists(video.FilePath))
                            {
                                _logger.LogWarning("File not found, skipping: {FilePath}", video.FilePath);
                                failCount++;
                                continue;
                            }
                            
                            var externalSubtitlePaths = FindAllExternalSubtitles(video.FilePath);
                            
                            var (videoInfo, compatibilityResult) = await Task.Run(() =>
                            {
                                try
                                {
                                    return videoAnalyzer.AnalyzeVideoStructured(video.FilePath, externalSubtitlePaths);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error analyzing video: {FilePath}", video.FilePath);
                                    throw;
                                }
                            });
                            
                            // Update video record
                            var videoRecord = await dbContext.VideoAnalyses
                                .FirstOrDefaultAsync(v => v.Id == video.Id);
                            
                            if (videoRecord != null)
                            {
                                bool isBroken = false;
                                string brokenReason = string.Empty;
                                
                                // Check if still broken
                                if (videoInfo == null || compatibilityResult == null ||
                                    string.IsNullOrEmpty(videoInfo.VideoCodec) || string.IsNullOrEmpty(videoInfo.Container) ||
                                    videoInfo.Width == 0 || videoInfo.Height == 0 || videoInfo.Duration <= 0)
                                {
                                    isBroken = true;
                                    brokenReason = "Incomplete media information detected after analysis.";
                                }
                                
                                videoRecord.FileName = System.IO.Path.GetFileName(video.FilePath);
                                videoRecord.FileSize = new System.IO.FileInfo(video.FilePath).Length;
                                videoRecord.Duration = videoInfo?.Duration ?? 0;
                                videoRecord.Container = videoInfo?.Container ?? string.Empty;
                                videoRecord.VideoCodec = videoInfo?.VideoCodec ?? string.Empty;
                                videoRecord.VideoCodecTag = videoInfo?.VideoCodecTag ?? string.Empty;
                                videoRecord.IsCodecTagCorrect = videoInfo?.IsCodecTagCorrect ?? true;
                                videoRecord.BitDepth = videoInfo?.BitDepth ?? 0;
                                videoRecord.Width = videoInfo?.Width ?? 0;
                                videoRecord.Height = videoInfo?.Height ?? 0;
                                videoRecord.FrameRate = videoInfo?.FrameRate ?? 0;
                                videoRecord.IsHDR = videoInfo?.IsHDR ?? false;
                                videoRecord.HDRType = videoInfo?.HDRType ?? string.Empty;
                                videoRecord.IsFastStart = videoInfo?.IsFastStart ?? false;
                                
                                videoRecord.AudioCodecs = string.Join(",", videoInfo?.AudioTracks.Select(t => t.Codec).Distinct() ?? Enumerable.Empty<string>());
                                videoRecord.AudioTrackCount = videoInfo?.AudioTracks.Count ?? 0;
                                var audioJsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
                                videoRecord.AudioTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo?.AudioTracks ?? new List<AudioTrack>(), audioJsonOptions);
                                
                                var subtitleFormats = videoInfo?.SubtitleTracks.Select(t => t.IsEmbedded ? t.Format : $"{t.Format} (External)").Distinct() ?? Enumerable.Empty<string>();
                                videoRecord.SubtitleFormats = string.Join(",", subtitleFormats);
                                videoRecord.SubtitleTrackCount = videoInfo?.SubtitleTracks.Count ?? 0;
                                var subtitleJsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
                                videoRecord.SubtitleTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo?.SubtitleTracks ?? new List<SubtitleTrack>(), subtitleJsonOptions);
                                
                                videoRecord.CompatibilityRating = compatibilityResult?.CompatibilityRating ?? 0;
                                videoRecord.DirectPlayClients = compatibilityResult?.ClientResults.Values.Count(r => r.Status == "Direct Play") ?? 0;
                                videoRecord.RemuxClients = compatibilityResult?.ClientResults.Values.Count(r => r.Status == "Remux") ?? 0;
                                videoRecord.TranscodeClients = compatibilityResult?.ClientResults.Values.Count(r => r.Status == "Transcode") ?? 0;
                                
                                videoRecord.Issues = System.Text.Json.JsonSerializer.Serialize(compatibilityResult?.Issues ?? new List<string>(), audioJsonOptions);
                                videoRecord.Recommendations = System.Text.Json.JsonSerializer.Serialize(compatibilityResult?.Recommendations ?? new List<string>(), audioJsonOptions);
                                videoRecord.ClientResults = System.Text.Json.JsonSerializer.Serialize(compatibilityResult?.ClientResults ?? new Dictionary<string, ClientCompatibility>(), audioJsonOptions);
                                
                                var recalculatedScore = CalculateOverallScore(videoRecord.CompatibilityRating, videoRecord.RemuxClients, videoRecord.VideoCodec, videoRecord.BitDepth);
                                if (Enum.TryParse<CompatibilityScore>(recalculatedScore, true, out var parsedScore))
                                {
                                    videoRecord.OverallScore = parsedScore;
                                }
                                else
                                {
                                    videoRecord.OverallScore = CompatibilityScore.Unknown;
                                }
                                
                                videoRecord.IsBroken = isBroken;
                                videoRecord.BrokenReason = brokenReason;
                                videoRecord.AnalyzedAt = DateTime.UtcNow;
                                
                                await dbContext.SaveChangesAsync();
                                successCount++;
                                _logger.LogInformation("Successfully rescanned broken video ID {Id}", video.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error rescanning broken video ID {Id}: {FilePath}", video.Id, video.FilePath);
                            failCount++;
                        }
                    }
                    
                    _logger.LogInformation("Completed rescan of broken videos: {SuccessCount} succeeded, {FailCount} failed", successCount, failCount);
                });
                
                return Ok(new { count = brokenVideos.Count, message = $"Rescan started for {brokenVideos.Count} broken video(s)" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting rescan of broken videos");
                return StatusCode(500, new { error = $"Failed to start rescan: {ex.Message}" });
            }
        }

        [HttpGet("videos/{id}/debug-subtitles")]
        public async Task<ActionResult<object>> DebugSubtitles(int id)
        {
            try
            {
                var video = await _dbContext.VideoAnalyses
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (video == null)
                {
                    return NotFound(new { error = $"Video with ID {id} not found" });
                }

                if (string.IsNullOrEmpty(video.FilePath) || !System.IO.File.Exists(video.FilePath))
                {
                    return BadRequest(new { error = "Video file not found on disk" });
                }

                var videoDir = System.IO.Path.GetDirectoryName(video.FilePath);
                if (string.IsNullOrEmpty(videoDir))
                {
                    return BadRequest(new { error = "Cannot get directory name for video file" });
                }
                
                var videoNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(video.FilePath);
                var subtitleExtensions = new[] { ".srt", ".vtt", ".ass", ".ssa", ".sub" };
                
                // Get all subtitle files in the directory
                var allSubtitleFiles = System.IO.Directory.GetFiles(videoDir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                    .Where(f => subtitleExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => new
                    {
                        FullPath = f,
                        FileName = System.IO.Path.GetFileName(f),
                        NameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(f)
                    })
                    .ToList();

                var foundSubtitles = FindAllExternalSubtitles(video.FilePath);

                return Ok(new
                {
                    videoPath = video.FilePath,
                    videoName = System.IO.Path.GetFileName(video.FilePath),
                    videoNameWithoutExt = videoNameWithoutExt,
                    directory = videoDir,
                    allSubtitleFiles = allSubtitleFiles,
                    foundSubtitles = foundSubtitles.Select(f => new
                    {
                        FullPath = f,
                        FileName = System.IO.Path.GetFileName(f),
                        NameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(f)
                    }).ToList(),
                    foundCount = foundSubtitles.Count,
                    totalSubtitleFiles = allSubtitleFiles.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error debugging subtitles for video ID {Id}", id);
                return StatusCode(500, new { error = $"Failed to debug subtitles: {ex.Message}" });
            }
        }

        [HttpGet("videos/{id}")]
        public async Task<ActionResult<VideoAnalysis>> GetVideo(int id)
        {
            try
            {
                var video = await _dbContext.VideoAnalyses
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (video == null)
                {
                    return NotFound(new { error = $"Video with ID {id} not found" });
                }

                // Recalculate OverallScore dynamically based on current thresholds
                try
                {
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
                        _logger.LogWarning("Failed to parse score '{Score}' for video {VideoId}, defaulting to Unknown", 
                            recalculatedScore, video.Id);
                        video.OverallScore = CompatibilityScore.Unknown;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error recalculating score for video {VideoId}, defaulting to Unknown", video.Id);
                    video.OverallScore = CompatibilityScore.Unknown;
                }

                // Note: AudioTracks and SubtitleTracks are stored as comma-separated strings
                // in AudioCodecs and SubtitleFormats properties, not as navigation properties
                return Ok(video);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading video {VideoId}", id);
                return StatusCode(500, new { error = "Failed to load video", message = ex.Message });
            }
        }

        [HttpPost("videos/redownload")]
        public async Task<ActionResult> RedownloadVideos([FromBody] RedownloadRequest request)
        {
            if (request.VideoIds == null || request.VideoIds.Count == 0)
            {
                return BadRequest(new { error = "No video IDs provided" });
            }

            _logger.LogInformation("Redownload requested for {Count} videos", request.VideoIds.Count);

            var results = new List<RedownloadResult>();
            var sonarrService = HttpContext.RequestServices.GetService<SonarrService>();
            var radarrService = HttpContext.RequestServices.GetService<RadarrService>();

            foreach (var videoId in request.VideoIds)
            {
                var result = new RedownloadResult
                {
                    VideoId = videoId,
                    Success = false,
                    Message = "Not processed"
                };

                try
                {
                    var video = await _dbContext.VideoAnalyses.FindAsync(videoId);
                    if (video == null)
                    {
                        result.Message = "Video not found in database";
                        results.Add(result);
                        continue;
                    }

                    if (string.IsNullOrEmpty(video.FilePath))
                    {
                        result.Message = "Video file path is empty";
                        results.Add(result);
                        continue;
                    }

                    var filePath = video.FilePath;
                    var fileExists = System.IO.File.Exists(filePath);

                    // Try to find in Sonarr first
                    if (sonarrService != null && sonarrService.IsEnabled && sonarrService.IsConnected)
                    {
                        var episode = await sonarrService.FindEpisodeByPath(filePath);
                        if (episode != null)
                        {
                            _logger.LogInformation("Found video in Sonarr: EpisodeId={EpisodeId}, FilePath={FilePath}", 
                                episode.Id, filePath);

                            // Delete file from disk if it exists
                            if (fileExists)
                            {
                                try
                                {
                                    System.IO.File.Delete(filePath);
                                    _logger.LogInformation("Deleted file from disk: {FilePath}", filePath);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to delete file from disk: {FilePath}", filePath);
                                }
                            }

                            // Delete from Sonarr if episode file ID exists
                            if (episode.EpisodeFileId.HasValue)
                            {
                                var deleted = await sonarrService.DeleteEpisodeFile(episode.EpisodeFileId.Value);
                                if (deleted)
                                {
                                    _logger.LogInformation("Deleted episode file from Sonarr: EpisodeFileId={EpisodeFileId}", 
                                        episode.EpisodeFileId.Value);
                                }
                            }

                            // Trigger search
                            var searchTriggered = await sonarrService.TriggerEpisodeSearch(episode.Id);
                            if (searchTriggered)
                            {
                                result.Success = true;
                                result.Message = "Redownload triggered in Sonarr";
                                result.Service = "Sonarr";
                            }
                            else
                            {
                                result.Message = "Failed to trigger Sonarr search";
                            }

                            // Remove from database
                            _dbContext.VideoAnalyses.Remove(video);
                            results.Add(result);
                            continue;
                        }
                    }

                    // Try Radarr
                    if (radarrService != null && radarrService.IsEnabled && radarrService.IsConnected)
                    {
                        var movie = await radarrService.FindMovieByPath(filePath);
                        if (movie != null && movie.MovieFile != null)
                        {
                            _logger.LogInformation("Found video in Radarr: MovieId={MovieId}, FilePath={FilePath}", 
                                movie.Id, filePath);

                            // Delete file from disk if it exists
                            if (fileExists)
                            {
                                try
                                {
                                    System.IO.File.Delete(filePath);
                                    _logger.LogInformation("Deleted file from disk: {FilePath}", filePath);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to delete file from disk: {FilePath}", filePath);
                                }
                            }

                            // Delete from Radarr
                            var deleted = await radarrService.DeleteMovieFile(movie.MovieFile.Id);
                            if (deleted)
                            {
                                _logger.LogInformation("Deleted movie file from Radarr: MovieFileId={MovieFileId}", 
                                    movie.MovieFile.Id);
                            }

                            // Trigger search
                            var searchTriggered = await radarrService.TriggerMovieSearch(movie.Id);
                            if (searchTriggered)
                            {
                                result.Success = true;
                                result.Message = "Redownload triggered in Radarr";
                                result.Service = "Radarr";
                            }
                            else
                            {
                                result.Message = "Failed to trigger Radarr search";
                            }

                            // Remove from database
                            _dbContext.VideoAnalyses.Remove(video);
                            results.Add(result);
                            continue;
                        }
                    }

                    // Not found in Sonarr or Radarr
                    result.Message = "Video not found in Sonarr or Radarr";
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing redownload for video {VideoId}", videoId);
                    result.Message = $"Error: {ex.Message}";
                    results.Add(result);
                }
            }

            await _dbContext.SaveChangesAsync();

            var successCount = results.Count(r => r.Success);
            return Ok(new
            {
                total = request.VideoIds.Count,
                success = successCount,
                failed = request.VideoIds.Count - successCount,
                results = results
            });
        }
    }

    public class ScanRequest
    {
        public string Path { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Category { get; set; } = "Misc";
    }

    public class RescanRequest
    {
        public string Path { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Category { get; set; } = "Misc";
    }

    public class RedownloadRequest
    {
        public List<int> VideoIds { get; set; } = new();
    }

    public class RedownloadResult
    {
        public int VideoId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Service { get; set; }
    }

    public class LibraryPathInfo
    {
        public int Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "Misc";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastScannedAt { get; set; }
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public string? LatestScanStatus { get; set; }
        public DateTime? LatestScanStartedAt { get; set; }
        public DateTime? LatestScanCompletedAt { get; set; }
        public int LatestScanProcessedFiles { get; set; }
        public int LatestScanFailedFiles { get; set; }
    }

    public class DashboardStats
    {
        public int TotalVideos { get; set; }
        public int OptimalCount { get; set; }
        public int GoodCount { get; set; }
        public int PoorCount { get; set; }
        public int BrokenCount { get; set; }
        public long TotalSize { get; set; }
        public double TotalDuration { get; set; }
        public int HdrCount { get; set; }
        public Dictionary<string, int> CodecDistribution { get; set; } = new();
        public Dictionary<string, int> ContainerDistribution { get; set; } = new();
        public List<LibraryScan> RecentScans { get; set; } = new();
    }

    public class CompatibilityBreakdown
    {
        public List<ScoreBreakdown> Breakdown { get; set; } = new();
    }

    public class ScoreBreakdown
    {
        public string Score { get; set; } = string.Empty;
        public int Count { get; set; }
        public int AvgDirectPlay { get; set; }
        public int AvgRemux { get; set; }
        public int AvgTranscode { get; set; }
    }

    public class IssueSummary
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string VideoCodec { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public string OverallScore { get; set; } = string.Empty;
        public int DirectPlayClients { get; set; }
        public int RemuxClients { get; set; }
        public int TranscodeClients { get; set; }
        public bool IsHDR { get; set; }
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class ScanProgress
    {
        public int Id { get; set; }
        public string LibraryPath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int FailedFiles { get; set; }
        public string? ErrorMessage { get; set; }
        public double Progress { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public double FilesPerSecond { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public string? CurrentFile { get; set; }
        public string? LastAnalyzedFile { get; set; }
        public DateTime? LastAnalyzedAt { get; set; }
        public List<FailedFileInfo> FailedFilesList { get; set; } = new();
    }

    public class FilterOptions
    {
        public List<string> Codecs { get; set; } = new();
        public List<string> Containers { get; set; } = new();
    }

    public class RatingSettings
    {
        public int OptimalDirectPlayThreshold { get; set; }
        public int GoodDirectPlayThreshold { get; set; }
        public int GoodCombinedThreshold { get; set; }
    }

    public class CodecThresholdConfig
    {
        public int OptimalDirectPlayThreshold { get; set; }
        public int GoodDirectPlayThreshold { get; set; }
        public int GoodCombinedThreshold { get; set; }
    }

    public class FailedFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? ErrorType { get; set; }
        public DateTime FailedAt { get; set; }
        public long? FileSize { get; set; }
    }

    public class DirectoryBrowseResponse
    {
        public string CurrentPath { get; set; } = string.Empty;
        public string? ParentPath { get; set; }
        public List<DirectoryItem> Items { get; set; } = new();
        public string? Error { get; set; }
    }

    public class DirectoryItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
    }
}

