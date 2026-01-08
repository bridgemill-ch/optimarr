using System.Net.Http.Headers;
using System.Text.Json;

namespace Optimarr.Services
{
    public class RadarrService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<RadarrService>? _logger;
        private bool _isEnabled;
        private bool _isConnected;

        private string? _apiKey;
        private string? _baseUrl;
        private string? _basicAuthUsername;
        private string? _basicAuthPassword;

        public RadarrService(IConfiguration configuration, ILogger<RadarrService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout for long operations
            Initialize();
        }

        private void Initialize()
        {
            _baseUrl = _configuration["Servarr:Radarr:BaseUrl"];
            _apiKey = _configuration["Servarr:Radarr:ApiKey"];
            _basicAuthUsername = _configuration["Servarr:Radarr:BasicAuthUsername"];
            _basicAuthPassword = _configuration["Servarr:Radarr:BasicAuthPassword"];
            _isEnabled = _configuration.GetValue<bool>("Servarr:Radarr:Enabled") && !string.IsNullOrEmpty(_baseUrl) && !string.IsNullOrEmpty(_apiKey);

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
                    _logger?.LogWarning("Unauthorized access to Radarr system status. Check API key.");
                    _isConnected = false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Radarr version");
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
                    _logger?.LogWarning("Radarr connection check failed: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger?.LogWarning(ex, "Radarr connection check failed");
            }
        }

        public async Task<RadarrMovie?> GetMovie(int movieId)
        {
            if (!_isEnabled || !_isConnected)
                return null;

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.GetAsync($"/api/v3/movie/{movieId}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var movie = JsonSerializer.Deserialize<RadarrMovie>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return movie;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Radarr movie");
            }

            return null;
        }

        public async Task<List<RadarrMovie>> GetMovies()
        {
            if (!_isEnabled || !_isConnected)
            {
                _logger?.LogWarning("Radarr service is not enabled or not connected");
                return new List<RadarrMovie>();
            }

            EnsureApiKeyHeader();

            try
            {
                _logger?.LogDebug("Requesting Radarr movies list from {BaseUrl}/api/v3/movie", _baseUrl);
                var response = await _httpClient.GetAsync("/api/v3/movie");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var movies = JsonSerializer.Deserialize<List<RadarrMovie>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    var count = movies?.Count ?? 0;
                    _logger?.LogDebug("Successfully retrieved {Count} movies from Radarr", count);
                    return movies ?? new List<RadarrMovie>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger?.LogWarning("Radarr API error for movies: {StatusCode} - {Error}", 
                        response.StatusCode, errorContent);
                    _isConnected = false; // Mark as disconnected on auth error
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Radarr movies");
                _isConnected = false; // Mark as disconnected on error
            }

            return new List<RadarrMovie>();
        }

        public async Task<List<RadarrRootFolder>> GetRootFolders()
        {
            if (!_isEnabled || !_isConnected)
                return new List<RadarrRootFolder>();

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.GetAsync("/api/v3/rootFolder");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var rootFolders = JsonSerializer.Deserialize<List<RadarrRootFolder>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return rootFolders ?? new List<RadarrRootFolder>();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Radarr root folders");
            }

            return new List<RadarrRootFolder>();
        }

        public async Task<RadarrMovie?> FindMovieByPath(string filePath)
        {
            if (!_isEnabled || !_isConnected)
                return null;

            EnsureApiKeyHeader();

            try
            {
                var movies = await GetMovies();
                if (movies == null || movies.Count == 0)
                    return null;

                // Normalize path for comparison
                var normalizedPath = NormalizePath(filePath);

                foreach (var movie in movies)
                {
                    if (movie.MovieFile != null && !string.IsNullOrEmpty(movie.MovieFile.Path))
                    {
                        var normalizedMoviePath = NormalizePath(movie.MovieFile.Path);
                        if (normalizedMoviePath == normalizedPath)
                        {
                            return movie;
                        }
                    }
                    // Also check the movie path itself
                    if (!string.IsNullOrEmpty(movie.Path))
                    {
                        var normalizedMoviePath = NormalizePath(movie.Path);
                        if (normalizedPath.StartsWith(normalizedMoviePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return movie;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error finding Radarr movie by path: {Path}", filePath);
            }

            return null;
        }

        public async Task<bool> DeleteMovieFile(int movieFileId)
        {
            if (!_isEnabled || !_isConnected)
                return false;

            EnsureApiKeyHeader();

            try
            {
                var response = await _httpClient.DeleteAsync($"/api/v3/movieFile/{movieFileId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting Radarr movie file: {MovieFileId}", movieFileId);
                return false;
            }
        }

        public async Task<bool> TriggerMovieSearch(int movieId)
        {
            if (!_isEnabled || !_isConnected)
                return false;

            EnsureApiKeyHeader();

            try
            {
                var request = new
                {
                    name = "MoviesSearch",
                    movieIds = new[] { movieId }
                };

                var content = new StringContent(JsonSerializer.Serialize(request), 
                    System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/v3/command", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error triggering Radarr movie search: {MovieId}", movieId);
                return false;
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }
    }

    public class RadarrMovie
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Path { get; set; }
        public bool HasFile { get; set; }
        public RadarrMovieFile? MovieFile { get; set; }
    }

    public class RadarrMovieFile
    {
        public int Id { get; set; }
        public string? Path { get; set; }
    }

    public class RadarrRootFolder
    {
        public int Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool Accessible { get; set; }
        public long? FreeSpace { get; set; }
    }
}

