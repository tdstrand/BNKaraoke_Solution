using BNKaraoke.Api.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Models;
using Npgsql;

namespace BNKaraoke.Api.Controllers
{
    public partial class EventController
    {
        [HttpGet("manage")]
        [Authorize(Roles = RoleConstants.EventManagementRolesCsv)]
        public async Task<IActionResult> GetManageEvents()
        {
            try
            {
                var userName = User.Identity?.Name ?? "Unknown";
                _logger?.LogInformation("Fetching events for management by UserName: {UserName}", userName);
                var sw = Stopwatch.StartNew();
                var events = await _context.Events
                    .AsNoTracking()
                    .Select(e => new EventDto
                    {
                        EventId = e.EventId,
                        EventCode = e.EventCode,
                        Description = e.Description,
                        Status = e.Status,
                        Visibility = e.Visibility,
                        Location = e.Location,
                        ScheduledDate = e.ScheduledDate,
                        ScheduledStartTime = e.ScheduledStartTime,
                        ScheduledEndTime = e.ScheduledEndTime,
                        KaraokeDJName = e.KaraokeDJName,
                        IsCanceled = e.IsCanceled,
                        RequestLimit = e.RequestLimit,
                        QueueCount = e.EventQueues.Count,
                        SongsCompleted = e.SongsCompleted
                    })
                    .OrderBy(e => e.ScheduledDate)
                    .ToListAsync();
                _logger?.LogInformation("GetManageEvents: Events query took {ElapsedMilliseconds} ms, fetched {Count} events: {Events}", sw.ElapsedMilliseconds, events.Count, JsonSerializer.Serialize(events));
                return Ok(events);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching events for management by UserName: {UserName}", User.Identity?.Name ?? "Unknown");
                return StatusCode(500, new { message = "Error fetching events for management", details = ex.Message });
            }
        }

