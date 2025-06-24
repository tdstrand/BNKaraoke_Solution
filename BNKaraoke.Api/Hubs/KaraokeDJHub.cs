using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using BNKaraoke.Api.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BNKaraoke.Api.Hubs
{
    [Authorize]
    public class KaraokeDJHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<KaraokeDJHub> _logger;

        public KaraokeDJHub(ApplicationDbContext context, ILogger<KaraokeDJHub> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task UpdateSingerStatus(string userId, int eventId, string displayName, bool isLoggedIn, bool isJoined, bool isOnBreak)
        {
            try
            {
                _logger.LogInformation("Broadcasting SingerStatusUpdated for UserId: {UserId}, EventId: {EventId}, ConnectionId: {ConnectionId}", userId, eventId, Context.ConnectionId);
                var response = new
                {
                    UserId = userId,
                    EventId = eventId,
                    DisplayName = displayName,
                    IsLoggedIn = isLoggedIn,
                    IsJoined = isJoined,
                    IsOnBreak = isOnBreak
                };
                await Clients.Group($"Event_{eventId}").SendAsync("SingerStatusUpdated", response);
                _logger.LogInformation("Successfully broadcasted SingerStatusUpdated for UserId: {UserId}, EventId: {EventId}, Group: Event_{EventId}", userId, eventId, eventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting SingerStatusUpdated for UserId: {UserId}, EventId: {EventId}, ConnectionId: {ConnectionId}", userId, eventId, Context.ConnectionId);
                throw;
            }
        }

        public async Task UpdateQueue(int queueId, int eventId, string action, string? youTubeUrl = null, string? holdReason = null)
        {
            try
            {
                _logger.LogInformation("Broadcasting QueueUpdated for QueueId: {QueueId}, EventId: {EventId}, Action: {Action}, ConnectionId: {ConnectionId}", queueId, eventId, action, Context.ConnectionId);
                var response = new
                {
                    QueueId = queueId,
                    EventId = eventId,
                    Action = action,
                    YouTubeUrl = youTubeUrl,
                    HoldReason = holdReason
                };
                await Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", response);
                _logger.LogInformation("Successfully broadcasted QueueUpdated for QueueId: {QueueId}, EventId: {EventId}, Group: Event_{EventId}", queueId, eventId, eventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting QueueUpdated for QueueId: {QueueId}, EventId: {EventId}, ConnectionId: {ConnectionId}", queueId, eventId, Context.ConnectionId);
                throw;
            }
        }

        public async Task QueuePlaying(int queueId, int eventId, string? youTubeUrl = null)
        {
            try
            {
                _logger.LogInformation("Broadcasting QueuePlaying for QueueId: {QueueId}, EventId: {EventId}, ConnectionId: {ConnectionId}", queueId, eventId, Context.ConnectionId);
                var response = new
                {
                    QueueId = queueId,
                    EventId = eventId,
                    YouTubeUrl = youTubeUrl
                };
                await Clients.Group($"Event_{eventId}").SendAsync("QueuePlaying", response);
                _logger.LogInformation("Successfully broadcasted QueuePlaying for QueueId: {QueueId}, EventId: {EventId}, Group: Event_{EventId}", queueId, eventId, eventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting QueuePlaying for QueueId: {QueueId}, EventId: {EventId}, ConnectionId: {ConnectionId}", queueId, eventId, Context.ConnectionId);
                throw;
            }
        }

        public async Task JoinEventGroup(int eventId)
        {
            const int maxRetries = 3;
            int retryCount = 0;
            bool joined = false;

            while (retryCount < maxRetries && !joined)
            {
                try
                {
                    _logger.LogInformation("Attempting to join group Event_{EventId} for ConnectionId: {ConnectionId}, Attempt: {Attempt}", eventId, Context.ConnectionId, retryCount + 1);
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"Event_{eventId}");
                    joined = true;
                    _logger.LogInformation("Successfully joined group Event_{EventId} for ConnectionId: {ConnectionId}", eventId, Context.ConnectionId);
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, "Failed to join group Event_{EventId} for ConnectionId: {ConnectionId}, Attempt: {Attempt}", eventId, Context.ConnectionId, retryCount);
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError(ex, "Max retries reached for joining group Event_{EventId} for ConnectionId: {ConnectionId}", eventId, Context.ConnectionId);
                        throw;
                    }
                    await Task.Delay(5000); // 5-second delay between retries
                }
            }
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected with ConnectionId: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogWarning(exception, "Client disconnected with ConnectionId: {ConnectionId}", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Client disconnected with ConnectionId: {ConnectionId}", Context.ConnectionId);
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}