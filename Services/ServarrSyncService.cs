using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Optimarr.Data;
using Optimarr.Models;

namespace Optimarr.Services
{
    public class ServarrSyncService
    {
        private readonly AppDbContext _dbContext;
        private readonly RadarrService _radarrService;
        private readonly SonarrService _sonarrService;
        private readonly ILogger<ServarrSyncService> _logger;
        private readonly IConfiguration _configuration;

        public ServarrSyncService(
            AppDbContext dbContext,
            RadarrService radarrService,
            SonarrService sonarrService,
            ILogger<ServarrSyncService> logger,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _radarrService = radarrService;
            _sonarrService = sonarrService;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<ServarrSyncResult> SyncRadarrAsync()
        {
            var result = new ServarrSyncResult
            {
                ServiceType = "Radarr",
                StartedAt = DateTime.UtcNow
            };

            if (!_radarrService.IsEnabled || !_radarrService.IsConnected)
            {
                result.Success = false;
                result.Message = "Radarr is not enabled or connected";
                return result;
            }

            try
            {
                _logger.LogInformation("Starting Radarr library sync");

                // Get root folders from Radarr
                var rootFolders = await _radarrService.GetRootFolders();
                result.RootFoldersFound = rootFolders.Count;

                // Skip movies fetch for performance - it's only used for counting which is not critical
                // If needed, this can be made optional or fetched in parallel
                result.ItemsFound = 0; // Will be set if movie count is needed later

                // Get root folder IDs for efficient lookup
                var rootFolderIds = rootFolders.Select(rf => rf.Id).ToHashSet();

                // Optimize: Only query paths that could match (Radarr paths or paths matching root folder IDs)
                // This reduces the dataset significantly compared to loading all paths
                var existingPathsQuery = _dbContext.LibraryPaths
                    .Where(lp => lp.ServarrType == "Radarr" || 
                                (lp.ServarrRootFolderId.HasValue && rootFolderIds.Contains(lp.ServarrRootFolderId.Value)));

                // Build lookup dictionaries for O(1) access instead of O(n) FirstOrDefault
                var existingPaths = await existingPathsQuery.ToListAsync();
                
                // Dictionary keyed by normalized path for fast lookup
                var pathsByNormalizedPath = new Dictionary<string, LibraryPath>(StringComparer.OrdinalIgnoreCase);
                
                // Dictionary keyed by root folder ID for fast lookup
                var pathsByRootFolderId = new Dictionary<int, LibraryPath>();

                // Pre-normalize and index all existing paths
                foreach (var path in existingPaths)
                {
                    var normalized = NormalizePath(path.Path);
                    if (!pathsByNormalizedPath.ContainsKey(normalized))
                    {
                        pathsByNormalizedPath[normalized] = path;
                    }
                    
                    if (path.ServarrRootFolderId.HasValue && !pathsByRootFolderId.ContainsKey(path.ServarrRootFolderId.Value))
                    {
                        pathsByRootFolderId[path.ServarrRootFolderId.Value] = path;
                    }
                }

                // Get Radarr-specific paths for cleanup
                var radarrPaths = existingPaths.Where(lp => lp.ServarrType == "Radarr").ToList();

                // Sync root folders as library paths
                foreach (var rootFolder in rootFolders)
                {
                    if (!rootFolder.Accessible)
                    {
                        _logger.LogWarning("Radarr root folder is not accessible: {Path}", rootFolder.Path);
                        continue;
                    }

                    var normalizedRootPath = NormalizePath(rootFolder.Path);
                    
                    // O(1) lookup: Check by root folder ID first (most specific), then by normalized path
                    LibraryPath? existingPath = null;
                    if (pathsByRootFolderId.TryGetValue(rootFolder.Id, out var pathByRootId) && 
                        pathByRootId.ServarrType == "Radarr")
                    {
                        existingPath = pathByRootId;
                    }
                    else if (pathsByNormalizedPath.TryGetValue(normalizedRootPath, out var pathByPath))
                    {
                        existingPath = pathByPath;
                    }

                    if (existingPath != null)
                    {
                        // Map the path if mappings are configured (update if path changed)
                        var mappedPath = MapPath(rootFolder.Path, "Radarr");
                        var normalizedMappedPath = NormalizePath(mappedPath);
                        var normalizedExistingPath = NormalizePath(existingPath.Path);
                        
                        if (normalizedExistingPath != normalizedMappedPath)
                        {
                            // Update the dictionary if path changed
                            pathsByNormalizedPath.Remove(normalizedExistingPath);
                            pathsByNormalizedPath[normalizedMappedPath] = existingPath;
                            existingPath.Path = mappedPath;
                        }
                        
                        // Update existing path
                        existingPath.ServarrRootFolderId = rootFolder.Id;
                        existingPath.ServarrRootFolderPath = rootFolder.Path;
                        existingPath.LastSyncedAt = DateTime.UtcNow;
                        existingPath.Category = "Movie";
                        if (string.IsNullOrEmpty(existingPath.Name))
                        {
                            existingPath.Name = $"Radarr: {System.IO.Path.GetFileName(mappedPath)}";
                        }
                        if (existingPath.ServarrType != "Radarr")
                        {
                            existingPath.ServarrType = "Radarr";
                        }
                        result.UpdatedPaths++;
                    }
                    else
                    {
                        // Map the path if mappings are configured
                        var mappedPath = MapPath(rootFolder.Path, "Radarr");
                        var normalizedMappedPath = NormalizePath(mappedPath);
                        
                        // Create new library path
                        var newPath = new LibraryPath
                        {
                            Path = mappedPath,
                            Name = $"Radarr: {System.IO.Path.GetFileName(mappedPath)}",
                            Category = "Movie",
                            IsActive = true,
                            ServarrType = "Radarr",
                            ServarrRootFolderId = rootFolder.Id,
                            ServarrRootFolderPath = rootFolder.Path,
                            LastSyncedAt = DateTime.UtcNow
                        };
                        _dbContext.LibraryPaths.Add(newPath);
                        
                        // Add to dictionaries for potential future lookups in this sync
                        pathsByNormalizedPath[normalizedMappedPath] = newPath;
                        pathsByRootFolderId[rootFolder.Id] = newPath;
                        radarrPaths.Add(newPath);
                        
                        result.CreatedPaths++;
                    }
                }

                // Remove library paths that are no longer in Radarr (using HashSet for O(1) lookup)
                var pathsToRemove = radarrPaths
                    .Where(ep => ep.ServarrRootFolderId.HasValue && 
                                !rootFolderIds.Contains(ep.ServarrRootFolderId.Value))
                    .ToList();

                foreach (var pathToRemove in pathsToRemove)
                {
                    _logger.LogInformation("Removing library path that no longer exists in Radarr: {Path}", pathToRemove.Path);
                    pathToRemove.ServarrType = null;
                    pathToRemove.ServarrRootFolderId = null;
                    pathToRemove.ServarrRootFolderPath = null;
                    result.RemovedPaths++;
                }

                await _dbContext.SaveChangesAsync();

                result.Success = true;
                result.Message = $"Synced {result.CreatedPaths} new paths, updated {result.UpdatedPaths} existing paths";
                result.CompletedAt = DateTime.UtcNow;
                result.Duration = result.CompletedAt.Value - result.StartedAt;

                _logger.LogInformation("Radarr sync completed: {Message}", result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Radarr library");
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
                result.CompletedAt = DateTime.UtcNow;
                result.Duration = result.CompletedAt.Value - result.StartedAt;
            }

            return result;
        }

        public async Task<ServarrSyncResult> SyncSonarrAsync()
        {
            var result = new ServarrSyncResult
            {
                ServiceType = "Sonarr",
                StartedAt = DateTime.UtcNow
            };

            if (!_sonarrService.IsEnabled || !_sonarrService.IsConnected)
            {
                result.Success = false;
                result.Message = "Sonarr is not enabled or connected";
                return result;
            }

            try
            {
                _logger.LogInformation("Starting Sonarr library sync");

                // Get root folders from Sonarr
                var rootFolders = await _sonarrService.GetRootFolders();
                result.RootFoldersFound = rootFolders.Count;

                // Skip series fetch for performance - it's only used for counting which is not critical
                // If needed, this can be made optional or fetched in parallel
                result.ItemsFound = 0; // Will be set if series count is needed later

                // Get root folder IDs for efficient lookup
                var rootFolderIds = rootFolders.Select(rf => rf.Id).ToHashSet();

                // Optimize: Only query paths that could match (Sonarr paths or paths matching root folder IDs)
                // This reduces the dataset significantly compared to loading all paths
                var existingPathsQuery = _dbContext.LibraryPaths
                    .Where(lp => lp.ServarrType == "Sonarr" || 
                                (lp.ServarrRootFolderId.HasValue && rootFolderIds.Contains(lp.ServarrRootFolderId.Value)));

                // Build lookup dictionaries for O(1) access instead of O(n) FirstOrDefault
                var existingPaths = await existingPathsQuery.ToListAsync();
                
                // Dictionary keyed by normalized path for fast lookup
                var pathsByNormalizedPath = new Dictionary<string, LibraryPath>(StringComparer.OrdinalIgnoreCase);
                
                // Dictionary keyed by root folder ID for fast lookup
                var pathsByRootFolderId = new Dictionary<int, LibraryPath>();

                // Pre-normalize and index all existing paths
                foreach (var path in existingPaths)
                {
                    var normalized = NormalizePath(path.Path);
                    if (!pathsByNormalizedPath.ContainsKey(normalized))
                    {
                        pathsByNormalizedPath[normalized] = path;
                    }
                    
                    if (path.ServarrRootFolderId.HasValue && !pathsByRootFolderId.ContainsKey(path.ServarrRootFolderId.Value))
                    {
                        pathsByRootFolderId[path.ServarrRootFolderId.Value] = path;
                    }
                }

                // Get Sonarr-specific paths for cleanup
                var sonarrPaths = existingPaths.Where(lp => lp.ServarrType == "Sonarr").ToList();

                // Sync root folders as library paths
                foreach (var rootFolder in rootFolders)
                {
                    if (!rootFolder.Accessible)
                    {
                        _logger.LogWarning("Sonarr root folder is not accessible: {Path}", rootFolder.Path);
                        continue;
                    }

                    var normalizedRootPath = NormalizePath(rootFolder.Path);
                    
                    // O(1) lookup: Check by root folder ID first (most specific), then by normalized path
                    LibraryPath? existingPath = null;
                    if (pathsByRootFolderId.TryGetValue(rootFolder.Id, out var pathByRootId) && 
                        pathByRootId.ServarrType == "Sonarr")
                    {
                        existingPath = pathByRootId;
                    }
                    else if (pathsByNormalizedPath.TryGetValue(normalizedRootPath, out var pathByPath))
                    {
                        existingPath = pathByPath;
                    }

                    if (existingPath != null)
                    {
                        // Map the path if mappings are configured (update if path changed)
                        var mappedPath = MapPath(rootFolder.Path, "Sonarr");
                        var normalizedMappedPath = NormalizePath(mappedPath);
                        var normalizedExistingPath = NormalizePath(existingPath.Path);
                        
                        if (normalizedExistingPath != normalizedMappedPath)
                        {
                            // Update the dictionary if path changed
                            pathsByNormalizedPath.Remove(normalizedExistingPath);
                            pathsByNormalizedPath[normalizedMappedPath] = existingPath;
                            existingPath.Path = mappedPath;
                        }
                        
                        // Update existing path
                        existingPath.ServarrRootFolderId = rootFolder.Id;
                        existingPath.ServarrRootFolderPath = rootFolder.Path;
                        existingPath.LastSyncedAt = DateTime.UtcNow;
                        existingPath.Category = "TV Shows";
                        if (string.IsNullOrEmpty(existingPath.Name))
                        {
                            existingPath.Name = $"Sonarr: {System.IO.Path.GetFileName(mappedPath)}";
                        }
                        if (existingPath.ServarrType != "Sonarr")
                        {
                            existingPath.ServarrType = "Sonarr";
                        }
                        result.UpdatedPaths++;
                    }
                    else
                    {
                        // Map the path if mappings are configured
                        var mappedPath = MapPath(rootFolder.Path, "Sonarr");
                        var normalizedMappedPath = NormalizePath(mappedPath);
                        
                        // Create new library path
                        var newPath = new LibraryPath
                        {
                            Path = mappedPath,
                            Name = $"Sonarr: {System.IO.Path.GetFileName(mappedPath)}",
                            Category = "TV Shows",
                            IsActive = true,
                            ServarrType = "Sonarr",
                            ServarrRootFolderId = rootFolder.Id,
                            ServarrRootFolderPath = rootFolder.Path,
                            LastSyncedAt = DateTime.UtcNow
                        };
                        _dbContext.LibraryPaths.Add(newPath);
                        
                        // Add to dictionaries for potential future lookups in this sync
                        pathsByNormalizedPath[normalizedMappedPath] = newPath;
                        pathsByRootFolderId[rootFolder.Id] = newPath;
                        sonarrPaths.Add(newPath);
                        
                        result.CreatedPaths++;
                    }
                }

                // Remove library paths that are no longer in Sonarr (using HashSet for O(1) lookup)
                var pathsToRemove = sonarrPaths
                    .Where(ep => ep.ServarrRootFolderId.HasValue && 
                                !rootFolderIds.Contains(ep.ServarrRootFolderId.Value))
                    .ToList();

                foreach (var pathToRemove in pathsToRemove)
                {
                    _logger.LogInformation("Removing library path that no longer exists in Sonarr: {Path}", pathToRemove.Path);
                    pathToRemove.ServarrType = null;
                    pathToRemove.ServarrRootFolderId = null;
                    pathToRemove.ServarrRootFolderPath = null;
                    result.RemovedPaths++;
                }

                await _dbContext.SaveChangesAsync();

                result.Success = true;
                result.Message = $"Synced {result.CreatedPaths} new paths, updated {result.UpdatedPaths} existing paths";
                result.CompletedAt = DateTime.UtcNow;
                result.Duration = result.CompletedAt.Value - result.StartedAt;

                _logger.LogInformation("Sonarr sync completed: {Message}", result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Sonarr library");
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
                result.CompletedAt = DateTime.UtcNow;
                result.Duration = result.CompletedAt.Value - result.StartedAt;
            }

            return result;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        private string MapPath(string path, string servarrType)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Get path mappings from configuration
            var mappings = GetPathMappings(servarrType);
            
            if (mappings == null || mappings.Count == 0)
                return path; // No mappings configured, return original path

            var normalizedPath = NormalizePath(path);
            
            // Try to find a matching mapping (longest match first)
            var sortedMappings = mappings.OrderByDescending(m => NormalizePath(m.From).Length);
            
            foreach (var mapping in sortedMappings)
            {
                var normalizedFrom = NormalizePath(mapping.From);
                
                // Check if path starts with the "from" path (with or without trailing slash)
                if (normalizedPath == normalizedFrom || normalizedPath.StartsWith(normalizedFrom + "/"))
                {
                    // Replace the "from" part with the "to" part
                    var remainingPath = normalizedPath.Length > normalizedFrom.Length 
                        ? normalizedPath.Substring(normalizedFrom.Length) 
                        : "";
                    
                    var normalizedTo = NormalizePath(mapping.To);
                    var mappedPath = normalizedTo + remainingPath;
                    
                    // Preserve original path format (case, trailing slash)
                    if (path.EndsWith('/') && !mappedPath.EndsWith('/'))
                        mappedPath += '/';
                    else if (!path.EndsWith('/') && mappedPath.EndsWith('/'))
                        mappedPath = mappedPath.TrimEnd('/');
                    
                    _logger.LogDebug("Mapped path: {Original} -> {Mapped}", path, mappedPath);
                    return mappedPath;
                }
            }
            
            // No mapping found, return original path
            return path;
        }

        private List<PathMapping>? GetPathMappings(string servarrType)
        {
            try
            {
                var section = _configuration.GetSection($"Servarr:{servarrType}:PathMappings");
                
                if (section == null || !section.Exists())
                    return null;

                var mappings = new List<PathMapping>();
                foreach (var item in section.GetChildren())
                {
                    var from = item["From"];
                    var to = item["To"];
                    if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                    {
                        mappings.Add(new PathMapping { From = from, To = to });
                    }
                }
                return mappings;
            }
            catch
            {
                return null;
            }
        }
    }

    public class ServarrSyncResult
    {
        public string ServiceType { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration { get; set; }
        public int RootFoldersFound { get; set; }
        public int ItemsFound { get; set; }
        public int CreatedPaths { get; set; }
        public int UpdatedPaths { get; set; }
        public int RemovedPaths { get; set; }
    }

    public class PathMapping
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }
}


