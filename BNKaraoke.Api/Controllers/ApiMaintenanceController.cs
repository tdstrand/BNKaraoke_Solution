using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using BNKaraoke.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BNKaraoke.Api.Controllers
{
    [ApiController]
    [Route("api/maintenance")]
    [Authorize(Policy = "ApplicationManager")]
    public class ApiMaintenanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ISongCacheService _cacheService;
        private readonly IServiceScopeFactory _scopeFactory;
        private static CancellationTokenSource? _manualCts;
        private static Task? _manualTask;
        private static int _manualTotal;
        private static int _manualProcessed;

        public ApiMaintenanceController(ApplicationDbContext context, ISongCacheService cacheService, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _cacheService = cacheService;
            _scopeFactory = scopeFactory;
        }

        [HttpGet("settings")]
        public async Task<ActionResult<IEnumerable<ApiSettings>>> GetSettings()
        {
            return await _context.ApiSettings.AsNoTracking().ToListAsync();
        }

        [HttpPost("settings")]
        public async Task<IActionResult> AddSetting([FromBody] ApiSettings setting)
        {
            _context.ApiSettings.Add(setting);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetSettings), new { id = setting.Id }, setting);
        }

        [HttpPut("settings/{id}")]
        public async Task<IActionResult> UpdateSetting(int id, [FromBody] ApiSettings setting)
        {
            if (id != setting.Id) return BadRequest();
            var existing = await _context.ApiSettings.FindAsync(id);
            if (existing == null) return NotFound();
            existing.SettingKey = setting.SettingKey;
            existing.SettingValue = setting.SettingValue;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("settings/{id}")]
        public async Task<IActionResult> DeleteSetting(int id)
        {
            var existing = await _context.ApiSettings.FindAsync(id);
            if (existing == null) return NotFound();
            _context.ApiSettings.Remove(existing);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("resync-cache")]
        public async Task<IActionResult> ResyncCache()
        {
            var path = await _context.ApiSettings.Where(s => s.SettingKey == "CacheStoragePath").Select(s => s.SettingValue).FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(path)) return BadRequest(new { error = "CacheStoragePath not configured" });
            int updated = 0;
            foreach (var song in _context.Songs)
            {
                bool exists = System.IO.File.Exists(System.IO.Path.Combine(path, $"{song.Id}.mp4"));
                if (song.Cached != exists)
                {
                    song.Cached = exists;
                    updated++;
                }
            }
            await _context.SaveChangesAsync();
            return Ok(new { updated });
        }

        [HttpPost("manual-cache/start")]
        public IActionResult StartManualCache()
        {
            if (_manualTask != null && !_manualTask.IsCompleted)
            {
                return BadRequest(new { error = "Manual caching already running" });
            }
            _manualCts = new CancellationTokenSource();
            var token = _manualCts.Token;
            _manualTask = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var songs = await ctx.Songs.Where(s => !s.Cached && !string.IsNullOrEmpty(s.YouTubeUrl)).ToListAsync(token);
                _manualTotal = songs.Count;
                _manualProcessed = 0;
                foreach (var song in songs)
                {
                    if (token.IsCancellationRequested) break;
                    var cached = await _cacheService.CacheSongAsync(song.Id, song.YouTubeUrl!, token);
                    if (cached)
                    {
                        song.Cached = true;
                    }
                    _manualProcessed++;
                    await ctx.SaveChangesAsync(token);
                }
            }, token);
            return Ok(new { started = true });
        }

        [HttpPost("manual-cache/stop")]
        public IActionResult StopManualCache()
        {
            if (_manualTask == null || _manualTask.IsCompleted)
            {
                return BadRequest(new { error = "Manual caching not running" });
            }
            _manualCts?.Cancel();
            return Ok(new { stopping = true });
        }

        [HttpGet("manual-cache/status")]
        public IActionResult ManualCacheStatus()
        {
            bool running = _manualTask != null && !_manualTask.IsCompleted;
            double percent = _manualTotal == 0 ? 0 : (_manualProcessed * 100.0 / _manualTotal);
            return Ok(new { running, processed = _manualProcessed, total = _manualTotal, percent });
        }
    }
}
