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

        public SonarrService(IConfiguration configuration, ILogger<SonarrService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = new HttpClient();
            Initialize();
        }

        private void Initialize()
        {
            var baseUrl = _configuration["Servarr:Sonarr:BaseUrl"];
            var apiKey = _configuration["Servarr:Sonarr:ApiKey"];
            _isEnabled = _configuration.GetValue<bool>("Servarr:Sonarr:Enabled") && !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey);

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
                _logger?.LogError(ex, "Error getting Sonarr version");
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

        public async Task<List<SonarrEpisode>> GetEpisodesBySeries(int seriesId)
        {
            if (!_isEnabled || !_isConnected)
                return new List<SonarrEpisode>();

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
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr episodes");
            }

            return new List<SonarrEpisode>();
        }

        public async Task<List<SonarrSeries>> GetSeries()
        {
            if (!_isEnabled || !_isConnected)
                return new List<SonarrSeries>();

            try
            {
                var response = await _httpClient.GetAsync("/api/v3/series");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var series = JsonSerializer.Deserialize<List<SonarrSeries>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return series ?? new List<SonarrSeries>();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting Sonarr series");
            }

            return new List<SonarrSeries>();
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
    }
}

