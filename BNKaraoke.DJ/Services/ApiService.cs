// ApiService.cs
using BNKaraoke.DJ.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace BNKaraoke.DJ.Services
{
    public class ApiService : IApiService
    {
        private readonly HttpClient _httpClient;
        private readonly IUserSessionService _userSessionService;
        private readonly SettingsService _settingsService;

        public ApiService(IUserSessionService userSessionService, SettingsService userSettingsService)
        {
            _userSessionService = userSessionService;
            _settingsService = userSettingsService;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_settingsService.Settings.ApiUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
            ConfigureAuthorizationHeader();
        }

        private void ConfigureAuthorizationHeader()
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            if (!string.IsNullOrEmpty(_userSessionService.Token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userSessionService.Token);
                Log.Information("[APISERVICE] Authorization header set with Bearer token");
            }
            else
            {
                Log.Warning("[APISERVICE] No token available for Authorization header");
            }
        }

        public async Task<List<EventDto>> GetLiveEventsAsync(CancellationToken cancellationToken = default)
        {
            if (!_userSessionService.IsAuthenticated)
            {
                Log.Information("[APISERVICE] Skipping GetLiveEventsAsync: User not authenticated");
                return new List<EventDto>();
            }

            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Attempting to fetch live events");
                var response = await _httpClient.GetAsync("/api/events?status=active", cancellationToken);
                response.EnsureSuccessStatusCode();
                var events = await response.Content.ReadFromJsonAsync<List<EventDto>>(cancellationToken);
                Log.Information("[APISERVICE] Fetched {Count} live events", events?.Count ?? 0);
                return events ?? new List<EventDto>();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Log.Error("[APISERVICE] Unauthorized access when fetching live events: {Message}", ex.Message);
                throw new UnauthorizedAccessException("Authentication failed. Please re-login.", ex);
            }
            catch (HttpRequestException ex)
            {
                Log.Error("[APISERVICE] Failed to fetch live events: Status={StatusCode}, Message={Message}, InnerException={InnerException}", ex.StatusCode, ex.Message, ex.InnerException?.Message);
                return new List<EventDto>();
            }
            catch (TaskCanceledException ex)
            {
                Log.Error("[APISERVICE] Fetch live events timed out or was canceled: {Message}", ex.Message);
                return new List<EventDto>();
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch live events: {Message}, InnerException={InnerException}", ex.Message, ex.InnerException?.Message);
                return new List<EventDto>();
            }
        }

        public async Task JoinEventAsync(string eventId, string requestorUserName)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { RequestorUserName = requestorUserName };
                Log.Information("[APISERVICE] Sending join event request for EventId={EventId}, RequestorUserName={RequestorUserName}, Payload={Payload}", eventId, requestorUserName, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync($"/api/dj/{eventId}/attendance/check-in", request);
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (errorContent.Contains("already checked in", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("[APISERVICE] User {RequestorUserName} is already checked in for EventId={EventId}", requestorUserName, eventId);
                        return;
                    }
                    Log.Error("[APISERVICE] Failed to join event {EventId}: Status={StatusCode}, Error={Error}", eventId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to join event: {response.StatusCode} - {errorContent}");
                }
                response.EnsureSuccessStatusCode();
                Log.Information("[APISERVICE] Successfully joined event: EventId={EventId}", eventId);
            }
            catch (HttpRequestException ex)
            {
                Log.Error("[APISERVICE] Failed to join event {EventId}: Status={StatusCode}, Message={Message}", eventId, ex.StatusCode, ex.Message);
                throw;
            }
        }

        public async Task LeaveEventAsync(string eventId, string requestorUserName)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { RequestorUserName = requestorUserName };
                Log.Information("[APISERVICE] Sending leave event request for EventId={EventId}, RequestorUserName={RequestorUserName}, Payload={Payload}", eventId, requestorUserName, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync($"/api/dj/{eventId}/attendance/check-out", request);
                response.EnsureSuccessStatusCode();
                Log.Information("[APISERVICE] Successfully left event: EventId={EventId}", eventId);
            }
            catch (HttpRequestException ex)
            {
                Log.Error("[APISERVICE] Failed to leave event {EventId}: Status={StatusCode}, Message={Message}", eventId, ex.StatusCode, ex.Message);
                throw;
            }
        }

        public async Task<string> GetDiagnosticAsync()
        {
            try
            {
                ConfigureAuthorizationHeader();
                var response = await _httpClient.GetAsync("/api/diagnostic/test");
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync();
                Log.Information("[APISERVICE] Fetched diagnostic data");
                return result;
            }
            catch (HttpRequestException ex)
            {
                Log.Error("[APISERVICE] Failed to fetch diagnostic data: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<LoginResult> LoginAsync(string userName, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userName))
                {
                    Log.Error("[APISERVICE] Login attempt with empty UserName");
                    throw new ArgumentException("UserName cannot be empty", nameof(userName));
                }
                if (string.IsNullOrWhiteSpace(password))
                {
                    Log.Error("[APISERVICE] Login attempt with empty Password");
                    throw new ArgumentException("Password cannot be empty", nameof(password));
                }

                var request = new { UserName = userName, Password = password };
                Log.Information("[APISERVICE] Sending login request for UserName={UserName}", userName);
                var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Login failed for UserName={UserName}: Status={StatusCode}, Error={Error}", userName, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Login failed: {response.StatusCode} - {errorContent}");
                }
                var result = await response.Content.ReadFromJsonAsync<LoginResult>();
                Log.Information("[APISERVICE] Login successful for UserName={UserName}", userName);
                return result ?? throw new InvalidOperationException("Login response is null");
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Login failed for UserName={UserName}: {Message}", userName ?? "null", ex.Message);
                throw;
            }
        }

        public async Task<List<Singer>> GetSingersAsync(string eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Fetching singers for EventId={EventId}", eventId);
                var response = await _httpClient.GetAsync($"/api/dj/events/{eventId}/singers");
                var rawJson = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("[APISERVICE] Failed to fetch singers for event {EventId}: Status={StatusCode}, Response={Response}", eventId, response.StatusCode, rawJson);
                    return new List<Singer>();
                }
                var singersResponse = JsonSerializer.Deserialize<SingerResponse>(rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Log.Information("[APISERVICE] Fetched {Count} singers for EventId={EventId}, Response={Response}", singersResponse?.Singers?.Count ?? 0, eventId, rawJson);
                return singersResponse?.Singers ?? new List<Singer>();
            }
            catch (JsonException ex)
            {
                Log.Error("[APISERVICE] Failed to deserialize singers for EventId={EventId}: {Message}", eventId, ex.Message);
                return new List<Singer>();
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch singers for EventId={EventId}: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<List<QueueEntry>> GetQueueAsync(string eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Fetching queue for EventId={EventId}", eventId);
                var response = await _httpClient.GetAsync($"/api/dj/queue/unplayed?eventId={eventId}");
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to fetch queue for event {EventId}: Status={StatusCode}, Message={Message}", eventId, response.StatusCode, errorContent);
                    return new List<QueueEntry>();
                }
                var queueResponse = await response.Content.ReadFromJsonAsync<List<QueueEntry>>();
                Log.Information("[APISERVICE] Fetched {Count} queue entries for EventId={EventId}", queueResponse?.Count ?? 0, eventId);
                return queueResponse ?? new List<QueueEntry>();
            }
            catch (JsonException ex)
            {
                Log.Error("[APISERVICE] Failed to deserialize queue for EventId={EventId}: {Message}", eventId, ex.Message);
                return new List<QueueEntry>();
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch queue for EventId={EventId}: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<List<QueueEntry>> GetLiveQueueAsync(string eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Fetching live queue for EventId={EventId}", eventId);
                var response = await _httpClient.GetAsync($"/api/dj/queue/now-playing?eventId={eventId}");
                response.EnsureSuccessStatusCode();
                var queue = await response.Content.ReadFromJsonAsync<List<QueueEntry>>();
                Log.Information("[APISERVICE] Fetched {Count} live queue entries for EventId={EventId}", queue?.Count ?? 0, eventId);
                return queue ?? new List<QueueEntry>();
            }
            catch (JsonException ex)
            {
                Log.Error("[APISERVICE] Failed to deserialize live queue for EventId={EventId}: {Message}", ex.Message);
                return new List<QueueEntry>();
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch live queue for EventId={EventId}: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<List<QueueEntry>> GetSungQueueAsync(string eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Fetching sung queue for EventId={EventId}", eventId);
                var response = await _httpClient.GetAsync($"/api/dj/queue/completed?eventId={eventId}");
                response.EnsureSuccessStatusCode();
                var queue = await response.Content.ReadFromJsonAsync<List<QueueEntry>>();
                Log.Information("[APISERVICE] Fetched {Count} sung queue entries for EventId={EventId}", queue?.Count ?? 0, eventId);
                return queue ?? new List<QueueEntry>();
            }
            catch (JsonException ex)
            {
                Log.Error("[APISERVICE] Failed to deserialize sung queue for EventId={EventId}: {Message}", ex.Message);
                return new List<QueueEntry>();
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch sung queue for EventId={EventId}: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<int> GetSungCountAsync(string eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Fetching sung count for EventId={EventId}", eventId);
                var response = await _httpClient.GetAsync($"/api/dj/queue/completed?eventId={eventId}");
                response.EnsureSuccessStatusCode();
                var queue = await response.Content.ReadFromJsonAsync<List<QueueEntry>>();
                var count = queue?.Count ?? 0;
                Log.Information("[APISERVICE] Fetched sung count {Count} for EventId={EventId}", count, eventId);
                return count;
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch sung count for EventId={EventId}: {Message}", ex.Message);
                throw;
            }
        }

        public async Task ReorderQueueAsync(string eventId, List<string> queueIds)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var intQueueIds = queueIds.Select(id => int.Parse(id)).ToList();
                var jsonPayload = JsonSerializer.Serialize(intQueueIds);
                Log.Information("[APISERVICE] Reordering queue for EventId={EventId}, QueueIds={QueueIds}, Payload={Payload}", eventId, string.Join(",", queueIds), jsonPayload);
                var response = await _httpClient.PutAsJsonAsync($"/api/dj/{eventId}/queue/reorder", intQueueIds);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to reorder queue for EventId={EventId}: Status={StatusCode}, Error={ErrorContent}", eventId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to reorder queue: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully reordered queue for EventId={EventId}", eventId);
            }
            catch (FormatException ex)
            {
                Log.Error("[APISERVICE] Failed to parse queue IDs for EventId={EventId}: {Message}, QueueIds={QueueIds}", eventId, ex.Message, string.Join(",", queueIds));
                throw new ArgumentException("Invalid queue ID format", nameof(queueIds), ex);
            }
            catch (HttpRequestException ex)
            {
                Log.Error("[APISERVICE] Failed to reorder queue for EventId={EventId}: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to reorder queue for EventId={EventId}: {Message}, InnerException={InnerException}", ex.Message, ex.InnerException?.Message);
                throw;
            }
        }

        public async Task PlayAsync(string eventId, string queueId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { QueueId = int.Parse(queueId) };
                Log.Information("[APISERVICE] Sending now playing request for EventId={EventId}, QueueId={QueueId}, Payload={Payload}", eventId, queueId, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync("/api/dj/queue/now-playing", request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to set now playing for EventId={EventId}, QueueId={QueueId}: Status={StatusCode}, Error={Error}", eventId, queueId, response.StatusCode, errorContent);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new HttpRequestException($"Queue entry or event not found: {errorContent}");
                    }
                    throw new HttpRequestException($"Failed to set now playing: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully set now playing for EventId={EventId}, QueueId={QueueId}", eventId, queueId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to set now playing for EventId={EventId}, QueueId={QueueId}: {Message}", eventId, queueId, ex.Message);
                throw;
            }
        }

        public async Task PauseAsync(string eventId, string queueId)
        {
            Log.Information("[APISERVICE] PauseAsync called for EventId={EventId}, QueueId={QueueId}. Handled locally, no API call.", eventId, queueId);
            await Task.CompletedTask;
        }

        public async Task StopAsync(string eventId, string queueId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { EventId = int.Parse(eventId), QueueId = int.Parse(queueId) };
                Log.Information("[APISERVICE] Sending stop/complete request for EventId={EventId}, QueueId={QueueId}, Payload={Payload}", eventId, queueId, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync("/api/dj/complete", request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to stop/complete for EventId={EventId}, QueueId={QueueId}: Status={StatusCode}, Error={Error}", eventId, queueId, response.StatusCode, errorContent);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new HttpRequestException($"Event or queue entry not found: {errorContent}");
                    }
                    throw new HttpRequestException($"Failed to stop: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully stopped/completed for EventId={EventId}, QueueId={QueueId}", eventId, queueId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to stop/complete for EventId={EventId}, QueueId={QueueId}: {Message}", eventId, queueId, ex.Message);
                throw;
            }
        }

        public async Task SkipAsync(string eventId, string queueId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Sending skip request for EventId={EventId}, QueueId={QueueId}", eventId, queueId);
                var response = await _httpClient.PostAsync($"/api/dj/{eventId}/queue/{queueId}/skipped", null);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to skip queue {QueueId} for EventId={EventId}: Status={StatusCode}, Error={Error}", queueId, eventId, response.StatusCode, errorContent);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new HttpRequestException($"Event or queue entry not found: {errorContent}");
                    }
                    throw new HttpRequestException($"Failed to skip: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully skipped queue {QueueId} for EventId={EventId}", queueId, eventId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to skip queue {QueueId} for EventId={EventId}: {Message}", queueId, eventId, ex.Message);
                throw;
            }
        }

        public async Task LaunchVideoAsync(string eventId, string queueId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { QueueId = int.Parse(queueId) };
                Log.Information("[APISERVICE] Sending launch video request for EventId={EventId}, QueueId={QueueId}, Payload={Payload}", eventId, queueId, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync("/api/dj/queue/now-playing", request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to launch video for EventId={EventId}, QueueId={QueueId}: Status={StatusCode}, Error={Error}", eventId, queueId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to launch video: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully launched video for EventId={EventId}, QueueId={QueueId}", eventId, queueId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to launch video for EventId={EventId}, QueueId={QueueId}: {Message}", eventId, queueId, ex.Message);
                throw;
            }
        }

        public async Task CompleteSongAsync(string eventId, int queueId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { EventId = int.Parse(eventId), QueueId = queueId };
                Log.Information("[APISERVICE] Sending complete song request for EventId={EventId}, QueueId={QueueId}, Payload={Payload}", eventId, queueId, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync("/api/dj/complete", request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to complete song for QueueId={QueueId} for EventId={EventId}: Status={StatusCode}, Error={Error}", queueId, eventId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to complete song: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully completed song for QueueId={QueueId} for EventId={EventId}", queueId, eventId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to complete song for QueueId={QueueId} for EventId={EventId}: {Message}", queueId, eventId, ex.Message);
                throw;
            }
        }

        public async Task ToggleBreakAsync(string eventId, int queueId, bool isOnBreak)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { EventId = int.Parse(eventId), QueueId = queueId, IsOnBreak = isOnBreak };
                Log.Information("[APISERVICE] Sending toggle break request for EventId={EventId}, QueueId={QueueId}, IsOnBreak={IsOnBreak}, Payload={Payload}", eventId, queueId, isOnBreak, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync($"/api/dj/break", request);
                response.EnsureSuccessStatusCode();
                Log.Information("[APISERVICE] Successfully toggled break for QueueId={QueueId} for EventId={EventId}", queueId, eventId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to toggle break for QueueId={QueueId} for EventId={EventId}: {Message}", queueId, eventId, ex.Message);
                throw;
            }
        }

        public async Task UpdateSingerStatusAsync(string eventId, string requestorUserName, bool isLoggedIn, bool isJoined, bool isOnBreak)
        {
            try
            {
                if (string.IsNullOrEmpty(eventId))
                {
                    Log.Error("[APISERVICE] UpdateSingerStatusAsync: EventId is null or empty");
                    throw new ArgumentException("EventId cannot be null or empty", nameof(eventId));
                }

                ConfigureAuthorizationHeader();
                var request = new { EventId = int.Parse(eventId), RequestorUserName = requestorUserName, IsLoggedIn = isLoggedIn, IsJoined = isJoined, IsOnBreak = isOnBreak };
                Log.Information("[APISERVICE] Sending update singer status request for EventId={EventId}, RequestorUserName={RequestorUserName}, Payload={Payload}", eventId, requestorUserName, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync("/api/dj/singer/update", request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to update singer status for EventId={EventId}, RequestorUserName={RequestorUserName}: Status={StatusCode}, Error={Error}", eventId, requestorUserName, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to update singer status: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully updated singer status for EventId={EventId}, RequestorUserName={RequestorUserName}", eventId, requestorUserName);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to update singer status for EventId={EventId}, RequestorUserName={RequestorUserName}: {Message}", eventId, requestorUserName, ex.Message);
                throw;
            }
        }

        public async Task AddSongAsync(string eventId, int songId, string requestorUserName, string[] singers)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new { SongId = songId, RequestorUserName = requestorUserName, Singers = singers };
                Log.Information("[APISERVICE] Sending add song request for EventId={EventId}, SongId={SongId}, RequestorUserName={RequestorUserName}, Payload={Payload}", eventId, songId, requestorUserName, JsonSerializer.Serialize(request));
                var response = await _httpClient.PostAsJsonAsync($"/api/dj/{eventId}/song", request);
                response.EnsureSuccessStatusCode();
                Log.Information("[APISERVICE] Adding song for EventId={EventId}, SongId={SongId}", eventId, songId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to add song for EventId={EventId}, SongId={SongId}: {Message}", eventId, songId, ex.Message);
                throw;
            }
        }
    }

    public class SingerResponse
    {
        public List<Singer> Singers { get; set; } = new List<Singer>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int Size { get; set; }
    }
}