using BNKaraoke.Api.Hubs;
using BNKaraoke.Api.Models;
using BNKaraoke.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BNKaraoke.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QueueController : ControllerBase
{
    private readonly IQueueService _queueService;
    private readonly IHubContext<KaraokeDJHub> _hubContext;

    public QueueController(IQueueService queueService, IHubContext<KaraokeDJHub> hubContext)
    {
        _queueService = queueService;
        _hubContext = hubContext;
    }

    [HttpPost("reorder-suggestions/{eventId}")]
    public async Task<ActionResult<ReorderSuggestionResponse>> GetReorderSuggestions(string eventId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(eventId, out var parsedEventId))
        {
            return BadRequest(new { message = "Invalid event identifier." });
        }

        var suggestions = await _queueService.GetComplexFairnessSuggestions(parsedEventId, cancellationToken);
        return Ok(suggestions);
    }

    [HttpPost("apply-reorder")]
    public async Task<IActionResult> ApplyReorder([FromBody] ApplyReorderRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var newOrder = await _queueService.ApplyComplexSuggestions(request, cancellationToken);
        await _hubContext.Clients.Group($"Event_{request.EventId}").SendAsync("QueueReordered", newOrder, cancellationToken);
        return Ok(newOrder);
    }
}
