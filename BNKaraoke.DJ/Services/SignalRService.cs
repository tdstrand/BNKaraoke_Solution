// Services\SignalRService.cs (updated, uses local DTOs)
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using BNKaraoke.DJ.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

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
        private readonly Serilog.ILogger _logger;
        private HubConnection? _connection;
        private bool _subscriptionsAdded = false;
        private int _currentEventId;
        private const string HubPath = "/hubs/karaoke-dj";
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private CancellationTokenSource? _cts;
        private long _lastQueueVersion = -1;
        private readonly object _versionLock = new();

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
            _logger = Log.ForContext<SignalRService>();
            _queueUpdatedCallback = queueUpdatedCallback;
            _queueReorderAppliedCallback = queueReorderAppliedCallback;
            _singerStatusUpdatedCallback = singerStatusUpdatedCallback;
            _initialQueueCallback = initialQueueCallback;
            _initialSingersCallback = initialSingersCallback;
        }

        public async Task StartAsync(int eventId)
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_connection != null && _connection.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                {
                    _logger.Debug("[SIGNALR] StartAsync skipped: connection already active with State={State}", _connection.State);
                    return;
                }

                if (_connection != null)
                {
                    try
                    {
                        await _connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "[SIGNALR] Failed to dispose previous connection before restart for EventId={EventId}", eventId);
                    }

                    _connection = null;
                    _subscriptionsAdded = false;
                }

                _currentEventId = eventId;
                string apiUrl = _settingsService.Settings.ApiUrl?.TrimEnd('/') ?? "http://localhost:7290";
                string hubUrl = $"{apiUrl}{HubPath}?eventId={eventId}";

                _logger.Information("[SIGNALR] Settings: ApiUrl={ApiUrl} for EventId={EventId}", apiUrl, eventId);
                _logger.Information("[SIGNALR] Constructing hub URL: {HubUrl} for EventId={EventId}", hubUrl, eventId);

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                HubConnection? connection = null;

                try
                {
                    connection = new HubConnectionBuilder()
                        .WithUrl(hubUrl, options =>
                        {
                            options.AccessTokenProvider = () =>
                            {
                                var token = _userSessionService.Token;
                                _logger.Information("[SIGNALR] Providing access token for EventId={EventId}, TokenExists={TokenExists}", eventId, !string.IsNullOrEmpty(token));
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

                    _connection = connection;
                    _subscriptionsAdded = false;
                    EnsureSubscriptions();

                    await connection.StartAsync(_cts.Token).ConfigureAwait(false);
                    _logger.Information("[SIGNALR] Connected to hub for EventId={EventId}, ConnectionId={ConnectionId}", eventId, connection.ConnectionId);

                    if (eventId > 0)
                    {
                        try
                        {
                            await JoinEventGroup(eventId, _cts.Token).ConfigureAwait(false);
                            _logger.Information("[SIGNALR] Joined group Event_{EventId}", eventId);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "[SIGNALR] Failed to join group Event_{EventId}", eventId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "[SIGNALR] Failed to start connection for EventId={EventId}", eventId);

                    if (connection != null)
                    {
                        try
                        {
                            await connection.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception disposeEx)
                        {
                            _logger.Warning(disposeEx, "[SIGNALR] Failed to dispose connection after start failure for EventId={EventId}", eventId);
                        }
                    }

                    _connection = null;
                    _subscriptionsAdded = false;
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                    throw new SignalRException($"Failed to start SignalR connection: {ex.Message}", ex);
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopAsync(int eventId)
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                var connection = _connection;

                if (connection != null)
                {
                    try
                    {
                        await connection.StopAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "[SIGNALR] Failed to stop connection for EventId={EventId}", eventId);
                    }

                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "[SIGNALR] Failed to dispose connection for EventId={EventId}", eventId);
                    }
                }

                _subscriptionsAdded = false;
                _connection = null;
                _currentEventId = 0;
                ResetQueueVersion();
            }
            finally
            {
                _lifecycleGate.Release();
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
            _logger.Debug("[SIGNALR] Subscribed to hub events");
        }

        private async Task OnInitialQueue(List<EventQueueDto> queue)
        {
            _logger.Information("[SIGNALR] Received InitialQueue Count={Count}", queue.Count);

            lock (_versionLock)
            {
                _lastQueueVersion = Math.Max(_lastQueueVersion, 0);
            }

            await RunOnUiAsync(() => _initialQueueCallback(queue)).ConfigureAwait(false);
        }

        private async Task OnInitialSingers(List<DJSingerDto> singers)
        {
            _logger.Information("[SIGNALR] Received InitialSingers Count={Count}", singers.Count);
            await RunOnUiAsync(() => _initialSingersCallback(singers)).ConfigureAwait(false);
        }

        private void OnQueueUpdated(JsonElement payload)
        {
            try
            {
                var message = ParseQueueUpdateMessage(payload);
                var incomingVersion = message.Version;

                if (incomingVersion.HasValue && IsStaleQueueVersion(incomingVersion.Value, out var lastVersion))
                {
                    _logger.Debug("[SIGNALR] Dropping stale queue event Version={Version} LastApplied={Last}", incomingVersion.Value, lastVersion);
                    return;
                }

                _logger.Information("[SIGNALR] QueueUpdated – Δ {Delta} (QueueId={QueueId}, Action={Action}, Version={Version})",
                    message.Queue != null ? 1 : 0,
                    message.QueueId,
                    message.Action,
                    message.Version);
                _queueUpdatedCallback(message);

                if (incomingVersion.HasValue)
                {
                    MarkQueueVersion(incomingVersion.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[SIGNALR] Failed to process QueueUpdated payload for EventId={EventId}", _currentEventId);
            }
        }

        private void OnSingerStatusUpdated(JsonElement payload)
        {
            try
            {
                var message = ParseSingerStatusUpdate(payload);
                _logger.Information("[SIGNALR] SingerStatusUpdated – {Singer}:{Status}",
                    message.DisplayName ?? message.UserId,
                    message.IsLoggedIn ? (message.IsJoined ? "Joined" : "Online") : "Offline");
                _singerStatusUpdatedCallback(message);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[SIGNALR] Failed to process SingerStatusUpdated payload for EventId={EventId}", _currentEventId);
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
                    _logger.Warning("[SIGNALR] Received null queue/reorder_applied payload after deserialization for EventId={EventId}", _currentEventId);
                }
                else
                {
                    long? incomingVersion = long.TryParse(message.Version, out var parsedVersion) ? parsedVersion : null;

                    if (incomingVersion.HasValue && IsStaleQueueVersion(incomingVersion.Value, out var lastVersion))
                    {
                        _logger.Debug("[SIGNALR] Dropping stale queue event Version={Version} LastApplied={Last}", incomingVersion.Value, lastVersion);
                        return Task.CompletedTask;
                    }

                    var movedCount = message.MovedQueueIds?.Count ?? message.Metrics?.MoveCount ?? 0;
                    _logger.Information(
                        "[SIGNALR] Received queue/reorder_applied for EventId={EventId}, Version={Version}, Moves={Moves}",
                        _currentEventId,
                        message.Version,
                        movedCount);
                    _queueReorderAppliedCallback(message);

                    if (incomingVersion.HasValue)
                    {
                        MarkQueueVersion(incomingVersion.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[SIGNALR] Failed to process queue/reorder_applied payload for EventId={EventId}", _currentEventId);
            }

            return Task.CompletedTask;
        }

        private async Task OnReconnectedAsync(string? connectionId)
        {
            _logger.Information("[SIGNALR] Reconnected successfully");

            if (_currentEventId != 0)
            {
                try
                {
                    await JoinEventGroup(_currentEventId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "[SIGNALR] JoinEventGroup failed for EventId={EventId}", _currentEventId);
                }
            }
        }

        private Task OnReconnecting(Exception? _)
        {
            _logger.Debug("[SIGNALR] Reconnecting…");
            return Task.CompletedTask;
        }

        private Task OnClosed(Exception? _)
        {
            _logger.Debug("[SIGNALR] Connection closed");
            return Task.CompletedTask;
        }

        private Task JoinEventGroup(int eventId, CancellationToken cancellationToken = default)
        {
            if (_connection == null)
            {
                return Task.CompletedTask;
            }

            return _connection.InvokeAsync("JoinEventGroup", new object?[] { eventId }, cancellationToken);
        }

        private static bool OnUi => Application.Current?.Dispatcher?.CheckAccess() == true;

        private Task RunOnUiAsync(Action action)
        {
            if (OnUi)
            {
                action();
                return Task.CompletedTask;
            }

            return Application.Current!.Dispatcher.InvokeAsync(action).Task;
        }

        private bool IsStaleQueueVersion(long incomingVersion, out long lastVersion)
        {
            lock (_versionLock)
            {
                lastVersion = _lastQueueVersion;
                return incomingVersion <= _lastQueueVersion && _lastQueueVersion >= 0;
            }
        }

        private void MarkQueueVersion(long version)
        {
            lock (_versionLock)
            {
                if (version > _lastQueueVersion)
                {
                    _lastQueueVersion = version;
                }
            }
        }

        private void ResetQueueVersion()
        {
            lock (_versionLock)
            {
                _lastQueueVersion = -1;
            }
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
