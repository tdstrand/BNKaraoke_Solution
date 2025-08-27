using BNKaraoke.Api.Data;
using BNKaraoke.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BNKaraoke.Api.Controllers
{
    [ApiController]
    [Route("api/cache")]
    [Authorize(Policy = "KaraokeDJ")]
    public class CacheController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ISongCacheService _songCacheService;

        public CacheController(ApplicationDbContext context, ISongCacheService songCacheService)
        {
            _context = context;
            _songCacheService = songCacheService;
        }

        [HttpGet("manifest")]
        public async Task<IActionResult> GetManifest()
        {
            var songIds = await _context.Songs
                .AsNoTracking()
                .Where(s => s.Cached)
                .Select(s => s.Id)
                .ToListAsync();

            var manifest = new List<object>();

            foreach (var id in songIds)
            {
                var info = await _songCacheService.GetCachedSongFileInfoAsync(id);
                if (info != null)
                {
                    manifest.Add(new
                    {
                        songId = id,
                        fileSize = info.Length,
                        lastModified = info.LastWriteTimeUtc
                    });
                }
            }

            return Ok(manifest);
        }

        [HttpGet("{songId}")]
        public async Task<IActionResult> GetCachedSong(int songId)
        {
            var stream = await _songCacheService.OpenCachedSongStreamAsync(songId);
            if (stream == null)
            {
                return NotFound();
            }

            return File(stream, "video/mp4", enableRangeProcessing: true);
        }
    }
}

