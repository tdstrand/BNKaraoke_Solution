using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Models;
using System.Linq;
using System.Security.Claims;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;

namespace BNKaraoke.Api.Controllers
{
    public partial class EventController
    {
        [HttpPost("{eventId}/queue")]
        [Authorize]
        public async Task<IActionResult> AddToQueue(int eventId, [FromBody] EventQueueCreateDto queueDto)
        {
            try
            {
                _logger.LogInformation("Adding song to queue for EventId: {EventId}, SongId: {SongId}, RequestorUserName: {RequestorUserName}", eventId, queueDto.SongId, queueDto.RequestorUserName);
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for AddToQueue: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                if (eventEntity.IsCanceled || eventEntity.Visibility != "Visible")
                {
                    _logger.LogWarning("Cannot add to queue for EventId {EventId}: Event is canceled or hidden", eventId);
                    return BadRequest("Cannot add to queue for a canceled or hidden event");
                }

                var song = await _context.Songs.FindAsync(queueDto.SongId);
                if (song == null)
                {
                    _logger.LogWarning("Song not found with SongId: {SongId}", queueDto.SongId);
                    return BadRequest("Song not found");
                }

                if (string.IsNullOrEmpty(queueDto.RequestorUserName))
                {
                    _logger.LogWarning("RequestorUserName is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("RequestorUserName cannot be null or empty");
                }

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == queueDto.RequestorUserName);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", queueDto.RequestorUserName, eventId);
                    return BadRequest("Requestor not found");
                }

                var exists = await _context.EventQueues.AnyAsync(q =>
                    q.EventId == eventId &&
                    q.RequestorUserName == queueDto.RequestorUserName &&
                    q.SongId == queueDto.SongId);
                if (exists)
                {
                    _logger.LogWarning("Song {SongId} already in queue for EventId {EventId} by RequestorUserName {UserName}", queueDto.SongId, eventId, queueDto.RequestorUserName);
                    return BadRequest("Song already in queue");
                }

                var requestedCount = await _context.EventQueues
                    .CountAsync(eq => eq.EventId == eventId && eq.RequestorUserName == queueDto.RequestorUserName);
                if (requestedCount >= eventEntity.RequestLimit)
                {
                    _logger.LogWarning("Requestor with UserName {UserName} reached request limit of {RequestLimit} for EventId {EventId}", queueDto.RequestorUserName, eventEntity.RequestLimit, eventId);
                    return BadRequest($"Request limit of {eventEntity.RequestLimit} songs reached");
                }

                if (eventEntity.Status != "Upcoming")
                {
                    var attendance = await _context.EventAttendances
                        .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);
                    if (attendance == null || !attendance.IsCheckedIn)
                    {
                        _logger.LogWarning("Requestor with UserName {UserName} must be checked in for EventId {EventId}", queueDto.RequestorUserName, eventId);
                        return BadRequest("Requestor must be checked in for non-upcoming event");
                    }
                }

                var maxPosition = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .MaxAsync(eq => (int?)eq.Position) ?? 0;

                var newQueueEntry = new EventQueue
                {
                    EventId = eventId,
                    SongId = queueDto.SongId,
                    RequestorUserName = requestor.UserName ?? string.Empty,
                    Singers = JsonSerializer.Serialize(new[] { requestor.UserName }),
                    Position = maxPosition + 1,
                    Status = eventEntity.Status,
                    IsActive = eventEntity.Status == "Live",
                    WasSkipped = false,
                    IsCurrentlyPlaying = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsOnBreak = false
                };

                _context.EventQueues.Add(newQueueEntry);
                await _context.SaveChangesAsync();

                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<string[]>(newQueueEntry.Singers) ?? Array.Empty<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", newQueueEntry.QueueId, ex.Message);
                }

