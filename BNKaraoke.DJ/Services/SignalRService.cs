using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BNKaraoke.DJ.Services
{
    public class SignalRService
    {
        private readonly IUserSessionService _userSessionService;
        private readonly Action<int, string, int?, bool?, bool, bool, bool> _queueUpdatedCallback;
        private readonly Action<string, bool, bool, bool> _singerStatusUpdatedCallback;
        private readonly SettingsService _settingsService;
        private HubConnection? _connection;
        private int _currentEventId;
        private const int MaxRetries = 5;
        private readonly int[] _retryDelays = { 5000, 10000, 15000, 20000, 25000 };
        private const string HubPath = "/hubs/karaoke-dj";

        public SignalRService(
            IUserSessionService userSessionService,
            Action<int, string, int?, bool?, bool, bool, bool> queueUpdatedCallback,
            Action<string, bool, bool, bool> singerStatusUpdatedCallback)
        {
            _userSessionService = userSessionService;
            _settingsService = SettingsService.Instance;
            _queueUpdatedCallback = queueUpdatedCallback;
            _singerStatusUpdatedCallback = singerStatusUpdatedCallback;
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
            string hubUrl = $"{apiUrl}{HubPath}";

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
#pragma warning disable CS8603
                            return Task.FromResult(token);
#pragma warning restore CS8603
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

                _connection.On<int, string, int?, bool?, bool, bool, bool>("QueueUpdated",
                    (queueId, action, position, isOnBreak, isSingerLoggedIn, isSingerJoined, isSingerOnBreak) =>
                    {
                        Log.Information("[SIGNALR] Received QueueUpdated for EventId={EventId}, QueueId={QueueId}, Action={Action}, Position={Position}, IsOnBreak={IsOnBreak}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                            _currentEventId, queueId, action, position, isOnBreak, isSingerLoggedIn, isSingerJoined, isSingerOnBreak);
                        _queueUpdatedCallback(queueId, action, position, isOnBreak, isSingerLoggedIn, isSingerJoined, isSingerOnBreak);
                    });
                _connection.On<string, bool, bool, bool>("SingerStatusUpdated", (requestorUserName, isLoggedIn, isJoined, isOnBreak) =>
                {
                    Log.Information("[SIGNALR] Received SingerStatusUpdated for EventId={EventId}, RequestorUserName={RequestorUserName}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}",
                        _currentEventId, requestorUserName, isLoggedIn, isJoined, isOnBreak);
                    _singerStatusUpdatedCallback(requestorUserName, isLoggedIn, isJoined, isOnBreak);
                });

                Log.Information("[SIGNALR] Subscribed to QueueUpdated and SingerStatusUpdated events for EventId={EventId}", eventId);

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