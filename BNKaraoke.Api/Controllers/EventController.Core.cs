using BNKaraoke.Api.Data;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
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
                _logger.LogInformation("Fetching visible events");
                var events = await _context.Events
                    .Where(e => e.Visibility == "Visible" && !e.IsCanceled)
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

                _logger.LogInformation("Fetched {Count} events", events.Count);
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
                    QueueCount = await _context.EventQueues.CountAsync(eq => eq.EventId == eventId),
                    SongsCompleted = eventEntity.SongsCompleted
                };

                _logger.LogInformation("Fetched event with EventId: {EventId}", eventId);
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