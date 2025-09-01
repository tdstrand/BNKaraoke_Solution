using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Hubs;
using BNKaraoke.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Transactions;

namespace BNKaraoke.Api.Controllers
{
    [Route("api/dj")]
    [ApiController]
    [Authorize]
    public class DJController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DJController> _logger;
        private readonly IHubContext<KaraokeDJHub> _hubContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private static readonly Dictionary<int, string> _holdReasons = new();

        public DJController(
            ApplicationDbContext context,
            ILogger<DJController> logger,
            IHubContext<KaraokeDJHub> hubContext,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _logger = logger;
            _hubContext = hubContext;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("{eventId}/attendance/check-in")]
        [Authorize(Roles = "Karaoke DJ,Singer")]
        public async Task<IActionResult> CheckIn(int eventId, [FromBody] CheckInDto request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.RequestorUserName))
                {
                    _logger.LogWarning("[DJController] RequestorUserName is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("RequestorUserName cannot be null or empty");
                }
                _logger.LogInformation("[DJController] Checking in for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserName == request.RequestorUserName);
                if (user == null)
                {
                    _logger.LogWarning("[DJController] User not found with UserName: {UserName}", request.RequestorUserName);
                    return NotFound("User not found");
                }
                var singerStatus = await _context.SingerStatus
                    .FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == user.Id);
                if (singerStatus == null)
                {
                    singerStatus = new SingerStatus
                    {
                        EventId = eventId,
                        RequestorId = user.Id,
                        IsLoggedIn = true,
                        IsJoined = true,
                        IsOnBreak = false,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.SingerStatus.Add(singerStatus);
                }
                else
                {
                    singerStatus.IsLoggedIn = true;
                    singerStatus.IsJoined = true;
                    singerStatus.IsOnBreak = false;
                    singerStatus.UpdatedAt = DateTime.UtcNow;
                }
                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == user.Id);
                if (attendance == null)
                {
                    attendance = new EventAttendance
                    {
                        EventId = eventId,
                        RequestorId = user.Id,
                        IsCheckedIn = true,
                        IsOnBreak = false
                    };
                    _context.EventAttendances.Add(attendance);
                }
                else
                {
                    attendance.IsCheckedIn = true;
                    attendance.IsOnBreak = false;
                    attendance.BreakStartAt = null;
                    attendance.BreakEndAt = null;
                }
                await _context.SaveChangesAsync();
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.RequestorUserName == request.RequestorUserName && eq.SungAt == null)
                    .ToListAsync();
                foreach (var entry in queueEntries)
                {
                    _holdReasons.Remove(entry.QueueId);
                    if (string.IsNullOrEmpty(entry.Singers))
                    {
                        entry.Singers = $"[\"{request.RequestorUserName}\"]";
                    }
                    entry.IsActive = true;
                    entry.Status = "Live";
                    entry.UpdatedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
                var response = new DJSingerDto
                {
                    UserId = user.UserName ?? string.Empty,
                    DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                    IsLoggedIn = true,
                    IsJoined = true,
                    IsOnBreak = false
                };
                try
                {
                    _logger.LogDebug("[DJController] Sending SingerStatusUpdated to group Event_{EventId} for RequestorUserName={RequestorUserName}", eventId, request.RequestorUserName);
                    await _hubContext.Clients.Group($"Event_{eventId}")
                        .SendAsync("SingerStatusUpdated", new { userName = request.RequestorUserName, eventId, isLoggedIn = true, isJoined = true, isOnBreak = false });
                    _logger.LogInformation("[DJController] Successfully sent SingerStatusUpdated for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                }
                catch (Exception signalREx)
                {
                    _logger.LogError(signalREx, "[DJController] Failed to send SingerStatusUpdated for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                }
                _logger.LogInformation("[DJController] Checked in successfully for EventId: {EventId}, UserId: {UserId}", eventId, user.Id);
                return Ok(new
                {
                    message = "Checked in successfully",
                    singerStatus = response
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error checking in for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                return StatusCode(500, new { message = "Error checking in", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/attendance/check-out")]
        [Authorize(Roles = "Karaoke DJ,Singer")]
        public async Task<IActionResult> CheckOut(int eventId, [FromBody] CheckInDto request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.RequestorUserName))
                {
                    _logger.LogWarning("[DJController] RequestorUserName is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("RequestorUserName cannot be null or empty");
                }
                _logger.LogInformation("[DJController] Checking out for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserName == request.RequestorUserName);
                if (user == null)
                {
                    _logger.LogWarning("[DJController] User not found with UserName: {UserName}", request.RequestorUserName);
                    return NotFound("User not found");
                }
                var singerStatus = await _context.SingerStatus
                    .FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == user.Id);
                if (singerStatus != null)
                {
                    singerStatus.IsLoggedIn = false;
                    singerStatus.IsJoined = false;
                    singerStatus.IsOnBreak = false;
                    singerStatus.UpdatedAt = DateTime.UtcNow;
                }
                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == user.Id);
                if (attendance != null)
                {
                    attendance.IsCheckedIn = false;
                    attendance.IsOnBreak = false;
                    attendance.BreakStartAt = null;
                    attendance.BreakEndAt = null;
                }
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.RequestorUserName == request.RequestorUserName && eq.SungAt == null)
                    .ToListAsync();
                foreach (var entry in queueEntries)
                {
                    _holdReasons[entry.QueueId] = "NotJoined";
                    entry.IsActive = false;
                    entry.UpdatedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
                var response = new DJSingerDto
                {
                    UserId = user.UserName ?? string.Empty,
                    DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                    IsLoggedIn = false,
                    IsJoined = false,
                    IsOnBreak = false
                };
                try
                {
                    _logger.LogDebug("[DJController] Sending SingerStatusUpdated to group Event_{EventId} for RequestorUserName={RequestorUserName}", eventId, request.RequestorUserName);
                    await _hubContext.Clients.Group($"Event_{eventId}")
                        .SendAsync("SingerStatusUpdated", new { userName = request.RequestorUserName, eventId, isLoggedIn = false, isJoined = false, isOnBreak = false });
                    _logger.LogInformation("[DJController] Successfully sent SingerStatusUpdated for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                }
                catch (Exception signalREx)
                {
                    _logger.LogError(signalREx, "[DJController] Failed to send SingerStatusUpdated for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                }
                _logger.LogInformation("[DJController] Checked out successfully for EventId: {EventId}, UserId: {UserId}", eventId, user.Id);
                return Ok(new { message = "Checked out successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error checking out for EventId: {EventId}, RequestorUserName: {RequestorUserName}", eventId, request.RequestorUserName);
                return StatusCode(500, new { message = "Error checking out", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/queue/{queueId}/play")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> PlaySong(int eventId, int queueId)
        {
            try
            {
                _logger.LogInformation("[DJController] Playing song for EventId: {EventId}, QueueId: {QueueId}", eventId, queueId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("[DJController] Queue entry not found with QueueId: {QueueId} for EventId: {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                var singerStatus = user != null
                    ? await _context.SingerStatus.FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == user.Id)
                    : null;
                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE public.\"EventQueues\" SET \"IsCurrentlyPlaying\" = FALSE, \"UpdatedAt\" = @p0 WHERE \"EventId\" = @p1 AND \"IsCurrentlyPlaying\" = TRUE",
                        DateTime.UtcNow, eventId);
                    queueEntry.IsCurrentlyPlaying = true;
                    _holdReasons.Remove(queueId);
                    queueEntry.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    scope.Complete();
                }
                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<List<string>>(queueEntry.Singers) ?? new List<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueId, ex.Message);
                }
                var queueDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = queueEntry.Song?.YouTubeUrl,
                    RequestorUserName = queueEntry.RequestorUserName,
                    RequestorFullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "",
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = queueEntry.Status,
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak,
                    HoldReason = string.Empty,
                    IsUpNext = false,
                    IsSingerLoggedIn = singerStatus?.IsLoggedIn ?? false,
                    IsSingerJoined = singerStatus?.IsJoined ?? false,
                    IsSingerOnBreak = singerStatus?.IsOnBreak ?? false,
                    IsServerCached = queueEntry.Song?.Cached ?? false,
                    IsMature = queueEntry.Song?.Mature ?? false
                };
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = queueDto, action = "Playing" });
                _logger.LogInformation("[DJController] Started play for QueueId: {QueueId} for EventId: {EventId}", queueId, eventId);
                return Ok(new { message = "Song play started", QueueId = queueId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error starting play for EventId: {EventId}, QueueId: {QueueId}", eventId, queueId);
                return StatusCode(500, new { message = "Error starting song play", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/queue/{queueId}/skipped")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> SkipSong(int eventId, int queueId)
        {
            try
            {
                _logger.LogInformation("[DJController] Skipping song for EventId: {EventId}, QueueId: {QueueId}", eventId, queueId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("[DJController] Queue entry not found with QueueId: {QueueId} for EventId: {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                var singerStatus = user != null
                    ? await _context.SingerStatus.FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == user.Id)
                    : null;
                queueEntry.WasSkipped = true;
                queueEntry.IsCurrentlyPlaying = false;
                queueEntry.SungAt = DateTime.UtcNow;
                queueEntry.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<List<string>>(queueEntry.Singers) ?? new List<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueId, ex.Message);
                }
                var queueDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = queueEntry.Song?.YouTubeUrl,
                    RequestorUserName = queueEntry.RequestorUserName,
                    RequestorFullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "",
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = queueEntry.Status,
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak,
                    HoldReason = string.Empty,
                    IsUpNext = false,
                    IsSingerLoggedIn = singerStatus?.IsLoggedIn ?? false,
                    IsSingerJoined = singerStatus?.IsJoined ?? false,
                    IsSingerOnBreak = singerStatus?.IsOnBreak ?? false,
                    IsServerCached = queueEntry.Song?.Cached ?? false,
                    IsMature = queueEntry.Song?.Mature ?? false
                };
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = queueDto, action = "Skipped" });
                _logger.LogInformation("[DJController] Skipped song with QueueId: {QueueId} for EventId: {EventId}", queueId, eventId);
                return Ok(new { message = "Song skipped" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error skipping song for EventId: {EventId}, QueueId: {QueueId}", eventId, queueId);
                return StatusCode(500, new { message = "Error skipping song", details = ex.Message });
            }
        }

        [HttpPost("queue/now-playing")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> SetNowPlaying([FromBody] NowPlayingDto request)
        {
            try
            {
                _logger.LogInformation("[DJController] Setting now playing for QueueId: {QueueId}", request.QueueId);
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.QueueId == request.QueueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("[DJController] Queue entry not found with QueueId: {QueueId}", request.QueueId);
                    return NotFound("Queue entry not found");
                }
                var eventEntity = await _context.Events.FindAsync(queueEntry.EventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", queueEntry.EventId);
                    return NotFound("Event not found or not live");
                }
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                var singerStatus = user != null
                    ? await _context.SingerStatus.FirstOrDefaultAsync(ss => ss.EventId == queueEntry.EventId && ss.RequestorId == user.Id)
                    : null;
                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE public.\"EventQueues\" SET \"IsCurrentlyPlaying\" = FALSE, \"UpdatedAt\" = @p0 WHERE \"EventId\" = @p1 AND \"IsCurrentlyPlaying\" = TRUE",
                        DateTime.UtcNow, queueEntry.EventId);
                    queueEntry.IsCurrentlyPlaying = true;
                    _holdReasons.Remove(request.QueueId);
                    queueEntry.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    scope.Complete();
                }
                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<List<string>>(queueEntry.Singers) ?? new List<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", request.QueueId, ex.Message);
                }
                var queueDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = queueEntry.Song?.YouTubeUrl,
                    RequestorUserName = queueEntry.RequestorUserName,
                    RequestorFullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "",
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = queueEntry.Status,
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak,
                    HoldReason = string.Empty,
                    IsUpNext = false,
                    IsSingerLoggedIn = singerStatus?.IsLoggedIn ?? false,
                    IsSingerJoined = singerStatus?.IsJoined ?? false,
                    IsSingerOnBreak = singerStatus?.IsOnBreak ?? false,
                    IsServerCached = queueEntry.Song?.Cached ?? false,
                    IsMature = queueEntry.Song?.Mature ?? false
                };
                await _hubContext.Clients.Group($"Event_{queueEntry.EventId}").SendAsync("QueueUpdated", new { data = queueDto, action = "Playing" });
                _logger.LogInformation("[DJController] Set now playing for QueueId: {QueueId}, EventId: {EventId}", request.QueueId, queueEntry.EventId);
                return Ok(new { message = "Song set as now playing", QueueId = request.QueueId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error setting now playing for QueueId: {QueueId}", request.QueueId);
                return StatusCode(500, new { message = "Error setting now playing", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/autoplay/next")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> AutoplayNext(int eventId, [FromQuery] bool simplified = false)
        {
            try
            {
                _logger.LogInformation("[DJController] Selecting next song for autoplay in EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.Status == "Live" && eq.SungAt == null && !eq.WasSkipped && !eq.IsCurrentlyPlaying)
                    .Include(eq => eq.Song)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();
                EventQueue? nextEntry = null;
                foreach (var entry in queueEntries)
                {
                    var singers = JsonSerializer.Deserialize<string[]>(entry.Singers) ?? Array.Empty<string>();
                    bool allSingersAvailable = true;
                    string holdReason = string.Empty;
                    foreach (var singer in singers)
                    {
                        if (singer == "AllSing" || singer == "TheBoys" || singer == "TheGirls")
                            continue;
                        var singerUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == singer);
                        if (singerUser == null)
                        {
                            if (!simplified)
                            {
                                holdReason = "NotJoined";
                                allSingersAvailable = false;
                                break;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        var singerStatus = await _context.SingerStatus
                            .FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == singerUser.Id);
                        if (simplified)
                        {
                            if (singerStatus != null && singerStatus.IsOnBreak)
                            {
                                holdReason = "OnBreak";
                                allSingersAvailable = false;
                                break;
                            }
                        }
                        else
                        {
                            if (singerStatus == null || !singerStatus.IsLoggedIn || !singerStatus.IsJoined)
                            {
                                holdReason = singerStatus == null ? "NotJoined" : "NotLoggedIn";
                                allSingersAvailable = false;
                                break;
                            }
                            else if (singerStatus.IsOnBreak)
                            {
                                holdReason = "OnBreak";
                                allSingersAvailable = false;
                                break;
                            }
                        }
                    }
                    if (allSingersAvailable)
                    {
                        nextEntry = entry;
                        break;
                    }
                    else
                    {
                        entry.IsOnBreak = true;
                        entry.UpdatedAt = DateTime.UtcNow;
                        _holdReasons[entry.QueueId] = holdReason;
                        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == entry.RequestorUserName);
                        var singerStatus = user != null
                            ? await _context.SingerStatus.FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == user.Id)
                            : null;
                        var singersList = new List<string>();
                        try
                        {
                            singersList.AddRange(JsonSerializer.Deserialize<List<string>>(entry.Singers) ?? new List<string>());
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", entry.QueueId, ex.Message);
                        }
                        var queueDto = new EventQueueDto
                        {
                            QueueId = entry.QueueId,
                            EventId = entry.EventId,
                            SongId = entry.SongId,
                            SongTitle = entry.Song?.Title ?? string.Empty,
                            SongArtist = entry.Song?.Artist ?? string.Empty,
                            YouTubeUrl = entry.Song?.YouTubeUrl,
                            RequestorUserName = entry.RequestorUserName,
                            RequestorFullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "",
                            Singers = singersList,
                            Position = entry.Position,
                            Status = entry.Status,
                            IsActive = entry.IsActive,
                            WasSkipped = entry.WasSkipped,
                            IsCurrentlyPlaying = entry.IsCurrentlyPlaying,
                            SungAt = entry.SungAt,
                            IsOnBreak = entry.IsOnBreak,
                            HoldReason = holdReason,
                            IsUpNext = false,
                            IsSingerLoggedIn = singerStatus?.IsLoggedIn ?? false,
                            IsSingerJoined = singerStatus?.IsJoined ?? false,
                            IsSingerOnBreak = singerStatus?.IsOnBreak ?? false,
                            IsServerCached = entry.Song?.Cached ?? false,
                            IsMature = entry.Song?.Mature ?? false
                        };
                        await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = queueDto, action = "OnHold" });
                    }
                }
                if (nextEntry == null)
                {
                    _logger.LogInformation("[DJController] No eligible songs for autoplay in EventId: {EventId}", eventId);
                    return NotFound(new { message = "No eligible songs" });
                }
                var nextUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == nextEntry.RequestorUserName);
                var nextSingerStatus = nextUser != null
                    ? await _context.SingerStatus.FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == nextUser.Id)
                    : null;
                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE public.\"EventQueues\" SET \"IsCurrentlyPlaying\" = FALSE, \"UpdatedAt\" = @p0 WHERE \"EventId\" = @p1 AND \"IsCurrentlyPlaying\" = TRUE",
                        DateTime.UtcNow, eventId);
                    nextEntry.IsCurrentlyPlaying = true;
                    _holdReasons.Remove(nextEntry.QueueId);
                    nextEntry.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    scope.Complete();
                }
                var nextSingersList = new List<string>();
                try
                {
                    nextSingersList.AddRange(JsonSerializer.Deserialize<List<string>>(nextEntry.Singers) ?? new List<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", nextEntry.QueueId, ex.Message);
                }
                var nextQueueDto = new EventQueueDto
                {
                    QueueId = nextEntry.QueueId,
                    EventId = nextEntry.EventId,
                    SongId = nextEntry.SongId,
                    SongTitle = nextEntry.Song?.Title ?? string.Empty,
                    SongArtist = nextEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = nextEntry.Song?.YouTubeUrl,
                    RequestorUserName = nextEntry.RequestorUserName,
                    RequestorFullName = nextUser != null ? $"{nextUser.FirstName} {nextUser.LastName}".Trim() : "",
                    Singers = nextSingersList,
                    Position = nextEntry.Position,
                    Status = nextEntry.Status,
                    IsActive = nextEntry.IsActive,
                    WasSkipped = nextEntry.WasSkipped,
                    IsCurrentlyPlaying = nextEntry.IsCurrentlyPlaying,
                    SungAt = nextEntry.SungAt,
                    IsOnBreak = nextEntry.IsOnBreak,
                    HoldReason = string.Empty,
                    IsUpNext = false,
                    IsSingerLoggedIn = nextSingerStatus?.IsLoggedIn ?? false,
                    IsSingerJoined = nextSingerStatus?.IsJoined ?? false,
                    IsSingerOnBreak = nextSingerStatus?.IsOnBreak ?? false,
                    IsServerCached = nextEntry.Song?.Cached ?? false,
                    IsMature = nextEntry.Song?.Mature ?? false
                };
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = nextQueueDto, action = "Playing" });
                _logger.LogInformation("[DJController] Selected next song for autoplay: QueueId: {QueueId}, EventId: {EventId}", nextEntry.QueueId, eventId);
                return Ok(new
                {
                    QueueId = nextEntry.QueueId,
                    SongId = nextEntry.SongId,
                    SongTitle = nextEntry.Song?.Title ?? string.Empty,
                    message = "Next song selected"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error selecting next song for autoplay in EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error selecting next song", details = ex.Message });
            }
        }

        [HttpGet("queue/unplayed")]
        public async Task<IActionResult> GetUnplayedQueue(int eventId)
        {
            try
            {
                _logger.LogInformation("[DJController] Fetching unplayed queue for EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.Status == "Live" && eq.SungAt == null && !eq.WasSkipped && !eq.IsCurrentlyPlaying)
                    .Include(eq => eq.Song)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();
                var queueDtos = await BuildQueueDtos(queueEntries, eventId);
                if (queueDtos.Any())
                {
                    var upNextEntry = queueDtos.FirstOrDefault(dto => dto.IsSingerLoggedIn && dto.IsSingerJoined && !dto.IsSingerOnBreak);
                    if (upNextEntry != null)
                    {
                        upNextEntry.IsUpNext = true;
                    }
                }
                foreach (var dto in queueDtos)
                {
                    _logger.LogDebug("[DJController] Unplayed queue entry for EventId={EventId}: QueueId={QueueId}, RequestorUserName={RequestorUserName}, IsOnBreak={IsOnBreak}, HoldReason={HoldReason}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}, IsUpNext={IsUpNext}",
                        eventId, dto.QueueId, dto.RequestorUserName, dto.IsOnBreak, dto.HoldReason, dto.IsSingerLoggedIn, dto.IsSingerJoined, dto.IsSingerOnBreak, dto.IsUpNext);
                }
                _logger.LogInformation("[DJController] Fetched {Count} unplayed queue entries for EventId: {EventId}", queueDtos.Count, eventId);
                return Ok(queueDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error fetching unplayed queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error retrieving unplayed queue", details = ex.Message });
            }
        }

        [HttpGet("queue/now-playing")]
        public async Task<IActionResult> GetNowPlayingQueue(int eventId)
        {
            try
            {
                _logger.LogInformation("[DJController] Fetching now playing queue for EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntry = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.IsCurrentlyPlaying)
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync();
                if (queueEntry == null)
                {
                    _logger.LogInformation("[DJController] No currently playing song for EventId: {EventId}", eventId);
                    return Ok(null);
                }
                var queueDtos = await BuildQueueDtos(new[] { queueEntry }, eventId);
                var queueDto = queueDtos.FirstOrDefault();
                _logger.LogInformation("[DJController] Fetched now playing queue entry for EventId: {EventId}", eventId);
                return Ok(queueDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error fetching now playing queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error retrieving now playing queue", details = ex.Message });
            }
        }

        [HttpGet("queue/completed")]
        public async Task<IActionResult> GetCompletedQueue(int eventId)
        {
            try
            {
                _logger.LogInformation("[DJController] Fetching completed queue for EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && (eq.SungAt != null || eq.WasSkipped))
                    .Include(eq => eq.Song)
                    .OrderByDescending(eq => eq.SungAt ?? DateTime.UtcNow)
                    .ToListAsync();
                var queueDtos = await BuildQueueDtos(queueEntries, eventId);
                _logger.LogInformation("[DJController] Fetched {Count} completed queue entries for EventId: {EventId}", queueDtos.Count, eventId);
                return Ok(queueDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error fetching completed queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error retrieving completed queue", details = ex.Message });
            }
        }

        [HttpGet("queue/all")]
        public async Task<IActionResult> GetAllQueue(int eventId)
        {
            try
            {
                _logger.LogInformation("[DJController] Fetching all queue for EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FirstOrDefaultAsync(e => e.EventId == eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .Include(eq => eq.Song)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();
                var queueDtos = await BuildQueueDtos(queueEntries, eventId);
                if (queueDtos.Any(q => q.Status == "Unplayed"))
                {
                    var upNextEntry = queueDtos.FirstOrDefault(q => q.Status == "Unplayed" && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak);
                    if (upNextEntry != null)
                    {
                        upNextEntry.IsUpNext = true;
                    }
                }
                _logger.LogInformation("[DJController] Fetched {Count} queue entries for EventId: {EventId}", queueDtos.Count, eventId);
                return Ok(queueDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error fetching all queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error retrieving all queue", details = ex.Message });
            }
        }

        [HttpGet("events/{eventId}/queue")]
        public async Task<IActionResult> GetEventQueue(int eventId)
        {
            try
            {
                _logger.LogInformation("[DJController] Fetching event queue for EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FirstOrDefaultAsync(e => e.EventId == eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .Include(eq => eq.Song)
                    .ToListAsync();
                var queueDtos = await BuildQueueDtos(queueEntries, eventId);
                if (queueDtos.Any(q => q.Status == "Unplayed"))
                {
                    var upNextEntry = queueDtos.FirstOrDefault(q => q.Status == "Unplayed" && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak);
                    if (upNextEntry != null)
                    {
                        upNextEntry.IsUpNext = true;
                    }
                }
                _logger.LogInformation("[DJController] Fetched {Count} queue entries for EventId: {EventId}", queueDtos.Count, eventId);
                return Ok(new { QueueEntries = queueDtos, SongsCompleted = eventEntity.SongsCompleted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error fetching event queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error retrieving event queue", details = ex.Message });
            }
        }

        [HttpGet("events/{eventId}/singers")]
        public async Task<IActionResult> GetSingers(int eventId, int page = 1, int size = 100)
        {
            try
            {
                _logger.LogInformation("[DJController] Fetching singers for EventId: {EventId}, Page: {Page}, Size: {Size}", eventId, page, size);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", eventId);
                    return NotFound("Event not found or not live");
                }
                var timeoutSetting = await _context.ApiSettings
                    .FirstOrDefaultAsync(s => s.SettingKey == "ActivityTimeoutMinutes");
                int timeoutMinutes = timeoutSetting != null && int.TryParse(timeoutSetting.SettingValue, out var parsedTimeout) ? parsedTimeout : 30;
                var singerStatuses = await _context.SingerStatus
                    .Where(ss => ss.EventId == eventId)
                    .Join(_context.Users,
                        ss => ss.RequestorId,
                        u => u.Id,
                        (ss, u) => new
                        {
                            UserId = u.UserName ?? string.Empty,
                            DisplayName = (u.FirstName + " " + u.LastName).Trim(),
                            IsLoggedIn = ss.IsLoggedIn,
                            IsJoined = ss.IsJoined,
                            IsOnBreak = ss.IsOnBreak,
                            LastActivity = u.LastActivity,
                            SingerStatusUpdatedAt = (DateTime?)ss.UpdatedAt,
                            Source = "SingerStatus"
                        })
                    .ToListAsync();
                var queueSingers = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.Status == "Live" && eq.SungAt == null)
                    .Select(eq => eq.RequestorUserName)
                    .Distinct()
                    .Join(_context.Users,
                        userName => userName,
                        u => u.UserName,
                        (userName, u) => new
                        {
                            UserId = u.UserName ?? string.Empty,
                            DisplayName = (u.FirstName + " " + u.LastName).Trim(),
                            IsLoggedIn = u.LastActivity != null && u.LastActivity >= DateTime.UtcNow.AddMinutes(-timeoutMinutes),
                            IsJoined = false,
                            IsOnBreak = false,
                            LastActivity = u.LastActivity,
                            SingerStatusUpdatedAt = (DateTime?)null,
                            Source = "EventQueues"
                        })
                    .ToListAsync();
                foreach (var singer in singerStatuses)
                {
                    _logger.LogDebug("[DJController] SingerStatus singer: UserId={UserId}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}, LastActivity={LastActivity}, UpdatedAt={UpdatedAt}, Source={Source}",
                        singer.UserId, singer.IsLoggedIn, singer.IsJoined, singer.IsOnBreak, singer.LastActivity, singer.SingerStatusUpdatedAt, singer.Source);
                }
                foreach (var singer in queueSingers)
                {
                    _logger.LogDebug("[DJController] EventQueues singer: UserId={UserId}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}, LastActivity={LastActivity}, UpdatedAt={UpdatedAt}, Source={Source}",
                        singer.UserId, singer.IsLoggedIn, singer.IsJoined, singer.IsOnBreak, singer.LastActivity, singer.SingerStatusUpdatedAt, singer.Source);
                }
                var allSingers = singerStatuses
                    .GroupJoin(queueSingers,
                        ss => ss.UserId,
                        qs => qs.UserId,
                        (ss, qsGroup) => new { SingerStatus = ss, QueueSingers = qsGroup })
                    .SelectMany(g => g.QueueSingers.DefaultIfEmpty(), (g, qs) => new { g.SingerStatus, QueueSinger = qs })
                    .GroupBy(x => x.SingerStatus.UserId)
                    .Select(g =>
                    {
                        var ss = g.First().SingerStatus;
                        var qs = g.FirstOrDefault(x => x.QueueSinger != null)?.QueueSinger;
                        return new DJSingerDto
                        {
                            UserId = ss.UserId,
                            DisplayName = ss.DisplayName.Length > 0 ? ss.DisplayName : ss.UserId,
                            // Treat presence in the queue as logged in/joined even if SingerStatus is missing
                            IsLoggedIn = ss.IsLoggedIn || qs != null,
                            IsJoined = ss.IsJoined || qs != null,
                            IsOnBreak = ss.IsOnBreak
                        };
                    })
                    .ToList();
                var queueOnlySingers = queueSingers
                    .Where(qs => !singerStatuses.Any(ss => ss.UserId == qs.UserId))
                    .Select(qs => new DJSingerDto
                    {
                        UserId = qs.UserId,
                        DisplayName = qs.DisplayName.Length > 0 ? qs.DisplayName : qs.UserId,
                        // Singer has queue entries but no SingerStatus record; assume logged in and joined
                        IsLoggedIn = true,
                        IsJoined = true,
                        IsOnBreak = qs.IsOnBreak
                    })
                    .ToList();
                allSingers.AddRange(queueOnlySingers);
                foreach (var singer in allSingers)
                {
                    _logger.LogDebug("[DJController] Final singer: UserId={UserId}, DisplayName={DisplayName}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}",
                        singer.UserId, singer.DisplayName, singer.IsLoggedIn, singer.IsJoined, singer.IsOnBreak);
                }
                var totalSingers = allSingers.Count;
                var pagedSingers = allSingers
                    .OrderBy(s => s.DisplayName)
                    .Skip((page - 1) * size)
                    .Take(size)
                    .ToList();
                _logger.LogInformation("[DJController] Fetched {Count} singers for EventId: {EventId}, Total: {TotalCount}", pagedSingers.Count, eventId, totalSingers);
                return Ok(new { Singers = pagedSingers, Total = totalSingers, Page = page, Size = size });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error fetching singers for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error retrieving singers", details = ex.Message });
            }
        }

        [HttpPost("complete")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> CompleteSong([FromBody] CompleteSongDto request)
        {
            try
            {
                _logger.LogInformation("[DJController] Completing song for EventId: {EventId}, QueueId: {QueueId}", request.EventId, request.QueueId);
                var eventEntity = await _context.Events.FindAsync(request.EventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", request.EventId);
                    return NotFound("Event not found or not live");
                }
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == request.EventId && eq.QueueId == request.QueueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("[DJController] Queue entry not found with QueueId: {QueueId} for EventId: {EventId}", request.QueueId, request.EventId);
                    return NotFound("Queue entry not found");
                }
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                var singerStatus = user != null
                    ? await _context.SingerStatus.FirstOrDefaultAsync(ss => ss.EventId == request.EventId && ss.RequestorId == user.Id)
                    : null;
                queueEntry.IsCurrentlyPlaying = false;
                queueEntry.SungAt = DateTime.UtcNow;
                queueEntry.Status = "Live";
                queueEntry.UpdatedAt = DateTime.UtcNow;
                eventEntity.SongsCompleted++;
                await _context.SaveChangesAsync();
                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<List<string>>(queueEntry.Singers) ?? new List<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", request.QueueId, ex.Message);
                }
                var queueDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = queueEntry.Song?.YouTubeUrl,
                    RequestorUserName = queueEntry.RequestorUserName,
                    RequestorFullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "",
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = queueEntry.Status,
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak,
                    HoldReason = string.Empty,
                    IsUpNext = false,
                    IsSingerLoggedIn = singerStatus?.IsLoggedIn ?? false,
                    IsSingerJoined = singerStatus?.IsJoined ?? false,
                    IsSingerOnBreak = singerStatus?.IsOnBreak ?? false,
                    IsServerCached = queueEntry.Song?.Cached ?? false,
                    IsMature = queueEntry.Song?.Mature ?? false
                };
                await _hubContext.Clients.Group($"Event_{request.EventId}").SendAsync("QueueUpdated", new { data = queueDto, action = "Sung" });
                _logger.LogInformation("[DJController] Completed song with QueueId: {QueueId} for EventId: {EventId}", request.QueueId, request.EventId);
                return Ok(new { message = "Song completed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error completing song for EventId: {EventId}, QueueId: {QueueId}", request.EventId, request.QueueId);
                return StatusCode(500, new { message = "Error completing song", details = ex.Message });
            }
        }

        [HttpPost("break")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> ToggleBreak([FromBody] ToggleBreakDto request)
        {
            try
            {
                _logger.LogInformation("[DJController] Toggling break for EventId: {EventId}, QueueId: {QueueId}, IsOnBreak: {IsOnBreak}", request.EventId, request.QueueId, request.IsOnBreak);
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == request.EventId && eq.QueueId == request.QueueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("[DJController] Queue entry not found with QueueId: {QueueId} for EventId: {EventId}", request.QueueId, request.EventId);
                    return NotFound("Queue entry not found");
                }
                var eventEntity = await _context.Events.FindAsync(request.EventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", request.EventId);
                    return NotFound("Event not found or not live");
                }
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                var singerStatus = user != null
                    ? await _context.SingerStatus.FirstOrDefaultAsync(ss => ss.EventId == request.EventId && ss.RequestorId == user.Id)
                    : null;
                queueEntry.IsOnBreak = request.IsOnBreak;
                if (request.IsOnBreak)
                    _holdReasons[queueEntry.QueueId] = "OnHold";
                else
                    _holdReasons.Remove(queueEntry.QueueId);
                queueEntry.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<List<string>>(queueEntry.Singers) ?? new List<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueEntry.QueueId, ex.Message);
                }
                var queueDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = queueEntry.Song?.YouTubeUrl,
                    RequestorUserName = queueEntry.RequestorUserName,
                    RequestorFullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "",
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = queueEntry.Status,
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak,
                    HoldReason = request.IsOnBreak ? "OnHold" : string.Empty,
                    IsUpNext = false,
                    IsSingerLoggedIn = singerStatus?.IsLoggedIn ?? false,
                    IsSingerJoined = singerStatus?.IsJoined ?? false,
                    IsSingerOnBreak = singerStatus?.IsOnBreak ?? false,
                    IsServerCached = queueEntry.Song?.Cached ?? false,
                    IsMature = queueEntry.Song?.Mature ?? false
                };
                await _hubContext.Clients.Group($"Event_{request.EventId}").SendAsync("QueueUpdated", new { data = queueDto, action = request.IsOnBreak ? "OnHold" : "Eligible" });
                _logger.LogInformation("[DJController] Toggled break for QueueId: {QueueId} to IsOnBreak: {IsOnBreak} for EventId: {EventId}", request.QueueId, request.IsOnBreak, request.EventId);
                return Ok(new { message = "Break status updated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error toggling break for EventId: {EventId}, QueueId: {QueueId}", request.EventId, request.QueueId);
                return StatusCode(500, new { message = "Error updating break status", details = ex.Message });
            }
        }

        [HttpPost("singer/update")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> UpdateSingerStatus([FromBody] UpdateSingerStatusDto request)
        {
            try
            {
                _logger.LogInformation("[DJController] Updating singer status for EventId: {EventId}, RequestorUserName: {RequestorUserName}", request.EventId, request.RequestorUserName);
                var eventEntity = await _context.Events.FindAsync(request.EventId);
                if (eventEntity == null || eventEntity.Status != "Live")
                {
                    _logger.LogWarning("[DJController] Event not found or not live with EventId: {EventId}", request.EventId);
                    return NotFound("Event not found or not live");
                }
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.UserName == request.RequestorUserName);
                if (user == null)
                {
                    _logger.LogWarning("[DJController] User not found with UserName: {UserName}", request.RequestorUserName);
                    return NotFound("User not found");
                }
                DateTime? originalUpdatedAt = null;
                bool hadConcurrencyConflict = false;
                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    var singerStatus = await _context.SingerStatus
                        .AsNoTracking()
                        .FirstOrDefaultAsync(ss => ss.EventId == request.EventId && ss.RequestorId == user.Id);
                    originalUpdatedAt = singerStatus?.UpdatedAt;
                    _logger.LogDebug("[DJController] Pre-update SingerStatus for UserId={UserId}, EventId={EventId}: IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}, UpdatedAt={UpdatedAt}",
                        user.Id, request.EventId, singerStatus?.IsLoggedIn, singerStatus?.IsJoined, singerStatus?.IsOnBreak, singerStatus?.UpdatedAt);
                    var dbSingerStatus = await _context.SingerStatus
                        .FirstOrDefaultAsync(ss => ss.EventId == request.EventId && ss.RequestorId == user.Id);
                    if (dbSingerStatus == null)
                    {
                        dbSingerStatus = new SingerStatus
                        {
                            EventId = request.EventId,
                            RequestorId = user.Id,
                            IsLoggedIn = request.IsLoggedIn,
                            IsJoined = request.IsJoined,
                            IsOnBreak = request.IsOnBreak,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.SingerStatus.Add(dbSingerStatus);
                    }
                    else
                    {
                        if (originalUpdatedAt != null && originalUpdatedAt != dbSingerStatus.UpdatedAt)
                        {
                            _logger.LogWarning("[DJController] Concurrency conflict for singer status update, EventId: {EventId}, UserId: {UserId}", request.EventId, user.Id);
                            hadConcurrencyConflict = true;
                            return Conflict("Singer status modified by another user, please retry");
                        }
                        dbSingerStatus.IsLoggedIn = request.IsLoggedIn;
                        dbSingerStatus.IsJoined = request.IsJoined;
                        dbSingerStatus.IsOnBreak = request.IsOnBreak;
                        dbSingerStatus.UpdatedAt = DateTime.UtcNow;
                    }
                    var attendance = await _context.EventAttendances
                        .FirstOrDefaultAsync(ea => ea.EventId == request.EventId && ea.RequestorId == user.Id);
                    if (attendance == null)
                    {
                        attendance = new EventAttendance
                        {
                            EventId = request.EventId,
                            RequestorId = user.Id,
                            IsCheckedIn = request.IsJoined,
                            IsOnBreak = request.IsOnBreak,
                            BreakStartAt = request.IsOnBreak ? DateTime.UtcNow : null,
                            BreakEndAt = !request.IsOnBreak ? DateTime.UtcNow : null
                        };
                        _context.EventAttendances.Add(attendance);
                    }
                    else
                    {
                        attendance.IsCheckedIn = request.IsJoined;
                        attendance.IsOnBreak = request.IsOnBreak;
                        attendance.BreakStartAt = request.IsOnBreak ? DateTime.UtcNow : attendance.BreakStartAt;
                        attendance.BreakEndAt = !request.IsOnBreak ? DateTime.UtcNow : attendance.BreakEndAt;
                    }
                    var holdReason = string.Empty;
                    if (!request.IsLoggedIn)
                        holdReason = "NotLoggedIn";
                    else if (!request.IsJoined)
                        holdReason = "NotJoined";
                    else if (request.IsOnBreak)
                        holdReason = "OnBreak";
                    var queueEntries = await _context.EventQueues
                        .Where(eq => eq.EventId == request.EventId && eq.RequestorUserName == request.RequestorUserName && eq.SungAt == null && eq.Status == "Live")
                        .ToListAsync();
                    foreach (var entry in queueEntries)
                    {
                        entry.IsOnBreak = request.IsOnBreak;
                        entry.UpdatedAt = DateTime.UtcNow;
                        if (!string.IsNullOrEmpty(holdReason))
                            _holdReasons[entry.QueueId] = holdReason;
                        else
                            _holdReasons.Remove(entry.QueueId);
                        var singersList = new List<string>();
                        try
                        {
                            singersList.AddRange(JsonSerializer.Deserialize<List<string>>(entry.Singers) ?? new List<string>());
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", entry.QueueId, ex.Message);
                        }
                        var queueDto = new EventQueueDto
                        {
                            QueueId = entry.QueueId,
                            EventId = entry.EventId,
                            SongId = entry.SongId,
                            SongTitle = entry.Song?.Title ?? string.Empty,
                            SongArtist = entry.Song?.Artist ?? string.Empty,
                            YouTubeUrl = entry.Song?.YouTubeUrl,
                            RequestorUserName = entry.RequestorUserName,
                            RequestorFullName = $"{user.FirstName} {user.LastName}".Trim(),
                            Singers = singersList,
                            Position = entry.Position,
                            Status = entry.Status,
                            IsActive = entry.IsActive,
                            WasSkipped = entry.WasSkipped,
                            IsCurrentlyPlaying = entry.IsCurrentlyPlaying,
                            SungAt = entry.SungAt,
                            IsOnBreak = entry.IsOnBreak,
                            HoldReason = holdReason,
                            IsUpNext = false,
                            IsSingerLoggedIn = request.IsLoggedIn,
                            IsSingerJoined = request.IsJoined,
                            IsSingerOnBreak = request.IsOnBreak,
                            IsServerCached = entry.Song?.Cached ?? false,
                            IsMature = entry.Song?.Mature ?? false
                        };
                        await _hubContext.Clients.Group($"Event_{request.EventId}").SendAsync("QueueUpdated", new { data = queueDto, action = !string.IsNullOrEmpty(holdReason) ? "Held" : "Eligible" });
                    }
                    await _context.SaveChangesAsync();
                    _logger.LogDebug("[DJController] Post-update SingerStatus for UserId={UserId}, EventId={EventId}: IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}, UpdatedAt={UpdatedAt}",
                        user.Id, request.EventId, dbSingerStatus.IsLoggedIn, dbSingerStatus.IsJoined, dbSingerStatus.IsOnBreak, dbSingerStatus.UpdatedAt);
                    scope.Complete();
                }
                var response = new DJSingerDto
                {
                    UserId = user.UserName ?? string.Empty,
                    DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                    IsLoggedIn = request.IsLoggedIn,
                    IsJoined = request.IsJoined,
                    IsOnBreak = request.IsOnBreak
                };
                try
                {
                    _logger.LogDebug("[DJController] Sending SingerStatusUpdated to group Event_{EventId} for RequestorUserName={RequestorUserName}", request.EventId, request.RequestorUserName);
                    await _hubContext.Clients.Group($"Event_{request.EventId}")
                        .SendAsync("SingerStatusUpdated", new { userName = request.RequestorUserName, eventId = request.EventId, isLoggedIn = request.IsLoggedIn, isJoined = request.IsJoined, isOnBreak = request.IsOnBreak });
                    _logger.LogInformation("[DJController] Successfully sent SingerStatusUpdated for EventId: {EventId}, RequestorUserName: {RequestorUserName}", request.EventId, request.RequestorUserName);
                }
                catch (Exception signalREx)
                {
                    _logger.LogError(signalREx, "[DJController] Failed to send SingerStatusUpdated for EventId: {EventId}, RequestorUserName: {RequestorUserName}", request.EventId, request.RequestorUserName);
                }
                _logger.LogInformation("[DJController] Updated singer status for RequestorUserName: {RequestorUserName} for EventId: {EventId}, Concurrency Conflict: {Conflict}", request.RequestorUserName, request.EventId, hadConcurrencyConflict);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DJController] Error updating singer status for EventId: {EventId}, RequestorUserName: {RequestorUserName}", request.EventId, request.RequestorUserName);
                return StatusCode(500, new { message = "Error updating singer status", details = ex.Message });
            }
        }

        private async Task<List<EventQueueDto>> BuildQueueDtos(IEnumerable<EventQueue> queueEntries, int eventId)
        {
            var requestorUserNames = queueEntries
                .Select(eq => eq.RequestorUserName)
                .Where(userName => !string.IsNullOrEmpty(userName))
                .Distinct()
                .ToList();
            var singerUserNames = new HashSet<string>();
            foreach (var eq in queueEntries)
            {
                try
                {
                    var singers = JsonSerializer.Deserialize<string[]>(eq.Singers) ?? Array.Empty<string>();
                    foreach (var singer in singers)
                    {
                        if (singer != "AllSing" && singer != "TheBoys" && singer != "TheGirls" && !string.IsNullOrEmpty(singer))
                        {
                            singerUserNames.Add(singer);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("[DJController] Failed to deserialize Singers for QueueId: {QueueId}, Error: {Message}", eq.QueueId, ex.Message);
                }
            }
            var allUserNames = requestorUserNames.Concat(singerUserNames).Distinct().ToList();
            var allUsers = await _context.Users
                .Where(u => u.UserName != null && allUserNames.Contains(u.UserName))
                .ToDictionaryAsync(u => u.UserName!, u => u);
            var singerStatuses = await _context.SingerStatus
                .Where(ss => ss.EventId == eventId)
                .Join(_context.Users,
                    ss => ss.RequestorId,
                    u => u.Id,
                    (ss, u) => new { SingerStatus = ss, UserName = u.UserName })
                .Where(x => x.UserName != null && allUserNames.Contains(x.UserName))
                .ToDictionaryAsync(x => x.UserName!, x => x.SingerStatus);
            var userIds = allUsers.Values.Select(u => u.Id).Distinct().ToList();
            var attendances = await _context.EventAttendances
                .Where(ea => ea.EventId == eventId && userIds.Contains(ea.RequestorId))
                .ToDictionaryAsync(ea => ea.RequestorId, ea => ea);
            var queueDtos = new List<EventQueueDto>();
            foreach (var eq in queueEntries)
            {
                if (string.IsNullOrEmpty(eq.RequestorUserName) || !allUsers.TryGetValue(eq.RequestorUserName, out var requestor))
                {
                    _logger.LogWarning("[DJController] Requestor not found with UserName: {UserName} for QueueId: {QueueId}", eq.RequestorUserName, eq.QueueId);
                    continue;
                }
                attendances.TryGetValue(requestor.Id, out var attendance);
                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<string[]>(eq.Singers) ?? Array.Empty<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("[DJController] Failed to deserialize Singers for QueueId: {QueueId}, Error: {Message}", eq.QueueId, ex.Message);
                }
                bool anySingerOnBreak = false;
                string holdReason = _holdReasons.TryGetValue(eq.QueueId, out var reason) ? reason : string.Empty;
                var combinedSingers = new HashSet<string>(singersList) { eq.RequestorUserName };
                bool isSingerLoggedIn = false;
                bool isSingerJoined = false;
                bool isSingerOnBreak = false;
                foreach (var singer in combinedSingers)
                {
                    if (singer == "AllSing" || singer == "TheBoys" || singer == "TheGirls" || string.IsNullOrEmpty(singer))
                        continue;
                    if (allUsers.TryGetValue(singer, out var singerUser) &&
                        attendances.TryGetValue(singerUser.Id, out var singerAttendance) &&
                        singerAttendance.IsOnBreak)
                    {
                        anySingerOnBreak = true;
                        if (string.IsNullOrEmpty(holdReason))
                            holdReason = "OnBreak";
                    }
                    if (singerStatuses.TryGetValue(singer, out var ss))
                    {
                        if (ss.IsLoggedIn) isSingerLoggedIn = true;
                        if (ss.IsJoined) isSingerJoined = true;
                        if (ss.IsOnBreak) isSingerOnBreak = true;
                    }
                }
                var statusString = ComputeSongStatus(eq, anySingerOnBreak, holdReason);
                var queueDto = new EventQueueDto
                {
                    QueueId = eq.QueueId,
                    EventId = eq.EventId,
                    SongId = eq.SongId,
                    SongTitle = eq.Song?.Title ?? string.Empty,
                    SongArtist = eq.Song?.Artist ?? string.Empty,
                    YouTubeUrl = eq.Song?.YouTubeUrl,
                    RequestorUserName = eq.RequestorUserName,
                    RequestorFullName = $"{requestor.FirstName} {requestor.LastName}".Trim(),
                    Singers = singersList,
                    Position = eq.Position,
                    Status = statusString,
                    IsActive = eq.IsActive,
                    WasSkipped = eq.WasSkipped,
                    IsCurrentlyPlaying = eq.IsCurrentlyPlaying,
                    SungAt = eq.SungAt,
                    IsOnBreak = eq.IsOnBreak,
                    HoldReason = holdReason,
                    IsUpNext = false,
                    IsSingerLoggedIn = isSingerLoggedIn,
                    IsSingerJoined = isSingerJoined,
                    IsSingerOnBreak = isSingerOnBreak,
                    IsServerCached = eq.Song?.Cached ?? false,
                    IsMature = eq.Song?.Mature ?? false
                };
                queueDtos.Add(queueDto);
            }
            return queueDtos.OrderBy(q => q.Position).ToList();
        }

        private string ComputeSongStatus(EventQueue queueEntry, bool anySingerOnBreak, string holdReason)
        {
            if (queueEntry.WasSkipped)
                return "Skipped";
            if (queueEntry.IsCurrentlyPlaying)
                return "Playing";
            if (queueEntry.SungAt != null)
                return "Sung";
            if (anySingerOnBreak || queueEntry.IsOnBreak || !string.IsNullOrEmpty(holdReason))
                return "Held";
            return "Unplayed";
        }
    }

    public class CheckInDto
    {
        public string RequestorUserName { get; set; } = string.Empty;
    }

    public class CompleteSongDto
    {
        public int EventId { get; set; }
        public int QueueId { get; set; }
    }

    public class NowPlayingDto
    {
        public int QueueId { get; set; }
    }

    public class ToggleBreakDto
    {
        public int EventId { get; set; }
        public int QueueId { get; set; }
        public bool IsOnBreak { get; set; }
    }

    public class UpdateSingerStatusDto
    {
        public int EventId { get; set; }
        public string RequestorUserName { get; set; } = string.Empty;
        public bool IsLoggedIn { get; set; }
        public bool IsJoined { get; set; }
        public bool IsOnBreak { get; set; }
    }
}