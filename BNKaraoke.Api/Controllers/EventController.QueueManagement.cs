using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Models;
using Humanizer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace BNKaraoke.Api.Controllers
{
    public partial class EventController : ControllerBase
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

                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.FindAsync(eventId);
                _logger.LogInformation("AddToQueue: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
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

                var swSong = Stopwatch.StartNew();
                var song = await _context.Songs.FindAsync(queueDto.SongId);
                _logger.LogInformation("AddToQueue: Songs query took {ElapsedMilliseconds} ms", swSong.ElapsedMilliseconds);
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

                var swUser = Stopwatch.StartNew();
                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == queueDto.RequestorUserName);
                _logger.LogInformation("AddToQueue: Users query took {ElapsedMilliseconds} ms", swUser.ElapsedMilliseconds);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", queueDto.RequestorUserName, eventId);
                    return BadRequest("Requestor not found");
                }

                var swExists = Stopwatch.StartNew();
                var exists = await _context.EventQueues.AsNoTracking().AnyAsync(q =>
                    q.EventId == eventId &&
                    q.RequestorUserName == queueDto.RequestorUserName &&
                    q.SongId == queueDto.SongId);
                _logger.LogInformation("AddToQueue: EventQueues exists query took {ElapsedMilliseconds} ms", swExists.ElapsedMilliseconds);
                if (exists)
                {
                    _logger.LogWarning("Song {SongId} already in queue for EventId {EventId} by RequestorUserName {UserName}", queueDto.SongId, eventId, queueDto.RequestorUserName);
                    return BadRequest("Song already in queue");
                }

                var swCount = Stopwatch.StartNew();
                var requestedCount = await _context.EventQueues
                    .AsNoTracking()
                    .CountAsync(eq => eq.EventId == eventId && eq.RequestorUserName == queueDto.RequestorUserName);
                _logger.LogInformation("AddToQueue: EventQueues count query took {ElapsedMilliseconds} ms", swCount.ElapsedMilliseconds);
                if (requestedCount >= eventEntity.RequestLimit)
                {
                    _logger.LogWarning("Requestor with UserName {UserName} reached request limit of {RequestLimit} for EventId {EventId}", queueDto.RequestorUserName, eventEntity.RequestLimit, eventId);
                    return BadRequest($"Request limit of {eventEntity.RequestLimit} songs reached");
                }

                if (eventEntity.Status != "Upcoming")
                {
                    var swAttendance = Stopwatch.StartNew();
                    var attendance = await _context.EventAttendances
                        .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);
                    _logger.LogInformation("AddToQueue: EventAttendances query took {ElapsedMilliseconds} ms", swAttendance.ElapsedMilliseconds);
                    if (attendance == null || !attendance.IsCheckedIn)
                    {
                        _logger.LogWarning("Requestor with UserName {UserName} must be checked in for EventId {EventId}", queueDto.RequestorUserName, eventId);
                        return BadRequest("Requestor must be checked in for non-upcoming event");
                    }
                }

                var swMaxPosition = Stopwatch.StartNew();
                var maxPosition = await _context.EventQueues
                    .AsNoTracking()
                    .Where(eq => eq.EventId == eventId)
                    .MaxAsync(eq => (int?)eq.Position) ?? 0;
                _logger.LogInformation("AddToQueue: EventQueues max position query took {ElapsedMilliseconds} ms", swMaxPosition.ElapsedMilliseconds);

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
                    YouTubeUrl = song.YouTubeUrl,
                    RequestorUserName = newQueueEntry.RequestorUserName,
                    RequestorFullName = $"{requestor.FirstName} {requestor.LastName}".Trim(),
                    Singers = singersList,
                    Position = newQueueEntry.Position,
                    Status = ComputeSongStatus(newQueueEntry, false),
                    IsActive = newQueueEntry.IsActive,
                    WasSkipped = newQueueEntry.WasSkipped,
                    IsCurrentlyPlaying = newQueueEntry.IsCurrentlyPlaying,
                    SungAt = newQueueEntry.SungAt,
                    IsOnBreak = newQueueEntry.IsOnBreak
                };

                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueEntryDto, "Added");
                _logger.LogInformation("Sent QueueUpdated for EventId={EventId}, QueueId={QueueId}, Action=Added in {TotalElapsedMilliseconds} ms", eventId, queueEntryDto.QueueId, sw.ElapsedMilliseconds);

                _logger.LogInformation("Added song to queue for EventId {EventId}, QueueId: {QueueId} in {TotalElapsedMilliseconds} ms", eventId, newQueueEntry.QueueId, sw.ElapsedMilliseconds);
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
                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId);
                _logger.LogInformation("GetEventQueue: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var swQueue = Stopwatch.StartNew();
                var queueEntries = await _context.EventQueues
                    .AsNoTracking()
                    .Where(eq => eq.EventId == eventId)
                    .Include(eq => eq.Song)
                    .ToListAsync();
                _logger.LogInformation("GetEventQueue: EventQueues query took {ElapsedMilliseconds} ms", swQueue.ElapsedMilliseconds);

                var requestorUserNames = queueEntries
                    .Select(eq => eq.RequestorUserName)
                    .Where(userName => !string.IsNullOrEmpty(userName))
                    .Distinct()
                    .ToList();
                var swUsers = Stopwatch.StartNew();
                var allUsers = await _context.Users
                    .OfType<ApplicationUser>()
                    .AsNoTracking()
                    .Where(u => u.UserName != null && requestorUserNames.Contains(u.UserName))
                    .ToListAsync();
                _logger.LogInformation("GetEventQueue: Users query took {ElapsedMilliseconds} ms", swUsers.ElapsedMilliseconds);
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

                var swSingerUsers = Stopwatch.StartNew();
                var singerUsers = await _context.Users
                    .OfType<ApplicationUser>()
                    .AsNoTracking()
                    .Where(u => u.UserName != null && singerUserNames.Contains(u.UserName))
                    .ToDictionaryAsync(u => u.UserName!, u => u);
                _logger.LogInformation("GetEventQueue: Singer Users query took {ElapsedMilliseconds} ms", swSingerUsers.ElapsedMilliseconds);

                var userIds = users.Values.Select(u => u.Id)
                    .Concat(singerUsers.Values.Select(u => u.Id))
                    .Distinct()
                    .ToList();
                var swAttendances = Stopwatch.StartNew();
                var attendances = await _context.EventAttendances
                    .AsNoTracking()
                    .Where(ea => ea.EventId == eventId && userIds.Contains(ea.RequestorId))
                    .ToDictionaryAsync(ea => ea.RequestorId, ea => ea);
                _logger.LogInformation("GetEventQueue: EventAttendances query took {ElapsedMilliseconds} ms", swAttendances.ElapsedMilliseconds);

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
                        if (singer == null || singer == "AllSing" || singer == "TheBoys" || singer == "TheGirls")
                            continue;

                        if (singerUsers.TryGetValue(singer, out var singerUser) &&
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
                        YouTubeUrl = eq.Song?.YouTubeUrl,
                        RequestorUserName = eq.RequestorUserName,
                        RequestorFullName = $"{requestor.FirstName} {requestor.LastName}".Trim(),
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

                _logger.LogInformation("Fetched {Count} queue entries for EventId: {EventId} in {TotalElapsedMilliseconds} ms: {Queue}", sortedQueueDtos.Count, eventId, sw.ElapsedMilliseconds, JsonSerializer.Serialize(sortedQueueDtos));
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

                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.FindAsync(eventId);
                _logger.LogInformation("ReorderQueue: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
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

                var swUserQueue = Stopwatch.StartNew();
                var userQueueEntries = await _context.EventQueues
                    .FromSqlRaw(
                        @"SELECT * FROM public.""EventQueues""
                          WHERE ""EventId"" = {0} AND (""RequestorUserName"" = {1} OR ""Singers"" @> {2}::jsonb)",
                        eventId, userName, $"[{userName}]"
                    )
                    .ToListAsync();
                _logger.LogInformation("ReorderQueue: User EventQueues query took {ElapsedMilliseconds} ms", swUserQueue.ElapsedMilliseconds);

                var requestQueueIds = request.NewOrder.Select(o => o.QueueId).ToList();
                var userQueueIds = userQueueEntries.Select(eq => eq.QueueId).ToList();

                if (requestQueueIds.Count != userQueueIds.Count || !requestQueueIds.All(qid => userQueueIds.Contains(qid)))
                {
                    _logger.LogWarning("Invalid reorder request: Queue IDs do not match user's queue entries for EventId {EventId}", eventId);
                    return BadRequest("Invalid reorder request: Queue IDs do not match user's queue entries");
                }

                var swAllQueues = Stopwatch.StartNew();
                var allQueueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();
                _logger.LogInformation("ReorderQueue: All EventQueues query took {ElapsedMilliseconds} ms", swAllQueues.ElapsedMilliseconds);

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

                var swFinalQueue = Stopwatch.StartNew();
                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .Include(eq => eq.Song)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();
                _logger.LogInformation("ReorderQueue: Final EventQueues query took {ElapsedMilliseconds} ms", swFinalQueue.ElapsedMilliseconds);

                var requestorUserNames = queueEntries
                    .Select(eq => eq.RequestorUserName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToList();
                var allUsers = await _context.Users
                    .Where(u => !string.IsNullOrEmpty(u.UserName) && requestorUserNames.Contains(u.UserName))
                    .ToDictionaryAsync(u => u.UserName!);

                var queueDtos = queueEntries.Select(eq =>
                {
                    var singersList = new List<string>();
                    try
                    {
                        singersList.AddRange(JsonSerializer.Deserialize<string[]>(eq.Singers) ?? Array.Empty<string>());
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
                        RequestorFullName = allUsers.ContainsKey(eq.RequestorUserName) ? $"{allUsers[eq.RequestorUserName].FirstName} {allUsers[eq.RequestorUserName].LastName}".Trim() : "",
                        Singers = singersList,
                        Position = eq.Position,
                        Status = ComputeSongStatus(eq, false),
                        IsActive = eq.IsActive,
                        WasSkipped = eq.WasSkipped,
                        IsCurrentlyPlaying = eq.IsCurrentlyPlaying,
                        SungAt = eq.SungAt,
                        IsOnBreak = eq.IsOnBreak
                    };
                }).ToList();

                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueDtos, "Reordered");
                _logger.LogInformation($"Sent QueueUpdated for EventId={eventId}, Action=Reordered, QueueCount={queueDtos.Count} in {sw.ElapsedMilliseconds} ms");

                _logger.LogInformation($"Reordered queue for EventId: {eventId} in {sw.ElapsedMilliseconds} ms");
                return Ok(new { message = "Queue reordered" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering queue for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error reordering queue", details = ex.Message });
            }
        }

        [HttpPut("{eventId}/queue/personal/reorder")]
        [Authorize]
        public async Task<IActionResult> ReorderPersonalQueue(int eventId, [FromBody] PersonalReorderQueueRequest request)
        {
            try
            {
                _logger.LogInformation("Reordering personal queue for EventId: {EventId}, User: {User}, RawPayload: {Payload}",
                    eventId, User.Identity?.Name, JsonSerializer.Serialize(request));
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for ReorderPersonalQueue: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.FindAsync(eventId);
                _logger.LogInformation("ReorderPersonalQueue: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
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

                var swUser = Stopwatch.StartNew();
                var user = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == userName);
                _logger.LogInformation("ReorderPersonalQueue: Users query took {ElapsedMilliseconds} ms", swUser.ElapsedMilliseconds);
                if (user == null)
                {
                    _logger.LogWarning("User not found with UserName: {UserName} for EventId: {EventId}", userName, eventId);
                    return BadRequest("User not found");
                }

                var swAttendance = Stopwatch.StartNew();
                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == user.Id);
                _logger.LogInformation("ReorderPersonalQueue: EventAttendances query took {ElapsedMilliseconds} ms", swAttendance.ElapsedMilliseconds);
                if (eventEntity.Status != "Upcoming" && (attendance == null || !attendance.IsCheckedIn))
                {
                    _logger.LogWarning("User with UserName {UserName} must be checked in for EventId {EventId}", userName, eventId);
                    return BadRequest("User must be checked in to reorder queue");
                }

                var swUserQueue = Stopwatch.StartNew();
                var userQueueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.RequestorUserName == userName && eq.SungAt == null && !eq.WasSkipped && !eq.IsCurrentlyPlaying)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();
                _logger.LogInformation("ReorderPersonalQueue: User EventQueues query took {ElapsedMilliseconds} ms", swUserQueue.ElapsedMilliseconds);
                var requestQueueIds = request.Reorder.Select(o => o.QueueId).ToList();
                var userQueueIds = userQueueEntries.Select(eq => eq.QueueId).ToList();

                if (requestQueueIds.Count != userQueueIds.Count || !requestQueueIds.All(qid => userQueueIds.Contains(qid)))
                {
                    _logger.LogWarning("Invalid reorder request: Queue IDs do not match user's queue entries for EventId {EventId}, User: {User}, RequestQueueIds: {RequestIds}, UserQueueIds: {UserIds}",
                        eventId, userName, JsonSerializer.Serialize(requestQueueIds), JsonSerializer.Serialize(userQueueIds));
                    return BadRequest("Invalid reorder request: Queue IDs do not match user's queue entries");
                }

                var requestedSlots = request.Reorder.Select(o => o.NewSlot).ToList();
                var currentSlots = userQueueEntries.Select(eq => eq.Position).ToList();
                if (!requestedSlots.OrderBy(s => s).SequenceEqual(currentSlots.OrderBy(s => s)))
                {
                    _logger.LogWarning("Invalid reorder request: Requested slots do not match user's assigned slots for EventId {EventId}, User: {User}, RequestedSlots: {RequestedSlots}, CurrentSlots: {CurrentSlots}",
                        eventId, userName, JsonSerializer.Serialize(requestedSlots), JsonSerializer.Serialize(currentSlots));
                    return BadRequest("Invalid reorder request: Requested slots do not match user's assigned slots");
                }

                _logger.LogInformation("Reordering personal queue: EventId={EventId}, User={User}, OriginalPositions={Original}, RequestedSwaps={Requested}",
                    eventId, userName, JsonSerializer.Serialize(userQueueEntries.Select(eq => new { eq.QueueId, eq.Position })),
                    JsonSerializer.Serialize(request.Reorder));

                foreach (var order in request.Reorder)
                {
                    var queueEntry = userQueueEntries.FirstOrDefault(eq => eq.QueueId == order.QueueId);
                    if (queueEntry != null)
                    {
                        queueEntry.Position = order.NewSlot;
                        queueEntry.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync();

                var swFinalQueue = Stopwatch.StartNew();
                var updatedQueue = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .Include(eq => eq.Song)
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();
                _logger.LogInformation("ReorderPersonalQueue: Final EventQueues query took {ElapsedMilliseconds} ms", swFinalQueue.ElapsedMilliseconds);

                var requestorUserNames = updatedQueue
                    .Select(eq => eq.RequestorUserName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToList();
                var allUsers = await _context.Users
                    .Where(u => !string.IsNullOrEmpty(u.UserName) && requestorUserNames.Contains(u.UserName))
                    .ToDictionaryAsync(u => u.UserName!);

                var queueDtos = updatedQueue.Select(eq =>
                {
                    var singersList = new List<string>();
                    try
                    {
                        singersList.AddRange(JsonSerializer.Deserialize<string[]>(eq.Singers) ?? Array.Empty<string>());
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
                        RequestorFullName = allUsers.ContainsKey(eq.RequestorUserName) ? $"{allUsers[eq.RequestorUserName].FirstName} {allUsers[eq.RequestorUserName].LastName}".Trim() : "",
                        Singers = singersList,
                        Position = eq.Position,
                        Status = ComputeSongStatus(eq, false),
                        IsActive = eq.IsActive,
                        WasSkipped = eq.WasSkipped,
                        IsCurrentlyPlaying = eq.IsCurrentlyPlaying,
                        SungAt = eq.SungAt,
                        IsOnBreak = eq.IsOnBreak
                    };
                }).ToList();

                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueDtos, "Reordered");
                _logger.LogInformation($"Sent QueueUpdated for EventId={eventId}, Action=Reordered, QueueCount={queueDtos.Count} in {sw.ElapsedMilliseconds} ms");

                _logger.LogInformation($"Personal queue reordered for EventId: {eventId}, User: {userName}, NewPositions={JsonSerializer.Serialize(queueDtos.Where(q => q.RequestorUserName == userName).Select(q => new { q.QueueId, q.Position }))} in {sw.ElapsedMilliseconds} ms");

                return Ok(new { message = "Personal Queue reordered", queue = queueDtos.OrderBy(q => q.Position) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering personal queue for EventId: {EventId}, User: {User}", eventId, User.Identity?.Name);
                return StatusCode(500, new { message = "Error reordering personal queue", details = ex.Message });
            }
        }

        [HttpPost("{eventId:int}/queue/{queueId:int}/singers")]
        [Authorize]
        public async Task<IActionResult> UpdateQueueSingers(int eventId, int queueId, [FromBody] UpdateQueueSingersDto singersDto)
        {
            try
            {
                _logger.LogInformation("Updating singers for QueueId {QueueId} in EventId: {EventId}", queueId, eventId);
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for UpdateQueueSingers: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.FindAsync(eventId);
                _logger.LogInformation("UpdateQueueSingers: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var swQueue = Stopwatch.StartNew();
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                _logger.LogInformation("UpdateQueueSingers: EventQueues query took {ElapsedMilliseconds} ms", swQueue.ElapsedMilliseconds);
                if (queueEntry == null)
                {
                    _logger.LogWarning("Queue entry not found with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }

                var userName = User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userName))
                {
                    _logger.LogWarning("User identity not found in token for EventId: {EventId}", eventId);
                    return Unauthorized("User identity not found");
                }

                var swRequestor = Stopwatch.StartNew();
                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                _logger.LogInformation("UpdateQueueSingers: Requestor Users query took {ElapsedMilliseconds} ms", swRequestor.ElapsedMilliseconds);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", queueEntry.RequestorUserName, eventId);
                    return BadRequest("Requestor not found");
                }

                if (queueEntry.RequestorUserName != userName)
                {
                    try
                    {
                        var currentSingers = JsonSerializer.Deserialize<string[]>(queueEntry.Singers) ?? Array.Empty<string>();
                        if (!currentSingers.Contains(userName ?? ""))
                        {
                            _logger.LogWarning("User with UserName {UserName} is not authorized to update singers for QueueId {QueueId}", userName, queueId);
                            return Forbid("Only the requestor or a singer can update singers");
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueId, ex.Message);
                        return StatusCode(500, new { message = "Error processing singers data", details = ex.Message });
                    }
                }

                var newSingers = singersDto.Singers ?? new List<string>();
                foreach (var singer in newSingers)
                {
                    if (singer == null || singer == "AllSing" || singer == "TheBoys" || singer == "TheGirls")
                        continue;

                    var swSinger = Stopwatch.StartNew();
                    var singerUser = await _context.Users
                        .OfType<ApplicationUser>()
                        .FirstOrDefaultAsync(u => u.UserName == singer);
                    _logger.LogInformation("UpdateQueueSingers: Singer Users query took {ElapsedMilliseconds} ms", swSinger.ElapsedMilliseconds);
                    if (singerUser == null)
                    {
                        _logger.LogWarning("Singer not found with UserName: {UserName} for EventId: {EventId}", singer, eventId);
                        return BadRequest($"Singer not found: {singer}");
                    }

                    var swAttendance = Stopwatch.StartNew();
                    var attendance = await _context.EventAttendances
                        .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == singerUser.Id);
                    _logger.LogInformation("UpdateQueueSingers: EventAttendances query took {ElapsedMilliseconds} ms", swAttendance.ElapsedMilliseconds);
                    if (eventEntity.Status != "Upcoming" && (attendance == null || !attendance.IsCheckedIn))
                    {
                        _logger.LogWarning("Singer with UserName {UserName} must be checked in for EventId {EventId}", singer, eventId);
                        return BadRequest($"Singer must be checked in: {singer}");
                    }
                }

                if (!newSingers.Any(s => s == queueEntry.RequestorUserName || s == "AllSing" || s == "TheBoys" || s == "TheGirls"))
                {
                    _logger.LogWarning("RequestorUserName {UserName} must be included in singers list for QueueId {QueueId}", queueEntry.RequestorUserName, queueId);
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
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueId, ex.Message);
                }

                var queueEntryDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = queueEntry.Song?.YouTubeUrl,
                    RequestorUserName = queueEntry.RequestorUserName,
                    RequestorFullName = $"{requestor.FirstName} {requestor.LastName}".Trim(),
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = ComputeSongStatus(queueEntry, false),
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak
                };

                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueEntryDto, "SingersUpdated");
                _logger.LogInformation("Sent QueueUpdated for EventId={EventId}, QueueId={QueueId}, Action=SingersUpdated in {TotalElapsedMilliseconds} ms", eventId, queueEntryDto.QueueId, sw.ElapsedMilliseconds);

                _logger.LogInformation("Updated singers for QueueId {QueueId} in EventId {EventId} in {TotalElapsedMilliseconds} ms", queueId, eventId, sw.ElapsedMilliseconds);
                return Ok(queueEntryDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating singers for QueueId {QueueId} in EventId {EventId}", queueId, eventId);
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
                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.FindAsync(eventId);
                _logger.LogInformation("SkipSong: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var swQueue = Stopwatch.StartNew();
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                _logger.LogInformation("SkipSong: EventQueues query took {ElapsedMilliseconds} ms", swQueue.ElapsedMilliseconds);
                if (queueEntry == null)
                {
                    _logger.LogWarning("Queue entry not found with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }

                var swRequestor = Stopwatch.StartNew();
                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == queueEntry.RequestorUserName);
                _logger.LogInformation("SkipSong: Requestor Users query took {ElapsedMilliseconds} ms", swRequestor.ElapsedMilliseconds);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", queueEntry.RequestorUserName, eventId);
                    return BadRequest("Requestor not found");
                }

                var swAttendance = Stopwatch.StartNew();
                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);
                _logger.LogInformation("SkipSong: EventAttendances query took {ElapsedMilliseconds} ms", swAttendance.ElapsedMilliseconds);
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

                var singersList = new List<string>();
                try
                {
                    singersList.AddRange(JsonSerializer.Deserialize<string[]>(queueEntry.Singers) ?? Array.Empty<string>());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Singers for QueueId {QueueId}: {Message}", queueId, ex.Message);
                }

                var queueEntryDto = new EventQueueDto
                {
                    QueueId = queueEntry.QueueId,
                    EventId = queueEntry.EventId,
                    SongId = queueEntry.SongId,
                    SongTitle = queueEntry.Song?.Title ?? string.Empty,
                    SongArtist = queueEntry.Song?.Artist ?? string.Empty,
                    YouTubeUrl = queueEntry.Song?.YouTubeUrl,
                    RequestorUserName = queueEntry.RequestorUserName,
                    RequestorFullName = $"{requestor.FirstName} {requestor.LastName}".Trim(),
                    Singers = singersList,
                    Position = queueEntry.Position,
                    Status = ComputeSongStatus(queueEntry, false),
                    IsActive = queueEntry.IsActive,
                    WasSkipped = queueEntry.WasSkipped,
                    IsCurrentlyPlaying = queueEntry.IsCurrentlyPlaying,
                    SungAt = queueEntry.SungAt,
                    IsOnBreak = queueEntry.IsOnBreak
                };

                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueEntryDto, "Skipped");
                _logger.LogInformation("Sent QueueUpdated for EventId={EventId}, QueueId={QueueId}, Action=Skipped in {TotalElapsedMilliseconds} ms", eventId, queueEntryDto.QueueId, sw.ElapsedMilliseconds);

                _logger.LogInformation("Skipped song with QueueId {QueueId} for EventId {EventId} in {TotalElapsedMilliseconds} ms", queueId, eventId, sw.ElapsedMilliseconds);
                return Ok(new { message = "Song skipped" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error skipping song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return StatusCode(500, new { message = "Error skipping song", details = ex.Message });
            }
        }
    }

    public class ReorderQueueRequest
    {
        public List<QueueOrderItem> NewOrder { get; set; } = new List<QueueOrderItem>();
    }

    public class PersonalReorderQueueRequest
    {
        public List<PersonalQueueOrderItem> Reorder { get; set; } = new List<PersonalQueueOrderItem>();
    }

    public class QueueOrderItem
    {
        public int QueueId { get; set; }
        public int Position { get; set; }
    }

    public class PersonalQueueOrderItem
    {
        public int QueueId { get; set; }
        public int OldSlot { get; set; }
        public int NewSlot { get; set; }
    }

    public class EventQueueCreateDto
    {
        public int SongId { get; set; }
        public string RequestorUserName { get; set; } = string.Empty;
    }

    public class UpdateQueueSingersDto
    {
        public List<string>? Singers { get; set; }
    }
}