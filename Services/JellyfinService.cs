using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Optimarr.Services
{
    public class JellyfinService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<JellyfinService>? _logger;
        private bool _isEnabled;
        private bool _isConnected;
        private string? _accessToken;
        private string? _userId;

        public JellyfinService(IConfiguration configuration, ILogger<JellyfinService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = new HttpClient();
            Initialize();
        }

        private void Initialize()
        {
            var baseUrl = _configuration["Jellyfin:BaseUrl"];
            var apiKey = _configuration["Jellyfin:ApiKey"];
            var username = _configuration["Jellyfin:Username"];
            var password = _configuration["Jellyfin:Password"];
            
            _isEnabled = _configuration.GetValue<bool>("Jellyfin:Enabled") && 
                        !string.IsNullOrEmpty(baseUrl) && 
                        (!string.IsNullOrEmpty(apiKey) || (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password)));

            if (_isEnabled)
            {
                _httpClient.BaseAddress = new Uri(baseUrl!);
                _httpClient.DefaultRequestHeaders.Add("X-Emby-Authorization", 
                    $"MediaBrowser Client=\"optimarr\", Device=\"optimarr\", DeviceId=\"optimarr\", Version=\"1.0.0\"");
                
                if (!string.IsNullOrEmpty(apiKey))
                {
                    _httpClient.DefaultRequestHeaders.Add("X-Emby-Token", apiKey);
                    _accessToken = apiKey;
                }
                
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                CheckConnection();
            }
        }

        public bool IsEnabled => _isEnabled;
        public bool IsConnected => _isConnected;

        private async void CheckConnection()
        {
            if (!_isEnabled) return;

            try
            {
                var response = await _httpClient.GetAsync("/System/Info");
                if (response.IsSuccessStatusCode)
                {
                    _isConnected = true;
                    _logger?.LogInformation("Successfully connected to Jellyfin");
                    
                    // If using username/password, authenticate
                    if (string.IsNullOrEmpty(_accessToken))
                    {
                        await AuthenticateAsync();
                    }
                }
                else
                {
                    _isConnected = false;
                    _logger?.LogWarning("Failed to connect to Jellyfin: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger?.LogError(ex, "Error checking Jellyfin connection");
            }
        }

        private async Task AuthenticateAsync()
        {
            try
            {
                var username = _configuration["Jellyfin:Username"];
                var password = _configuration["Jellyfin:Password"];
                
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                    return;

                var authRequest = new
                {
                    Username = username,
                    Pw = password
                };

                var content = new StringContent(JsonSerializer.Serialize(authRequest), 
                    System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/Users/authenticatebyname", content);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var authResult = JsonSerializer.Deserialize<AuthResult>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (authResult != null && !string.IsNullOrEmpty(authResult.AccessToken))
                    {
                        _accessToken = authResult.AccessToken;
                        _userId = authResult.User?.Id;
                        _httpClient.DefaultRequestHeaders.Remove("X-Emby-Token");
                        _httpClient.DefaultRequestHeaders.Add("X-Emby-Token", _accessToken);
                        _logger?.LogInformation("Successfully authenticated with Jellyfin");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error authenticating with Jellyfin");
            }
        }

        public async Task<List<PlaybackSession>> GetPlaybackSessionsAsync()
        {
            if (!_isEnabled || !_isConnected) return new List<PlaybackSession>();

            try
            {
                var response = await _httpClient.GetAsync("/Sessions");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var sessions = JsonSerializer.Deserialize<List<PlaybackSession>>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    return sessions ?? new List<PlaybackSession>();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching playback sessions");
            }

            return new List<PlaybackSession>();
        }

        public async Task<List<PlaybackHistoryItem>> GetPlaybackHistoryAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!_isEnabled || !_isConnected) 
                return new List<PlaybackHistoryItem>();

            try
            {
                var history = new List<PlaybackHistoryItem>();
                
                // Get all users if we have admin access
                var usersResponse = await _httpClient.GetAsync("/Users");
                if (!usersResponse.IsSuccessStatusCode) return history;

                var usersJson = await usersResponse.Content.ReadAsStringAsync();
                var users = JsonSerializer.Deserialize<List<JellyfinUser>>(usersJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (users == null) return history;

                foreach (var user in users)
                {
                    // Get user's activity log
                    var url = $"/Users/{user.Id}/ActivityLog/Entries";
                    if (startDate.HasValue)
                    {
                        var startTicks = startDate.Value.Ticks;
                        url += $"?StartIndex=0&Limit=100";
                    }

                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var entries = JsonSerializer.Deserialize<ActivityLogResponse>(json, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });

                        if (entries?.Items != null)
                        {
                            foreach (var entry in entries.Items)
                            {
                                // Filter for playback-related activities
                                if (entry.Type == "PlaybackStart" || entry.Type == "PlaybackStop")
                                {
                                    // Get item details
                                    var item = await GetItemAsync(entry.ItemId ?? string.Empty);
                                    if (item != null)
                                    {
                                        history.Add(new PlaybackHistoryItem
                                        {
                                            ItemId = entry.ItemId,
                                            UserId = user.Id,
                                            UserName = user.Name,
                                            ItemName = item.Name,
                                            ItemType = item.Type,
                                            MediaType = item.MediaType,
                                            Path = item.Path,
                                            PlaybackStartTime = entry.Date,
                                            PlaybackStopTime = entry.Type == "PlaybackStop" ? entry.Date : null,
                                            ClientName = entry.Client,
                                            DeviceName = entry.DeviceName,
                                            PlayMethod = ParsePlayMethod(entry),
                                            IsDirectPlay = entry.PlayMethod == "DirectPlay",
                                            IsDirectStream = entry.PlayMethod == "DirectStream",
                                            IsTranscode = entry.PlayMethod == "Transcode",
                                            TranscodeReason = entry.TranscodingInfo?.Reason
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                return history;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching playback history");
            }

            return new List<PlaybackHistoryItem>();
        }

        private async Task<JellyfinItem?> GetItemAsync(string itemId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/Items/{itemId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<JellyfinItem>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching item {ItemId}", itemId);
            }

            return null;
        }

        private string ParsePlayMethod(ActivityLogEntry entry)
        {
            // Try to determine play method from entry
            if (entry.PlayMethod != null) return entry.PlayMethod;
            if (entry.TranscodingInfo != null) return "Transcode";
            return "Unknown";
        }

        // Helper classes for JSON deserialization
        private class AuthResult
        {
            [JsonPropertyName("AccessToken")]
            public string? AccessToken { get; set; }
            [JsonPropertyName("User")]
            public JellyfinUser? User { get; set; }
        }

        private class JellyfinUser
        {
            [JsonPropertyName("Id")]
            public string? Id { get; set; }
            [JsonPropertyName("Name")]
            public string? Name { get; set; }
        }

        private class ActivityLogResponse
        {
            [JsonPropertyName("Items")]
            public List<ActivityLogEntry>? Items { get; set; }
        }

        private class ActivityLogEntry
        {
            [JsonPropertyName("Id")]
            public string? Id { get; set; }
            [JsonPropertyName("ItemId")]
            public string? ItemId { get; set; }
            [JsonPropertyName("Date")]
            public DateTime Date { get; set; }
            [JsonPropertyName("Type")]
            public string? Type { get; set; }
            [JsonPropertyName("Client")]
            public string? Client { get; set; }
            [JsonPropertyName("DeviceName")]
            public string? DeviceName { get; set; }
            [JsonPropertyName("PlayMethod")]
            public string? PlayMethod { get; set; }
            [JsonPropertyName("TranscodingInfo")]
            public TranscodingInfo? TranscodingInfo { get; set; }
        }

        private class TranscodingInfo
        {
            [JsonPropertyName("Reason")]
            public string? Reason { get; set; }
        }

        private class JellyfinItem
        {
            [JsonPropertyName("Id")]
            public string? Id { get; set; }
            [JsonPropertyName("Name")]
            public string? Name { get; set; }
            [JsonPropertyName("Type")]
            public string? Type { get; set; }
            [JsonPropertyName("MediaType")]
            public string? MediaType { get; set; }
            [JsonPropertyName("Path")]
            public string? Path { get; set; }
        }
    }

    public class PlaybackSession
    {
        public string? Id { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Client { get; set; }
        public string? DeviceName { get; set; }
        public string? ItemId { get; set; }
        public string? ItemName { get; set; }
        public string? PlayState { get; set; }
        public bool IsPaused { get; set; }
        public bool IsMuted { get; set; }
        public string? PlayMethod { get; set; }
    }

    public class PlaybackHistoryItem
    {
        public string? ItemId { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? ItemName { get; set; }
        public string? ItemType { get; set; }
        public string? MediaType { get; set; }
        public string? Path { get; set; }
        public DateTime PlaybackStartTime { get; set; }
        public DateTime? PlaybackStopTime { get; set; }
        public string? ClientName { get; set; }
        public string? DeviceName { get; set; }
        public string? PlayMethod { get; set; }
        public bool IsDirectPlay { get; set; }
        public bool IsDirectStream { get; set; }
        public bool IsTranscode { get; set; }
        public string? TranscodeReason { get; set; }
    }
}

