using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using Microsoft.AspNetCore.SignalR;
using BNKaraoke.Api.Hubs;

namespace BNKaraoke.Api.Controllers
{
    public partial class EventController
    {
        [HttpPost("{eventId}/queue/{queueId}/play")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> PlaySong(int eventId, int queueId)
        {
            try
            {
                _logger.LogInformation("Playing song with QueueId {QueueId} for EventId: {EventId}", queueId, eventId);
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

                queueEntry.IsCurrentlyPlaying = true;
                queueEntry.Status = "Live";
                queueEntry.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueuePlaying", queueId, eventId);

                _logger.LogInformation("Played song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return Ok(new { message = "Song playing" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error playing song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return StatusCode(500, new { message = "Error playing song", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/queue/{queueId}/pause")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> PauseSong(int eventId, int queueId)
        {
            try
            {
                _logger.LogInformation("Pausing song with QueueId {QueueId} for EventId: {EventId}", queueId, eventId);
                var queueEntry = await _context.EventQueues
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("Queue entry not found with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }

                queueEntry.IsCurrentlyPlaying = false;
                queueEntry.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueId, "Paused");

                _logger.LogInformation("Paused song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return Ok(new { message = "Song paused" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return StatusCode(500, new { message = "Error pausing song", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/queue/{queueId}/stop")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> StopSong(int eventId, int queueId)
        {
            try
            {
                _logger.LogInformation("Stopping song with QueueId {QueueId} for EventId: {EventId}", queueId, eventId);
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

                queueEntry.IsCurrentlyPlaying = false;
                queueEntry.SungAt = DateTime.UtcNow;
                queueEntry.Status = "Archived";
                queueEntry.UpdatedAt = DateTime.UtcNow;

                eventEntity.SongsCompleted++;

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueueUpdated", queueId, "Stopped");

                _logger.LogInformation("Stopped song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return Ok(new { message = "Song stopped" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping song with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                return StatusCode(500, new { message = "Error stopping song", details = ex.Message });
            }
        }

        [HttpPost("{eventId}/queue/{queueId}/launch")]
        [Authorize(Roles = "Karaoke DJ")]
        public async Task<IActionResult> LaunchVideo(int eventId, int queueId)
        {
            try
            {
                _logger.LogInformation("Launching video for QueueId {QueueId} in EventId: {EventId}", queueId, eventId);
                var queueEntry = await _context.EventQueues
                    .Include(eq => eq.Song)
                    .FirstOrDefaultAsync(eq => eq.EventId == eventId && eq.QueueId == queueId);
                if (queueEntry == null)
                {
                    _logger.LogWarning("Queue entry not found with QueueId {QueueId} for EventId {EventId}", queueId, eventId);
                    return NotFound("Queue entry not found");
                }

                if (string.IsNullOrEmpty(queueEntry.Song?.YouTubeUrl))
                {
                    _logger.LogWarning("No YouTube URL for QueueId {QueueId} in EventId {EventId}", queueId, eventId);
                    return BadRequest("No YouTube URL available");
                }

                queueEntry.IsCurrentlyPlaying = true;
                queueEntry.Status = "Live";
                queueEntry.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group($"Event_{eventId}").SendAsync("QueuePlaying", queueId, eventId, queueEntry.Song.YouTubeUrl);

                _logger.LogInformation("Launched video for QueueId {QueueId} in EventId {EventId}", queueId, eventId);
                return Ok(new { youTubeUrl = queueEntry.Song.YouTubeUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error launching video for QueueId {QueueId} in EventId {EventId}", queueId, eventId);
                return StatusCode(500, new { message = "Error launching video", details = ex.Message });
            }
        }

        private string ComputeSongStatus(EventQueue queueEntry, bool anySingerOnBreak)
        {
            if (queueEntry.WasSkipped)
                return "Skipped";
            if (queueEntry.IsCurrentlyPlaying)
                return "Playing";
            if (queueEntry.SungAt != null)
                return "Sung";
            if (anySingerOnBreak || queueEntry.IsOnBreak)
                return "OnBreak";
            return queueEntry.Status;
        }
    }
}