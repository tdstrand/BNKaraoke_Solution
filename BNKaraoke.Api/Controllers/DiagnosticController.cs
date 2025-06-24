using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BNKaraoke.Api.Controllers
{
    [Route("api/diagnostic")]
    [ApiController]
    public class DiagnosticController : ControllerBase
    {
        private readonly ILogger<DiagnosticController> _logger;

        public DiagnosticController(ILogger<DiagnosticController> logger)
        {
            _logger = logger;
            _logger.LogInformation("DiagnosticController instantiated");
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            _logger.LogInformation("Diagnostic test endpoint called");
            return Ok(new { message = "Diagnostic endpoint reached" });
        }
    }
}