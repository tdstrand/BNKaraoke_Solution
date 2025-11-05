// Services\SignalRService.cs (updated, uses local DTOs)
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Services
{
    public class SignalRService
    {
        private readonly IUserSessionService _userSessionService;
        private readonly Action<QueueUpdateMessage> _queueUpdatedCallback;
        private readonly Action<QueueReorderAppliedMessage> _queueReorderAppliedCallback;
        private readonly Action<SingerStatusUpdateMessage> _singerStatusUpdatedCallback;
        private readonly Action<List<EventQueueDto>> _initialQueueCallback;
        private readonly Action<List<DJSingerDto>> _initialSingersCallback;
        private readonly SettingsService _settingsService;
        private HubConnection? _connection;
        private bool _subscriptionsAdded = false;
        private int _currentEventId;
        private const int MaxRetries = 5;
        private readonly int[] _retryDelays = { 5000, 10000, 15000, 20000, 25000 };
        private const string HubPath = "/hubs/karaoke-dj";

        private static readonly JsonSerializerOptions QueueSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public SignalRService(
            IUserSessionService userSessionService,
            Action<QueueUpdateMessage> queueUpdatedCallback,
            Action<QueueReorderAppliedMessage> queueReorderAppliedCallback,
            Action<SingerStatusUpdateMessage> singerStatusUpdatedCallback,
            Action<List<EventQueueDto>> initialQueueCallback,
            Action<List<DJSingerDto>> initialSingersCallback)
        {
            _userSessionService = userSessionService;
            _settingsService = SettingsService.Instance;
            _queueUpdatedCallback = queueUpdatedCallback;
            _queueReorderAppliedCallback = queueReorderAppliedCallback;
            _singerStatusUpdatedCallback = singerStatusUpdatedCallback;
            _initialQueueCallback = initialQueueCallback;
            _initialSingersCallback = initialSingersCallback;
        }

        public async Task StartAsync(int eventId)
        {
            if (_connection != null && _connection.State != HubConnectionState.Disconnected)
            {
                Log.Information("[SIGNALR] Stopping existing connection for EventId={EventId}, CurrentState={State}", _currentEventId, _connection.State);
                await StopAsync(_currentEventId);
            }

            _currentEventId = eventId;
            string apiUrl = _settingsService.Settings.ApiUrl?.TrimEnd('/') ?? "http://localhost:7290";
            string hubUrl = $"{apiUrl}{HubPath}?eventId={eventId}";

            Log.Information("[SIGNALR] Settings: ApiUrl={ApiUrl} for EventId={EventId}", apiUrl, eventId);
            Log.Information("[SIGNALR] Constructing hub URL: {HubUrl} for EventId={EventId}", hubUrl, eventId);

            try
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.AccessTokenProvider = () =>
                        {
                            var token = _userSessionService.Token;
                            Log.Information("[SIGNALR] Providing access token for EventId={EventId}, TokenExists={TokenExists}", eventId, !string.IsNullOrEmpty(token));
                            return Task.FromResult(token);
                        };
                        options.HttpMessageHandlerFactory = (message) =>
                        {
                            if (message is HttpClientHandler clientHandler)
                            {
                                clientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                            }
                            return message;
                        };
                        options.Transports = HttpTransportType.WebSockets;
                    })
                    .WithAutomaticReconnect()
                    .Build();

                EnsureSubscriptions();

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        Log.Information("[SIGNALR] Starting connection for EventId={EventId}, CurrentState={State}, Attempt={Attempt}", eventId, _connection.State, attempt);
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await _connection.StartAsync(cts.Token);
                        Log.Information("[SIGNALR] Connected to hub for EventId={EventId}, ConnectionId={ConnectionId}", eventId, _connection.ConnectionId);
                        Log.Information("[SIGNALR] Attempting to join group Event_{EventId} for EventId={EventId}", eventId, eventId);
                        await JoinEventGroup(eventId, cts.Token);
                        Log.Information("[SIGNALR] Joined group Event_{EventId} for EventId={EventId}", eventId, eventId);
                        Log.Information("[SIGNALR] Connected to hub");
                        return;
                    }
                    catch (HttpRequestException ex)
                    {
                        Log.Error("[SIGNALR] Failed to start connection for EventId={EventId} on attempt {Attempt}: {Message}, StackTrace={StackTrace}", eventId, attempt, ex.Message, ex.StackTrace);
                        if (attempt == MaxRetries)
                        {
                            throw new SignalRException($"Failed to connect after {MaxRetries} attempts: {ex.Message}", ex);
                        }
                        int delay = _retryDelays[attempt - 1];
                        Log.Information("[SIGNALR] Retrying after {Delay}ms for EventId={EventId}", delay, eventId);
                        await Task.Delay(delay, CancellationToken.None);
                    }
                    catch (OperationCanceledException ex)
                    {
                        Log.Error("[SIGNALR] Connection timed out for EventId={EventId} on attempt {Attempt}: {Message}", eventId, attempt, ex.Message);
                        if (attempt == MaxRetries)
                        {
                            throw new SignalRException($"Connection timed out after {MaxRetries} attempts: {ex.Message}", ex);
                        }
                        int delay = _retryDelays[attempt - 1];
                        Log.Information("[SIGNALR] Retrying after {Delay}ms for EventId={EventId}", delay, eventId);
                        await Task.Delay(delay, CancellationToken.None);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SIGNALR] Failed to start connection for EventId={EventId}: {Message}, StackTrace={StackTrace}", eventId, ex.Message, ex.StackTrace);
                throw new SignalRException($"Failed to start SignalR connection: {ex.Message}", ex);
            }
        }

        public async Task StopAsync(int eventId)
        {
            if (_connection == null || _connection.State == HubConnectionState.Disconnected)
            {
                Log.Information("[SIGNALR] Connection already stopped for EventId={EventId}", eventId);
                return;
            }

            try
            {
                Log.Information("[SIGNALR] Stopping connection for EventId={EventId}, CurrentState={State}", eventId, _connection.State);
                await _connection.StopAsync();
                Log.Information("[SIGNALR] Connection stopped for EventId={EventId}", eventId);
            }
            catch (Exception ex)
            {
                Log.Error("[SIGNALR] Failed to stop connection for EventId={EventId}: {Message}, StackTrace={StackTrace}", eventId, ex.Message, ex.StackTrace);
            }
            finally
            {
                _connection = null;
                _currentEventId = 0;
                _subscriptionsAdded = false;
            }
        }

        private void EnsureSubscriptions()
        {
            if (_subscriptionsAdded || _connection == null)
            {
                return;
            }

            _connection.On<List<EventQueueDto>>("InitialQueue", OnInitialQueue);
            _connection.On<List<DJSingerDto>>("InitialSingers", OnInitialSingers);
            _connection.On<JsonElement>("QueueUpdated", OnQueueUpdated);
            _connection.On<JsonElement>("SingerStatusUpdated", OnSingerStatusUpdated);
            _connection.On<JsonElement>("queue/reorder_applied", OnQueueReorderApplied);

            _connection.Reconnected += OnReconnectedAsync;
            _connection.Reconnecting += OnReconnecting;
            _connection.Closed += OnClosed;

            _subscriptionsAdded = true;
            Log.Information("[SIGNALR] Subscribed to hub events");
        }

        private void OnInitialQueue(List<EventQueueDto> queue)
        {
            Log.Information("[SIGNALR] Received InitialQueue for EventId={EventId}, Count={Count}", _currentEventId, queue.Count);
            _initialQueueCallback(queue);
        }

        private void OnInitialSingers(List<DJSingerDto> singers)
        {
            Log.Information("[SIGNALR] Received InitialSingers for EventId={EventId}, Count={Count}", _currentEventId, singers.Count);
            _initialSingersCallback(singers);
        }

        private void OnQueueUpdated(JsonElement payload)
        {
            try
            {
                var message = ParseQueueUpdateMessage(payload);
                Log.Information("[SIGNALR] QueueUpdated – Δ {Delta} (QueueId={QueueId}, Action={Action}, Version={Version})",
                    message.Queue != null ? 1 : 0,
                    message.QueueId,
                    message.Action,
                    message.Version);
                _queueUpdatedCallback(message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SIGNALR] Failed to process QueueUpdated payload for EventId={EventId}", _currentEventId);
            }
        }

        private void OnSingerStatusUpdated(JsonElement payload)
        {
            try
            {
                var message = ParseSingerStatusUpdate(payload);
                Log.Information("[SIGNALR] SingerStatusUpdated – {Singer}:{Status}",
                    message.DisplayName ?? message.UserId,
                    message.IsLoggedIn ? (message.IsJoined ? "Joined" : "Online") : "Offline");
                _singerStatusUpdatedCallback(message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SIGNALR] Failed to process SingerStatusUpdated payload for EventId={EventId}", _currentEventId);
            }
        }

        private Task OnQueueReorderApplied(JsonElement payload)
        {
            try
            {
                var raw = payload.GetRawText();
                var message = JsonSerializer.Deserialize<QueueReorderAppliedMessage>(raw, QueueSerializerOptions);
                if (message == null)
                {
                    Log.Warning("[SIGNALR] Received null queue/reorder_applied payload after deserialization for EventId={EventId}", _currentEventId);
                    return Task.CompletedTask;
                }

                var movedCount = message.MovedQueueIds?.Count ?? message.Metrics?.MoveCount ?? 0;
                Log.Information("[SIGNALR] Received queue/reorder_applied for EventId={EventId}, Version={Version}, Moves={Moves}",
                    message.EventId, message.Version, movedCount);
                _queueReorderAppliedCallback(message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SIGNALR] Failed to process queue/reorder_applied payload for EventId={EventId}", _currentEventId);
            }

            return Task.CompletedTask;
        }

        private Task OnReconnectedAsync(string? connectionId)
        {
            Log.Information("[SIGNALR] Reconnected successfully");
            if (_currentEventId != 0)
            {
                try
                {
                    _ = JoinEventGroup(_currentEventId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[SIGNALR] JoinEventGroup failed");
                }
            }

            return Task.CompletedTask;
        }

        private Task OnReconnecting(Exception? error)
        {
            Log.Warning(error, "[SIGNALR] Connection lost. Attempting to reconnect for EventId={EventId}", _currentEventId);
            return Task.CompletedTask;
        }

        private Task OnClosed(Exception? error)
        {
            Log.Warning(error, "[SIGNALR] Connection closed for EventId={EventId}", _currentEventId);
            return Task.CompletedTask;
        }

        private Task JoinEventGroup(int eventId, CancellationToken cancellationToken = default)
        {
            if (_connection == null)
            {
                return Task.CompletedTask;
            }

            return _connection.InvokeAsync("JoinEventGroup", eventId, cancellationToken);
        }

        private static QueueUpdateMessage ParseQueueUpdateMessage(JsonElement message)
        {
            var result = new QueueUpdateMessage();

            if (message.TryGetProperty("action", out var actionElement) && actionElement.ValueKind == JsonValueKind.String)
            {
                result.Action = actionElement.GetString() ?? string.Empty;
            }

            if (message.TryGetProperty("updateId", out var updateIdElement) && updateIdElement.ValueKind == JsonValueKind.String && Guid.TryParse(updateIdElement.GetString(), out var updateId))
            {
                result.UpdateId = updateId;
            }

            if (message.TryGetProperty("version", out var versionElement) && versionElement.TryGetInt64(out var version))
            {
                result.Version = version;
            }

            if (message.TryGetProperty("updatedAt", out var updatedAtElement) && updatedAtElement.ValueKind == JsonValueKind.String && DateTime.TryParse(updatedAtElement.GetString(), out var updatedAt))
            {
                result.UpdatedAtUtc = updatedAt.ToUniversalTime();
            }

            if (message.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.ValueKind == JsonValueKind.Number && dataElement.TryGetInt32(out var queueId))
                {
                    result.QueueId = queueId;
                }
                else if (dataElement.ValueKind == JsonValueKind.Object)
                {
                    if (dataElement.TryGetProperty("queueId", out var queueIdElement) && queueIdElement.TryGetInt32(out var queueIdValue))
                    {
                        result.QueueId = queueIdValue;
                    }
                    if (dataElement.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.TryGetInt32(out var eventId))
                    {
                        result.EventId = eventId;
                    }
                    if (dataElement.TryGetProperty("youTubeUrl", out var youTubeElement) && youTubeElement.ValueKind == JsonValueKind.String)
                    {
                        result.YouTubeUrl = youTubeElement.GetString();
                    }
                    if (dataElement.TryGetProperty("holdReason", out var holdElement) && holdElement.ValueKind == JsonValueKind.String)
                    {
                        result.HoldReason = holdElement.GetString();
                    }

                    try
                    {
                        var dto = JsonSerializer.Deserialize<EventQueueDto>(dataElement.GetRawText(), QueueSerializerOptions);
                        result.Queue = dto;
                    }
                    catch (JsonException)
                    {
                        // Payload might not be a full queue DTO.
                    }
                }
            }

            if (result.EventId == 0 && message.TryGetProperty("eventId", out var rootEventId) && rootEventId.TryGetInt32(out var evt))
            {
                result.EventId = evt;
            }

            return result;
        }

        private static SingerStatusUpdateMessage ParseSingerStatusUpdate(JsonElement message)
        {
            var result = new SingerStatusUpdateMessage();

            if (message.TryGetProperty("userId", out var userIdElement) && userIdElement.ValueKind == JsonValueKind.String)
            {
                result.UserId = userIdElement.GetString() ?? string.Empty;
            }
            else if (message.TryGetProperty("userName", out var userNameElement) && userNameElement.ValueKind == JsonValueKind.String)
            {
                result.UserId = userNameElement.GetString() ?? string.Empty;
            }

            if (message.TryGetProperty("displayName", out var displayNameElement) && displayNameElement.ValueKind == JsonValueKind.String)
            {
                result.DisplayName = displayNameElement.GetString();
            }

            if (message.TryGetProperty("isLoggedIn", out var loggedElement) && loggedElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result.IsLoggedIn = loggedElement.GetBoolean();
            }

            if (message.TryGetProperty("isJoined", out var joinedElement) && joinedElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result.IsJoined = joinedElement.GetBoolean();
            }

            if (message.TryGetProperty("isOnBreak", out var breakElement) && breakElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result.IsOnBreak = breakElement.GetBoolean();
            }

            if (message.TryGetProperty("updatedAt", out var updatedAtElement) && updatedAtElement.ValueKind == JsonValueKind.String && DateTime.TryParse(updatedAtElement.GetString(), out var updatedAt))
            {
                result.UpdatedAtUtc = updatedAt.ToUniversalTime();
            }

            if (message.TryGetProperty("updateId", out var updateIdElement) && updateIdElement.ValueKind == JsonValueKind.String && Guid.TryParse(updateIdElement.GetString(), out var updateId))
            {
                result.UpdateId = updateId;
            }

            return result;
        }
    }

    public class SignalRException : Exception
    {
        public SignalRException(string message, Exception innerException) : base(message, innerException) { }
    }
}
