using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Models;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace BNKaraoke.Api.Controllers
{
    public partial class EventController
    {
        [HttpGet("{eventId}/attendance/status")]
        [Authorize]
        public async Task<IActionResult> GetAttendanceStatus(int eventId)
        {
            try
            {
                _logger.LogInformation("Fetching attendance status for EventId: {EventId}", eventId);
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

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == userName);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", userName, eventId);
                    return BadRequest("Requestor not found");
                }

                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);

                if (attendance == null)
                {
                    return Ok(new { isCheckedIn = false, isOnBreak = false });
                }

                return Ok(new
                {
                    isCheckedIn = attendance.IsCheckedIn,
                    isOnBreak = attendance.IsOnBreak,
                    breakStartAt = attendance.BreakStartAt,
                    breakEndAt = attendance.BreakEndAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching attendance status for EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error fetching attendance status", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/attendance/check-in")]
        [Authorize]
        public async Task<IActionResult> CheckIn(int eventId, [FromBody] AttendanceActionDto actionDto)
        {
            try
            {
                _logger.LogInformation("Checking in requestor with UserName {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for CheckIn: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
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
                    _logger.LogWarning("Cannot check in to EventId {EventId}: Event is canceled or hidden", eventId);
                    return BadRequest("Cannot check in to a canceled or hidden event");
                }

                if (eventEntity.Status != "Live")
                {
                    _logger.LogWarning("Cannot check in to EventId {EventId}: Event is not live (Status: {Status})", eventId, eventEntity.Status);
                    return BadRequest("Can only check in to live events");
                }

                if (string.IsNullOrEmpty(actionDto.RequestorId))
                {
                    _logger.LogWarning("RequestorId is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("RequestorId cannot be null or empty");
                }

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == actionDto.RequestorId);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor not found");
                }

                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);

                if (attendance == null)
                {
                    attendance = new EventAttendance
                    {
                        EventId = eventId,
                        RequestorId = requestor.Id,
                        IsCheckedIn = true,
                        IsOnBreak = false
                    };
                    _context.EventAttendances.Add(attendance);
                }
                else
                {
                    if (attendance.IsCheckedIn)
                    {
                        _logger.LogWarning("Requestor with UserName {UserName} is already checked in for EventId {EventId}", actionDto.RequestorId, eventId);
                        return BadRequest("Requestor is already checked in");
                    }

                    attendance.IsCheckedIn = true;
                    attendance.IsOnBreak = false;
                    attendance.BreakStartAt = null;
                    attendance.BreakEndAt = null;
                }

                await _context.SaveChangesAsync();

                var attendanceHistory = new EventAttendanceHistory
                {
                    EventId = eventId,
                    RequestorId = requestor.Id,
                    Action = "CheckIn",
                    ActionTimestamp = DateTime.UtcNow,
                    AttendanceId = attendance.AttendanceId
                };
                _context.EventAttendanceHistories.Add(attendanceHistory);

                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.RequestorUserName == requestor.UserName)
                    .ToListAsync();

                foreach (var entry in queueEntries)
                {
                    entry.IsActive = true;
                    entry.Status = "Live";
                    entry.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("SingerStatusUpdated", requestor.Id, eventId, $"{requestor.FirstName} {requestor.LastName}".Trim(), true, true, false);

                _logger.LogInformation("Checked in requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return Ok(new { message = "Check-in successful" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking in requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return StatusCode(500, new { message = "Error checking in", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/attendance/check-out")]
        [Authorize]
        public async Task<IActionResult> CheckOut(int eventId, [FromBody] AttendanceActionDto actionDto)
        {
            try
            {
                _logger.LogInformation("Checking out requestor with UserName {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for CheckOut: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                if (string.IsNullOrEmpty(actionDto.RequestorId))
                {
                    _logger.LogWarning("RequestorId is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("RequestorId cannot be null or empty");
                }

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == actionDto.RequestorId);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor not found");
                }

                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);
                if (attendance == null || !attendance.IsCheckedIn)
                {
                    _logger.LogWarning("Requestor with UserName {UserName} is not checked in for EventId {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor is not checked in");
                }

                attendance.IsCheckedIn = false;
                attendance.IsOnBreak = false;
                attendance.BreakStartAt = null;
                attendance.BreakEndAt = null;

                await _context.SaveChangesAsync();

                var attendanceHistory = new EventAttendanceHistory
                {
                    EventId = eventId,
                    RequestorId = requestor.Id,
                    Action = "CheckOut",
                    ActionTimestamp = DateTime.UtcNow,
                    AttendanceId = attendance.AttendanceId
                };
                _context.EventAttendanceHistories.Add(attendanceHistory);

                var queueEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.RequestorUserName == requestor.UserName)
                    .ToListAsync();

                foreach (var entry in queueEntries)
                {
                    entry.IsActive = false;
                    entry.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("SingerStatusUpdated", requestor.Id, eventId, $"{requestor.FirstName} {requestor.LastName}".Trim(), true, false, false);

                _logger.LogInformation("Checked out requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return Ok(new { message = "Check-out successful" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking out requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return StatusCode(500, new { message = "Error checking out", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/attendance/break/start")]
        [Authorize]
        public async Task<IActionResult> StartBreak(int eventId, [FromBody] AttendanceActionDto actionDto)
        {
            try
            {
                _logger.LogInformation("Starting break for requestor with UserName {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for StartBreak: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                if (string.IsNullOrEmpty(actionDto.RequestorId))
                {
                    _logger.LogWarning("RequestorId is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("RequestorId cannot be null or empty");
                }

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == actionDto.RequestorId);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor not found");
                }

                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);
                if (attendance == null || !attendance.IsCheckedIn)
                {
                    _logger.LogWarning("Requestor with UserName {UserName} must be checked in to take a break for EventId {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor must be checked in to take a break");
                }

                if (attendance.IsOnBreak)
                {
                    _logger.LogWarning("Requestor with UserName {UserName} is already on break for EventId {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor is already on break");
                }

                attendance.IsOnBreak = true;
                attendance.BreakStartAt = DateTime.UtcNow;
                attendance.BreakEndAt = null;

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("SingerStatusUpdated", requestor.Id, eventId, $"{requestor.FirstName} {requestor.LastName}".Trim(), true, true, true);

                _logger.LogInformation("Started break for requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return Ok(new { message = "Break started" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting break for requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return StatusCode(500, new { message = "Error starting break", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/attendance/break/end")]
        [Authorize]
        public async Task<IActionResult> EndBreak(int eventId, [FromBody] AttendanceActionDto actionDto)
        {
            try
            {
                _logger.LogInformation("Ending break for requestor with UserName {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for EndBreak: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return BadRequest(ModelState);
                }

                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                if (string.IsNullOrEmpty(actionDto.RequestorId))
                {
                    _logger.LogWarning("RequestorId is null or empty for EventId: {EventId}", eventId);
                    return BadRequest("RequestorId cannot be null or empty");
                }

                var requestor = await _context.Users
                    .OfType<ApplicationUser>()
                    .FirstOrDefaultAsync(u => u.UserName == actionDto.RequestorId);
                if (requestor == null)
                {
                    _logger.LogWarning("Requestor not found with UserName: {UserName} for EventId: {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor not found");
                }

                var attendance = await _context.EventAttendances
                    .FirstOrDefaultAsync(ea => ea.EventId == eventId && ea.RequestorId == requestor.Id);
                if (attendance == null || !attendance.IsCheckedIn || !attendance.IsOnBreak)
                {
                    _logger.LogWarning("Requestor with UserName {UserName} must be checked in and on break to end break for EventId {EventId}", actionDto.RequestorId, eventId);
                    return BadRequest("Requestor must be checked in and on break");
                }

                attendance.IsOnBreak = false;
                attendance.BreakEndAt = DateTime.UtcNow;

                var minPosition = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId)
                    .MinAsync(eq => (int?)eq.Position) ?? 1;

                var skippedEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && eq.RequestorUserName == requestor.UserName && eq.WasSkipped)
                    .OrderBy(eq => eq.QueueId)
                    .ToListAsync();

                for (int i = 0; i < skippedEntries.Count; i++)
                {
                    skippedEntries[i].Position = minPosition - (i + 1);
                    skippedEntries[i].WasSkipped = false;
                    skippedEntries[i].UpdatedAt = DateTime.UtcNow;
                }

                var otherEntries = await _context.EventQueues
                    .Where(eq => eq.EventId == eventId && (eq.RequestorUserName != requestor.UserName || !eq.WasSkipped))
                    .OrderBy(eq => eq.Position)
                    .ToListAsync();

                for (int i = 0; i < otherEntries.Count; i++)
                {
                    otherEntries[i].Position = minPosition + i;
                    otherEntries[i].UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("SingerStatusUpdated", requestor.Id, eventId, $"{requestor.FirstName} {requestor.LastName}".Trim(), true, true, false);

                _logger.LogInformation("Ended break for requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return Ok(new { message = "Break ended" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending break for requestor with UserName {UserName} for EventId {EventId}", actionDto.RequestorId, eventId);
                return StatusCode(500, new { message = "Error ending break", details = ex.Message });
            }
        }
    }
}