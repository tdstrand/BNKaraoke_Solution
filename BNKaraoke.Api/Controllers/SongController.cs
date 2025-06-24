using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Globalization;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;

namespace BNKaraoke.Api.Controllers
{
    [Route("api/songs")]
    [ApiController]
    public class SongController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SongController> _logger;

        public SongController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<SongController> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("{songId}")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> GetSongById(int songId)
        {
            _logger.LogInformation("Fetching song with SongId: {SongId}", songId);
            try
            {
                var song = await _context.Songs.FindAsync(songId);
                if (song == null)
                {
                    _logger.LogWarning("Song not found with SongId: {SongId}", songId);
                    return NotFound("Song not found");
                }
                _logger.LogInformation("Successfully fetched song with SongId: {SongId}", songId);
                return Ok(song);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching song with SongId {SongId}: {Message}", songId, ex.Message);
                return StatusCode(500, new { message = "An error occurred while fetching the song", details = ex.Message });
            }
        }

        [HttpGet("users")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> GetUsers()
        {
            _logger.LogInformation("Fetching list of users");
            try
            {
                var users = await _context.Users
                    .Select(u => new
                    {
                        Id = u.Id,
                        UserName = u.UserName,
                        FirstName = u.FirstName,
                        LastName = u.LastName
                    })
                    .OrderBy(u => u.FirstName)
                    .ThenBy(u => u.LastName)
                    .ToListAsync();
                _logger.LogInformation("Successfully fetched {Count} users", users.Count);
                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users: {Message}", ex.Message);
                return StatusCode(500, new { message = "An error occurred while fetching users", details = ex.Message });
            }
        }

        [HttpGet("search")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> Search(
            string? query = "",
            string? artist = "",
            string? decade = "",
            string? genre = "",
            string? popularity = "",
            string? requestedBy = "",
            int page = 1,
            int pageSize = 50)
        {
            _logger.LogInformation("Search: Query={Query}, Artist={Artist}, Decade={Decade}, Genre={Genre}, Popularity={Popularity}, RequestedBy={RequestedBy}, Page={Page}, PageSize={PageSize}",
                query, artist, decade, genre, popularity, requestedBy, page, pageSize);
            if (pageSize > 100)
            {
                _logger.LogWarning("Search: PageSize {PageSize} exceeds maximum limit of 100", pageSize);
                return BadRequest(new { error = "PageSize cannot exceed 100" });
            }
            var songsQuery = _context.Songs.Where(s => s.Status == "active");
            _logger.LogDebug("Initial song count after status filter: {Count}", await songsQuery.CountAsync());
            if (!string.IsNullOrEmpty(artist))
            {
                songsQuery = songsQuery.Where(s => EF.Functions.ILike(s.Artist, artist));
                _logger.LogDebug("Song count after artist filter ({Artist}): {Count}", artist, await songsQuery.CountAsync());
            }
            else if (!string.IsNullOrEmpty(query) && query.ToLower() != "all")
            {
                songsQuery = songsQuery.Where(s => EF.Functions.ILike(s.Title, $"%{query}%") ||
                                                  EF.Functions.ILike(s.Artist, $"%{query}%"));
                _logger.LogDebug("Song count after query filter ({Query}): {Count}", query, await songsQuery.CountAsync());
            }
            if (!string.IsNullOrEmpty(decade))
            {
                songsQuery = songsQuery.Where(s => s.Decade != null && EF.Functions.ILike(s.Decade, decade));
                _logger.LogDebug("Song count after decade filter ({Decade}): {Count}", decade, await songsQuery.CountAsync());
            }
            if (!string.IsNullOrEmpty(genre))
            {
                songsQuery = songsQuery.Where(s => s.Genre != null && EF.Functions.ILike(s.Genre, genre));
                _logger.LogDebug("Song count after genre filter ({Genre}): {Count}", genre, await songsQuery.CountAsync());
            }
            if (!string.IsNullOrEmpty(popularity) && popularity != "popularity")
            {
                var range = popularity.Split('=');
                if (range.Length == 2)
                {
                    var bounds = range[1].Split('-');
                    if (bounds.Length == 2 && int.TryParse(bounds[0], out int min) && int.TryParse(bounds[1], out int max))
                    {
                        songsQuery = songsQuery.Where(s => s.Popularity.HasValue && s.Popularity.Value >= min && s.Popularity.Value <= max);
                        _logger.LogDebug("Song count after popularity filter ({Min}-{Max}): {Count}", min, max, await songsQuery.CountAsync());
                    }
                }
            }
            if (!string.IsNullOrEmpty(requestedBy))
            {
                songsQuery = songsQuery.Where(s => s.RequestedBy != null && EF.Functions.ILike(s.RequestedBy, requestedBy));
                _logger.LogDebug("Song count after requestedBy filter ({RequestedBy}): {Count}", requestedBy, await songsQuery.CountAsync());
            }
            if (popularity == "popularity")
            {
                songsQuery = songsQuery.OrderByDescending(s => s.Popularity.GetValueOrDefault(0));
            }
            else
            {
                songsQuery = songsQuery.OrderBy(s => s.Title);
            }
            var totalSongs = await songsQuery.CountAsync();
            var songs = await songsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.Title,
                    s.Artist,
                    s.Status,
                    s.Bpm,
                    s.Danceability,
                    s.Energy,
                    s.Mood,
                    s.Popularity,
                    s.Genre,
                    s.Decade,
                    s.RequestDate,
                    s.RequestedBy,
                    s.SpotifyId,
                    s.YouTubeUrl,
                    s.MusicBrainzId,
                    s.LastFmPlaycount,
                    s.Valence
                })
                .ToListAsync();
            _logger.LogInformation("Search: Found {TotalSongs} songs, returning {SongCount} for page {Page}", totalSongs, songs.Count, page);
            return Ok(new
            {
                totalSongs,
                songs,
                currentPage = page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalSongs / (double)pageSize)
            });
        }

        [HttpGet("artists")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> GetArtists()
        {
            _logger.LogInformation("GetArtists: Received request to fetch unique artists");
            try
            {
                _logger.LogDebug("GetArtists: Verifying database context");
                if (_context == null)
                {
                    _logger.LogError("GetArtists: Database context is null");
                    return StatusCode(500, new { error = "Database context is not initialized" });
                }
                _logger.LogDebug("GetArtists: Querying Songs table for active songs");
                var songsQuery = _context.Songs.Where(s => s.Status == "active");
                _logger.LogDebug("GetArtists: Selecting distinct artists");
                var artists = await songsQuery
                    .Select(s => s.Artist)
                    .Distinct()
                    .OrderBy(a => a)
                    .ToListAsync();
                _logger.LogInformation("GetArtists: Successfully fetched {ArtistCount} unique artists", artists.Count);
                return Ok(artists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetArtists: Exception occurred while fetching artists");
                return StatusCode(500, new { error = "Failed to retrieve artists: " + ex.Message });
            }
        }

        [HttpGet("genres")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> GetGenres()
        {
            _logger.LogInformation("GetGenres: Received request to fetch unique genres");
            try
            {
                _logger.LogDebug("GetGenres: Verifying database context");
                if (_context == null)
                {
                    _logger.LogError("GetGenres: Database context is null");
                    return StatusCode(500, new { error = "Database context is not initialized" });
                }
                _logger.LogDebug("GetGenres: Querying Songs table for active songs with non-null genres");
                var songsQuery = _context.Songs.Where(s => s.Status == "active" && s.Genre != null);
                _logger.LogDebug("GetGenres: Selecting distinct genres");
                var genres = await songsQuery
                    .Select(s => s.Genre)
                    .Distinct()
                    .OrderBy(g => g)
                    .ToListAsync();
                _logger.LogInformation("GetGenres: Successfully fetched {GenreCount} unique genres", genres.Count);
                return Ok(genres);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetGenres: Exception occurred while fetching genres");
                return StatusCode(500, new { error = "Failed to retrieve genres: " + ex.Message });
            }
        }

        [HttpGet("user-requests")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> GetUserRequests()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _logger.LogInformation("GetUserRequests: UserId={UserId}", userId);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("GetUserRequests: User identity not found in token");
                return BadRequest(new { error = "User identity not found in token" });
            }
            var songs = await _context.Songs
                .Where(s => s.RequestedBy == userId && s.Status == "pending")
                .ToListAsync();
            _logger.LogInformation("GetUserRequests: Found {SongCount} pending songs for UserId={UserId}", songs.Count, userId);
            return Ok(songs);
        }

        [HttpGet("pending")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> GetPending()
        {
            _logger.LogInformation("GetPending: Querying database for pending songs");
            try
            {
                var songs = await _context.Songs
                    .Where(s => s.Status == "pending")
                    .Join(
                        _context.Users,
                        song => song.RequestedBy,
                        user => user.UserName,
                        (song, user) => new
                        {
                            song.Id,
                            song.Title,
                            song.Artist,
                            song.Genre,
                            song.Status,
                            song.RequestDate,
                            song.SpotifyId,
                            FirstName = user.FirstName,
                            LastName = user.LastName
                        }
                    )
                    .OrderBy(s => s.RequestDate)
                    .ToListAsync();
                _logger.LogInformation("GetPending: Found {SongCount} pending songs", songs.Count);
                return Ok(songs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPending: Exception occurred");
                return StatusCode(500, new { error = "Failed to retrieve pending songs" });
            }
        }

        [HttpGet("manage")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> GetManageableSongs(string? query = "", string? artist = "", string? status = "", int page = 1, int pageSize = 50)
        {
            _logger.LogInformation("GetManageableSongs: Query={Query}, Artist={Artist}, Status={Status}, Page={Page}, PageSize={PageSize}",
                query, artist, status, page, pageSize);
            try
            {
                if (pageSize > 100)
                {
                    _logger.LogWarning("GetManageableSongs: PageSize {PageSize} exceeds maximum limit of 100", pageSize);
                    return BadRequest(new { error = "PageSize cannot exceed 100" });
                }
                var songsQuery = _context.Songs
                    .Where(s => s.Status == "active" || s.Status == "pending");
                if (!string.IsNullOrEmpty(query))
                {
                    songsQuery = songsQuery.Where(s => EF.Functions.ILike(s.Title, $"%{query}%") ||
                                                      EF.Functions.ILike(s.Artist, $"%{query}%"));
                }
                if (!string.IsNullOrEmpty(artist))
                {
                    songsQuery = songsQuery.Where(s => EF.Functions.ILike(s.Artist, $"%{artist}%"));
                }
                if (!string.IsNullOrEmpty(status))
                {
                    songsQuery = songsQuery.Where(s => s.Status == status);
                }
                var totalSongs = await songsQuery.CountAsync();
                var songs = await songsQuery
                    .OrderBy(s => s.Title)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(s => new
                    {
                        s.Id,
                        s.Title,
                        s.Artist,
                        s.Genre,
                        s.Status,
                        s.Bpm,
                        s.Danceability,
                        s.Energy,
                        s.Mood,
                        s.Popularity,
                        s.Decade,
                        s.SpotifyId,
                        s.YouTubeUrl,
                        s.MusicBrainzId,
                        s.LastFmPlaycount,
                        s.Valence
                    })
                    .ToListAsync();
                _logger.LogInformation("GetManageableSongs: Found {TotalSongs} songs, returning {SongCount} for page {Page}", totalSongs, songs.Count, page);
                return Ok(new
                {
                    totalSongs,
                    songs,
                    currentPage = page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(totalSongs / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetManageableSongs: Exception occurred");
                return StatusCode(500, new { error = "Failed to retrieve manageable songs" });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> UpdateSong(int id, [FromBody] SongUpdateRequest request)
        {
            _logger.LogInformation("UpdateSong: Updating song with Id {SongId}", id);
            try
            {
                if (id != request.Id)
                {
                    _logger.LogWarning("UpdateSong: Id mismatch between route {RouteId} and body {BodyId}", id, request.Id);
                    return BadRequest(new { error = "Id mismatch" });
                }
                var song = await _context.Songs.FindAsync(id);
                if (song == null)
                {
                    _logger.LogWarning("UpdateSong: Song not found with Id {SongId}", id);
                    return NotFound(new { error = "Song not found" });
                }
                song.Title = request.Title ?? song.Title;
                song.Artist = request.Artist ?? song.Artist;
                song.Genre = request.Genre;
                song.Decade = request.Decade;
                song.Bpm = request.Bpm;
                song.Danceability = request.Danceability;
                song.Energy = request.Energy;
                song.Mood = request.Mood;
                song.Popularity = request.Popularity;
                song.SpotifyId = request.SpotifyId;
                song.YouTubeUrl = request.YouTubeUrl;
                song.Status = request.Status ?? song.Status;
                song.MusicBrainzId = request.MusicBrainzId;
                song.LastFmPlaycount = request.LastFmPlaycount;
                song.Valence = request.Valence;
                await _context.SaveChangesAsync();
                _logger.LogInformation("UpdateSong: Successfully updated song with Id {SongId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateSong: Exception occurred while updating song with Id {SongId}", id);
                return StatusCode(500, new { error = "Failed to update song" });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> DeleteSong(int id)
        {
            _logger.LogInformation("DeleteSong: Deleting song with Id {SongId}", id);
            try
            {
                var song = await _context.Songs.FindAsync(id);
                if (song == null)
                {
                    _logger.LogWarning("DeleteSong: Song not found with Id {SongId}", id);
                    return NotFound(new { error = "Song not found" });
                }
                _context.Songs.Remove(song);
                await _context.SaveChangesAsync();
                _logger.LogInformation("DeleteSong: Successfully deleted song with Id {SongId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteSong: Exception occurred while deleting song with Id {SongId}", id);
                return StatusCode(500, new { error = "Failed to delete song" });
            }
        }

        [HttpGet("youtube-search")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> YouTubeSearch(string query)
        {
            _logger.LogInformation("YouTubeSearch: Query={Query}", query);
            try
            {
                var client = _httpClientFactory.CreateClient();
                var apiKey = _configuration["YouTube:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("YouTubeSearch: YouTube API key is missing in configuration");
                    return BadRequest(new { error = "YouTube API key is missing" });
                }
                var searchResponse = await client.GetAsync(
                    $"https://www.googleapis.com/youtube/v3/search?part=snippet&q={Uri.EscapeDataString(query)}&type=video&key={apiKey}&maxResults=10"
                );
                _logger.LogInformation("YouTubeSearch: Search status for query '{Query}': {StatusCode}", query, searchResponse.StatusCode);
                if (!searchResponse.IsSuccessStatusCode)
                {
                    var errorText = await searchResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("YouTubeSearch: Search error response: {ErrorText}", errorText);
                    return BadRequest(new { error = $"YouTube search failed: {errorText}" });
                }
                var searchJson = await searchResponse.Content.ReadAsStringAsync();
                var searchData = JsonSerializer.Deserialize<YouTubeSearchResponse>(searchJson);
                if (searchData == null || searchData.Items == null || !searchData.Items.Any())
                {
                    _logger.LogWarning("YouTubeSearch: No valid response found for query '{Query}'", query);
                    return Ok(new List<object>());
                }
                var videoIds = string.Join(",", searchData.Items
                    .Where(v => v.Id?.VideoId != null)
                    .Select(v => v.Id!.VideoId!));
                if (string.IsNullOrEmpty(videoIds))
                {
                    _logger.LogWarning("YouTubeSearch: No valid video IDs found for query '{Query}'", query);
                    return Ok(new List<object>());
                }
                var videosResponse = await client.GetAsync(
                    $"https://www.googleapis.com/youtube/v3/videos?part=contentDetails,statistics&id={videoIds}&key={apiKey}"
                );
                _logger.LogInformation("YouTubeSearch: Videos details status for query '{Query}': {StatusCode}", query, videosResponse.StatusCode);
                if (!videosResponse.IsSuccessStatusCode)
                {
                    var errorText = await videosResponse.Content.ReadAsStringAsync();
                    _logger.LogWarning("YouTubeSearch: Videos details error response: {ErrorText}", errorText);
                    return BadRequest(new { error = $"YouTube video details fetch failed: {errorText}" });
                }
                var videosJson = await videosResponse.Content.ReadAsStringAsync();
                _logger.LogDebug("YouTubeSearch: Raw videos JSON for query '{Query}': {VideosJson}", query, videosJson);
                var videosData = JsonSerializer.Deserialize<YouTubeVideosResponse>(videosJson);
                var videos = searchData.Items
                    .Where(v => v.Id?.VideoId != null && v.Snippet != null)
                    .Select(v =>
                    {
                        var videoDetails = videosData?.Items?.FirstOrDefault(vd => vd.Id == v.Id!.VideoId);
                        long viewCount = 0;
                        if (videoDetails?.Statistics?.ViewCount != null && long.TryParse(videoDetails.Statistics.ViewCount, out var parsedViewCount))
                        {
                            viewCount = parsedViewCount;
                        }
                        return new
                        {
                            videoId = v.Id!.VideoId,
                            title = v.Snippet!.Title ?? "Untitled",
                            url = $"https://www.youtube.com/watch?v={v.Id.VideoId}",
                            channelTitle = v.Snippet!.ChannelTitle ?? "Unknown",
                            duration = videoDetails?.ContentDetails?.Duration ?? "PT0S",
                            uploadDate = v.Snippet!.PublishedAt?.ToString("o") ?? DateTime.UtcNow.ToString("o"),
                            viewCount
                        };
                    })
                    .ToList();
                _logger.LogInformation("YouTubeSearch: Found {VideoCount} videos for query '{Query}'", videos.Count, query);
                return Ok(videos);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "YouTubeSearch: JSON deserialization error for query '{Query}': {Message}", query, ex.Message);
                return StatusCode(500, new { error = "Failed to parse YouTube response", details = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "YouTubeSearch: Exception for query '{Query}': {Message}", query, ex.Message);
                return StatusCode(500, new { error = "Failed to search YouTube", details = ex.Message });
            }
        }

        [HttpGet("spotify-search")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> SpotifySearch(string query)
        {
            _logger.LogInformation("SpotifySearch: Query={Query}", query);
            try
            {
                if (string.IsNullOrEmpty(query))
                {
                    _logger.LogWarning("SpotifySearch: Query parameter is missing");
                    return BadRequest(new { error = "Query parameter is required" });
                }
                var client = _httpClientFactory.CreateClient();
                var token = await GetSpotifyToken(client);
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("SpotifySearch: Failed to retrieve Spotify token");
                    return StatusCode(500, new { error = "Failed to retrieve Spotify token" });
                }
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                var response = await client.GetAsync($"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit=10");
                _logger.LogInformation("SpotifySearch: Search status: {StatusCode}", response.StatusCode);
                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("SpotifySearch: Search error response: {ErrorText}", errorText);
                    return BadRequest(new { error = $"Spotify search failed: {errorText}" });
                }
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<SpotifySearchResponse>(json);
                if (data?.Tracks == null)
                {
                    _logger.LogWarning("SpotifySearch: Tracks object is null for query '{Query}'", query);
                    return BadRequest(new { error = "No tracks found in Spotify response" });
                }
                var songs = new List<object>();
                foreach (var track in data.Tracks.Items)
                {
                    var song = new
                    {
                        id = track.Id,
                        title = track.Name,
                        artist = track.Artists != null ? string.Join(", ", track.Artists.Select(a => a.Name ?? "Unknown")) : "Unknown",
                        popularity = track.Popularity,
                        genre = "Unknown",
                        bpm = (float?)0,
                        danceability = (float?)0,
                        energy = (float?)0,
                        valence = (float?)null,
                        decade = "Unknown"
                    };
                    string decade = "Unknown";
                    string genre = "Unknown";
                    var trackResponse = await client.GetAsync($"https://api.spotify.com/v1/tracks/{track.Id}");
                    if (trackResponse.IsSuccessStatusCode)
                    {
                        var trackJson = await trackResponse.Content.ReadAsStringAsync();
                        var trackDetails = JsonSerializer.Deserialize<SpotifyTrackDetails>(trackJson);
                        if (trackDetails != null)
                        {
                            string? releaseDate = trackDetails.ReleaseDate ?? trackDetails.Album?.ReleaseDate;
                            if (!string.IsNullOrEmpty(releaseDate))
                            {
                                var yearStr = releaseDate.Split('-')[0];
                                if (int.TryParse(yearStr, out int year))
                                {
                                    decade = $"{year - (year % 10)}s";
                                }
                            }
                            song = new
                            {
                                id = song.id,
                                title = song.title,
                                artist = song.artist,
                                popularity = trackDetails.Popularity,
                                genre = song.genre,
                                bpm = song.bpm,
                                danceability = song.danceability,
                                energy = song.energy,
                                valence = song.valence,
                                decade = decade
                            };
                            if (trackDetails.Artists != null && trackDetails.Artists.Any())
                            {
                                var artistId = trackDetails.Artists[0].Id;
                                var artistResponse = await client.GetAsync($"https://api.spotify.com/v1/artists/{artistId}");
                                if (artistResponse.IsSuccessStatusCode)
                                {
                                    var artistJson = await artistResponse.Content.ReadAsStringAsync();
                                    var artistDetails = JsonSerializer.Deserialize<SpotifyArtistDetails>(artistJson);
                                    if (artistDetails?.Genres.Any() == true)
                                    {
                                        genre = CapitalizeGenre(artistDetails.Genres.First());
                                    }
                                }
                            }
                            if (genre == "Unknown" && trackDetails.Album?.Id != null)
                            {
                                var albumResponse = await client.GetAsync($"https://api.spotify.com/v1/albums/{trackDetails.Album.Id}");
                                if (albumResponse.IsSuccessStatusCode)
                                {
                                    var albumJson = await albumResponse.Content.ReadAsStringAsync();
                                    var albumDetails = JsonSerializer.Deserialize<SpotifyAlbumDetails>(albumJson);
                                    if (albumDetails?.Artists != null)
                                    {
                                        foreach (var albumArtist in albumDetails.Artists)
                                        {
                                            var artistResponse = await client.GetAsync($"https://api.spotify.com/v1/artists/{albumArtist.Id}");
                                            if (artistResponse.IsSuccessStatusCode)
                                            {
                                                var artistJson = await artistResponse.Content.ReadAsStringAsync();
                                                var artistDetails = JsonSerializer.Deserialize<SpotifyArtistDetails>(artistJson);
                                                if (artistDetails?.Genres.Any() == true)
                                                {
                                                    genre = CapitalizeGenre(artistDetails.Genres.First());
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            song = new
                            {
                                id = song.id,
                                title = song.title,
                                artist = song.artist,
                                popularity = song.popularity,
                                genre = genre,
                                bpm = song.bpm,
                                danceability = song.danceability,
                                energy = song.energy,
                                valence = song.valence,
                                decade = decade
                            };
                        }
                    }
                    songs.Add(song);
                }
                _logger.LogInformation("SpotifySearch: Found {SongCount} songs for query '{Query}'", songs.Count, query);
                return Ok(new { songs });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SpotifySearch: Exception for query '{Query}'", query);
                return StatusCode(500, new { error = "Failed to search Spotify" });
            }
        }

        [HttpPost("approve")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> ApproveSong([FromBody] ApproveSongRequest request)
        {
            _logger.LogInformation("ApproveSong: SongId={SongId}", request.Id);
            try
            {
                var song = await _context.Songs.FindAsync(request.Id);
                if (song == null)
                {
                    _logger.LogWarning("ApproveSong: Song not found - SongId={SongId}", request.Id);
                    return NotFound(new { error = "Song not found" });
                }
                song.YouTubeUrl = request.YouTubeUrl;
                song.Status = "active";
                song.ApprovedBy = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(song.ApprovedBy))
                {
                    _logger.LogWarning("ApproveSong: ApprovedBy is null. Claims: {Claims}", string.Join(", ", User.Claims.Select(c => $"{c.Type}: {c.Value}")));
                }
                await _context.SaveChangesAsync();
                _logger.LogInformation("ApproveSong: Song '{Title}' approved by {ApprovedBy}", song.Title, song.ApprovedBy);
                return Ok(new { message = "Party hit approved!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ApproveSong: Exception for SongId={SongId}", request.Id);
                return StatusCode(500, new { error = "Failed to approve song" });
            }
        }

        [HttpPost("reject")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> RejectSong([FromBody] RejectSongRequest request)
        {
            _logger.LogInformation("RejectSong: SongId={SongId}", request.Id);
            try
            {
                var song = await _context.Songs.FindAsync(request.Id);
                if (song == null)
                {
                    _logger.LogWarning("RejectSong: Song not found - SongId={SongId}", request.Id);
                    return NotFound(new { error = "Song not found" });
                }
                song.Status = "unavailable";
                await _context.SaveChangesAsync();
                _logger.LogInformation("RejectSong: Song '{Title}' rejected", song.Title);
                return Ok(new { message = "Song sidelined for now!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RejectSong: Exception for SongId={SongId}", request.Id);
                return StatusCode(500, new { error = "Failed to reject song" });
            }
        }

        [HttpPost("request")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> RequestSong([FromBody] Song song)
        {
            _logger.LogInformation("RequestSong: Title={Title}, Artist={Artist}", song?.Title, song?.Artist);
            try
            {
                if (song == null || string.IsNullOrEmpty(song.Title) || string.IsNullOrEmpty(song.Artist))
                {
                    _logger.LogWarning("RequestSong: Invalid song data. Title or Artist is missing.");
                    return BadRequest(new { error = "Song data is invalid. Title and Artist are required." });
                }
                song.Status = "pending";
                song.RequestDate = DateTime.UtcNow;
                song.RequestedBy = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(song.RequestedBy))
                {
                    _logger.LogWarning("RequestSong: RequestedBy is null. Claims: {Claims}", string.Join(", ", User.Claims.Select(c => $"{c.Type}: {c.Value}")));
                    return BadRequest(new { error = "User identity not found in token" });
                }
                if (!string.IsNullOrEmpty(song.SpotifyId))
                {
                    var client = _httpClientFactory.CreateClient();
                    var token = await GetSpotifyToken(client);
                    if (string.IsNullOrEmpty(token))
                    {
                        _logger.LogWarning("RequestSong: Failed to retrieve Spotify token for SpotifyId={SpotifyId}", song.SpotifyId);
                        return StatusCode(500, new { error = "Failed to retrieve Spotify token" });
                    }
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                    var trackResponse = await client.GetAsync($"https://api.spotify.com/v1/tracks/{song.SpotifyId}");
                    if (trackResponse.IsSuccessStatusCode)
                    {
                        var trackJson = await trackResponse.Content.ReadAsStringAsync();
                        var trackDetails = JsonSerializer.Deserialize<SpotifyTrackDetails>(trackJson);
                        if (trackDetails != null)
                        {
                            string? releaseDate = trackDetails.ReleaseDate ?? trackDetails.Album?.ReleaseDate;
                            if (!string.IsNullOrEmpty(releaseDate))
                            {
                                var yearStr = releaseDate.Split('-')[0];
                                if (int.TryParse(yearStr, out int year))
                                {
                                    song.Decade = $"{year - (year % 10)}s";
                                }
                            }
                            string genre = "Unknown";
                            if (trackDetails.Artists != null && trackDetails.Artists.Any())
                            {
                                var artistId = trackDetails.Artists[0].Id;
                                var artistResponse = await client.GetAsync($"https://api.spotify.com/v1/artists/{artistId}");
                                if (artistResponse.IsSuccessStatusCode)
                                {
                                    var artistJson = await artistResponse.Content.ReadAsStringAsync();
                                    var artistDetails = JsonSerializer.Deserialize<SpotifyArtistDetails>(artistJson);
                                    if (artistDetails?.Genres.Any() == true)
                                    {
                                        genre = CapitalizeGenre(artistDetails.Genres.First());
                                    }
                                }
                            }
                            if (genre == "Unknown" && trackDetails.Album?.Id != null)
                            {
                                var albumResponse = await client.GetAsync($"https://api.spotify.com/v1/albums/{trackDetails.Album.Id}");
                                if (albumResponse.IsSuccessStatusCode)
                                {
                                    var albumJson = await albumResponse.Content.ReadAsStringAsync();
                                    var albumDetails = JsonSerializer.Deserialize<SpotifyAlbumDetails>(albumJson);
                                    if (albumDetails?.Artists != null)
                                    {
                                        foreach (var albumArtist in albumDetails.Artists)
                                        {
                                            var artistResponse = await client.GetAsync($"https://api.spotify.com/v1/artists/{albumArtist.Id}");
                                            if (artistResponse.IsSuccessStatusCode)
                                            {
                                                var artistJson = await artistResponse.Content.ReadAsStringAsync();
                                                var artistDetails = JsonSerializer.Deserialize<SpotifyArtistDetails>(artistJson);
                                                if (artistDetails?.Genres.Any() == true)
                                                {
                                                    genre = CapitalizeGenre(artistDetails.Genres.First());
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            song.Genre = genre;
                        }
                    }
                }
                _context.Songs.Add(song);
                await _context.SaveChangesAsync();
                _logger.LogInformation("RequestSong: Song '{Title}' added by {RequestedBy}", song.Title, song.RequestedBy);
                return Ok(new { message = "Song added to the party queue!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RequestSong: Exception for Title={Title}", song?.Title ?? "Unknown");
                return StatusCode(500, new { error = "Failed to add song request", details = ex.Message });
            }
        }

        [HttpGet("favorites")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> GetFavorites()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("GetFavorites: User identity not found in token");
                return Unauthorized();
            }
            var favoriteSongIds = await _context.FavoriteSongs
                .Where(fs => fs.SingerId == userId)
                .Select(fs => fs.SongId)
                .ToListAsync();
            var songs = await _context.Songs
                .Where(s => favoriteSongIds.Contains(s.Id))
                .ToListAsync();
            _logger.LogInformation("GetFavorites: Found {SongCount} favorite songs for UserId={UserId}", songs.Count, userId);
            return Ok(songs);
        }

        [HttpPost("favorites")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> AddFavorite([FromBody] AddFavoriteRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("AddFavorite: User identity not found in token");
                return Unauthorized();
            }
            var song = await _context.Songs.FindAsync(request.SongId);
            if (song == null)
            {
                _logger.LogWarning("AddFavorite: Song not found - SongId={SongId}", request.SongId);
                return NotFound("Song not found");
            }
            var existingFavorite = await _context.FavoriteSongs
                .FirstOrDefaultAsync(fs => fs.SingerId == userId && fs.SongId == request.SongId);
            if (existingFavorite != null)
            {
                _logger.LogWarning("AddFavorite: Song already in favorites - SongId={SongId}, UserId={UserId}", request.SongId, userId);
                return BadRequest("Song already in favorites");
            }
            var favorite = new FavoriteSong
            {
                SingerId = userId,
                SongId = request.SongId
            };
            _context.FavoriteSongs.Add(favorite);
            await _context.SaveChangesAsync();
            _logger.LogInformation("AddFavorite: Added song to favorites - SongId={SongId}, UserId={UserId}", request.SongId, userId);
            return Ok(new { success = true });
        }

        [HttpDelete("favorites/{songId}")]
        [Authorize(Policy = "Singer")]
        public async Task<IActionResult> RemoveFavorite(int songId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("RemoveFavorite: User identity not found in token");
                return Unauthorized();
            }
            var favorite = await _context.FavoriteSongs
                .FirstOrDefaultAsync(fs => fs.SingerId == userId && fs.SongId == songId);
            if (favorite == null)
            {
                _logger.LogWarning("RemoveFavorite: Favorite not found - SongId={SongId}, UserId={UserId}", songId, userId);
                return NotFound("Favorite not found");
            }
            _context.FavoriteSongs.Remove(favorite);
            await _context.SaveChangesAsync();
            _logger.LogInformation("RemoveFavorite: Removed song from favorites - SongId={SongId}, UserId={UserId}", songId, userId);
            return Ok(new { success = true });
        }

        private async Task<string> GetSpotifyToken(HttpClient client)
        {
            _logger.LogInformation("GetSpotifyToken: Requesting token");
            try
            {
                var clientId = _configuration["Spotify:ClientId"];
                var clientSecret = _configuration["Spotify:ClientSecret"];
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    _logger.LogError("GetSpotifyToken: Spotify ClientId or ClientSecret is missing in configuration");
                    throw new InvalidOperationException("Spotify ClientId or ClientSecret is missing.");
                }
                var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "grant_type", "client_credentials" },
                        { "client_id", clientId },
                        { "client_secret", clientSecret }
                    })
                };
                var response = await client.SendAsync(request);
                _logger.LogInformation("GetSpotifyToken: Response status: {StatusCode}", response.StatusCode);
                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("GetSpotifyToken: Error response: {ErrorText}", errorText);
                    throw new InvalidOperationException($"Failed to get Spotify token: {errorText}");
                }
                var json = await response.Content.ReadAsStringAsync();
                var tokenData = JsonSerializer.Deserialize<SpotifyTokenResponse>(json);
                if (string.IsNullOrEmpty(tokenData?.AccessToken))
                {
                    _logger.LogError("GetSpotifyToken: Spotify token response missing access_token");
                    throw new InvalidOperationException("Spotify token response missing access_token");
                }
                _logger.LogInformation("GetSpotifyToken: Token retrieved successfully");
                return tokenData.AccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSpotifyToken: Exception occurred");
                throw;
            }
        }

        private string CapitalizeGenre(string genre)
        {
            if (string.IsNullOrEmpty(genre) || genre == "Unknown")
            {
                return genre;
            }
            TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
            return textInfo.ToTitleCase(genre.ToLower());
        }

        public class SpotifySearchResponse
        {
            [JsonPropertyName("tracks")]
            public required SpotifyTracks Tracks { get; set; }
        }

        public class SpotifyTracks
        {
            [JsonPropertyName("items")]
            public required List<SpotifyTrack> Items { get; set; }
        }

        public class SpotifyTrack
        {
            [JsonPropertyName("id")]
            public required string Id { get; set; }
            [JsonPropertyName("name")]
            public required string Name { get; set; }
            [JsonPropertyName("artists")]
            public required List<SpotifyArtist> Artists { get; set; }
            [JsonPropertyName("album")]
            public SpotifyAlbum? Album { get; set; }
            [JsonPropertyName("popularity")]
            public int Popularity { get; set; }
        }

        public class SpotifyArtist
        {
            [JsonPropertyName("id")]
            public required string Id { get; set; }
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        public class SpotifyTokenResponse
        {
            [JsonPropertyName("access_token")]
            public required string AccessToken { get; set; }
        }

        public class SpotifyTrackDetails
        {
            [JsonPropertyName("popularity")]
            public int Popularity { get; set; }
            [JsonPropertyName("release_date")]
            public string? ReleaseDate { get; set; }
            [JsonPropertyName("album")]
            public SpotifyAlbum? Album { get; set; }
            [JsonPropertyName("artists")]
            public List<SpotifyArtist>? Artists { get; set; }
        }

        public class SpotifyAlbum
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }
            [JsonPropertyName("release_date")]
            public string? ReleaseDate { get; set; }
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        public class SpotifyAlbumDetails
        {
            [JsonPropertyName("artists")]
            public List<SpotifyArtist>? Artists { get; set; }
        }

        public class SpotifyArtistDetails
        {
            [JsonPropertyName("genres")]
            public required List<string> Genres { get; set; }
        }

        public class YouTubeSearchResponse
        {
            [JsonPropertyName("items")]
            public required List<YouTubeItem> Items { get; set; }
        }

        public class YouTubeVideosResponse
        {
            [JsonPropertyName("items")]
            public required List<YouTubeVideoItem> Items { get; set; }
        }

        public class YouTubeItem
        {
            [JsonPropertyName("id")]
            public YouTubeId? Id { get; set; }
            [JsonPropertyName("snippet")]
            public YouTubeSnippet? Snippet { get; set; }
        }

        public class YouTubeVideoItem
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }
            [JsonPropertyName("contentDetails")]
            public YouTubeContentDetails? ContentDetails { get; set; }
            [JsonPropertyName("statistics")]
            public YouTubeStatistics? Statistics { get; set; }
        }

        public class YouTubeId
        {
            [JsonPropertyName("videoId")]
            public string? VideoId { get; set; }
        }

        public class YouTubeSnippet
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }
            [JsonPropertyName("channelTitle")]
            public string? ChannelTitle { get; set; }
            [JsonPropertyName("publishedAt")]
            public DateTime? PublishedAt { get; set; }
        }

        public class YouTubeContentDetails
        {
            [JsonPropertyName("duration")]
            public string? Duration { get; set; }
        }

        public class YouTubeStatistics
        {
            [JsonPropertyName("viewCount")]
            public string? ViewCount { get; set; }
        }

        public class ApproveSongRequest
        {
            public int Id { get; set; }
            public string? YouTubeUrl { get; set; }
        }

        public class RejectSongRequest
        {
            public int Id { get; set; }
        }

        public class AddFavoriteRequest
        {
            public int SongId { get; set; }
        }

        public class SongUpdateRequest
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public string? Artist { get; set; }
            public string? Genre { get; set; }
            public string? Decade { get; set; }
            public float? Bpm { get; set; }
            public float? Danceability { get; set; }
            public float? Energy { get; set; }
            public string? Mood { get; set; }
            public int? Popularity { get; set; }
            public string? SpotifyId { get; set; }
            public string? YouTubeUrl { get; set; }
            public string? Status { get; set; }
            public string? MusicBrainzId { get; set; }
            public int? LastFmPlaycount { get; set; }
            public int? Valence { get; set; }
        }
    }
}