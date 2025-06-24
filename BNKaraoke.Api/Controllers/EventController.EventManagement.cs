using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Models;
using System.Transactions;
using Microsoft.AspNetCore.SignalR;

namespace BNKaraoke.Api.Controllers
{
    public partial class EventController
    {
        [HttpPost("create")]
        [Authorize(Roles = "EventManager")]
        public async Task<IActionResult> CreateEvent([FromBody] EventCreateDto eventDto)
        {
            try
            {
                _logger?.LogInformation("Creating event with EventCode: {EventCode}", eventDto.EventCode);
                if (!ModelState.IsValid)
                {
                    _logger?.LogWarning("Invalid model state for CreateEvent: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                if (string.IsNullOrEmpty(eventDto.EventCode))
                {
                    _logger?.LogWarning("EventCode is null or empty");
                    return BadRequest("EventCode cannot be null or empty");
                }

                if (_context == null)
                {
                    _logger?.LogError("Database context is null");
                    return StatusCode(500, new { message = "Internal server error" });
                }

                var newEvent = new Event
                {
                    EventCode = eventDto.EventCode,
                    Description = eventDto.Description ?? string.Empty,
                    Status = eventDto.Status ?? "Upcoming",
                    Visibility = eventDto.Visibility ?? "Visible",
                    Location = eventDto.Location ?? string.Empty,
                    ScheduledDate = eventDto.ScheduledDate,
                    ScheduledStartTime = eventDto.ScheduledStartTime,
                    ScheduledEndTime = eventDto.ScheduledEndTime,
                    KaraokeDJName = eventDto.KaraokeDJName ?? string.Empty,
                    IsCanceled = eventDto.IsCanceled.GetValueOrDefault(false),
                    RequestLimit = eventDto.RequestLimit,
                    SongsCompleted = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Events.Add(newEvent);
                await _context.SaveChangesAsync();

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

                _logger?.LogInformation("Created event with EventId: {EventId}", newEvent.EventId);
                return CreatedAtAction(nameof(GetEvent), new { eventId = newEvent.EventId }, eventResponse);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating event with EventCode: {EventCode}", eventDto.EventCode);
                return StatusCode(500, new { message = "Error creating event", details = ex.Message });
            }
        }

        [HttpPut("{eventId:int}/update")]
        [Authorize(Roles = "EventManager")]
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

                if (_context == null)
                {
                    _logger?.LogError("Database context is null");
                    return StatusCode(500, new { message = "Internal server error" });
                }

                var existingEvent = await _context.Events.FindAsync(eventId);
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

                var oldStatus = existingEvent.Status;
                existingEvent.EventCode = eventDto.EventCode;
                existingEvent.Description = eventDto.Description ?? existingEvent.Description;
                existingEvent.Status = eventDto.Status ?? existingEvent.Status;
                existingEvent.Visibility = eventDto.Visibility ?? existingEvent.Visibility;
                existingEvent.Location = eventDto.Location ?? existingEvent.Location;
                existingEvent.ScheduledDate = eventDto.ScheduledDate;
                existingEvent.ScheduledStartTime = eventDto.ScheduledStartTime ?? existingEvent.ScheduledStartTime;
                existingEvent.ScheduledEndTime = eventDto.ScheduledEndTime ?? existingEvent.ScheduledEndTime;
                existingEvent.KaraokeDJName = eventDto.KaraokeDJName ?? existingEvent.KaraokeDJName;
                existingEvent.IsCanceled = eventDto.IsCanceled.GetValueOrDefault(existingEvent.IsCanceled);
                existingEvent.RequestLimit = eventDto.RequestLimit;
                existingEvent.SongsCompleted = existingEvent.SongsCompleted;
                existingEvent.UpdatedAt = DateTime.UtcNow;

                if (oldStatus != existingEvent.Status)
                {
                    _logger?.LogInformation("Event status changed from {OldStatus} to {NewStatus} for EventId: {EventId}", oldStatus, existingEvent.Status, eventId);
                    var queueEntries = await _context.EventQueues
                        .Where(eq => eq.EventId == eventId)
                        .ToListAsync();

                    foreach (var entry in queueEntries)
                    {
                        entry.Status = existingEvent.Status;
                        entry.IsActive = existingEvent.Status == "Live";
                        entry.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync();

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
                    QueueCount = await _context.EventQueues.CountAsync(eq => eq.EventId == eventId),
                    SongsCompleted = existingEvent.SongsCompleted
                };

                if (_hubContext != null)
                {
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", 0, $"Status_{existingEvent.Status}");
                }

                _logger?.LogInformation("Updated event with EventId: {EventId}", eventId);
                return Ok(eventResponse);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating event with EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error updating event", details = ex.Message });
            }
        }

        [HttpPost("{eventId:int}/start")]
        [Authorize(Roles = "EventManager")]
        public async Task<IActionResult> StartEvent(int eventId)
        {
            try
            {
                _logger?.LogInformation("Starting event with EventId: {EventId}", eventId);
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

                if (eventEntity.Status != "Upcoming")
                {
                    _logger?.LogWarning("Event with EventId {EventId} is not in Upcoming status (Status: {Status})", eventId, eventEntity.Status);
                    return BadRequest("Event must be in Upcoming status to start");
                }

                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    eventEntity.Status = "Live";
                    eventEntity.UpdatedAt = DateTime.UtcNow;

                    var queueEntries = await _context.EventQueues
                        .Where(eq => eq.EventId == eventId && eq.Status == "Upcoming")
                        .ToListAsync();

                    foreach (var entry in queueEntries)
                    {
                        entry.Status = "Live";
                        entry.IsActive = true;
                        entry.UpdatedAt = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();
                    scope.Complete();
                }

                if (_hubContext != null)
                {
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", 0, "EventStarted");
                }

                _logger?.LogInformation("Started event with EventId: {EventId}", eventId);
                return Ok(new { message = "Event started" });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error starting event with EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error starting event", details = ex.Message });
            }
        }

        [HttpPost("{eventId:int}/end")]
        [Authorize(Roles = "EventManager")]
        public async Task<IActionResult> EndEvent(int eventId)
        {
            try
            {
                _logger?.LogInformation("Ending event with EventId: {EventId}", eventId);
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

                if (eventEntity.Status != "Live")
                {
                    _logger?.LogWarning("Event with EventId {EventId} is not in Live status (Status: {Status})", eventId, eventEntity.Status);
                    return BadRequest("Event must be in Live status to end");
                }

                using (var scope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                {
                    var queueEntries = await _context.EventQueues
                        .Where(eq => eq.EventId == eventId && eq.Status == "Live" && !eq.IsCurrentlyPlaying)
                        .ToListAsync();

                    foreach (var entry in queueEntries)
                    {
                        entry.Status = "Archived";
                        entry.IsActive = false;
                        entry.UpdatedAt = DateTime.UtcNow;
                    }

                    eventEntity.Status = "Archived";
                    eventEntity.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    scope.Complete();
                }

                if (_hubContext != null)
                {
                    await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", 0, "EventEnded");
                }

                _logger?.LogInformation("Ended event with EventId: {EventId}", eventId);
                return Ok(new { message = "Event ended" });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error ending event with EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error ending event", details = ex.Message });
            }
        }
    }
}