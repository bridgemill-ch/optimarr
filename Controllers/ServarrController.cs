using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Optimarr.Services;

namespace Optimarr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServarrController : ControllerBase
    {
        private readonly SonarrService _sonarrService;
        private readonly RadarrService _radarrService;
        private readonly ServarrSyncService _syncService;
        private readonly VideoServarrMatcherService _matcherService;
        private readonly VideoMatchingProgressService _progressService;
        private readonly ILogger<ServarrController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly IServiceProvider _serviceProvider;

        public ServarrController(
            SonarrService sonarrService,
            RadarrService radarrService,
            ServarrSyncService syncService,
            VideoServarrMatcherService matcherService,
            VideoMatchingProgressService progressService,
            ILogger<ServarrController> logger,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            IServiceProvider serviceProvider)
        {
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _syncService = syncService;
            _matcherService = matcherService;
            _progressService = progressService;
            _logger = logger;
            _configuration = configuration;
            _environment = environment;
            _serviceProvider = serviceProvider;
        }

        [HttpGet("status")]
        public ActionResult<ServarrStatusResponse> GetStatus()
        {
            _logger.LogDebug("Servarr status check requested");
            return Ok(new ServarrStatusResponse
            {
                Sonarr = new ServiceStatus
                {
                    Enabled = _sonarrService.IsEnabled,
                    Connected = _sonarrService.IsConnected,
                    Version = _sonarrService.GetVersion()
                },
                Radarr = new ServiceStatus
                {
                    Enabled = _radarrService.IsEnabled,
                    Connected = _radarrService.IsConnected,
                    Version = _radarrService.GetVersion()
                }
            });
        }

        [HttpPost("sonarr/analyze-series/{seriesId}")]
        public async Task<ActionResult> AnalyzeSonarrSeries(int seriesId)
        {
            _logger.LogInformation("Sonarr series analysis requested for series ID: {SeriesId}", seriesId);
            try
            {
                if (!_sonarrService.IsEnabled)
                {
                    _logger.LogWarning("Sonarr analysis requested but Sonarr is not enabled");
                    return BadRequest(new { error = "Sonarr is not enabled" });
                }

                var episodes = await _sonarrService.GetEpisodesBySeries(seriesId);
                _logger.LogInformation("Retrieved {Count} episodes for series {SeriesId}", episodes.Count, seriesId);
                var results = new List<object>();

                foreach (var episode in episodes)
                {
                    if (!string.IsNullOrEmpty(episode.Path) && System.IO.File.Exists(episode.Path))
                    {
                        // Analyze each episode file
                        // This would integrate with the video analyzer
                        results.Add(new
                        {
                            EpisodeId = episode.Id,
                            EpisodeNumber = episode.EpisodeNumber,
                            Path = episode.Path,
                            Status = "Analyzed"
                        });
                    }
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing Sonarr series");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("radarr/analyze-movie/{movieId}")]
        public async Task<ActionResult> AnalyzeRadarrMovie(int movieId)
        {
            _logger.LogInformation("Radarr movie analysis requested for movie ID: {MovieId}", movieId);
            try
            {
                if (!_radarrService.IsEnabled)
                {
                    _logger.LogWarning("Radarr analysis requested but Radarr is not enabled");
                    return BadRequest(new { error = "Radarr is not enabled" });
                }

                var movie = await _radarrService.GetMovie(movieId);
                if (movie == null)
                {
                    return NotFound(new { error = "Movie not found" });
                }

                if (!string.IsNullOrEmpty(movie.Path) && System.IO.File.Exists(movie.Path))
                {
                    // Analyze movie file
                    return Ok(new
                    {
                        MovieId = movie.Id,
                        Title = movie.Title,
                        Path = movie.Path,
                        Status = "Ready for analysis"
                    });
                }

                return BadRequest(new { error = "Movie file not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing Radarr movie");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sonarr/test")]
        public async Task<ActionResult> TestSonarrConnection([FromBody] TestConnectionRequest request)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.BaseAddress = new Uri(request.BaseUrl);
                httpClient.DefaultRequestHeaders.Add("X-Api-Key", request.ApiKey);
                httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var response = await httpClient.GetAsync("/api/v3/system/status");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var status = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
                    var version = status.GetProperty("version").GetString();
                    return Ok(new { success = true, message = "Connection successful", version });
                }
                else
                {
                    return BadRequest(new { success = false, message = $"Connection failed: {response.StatusCode}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Sonarr connection");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("radarr/test")]
        public async Task<ActionResult> TestRadarrConnection([FromBody] TestConnectionRequest request)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.BaseAddress = new Uri(request.BaseUrl);
                httpClient.DefaultRequestHeaders.Add("X-Api-Key", request.ApiKey);
                httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var response = await httpClient.GetAsync("/api/v3/system/status");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var status = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
                    var version = status.GetProperty("version").GetString();
                    return Ok(new { success = true, message = "Connection successful", version });
                }
                else
                {
                    return BadRequest(new { success = false, message = $"Connection failed: {response.StatusCode}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Radarr connection");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("radarr/sync")]
        public async Task<ActionResult> SyncRadarr()
        {
            _logger.LogInformation("Radarr library sync requested");
            try
            {
                var result = await _syncService.SyncRadarrAsync();
                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Radarr library");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sonarr/sync")]
        public async Task<ActionResult> SyncSonarr()
        {
            _logger.LogInformation("Sonarr library sync requested");
            try
            {
                var result = await _syncService.SyncSonarrAsync();
                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Sonarr library");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sync-all")]
        public async Task<ActionResult> SyncAll()
        {
            _logger.LogInformation("Full Servarr library sync requested");
            try
            {
                var radarrResult = await _syncService.SyncRadarrAsync();
                var sonarrResult = await _syncService.SyncSonarrAsync();

                return Ok(new
                {
                    radarr = radarrResult,
                    sonarr = sonarrResult,
                    overallSuccess = radarrResult.Success && sonarrResult.Success
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Servarr libraries");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("sonarr/settings")]
        public ActionResult<object> GetSonarrSettings()
        {
            return Ok(new
            {
                baseUrl = _configuration["Servarr:Sonarr:BaseUrl"] ?? "",
                apiKey = _configuration["Servarr:Sonarr:ApiKey"] ?? "",
                enabled = _configuration.GetValue<bool>("Servarr:Sonarr:Enabled", false)
            });
        }

        [HttpPost("sonarr/settings")]
        public async Task<ActionResult> SaveSonarrSettings([FromBody] ServarrSettingsRequest request)
        {
            try
            {
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
                
                var jsonContent = System.IO.File.Exists(appsettingsPath) 
                    ? await System.IO.File.ReadAllTextAsync(appsettingsPath) 
                    : "{}";
                
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                var jsonObject = new System.Text.Json.Nodes.JsonObject();
                
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name != "Servarr")
                    {
                        jsonObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                    }
                }
                
                var servarrObject = new System.Text.Json.Nodes.JsonObject();
                if (root.TryGetProperty("Servarr", out var existingServarr))
                {
                    foreach (var prop in existingServarr.EnumerateObject())
                    {
                        if (prop.Name != "Sonarr")
                        {
                            servarrObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                        }
                    }
                }
                
                var sonarrSettings = new System.Text.Json.Nodes.JsonObject
                {
                    ["BaseUrl"] = request.BaseUrl ?? "",
                    ["ApiKey"] = request.ApiKey ?? "",
                    ["Enabled"] = request.Enabled
                };
                servarrObject["Sonarr"] = sonarrSettings;
                jsonObject["Servarr"] = servarrObject;
                
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var newJson = jsonObject.ToJsonString(options);
                await System.IO.File.WriteAllTextAsync(appsettingsPath, newJson);
                
                _logger.LogInformation("Sonarr settings saved");
                
                if (_configuration is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                    _logger.LogInformation("Configuration reloaded");
                }
                
                return Ok(new { message = "Sonarr settings saved successfully. The service will reconnect with the new settings." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Sonarr settings");
                return StatusCode(500, new { error = $"Failed to save Sonarr settings: {ex.Message}" });
            }
        }

        [HttpGet("radarr/settings")]
        public ActionResult<object> GetRadarrSettings()
        {
            return Ok(new
            {
                baseUrl = _configuration["Servarr:Radarr:BaseUrl"] ?? "",
                apiKey = _configuration["Servarr:Radarr:ApiKey"] ?? "",
                enabled = _configuration.GetValue<bool>("Servarr:Radarr:Enabled", false)
            });
        }

        [HttpPost("radarr/settings")]
        public async Task<ActionResult> SaveRadarrSettings([FromBody] ServarrSettingsRequest request)
        {
            try
            {
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
                
                var jsonContent = System.IO.File.Exists(appsettingsPath) 
                    ? await System.IO.File.ReadAllTextAsync(appsettingsPath) 
                    : "{}";
                
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                var jsonObject = new System.Text.Json.Nodes.JsonObject();
                
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name != "Servarr")
                    {
                        jsonObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                    }
                }
                
                var servarrObject = new System.Text.Json.Nodes.JsonObject();
                if (root.TryGetProperty("Servarr", out var existingServarr))
                {
                    foreach (var prop in existingServarr.EnumerateObject())
                    {
                        if (prop.Name != "Radarr")
                        {
                            servarrObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                        }
                    }
                }
                
                var radarrSettings = new System.Text.Json.Nodes.JsonObject
                {
                    ["BaseUrl"] = request.BaseUrl ?? "",
                    ["ApiKey"] = request.ApiKey ?? "",
                    ["Enabled"] = request.Enabled
                };
                servarrObject["Radarr"] = radarrSettings;
                jsonObject["Servarr"] = servarrObject;
                
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var newJson = jsonObject.ToJsonString(options);
                await System.IO.File.WriteAllTextAsync(appsettingsPath, newJson);
                
                _logger.LogInformation("Radarr settings saved");
                
                if (_configuration is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                    _logger.LogInformation("Configuration reloaded");
                }
                
                return Ok(new { message = "Radarr settings saved successfully. The service will reconnect with the new settings." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Radarr settings");
                return StatusCode(500, new { error = $"Failed to save Radarr settings: {ex.Message}" });
            }
        }

        [HttpGet("sonarr/path-mappings")]
        public ActionResult<object> GetSonarrPathMappings()
        {
            try
            {
                var section = _configuration.GetSection("Servarr:Sonarr:PathMappings");
                var mappings = new List<PathMappingRequest>();
                
                if (section.Exists())
                {
                    foreach (var item in section.GetChildren())
                    {
                        var from = item["From"] ?? "";
                        var to = item["To"] ?? "";
                        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                        {
                            mappings.Add(new PathMappingRequest { From = from, To = to });
                        }
                    }
                }
                
                return Ok(new { mappings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Sonarr path mappings");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sonarr/path-mappings")]
        public async Task<ActionResult> SaveSonarrPathMappings([FromBody] List<PathMappingRequest> mappings)
        {
            try
            {
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
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(appsettingsPath)!);
                }
                
                var jsonContent = System.IO.File.Exists(appsettingsPath) 
                    ? await System.IO.File.ReadAllTextAsync(appsettingsPath) 
                    : "{}";
                
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                var jsonObject = new System.Text.Json.Nodes.JsonObject();
                
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name != "Servarr")
                    {
                        jsonObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                    }
                }
                
                var servarrObject = new System.Text.Json.Nodes.JsonObject();
                if (root.TryGetProperty("Servarr", out var existingServarr))
                {
                    foreach (var prop in existingServarr.EnumerateObject())
                    {
                        if (prop.Name != "Sonarr")
                        {
                            servarrObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                        }
                    }
                }
                
                var sonarrObject = new System.Text.Json.Nodes.JsonObject();
                if (root.TryGetProperty("Servarr", out var servarr) && servarr.TryGetProperty("Sonarr", out var existingSonarr))
                {
                    foreach (var prop in existingSonarr.EnumerateObject())
                    {
                        if (prop.Name != "PathMappings")
                        {
                            sonarrObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                        }
                    }
                }
                
                // Add path mappings
                var mappingsArray = new System.Text.Json.Nodes.JsonArray();
                foreach (var mapping in mappings.Where(m => !string.IsNullOrEmpty(m.From) && !string.IsNullOrEmpty(m.To)))
                {
                    var mappingObject = new System.Text.Json.Nodes.JsonObject
                    {
                        ["From"] = mapping.From,
                        ["To"] = mapping.To
                    };
                    mappingsArray.Add(mappingObject);
                }
                sonarrObject["PathMappings"] = mappingsArray;
                servarrObject["Sonarr"] = sonarrObject;
                jsonObject["Servarr"] = servarrObject;
                
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var newJson = jsonObject.ToJsonString(options);
                await System.IO.File.WriteAllTextAsync(appsettingsPath, newJson);
                
                if (_configuration is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                }
                
                return Ok(new { message = "Path mappings saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Sonarr path mappings");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("radarr/path-mappings")]
        public ActionResult<object> GetRadarrPathMappings()
        {
            try
            {
                var section = _configuration.GetSection("Servarr:Radarr:PathMappings");
                var mappings = new List<PathMappingRequest>();
                
                if (section.Exists())
                {
                    foreach (var item in section.GetChildren())
                    {
                        var from = item["From"] ?? "";
                        var to = item["To"] ?? "";
                        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                        {
                            mappings.Add(new PathMappingRequest { From = from, To = to });
                        }
                    }
                }
                
                return Ok(new { mappings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Radarr path mappings");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("radarr/path-mappings")]
        public async Task<ActionResult> SaveRadarrPathMappings([FromBody] List<PathMappingRequest> mappings)
        {
            try
            {
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
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(appsettingsPath)!);
                }
                
                var jsonContent = System.IO.File.Exists(appsettingsPath) 
                    ? await System.IO.File.ReadAllTextAsync(appsettingsPath) 
                    : "{}";
                
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                var jsonObject = new System.Text.Json.Nodes.JsonObject();
                
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name != "Servarr")
                    {
                        jsonObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                    }
                }
                
                var servarrObject = new System.Text.Json.Nodes.JsonObject();
                if (root.TryGetProperty("Servarr", out var existingServarr))
                {
                    foreach (var prop in existingServarr.EnumerateObject())
                    {
                        if (prop.Name != "Radarr")
                        {
                            servarrObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                        }
                    }
                }
                
                var radarrObject = new System.Text.Json.Nodes.JsonObject();
                if (root.TryGetProperty("Servarr", out var servarr) && servarr.TryGetProperty("Radarr", out var existingRadarr))
                {
                    foreach (var prop in existingRadarr.EnumerateObject())
                    {
                        if (prop.Name != "PathMappings")
                        {
                            radarrObject[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                        }
                    }
                }
                
                // Add path mappings
                var mappingsArray = new System.Text.Json.Nodes.JsonArray();
                foreach (var mapping in mappings.Where(m => !string.IsNullOrEmpty(m.From) && !string.IsNullOrEmpty(m.To)))
                {
                    var mappingObject = new System.Text.Json.Nodes.JsonObject
                    {
                        ["From"] = mapping.From,
                        ["To"] = mapping.To
                    };
                    mappingsArray.Add(mappingObject);
                }
                radarrObject["PathMappings"] = mappingsArray;
                servarrObject["Radarr"] = radarrObject;
                jsonObject["Servarr"] = servarrObject;
                
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var newJson = jsonObject.ToJsonString(options);
                await System.IO.File.WriteAllTextAsync(appsettingsPath, newJson);
                
                if (_configuration is Microsoft.Extensions.Configuration.IConfigurationRoot configRoot)
                {
                    configRoot.Reload();
                }
                
                return Ok(new { message = "Path mappings saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Radarr path mappings");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("match-videos")]
        public async Task<ActionResult> MatchVideosWithServarr()
        {
            _logger.LogInformation("Video matching with Servarr requested");
            try
            {
                // Check if services are available
                if (!_sonarrService.IsEnabled && !_radarrService.IsEnabled)
                {
                    return BadRequest(new { error = "Neither Sonarr nor Radarr is enabled. Please configure at least one service in Settings." });
                }

                if ((_sonarrService.IsEnabled && !_sonarrService.IsConnected) && 
                    (_radarrService.IsEnabled && !_radarrService.IsConnected))
                {
                    return BadRequest(new { error = "Servarr services are enabled but not connected. Please check your connection settings." });
                }

                // Generate match ID
                var matchId = Guid.NewGuid().ToString();
                _progressService.CreateProgress(matchId);

                // Start matching in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var matcherService = scope.ServiceProvider.GetRequiredService<VideoServarrMatcherService>();
                        var progressService = scope.ServiceProvider.GetRequiredService<VideoMatchingProgressService>();
                        
                        _logger.LogInformation("Starting video matching process (matchId: {MatchId})...", matchId);
                        await matcherService.MatchAllVideosAsync(progressService, matchId);
                        _logger.LogInformation("Video matching completed (matchId: {MatchId})", matchId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background video matching (matchId: {MatchId})", matchId);
                        _progressService.FailProgress(matchId, ex.Message);
                    }
                });

                return Ok(new { matchId, message = "Video matching started" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting video matching with Servarr");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("match-videos/progress/{matchId}")]
        public ActionResult<VideoMatchingProgress> GetMatchProgress(string matchId)
        {
            var progress = _progressService.GetProgress(matchId);
            if (progress == null)
            {
                return NotFound(new { error = "Match progress not found" });
            }
            return Ok(progress);
        }

        [HttpGet("match-videos/active")]
        public ActionResult<object> GetActiveMatches()
        {
            try
            {
                var activeMatchId = _progressService.GetActiveMatchId();
                if (activeMatchId == null)
                {
                    return Ok(new { activeMatchId = null, hasActiveMatch = false });
                }

                var progress = _progressService.GetProgress(activeMatchId);
                return Ok(new 
                { 
                    activeMatchId = activeMatchId,
                    hasActiveMatch = true,
                    progress = progress
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active matches");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("match-videos/{libraryPathId}")]
        public async Task<ActionResult> MatchVideosForLibraryPath(int libraryPathId)
        {
            _logger.LogInformation("Video matching for library path {LibraryPathId} requested", libraryPathId);
            try
            {
                var matchedCount = await _matcherService.MatchVideosForLibraryPathAsync(libraryPathId);
                return Ok(new { matched = matchedCount, message = $"Matched {matchedCount} videos for library path" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error matching videos for library path {LibraryPathId}", libraryPathId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

    }

    public class TestConnectionRequest
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    public class ServarrStatusResponse
    {
        public ServiceStatus Sonarr { get; set; } = new();
        public ServiceStatus Radarr { get; set; } = new();
    }

    public class ServiceStatus
    {
        public bool Enabled { get; set; }
        public bool Connected { get; set; }
        public string? Version { get; set; }
    }

    public class ServarrSettingsRequest
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    public class PathMappingRequest
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

}

