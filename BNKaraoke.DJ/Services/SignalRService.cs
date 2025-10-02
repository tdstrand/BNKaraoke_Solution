﻿// Services\SignalRService.cs (updated, uses local DTOs)
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Services
{
    public class SignalRService
    {
        private readonly IUserSessionService _userSessionService;
        private readonly Action<QueueUpdateMessage> _queueUpdatedCallback;
        private readonly Action<QueueReorderAppliedMessage> _queueReorderAppliedCallback;
        private readonly Action<string, bool, bool, bool> _singerStatusUpdatedCallback;
        private readonly Action<List<EventQueueDto>> _initialQueueCallback;
        private readonly Action<List<DJSingerDto>> _initialSingersCallback;
        private readonly SettingsService _settingsService;
        private HubConnection? _connection;
        private int _currentEventId;
        private const int MaxRetries = 5;
        private readonly int[] _retryDelays = { 5000, 10000, 15000, 20000, 25000 };
        private const string HubPath = "/hubs/karaoke-dj";

        private static readonly JsonSerializerOptions ReorderSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public SignalRService(
            IUserSessionService userSessionService,
            Action<QueueUpdateMessage> queueUpdatedCallback,
            Action<QueueReorderAppliedMessage> queueReorderAppliedCallback,
            Action<string, bool, bool, bool> singerStatusUpdatedCallback,
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
                        options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _connection.On<JsonElement>("QueueUpdated", message =>
                {
                    int queueId = 0;
                    int eventId = _currentEventId;
                    string action = string.Empty;
                    string? youTubeUrl = null;
                    string? holdReason = null;

                    try
                    {
                        if (message.TryGetProperty("action", out var act) && act.ValueKind == JsonValueKind.String)
                            action = act.GetString() ?? string.Empty;

                        if (message.TryGetProperty("data", out var payload))
                        {
                            if (payload.ValueKind == JsonValueKind.Object)
                            {
                                if (payload.TryGetProperty("queueId", out var qId) && qId.TryGetInt32(out var q))
                                    queueId = q;
                                if (payload.TryGetProperty("eventId", out var eId) && eId.TryGetInt32(out var e))
                                    eventId = e;
                                if (payload.TryGetProperty("youTubeUrl", out var yt) && yt.ValueKind == JsonValueKind.String)
                                    youTubeUrl = yt.GetString();
                                if (payload.TryGetProperty("holdReason", out var hr) && hr.ValueKind == JsonValueKind.String)
                                    holdReason = hr.GetString();
                            }
                            else if (payload.ValueKind == JsonValueKind.Number && payload.TryGetInt32(out var q))
                            {
                                queueId = q;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[SIGNALR] Failed to parse QueueUpdated payload: {Message}", ex.Message);
                    }

                    Log.Information("[SIGNALR] Received QueueUpdated for EventId={EventId}, QueueId={QueueId}, Action={Action}, YouTubeUrl={YouTubeUrl}, HoldReason={HoldReason}",
                        eventId, queueId, action, youTubeUrl, holdReason);
                    _queueUpdatedCallback(new QueueUpdateMessage
                    {
                        QueueId = queueId,
                        EventId = eventId,
                        Action = action,
                        YouTubeUrl = youTubeUrl,
                        HoldReason = holdReason
                    });
                });

                _connection.On<JsonElement>("SingerStatusUpdated", message =>
                {
                    string userName = string.Empty;
                    bool isLoggedIn = false;
                    bool isJoined = false;
                    bool isOnBreak = false;

                    try
                    {
                        if (message.TryGetProperty("userName", out var u) && u.ValueKind == JsonValueKind.String)
                            userName = u.GetString() ?? string.Empty;
                        if (message.TryGetProperty("isLoggedIn", out var logged) && (logged.ValueKind == JsonValueKind.True || logged.ValueKind == JsonValueKind.False))
                            isLoggedIn = logged.GetBoolean();
                        if (message.TryGetProperty("isJoined", out var joined) && (joined.ValueKind == JsonValueKind.True || joined.ValueKind == JsonValueKind.False))
                            isJoined = joined.GetBoolean();
                        if (message.TryGetProperty("isOnBreak", out var brk) && (brk.ValueKind == JsonValueKind.True || brk.ValueKind == JsonValueKind.False))
                            isOnBreak = brk.GetBoolean();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[SIGNALR] Failed to parse SingerStatusUpdated payload: {Message}", ex.Message);
                    }

                    Log.Information("[SIGNALR] Received SingerStatusUpdated for EventId={EventId}, RequestorUserName={RequestorUserName}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}",
                        _currentEventId, userName, isLoggedIn, isJoined, isOnBreak);
                    _singerStatusUpdatedCallback(userName, isLoggedIn, isJoined, isOnBreak);
                });

                _connection.On<JsonElement>("queue/reorder_applied", payload =>
                {
                    try
                    {
                        var raw = payload.GetRawText();
                        var message = JsonSerializer.Deserialize<QueueReorderAppliedMessage>(raw, ReorderSerializerOptions);
                        if (message == null)
                        {
                            Log.Warning("[SIGNALR] Received null queue/reorder_applied payload after deserialization for EventId={EventId}", _currentEventId);
                            return;
                        }

                        var movedCount = message.MovedQueueIds?.Count ?? message.Metrics?.MoveCount ?? 0;
                        Log.Information("[SIGNALR] Received queue/reorder_applied for EventId={EventId}, Version={Version}, Moves={Moves}",
                            message.EventId, message.Version, movedCount);
                        _queueReorderAppliedCallback(message);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[SIGNALR] Failed to process queue/reorder_applied payload for EventId={EventId}: {Message}", _currentEventId, ex.Message);
                    }
                });

                _connection.On<List<EventQueueDto>>("InitialQueue", (queue) =>
                {
                    Log.Information("[SIGNALR] Received InitialQueue for EventId={EventId}, Count={Count}", _currentEventId, queue.Count);
                    foreach (var item in queue)
                    {
                        Log.Debug("[SIGNALR] Queue item {QueueId} IsServerCached={IsServerCached}", item.QueueId, item.IsServerCached);
                    }
                    _initialQueueCallback(queue);
                });

                _connection.On<List<DJSingerDto>>("InitialSingers", (singers) =>
                {
                    Log.Information("[SIGNALR] Received InitialSingers for EventId={EventId}, Count={Count}", _currentEventId, singers.Count);
                    _initialSingersCallback(singers);
                });

                Log.Information("[SIGNALR] Subscribed to QueueUpdated, queue/reorder_applied, SingerStatusUpdated, InitialQueue, InitialSingers events for EventId={EventId}", eventId);

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        Log.Information("[SIGNALR] Starting connection for EventId={EventId}, CurrentState={State}, Attempt={Attempt}", eventId, _connection.State, attempt);
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await _connection.StartAsync(cts.Token);
                        Log.Information("[SIGNALR] Connected to hub for EventId={EventId}, ConnectionId={ConnectionId}", eventId, _connection.ConnectionId);
                        Log.Information("[SIGNALR] Attempting to join group Event_{EventId} for EventId={EventId}", eventId, eventId);
                        await _connection.InvokeAsync("JoinEventGroup", eventId, cts.Token);
                        Log.Information("[SIGNALR] Joined group Event_{EventId} for EventId={EventId}", eventId, eventId);
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
            }
        }
    }

    public class SignalRException : Exception
    {
        public SignalRException(string message, Exception innerException) : base(message, innerException) { }
    }
}