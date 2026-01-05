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

        public RadarrService(IConfiguration configuration, ILogger<RadarrService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = new HttpClient();
            Initialize();
        }

        private void Initialize()
        {
            var baseUrl = _configuration["Servarr:Radarr:BaseUrl"];
            var apiKey = _configuration["Servarr:Radarr:ApiKey"];
            _isEnabled = _configuration.GetValue<bool>("Servarr:Radarr:Enabled") && !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey);

            if (_isEnabled)
            {
                _httpClient.BaseAddress = new Uri(baseUrl!);
                _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                CheckConnection();
            }
        }

        public bool IsEnabled => _isEnabled;
        public bool IsConnected => _isConnected;

        public string? GetVersion()
        {
            if (!_isEnabled) return null;
            try
            {
                var response = _httpClient.GetAsync("/api/v3/system/status").Result;
                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    var status = JsonSerializer.Deserialize<JsonElement>(content);
                    return status.GetProperty("version").GetString();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Radarr version");
            }
            return null;
        }

        private void CheckConnection()
        {
            try
            {
                var response = _httpClient.GetAsync("/api/v3/system/status").Result;
                _isConnected = response.IsSuccessStatusCode;
            }
            catch
            {
                _isConnected = false;
            }
        }

        public async Task<RadarrMovie?> GetMovie(int movieId)
        {
            if (!_isEnabled || !_isConnected)
                return null;

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
                return new List<RadarrMovie>();

            try
            {
                var response = await _httpClient.GetAsync("/api/v3/movie");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var movies = JsonSerializer.Deserialize<List<RadarrMovie>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return movies ?? new List<RadarrMovie>();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Radarr movies");
            }

            return new List<RadarrMovie>();
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
        public string? Path { get; set; }
    }
}

