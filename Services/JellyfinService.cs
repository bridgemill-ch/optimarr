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
                try
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
                    _ = CheckConnectionAsync(); // Fire and forget for initialization
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error initializing Jellyfin service");
                    _isEnabled = false;
                    _isConnected = false;
                }
            }
            else
            {
                _isConnected = false;
            }
        }

        public bool IsEnabled => _isEnabled;
        public bool IsConnected => _isConnected;

        public async Task ReconnectAsync()
        {
            // Clear existing connection state
            _isConnected = false;
            _accessToken = null;
            _userId = null;
            
            // Clear existing headers
            _httpClient.DefaultRequestHeaders.Clear();
            
            // Reinitialize with current configuration
            var baseUrl = _configuration["Jellyfin:BaseUrl"];
            var apiKey = _configuration["Jellyfin:ApiKey"];
            var username = _configuration["Jellyfin:Username"];
            var password = _configuration["Jellyfin:Password"];
            
            _isEnabled = _configuration.GetValue<bool>("Jellyfin:Enabled") && 
                        !string.IsNullOrEmpty(baseUrl) && 
                        (!string.IsNullOrEmpty(apiKey) || (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password)));

            if (_isEnabled)
            {
                try
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
                    
                    // Wait for connection check to complete
                    await CheckConnectionAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error reconnecting Jellyfin service");
                    _isEnabled = false;
                    _isConnected = false;
                }
            }
            else
            {
                _isConnected = false;
            }
        }

        private async Task CheckConnectionAsync()
        {
            if (!_isEnabled)
            {
                _isConnected = false;
                return;
            }

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

        public async IAsyncEnumerable<PlaybackHistoryItem> GetPlaybackHistoryStreamAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!_isEnabled || !_isConnected) 
            {
                _logger?.LogWarning("Jellyfin service not enabled or connected. Enabled: {Enabled}, Connected: {Connected}", _isEnabled, _isConnected);
                yield break;
            }

            _logger?.LogInformation("Fetching playback history from Jellyfin. StartDate: {StartDate}, EndDate: {EndDate}", startDate, endDate);
            
            // Get all users if we have admin access
            HttpResponseMessage? usersResponse = null;
            try
            {
                usersResponse = await _httpClient.GetAsync("/Users");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching users list");
                yield break;
            }
            
            if (usersResponse == null || !usersResponse.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Failed to get users list. Status: {StatusCode}", usersResponse?.StatusCode);
                yield break;
            }

            string usersJson;
            try
            {
                usersJson = await usersResponse.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading users response");
                yield break;
            }
            
            var users = JsonSerializer.Deserialize<List<JellyfinUser>>(usersJson, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (users == null || users.Count == 0)
            {
                _logger?.LogWarning("No users found in Jellyfin");
                yield break;
            }

            _logger?.LogInformation("Found {UserCount} users in Jellyfin", users.Count);

            // Try ActivityLog endpoint first, but if it fails for all users, use alternative method
            bool activityLogAvailable = false;
            bool hasYieldedItems = false;
            
            foreach (var user in users)
            {
                _logger?.LogInformation("Fetching activity log for user: {UserName} ({UserId})", user.Name, user.Id);
                
                // Get user's activity log with pagination to get ALL entries
                var startIndex = 0;
                var limit = 100;
                var hasMore = true;
                var totalEntriesForUser = 0;
                var playbackEntriesForUser = 0;

                while (hasMore)
                {
                    // Try different endpoint variations
                    var url = $"/Users/{user.Id}/ActivityLog/Entries?StartIndex={startIndex}&Limit={limit}";
                    _logger?.LogDebug("Fetching activity log: {Url}", url);
                    
                    HttpResponseMessage? response = null;
                    try
                    {
                        response = await _httpClient.GetAsync(url);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error fetching activity log for user {UserId}", user.Id);
                        break;
                    }
                    
                    if (response == null || !response.IsSuccessStatusCode)
                    {
                        // If 404, try alternative endpoint format
                        if (response?.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            _logger?.LogInformation("ActivityLog endpoint not found, trying alternative endpoint...");
                            
                            // Try without /Entries
                            var altUrl = $"/Users/{user.Id}/ActivityLog?StartIndex={startIndex}&Limit={limit}";
                            try
                            {
                                response = await _httpClient.GetAsync(altUrl);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(ex, "Error fetching alternative activity log endpoint");
                                break;
                            }
                            
                            if (response == null || !response.IsSuccessStatusCode)
                            {
                                _logger?.LogWarning("Alternative ActivityLog endpoint also failed. Status: {StatusCode}", response?.StatusCode);
                                activityLogAvailable = false;
                                break;
                            }
                            else
                            {
                                url = altUrl;
                                activityLogAvailable = true;
                            }
                        }
                        else
                        {
                            string? errorContent = null;
                            try
                            {
                                errorContent = response != null ? await response.Content.ReadAsStringAsync() : null;
                            }
                            catch { }
                            _logger?.LogWarning("Failed to get activity log for user {UserId}. Status: {StatusCode}, Error: {Error}", 
                                user.Id, response?.StatusCode, errorContent);
                            break;
                        }
                    }
                    else
                    {
                        activityLogAvailable = true;
                    }

                    string json;
                    try
                    {
                        json = await response!.Content.ReadAsStringAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error reading activity log response");
                        break;
                    }
                    
                    List<ActivityLogEntry>? entryList = null;
                    
                    // Try to deserialize as object with Items property first
                    var entries = JsonSerializer.Deserialize<ActivityLogResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (entries?.Items != null)
                    {
                        entryList = entries.Items;
                    }
                    else
                    {
                        // Try as direct array
                        entryList = JsonSerializer.Deserialize<List<ActivityLogEntry>>(json, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                    }

                    if (entryList == null || entryList.Count == 0)
                    {
                        _logger?.LogDebug("No more entries for user {UserId}", user.Id);
                        hasMore = false;
                        break;
                    }

                    totalEntriesForUser += entryList.Count;

                    foreach (var entry in entryList)
                    {
                        // Apply date filter if provided
                        if (startDate.HasValue && entry.Date < startDate.Value)
                        {
                            hasMore = false;
                            break;
                        }
                        if (endDate.HasValue && entry.Date > endDate.Value)
                        {
                            continue;
                        }

                        // Filter for playback-related activities
                        var isPlaybackEntry = entry.Type == "PlaybackStart" || 
                                              entry.Type == "PlaybackStop" ||
                                              entry.Type == "playbackstart" ||
                                              entry.Type == "playbackstop" ||
                                              entry.Type?.Contains("Playback", StringComparison.OrdinalIgnoreCase) == true;
                        
                        if (isPlaybackEntry)
                        {
                            playbackEntriesForUser++;
                            
                            if (string.IsNullOrEmpty(entry.ItemId))
                            {
                                _logger?.LogWarning("Playback entry has no ItemId. Type: {Type}, Date: {Date}", entry.Type, entry.Date);
                                continue;
                            }
                            
                            // Get item details
                            JellyfinItem? item = null;
                            try
                            {
                                item = await GetItemAsync(entry.ItemId);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(ex, "Error fetching item {ItemId}", entry.ItemId);
                            }
                            
                            if (item != null)
                            {
                                // Try to get path from item or media sources (episodes often have path in MediaSources)
                                string? itemPath = item.Path;
                                if (string.IsNullOrEmpty(itemPath) && item.MediaSources != null && item.MediaSources.Count > 0)
                                {
                                    itemPath = item.MediaSources[0].Path;
                                }
                                
                                if (string.IsNullOrEmpty(itemPath))
                                {
                                    _logger?.LogWarning("Item {ItemId} ({ItemName}) has no path. Skipping.", entry.ItemId, item.Name);
                                    continue;
                                }
                                
                                var historyItem = new PlaybackHistoryItem
                                {
                                    ItemId = entry.ItemId,
                                    UserId = user.Id,
                                    UserName = user.Name,
                                    ItemName = item.Name,
                                    ItemType = item.Type,
                                    MediaType = item.MediaType,
                                    Path = itemPath,
                                    PlaybackStartTime = entry.Date,
                                    PlaybackStopTime = entry.Type?.Contains("Stop", StringComparison.OrdinalIgnoreCase) == true ? entry.Date : null,
                                    ClientName = entry.Client,
                                    DeviceName = entry.DeviceName,
                                    PlayMethod = ParsePlayMethod(entry),
                                    IsDirectPlay = entry.PlayMethod == "DirectPlay",
                                    IsDirectStream = entry.PlayMethod == "DirectStream",
                                    IsTranscode = entry.PlayMethod == "Transcode",
                                    TranscodeReason = entry.TranscodingInfo?.Reason
                                };
                                
                                hasYieldedItems = true;
                                yield return historyItem;
                            }
                            else
                            {
                                _logger?.LogWarning("Could not fetch item details for ItemId: {ItemId}, Type: {Type}", 
                                    entry.ItemId, entry.Type);
                            }
                        }
                    }

                    // Check if we got fewer items than requested (end of data)
                    if (entryList.Count < limit)
                    {
                        hasMore = false;
                    }
                    else
                    {
                        startIndex += limit;
                    }
                }

                _logger?.LogInformation("User {UserName}: Total entries: {Total}, Playback entries: {Playback}", 
                    user.Name, totalEntriesForUser, playbackEntriesForUser);
            }

            // If ActivityLog is not available or no items found, try alternative method
            if (!activityLogAvailable || !hasYieldedItems)
            {
                _logger?.LogInformation("ActivityLog not available or no items found, trying alternative method...");
                await foreach (var item in GetPlaybackHistoryAlternativeStreamAsync(startDate, endDate))
                {
                    yield return item;
                }
            }
        }

        public async Task<int> GetUserCountAsync()
        {
            if (!_isEnabled || !_isConnected) 
            {
                return 0;
            }

            try
            {
                var usersResponse = await _httpClient.GetAsync("/Users");
                if (!usersResponse.IsSuccessStatusCode)
                {
                    return 0;
                }

                var usersJson = await usersResponse.Content.ReadAsStringAsync();
                var users = JsonSerializer.Deserialize<List<JellyfinUser>>(usersJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                return users?.Count ?? 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting user count");
                return 0;
            }
        }

        public async Task<List<PlaybackHistoryItem>> GetPlaybackHistoryAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            // Use streaming version and collect results
            var history = new List<PlaybackHistoryItem>();
            await foreach (var item in GetPlaybackHistoryStreamAsync(startDate, endDate))
            {
                history.Add(item);
            }
            return history;
        }

        // Old implementation kept for reference - now uses streaming version above
        private async Task<List<PlaybackHistoryItem>> GetPlaybackHistoryAsyncOld(DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!_isEnabled || !_isConnected) 
            {
                _logger?.LogWarning("Jellyfin service not enabled or connected. Enabled: {Enabled}, Connected: {Connected}", _isEnabled, _isConnected);
                return new List<PlaybackHistoryItem>();
            }

            try
            {
                _logger?.LogInformation("Fetching playback history from Jellyfin. StartDate: {StartDate}, EndDate: {EndDate}", startDate, endDate);
                var history = new List<PlaybackHistoryItem>();
                
                // Get all users if we have admin access
                var usersResponse = await _httpClient.GetAsync("/Users");
                if (!usersResponse.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("Failed to get users list. Status: {StatusCode}", usersResponse.StatusCode);
                    return history;
                }

                var usersJson = await usersResponse.Content.ReadAsStringAsync();
                var users = JsonSerializer.Deserialize<List<JellyfinUser>>(usersJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (users == null || users.Count == 0)
                {
                    _logger?.LogWarning("No users found in Jellyfin");
                    return history;
                }

                _logger?.LogInformation("Found {UserCount} users in Jellyfin", users.Count);

                // Try ActivityLog endpoint first, but if it fails for all users, use alternative method
                bool activityLogAvailable = false;
                
                foreach (var user in users)
                {
                    _logger?.LogInformation("Fetching activity log for user: {UserName} ({UserId})", user.Name, user.Id);
                    
                    // Get user's activity log with pagination to get ALL entries
                    var startIndex = 0;
                    var limit = 100;
                    var hasMore = true;
                    var totalEntriesForUser = 0;
                    var playbackEntriesForUser = 0;

                    while (hasMore)
                    {
                        // Try different endpoint variations
                        var url = $"/Users/{user.Id}/ActivityLog/Entries?StartIndex={startIndex}&Limit={limit}";
                        _logger?.LogDebug("Fetching activity log: {Url}", url);
                        
                        var response = await _httpClient.GetAsync(url);
                        if (!response.IsSuccessStatusCode)
                        {
                            // If 404, try alternative endpoint format
                            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                _logger?.LogInformation("ActivityLog endpoint not found, trying alternative endpoint...");
                                
                                // Try without /Entries
                                var altUrl = $"/Users/{user.Id}/ActivityLog?StartIndex={startIndex}&Limit={limit}";
                                response = await _httpClient.GetAsync(altUrl);
                                
                                if (!response.IsSuccessStatusCode)
                                {
                                    _logger?.LogWarning("Alternative ActivityLog endpoint also failed. Status: {StatusCode}", response.StatusCode);
                                    activityLogAvailable = false;
                                    break;
                                }
                                else
                                {
                                    url = altUrl;
                                    activityLogAvailable = true;
                                }
                            }
                            else
                            {
                                var errorContent = await response.Content.ReadAsStringAsync();
                                _logger?.LogWarning("Failed to get activity log for user {UserId}. Status: {StatusCode}, Error: {Error}", 
                                    user.Id, response.StatusCode, errorContent);
                                break;
                            }
                        }
                        else
                        {
                            activityLogAvailable = true;
                        }

                        var json = await response.Content.ReadAsStringAsync();
                        
                        // Log first page to debug structure
                        if (startIndex == 0)
                        {
                            _logger?.LogInformation("Activity log response (first 1000 chars): {Response}", 
                                json.Length > 1000 ? json.Substring(0, 1000) : json);
                        }
                        
                        List<ActivityLogEntry>? entryList = null;
                        
                        // Try to deserialize as object with Items property first
                        var entries = JsonSerializer.Deserialize<ActivityLogResponse>(json, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                        
                        if (entries?.Items != null)
                        {
                            entryList = entries.Items;
                        }
                        else
                        {
                            // Try as direct array
                            entryList = JsonSerializer.Deserialize<List<ActivityLogEntry>>(json, new JsonSerializerOptions 
                            { 
                                PropertyNameCaseInsensitive = true 
                            });
                        }

                        if (entryList == null || entryList.Count == 0)
                        {
                            _logger?.LogDebug("No more entries for user {UserId}", user.Id);
                            hasMore = false;
                            break;
                        }
                        
                        // Log sample entry structure
                        if (startIndex == 0 && entryList.Count > 0)
                        {
                            var sampleEntry = entryList[0];
                            _logger?.LogInformation("Sample activity log entry - Type: {Type}, Date: {Date}, ItemId: {ItemId}, Client: {Client}, HasItemId: {HasItemId}", 
                                sampleEntry.Type, sampleEntry.Date, sampleEntry.ItemId, sampleEntry.Client, !string.IsNullOrEmpty(sampleEntry.ItemId));
                            
                            // Log all entry types found
                            var entryTypes = entryList.Select(e => e.Type).Distinct().ToList();
                            _logger?.LogInformation("Found entry types: {Types}", string.Join(", ", entryTypes));
                        }

                        totalEntriesForUser += entryList.Count;
                        _logger?.LogDebug("Retrieved {Count} activity log entries for user {UserId} (page {Page})", 
                            entryList.Count, user.Id, startIndex / limit + 1);

                        foreach (var entry in entryList)
                        {
                            // Apply date filter if provided
                            if (startDate.HasValue && entry.Date < startDate.Value)
                            {
                                hasMore = false;
                                break;
                            }
                            if (endDate.HasValue && entry.Date > endDate.Value)
                            {
                                continue;
                            }

                            // Filter for playback-related activities - try multiple type names
                            var isPlaybackEntry = entry.Type == "PlaybackStart" || 
                                                  entry.Type == "PlaybackStop" ||
                                                  entry.Type == "playbackstart" ||
                                                  entry.Type == "playbackstop" ||
                                                  entry.Type?.Contains("Playback", StringComparison.OrdinalIgnoreCase) == true;
                            
                            if (isPlaybackEntry)
                            {
                                playbackEntriesForUser++;
                                
                                if (string.IsNullOrEmpty(entry.ItemId))
                                {
                                    _logger?.LogWarning("Playback entry has no ItemId. Type: {Type}, Date: {Date}", entry.Type, entry.Date);
                                    continue;
                                }
                                
                                // Get item details
                                var item = await GetItemAsync(entry.ItemId);
                                if (item != null)
                                {
                                    // Try to get path from item or media sources (episodes often have path in MediaSources)
                                    string? itemPath = item.Path;
                                    if (string.IsNullOrEmpty(itemPath) && item.MediaSources != null && item.MediaSources.Count > 0)
                                    {
                                        itemPath = item.MediaSources[0].Path;
                                    }
                                    
                                    if (string.IsNullOrEmpty(itemPath))
                                    {
                                        _logger?.LogWarning("Item {ItemId} ({ItemName}) has no path. Skipping.", entry.ItemId, item.Name);
                                        continue;
                                    }
                                    
                                    history.Add(new PlaybackHistoryItem
                                    {
                                        ItemId = entry.ItemId,
                                        UserId = user.Id,
                                        UserName = user.Name,
                                        ItemName = item.Name,
                                        ItemType = item.Type,
                                        MediaType = item.MediaType,
                                        Path = itemPath,
                                        PlaybackStartTime = entry.Date,
                                        PlaybackStopTime = entry.Type?.Contains("Stop", StringComparison.OrdinalIgnoreCase) == true ? entry.Date : null,
                                        ClientName = entry.Client,
                                        DeviceName = entry.DeviceName,
                                        PlayMethod = ParsePlayMethod(entry),
                                        IsDirectPlay = entry.PlayMethod == "DirectPlay",
                                        IsDirectStream = entry.PlayMethod == "DirectStream",
                                        IsTranscode = entry.PlayMethod == "Transcode",
                                        TranscodeReason = entry.TranscodingInfo?.Reason
                                    });
                                }
                                else
                                {
                                    _logger?.LogWarning("Could not fetch item details for ItemId: {ItemId}, Type: {Type}", 
                                        entry.ItemId, entry.Type);
                                }
                            }
                        }

                        // Check if we got fewer items than requested (end of data)
                        if (entryList.Count < limit)
                        {
                            hasMore = false;
                        }
                        else
                        {
                            startIndex += limit;
                        }
                    }

                    _logger?.LogInformation("User {UserName}: Total entries: {Total}, Playback entries: {Playback}, Added to history: {History}", 
                        user.Name, totalEntriesForUser, playbackEntriesForUser, history.Count);
                }

                _logger?.LogInformation("Total playback history items retrieved: {Count}", history.Count);
                
                // If ActivityLog is not available or no items found, try alternative method
                if (!activityLogAvailable || history.Count == 0)
                {
                    _logger?.LogInformation("ActivityLog not available or no items found, trying alternative method...");
                    var alternativeHistory = await GetPlaybackHistoryAlternativeAsync(startDate, endDate);
                    if (alternativeHistory.Count > 0)
                    {
                        _logger?.LogInformation("Found {Count} items via alternative method", alternativeHistory.Count);
                        return alternativeHistory;
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

        private async IAsyncEnumerable<PlaybackHistoryItem> GetPlaybackHistoryAlternativeStreamAsync(DateTime? startDate, DateTime? endDate)
        {
            // Get all users to fetch their recently played items
            HttpResponseMessage? usersResponse = null;
            try
            {
                usersResponse = await _httpClient.GetAsync("/Users");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching users for alternative method");
                yield break;
            }
            
            if (usersResponse == null || !usersResponse.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Failed to get users for alternative method. Status: {StatusCode}", usersResponse?.StatusCode);
                yield break;
            }

            string usersJson;
            try
            {
                usersJson = await usersResponse.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading users response for alternative method");
                yield break;
            }
            
            var users = JsonSerializer.Deserialize<List<JellyfinUser>>(usersJson, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (users == null || users.Count == 0)
            {
                _logger?.LogWarning("No users found for alternative method");
                yield break;
            }

            _logger?.LogInformation("Using alternative method: fetching recently played items for {UserCount} users", users.Count);

            foreach (var user in users)
            {
                // Get user's recently played items
                var url = $"/Users/{user.Id}/Items?Recursive=true&IncludeItemTypes=Movie,Episode&SortBy=DatePlayed&SortOrder=Descending&Limit=1000&Fields=Path,MediaSources,UserData";
                if (startDate.HasValue)
                {
                    var startDateStr = startDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    url += $"&MinDate={startDateStr}";
                }
                
                _logger?.LogInformation("Fetching recently played items for user {UserName}: {Url}", user.Name, url);
                
                HttpResponseMessage? response = null;
                try
                {
                    response = await _httpClient.GetAsync(url);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error fetching items for user {UserName}", user.Name);
                    continue;
                }
                
                if (response != null && response.IsSuccessStatusCode)
                {
                    string json;
                    try
                    {
                        json = await response.Content.ReadAsStringAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error reading items response for user {UserName}", user.Name);
                        continue;
                    }
                    
                    var itemsResponse = JsonSerializer.Deserialize<ItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (itemsResponse?.Items != null)
                    {
                        _logger?.LogInformation("Found {Count} items for user {UserName}", itemsResponse.Items.Count, user.Name);
                        
                        foreach (var item in itemsResponse.Items)
                        {
                            if (item.UserData != null && item.UserData.LastPlayedDate.HasValue)
                            {
                                var playedDate = item.UserData.LastPlayedDate.Value;
                                
                                // Apply date filters
                                if (startDate.HasValue && playedDate < startDate.Value) continue;
                                if (endDate.HasValue && playedDate > endDate.Value) continue;
                                
                                // Try to get path from item or media sources
                                string? itemPath = item.Path;
                                if (string.IsNullOrEmpty(itemPath) && item.MediaSources != null && item.MediaSources.Count > 0)
                                {
                                    itemPath = item.MediaSources[0].Path;
                                }
                                
                                if (!string.IsNullOrEmpty(itemPath))
                                {
                                    yield return new PlaybackHistoryItem
                                    {
                                        ItemId = item.Id,
                                        UserId = user.Id,
                                        UserName = user.Name,
                                        ItemName = item.Name,
                                        ItemType = item.Type,
                                        MediaType = item.MediaType,
                                        Path = itemPath,
                                        PlaybackStartTime = playedDate,
                                        PlaybackStopTime = null,
                                        ClientName = null,
                                        DeviceName = null,
                                        PlayMethod = "Unknown",
                                        IsDirectPlay = false,
                                        IsDirectStream = false,
                                        IsTranscode = false,
                                        TranscodeReason = null
                                    };
                                }
                            }
                        }
                    }
                }
                else
                {
                    string? errorContent = null;
                    try
                    {
                        errorContent = response != null ? await response.Content.ReadAsStringAsync() : null;
                    }
                    catch { }
                    _logger?.LogWarning("Failed to get items for user {UserName}. Status: {StatusCode}, Error: {Error}", 
                        user.Name, response?.StatusCode, errorContent);
                }
            }
        }

        private async Task<List<PlaybackHistoryItem>> GetPlaybackHistoryAlternativeAsync(DateTime? startDate, DateTime? endDate)
        {
            var history = new List<PlaybackHistoryItem>();
            
            try
            {
                // Get all users to fetch their recently played items
                var usersResponse = await _httpClient.GetAsync("/Users");
                if (!usersResponse.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("Failed to get users for alternative method. Status: {StatusCode}", usersResponse.StatusCode);
                    return history;
                }

                var usersJson = await usersResponse.Content.ReadAsStringAsync();
                var users = JsonSerializer.Deserialize<List<JellyfinUser>>(usersJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (users == null || users.Count == 0)
                {
                    _logger?.LogWarning("No users found for alternative method");
                    return history;
                }

                _logger?.LogInformation("Using alternative method: fetching recently played items for {UserCount} users", users.Count);

                foreach (var user in users)
                {
                    try
                    {
                        // Get user's recently played items
                        var url = $"/Users/{user.Id}/Items?Recursive=true&IncludeItemTypes=Movie,Episode&SortBy=DatePlayed&SortOrder=Descending&Limit=1000&Fields=Path,MediaSources";
                        if (startDate.HasValue)
                        {
                            var startDateStr = startDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                            url += $"&MinDate={startDateStr}";
                        }
                        
                        _logger?.LogInformation("Fetching recently played items for user {UserName}: {Url}", user.Name, url);
                        
                        var response = await _httpClient.GetAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            var itemsResponse = JsonSerializer.Deserialize<ItemsResponse>(json, new JsonSerializerOptions 
                            { 
                                PropertyNameCaseInsensitive = true 
                            });
                            
                            if (itemsResponse?.Items != null)
                            {
                                _logger?.LogInformation("Found {Count} items for user {UserName}", itemsResponse.Items.Count, user.Name);
                                
                                foreach (var item in itemsResponse.Items)
                                {
                                    if (item.UserData != null && item.UserData.LastPlayedDate.HasValue)
                                    {
                                        var playedDate = item.UserData.LastPlayedDate.Value;
                                        
                                        // Apply date filters
                                        if (startDate.HasValue && playedDate < startDate.Value) continue;
                                        if (endDate.HasValue && playedDate > endDate.Value) continue;
                                        
                                        // Try to get path from item or media sources
                                        string? itemPath = item.Path;
                                        if (string.IsNullOrEmpty(itemPath) && item.MediaSources != null && item.MediaSources.Count > 0)
                                        {
                                            itemPath = item.MediaSources[0].Path;
                                        }
                                        
                                        if (!string.IsNullOrEmpty(itemPath))
                                        {
                                            history.Add(new PlaybackHistoryItem
                                            {
                                                ItemId = item.Id,
                                                UserId = user.Id,
                                                UserName = user.Name,
                                                ItemName = item.Name,
                                                ItemType = item.Type,
                                                MediaType = item.MediaType,
                                                Path = itemPath,
                                                PlaybackStartTime = playedDate,
                                                PlaybackStopTime = null,
                                                ClientName = null,
                                                DeviceName = null,
                                                PlayMethod = "Unknown",
                                                IsDirectPlay = false,
                                                IsDirectStream = false,
                                                IsTranscode = false,
                                                TranscodeReason = null
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            _logger?.LogWarning("Failed to get items for user {UserName}. Status: {StatusCode}, Error: {Error}", 
                                user.Name, response.StatusCode, errorContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error fetching items for user {UserName}", user.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in alternative playback history method");
            }
            
            return history;
        }

        private async Task<JellyfinItem?> GetItemAsync(string itemId)
        {
            try
            {
                // Request MediaSources field to get paths for episodes
                var response = await _httpClient.GetAsync($"/Items/{itemId}?Fields=Path,MediaSources");
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
            [JsonPropertyName("UserData")]
            public JellyfinUserData? UserData { get; set; }
            [JsonPropertyName("MediaSources")]
            public List<MediaSource>? MediaSources { get; set; }
        }

        private class MediaSource
        {
            [JsonPropertyName("Path")]
            public string? Path { get; set; }
        }

        private class JellyfinUserData
        {
            [JsonPropertyName("LastPlayedDate")]
            public DateTime? LastPlayedDate { get; set; }
        }

        private class ItemsResponse
        {
            [JsonPropertyName("Items")]
            public List<JellyfinItem>? Items { get; set; }
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

