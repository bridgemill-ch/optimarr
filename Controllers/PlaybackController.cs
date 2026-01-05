using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Optimarr.Data;
using Optimarr.Models;
using Optimarr.Services;

namespace Optimarr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlaybackController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly JellyfinService _jellyfinService;
        private readonly ILogger<PlaybackController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public PlaybackController(
            AppDbContext dbContext,
            JellyfinService jellyfinService,
            ILogger<PlaybackController> logger,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _dbContext = dbContext;
            _jellyfinService = jellyfinService;
            _logger = logger;
            _configuration = configuration;
            _environment = environment;
        }

        [HttpGet("status")]
        public ActionResult<object> GetStatus()
        {
            return Ok(new
            {
                enabled = _jellyfinService.IsEnabled,
                connected = _jellyfinService.IsConnected
            });
        }

        [HttpGet("settings")]
        public ActionResult<object> GetJellyfinSettings()
        {
            return Ok(new
            {
                baseUrl = _configuration["Jellyfin:BaseUrl"] ?? "",
                apiKey = _configuration["Jellyfin:ApiKey"] ?? "",
                username = _configuration["Jellyfin:Username"] ?? "",
                password = "", // Never return password
                enabled = _configuration.GetValue<bool>("Jellyfin:Enabled", false)
            });
        }

        [HttpPost("settings")]
        public async Task<ActionResult> SaveJellyfinSettings([FromBody] JellyfinSettingsRequest request)
        {
            try
            {
                // Get the path to appsettings.json (same logic as LibraryController)
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
                
                if (!System.IO.File.Exists(appsettingsPath))
                {
                    _logger.LogWarning("appsettings.json not found at {Path}, creating new file", appsettingsPath);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(appsettingsPath)!);
                }
                
                // Read existing configuration
                var jsonContent = System.IO.File.Exists(appsettingsPath) 
                    ? await System.IO.File.ReadAllTextAsync(appsettingsPath) 
                    : "{}";
                
                // Parse JSON and update the Jellyfin section
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                // Get existing password if not provided in request
                string? existingPassword = null;
                if (root.TryGetProperty("Jellyfin", out var existingJellyfin))
                {
                    if (existingJellyfin.TryGetProperty("Password", out var passwordProp))
                    {
                        existingPassword = passwordProp.GetString();
                    }
                }
                
                // Create a mutable JSON structure using JsonObject
                var jsonObject = new System.Text.Json.Nodes.JsonObject();
                
                // Copy all existing properties except Jellyfin
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name != "Jellyfin")
                    {
                        jsonObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                    }
                }
                
                // Update Jellyfin section - preserve password if not provided
                var passwordToSave = string.IsNullOrEmpty(request.Password) 
                    ? existingPassword ?? "" 
                    : request.Password;
                
                var jellyfinSettings = new System.Text.Json.Nodes.JsonObject
                {
                    ["BaseUrl"] = request.BaseUrl ?? "",
                    ["ApiKey"] = request.ApiKey ?? "",
                    ["Username"] = request.Username ?? "",
                    ["Password"] = passwordToSave,
                    ["Enabled"] = request.Enabled
                };
                jsonObject["Jellyfin"] = jellyfinSettings;
                
                // Write back to file with proper formatting
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var newJson = jsonObject.ToJsonString(options);
                await System.IO.File.WriteAllTextAsync(appsettingsPath, newJson);
                
                _logger.LogInformation("Jellyfin settings saved");
                
                // Trigger configuration reload if IConfigurationRoot is available
                if (_configuration is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                    _logger.LogInformation("Configuration reloaded");
                }
                else
                {
                    _logger.LogWarning("Configuration is not IConfigurationRoot, cannot reload. Changes may not take effect until restart.");
                }
                
                return Ok(new { message = "Jellyfin settings saved successfully. The service will reconnect with the new settings." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Jellyfin settings");
                return StatusCode(500, new { error = $"Failed to save Jellyfin settings: {ex.Message}" });
            }
        }

        [HttpPost("test")]
        public async Task<ActionResult> TestJellyfinConnection([FromBody] TestJellyfinConnectionRequest request)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.BaseAddress = new Uri(request.BaseUrl);
                httpClient.DefaultRequestHeaders.Add("X-Emby-Authorization", 
                    "MediaBrowser Client=\"optimarr\", Device=\"optimarr\", DeviceId=\"optimarr\", Version=\"1.0.0\"");

                // Try API key first if provided
                if (!string.IsNullOrEmpty(request.ApiKey))
                {
                    httpClient.DefaultRequestHeaders.Add("X-Emby-Token", request.ApiKey);
                }
                else if (!string.IsNullOrEmpty(request.Username) && !string.IsNullOrEmpty(request.Password))
                {
                    // Authenticate with username/password
                    var authRequest = new
                    {
                        Username = request.Username,
                        Pw = request.Password
                    };

                    var content = new System.Net.Http.StringContent(
                        System.Text.Json.JsonSerializer.Serialize(authRequest),
                        System.Text.Encoding.UTF8,
                        "application/json");

                    var authResponse = await httpClient.PostAsync("/Users/authenticatebyname", content);
                    if (!authResponse.IsSuccessStatusCode)
                    {
                        return BadRequest(new { success = false, message = "Authentication failed" });
                    }
                }
                else
                {
                    return BadRequest(new { success = false, message = "Either API Key or Username/Password must be provided" });
                }

                httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var response = await httpClient.GetAsync("/System/Info");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var info = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    var version = info.TryGetProperty("Version", out var versionProp) ? versionProp.GetString() : "Unknown";
                    return Ok(new { success = true, message = "Connection successful", version });
                }
                else
                {
                    return BadRequest(new { success = false, message = $"Connection failed: {response.StatusCode}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Jellyfin connection");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("sync")]
        public async Task<ActionResult<object>> SyncPlaybackHistory([FromQuery] int? days = 30)
        {
            if (!_jellyfinService.IsEnabled || !_jellyfinService.IsConnected)
            {
                return BadRequest(new { error = "Jellyfin service is not enabled or connected" });
            }

            try
            {
                _logger.LogInformation("Starting playback history sync (last {Days} days)", days);
                
                var startDate = DateTime.UtcNow.AddDays(-(days ?? 30));
                var historyItems = await _jellyfinService.GetPlaybackHistoryAsync(startDate, DateTime.UtcNow);
                
                _logger.LogInformation("Retrieved {Count} playback history items from Jellyfin", historyItems.Count);

                var syncedCount = 0;
                var matchedCount = 0;

                foreach (var item in historyItems)
                {
                    if (string.IsNullOrEmpty(item.Path)) continue;

                    // Check if already exists
                    var existing = await _dbContext.PlaybackHistories
                        .FirstOrDefaultAsync(p => 
                            p.JellyfinItemId == item.ItemId && 
                            p.PlaybackStartTime == item.PlaybackStartTime &&
                            p.JellyfinUserId == item.UserId);

                    if (existing != null) continue;

                    var playback = new PlaybackHistory
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
                    await MatchPlaybackWithLibrary(playback);

                    _dbContext.PlaybackHistories.Add(playback);
                    syncedCount++;
                    
                    if (playback.VideoAnalysisId != null || playback.LibraryPathId != null)
                    {
                        matchedCount++;
                    }
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Synced {SyncedCount} playback records, matched {MatchedCount} with local libraries", 
                    syncedCount, matchedCount);

                return Ok(new
                {
                    synced = syncedCount,
                    matched = matchedCount,
                    total = historyItems.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing playback history");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task MatchPlaybackWithLibrary(PlaybackHistory playback)
        {
            if (string.IsNullOrEmpty(playback.FilePath)) return;

            // Normalize path for comparison
            var normalizedPlaybackPath = NormalizePath(playback.FilePath);

            // Try to match with VideoAnalysis by file path
            var videoAnalysis = await _dbContext.VideoAnalyses
                .FirstOrDefaultAsync(v => NormalizePath(v.FilePath) == normalizedPlaybackPath);

            if (videoAnalysis != null)
            {
                playback.VideoAnalysisId = videoAnalysis.Id;
            }

            // Try to match with LibraryPath
            var libraryPaths = await _dbContext.LibraryPaths
                .Where(lp => lp.IsActive)
                .ToListAsync();

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

        [HttpGet("statistics")]
        public async Task<ActionResult<object>> GetPlaybackStatistics([FromQuery] int? libraryPathId = null)
        {
            try
            {
                var query = _dbContext.PlaybackHistories.AsQueryable();

                if (libraryPathId.HasValue)
                {
                    query = query.Where(p => p.LibraryPathId == libraryPathId.Value);
                }

                var total = await query.CountAsync();
                var directPlay = await query.CountAsync(p => p.IsDirectPlay);
                var directStream = await query.CountAsync(p => p.IsDirectStream);
                var transcode = await query.CountAsync(p => p.IsTranscode);

                var byDevice = await query
                    .Where(p => !string.IsNullOrEmpty(p.DeviceName))
                    .GroupBy(p => p.DeviceName)
                    .Select(g => new { Device = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                var byClient = await query
                    .Where(p => !string.IsNullOrEmpty(p.ClientName))
                    .GroupBy(p => p.ClientName)
                    .Select(g => new { Client = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                var transcodeReasons = await query
                    .Where(p => p.IsTranscode && !string.IsNullOrEmpty(p.TranscodeReason))
                    .GroupBy(p => p.TranscodeReason)
                    .Select(g => new { Reason = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                return Ok(new
                {
                    total,
                    directPlay,
                    directStream,
                    transcode,
                    byDevice,
                    byClient,
                    transcodeReasons
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting playback statistics");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("clients")]
        public async Task<ActionResult<object>> GetUsedClients()
        {
            try
            {
                // Get all unique clients from playback history
                var clients = await _dbContext.PlaybackHistories
                    .Where(p => !string.IsNullOrEmpty(p.ClientName))
                    .GroupBy(p => new { p.ClientName, p.DeviceName })
                    .Select(g => new
                    {
                        clientName = g.Key.ClientName,
                        deviceName = g.Key.DeviceName,
                        playbackCount = g.Count(),
                        directPlayCount = g.Count(p => p.IsDirectPlay),
                        directStreamCount = g.Count(p => p.IsDirectStream),
                        transcodeCount = g.Count(p => p.IsTranscode),
                        lastUsed = g.Max(p => p.PlaybackStartTime)
                    })
                    .OrderByDescending(x => x.playbackCount)
                    .ToListAsync();

                // Also get unique client names only (for summary)
                var uniqueClientNames = await _dbContext.PlaybackHistories
                    .Where(p => !string.IsNullOrEmpty(p.ClientName))
                    .Select(p => p.ClientName!)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToListAsync();

                return Ok(new
                {
                    clients,
                    uniqueClientNames,
                    totalClients = uniqueClientNames.Count,
                    totalDevices = clients.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting used clients");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("history")]
        public async Task<ActionResult<object>> GetPlaybackHistory(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] int? libraryPathId = null,
            [FromQuery] bool? isTranscode = null,
            [FromQuery] string? deviceName = null)
        {
            try
            {
                var query = _dbContext.PlaybackHistories.AsQueryable();

                if (libraryPathId.HasValue)
                {
                    query = query.Where(p => p.LibraryPathId == libraryPathId.Value);
                }

                if (isTranscode.HasValue)
                {
                    query = query.Where(p => p.IsTranscode == isTranscode.Value);
                }

                if (!string.IsNullOrEmpty(deviceName))
                {
                    query = query.Where(p => p.DeviceName == deviceName);
                }

                var total = await query.CountAsync();
                var items = await query
                    .Include(p => p.VideoAnalysis)
                    .Include(p => p.LibraryPath)
                    .OrderByDescending(p => p.PlaybackStartTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        id = p.Id,
                        itemName = p.ItemName,
                        filePath = p.FilePath,
                        playbackStartTime = p.PlaybackStartTime,
                        playbackStopTime = p.PlaybackStopTime,
                        playbackDuration = p.PlaybackDuration,
                        clientName = p.ClientName,
                        deviceName = p.DeviceName,
                        playMethod = p.PlayMethod,
                        isDirectPlay = p.IsDirectPlay,
                        isDirectStream = p.IsDirectStream,
                        isTranscode = p.IsTranscode,
                        transcodeReason = p.TranscodeReason,
                        hardwareAccelerationType = p.HardwareAccelerationType,
                        videoAnalysisId = p.VideoAnalysisId,
                        libraryPathId = p.LibraryPathId,
                        libraryPathName = p.LibraryPath != null ? p.LibraryPath.Name : null
                    })
                    .ToListAsync();

                return Ok(new
                {
                    items,
                    total,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting playback history");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class TestJellyfinConnectionRequest
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public class JellyfinSettingsRequest
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool Enabled { get; set; }
    }
}

