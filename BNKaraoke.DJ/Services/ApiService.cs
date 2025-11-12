using BNKaraoke.DJ.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

        private void EnsureBaseAddress()
        {
            var configuredUrl = _settingsService.Settings.ApiUrl;
            if (string.IsNullOrWhiteSpace(configuredUrl))
            {
                Log.Warning("[APISERVICE] Cannot update base address: ApiUrl is not configured");
                return;
            }

            var currentBase = _httpClient.BaseAddress?.ToString().TrimEnd('/');
            var desiredBase = configuredUrl.TrimEnd('/');
            if (!string.Equals(currentBase, desiredBase, StringComparison.OrdinalIgnoreCase))
            {
                _httpClient.BaseAddress = new Uri(configuredUrl);
                Log.Information("[APISERVICE] Updated base address to {ApiUrl}", configuredUrl);
            }
        }

        private void ConfigureAuthorizationHeader()
        {
            EnsureBaseAddress();
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
                Log.Information("[APISERVICE] Attempting to fetch events");
                var response = await _httpClient.GetAsync("/api/events?status=active", cancellationToken);
                response.EnsureSuccessStatusCode();
                var events = await response.Content.ReadFromJsonAsync<List<EventDto>>(cancellationToken);
                var liveEvents = events?.Where(e => e.Status == "Live").ToList() ?? new List<EventDto>();
                Log.Information("[APISERVICE] Fetched {TotalCount} events, filtered to {LiveCount} live events", events?.Count ?? 0, liveEvents.Count);
                return liveEvents;
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
                EnsureBaseAddress();
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
                Log.Error("[APISERVICE] Failed to fetch singers for EventId={EventId}: {Message}", eventId, ex.Message);
                throw;
            }
        }

        public async Task<List<DJQueueItemDto>> GetQueueAsync(string eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Fetching queue for EventId={EventId}", eventId);
                var v2Response = await _httpClient.GetAsync($"/api/v2/dj/queue/unplayed?eventId={eventId}");
                if (v2Response.IsSuccessStatusCode)
                {
                    var v2Queue = await v2Response.Content.ReadFromJsonAsync<List<DJQueueItemDto>>(new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var result = v2Queue ?? new List<DJQueueItemDto>();
                    Log.Information("[APISERVICE] Fetched {Count} queue entries for EventId={EventId}", result.Count, eventId);
                    return result;
                }

                if (v2Response.StatusCode != HttpStatusCode.NotFound)
                {
                    var v2Error = await v2Response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] V2 queue fetch failed for EventId={EventId}: Status={StatusCode}, Message={Message}", eventId, v2Response.StatusCode, v2Error);

                    if (!v2Response.IsSuccessStatusCode)
                    {
                        return new List<DJQueueItemDto>();
                    }
                }

                Log.Information("[APISERVICE] Using legacy queue REST shape, mapping to V2 DTO locally");
                // TODO: Switch to V2 REST once fully deployed.

                var legacyResponse = await _httpClient.GetAsync($"/api/dj/queue/unplayed?eventId={eventId}");
                if (!legacyResponse.IsSuccessStatusCode)
                {
                    var errorContent = await legacyResponse.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to fetch queue for event {EventId}: Status={StatusCode}, Message={Message}", eventId, legacyResponse.StatusCode, errorContent);
                    return new List<DJQueueItemDto>();
                }

                var queueResponse = await legacyResponse.Content.ReadFromJsonAsync<List<LegacyEventQueueDto>>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                var converted = queueResponse != null
                    ? queueResponse.Select(ConvertLegacyQueueItem).Where(item => item != null).Cast<DJQueueItemDto>().ToList()
                    : new List<DJQueueItemDto>();

                Log.Information("[APISERVICE] Fetched {Count} queue entries for EventId={EventId}", converted.Count, eventId);
                return converted;
            }
            catch (JsonException ex)
            {
                Log.Error("[APISERVICE] Failed to deserialize queue for EventId={EventId}: {Message}", eventId, ex.Message);
                return new List<DJQueueItemDto>();
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch queue for EventId={EventId}: {Message}", eventId, ex.Message);
                throw;
            }
        }

        private static DJQueueItemDto? ConvertLegacyQueueItem(LegacyEventQueueDto? dto)
        {
            if (dto == null)
            {
                return null;
            }

            var singer = new SingerStatusDto
            {
                UserId = dto.RequestorUserName ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(dto.RequestorFullName)
                    ? dto.RequestorUserName ?? string.Empty
                    : dto.RequestorFullName,
                IsLoggedIn = dto.IsSingerLoggedIn,
                IsJoined = dto.IsSingerJoined,
                IsOnBreak = dto.IsSingerOnBreak || dto.IsOnBreak
            };

            var flags = SingerStatusFlags.None;
            if (singer.IsLoggedIn)
            {
                flags |= SingerStatusFlags.LoggedIn;
            }
            if (singer.IsJoined)
            {
                flags |= SingerStatusFlags.Joined;
            }
            if (singer.IsOnBreak)
            {
                flags |= SingerStatusFlags.OnBreak;
            }
            singer.Flags = flags;

            var holdReason = dto.HoldReason ?? string.Empty;

            return new DJQueueItemDto
            {
                QueueId = dto.QueueId,
                EventId = dto.EventId,
                SongId = dto.SongId,
                SongTitle = dto.SongTitle ?? string.Empty,
                SongArtist = dto.SongArtist ?? string.Empty,
                YouTubeUrl = dto.YouTubeUrl,
                RequestorUserName = dto.RequestorUserName ?? string.Empty,
                RequestorDisplayName = string.IsNullOrWhiteSpace(dto.RequestorFullName) ? singer.DisplayName : dto.RequestorFullName,
                Singers = dto.Singers != null ? new List<string>(dto.Singers) : new List<string>(),
                Singer = singer,
                Position = dto.Position,
                Status = dto.Status ?? string.Empty,
                IsActive = dto.IsActive,
                WasSkipped = dto.WasSkipped,
                IsCurrentlyPlaying = dto.IsCurrentlyPlaying,
                SungAt = dto.SungAt,
                IsUpNext = dto.IsUpNext,
                HoldReason = holdReason,
                IsSingerLoggedIn = singer.IsLoggedIn,
                IsSingerJoined = singer.IsJoined,
                IsSingerOnBreak = singer.IsOnBreak,
                IsServerCached = dto.IsServerCached,
                IsMature = dto.IsMature,
                NormalizationGain = dto.NormalizationGain,
                FadeStartTime = dto.FadeStartTime,
                IntroMuteDuration = dto.IntroMuteDuration
            };
        }

        private sealed class LegacyEventQueueDto
        {
            public int QueueId { get; set; }
            public int EventId { get; set; }
            public int SongId { get; set; }
            public string? SongTitle { get; set; }
            public string? SongArtist { get; set; }
            public string? YouTubeUrl { get; set; }
            public string? RequestorUserName { get; set; }
            public string? RequestorFullName { get; set; }
            public List<string>? Singers { get; set; }
            public int Position { get; set; }
            public string? Status { get; set; }
            public bool IsActive { get; set; }
            public bool WasSkipped { get; set; }
            public bool IsCurrentlyPlaying { get; set; }
            public DateTime? SungAt { get; set; }
            public bool IsOnBreak { get; set; }
            public string? HoldReason { get; set; }
            public bool IsUpNext { get; set; }
            public bool IsSingerLoggedIn { get; set; }
            public bool IsSingerJoined { get; set; }
            public bool IsSingerOnBreak { get; set; }
            public bool IsServerCached { get; set; }
            public bool IsMature { get; set; }
            public float? NormalizationGain { get; set; }
            public float? FadeStartTime { get; set; }
            public float? IntroMuteDuration { get; set; }
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

        public async Task ReorderQueueAsync(string eventId, List<QueuePosition> newOrder)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var request = new ReorderQueueRequest { NewOrder = newOrder };
                var jsonPayload = JsonSerializer.Serialize(request);
                Log.Information("[APISERVICE] Reordering queue for EventId={EventId}, Payload={Payload}", eventId, jsonPayload);
                var response = await _httpClient.PutAsJsonAsync($"/api/events/{eventId}/queue/reorder", request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to reorder queue for EventId={EventId}: Status={StatusCode}, Error={ErrorContent}", eventId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to reorder queue: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Successfully reordered queue for EventId={EventId}", eventId);
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

        public async Task<ReorderSuggestionResponse?> GetReorderSuggestionsAsync(int eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Requesting reorder suggestions for EventId={EventId}", eventId);
                var response = await _httpClient.PostAsJsonAsync($"/api/queue/reorder-suggestions/{eventId}", new { });

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to fetch reorder suggestions for EventId={EventId}: Status={StatusCode}, Error={ErrorContent}", eventId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to fetch reorder suggestions: {response.StatusCode} - {errorContent}", null, response.StatusCode);
                }

                var suggestions = await response.Content.ReadFromJsonAsync<ReorderSuggestionResponse>();
                var count = suggestions?.Suggestions?.Count ?? 0;
                Log.Information("[APISERVICE] Received {Count} reorder suggestions for EventId={EventId}", count, eventId);
                return suggestions;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[APISERVICE] Failed to fetch reorder suggestions for EventId={EventId}: {Message}", eventId, ex.Message);
                throw;
            }
        }

        public async Task ApplyReorderSuggestionsAsync(ApplyReorderRequest request)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var suggestionCount = request?.Suggestions?.Count ?? 0;
                Log.Information("[APISERVICE] Applying {Count} reorder suggestions for EventId={EventId}", suggestionCount, request?.EventId);
                var response = await _httpClient.PostAsJsonAsync("/api/queue/apply-reorder", request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to apply reorder suggestions for EventId={EventId}: Status={StatusCode}, Error={ErrorContent}", request?.EventId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to apply reorder suggestions: {response.StatusCode} - {errorContent}", null, response.StatusCode);
                }

                Log.Information("[APISERVICE] Applied reorder suggestions for EventId={EventId}", request?.EventId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[APISERVICE] Failed to apply reorder suggestions for EventId={EventId}: {Message}", request?.EventId, ex.Message);
                throw;
            }
        }

        public async Task<ReorderPreviewResponse> PreviewQueueReorderAsync(ReorderPreviewRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Requesting reorder preview for EventId={EventId}", request.EventId);
                var response = await _httpClient.PostAsJsonAsync("/api/dj/queue/reorder/preview", request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    var error = await response.Content.ReadFromJsonAsync<ReorderErrorResponse>(cancellationToken: cancellationToken);
                    var message = error?.Message ?? "Unable to generate reorder preview.";
                    Log.Warning("[APISERVICE] Reorder preview returned 422: {Message}", message);
                    throw new ApiRequestException(message, response.StatusCode, error?.Warnings);
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    var conflict = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken: cancellationToken) ?? new Dictionary<string, string>();
                    conflict.TryGetValue("message", out var conflictMessage);
                    conflict.TryGetValue("currentVersion", out var currentVersion);
                    Log.Warning("[APISERVICE] Reorder preview conflicted with server state. Message={Message}, Version={Version}", conflictMessage, currentVersion);
                    throw new ApiRequestException(conflictMessage ?? "Queue has changed. Refresh and try again.", response.StatusCode, currentVersion: currentVersion);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to preview reorder plan: Status={StatusCode}, Error={Error}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to preview reorder plan: {response.StatusCode} - {errorContent}", null, response.StatusCode);
                }

                var preview = await response.Content.ReadFromJsonAsync<ReorderPreviewResponse>(cancellationToken: cancellationToken);
                if (preview == null)
                {
                    throw new InvalidOperationException("Preview response was empty.");
                }

                Log.Information("[APISERVICE] Received reorder preview: PlanId={PlanId}, Moves={MoveCount}", preview.PlanId, preview.Summary.MoveCount);
                return preview;
            }
            catch (ApiRequestException)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                Log.Error("[APISERVICE] Reorder preview request timed out or was canceled: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[APISERVICE] Unexpected error during reorder preview: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<ReorderApplyResponse> ApplyQueueReorderAsync(ReorderApplyRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Applying reorder plan {PlanId} for EventId={EventId}", request.PlanId, request.EventId);
                var response = await _httpClient.PostAsJsonAsync("/api/dj/queue/reorder/apply", request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    var error = await response.Content.ReadFromJsonAsync<ReorderErrorResponse>(cancellationToken: cancellationToken);
                    var message = error?.Message ?? "Unable to apply reorder plan.";
                    Log.Warning("[APISERVICE] Reorder apply returned 422: {Message}", message);
                    throw new ApiRequestException(message, response.StatusCode, error?.Warnings);
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    var conflict = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken: cancellationToken) ?? new Dictionary<string, string>();
                    conflict.TryGetValue("message", out var conflictMessage);
                    conflict.TryGetValue("currentVersion", out var currentVersion);
                    Log.Warning("[APISERVICE] Reorder apply conflicted with server state. Message={Message}, Version={Version}", conflictMessage, currentVersion);
                    throw new ApiRequestException(conflictMessage ?? "Queue has changed. Preview a new plan before applying.", response.StatusCode, currentVersion: currentVersion);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to apply reorder plan: Status={StatusCode}, Error={Error}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to apply reorder plan: {response.StatusCode} - {errorContent}", null, response.StatusCode);
                }

                var applyResponse = await response.Content.ReadFromJsonAsync<ReorderApplyResponse>(cancellationToken: cancellationToken);
                if (applyResponse == null)
                {
                    throw new InvalidOperationException("Apply response was empty.");
                }

                Log.Information("[APISERVICE] Reorder plan applied. AppliedVersion={AppliedVersion}, Moves={MoveCount}", applyResponse.AppliedVersion, applyResponse.MoveCount);
                return applyResponse;
            }
            catch (ApiRequestException)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                Log.Error("[APISERVICE] Reorder apply request timed out or was canceled: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[APISERVICE] Unexpected error while applying reorder plan: {Message}", ex.Message);
                throw;
            }
        }

        public async Task ResetNowPlayingAsync(string eventId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                Log.Information("[APISERVICE] Resetting now playing for EventId={EventId}", eventId);
                var response = await _httpClient.PostAsync($"/api/dj/{eventId}/queue/reset-now-playing", null);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("[APISERVICE] Failed to reset now playing for EventId={EventId}: Status={StatusCode}, Error={Error}", eventId, response.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to reset now playing: {response.StatusCode} - {errorContent}");
                }
                Log.Information("[APISERVICE] Reset now playing for EventId={EventId}", eventId);
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to reset now playing for EventId={EventId}: {Message}", eventId, ex.Message);
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

        public async Task<List<CacheManifestItem>> GetCacheManifestAsync()
        {
            try
            {
                ConfigureAuthorizationHeader();
                var response = await _httpClient.GetAsync("/api/cache/manifest");
                response.EnsureSuccessStatusCode();
                var manifest = await response.Content.ReadFromJsonAsync<List<CacheManifestItem>>();
                Log.Information("[APISERVICE] Fetched cache manifest with {Count} entries", manifest?.Count ?? 0);
                return manifest ?? new List<CacheManifestItem>();
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to fetch cache manifest: {Message}", ex.Message);
                return new List<CacheManifestItem>();
            }
        }

        public async Task<Stream> DownloadCachedSongAsync(int songId)
        {
            try
            {
                ConfigureAuthorizationHeader();
                var response = await _httpClient.GetAsync($"/api/cache/{songId}", HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync();
                Log.Information("[APISERVICE] Downloading cache file for SongId={SongId}", songId);
                return stream;
            }
            catch (Exception ex)
            {
                Log.Error("[APISERVICE] Failed to download cache file for SongId={SongId}: {Message}", songId, ex.Message);
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