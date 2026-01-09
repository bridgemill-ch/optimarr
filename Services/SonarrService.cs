using System.Net.Http.Headers;
using System.Text.Json;

namespace Optimarr.Services
{
    public class SonarrService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<SonarrService>? _logger;
        private bool _isEnabled;
        private bool _isConnected;

        private string? _apiKey;
        private string? _baseUrl;
        private string? _basicAuthUsername;
        private string? _basicAuthPassword;

        public SonarrService(IConfiguration configuration, ILogger<SonarrService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout for long operations
            Initialize();
        }

        private void Initialize()
        {
            _baseUrl = _configuration["Servarr:Sonarr:BaseUrl"];
            _apiKey = _configuration["Servarr:Sonarr:ApiKey"];
            _basicAuthUsername = _configuration["Servarr:Sonarr:BasicAuthUsername"];
            _basicAuthPassword = _configuration["Servarr:Sonarr:BasicAuthPassword"];
            _isEnabled = _configuration.GetValue<bool>("Servarr:Sonarr:Enabled") && !string.IsNullOrEmpty(_baseUrl) && !string.IsNullOrEmpty(_apiKey);

            if (_isEnabled)
            {
                _httpClient.BaseAddress = new Uri(_baseUrl!);
                _httpClient.DefaultRequestHeaders.Clear(); // Clear any existing headers
                _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _apiKey!);
                
                // Add Basic Auth if credentials are provided
                if (!string.IsNullOrEmpty(_basicAuthUsername) && !string.IsNullOrEmpty(_basicAuthPassword))
                {
                    var authValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_basicAuthUsername}:{_basicAuthPassword}"));
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                }
                
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                CheckConnection();
            }
        }

        /// <summary>
        /// Ensures API key is set on request headers (in case headers were cleared)
        /// </summary>
        private void EnsureApiKeyHeader()
        {
            if (!string.IsNullOrEmpty(_apiKey) && !_httpClient.DefaultRequestHeaders.Contains("X-Api-Key"))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
            }
        }

        public bool IsEnabled => _isEnabled;
        public bool IsConnected => _isConnected;

        public string? GetVersion()
        {
            if (!_isEnabled) return null;
            try
            {
                EnsureApiKeyHeader();
                var response = _httpClient.GetAsync("/api/v3/system/status").Result;
                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    var status = JsonSerializer.Deserialize<JsonElement>(content);
                    return status.GetProperty("version").GetString();
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger?.LogWarning("Unauthorized access to Sonarr system status. Check API key.");
                    _isConnected = false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr version");
                _isConnected = false;
            }
            return null;
        }

        private void CheckConnection()
        {
            try
            {
                EnsureApiKeyHeader();
                var response = _httpClient.GetAsync("/api/v3/system/status").Result;
                _isConnected = response.IsSuccessStatusCode;
                if (!_isConnected)
                {
                    _logger?.LogWarning("Sonarr connection check failed: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger?.LogWarning(ex, "Sonarr connection check failed");
            }
        }

        public async Task<SonarrEpisode?> GetEpisode(int episodeId)
        {
            if (!_isEnabled || !_isConnected)
                return null;

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.GetAsync($"/api/v3/episode/{episodeId}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var episode = JsonSerializer.Deserialize<SonarrEpisode>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return episode;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr episode {EpisodeId}", episodeId);
            }

            return null;
        }

        public async Task<List<SonarrEpisode>> GetEpisodesBySeries(int seriesId)
        {
            if (!_isEnabled || !_isConnected)
                return new List<SonarrEpisode>();

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.GetAsync($"/api/v3/episode?seriesId={seriesId}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var episodes = JsonSerializer.Deserialize<List<SonarrEpisode>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return episodes ?? new List<SonarrEpisode>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger?.LogWarning("Sonarr API error for episodes (series {SeriesId}): {StatusCode} - {Error}", 
                        seriesId, response.StatusCode, errorContent);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr episodes for series {SeriesId}", seriesId);
            }

            return new List<SonarrEpisode>();
        }

        public async Task<SonarrEpisodeFile?> GetEpisodeFile(int episodeFileId)
        {
            if (!_isEnabled || !_isConnected)
                return null;

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.GetAsync($"/api/v3/episodefile/{episodeFileId}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var episodeFile = JsonSerializer.Deserialize<SonarrEpisodeFile>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return episodeFile;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger?.LogWarning("Unauthorized access to Sonarr episode file {EpisodeFileId}. Check API key.", episodeFileId);
                    _isConnected = false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr episode file: {EpisodeFileId}", episodeFileId);
            }

            return null;
        }

        public async Task<List<SonarrSeries>> GetSeries()
        {
            if (!_isEnabled || !_isConnected)
            {
                _logger?.LogWarning("Sonarr service is not enabled or not connected");
                return new List<SonarrSeries>();
            }

            EnsureApiKeyHeader();

            try
            {
                _logger?.LogDebug("Requesting Sonarr series list from {BaseUrl}/api/v3/series", _baseUrl);
                var response = await _httpClient.GetAsync("/api/v3/series");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var series = JsonSerializer.Deserialize<List<SonarrSeries>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    var count = series?.Count ?? 0;
                    _logger?.LogDebug("Successfully retrieved {Count} series from Sonarr", count);
                    return series ?? new List<SonarrSeries>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger?.LogWarning("Sonarr API error for series: {StatusCode} - {Error}", 
                        response.StatusCode, errorContent);
                    _isConnected = false; // Mark as disconnected on auth error
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr series");
                _isConnected = false; // Mark as disconnected on error
            }

            return new List<SonarrSeries>();
        }

        public async Task<List<SonarrRootFolder>> GetRootFolders()
        {
            if (!_isEnabled || !_isConnected)
                return new List<SonarrRootFolder>();

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.GetAsync("/api/v3/rootFolder");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var rootFolders = JsonSerializer.Deserialize<List<SonarrRootFolder>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return rootFolders ?? new List<SonarrRootFolder>();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr root folders");
            }

            return new List<SonarrRootFolder>();
        }

        public async Task<SonarrEpisode?> FindEpisodeByPath(string filePath)
        {
            if (!_isEnabled || !_isConnected)
                return null;

            EnsureApiKeyHeader();

            try
            {
                // Get all series
                var series = await GetSeries();
                if (series == null || series.Count == 0)
                    return null;

                // Normalize path for comparison
                var normalizedPath = NormalizePath(filePath);

                foreach (var s in series)
                {
                    // Get all episodes for this series
                    var episodes = await GetEpisodesBySeries(s.Id);
                    foreach (var episode in episodes)
                    {
                        if (episode.EpisodeFileId.HasValue)
                        {
                            // Get the episode file to check its path
                            var episodeFile = await GetEpisodeFile(episode.EpisodeFileId.Value);
                            if (episodeFile != null && !string.IsNullOrEmpty(episodeFile.Path))
                            {
                                var normalizedEpisodePath = NormalizePath(episodeFile.Path);
                                if (normalizedEpisodePath == normalizedPath)
                                {
                                    return episode;
                                }
                            }
                        }
                        // Fallback to episode path if available
                        if (!string.IsNullOrEmpty(episode.Path))
                        {
                            var normalizedEpisodePath = NormalizePath(episode.Path);
                            if (normalizedEpisodePath == normalizedPath)
                            {
                                return episode;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error finding Sonarr episode by path: {Path}", filePath);
            }

            return null;
        }

        public async Task<bool> DeleteEpisodeFile(int episodeFileId)
        {
            if (!_isEnabled || !_isConnected)
                return false;

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.DeleteAsync($"/api/v3/episodefile/{episodeFileId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting Sonarr episode file: {EpisodeFileId}", episodeFileId);
                return false;
            }
        }

        public async Task<bool> TriggerEpisodeSearch(int episodeId)
        {
            if (!_isEnabled || !_isConnected)
                return false;

            EnsureApiKeyHeader();

            try
            {
                var request = new
                {
                    name = "EpisodeSearch",
                    episodeIds = new[] { episodeId }
                };

                var content = new StringContent(JsonSerializer.Serialize(request), 
                    System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/v3/command", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error triggering Sonarr episode search: {EpisodeId}", episodeId);
                return false;
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }
    }

    public class SonarrSeries
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class SonarrEpisode
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int EpisodeNumber { get; set; }
        public int SeasonNumber { get; set; }
        public string? Path { get; set; }
        public bool HasFile { get; set; }
        public int? EpisodeFileId { get; set; }
    }

    public class SonarrEpisodeFile
    {
        public int Id { get; set; }
        public string? Path { get; set; }
        public int EpisodeId { get; set; }
    }

    public class SonarrRootFolder
    {
        public int Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool Accessible { get; set; }
        public long? FreeSpace { get; set; }
    }
}