                var queueEntryDto = new EventQueueDto
                {
                    QueueId = newQueueEntry.QueueId,
                    EventId = newQueueEntry.EventId,
                    SongId = newQueueEntry.SongId,
                    SongTitle = song.Title ?? string.Empty,
                    SongArtist = song.Artist ?? string.Empty,
                    RequestorUserName = newQueueEntry.RequestorUserName,
                    Singers = singersList,
                    Position = newQueueEntry.Position,
                    Status = ComputeSongStatus(newQueueEntry, false),
                    IsActive = newQueueEntry.IsActive,
                    WasSkipped = newQueueEntry.WasSkipped,
                    IsCurrentlyPlaying = newQueueEntry.IsCurrentlyPlaying,
                    SungAt = newQueueEntry.SungAt,
                    IsOnBreak = newQueueEntry.IsOnBreak
                };

                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", newQueueEntry.QueueId, "Added");

                _logger.LogInformation("Added song to queue for EventId {EventId}, QueueId: {QueueId}", eventId, newQueueEntry.QueueId);
                return CreatedAtAction(nameof(GetEventQueue), new { eventId, queueId = newQueueEntry.QueueId }, queueEntryDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding song to queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error adding to queue", details = ex.Message });
            }
        }

        [HttpGet("{eventId}/queue")]
        [Authorize]
        public async Task<IActionResult> GetEventQueue(int eventId)
        {
            try
            {
                _logger.LogInformation("Fetching event queue for EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FirstOrDefaultAsync(e => e.EventId == eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .Include(eq => eq.Song)
                    .ToListAsync();

                var requestorUserNames = queueEntries
                    .Select(eq => eq.RequestorUserName)
                    .Where(userName => !string.IsNullOrEmpty(userName))
                    .Distinct()
                    .ToList();
                var allUsers = await _context.Users
                    .OfType<ApplicationUser>()
                    .Where(u => u.UserName != null && requestorUserNames.Contains(u.UserName))
                    .ToListAsync();
                var users = allUsers.ToDictionary(u => u.UserName!, u => u);

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
                        _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", eq.QueueId, ex.Message);
                    }
                }

                var singerUsers = await _context.Users
                    .OfType<ApplicationUser>()
                    .Where(u => u.UserName != null && singerUserNames.Contains(u.UserName))
                    .ToDictionaryAsync(u => u.UserName!, u => u);

                var userIds = users.Values.Select(u => u.Id)
                    .Concat(singerUsers.Values.Select(u => u.Id))
                    .Distinct()
                    .ToList();
                var attendances = await _context.EventAttendances
                    .Where(ea => ea.EventId == eventId && userIds.Contains(ea.RequestorId))
                    .ToDictionaryAsync(ea => ea.RequestorId, ea => ea);

                var queueDtos = new List<EventQueueDto>();
                foreach (var eq in queueEntries)
                {
                    if (string.IsNullOrEmpty(eq.RequestorUserName) || !users.TryGetValue(eq.RequestorUserName, out var requestor))
                    {
                        _logger.LogWarning("Requestor not found with UserName: {UserName} for QueueId {QueueId}", eq.RequestorUserName, eq.QueueId);
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
                        _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", eq.QueueId, ex.Message);
                    }

                    bool anySingerOnBreak = false;
                    foreach (var singer in singersList)
                    {
                        if (singer == "AllSing" || singer == "TheBoys" || singer == "TheGirls")
                        {
                            continue;
                        }

                        if (singer != null && singerUsers.TryGetValue(singer, out var singerUser) &&
                            attendances.TryGetValue(singerUser.Id, out var singerAttendance) &&
                            singerAttendance.IsOnBreak)
                        {
                            anySingerOnBreak = true;
                            break;
                        }
                    }

                    var queueDto = new EventQueueDto
                    {
                        QueueId = eq.QueueId,
                        EventId = eq.EventId,
                        SongId = eq.SongId,
                        SongTitle = eq.Song?.Title ?? string.Empty,
                        SongArtist = eq.Song?.Artist ?? string.Empty,
                        RequestorUserName = eq.RequestorUserName,
                        Singers = singersList,
                        Position = eq.Position,
                        Status = ComputeSongStatus(eq, anySingerOnBreak),
                        IsActive = eq.IsActive,
                        WasSkipped = eq.WasSkipped,
                        IsCurrentlyPlaying = eq.IsCurrentlyPlaying,
                        SungAt = eq.SungAt,
                        IsOnBreak = eq.IsOnBreak
                    };

                    queueDtos.Add(queueDto);
                }

                var sortedQueueDtos = queueDtos.OrderBy(eq => eq.Position).ToList();

                _logger.LogInformation("Fetched {Count} queue entries for EventId: {EventId}", sortedQueueDtos.Count, eventId);
                return Ok(sortedQueueDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching event queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error fetching event queue", details = ex.Message });
            }
        }

        [HttpPut("{eventId}/queue/reorder")]
        [Authorize]
        public async Task<IActionResult> ReorderQueue(int eventId, [FromBody] ReorderQueueRequest request)
        {
            try
            {
                _logger.LogInformation("Reordering queue for EventId: {EventId}", eventId);
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for ReorderQueue: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var userName = User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userName))
                {
                    _logger.LogWarning("User identity not found in token for EventId: {EventId}", eventId);
                    return Unauthorized("User identity not found");
                }

                var userQueueEntries = await _context.EventQueues
                    .FromSqlRaw(
                        @"SELECT * FROM public.""EventQueues""
                          WHERE ""EventId"" = {0} AND (""RequestorUserName"" = {1} OR ""Singers"" @> {2}::jsonb)",
                        eventId, userName, $"[{userName}]"
                    )
                    .ToListAsync();

                var requestQueueIds = request.NewOrder.Select(o => o.QueueId).ToList();
                var userQueueIds = userQueueEntries.Select(eq => eq.QueueId).ToList();

                if (requestQueueIds.Count != userQueueIds.Count || !requestQueueIds.All(qid => userQueueIds.Contains(qid)))
                {
                    _logger.LogWarning("Invalid reorder request: Queue IDs do not match user's queue entries for EventId {EventId}", eventId);
                    return BadRequest("Invalid reorder request: Queue IDs do not match user's queue entries");
                }

                var allQueueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();

                var positionMapping = allQueueEntries.ToDictionary(eq => eq.QueueId, eq => eq.Position);

                foreach (var order in request.NewOrder)
                {
                    var queueEntry = allQueueEntries.FirstOrDefault(eq => eq.QueueId == order.QueueId);
                    if (queueEntry != null)
                    {
                        positionMapping[queueEntry.QueueId] = order.Position;
                    }
                }

                var otherQueueEntries = allQueueEntries
                    .Where(eq => !userQueueIds.Contains(eq.QueueId))
                    .OrderBy(eq => eq.Position)
                    .ToList();

                var sortedUserEntries = userQueueEntries
                    .OrderBy(eq => request.NewOrder.FirstOrDefault(o => o.QueueId == eq.QueueId)?.Position ?? int.MaxValue)
                    .ToList();

                int position = 1;
                var reorderedEntries = new List<EventQueue>();
                foreach (var userEntry in sortedUserEntries)
                {
                    userEntry.Position = position++;
                    userEntry.UpdatedAt = DateTime.UtcNow;
                    reorderedEntries.Add(userEntry);
                }

                foreach (var otherEntry in otherQueueEntries)
                {
                    otherEntry.Position = position++;
                    otherEntry.UpdatedAt = DateTime.UtcNow;
                    reorderedEntries.Add(otherEntry);
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", 0, "Reordered");

                _logger.LogInformation("Reordered queue for EventId: {EventId}", eventId);
                return Ok(new { message = "Queue reordered" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error reordering queue", details = ex.Message });
            }
        }

        [HttpPost("{eventId:int}/queue/{queueId:int}/singers")]
        [Authorize]
        public async Task<IActionResult> UpdateQueueSingers(int eventId, int queueId, [FromBody] UpdateQueueSingersDto singersDto)
        {
            try
            {
                _logger?.LogInformation("Updating singers for QueueId {QueueId} in EventId: {EventId}", queueId, eventId);
                if (!ModelState.IsValid)
                {
                    _logger?.LogWarning("Invalid model state for UpdateQueueSingers: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                if (_context == null)
                {
                    _logger?.LogError("Database context is null");
                    return StatusCode(500, new { message = "Internal server error" });
                }

                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger?.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                if (queueEntry == null)
                {
                    _logger?.LogWarning("Queue entry not found with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }

                var userName = User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userName))
                {
                    _logger?.LogWarning("User identity not found in token for EventId: {EventId}", eventId);
                    return Unauthorized("User identity not found");
                }

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                if (requestor == null)
                {
                    _logger?.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", queueEntry.RequestorUserName, eventId);
                    return BadRequest("Requestor not found");
                }

                if (queueEntry.RequestorUserName != userName)
                {
                    try
                    {
                        var currentSingers = JsonSerializer.Deserialize<string[]>(queueEntry.Singers) ?? Array.Empty<string>();
                        if (!currentSingers.Contains(userName))
                        {
                            _logger?.LogWarning("User with UserName {UserName} is not authorized to update singers for QueueId {QueueId}", userName, queueId);
                            return Forbid("Only the requestor or a singer can update singers");
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger?.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueId, ex.Message);
                        return StatusCode(500, new { message = "Error processing singers data", details = ex.Message });
                    }
                }

                var newSingers = singersDto.Singers ?? new List<string>(); // Use List<string>
                foreach (var singer in newSingers)
                {
                    if (singer != "AllSing" && singer != "TheBoys" && singer != "TheGirls")
                    {
                        var singerUser = await _context.Users
                            .OfType<ApplicationUser>()
                            .FirstOrDefaultAsync(u => u.UserName == singer);
                        if (singerUser == null)
                        {
                            _logger?.LogWarning("Singer not found with UserName: {UserName} for EventId: {EventId}", singer, eventId);
                            return BadRequest($"Singer not found: {singer}");
                        }

                        var attendance = await _context.EventAttendances
                            .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == singerUser.Id);
                        if (eventEntity.Status != "Upcoming" && (attendance == null || !attendance.IsCheckedIn))
                        {
                            _logger?.LogWarning("Singer with UserName {UserName} must be checked in for EventId {EventId}", singer, eventId);
                            return BadRequest($"Singer must be checked in: {singer}");
                        }
                    }
                }

                if (!newSingers.Any(s => s == queueEntry.RequestorUserName || s == "AllSing" || s == "TheBoys" || s == "TheGirls"))
                {
                    _logger?.LogWarning("RequestorUserName {UserName} must be included in singers list for QueueId {QueueId}", queueEntry.RequestorUserName, queueId);
                    return BadRequest("Requestor must be included in singers list");
                }

                var serializedSingers = JsonSerializer.Serialize(newSingers);
                queueEntry.Singers = serializedSingers;
                queueEntry.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<string[]>(queueEntry.Singers) ?? Array.Empty<string>());
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueId, ex.Message);
                }

                var queueEntryDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    RequestorUserName = queueEntry.RequestorUserName,
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = ComputeSongStatus(queueEntry, false),
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak
                };

                if (_hubContext != null)
                {
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueId, "SingersUpdated");
                }

                _logger?.LogInformation("Updated singers for QueueId {QueueId} in EventId {EventId}", queueId, eventId);
                return Ok(queueEntryDto);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating singers for QueueId {QueueId} in EventId {EventId}", queueId, eventId);
                return StatusCode(500, new { message = "Error updating singers", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/queue/{queueId}/skip")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> SkipSong(int eventId, int queueId)
        {
            try
            {
                _logger.LogInformation("Skipping song with QueueId {QueueId} for EventId: {EventId}", queueId, eventId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var queueEntry = await _context.EventQueues
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("Queue entry not found with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", queueEntry.RequestorUserName, eventId);
                    return BadRequest("Requestor not found");
                }

                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);
                if (attendance == null || !attendance.IsOnBreak)
                {
                    _logger.LogWarning("Requestor with UserName {UserName} must be on break to skip song for EventId {EventId}", queueEntry.RequestorUserName, eventId);
                    return BadRequest("Requestor must be on break to skip their song");
                }

                queueEntry.WasSkipped = true;
                queueEntry.SungAt = DateTime.UtcNow;
                queueEntry.UpdatedAt = DateTime.UtcNow;

                eventEntity.SongsCompleted++;

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueId, "Skipped");

                _logger.LogInformation("Skipped song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return Ok(new { message = "Song skipped" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error skipping song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return StatusCode(500, new { message = "Error skipping song", details = ex.Message });
            }
        }
    }
}