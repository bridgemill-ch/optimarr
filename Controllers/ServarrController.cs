using Microsoft.AspNetCore.Mvc;
using Optimarr.Services;

namespace Optimarr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServarrController : ControllerBase
    {
        private readonly SonarrService _sonarrService;
        private readonly RadarrService _radarrService;
        private readonly ILogger<ServarrController> _logger;

        public ServarrController(
            SonarrService sonarrService,
            RadarrService radarrService,
            ILogger<ServarrController> logger)
        {
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _logger = logger;
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

}

