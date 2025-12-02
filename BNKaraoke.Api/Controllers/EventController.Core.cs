using BNKaraoke.Api.Constants;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BNKaraoke.Api.Controllers
{
    [Route("api/events")]
    [ApiController]
    public partial class EventController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EventController> _logger;
        private readonly IHubContext<KaraokeDJHub> _hubContext;

        public EventController(ApplicationDbContext context, ILogger<EventController> logger, IHubContext<KaraokeDJHub> hubContext)
        {
            _context = context;
            _logger = logger;
            _hubContext = hubContext;
            _logger.LogInformation("EventController instantiated");
        }

        private bool UserCanAccessHiddenEvents()
        {
            return RoleConstants.HiddenEventAccessRoles.Any(role => User.IsInRole(role));
        }

        private async Task BroadcastSungCountAsync(int eventId)
        {
            var count = await _context.EventQueues
                .AsNoTracking()
                .CountAsync(eq => eq.EventId == eventId && (eq.SungAt != null || eq.WasSkipped));

            await _hubContext.Clients.Group($"Event_{eventId}")
                .SendAsync("SungCountUpdated", new { eventId, count });
        }

        private async Task BroadcastNowPlayingAsync(int eventId, DJQueueItemDto? queueItem)
        {
            await _hubContext.Clients.Group($"Event_{eventId}")
                .SendAsync("NowPlayingChanged", new { eventId, queueItem });
        }

        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult HealthCheck()
        {
            _logger.LogInformation("Health check endpoint called");
            return Ok(new { status = "healthy", message = "EventController is running" });
        }

        [HttpGet("minimal")]
        [AllowAnonymous]
        public IActionResult MinimalTest()
        {
            _logger.LogInformation("Minimal test endpoint called");
            return Ok(new { message = "Minimal test endpoint reached" });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetEvents()
        {
            try
            {
                _logger.LogInformation("Fetching events for user {UserName}", User.Identity?.Name ?? "Unknown");

                var canAccessHidden = UserCanAccessHiddenEvents();
                var eventsQuery = _context.Events
                    .AsNoTracking()
                    .Where(e => !e.IsCanceled);

                if (!canAccessHidden)
                {
                    eventsQuery = eventsQuery.Where(e => e.Visibility == "Visible");
                }

                if (Request.Query.TryGetValue("status", out var statusFilterRaw))
                {
                    var statusFilter = statusFilterRaw.ToString().Trim().ToLowerInvariant();
                    eventsQuery = statusFilter switch
                    {
                        "live" => eventsQuery.Where(e => e.Status == "Live"),
                        "upcoming" => eventsQuery.Where(e => e.Status == "Upcoming"),
                        "active" => eventsQuery.Where(e => e.Status == "Live" || e.Status == "Upcoming"),
                        "archived" => eventsQuery.Where(e => e.Status == "Archived"),
                        _ => eventsQuery
                    };
                }

                _logger.LogDebug("User {UserName} {VisibilityAccess} hidden events", User.Identity?.Name ?? "Unknown",
                    canAccessHidden ? "can access" : "cannot access");

                var events = await eventsQuery
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
                        QueueCount = e.EventQueues.Count(eq => eq.Status != "Archived" && !eq.WasSkipped && eq.SungAt == null),
                        SongsCompleted = e.SongsCompleted
                    })
                    .OrderBy(e => e.ScheduledDate)
                    .ToListAsync();

                _logger.LogInformation("Fetched {Count} events: {Events}", events.Count, JsonSerializer.Serialize(events));
                return Ok(events);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching events");
                return StatusCode(500, new { message = "Error fetching events", details = ex.Message });
            }
        }

        [HttpGet("{eventId}")]
        [Authorize]
        public async Task<IActionResult> GetEvent(int eventId)
        {
            try
            {
                _logger.LogInformation("Fetching event with EventId: {EventId}", eventId);
                var eventEntity = await _context.Events.FindAsync(eventId);
                if (eventEntity == null)
                {
                    _logger.LogWarning("Event not found with EventId: {EventId}", eventId);
                    return NotFound("Event not found");
                }

                var eventResponse = new EventDto
                {
                    EventId = eventEntity.EventId,
                    EventCode = eventEntity.EventCode,
                    Description = eventEntity.Description,
                    Status = eventEntity.Status,
                    Visibility = eventEntity.Visibility,
                    Location = eventEntity.Location,
                    ScheduledDate = eventEntity.ScheduledDate,
                    ScheduledStartTime = eventEntity.ScheduledStartTime,
                    ScheduledEndTime = eventEntity.ScheduledEndTime,
                    KaraokeDJName = eventEntity.KaraokeDJName,
                    IsCanceled = eventEntity.IsCanceled,
                    RequestLimit = eventEntity.RequestLimit,
                    QueueCount = await _context.EventQueues.CountAsync(eq =>
                        eq.EventId == eventId &&
                        eq.Status != "Archived" &&
                        !eq.WasSkipped &&
                        eq.SungAt == null),
                    SongsCompleted = eventEntity.SongsCompleted
                };

                _logger.LogInformation("Fetched event with EventId: {EventId}: {Event}", eventId, JsonSerializer.Serialize(eventResponse));
                return Ok(eventResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching event with EventId: {EventId}", eventId);
                return StatusCode(500, new { message = "Error fetching event", details = ex.Message });
            }
        }
    }
}
