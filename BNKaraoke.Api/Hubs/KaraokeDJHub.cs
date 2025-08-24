using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using System.Linq;
using System.Text.Json;

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

        public override async Task OnConnectedAsync()
        {
            var userName = Context.User?.Identity?.Name ?? "Unknown";
            var eventId = Context.GetHttpContext()?.Request.Query["eventId"].ToString();

            _logger.LogInformation("Client connected with ConnectionId: {ConnectionId}, UserName: {UserName}, EventId: {EventId}", Context.ConnectionId, userName, eventId ?? "None");

            if (string.IsNullOrEmpty(eventId))
            {
                _logger.LogWarning("OnConnectedAsync: No eventId provided for UserName={UserName}, ConnectionId: {ConnectionId}", userName, Context.ConnectionId);
            }
            else
            {
                const int maxRetries = 3;
                int retryCount = 0;
                bool joined = false;

                while (retryCount < maxRetries && !joined)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to join group Event_{EventId} for UserName={UserName}, ConnectionId: {ConnectionId}, Attempt: {Attempt}", eventId, userName, Context.ConnectionId, retryCount + 1);
                        await Groups.AddToGroupAsync(Context.ConnectionId, $"Event_{eventId}");
                        joined = true;
                        _logger.LogInformation("Successfully joined group Event_{EventId} for UserName={UserName}, ConnectionId: {ConnectionId}", eventId, userName, Context.ConnectionId);
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "Failed to join group Event_{EventId} for UserName={UserName}, ConnectionId: {ConnectionId}, Attempt: {Attempt}", eventId, userName, Context.ConnectionId, retryCount);
                        if (retryCount >= maxRetries)
                        {
                            _logger.LogError(ex, "Max retries reached for joining group Event_{EventId} for UserName={UserName}, ConnectionId: {ConnectionId}", eventId, userName, Context.ConnectionId);
                            throw;
                        }
                        await Task.Delay(5000);
                    }
                }

                // Send initial queue data
                try
                {
                    if (int.TryParse(eventId, out int eventIdInt))
                    {
                        var sw = Stopwatch.StartNew();
                        var queueEntries = await _context.EventQueues
                            .AsNoTracking()
                            .Where(eq => eq.EventId == eventIdInt && eq.IsActive)
                            .Include(eq => eq.Song)
                            .OrderBy(eq => eq.Position)
                            .ToListAsync();
                        _logger.LogInformation("OnConnectedAsync: EventQueues query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);

                        var requestorUserNames = queueEntries.Select(eq => eq.RequestorUserName).Distinct().ToList();
                        var users = await _context.Users.Where(u => u.UserName != null && requestorUserNames.Contains(u.UserName)).ToDictionaryAsync(u => u.UserName!);

                        var queue = queueEntries.Select(eq =>
                        {
                            var singersList = new List<string>();
                            try
                            {
                                singersList.AddRange(JsonSerializer.Deserialize<List<string>>(eq.Singers) ?? new List<string>());
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", eq.QueueId, ex.Message);
                            }

                            return new EventQueueDto
                            {
                                QueueId = eq.QueueId,
                                EventId = eq.EventId,
                                SongId = eq.SongId,
                                SongTitle = eq.Song?.Title ?? string.Empty,
                                SongArtist = eq.Song?.Artist ?? string.Empty,
                                YouTubeUrl = eq.Song?.YouTubeUrl,
                                RequestorUserName = eq.RequestorUserName,
                                RequestorFullName = users.ContainsKey(eq.RequestorUserName) ? $"{users[eq.RequestorUserName].FirstName} {users[eq.RequestorUserName].LastName}".Trim() : eq.RequestorUserName,
                                Singers = singersList,
                                Position = eq.Position,
                                Status = eq.Status,
                                IsActive = eq.IsActive,
                                WasSkipped = eq.WasSkipped,
                                IsCurrentlyPlaying = eq.IsCurrentlyPlaying,
                                SungAt = eq.SungAt,
                                IsOnBreak = eq.IsOnBreak,
                                HoldReason = "None", // Adjust as needed
                                IsUpNext = false,
                                IsSingerLoggedIn = false, // Fetch if needed
                                IsSingerJoined = false,
                                IsSingerOnBreak = false
                            };
                        }).ToList();

                        await Clients.Caller.SendAsync("InitialQueue", queue);
                        _logger.LogInformation("Sent initial queue data for EventId={EventId} to UserName={UserName}: {QueueCount} items in {TotalElapsedMilliseconds} ms", eventId, userName, queue.Count, sw.ElapsedMilliseconds);
                    }
                    else
                    {
                        _logger.LogWarning("OnConnectedAsync: Invalid eventId format for UserName={UserName}, EventId={EventId}", userName, eventId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending initial queue data for EventId={EventId}, UserName={UserName}", eventId, userName);
                }

                // Send initial singers data
                try
                {
                    if (int.TryParse(eventId, out int eventIdInt))
                    {
                        var singers = await _context.SingerStatus.Where(ss => ss.EventId == eventIdInt)
                            .Join(_context.Users, ss => ss.RequestorId, u => u.Id, (ss, u) => new DJSingerDto
                            {
                                UserId = u.UserName ?? "",
                                DisplayName = $"{u.FirstName} {u.LastName}".Trim(),
                                IsLoggedIn = ss.IsLoggedIn,
                                IsJoined = ss.IsJoined,
                                IsOnBreak = ss.IsOnBreak
                            }).ToListAsync();
                        await Clients.Caller.SendAsync("InitialSingers", singers);
                        _logger.LogInformation("Sent initial singers data for EventId={EventId} to UserName={UserName}: {SingerCount} items", eventId, userName, singers.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending initial singers data for EventId={EventId}, UserName={UserName}", eventId, userName);
                }
            }

            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userName = Context.User?.Identity?.Name ?? "Unknown";
            var eventId = Context.GetHttpContext()?.Request.Query["eventId"].ToString();
            if (!string.IsNullOrEmpty(eventId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Event_{eventId}");
            }
            _logger.LogInformation("Client disconnected with ConnectionId: {ConnectionId}, UserName: {UserName}, EventId: {EventId}, Reason: {Exception}", Context.ConnectionId, userName, eventId ?? "None", exception?.Message ?? "None");
            await base.OnDisconnectedAsync(exception);
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
                    await Task.Delay(5000);
                }
            }
        }
    }
}