        [HttpPost("create")]
        [Authorize(Roles = RoleConstants.EventManagementRolesCsv)]
        public async Task<IActionResult> CreateEvent([FromBody] EventCreateDto eventDto)
        {
            try
            {
                // Log request body safely using EnableBuffering
                string rawBody = string.Empty;
                string rawScheduledEndTime = "Unknown";
                if (Request.Body.CanRead)
                {
                    Request.EnableBuffering();
                    using (var reader = new StreamReader(Request.Body, Encoding.UTF8, false, 1024, true))
                    {
                        rawBody = await reader.ReadToEndAsync();
                        _logger?.LogDebug("CreateEvent: Raw request body: {RawBody}", rawBody.Length > 1000 ? rawBody.Substring(0, 1000) + "..." : rawBody);
                        Request.Body.Position = 0; // Reset for model binding (safe with buffering)
                    }
                    try
                    {
                        var jsonDoc = JsonSerializer.Deserialize<JsonElement>(rawBody);
                        rawScheduledEndTime = jsonDoc.TryGetProperty("scheduledEndTime", out var endTimeProp) ? endTimeProp.GetString() ?? "Null" : "Missing";
                    }
                    catch (JsonException ex)
                    {
                        _logger?.LogWarning("Failed to parse raw request body for scheduledEndTime: {Error}", ex.Message);
                    }
                }

                _logger?.LogInformation("Creating event with EventCode: {EventCode}, ScheduledStartTime: {ScheduledStartTime}, ScheduledEndTime: {ScheduledEndTime}, RawScheduledEndTime: {RawScheduledEndTime}",
                    eventDto?.EventCode, eventDto?.ScheduledStartTime, eventDto?.ScheduledEndTime, rawScheduledEndTime);
                if (!ModelState.IsValid)
                {
                    _logger?.LogWarning("Invalid model state for CreateEvent: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                if (string.IsNullOrEmpty(eventDto?.EventCode))
                {
                    _logger?.LogWarning("EventCode is null or empty");
                    return BadRequest(new { error = "EventCode cannot be null or empty" });
                }

                // Validate ScheduledStartTime and ScheduledEndTime
                if (eventDto.ScheduledStartTime.HasValue)
                {
                    var startTime = eventDto.ScheduledStartTime.Value;
                    if (startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays(1))
                    {
                        _logger?.LogWarning("Invalid ScheduledStartTime: {ScheduledStartTime}. Must be between 00:00:00 and 23:59:59.", startTime);
                        return BadRequest(new { error = "ScheduledStartTime must be a valid time (e.g., '18:00:00') between 00:00:00 and 23:59:59" });
                    }
                }
                if (eventDto.ScheduledEndTime.HasValue)
                {
                    var endTime = eventDto.ScheduledEndTime.Value;
                    if (endTime < TimeSpan.Zero || endTime >= TimeSpan.FromDays(2))
                    {
                        _logger?.LogWarning("Invalid ScheduledEndTime: {ScheduledEndTime}. Must be between 00:00:00 and 47:59:59.", endTime);
                        return BadRequest(new { error = "ScheduledEndTime must be a valid time (e.g., '02:00:00' for next day) between 00:00:00 and 47:59:59" });
                    }
                    // Ensure end time is after start time
                    if (eventDto.ScheduledStartTime.HasValue && eventDto.ScheduledEndTime.HasValue)
                    {
                        var startTime = eventDto.ScheduledStartTime.Value;
                        var endTimeAdjusted = eventDto.ScheduledEndTime.Value;
                        if (endTimeAdjusted < startTime)
                            endTimeAdjusted += TimeSpan.FromDays(1);
                        if (endTimeAdjusted <= startTime)
                        {
                            _logger?.LogWarning("ScheduledEndTime {ScheduledEndTime} is not after ScheduledStartTime {ScheduledStartTime}",
                                eventDto.ScheduledEndTime, eventDto.ScheduledStartTime);
                            return BadRequest(new { error = "ScheduledEndTime must be after ScheduledStartTime" });
                        }
                    }
                }

                var newEvent = new Event
                {
                    EventCode = eventDto.EventCode,
                    Description = eventDto.Description ?? string.Empty,
                    Status = eventDto.Status ?? "Upcoming",
                    Visibility = eventDto.Visibility ?? "Visible",
                    Location = eventDto.Location ?? string.Empty,
                    ScheduledDate = DateTime.SpecifyKind(eventDto.ScheduledDate.Date, DateTimeKind.Utc),
                    ScheduledStartTime = eventDto.ScheduledStartTime,
                    ScheduledEndTime = eventDto.ScheduledEndTime,
                    KaraokeDJName = eventDto.KaraokeDJName ?? string.Empty,
                    IsCanceled = eventDto.IsCanceled.GetValueOrDefault(false),
                    RequestLimit = eventDto.RequestLimit,
                    SongsCompleted = 0,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                };

                var sw = Stopwatch.StartNew();
                _context.Events.Add(newEvent);
                await _context.SaveChangesAsync();
                _logger?.LogInformation("CreateEvent: Event '{EventCode}' created in {TotalElapsedMilliseconds} ms", newEvent.EventCode, sw.ElapsedMilliseconds);

                var eventResponse = new EventDto
                {
                    EventId = newEvent.EventId,
                    EventCode = newEvent.EventCode,
                    Description = newEvent.Description,
                    Status = newEvent.Status,
                    Visibility = newEvent.Visibility,
                    Location = newEvent.Location,
                    ScheduledDate = newEvent.ScheduledDate,
                    ScheduledStartTime = newEvent.ScheduledStartTime,
                    ScheduledEndTime = newEvent.ScheduledEndTime,
                    KaraokeDJName = newEvent.KaraokeDJName,
                    IsCanceled = newEvent.IsCanceled,
                    RequestLimit = newEvent.RequestLimit,
                    QueueCount = 0,
                    SongsCompleted = newEvent.SongsCompleted
                };

                return CreatedAtAction(nameof(GetManageEvents), new { eventId = newEvent.EventId }, eventResponse);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating event with EventCode: {EventCode}", eventDto?.EventCode ?? "Unknown");
                return StatusCode(500, new { error = "Failed to create event", details = ex.Message });
            }
        }

        [HttpPut("{eventId:int}/update")]
        [Authorize(Roles = RoleConstants.EventManagementRolesCsv)]
        public async Task<IActionResult> UpdateEvent(int eventId, [FromBody] EventUpdateDto eventDto)
        {
            try
            {
                _logger?.LogInformation("Updating event with EventId: {EventId}", eventId);
                if (!ModelState.IsValid)
                {
                    _logger?.LogWarning("Invalid model state for UpdateEvent: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var sw = Stopwatch.StartNew();
                var existingEvent = await _context.Events.FindAsync(eventId);
                _logger?.LogInformation("UpdateEvent: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (existingEvent == null)
                {
                    _logger?.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                if (string.IsNullOrEmpty(eventDto.EventCode))
                {
                    _logger?.LogWarning("EventCode is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("EventCode cannot be null or empty");
                }

                // Validate ScheduledStartTime and ScheduledEndTime
                if (eventDto.ScheduledStartTime.HasValue)
                {
                    var startTime = eventDto.ScheduledStartTime.Value;
                    if (startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays(1))
                    {
                        _logger?.LogWarning("Invalid ScheduledStartTime: {ScheduledStartTime}. Must be between 00:00:00 and 23:59:59.", startTime);
                        return BadRequest(new { error = "ScheduledStartTime must be a valid time (e.g., '18:00:00') between 00:00:00 and 23:59:59" });
                    }
                }
                if (eventDto.ScheduledEndTime.HasValue)
                {
                    var endTime = eventDto.ScheduledEndTime.Value;
                    if (endTime < TimeSpan.Zero || endTime >= TimeSpan.FromDays(2))
                    {
                        _logger?.LogWarning("Invalid ScheduledEndTime: {ScheduledEndTime}. Must be between 00:00:00 and 47:59:59.", endTime);
                        return BadRequest(new { error = "ScheduledEndTime must be a valid time (e.g., '02:00:00' for next day) between 00:00:00 and 47:59:59" });
                    }
                    // Ensure end time is after start time
                    if (eventDto.ScheduledStartTime.HasValue && eventDto.ScheduledEndTime.HasValue)
                    {
                        var startTime = eventDto.ScheduledStartTime.Value;
                        var endTimeAdjusted = eventDto.ScheduledEndTime.Value;
                        if (endTimeAdjusted < startTime)
                            endTimeAdjusted += TimeSpan.FromDays(1);
                        if (endTimeAdjusted <= startTime)
                        {
                            _logger?.LogWarning("ScheduledEndTime {ScheduledEndTime} is not after ScheduledStartTime {ScheduledStartTime}",
                                eventDto.ScheduledEndTime, eventDto.ScheduledStartTime);
                            return BadRequest(new { error = "ScheduledEndTime must be after ScheduledStartTime" });
                        }
                    }
                }

                var oldStatus = existingEvent.Status;
                existingEvent.EventCode = eventDto.EventCode;
                existingEvent.Description = eventDto.Description ?? existingEvent.Description;
                existingEvent.Status = eventDto.Status ?? existingEvent.Status;
                existingEvent.Visibility = eventDto.Visibility ?? existingEvent.Visibility;
                existingEvent.Location = eventDto.Location ?? existingEvent.Location;
                existingEvent.ScheduledDate = DateTime.SpecifyKind(eventDto.ScheduledDate.Date, DateTimeKind.Utc);
                existingEvent.ScheduledStartTime = eventDto.ScheduledStartTime ?? existingEvent.ScheduledStartTime;
                existingEvent.ScheduledEndTime = eventDto.ScheduledEndTime ?? existingEvent.ScheduledEndTime;
                existingEvent.KaraokeDJName = eventDto.KaraokeDJName ?? existingEvent.KaraokeDJName;
                existingEvent.IsCanceled = eventDto.IsCanceled.GetValueOrDefault(existingEvent.IsCanceled);
                existingEvent.RequestLimit = eventDto.RequestLimit;
                existingEvent.SongsCompleted = existingEvent.SongsCompleted;
                existingEvent.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                if (oldStatus != existingEvent.Status)
                {
                    _logger?.LogInformation("Event status changed from {OldStatus} to {NewStatus} for EventId: {EventId}", oldStatus, existingEvent.Status, eventId);
                    var swQueue = Stopwatch.StartNew();
                    var queueEntries = await _context.EventQueues
                        .Where(eq => eq.EventId == eventId)
                        .ToListAsync();
                    _logger?.LogInformation("UpdateEvent: EventQueues query took {ElapsedMilliseconds} ms", swQueue.ElapsedMilliseconds);

                    foreach (var entry in queueEntries)
                    {
                        entry.Status = existingEvent.Status;
                        entry.IsActive = existingEvent.Status == "Live";
                        entry.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                    }
                }

                await _context.SaveChangesAsync();

                var swQueueCount = Stopwatch.StartNew();
                var queueCount = await _context.EventQueues.AsNoTracking().CountAsync(eq => eq.EventId == eventId);
                _logger?.LogInformation("UpdateEvent: EventQueues count query took {ElapsedMilliseconds} ms", swQueueCount.ElapsedMilliseconds);

                var eventResponse = new EventDto
                {
                    EventId = existingEvent.EventId,
                    EventCode = existingEvent.EventCode,
                    Description = existingEvent.Description,
                    Status = existingEvent.Status,
                    Visibility = existingEvent.Visibility,
                    Location = existingEvent.Location,
                    ScheduledDate = existingEvent.ScheduledDate,
                    ScheduledStartTime = existingEvent.ScheduledStartTime,
                    ScheduledEndTime = existingEvent.ScheduledEndTime,
                    KaraokeDJName = existingEvent.KaraokeDJName,
                    IsCanceled = existingEvent.IsCanceled,
                    RequestLimit = existingEvent.RequestLimit,
                    QueueCount = queueCount,
                    SongsCompleted = existingEvent.SongsCompleted
                };

                if (_hubContext != null)
                {
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("EventUpdated", eventResponse);
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = 0, action = $"Status_{existingEvent.Status}" });
                }

                _logger?.LogInformation("Updated event with EventId: {EventId} in {TotalElapsedMilliseconds} ms", eventId, sw.ElapsedMilliseconds);
                return Ok(eventResponse);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating event with EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error updating event", details = ex.Message });
            }
        }

        [HttpPost("{eventId:int}/start")]
        [Authorize(Roles = RoleConstants.EventManagementRolesCsv)]
        public async Task<IActionResult> StartEvent(int eventId)
        {
            try
            {
                _logger?.LogInformation("Starting event with EventId: {EventId}", eventId);
                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.FindAsync(eventId);
                _logger?.LogInformation("StartEvent: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (eventEntity == null)
                {
                    _logger?.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                if (eventEntity.Status != "Upcoming")
                {
                    _logger?.LogWarning("Event with EventId {EventId} is not in Upcoming status (Status: {Status})", eventId, eventEntity.Status);
                    return BadRequest("Event must be in Upcoming status to start");
                }

                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    eventEntity.Status = "Live";
                    eventEntity.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                    var swQueue = Stopwatch.StartNew();
                    var queueEntries = await _context.EventQueues
                        .Where(eq => eq.EventId == eventId && eq.Status == "Upcoming")
                        .ToListAsync();
                    _logger?.LogInformation("StartEvent: EventQueues query took {ElapsedMilliseconds} ms", swQueue.ElapsedMilliseconds);

                    foreach (var entry in queueEntries)
                    {
                        entry.Status = "Live";
                        entry.IsActive = true;
                        entry.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                    }

                    await _context.SaveChangesAsync();
                    scope.Complete();
                }

                if (_hubContext != null)
                {
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = 0, action = "EventStarted" });
                }

                _logger?.LogInformation("Started event with EventId: {EventId} in {TotalElapsedMilliseconds} ms", eventId, sw.ElapsedMilliseconds);
                return Ok(new { message = "Event started" });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error starting event with EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error starting event", details = ex.Message });
            }
        }

        [HttpPost("{eventId:int}/end")]
        [Authorize(Roles = RoleConstants.EventManagementRolesCsv)]
        public async Task<IActionResult> EndEvent(int eventId)
        {
            try
            {
                _logger?.LogInformation("Ending event with EventId: {EventId}", eventId);
                var sw = Stopwatch.StartNew();
                var eventEntity = await _context.Events.FindAsync(eventId);
                _logger?.LogInformation("EndEvent: Events query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (eventEntity == null)
                {
                    _logger?.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                if (eventEntity.Status != "Live")
                {
                    _logger?.LogWarning("Event with EventId {EventId} is not in Live status (Status: {Status})", eventId, eventEntity.Status);
                    return BadRequest("Event must be in Live status to end");
                }

                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    var swQueue = Stopwatch.StartNew();
                    var queueEntries = await _context.EventQueues
                        .Where(eq => eq.EventId == eventId && eq.Status == "Live" && !eq.IsCurrentlyPlaying)
                        .ToListAsync();
                    _logger?.LogInformation("EndEvent: EventQueues query took {ElapsedMilliseconds} ms", swQueue.ElapsedMilliseconds);

                    foreach (var entry in queueEntries)
                    {
                        entry.Status = "Archived";
                        entry.IsActive = false;
                        entry.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                    }

                    eventEntity.Status = "Archived";
                    eventEntity.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                    await _context.SaveChangesAsync();
                    scope.Complete();
                }

                if (_hubContext != null)
                {
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", new { data = 0, action = "EventEnded" });
                }

                _logger?.LogInformation("Ended event with EventId: {EventId} in {TotalElapsedMilliseconds} ms", eventId, sw.ElapsedMilliseconds);
                return Ok(new { message = "Event ended" });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error ending event with EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error ending event", details = ex.Message });
            }
        }

        [HttpDelete("{eventId:int}")]
        [Authorize(Roles = RoleConstants.EventManagementRolesCsv)]
        public async Task<IActionResult> DeleteEvent(int eventId)
        {
            try
            {
                _logger?.LogInformation("DeleteEvent requested for EventId: {EventId}", eventId);
                var swExists = Stopwatch.StartNew();
                var eventExists = await _context.Events.AsNoTracking().AnyAsync(e => e.EventId == eventId);
                _logger?.LogInformation("DeleteEvent: existence check completed in {ElapsedMilliseconds} ms", swExists.ElapsedMilliseconds);
                if (!eventExists)
                {
                    _logger?.LogWarning("DeleteEvent: EventId {EventId} not found", eventId);
                    return NotFound("Event not found");
                }

                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var sw = Stopwatch.StartNew();
                    var deletedQueues = await _context.EventQueues
                        .Where(eq => eq.EventId == eventId)
                        .ExecuteDeleteAsync();
                    var deletedSingerStatuses = await _context.SingerStatus
                        .Where(ss => ss.EventId == eventId)
                        .ExecuteDeleteAsync();
                    var deletedHistories = await _context.EventAttendanceHistories
                        .Where(eah => eah.EventId == eventId)
                        .ExecuteDeleteAsync();
                    var deletedAttendances = await _context.EventAttendances
                        .Where(ea => ea.EventId == eventId)
                        .ExecuteDeleteAsync();
                    var deletedQueueItems = 0;
                    try
                    {
                        deletedQueueItems = await _context.QueueItems
                            .Where(q => q.EventId == eventId)
                            .ExecuteDeleteAsync();
                    }
                    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
                    {
                        _logger?.LogWarning(ex, "DeleteEvent: QueueItems table not found. Assuming partitioned event queues and skipping legacy cleanup for EventId {EventId}", eventId);
                    }
                    var deletedEvents = await _context.Events
                        .Where(e => e.EventId == eventId)
                        .ExecuteDeleteAsync();

                    if (deletedEvents == 0)
                    {
                        await transaction.RollbackAsync();
                        _logger?.LogWarning("DeleteEvent: EventId {EventId} removed by concurrent operation", eventId);
                        return NotFound("Event not found");
                    }

                    await transaction.CommitAsync();

                    _logger?.LogInformation(
                        "DeleteEvent: EventId {EventId} deleted in {ElapsedMilliseconds} ms. Removed {QueueCount} queue entries, {AttendanceCount} attendances, {HistoryCount} attendance histories, {SingerStatusCount} singer statuses, {QueueItemsCount} queue items.",
                        eventId,
                        sw.ElapsedMilliseconds,
                        deletedQueues,
                        deletedAttendances,
                        deletedHistories,
                        deletedSingerStatuses,
                        deletedQueueItems);

                    return Ok(new
                    {
                        message = "Event deleted",
                        deletedQueues,
                        deletedAttendances,
                        deletedHistories,
                        deletedSingerStatuses,
                        deletedQueueItems
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger?.LogError(ex, "DeleteEvent: Error deleting EventId {EventId}", eventId);
                    return StatusCode(500, new { message = "Error deleting event", details = ex.Message });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteEvent: Unexpected error for EventId {EventId}", eventId);
                return StatusCode(500, new { message = "Error deleting event", details = ex.Message });
            }
        }
    }
}