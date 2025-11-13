using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
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
        private readonly Action<DJQueueItemDto> _queueItemAddedCallback;
        private readonly Action<DJQueueItemDto> _queueItemUpdatedCallback;
        private readonly Action<int> _queueItemRemovedCallback;
        private readonly Action<QueueReorderAppliedMessage> _queueReorderAppliedCallback;
        private readonly Action<SingerStatusUpdateMessage> _singerStatusUpdatedCallback;
        private readonly Action<List<DJQueueItemDto>> _initialQueueCallback;
        private readonly Action<List<SingerStatusDto>> _initialSingersCallback;
        private readonly SettingsService _settingsService;
        private readonly Serilog.ILogger _logger;
        private HubConnection? _connection;
        private bool _subscriptionsAdded = false;
        private int _currentEventId;
        private const string HubPath = "/hubs/karaoke-dj";
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private CancellationTokenSource? _cts;

        private static readonly JsonSerializerOptions QueueSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public SignalRService(
            IUserSessionService userSessionService,
            Action<DJQueueItemDto> queueItemAddedCallback,
            Action<DJQueueItemDto> queueItemUpdatedCallback,
            Action<int> queueItemRemovedCallback,
            Action<QueueReorderAppliedMessage> queueReorderAppliedCallback,
            Action<SingerStatusUpdateMessage> singerStatusUpdatedCallback,
            Action<List<DJQueueItemDto>> initialQueueCallback,
            Action<List<SingerStatusDto>> initialSingersCallback)
        {
            _userSessionService = userSessionService;
            _settingsService = SettingsService.Instance;
            _logger = Log.ForContext<SignalRService>();
            _queueItemAddedCallback = queueItemAddedCallback;
            _queueItemUpdatedCallback = queueItemUpdatedCallback;
            _queueItemRemovedCallback = queueItemRemovedCallback;
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
                        catch (HubException ex)
                        {
                            _logger.Warning("[SIGNALR] JoinEventGroup failed for Event_{EventId}: {Message} — Continuing hydration", eventId, ex.Message);
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

            _connection.On<List<DJQueueItemDto>>("InitialQueueV2", OnInitialQueueV2);
            _connection.On<List<SingerStatusDto>>("InitialSingersV2", OnInitialSingersV2);
            _connection.On<DJQueueItemDto>("QueueItemAddedV2", OnQueueItemAddedV2);
            _connection.On<DJQueueItemDto>("QueueItemUpdatedV2", OnQueueItemUpdatedV2);
            _connection.On<JsonElement>("QueueItemRemovedV2", OnQueueItemRemovedV2);
            _connection.On<JsonElement>("SingerStatusUpdated", OnSingerStatusUpdated);
            _connection.On<JsonElement>("queue/reorder_applied", OnQueueReorderApplied);

            _connection.Reconnected += OnReconnectedAsync;
            _connection.Reconnecting += OnReconnecting;
            _connection.Closed += OnClosed;

            _subscriptionsAdded = true;
            _logger.Information("[SIGNALR] Using V2-only handlers (InitialQueueV2/Queue*V2)");
        }

        private async Task OnInitialQueueV2(List<DJQueueItemDto> queue)
        {
            _logger.Information("[SIGNALR] Received InitialQueueV2 Count={Count}", queue.Count);
            await RunOnUiAsync(() => _initialQueueCallback(queue)).ConfigureAwait(false);
        }

        private async Task OnInitialSingersV2(List<SingerStatusDto> singers)
        {
            _logger.Information("[SIGNALR] Received InitialSingersV2 Count={Count}", singers.Count);
            await RunOnUiAsync(() => _initialSingersCallback(singers)).ConfigureAwait(false);
        }

        private Task OnQueueItemAddedV2(DJQueueItemDto item)
        {
            if (item == null)
            {
                _logger.Warning("[SIGNALR] QueueItemAddedV2 payload was null for EventId={EventId}", _currentEventId);
                return Task.CompletedTask;
            }

            _logger.Information("[SIGNALR] QueueItemAddedV2 QueueId={QueueId} Position={Position}", item.QueueId, item.Position);
            return RunOnUiAsync(() => _queueItemAddedCallback(item));
        }

        private Task OnQueueItemUpdatedV2(DJQueueItemDto item)
        {
            if (item == null)
            {
                _logger.Warning("[SIGNALR] QueueItemUpdatedV2 payload was null for EventId={EventId}", _currentEventId);
                return Task.CompletedTask;
            }

            _logger.Information("[SIGNALR] QueueItemUpdatedV2 QueueId={QueueId} Position={Position} Status={Status}", item.QueueId, item.Position, item.Status);
            return RunOnUiAsync(() => _queueItemUpdatedCallback(item));
        }

        private Task OnQueueItemRemovedV2(JsonElement payload)
        {
            const int MaxPayloadLogLength = 200;
            var rawPayload = payload.GetRawText();
            var truncatedPayload = TruncateRaw(rawPayload, MaxPayloadLogLength);

            try
            {
                var (success, queueId, shape) = TryParseRemovedId(payload);

                if (!success || queueId <= 0)
                {
                    _logger.Warning("[SIGNALR WARN] QueueItemRemovedV2 unparseable payload='{Payload}'", truncatedPayload);
                    return Task.CompletedTask;
                }

                _logger.Information("[SIGNALR REMOVE] QueueItemRemovedV2 id={Id} (shape={Shape})", queueId, shape);
                return RunOnUiAsync(() => _queueItemRemovedCallback(queueId));
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[SIGNALR WARN] QueueItemRemovedV2 parse error payload='{Payload}'", truncatedPayload);
                return Task.CompletedTask;
            }

            (bool Success, int QueueId, string Shape) TryParseRemovedId(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Number && TryReadQueueId(value, out var numberId))
                {
                    return (true, numberId, "Number");
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in value.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "queueId", StringComparison.OrdinalIgnoreCase) &&
                            TryReadQueueId(property.Value, out var objectId))
                        {
                            return (true, objectId, "Object");
                        }
                    }
                }

                return (false, 0, string.Empty);
            }

            static bool TryReadQueueId(JsonElement element, out int id)
            {
                if (element.ValueKind == JsonValueKind.Number)
                {
                    if (element.TryGetInt32(out id))
                    {
                        return true;
                    }

                    if (element.TryGetInt64(out var longValue) && longValue >= int.MinValue && longValue <= int.MaxValue)
                    {
                        id = (int)longValue;
                        return true;
                    }
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && int.TryParse(text, out id))
                    {
                        return true;
                    }
                }

                id = 0;
                return false;
            }

            static string TruncateRaw(string? raw, int maxLength)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    return raw ?? string.Empty;
                }

                return raw.Length <= maxLength ? raw : raw.Substring(0, maxLength);
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
                    var movedCount = message.MovedQueueIds?.Count ?? message.Metrics?.MoveCount ?? 0;
                    _logger.Information(
                        "[SIGNALR] Received queue/reorder_applied for EventId={EventId}, Version={Version}, Moves={Moves}",
                        _currentEventId,
                        message.Version,
                        movedCount);
                    _queueReorderAppliedCallback(message);
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
