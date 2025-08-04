using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;

namespace BNKaraoke.Api.Controllers
{
    [Authorize]
    [Route("api/songs")]
    [ApiController]
    public class ExploreController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ExploreController> _logger;

        public ExploreController(ApplicationDbContext context, ILogger<ExploreController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("explore")]
        public async Task<IActionResult> ExploreSongs(
            [FromQuery] string? status = null,
            [FromQuery] string? artist = null,
            [FromQuery] string? decade = null,
            [FromQuery] string? genre = null,
            [FromQuery] string? popularity = null,
            [FromQuery] string? requestedBy = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            _logger.LogInformation("ExploreSongs: status={status}, artist={artist}, decade={decade}, genre={genre}, popularity={popularity}, requestedBy={requestedBy}, page={page}, pageSize={pageSize}",
                status, artist, decade, genre, popularity, requestedBy, page, pageSize);
            try
            {
                if (status != null && !new[] { "active", "pending", "unavailable" }.Contains(status.ToLower()))
                {
                    return BadRequest(new { error = "Invalid status value. Must be 'active', 'pending', or 'unavailable'." });
                }
                if (popularity != null && !new[] { "veryPopular", "popular", "moderate", "lessPopular" }.Contains(popularity))
                {
                    return BadRequest(new { error = "Invalid popularity value. Must be 'veryPopular', 'popular', 'moderate', or 'lessPopular'." });
                }
                if (page < 1)
                {
                    return BadRequest(new { error = "Page must be at least 1." });
                }
                if (pageSize < 1 || pageSize > 100)
                {
                    return BadRequest(new { error = "PageSize must be between 1 and 100." });
                }

                var query = _context.Songs.AsNoTracking();
                if (status != null)
                {
                    query = query.Where(s => !string.IsNullOrEmpty(s.Status) && EF.Functions.ILike(s.Status, status));
                }
                if (artist != null)
                {
                    query = query.Where(s => s.Artist == artist);
                }
                if (decade != null)
                {
                    query = query.Where(s => s.Decade == decade);
                }
                if (genre != null)
                {
                    query = query.Where(s => !string.IsNullOrEmpty(s.Genre) && EF.Functions.ILike(s.Genre, genre));
                }
                if (requestedBy != null)
                {
                    query = query.Where(s => s.RequestedBy == requestedBy);
                }
                if (popularity != null)
                {
                    switch (popularity)
                    {
                        case "veryPopular":
                            query = query.Where(s => s.Popularity >= 80);
                            break;
                        case "popular":
                            query = query.Where(s => s.Popularity >= 50 && s.Popularity <= 79);
                            break;
                        case "moderate":
                            query = query.Where(s => s.Popularity >= 20 && s.Popularity <= 49);
                            break;
                        case "lessPopular":
                            query = query.Where(s => s.Popularity >= 0 && s.Popularity <= 19);
                            break;
                    }
                }

                var totalCount = await query.CountAsync();
                var songs = await query
                    .OrderBy(s => s.Title)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(s => new
                    {
                        id = s.Id,
                        title = s.Title,
                        artist = s.Artist,
                        genre = s.Genre,
                        decade = s.Decade,
                        status = s.Status,
                        requestedBy = s.RequestedBy,
                        popularity = s.Popularity,
                        youTubeUrl = s.YouTubeUrl,
                        spotifyId = s.SpotifyId,
                        approvedBy = s.ApprovedBy,
                        bpm = s.Bpm,
                        requestDate = s.RequestDate,
                        musicBrainzId = s.MusicBrainzId,
                        mood = s.Mood,
                        lastFmPlaycount = s.LastFmPlaycount,
                        danceability = s.Danceability,
                        energy = s.Energy,
                        valence = s.Valence
                    })
                    .ToListAsync();

                return Ok(new
                {
                    totalCount,
                    songs,
                    currentPage = page,
                    pageSize,
                    totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExploreSongs: Exception occurred");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }
}