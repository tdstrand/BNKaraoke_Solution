using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Models;
using BNKaraoke.Api.Services;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

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
                Dictionary<string, SingerStatusData> singerStatuses = new(StringComparer.OrdinalIgnoreCase);
                List<EventQueueDto> queue = new();
                Dictionary<string, ApplicationUser> users = new(StringComparer.OrdinalIgnoreCase);

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
                        var userEntities = await _context.Users
                            .Where(u => u.UserName != null && requestorUserNames.Contains(u.UserName))
                            .ToListAsync();
                        users = userEntities
                            .Where(u => u.UserName != null)
                            .ToDictionary(u => u.UserName!, StringComparer.OrdinalIgnoreCase);

                        var singerStatusSnapshots = await _context.SingerStatus
                            .Where(ss => ss.EventId == eventIdInt)
                            .Join(_context.Users, ss => ss.RequestorId, u => u.Id,
                                (ss, u) => new SingerStatusData(
                                    u.UserName ?? string.Empty,
                                    $"{u.FirstName} {u.LastName}".Trim(),
                                    ss.IsLoggedIn,
                                    ss.IsJoined,
                                    ss.IsOnBreak))
                            .Where(snapshot => !string.IsNullOrEmpty(snapshot.UserName))
                            .ToListAsync();

                        singerStatuses = singerStatusSnapshots
                            .GroupBy(snapshot => snapshot.UserName, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(
                                group => group.Key,
                                group => group.First(),
                                StringComparer.OrdinalIgnoreCase);

                        queue = queueEntries.Select(eq =>
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

                            singerStatuses.TryGetValue(eq.RequestorUserName, out var status);

                            var holdReason = string.Empty;
                            if (status == null || !status.IsJoined)
                                holdReason = "NotJoined";
                            else if (!status.IsLoggedIn)
                                holdReason = "NotLoggedIn";
                            else if (status.IsOnBreak || eq.IsOnBreak)
                                holdReason = "OnBreak";

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
                                HoldReason = holdReason,
                                IsUpNext = false,
                                IsSingerLoggedIn = status?.IsLoggedIn ?? false,
                                IsSingerJoined = status?.IsJoined ?? false,
                                IsSingerOnBreak = status?.IsOnBreak ?? false,
                                IsServerCached = eq.Song?.Cached ?? false,
                                IsMature = eq.Song?.Mature ?? false,
                                NormalizationGain = eq.Song?.NormalizationGain,
                                FadeStartTime = eq.Song?.FadeStartTime,
                                IntroMuteDuration = eq.Song?.IntroMuteDuration
                            };
                        }).ToList();

                        await Clients.Caller.SendAsync("InitialQueue", queue);
                        _logger.LogInformation("Sent initial queue data for EventId={EventId} to UserName={UserName}: {QueueCount} items in {TotalElapsedMilliseconds} ms", eventId, userName, queue.Count, sw.ElapsedMilliseconds);

                        var placeholderCount = 0;
                        var queueV2 = new List<DJQueueItemDto>(queue.Count);
                        foreach (var queueItem in queue)
                        {
                            singerStatuses.TryGetValue(queueItem.RequestorUserName, out var snapshot);
                            if (snapshot == null)
                            {
                                placeholderCount++;
                            }

                            var singerDto = DJQueueItemBuilder.BuildSingerStatus(queueItem, snapshot, users);
                            var queueItemV2 = DJQueueItemBuilder.BuildDjQueueItem(queueItem, singerDto);
                            queueV2.Add(queueItemV2);
                        }

                        await Clients.Caller.SendAsync("InitialQueueV2", queueV2);
                        _logger.LogInformation(
                            "Sent InitialQueueV2 for EventId={EventId} to UserName={UserName}: {QueueCount} items (placeholders synthesized={PlaceholderCount})",
                            eventId,
                            userName,
                            queueV2.Count,
                            placeholderCount);
                        _logger.LogInformation(
                            "Initial queue counts for EventId={EventId}: Legacy={LegacyCount}, V2={V2Count}",
                            eventId,
                            queue.Count,
                            queueV2.Count);
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

                        var singerRoster = new Dictionary<string, SingerStatusDto>(StringComparer.OrdinalIgnoreCase);
                        foreach (var snapshot in singerStatuses.Values)
                        {
                            var dto = new SingerStatusDto
                            {
                                UserId = snapshot.UserName,
                                DisplayName = !string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.DisplayName : snapshot.UserName,
                                IsLoggedIn = snapshot.IsLoggedIn,
                                IsJoined = snapshot.IsJoined,
                                IsOnBreak = snapshot.IsOnBreak,
                            };
                            dto.Flags = DJQueueItemBuilder.BuildFlags(dto);
                            singerRoster[snapshot.UserName] = dto;
                        }

                        var singerPlaceholderCount = 0;
                        foreach (var queueItem in queue)
                        {
                            if (singerRoster.ContainsKey(queueItem.RequestorUserName))
                            {
                                var existing = singerRoster[queueItem.RequestorUserName];
                                if (queueItem.IsOnBreak && !existing.IsOnBreak)
                                {
                                    existing.IsOnBreak = true;
                                    existing.Flags |= SingerStatusFlags.OnBreak;
                                }
                                if (string.IsNullOrWhiteSpace(existing.DisplayName))
                                {
                                    existing.DisplayName = queueItem.RequestorFullName ?? queueItem.RequestorUserName;
                                }
                                continue;
                            }

                            var placeholder = DJQueueItemBuilder.BuildSingerStatus(queueItem, null, users);
                            placeholder.Flags = DJQueueItemBuilder.BuildFlags(placeholder);
                            singerRoster[queueItem.RequestorUserName] = placeholder;
                            singerPlaceholderCount++;
                        }

                        await Clients.Caller.SendAsync("InitialSingersV2", singerRoster.Values.ToList());
                        _logger.LogInformation(
                            "Sent InitialSingersV2 for EventId={EventId} to UserName={UserName}: {SingerCount} singers (placeholders synthesized={PlaceholderCount})",
                            eventId,
                            userName,
                            singerRoster.Count,
                            singerPlaceholderCount);
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
                    userName = userId,
                    eventId,
                    displayName,
                    isLoggedIn,
                    isJoined,
                    isOnBreak
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
                var payload = new
                {
                    queueId,
                    eventId,
                    youTubeUrl,
                    holdReason
                };
                await Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = payload, action });
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
